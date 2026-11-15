using System.Collections.Generic;

namespace CandyUI
{
    /// <summary>
    /// 窗口历史栈：记录"谁被谁盖住了"。
    ///
    /// Window 层同一时间只显示一个窗口。打开新窗口时，旧窗口被隐藏（不销毁）
    /// 并压入这个栈；玩家点"返回"时，关闭当前窗口，再把上一个窗口显示出来。
    ///
    /// 为什么存 UIPanel 而不是"类型 + 数据"？
    /// 因为窗口实例还在（只是隐藏了），直接显示即可，不用重新加载、也不会丢滚动位置。
    /// 这也意味着隐藏的窗口会占内存 —— 对 UI 来说通常可以接受；
    /// 如果某个窗口特别大，应该关掉而不是压栈（用 UIManager.Close 而不是 NavigateBack）。
    ///
    /// 为什么要清理 Closed 的项？
    /// 业务代码可能绕过 NavigateBack 直接 Close 某个窗口，栈里就会留下已销毁的引用。
    /// Peek() 时会顺手把它们剔除，避免显示一个空引用。
    /// </summary>
    internal sealed class WindowStack
    {
        private readonly List<UIPanel> _panels = new List<UIPanel>();

        public int Count
        {
            get { return _panels.Count; }
        }

        /// <summary>当前最上层的窗口（会剔除已关闭的项）。没有则返回 null。</summary>
        public UIPanel Peek()
        {
            for (int i = _panels.Count - 1; i >= 0; i--)
            {
                UIPanel panel = _panels[i];
                if (panel == null || panel.State == UIPanelState.Closed)
                {
                    _panels.RemoveAt(i);
                    continue;
                }

                return panel;
            }

            return null;
        }

        /// <summary>
        /// 把窗口压到栈顶；打开新窗口或把已有窗口重新置顶时调用，null 会被忽略。
        /// </summary>
        public void Push(UIPanel panel)
        {
            if (panel != null)
                _panels.Add(panel);
        }

        /// <summary>
        /// 判断窗口是否还在栈中；UIManager 用它决定 Close 走窗口恢复流程还是普通面板关闭。
        /// </summary>
        public bool Contains(UIPanel panel)
        {
            return panel != null && _panels.Contains(panel);
        }

        /// <summary>
        /// 从栈里移除指定窗口；关闭窗口时调用，不存在时静默忽略。
        /// </summary>
        public void Remove(UIPanel panel)
        {
            if (panel != null)
                _panels.Remove(panel);
        }

        /// <summary>
        /// 清空窗口栈但不销毁窗口实例；UIManager.CloseAll 在关闭所有面板后才调用。
        /// </summary>
        public void Clear()
        {
            _panels.Clear();
        }

        /// <summary>按栈底到栈顶的顺序返回当前窗口（跳过已关闭的），用于调试面板显示。</summary>
        public List<UIPanel> Snapshot()
        {
            List<UIPanel> result = new List<UIPanel>(_panels.Count);
            for (int i = 0; i < _panels.Count; i++)
            {
                UIPanel panel = _panels[i];
                if (panel != null && panel.State != UIPanelState.Closed)
                    result.Add(panel);
            }

            return result;
        }
    }
}
