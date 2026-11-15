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
        }

        #endregion

        #region 打开

        /// <summary>
        /// 打开一个窗口：新窗口显示，旧窗口隐藏并入栈。
        /// 已经打开过的窗口不重新加载 Prefab，只移动到栈顶并重新 Show。
        /// </summary>
        internal async Task<T> OpenWindowAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default) where T : UIPanel
        {
            cancellationToken.ThrowIfCancellationRequested();

            Type type = typeof(T);

            // 已经打开过：把实例重新显示，并移到栈顶（不重新加载 Prefab）。
            if (_panels.TryGetOpenedOfType(type, out UIPanelInfo opened)
                && opened.Panel != null
                && opened.Panel.State != UIPanelState.Closed)
            {
                UIPanel already = opened.Panel;

                if (_stack.Peek() == already)
                {
                    already.InternalShow(data);
                    CandyUILog.Info($"窗口已在栈顶：{type.Name}（栈深 {_stack.Count}）");
                    return (T)already;
                }

                UIPanel current = _stack.Peek();
                if (current != null && current != already)
                    current.InternalHide();

                _stack.Remove(already);
                _stack.Push(already);
                already.InternalShow(data);
                CandyUILog.Info($"窗口重新置顶：{type.Name}（{DescribeStack()}）");
                return (T)already;
            }

            UIPanel panel = await _panels.OpenPanelAsync<T>(data, UILayer.Window, cancellationToken);

            UIPanel previous = _stack.Peek();
            if (previous != null && previous != panel)
                previous.InternalHide();

            _stack.Push(panel);
            CandyUILog.Info($"窗口入栈：{type.Name}（{DescribeStack()}），上层已隐藏");
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

        /// <summary>关闭指定窗口；只有关掉的是栈顶时，才恢复下面的窗口。</summary>
        internal bool CloseWindow(UIPanel window, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (window == null)
                return false;

            // 必须在 ClosePanel 之前判断：ClosePanel 会把窗口置 Closed 并从栈里移除，
            // 之后再 Peek 只能看到"移除后的新栈顶"，无法还原它原来是不是栈顶。
            bool wasTop = _stack.Peek() == window;

            // 先关面板：如果被拦截，窗口仍留在栈里，不能提前 Remove。
            if (!_panels.ClosePanel(window, reason))
                return false;

            _stack.Remove(window);
            CandyUILog.Info($"窗口出栈：{window.GetType().Name}（{DescribeStack()}）");

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
            {
                previous.InternalShow(previous.Data);
                CandyUILog.Info($"恢复上一个窗口：{previous.GetType().Name}（栈深 {_stack.Count}）");
            }

            return true;
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
