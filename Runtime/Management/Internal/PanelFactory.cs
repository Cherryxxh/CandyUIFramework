using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CandyUI
{
    /// <summary>
    /// 面板工厂（框架内部）：只负责"资源地址 -> 场景里可用的面板实例"这一段。
    ///
    /// 职责：
    /// 1. 取 [UIPanel] 特性，确定 Prefab 地址与默认层；
    /// 2. 通过 AssetCache 加载 Prefab，并校验 generation（加载期间 CloseAll / Reset 则放弃）；
    /// 3. 校验空句柄 / 不是 GameObject / 缺脚本 / 不是 UIPanel 子类，失败路径释放句柄；
    /// 4. 实例化到对应层、铺满、组装 UIPanelInfo、首次 InternalCreate 失败清理、绑定默认转场。
    ///
    /// 不登记面板注册表：登记与 Show 失败回滚留在 PanelService，因为那需要知道打开表/缓存表状态，
    /// 也要先登记再 Show（OnShow 里重入关闭要能在打开表里找到自己）。
    /// 依赖方向：PanelService -> PanelFactory -> AssetCache / UIRoot；工厂持有 PanelService 引用只为读取
    /// 面板归属的 UI 上下文（panel.Framework），不回调登记表。
    /// </summary>
    internal sealed class PanelFactory
    {
        private readonly PanelService _owner;
        private readonly Func<Type, UIPanelAttribute> _attributeProvider;
        private readonly Func<AssetCache> _cacheProvider;
        private readonly Func<int> _generationProvider;
        private readonly Func<IPanelTransition> _defaultTransitionProvider;

        /// <summary>
        /// 注入所属服务与四个只读依赖：特性查询、资源缓存、清理代号与默认转场；工厂自身不持有面板注册表。
        /// </summary>
        internal PanelFactory(
            PanelService owner,
            Func<Type, UIPanelAttribute> attributeProvider,
            Func<AssetCache> cacheProvider,
            Func<int> generationProvider,
            Func<IPanelTransition> defaultTransitionProvider)
        {
            _owner = owner;
            _attributeProvider = attributeProvider;
            _cacheProvider = cacheProvider;
            _generationProvider = generationProvider;
            _defaultTransitionProvider = defaultTransitionProvider;
        }

        /// <summary>
        /// 加载并创建一个面板实例（含 InternalCreate），返回尚未登记注册表的 UIPanelInfo。
        /// 任一失败路径都自行释放句柄/销毁半成品，异常原样上抛。
        /// </summary>
        internal async Task<UIPanelInfo> CreateAsync(
            Type type,
            UILayer? layerOverride,
            CancellationToken cancellationToken)
        {
            UIPanelAttribute attribute = _attributeProvider(type);
            if (attribute == null)
            {
                throw new InvalidOperationException(
                    $"[CandyUI] 面板 {type.Name} 缺少 [UIPanel(\"资源地址\", UILayer.XXX)] 标记，" +
                    "框架不知道要加载哪个 Prefab。");
            }

            UILayer layer = layerOverride ?? attribute.Layer;
            int generation = _generationProvider();
            CandyUILog.Info($"开始打开面板：{type.Name}（地址 {attribute.PrefabAddress}，层 {layer}，缓存策略 {attribute.CachePolicy}）");

            IAssetHandle handle = await _cacheProvider().LoadAsync(
                attribute.PrefabAddress, attribute.CachePolicy, cancellationToken);

            // 加载期间执行了 CloseAll / Reset（例如切了场景）：这个面板已经过期，不要再实例化出来。
            if (generation != _generationProvider())
            {
                if (handle != null)
                    handle.Release();

                throw new OperationCanceledException(
                    $"[CandyUI] 面板打开已取消：{type.Name}（期间执行了 CloseAll / Reset）");
            }

            if (handle == null || handle.Asset == null)
            {
                if (handle != null)
                    handle.Release();

                throw new InvalidOperationException(
                    $"[CandyUI] 加载面板资源失败：{attribute.PrefabAddress}（{type.Name}）");
            }

            GameObject prefab = handle.Asset as GameObject;
            if (prefab == null)
            {
                handle.Release();
                throw new InvalidOperationException(
                    $"[CandyUI] 地址 {attribute.PrefabAddress} 加载到的不是 GameObject，无法作为面板 Prefab。");
            }

            if (prefab.GetComponent(type) == null)
            {
                handle.Release();
                throw new InvalidOperationException(
                    $"[CandyUI] Prefab {attribute.PrefabAddress} 上没有挂 {type.Name} 脚本，无法当成面板使用。");
            }

            // 只查 Prefab 根：根自带 Canvas 的面板就是一个独立 Canvas（嵌套在层容器下），
            // 框架只记录并照常实例化，绝不代加 Canvas，也不改 renderMode/overrideSorting/sortingOrder。
            bool hasOwnCanvas = prefab.GetComponent<Canvas>() != null;

            UIRoot root = UIRoot.EnsureUIRoot();
            RectTransform layerRoot = root.GetLayerRoot(layer);

            // worldPositionStays: false —— 保持 Prefab 里的局部布局，否则会按世界坐标重新计算出现偏移。
            GameObject instance = Object.Instantiate(prefab, layerRoot, false);
            instance.name = type.Name;

            // 面板默认铺满所在层；需要固定尺寸的面板在 OnCreate 里自己改 RectTransform。
            RectTransform rect = instance.GetComponent<RectTransform>();
            Stretch(rect);

            UIPanel panel = instance.GetComponent(type) as UIPanel;
            if (panel == null)
            {
                Object.Destroy(instance);
                handle.Release();
                throw new InvalidOperationException(
                    $"[CandyUI] Prefab {attribute.PrefabAddress} 上的 {type.Name} 不是 UIPanel 子类。");
            }

            UIPanelInfo info = new UIPanelInfo
            {
                PanelType = type,
                Address = attribute.PrefabAddress,
                Layer = layer,
                Panel = panel,
                GameObject = instance,
                Handle = handle,
                HasOwnCanvas = hasOwnCanvas,
            };

            panel.Layer = layer;
            panel.Address = attribute.PrefabAddress;
            // 记录创建它的 UI 上下文：CloseSelf 要路由回这一套（未绑定时留 null，退回默认门面）。
            panel.Framework = _owner.Framework;

            if (hasOwnCanvas)
            {
                CandyUILog.Info(
                    $"面板 {type.Name} 自带 Canvas，作为独立 Canvas 挂在 {layer} 层（框架不修改其排序与缩放）");
            }

            try
            {
                panel.InternalCreate();
            }
            catch
            {
                // OnCreate / OnBind 失败：实例还没登记，直接销毁并释放句柄。
                Object.Destroy(instance);
                handle.Release();
                throw;
            }

            // 先确定转场：面板重写的 CreateTransition 优先，否则用全局默认。
            panel.SetupTransition(_defaultTransitionProvider());

            return info;
        }

        /// <summary>预加载面板 Prefab，但不打开界面。KeepAlive 策略：之后第一次打开不用再等资源读取。</summary>
        internal async Task PreloadAsync<T>(CancellationToken cancellationToken = default) where T : UIPanel
        {
            Type type = typeof(T);
            UIPanelAttribute attribute = _attributeProvider(type);
            if (attribute == null)
            {
                throw new InvalidOperationException(
                    $"[CandyUI] 面板 {type.Name} 缺少 [UIPanel(\"资源地址\", UILayer.XXX)] 标记，无法预加载。");
            }

            IAssetHandle handle = await _cacheProvider().LoadAsync(
                attribute.PrefabAddress, AssetCachePolicy.KeepAlive, cancellationToken);

            // 释放调用方这一次的引用；因为策略是 KeepAlive，缓存仍保留资源。
            handle.Release();
        }

        /// <summary>
        /// 销毁一个面板实例：OnClose -> OnClosed -> Destroy -> 释放句柄。
        /// 这里走不播动画的强制关闭，保证缓存清理/打开失败回滚不依赖帧推进。
        /// </summary>
        internal void Destroy(UIPanelInfo info)
        {
            if (info.Panel != null)
                _ = info.Panel.InternalCloseAsync(PanelCloseReason.ForceClose, animateOut: false, destroyInstance: true);

            if (info.GameObject != null)
                Object.Destroy(info.GameObject);

            // 句柄必须释放：缓存靠这一步减少引用计数，计数归零后（且不是 KeepAlive）才会真正卸载资源。
            if (info.Handle != null)
                info.Handle.Release();
        }

        /// <summary>关闭行为：面板特性声明优先，没声明就销毁。</summary>
        internal PanelCloseBehavior GetCloseBehavior(Type type)
        {
            UIPanelAttribute attribute = _attributeProvider(type);
            return attribute != null ? attribute.CloseBehavior : PanelCloseBehavior.Destroy;
        }

        /// <summary>
        /// 把面板 RectTransform 拉满所在层并复位缩放；需要固定尺寸的面板在 OnCreate 里自行改。
        /// </summary>
        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
