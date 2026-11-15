namespace CandyUI.Demo
{
    /// <summary>
    /// 商店 Presenter（M3 最小 Presenter 示例）。
    ///
    /// 它只做四件事：
    ///   1. OnBind 订阅 View 的"购买"事件；
    ///   2. OnShow 记住这次打开带的招呼语，拉数据刷新界面；
    ///   3. 处理购买：让 ShopService 判断/扣钱，再刷新界面并给提示；
    ///   4. OnClose 退订。
    ///
    /// 它不知道按钮长什么样、不知道格子在哪，只调用 ShopPanel 暴露的显示方法。
    /// </summary>
    public sealed class ShopPresenter : PresenterBase
    {
        private string _greeting = "欢迎光临";

        private ShopPanel Panel
        {
            get { return (ShopPanel)View; }
        }

        /// <summary>
        /// Presenter 绑定 View 时订阅“购买”事件；框架负责在视图就绪后调用一次。
        /// </summary>
        protected override void OnBind()
        {
            Panel.BuyClicked += HandleBuyClicked;
        }

        /// <summary>
        /// 每次打开时记住调用方传的招呼语，并刷新商品列表与提示。
        /// </summary>
        public override void OnShow(UIPanelData data)
        {
            ShopPanelData shopData = data as ShopPanelData;
            _greeting = shopData != null ? shopData.Greeting : "欢迎光临";

            Refresh();
            Panel.ShowMessage(_greeting);
        }

        /// <summary>
        /// 面板销毁前退订事件；IsBound 判断让重复关闭也安全。
        /// </summary>
        public override void OnClose()
        {
            if (!IsBound)
                return;

            Panel.BuyClicked -= HandleBuyClicked;
        }

        /// <summary>
        /// 购买回调：交给 ShopService 判断并扣钱，成功或失败都刷新界面并写提示。
        /// </summary>
        private void HandleBuyClicked(int itemId)
        {
            ShopItem item = ShopService.Find(itemId);
            if (item == null)
                return;

            string error;
            if (ShopService.TryBuy(itemId, out error))
                Panel.ShowMessage("购买成功：" + item.Name);
            else
                Panel.ShowMessage(error);

            // 不管成功失败都刷新一次：成功要更新金币/置灰，失败也保持显示一致。
            Refresh();
        }

        /// <summary>
        /// 把 Service 的商品和金币同步到 View；View 不参与任何价格计算。
        /// </summary>
        private void Refresh()
        {
            Panel.ShowItems(ShopService.Items, ShopService.Gold);
        }
    }
}
