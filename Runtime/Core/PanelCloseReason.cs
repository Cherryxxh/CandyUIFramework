namespace CandyUI
{
    /// <summary>
    /// 关闭原因。框架用它在关闭前询问界面"能不能关"。
    ///
    /// 只有 UserClose 会触发拦截钩子（PresenterBase/UIPanel.CanClose）；
    /// ForceClose 用于"关闭全部界面""切场景""重置框架"这类必须关掉的场景，
    /// 如果也被拦截，界面就永远关不掉了。
    /// </summary>
    public enum PanelCloseReason
    {
        /// <summary>用户主动关闭：点关闭按钮、返回、Esc、Android 返回键。</summary>
        UserClose = 0,

        /// <summary>框架强制关闭：CloseAll、切场景、ResetStatics。</summary>
        ForceClose = 1,
    }
}
