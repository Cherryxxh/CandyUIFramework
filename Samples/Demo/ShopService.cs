using System.Collections.Generic;

namespace CandyUI.Demo
{
    /// <summary>
    /// 商店数据服务（Service）：管金币和商品。
    ///
    /// Presenter 只调这里的 TryBuy，不自己算"钱够不够"。
    /// 以后金币改成服务器下发，也只需要改这个类。
    /// </summary>
    public static class ShopService
    {
        private static List<ShopItem> s_items;

        public static int Gold { get; private set; }

        public static IReadOnlyList<ShopItem> Items
        {
            get
            {
                EnsureCreated();
                return s_items;
            }
        }

        /// <summary>
        /// 按 Id 查找商品；找不到返回 null，由 Presenter 决定怎么提示。
        /// </summary>
        public static ShopItem Find(int itemId)
        {
            EnsureCreated();
            return s_items.Find(item => item.Id == itemId);
        }

        /// <summary>购买：成功扣金币并返回 true；失败通过 error 说明原因。</summary>
        public static bool TryBuy(int itemId, out string error)
        {
            EnsureCreated();

            ShopItem item = s_items.Find(candidate => candidate.Id == itemId);
            if (item == null)
            {
                error = "商品不存在";
                return false;
            }

            if (Gold < item.Price)
            {
                error = "金币不足，先去打两只史莱姆吧";
                return false;
            }

            Gold -= item.Price;
            error = null;
            return true;
        }

        /// <summary>
        /// 清空商品并清零金币；Demo 的“重置数据”演示后调用。
        /// </summary>
        public static void Reset()
        {
            s_items = null;
            Gold = 0;
        }

        /// <summary>
        /// 懒初始化：首次访问时建商品列表并给 100 初始金币。
        /// </summary>
        private static void EnsureCreated()
        {
            if (s_items != null)
                return;

            Gold = 100;
            s_items = new List<ShopItem>
            {
                new ShopItem(3001, "生命药水", 20, "恢复 100 点生命值。"),
                new ShopItem(3002, "魔力药水", 25, "恢复 80 点魔力值。"),
                new ShopItem(3003, "守护戒指", 60, "生命上限 +50，冒险者的第一件首饰。"),
            };
        }
    }
}
