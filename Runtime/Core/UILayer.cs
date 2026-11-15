namespace CandyUI
{
    /// <summary>
    /// UI 层级。数字越大，显示越靠上（后渲染）。
    ///
    /// 为什么需要"层"而不是把所有面板都挂在 Canvas 下？
    /// 因为 UI 有天然的覆盖关系：背景层放 HUD/主界面，Window 层放背包/设置页，
    /// Popup 层放确认框，Toast 层放飘字。分层之后，面板的显示顺序由层决定，
    /// 不需要每个面板自己算 SortingOrder，也不会出现"背包盖住了确认框"这种事。
    ///
    /// 典型游戏例子：
    /// Background = 战斗 HUD 底图；Main = 主城界面；Window = 背包/设置；
    /// Popup = "是否出售这件装备？"确认框；Toast = "获得 100 金币"飘字；Top = 全屏 Loading。
    ///
    /// 数值故意留出间隔（0/100/200...），以后想插入新层（比如 Guide 引导层放在 Popup 之上）
    /// 时不用改已有层的数字。
    /// </summary>
    public enum UILayer
    {
        Background = 0, // 背景层
        Main = 100,     // 主界面层
        Window = 200,   // 窗口层
        Mask = 250,     // 弹窗遮罩层（Window 之上、Popup 之下）
        Popup = 300,    // 弹窗层
        Toast = 400,    // 飘字层
        Top = 500,      // 顶部层
    }
}
