using System.Collections.Generic;

namespace CandyUI.Demo
{
    /// <summary>
    /// 背包数据服务（演示用的 Service）。
    ///
    /// Presenter 不直接 new 数据，而是通过它拿：
    /// 以后换成读配置表、读存档、请求服务器，只要改这里，
    /// BagPresenter 和 BagPanel 都不用动。
    /// </summary>
    public static class BagService
    {
        private static List<BagItem> s_items;

        /// <summary>按页签过滤，只返回还有剩余数量的道具。</summary>
        public static List<BagItem> GetItems(BagTab tab)
        {
            EnsureCreated();

            List<BagItem> result = new List<BagItem>();
            for (int i = 0; i < s_items.Count; i++)
            {
                BagItem item = s_items[i];
                if (item.Count <= 0)
                    continue;

                if (tab == BagTab.All || item.Tab == tab)
                    result.Add(item);
            }

            return result;
        }

        /// <summary>使用一个道具：成功返回 true。</summary>
        public static bool Use(int itemId)
        {
            EnsureCreated();

            for (int i = 0; i < s_items.Count; i++)
            {
                if (s_items[i].Id == itemId)
                    return s_items[i].TryUseOne();
            }

            return false;
        }

        /// <summary>恢复初始数据（演示里方便反复测试）。</summary>
        public static void Reset()
        {
            s_items = null;
        }

        /// <summary>
        /// 懒加载演示数据：第一次访问时创建道具列表；Reset 后再次访问会重新创建。
        /// </summary>
        private static void EnsureCreated()
        {
            if (s_items != null)
                return;

            s_items = new List<BagItem>
            {
                new BagItem(1001, "生命药水", BagTab.Material, 5, "使用后恢复 100 点生命值。"),
                new BagItem(1002, "魔力药水", BagTab.Material, 3, "使用后恢复 80 点魔力值。"),
                new BagItem(1003, "面包", BagTab.Material, 10, "冒险途中的干粮，恢复少量生命。"),
                new BagItem(1004, "铁矿石", BagTab.Material, 8, "打造装备的基础材料。"),
                new BagItem(1005, "魔法布料", BagTab.Material, 6, "制作法系防具的材料。"),
                new BagItem(2001, "铁剑", BagTab.Equipment, 1, "普通铁匠打造的长剑，攻击力 +12。"),
                new BagItem(2002, "皮甲", BagTab.Equipment, 1, "轻便的皮甲，防御力 +8。"),
                new BagItem(2003, "木盾", BagTab.Equipment, 1, "格挡远程攻击有奇效。"),
                new BagItem(2004, "旅人靴", BagTab.Equipment, 1, "移动速度 +5%。"),
                new BagItem(2005, "守护戒指", BagTab.Equipment, 1, "生命上限 +50。"),
                new BagItem(2006, "火把", BagTab.Equipment, 2, "照亮洞穴，也能用来点火。"),
                new BagItem(2007, "旧地图", BagTab.Material, 1, "标记着某个宝箱的位置。"),
            };
        }
    }
}
