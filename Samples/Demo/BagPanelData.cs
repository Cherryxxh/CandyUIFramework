namespace CandyUI.Demo
{
    /// <summary>
    /// 打开背包时传进来的参数：比如从主城菜单点"背包"时，默认停在"全部"页签；
    /// 从铁匠铺点"材料"时，可以直接传 BagTab.Material。
    ///
    /// 这就是 View 和 Presenter 之间唯一的数据通道：调用方 new 一个 Data 传进去，
    /// 界面自己不去猜"这次该显示什么"。
    /// </summary>
    public sealed class BagPanelData : UIPanelData
    {
        public BagTab Tab { get; set; } = BagTab.All;
    }
}
