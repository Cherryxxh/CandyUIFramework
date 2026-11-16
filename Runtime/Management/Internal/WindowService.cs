using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 窗口服务（框架内部）：管理"同一时间只显示一个、可以逐级返回"的窗口。
    ///
    /// 窗口和普通面板的唯一区别是语义：
    /// - 打开新窗口时，旧窗口被隐藏（不销毁），压入历史栈；
    /// - 关闭当前窗口时，恢复历史栈里的上一个窗口。
    ///
    /// 它建立在 PanelService 之上：实例化、生命周期、资源加载都交给 PanelService，
    /// 自己只维护一个 WindowStack。这样"窗口"这个概念不会污染面板层。
    ///
    /// 外部不要直接使用本类；统一通过 UIManager 门面调用。
    /// </summary>
    internal sealed class WindowService
    {
        private readonly PanelService _panels;
        private readonly WindowStack _stack = new WindowStack();

        // 最近一次"关窗后恢复上一个窗口"的异步流程；打开新窗口前要先等它结束，避免栈序错乱。
        private Task _pendingClose;

        #region 初始化

        /// <summary>
        /// 持有 PanelService 承担窗口的实例化与生命周期；窗口栈状态由本类维护。
        /// </summary>
        internal WindowService(PanelService panels)
        {
            _panels = panels;
        }

        /// <summary>
        /// 只清空窗口栈（静态重置用），不销毁窗口实例。
        /// </summary>
        internal void Reset()
        {
            _stack.Clear();
            _pendingClose = null; // 静态重置后不再等待上一轮遗留的恢复流程。
        }

        #endregion

        #region 打开

        /// <summary>
        /// 打开一个窗口：新窗口显示，旧窗口隐藏并入栈。
        /// 已经打开过的窗口不重新加载 Prefab，只移动到栈顶并重新 Show；
        /// instanceName 区分同类型多实例（null = 默认实例，与旧行为一致）。
        /// </summary>
        internal async Task<T> OpenWindowAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default,
            string instanceName = null) where T : UIPanel
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 上一次关窗的恢复流程还没结束：先等它完成再压栈，否则新窗口可能被随后恢复的旧窗口重新显示。
            await WaitPendingCloseAsync();

            Type type = typeof(T);
            PanelKey key = new PanelKey(type, instanceName);

            // 已经打开过：把实例重新显示，并移到栈顶（不重新加载 Prefab）。
            if (_panels.TryGetOpened(key, out UIPanelInfo opened)
                && opened.Panel != null
                && opened.Panel.State != UIPanelState.Closed)
            {
                UIPanel already = opened.Panel;

                if (_stack.Peek() == already)
                {
                    already.InternalShow(data);
                    CandyUILog.Info($"窗口已在栈顶：{key}（栈深 {_stack.Count}）");
                    return (T)already;
                }

                UIPanel current = _stack.Peek();
                if (current != null && current != already)
                {
                    current.InternalHide();
                    // 等旧栈顶退场播完再显示新栈顶，避免两个窗口的动画叠在一起。
                    await current.HideAsync();
                }

                _stack.Remove(already);
                _stack.Push(already);
                already.InternalShow(data);
                CandyUILog.Info($"窗口重新置顶：{key}（{DescribeStack()}）");
                return (T)already;
            }

            UIPanel panel = await _panels.OpenPanelAsync<T>(data, UILayer.Window, cancellationToken, instanceName);

            UIPanel previous = _stack.Peek();
            if (previous != null && previous != panel)
            {
                previous.InternalHide();
                // 窗口切换等旧窗口 Out 播完，保证视觉上是"旧窗口退场 -> 新窗口入场"。
                await previous.HideAsync();
            }

            _stack.Push(panel);
            CandyUILog.Info($"窗口入栈：{key}（{DescribeStack()}），上层已隐藏");
            return (T)panel;
        }

        #endregion

        #region 关闭

        /// <summary>
        /// 关闭当前窗口，并恢复上一个窗口（如果有）。
        /// 返回 false 表示窗口拒绝了关闭（CanClose），栈与显示都不变。
        /// </summary>
        internal bool CloseCurrentWindow(PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            UIPanel current = _stack.Peek();
            return current != null && CloseWindow(current, reason);
        }

        /// <summary>
        /// 发起关闭：返回 false 表示窗口拒绝了关闭（CanClose），栈与显示都不变；
        /// 受理后立即出栈，上一个窗口要等当前窗口退场播完才恢复（想等恢复完成请用 CloseWindowAsync）。
        /// </summary>
        internal bool CloseWindow(UIPanel window, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (!TryAcceptClose(window, reason, out bool wasTop))
                return false;

            if (!wasTop)
            {
                // 关的不是栈顶（例如业务直接 Close 了压在下面的窗口）：
                // 只从栈里移除，当前栈顶的显示状态保持原样。
                // 否则会把新的栈顶重新 InternalShow 一次，导致它的 OnShow
                // 重复执行（数据刷新两次、入场动画重入）。
                return true;
            }

            UIPanel previous = _stack.Peek();
            if (previous != null && previous.State != UIPanelState.Closed)
                _pendingClose = RestorePreviousWindowAsync(window);

            return true;
        }

        /// <summary>
        /// 关闭指定窗口并等待"出栈 + 上一个窗口恢复显示"全部完成，返回是否关成功。
        /// </summary>
        internal async Task<bool> CloseWindowAsync(UIPanel window, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (!TryAcceptClose(window, reason, out bool wasTop))
                return false;

            if (wasTop)
            {
                UIPanel previous = _stack.Peek();
                if (previous != null && previous.State != UIPanelState.Closed)
                    await RestorePreviousWindowAsync(window);
            }

            return true;
        }

        /// <summary>
        /// 受理关闭的公共部分：先判断是否栈顶（必须在 ClosePanel 之前），再交给面板关闭并立即出栈。
        /// </summary>
        private bool TryAcceptClose(UIPanel window, PanelCloseReason reason, out bool wasTop)
        {
            wasTop = false;
            if (window == null)
                return false;

            // 必须在 ClosePanel 之前判断：ClosePanel 会立刻把窗口移出打开表，
            // 之后再 Peek 只能看到"移除后的新栈顶"，无法还原它原来是不是栈顶。
            wasTop = _stack.Peek() == window;

            // 先关面板：如果被拦截，窗口仍留在栈里，不能提前 Remove。
            if (!_panels.ClosePanel(window, reason))
                return false;

            _stack.Remove(window);
            CandyUILog.Info($"窗口出栈：{window.GetType().Name}（{DescribeStack()}）");
            return true;
        }

        /// <summary>
        /// 等窗口关闭（退场播完/实例销毁或入缓存）后恢复当前栈顶窗口；异常只记录不打断关闭。
        /// 恢复时重新取栈顶：关闭动画期间若有窗口被打开/入栈，就按当前栈序判断，不再用过期的捕获变量。
        /// </summary>
        private async Task RestorePreviousWindowAsync(UIPanel window)
        {
            try
            {
                await _panels.WaitCloseAsync(window);
            }
            catch (Exception exception)
            {
                CandyUILog.Warn($"等待窗口关闭完成时出错：{window.GetType().Name}（{exception.Message}）");
                UnityEngine.Debug.LogException(exception);
            }

            UIPanel top = _stack.Peek();
            if (top == null || top.State == UIPanelState.Closed || top.State == UIPanelState.Visible)
                return; // 栈顶已经可见 = 关闭动画期间有新窗口打开了，不再重复 show 旧窗口。

            top.InternalShow(top.Data);
            CandyUILog.Info($"恢复上一个窗口：{top.GetType().Name}（栈深 {_stack.Count}）");
        }

        /// <summary>
        /// 等待上一次"关窗后恢复"流程结束；没有待处理任务时立即完成，异常只记录不打断打开。
        /// </summary>
        private async Task WaitPendingCloseAsync()
        {
            Task pending = _pendingClose;
            if (pending == null || pending.IsCompleted)
                return;

            try
            {
                await pending;
            }
            catch (Exception exception)
            {
                CandyUILog.Warn($"等待上一次关窗完成时出错：{exception.Message}");
                UnityEngine.Debug.LogException(exception);
            }

            // 等待期间可能又记录了新的 pending close，只清掉自己等的那一个。
            if (ReferenceEquals(_pendingClose, pending))
                _pendingClose = null;
        }

        /// <summary>只清空历史栈，不销毁窗口实例（CloseAll 会紧接着统一销毁）。</summary>
        internal void Clear()
        {
            _stack.Clear();
            CandyUILog.Info("窗口栈已清空");
        }

        #endregion

        #region 查询

        /// <summary>窗口栈里是否还有"可以返回"的上一个窗口（根窗口不算）。</summary>
        internal bool CanGoBack
        {
            get { return _stack.Count > 1; }
        }

        /// <summary>某个面板实例是否在窗口栈里。</summary>
        internal bool Contains(UIPanel panel)
        {
            return _stack.Contains(panel);
        }

        /// <summary>某个面板实例是不是窗口栈的栈顶（只有栈顶才允许 Show）。</summary>
        internal bool IsTop(UIPanel panel)
        {
            return panel != null && _stack.Peek() == panel;
        }

        #endregion

        #region 调试

        /// <summary>调试面板用：一行描述当前窗口栈。</summary>
        internal string GetDebugSnapshot()
        {
            List<UIPanel> panels = _stack.Snapshot();
            return $"窗口栈({panels.Count}): {DescribeStack()}";
        }

        /// <summary>
        /// 按从栈底到栈顶的顺序把窗口类型名拼成一行，供日志与调试快照使用。
        /// </summary>
        private string DescribeStack()
        {
            List<UIPanel> panels = _stack.Snapshot();
            if (panels.Count == 0)
                return "窗口栈空";

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < panels.Count; i++)
            {
                if (i > 0)
                    builder.Append(" -> ");

                builder.Append(panels[i].GetType().Name);
            }

            return builder.ToString();
        }

        #endregion

    }
}
