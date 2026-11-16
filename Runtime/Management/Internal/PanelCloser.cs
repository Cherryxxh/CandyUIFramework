using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 面板关闭编排：受理关闭后等退场动画播完，再按 CloseBehavior 入缓存或销毁并释放句柄。
    /// 打开表/缓存表由 PanelService 注入，本类只负责"关闭"这一条链路；
    /// 强制关闭（CloseAll/切场景）走不播动画的同步路径，保证调用方不用等动画。
    /// </summary>
    internal sealed class PanelCloser
    {
        private readonly Dictionary<Type, UIPanelInfo> _dicOpenedPanels;
        private readonly Dictionary<Type, UIPanelInfo> _dicPanelCache;
        private readonly PanelFactory _factory;
        private readonly Func<int> _generationProvider;
        private readonly Func<int> _cacheGenerationProvider;

        internal PanelCloser(
            Dictionary<Type, UIPanelInfo> dicOpenedPanels,
            Dictionary<Type, UIPanelInfo> dicPanelCache,
            PanelFactory factory,
            Func<int> generationProvider,
            Func<int> cacheGenerationProvider)
        {
            _dicOpenedPanels = dicOpenedPanels;
            _dicPanelCache = dicPanelCache;
            _factory = factory;
            _generationProvider = generationProvider;
            _cacheGenerationProvider = cacheGenerationProvider;
        }

        /// <summary>
        /// 受理关闭：被 CanClose 拦截返回 false；受理后立即移出打开表，退场动画播完再销毁或入缓存。
        /// </summary>
        internal bool TryClose(UIPanelInfo info, PanelCloseReason reason)
        {
            if (!CanClose(info, reason))
                return false;

            _dicOpenedPanels.Remove(info.PanelType);
            _ = RunCloseAsync(info, reason, _generationProvider(), _cacheGenerationProvider());
            return true;
        }

        /// <summary>
        /// 受理关闭并等待关闭完成，返回是否关成功（被拦截或关闭异常为 false）。
        /// </summary>
        internal async Task<bool> TryCloseAsync(UIPanelInfo info, PanelCloseReason reason)
        {
            if (!CanClose(info, reason))
                return false;

            _dicOpenedPanels.Remove(info.PanelType);
            try
            {
                await CompleteCloseAsync(info, reason, _generationProvider(), _cacheGenerationProvider());
                return true;
            }
            catch (Exception exception)
            {
                LogFailure(info, exception);
                return false;
            }
        }

        /// <summary>
        /// 强制关闭：不播退场动画，立即销毁实例并释放句柄；不 await 也会同步完成（切场景/清空用）。
        /// </summary>
        internal void CloseImmediate(UIPanelInfo info, PanelCloseReason reason)
        {
            if (!CanClose(info, reason))
                return;

            _dicOpenedPanels.Remove(info.PanelType);

            // animateOut=false 时关闭流程不经过任何 await，调用返回前已经落到 Closed。
            if (info.Panel != null)
                _ = info.Panel.InternalCloseAsync(reason, animateOut: false, destroyInstance: true);

            _factory.Destroy(info);
            CandyUILog.Info($"面板关闭：{info.PanelType.Name}（强制关闭，从打开表移除，销毁实例并释放句柄）");
        }

        /// <summary>后台关闭入口：异常必须在这里收住，不能丢到 async void 上。</summary>
        private async Task RunCloseAsync(UIPanelInfo info, PanelCloseReason reason, int generation, int cacheGeneration)
        {
            try
            {
                await CompleteCloseAsync(info, reason, generation, cacheGeneration);
            }
            catch (Exception exception)
            {
                LogFailure(info, exception);
            }
        }

        /// <summary>
        /// 关闭核心：先等面板退场（KeepInstance 时只休眠不 OnClose），再入缓存或交给工厂销毁。
        /// 缓存前复查"期间是否又打开了同类型实例 / 是否切过场景 / 是否清过缓存"，避免旧实例覆盖新缓存。
        /// </summary>
        private async Task CompleteCloseAsync(
            UIPanelInfo info, PanelCloseReason reason, int generation, int cacheGeneration)
        {
            bool keepInstance = _factory.GetCloseBehavior(info.PanelType) == PanelCloseBehavior.KeepInstance;

            if (info.Panel != null)
                await info.Panel.InternalCloseAsync(reason, animateOut: true, destroyInstance: !keepInstance);

            if (!keepInstance)
            {
                _factory.Destroy(info);
                CandyUILog.Info($"面板关闭：{info.PanelType.Name}（从打开表移除，销毁实例并释放句柄）");
                return;
            }

            // 关闭动画期间缓存被清空过：语义上"缓存已清空"，本次关闭不能再把实例写回缓存。
            bool cacheCleared = cacheGeneration != _cacheGenerationProvider();
            bool cacheInvalid = info.Panel == null
                                || info.GameObject == null
                                || generation != _generationProvider()
                                || _dicOpenedPanels.ContainsKey(info.PanelType);
            if (cacheCleared || cacheInvalid)
            {
                _factory.Destroy(info);
                if (cacheCleared)
                    CandyUILog.Info($"关闭期间面板缓存已被清空，实例 {info.PanelType.Name} 直接销毁");
                else
                    CandyUILog.Warn($"面板关闭时同类型已被重新打开或已切场景，丢弃缓存实例：{info.PanelType.Name}");
                return;
            }

            _dicPanelCache[info.PanelType] = info;
            CandyUILog.Info($"面板关闭并保留实例：{info.PanelType.Name}（缓存实例 {_dicPanelCache.Count}，OnClose 不触发）");
        }

        /// <summary>关闭前置校验：ForceClose 直接放行，UserClose 先问面板与 Presenter。</summary>
        private static bool CanClose(UIPanelInfo info, PanelCloseReason reason)
        {
            if (info == null)
                return false;

            if (info.Panel == null)
                return true;

            if (!info.Panel.InternalCanClose(reason))
            {
                CandyUILog.Info($"关闭被拦截：{info.PanelType.Name}（原因 {reason}，界面还没有准备好关闭）");
                return false;
            }

            return true;
        }

        /// <summary>关闭流程出错：先记一行带面板上下文的警告，再打印异常堆栈方便定位。</summary>
        private static void LogFailure(UIPanelInfo info, Exception exception)
        {
            CandyUILog.Warn($"关闭面板异常：{info?.PanelType.Name}（{exception.GetType().Name}：{exception.Message}）");
            Debug.LogException(exception);
        }
    }
}
