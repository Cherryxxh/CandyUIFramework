namespace CandyUI
{
    /// <summary>
    /// 面板被"关闭"时，实例怎么处理。
    ///
    /// 默认是 Destroy：关闭 = 销毁 GameObject + 释放资源句柄，内存干净。
    ///
    /// KeepInstance 适合"高频开关"的界面（背包、设置、角色面板）：
    /// 关闭时只 SetActive(false) 并留在实例缓存里，下次打开直接 Show，
    /// 不用重新加载 Prefab、不用重新 Instantiate、OnCreate 也不会重跑。
    ///
    /// 注意两个约束：
    /// 1. KeepInstance 的面板关闭时只触发 OnHide，不触发 OnClose；
    ///    OnClose 只会在真正销毁时触发（ClearPanelCache / CloseAll / 切加载器），
    ///    所以"关闭时注销全局事件"这类逻辑要放在 OnHide 里。
    /// 2. KeepInstance 会一直持有资源句柄，资源不会被卸载；
    ///    界面越多、缓存越大，占的内存越多。请只给真正高频的面板开这个策略。
    /// </summary>
    public enum PanelCloseBehavior
    {
        /// <summary>关闭即销毁实例并释放资源句柄（默认）。</summary>
        Destroy = 0,

        /// <summary>关闭只隐藏实例，下次打开直接复用；OnClose 延后到真正销毁时。</summary>
        KeepInstance = 1,
    }
}
