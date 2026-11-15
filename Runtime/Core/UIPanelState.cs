namespace CandyUI
{
    /// <summary>
    /// 面板生命周期状态。
    ///
    /// 状态存在的意义：面板的"打开/关闭"不是一瞬间完成的，中间可能有异步加载。
    /// 有了状态，框架才能回答"这个面板现在到底在干什么"，
    /// 从而做到防重复打开、关闭时等待、缓存复用等逻辑。
    ///
    /// 一个面板正常的一生：
    /// None -> Loading -> Created -> Showing -> Visible -> Hiding -> Hidden -> Closed
    /// 关闭时先进入 Closing（播退场动画），再落到 Closed（销毁）或 Hidden（KeepInstance 入缓存）。
    /// </summary>
    public enum UIPanelState
    {
        /// <summary>还没开始加载。</summary>
        None = 0,

        /// <summary>正在异步加载 Prefab（这个状态在 M2 的注册表方案里会用得更多）。</summary>
        Loading,

        /// <summary>Prefab 已实例化，OnCreate 已执行（只执行一次）。</summary>
        Created,

        /// <summary>正在执行 OnShow。</summary>
        Showing,

        /// <summary>已显示，玩家可以看到并交互。</summary>
        Visible,

        /// <summary>正在执行 OnHide（隐藏但没销毁，下次打开可复用）。</summary>
        Hiding,

        /// <summary>已隐藏（GameObject 被 SetActive(false)）。</summary>
        Hidden,

        /// <summary>已关闭，等待销毁或已被销毁。</summary>
        Closed,

        /// <summary>正在关闭：退场动画播放中，播完后销毁或进入缓存。</summary>
        Closing = 8,
    }
}
