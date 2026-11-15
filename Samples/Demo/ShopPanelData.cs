namespace CandyUI.Demo
{
    /// <summary>
    /// 打开商店时传的参数。演示"界面不猜数据，调用方告诉它"：
    /// 从主城进来传"欢迎光临"，从铁匠铺进来可以传别的招呼语。
    /// </summary>
    public sealed class ShopPanelData : UIPanelData
    {
        public string Greeting { get; set; } = "欢迎光临，看看需要点什么？";
    }
}
