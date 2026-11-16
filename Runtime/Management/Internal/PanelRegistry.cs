using System;
using System.Collections.Generic;

namespace CandyUI
{
    /// <summary>
    /// 打开表/缓存表的"按 key 查询"辅助：多实例后同一类型会有多个 PanelKey，
    /// 按类型统计、按打开顺序排序、批量摘除缓存这些遍历集中在这里，避免主流程类被撑大。
    /// 只做纯查询与集合整理，不修改面板状态、不销毁实例（销毁仍由 PanelService/PanelFactory 负责）。
    /// </summary>
    internal static class PanelRegistry
    {
        /// <summary>
        /// 按打开顺序收集指定类型的全部已打开实例（默认实例与所有命名实例）；type 为 null 表示不过滤类型。
        /// </summary>
        internal static List<UIPanel> CollectOpenedPanels(
            Dictionary<PanelKey, UIPanelInfo> dicOpenedPanels, Type type)
        {
            List<UIPanelInfo> listInfos = new List<UIPanelInfo>();
            foreach (KeyValuePair<PanelKey, UIPanelInfo> pair in dicOpenedPanels)
            {
                if (pair.Value.Panel != null
                    && (type == null || ReferenceEquals(pair.Key.PanelType, type)))
                {
                    listInfos.Add(pair.Value);
                }
            }

            // 打开顺序由 PanelService 注册时写入的自增序号决定：字典遍历顺序不可依赖。
            listInfos.Sort((left, right) => left.OpenSequence.CompareTo(right.OpenSequence));

            List<UIPanel> listPanels = new List<UIPanel>(listInfos.Count);
            for (int i = 0; i < listInfos.Count; i++)
                listPanels.Add(listInfos[i].Panel);

            return listPanels;
        }

        /// <summary>
        /// 摘除指定类型全部实例名的缓存实例，摘下来的 info 追加到 listDetached 交给调用方销毁。
        /// </summary>
        internal static void DetachCachedOfType(
            Dictionary<PanelKey, UIPanelInfo> dicPanelCache, Type type, List<UIPanelInfo> listDetached)
        {
            List<PanelKey> listKeys = new List<PanelKey>();
            foreach (PanelKey key in dicPanelCache.Keys)
            {
                if (ReferenceEquals(key.PanelType, type))
                    listKeys.Add(key);
            }

            for (int i = 0; i < listKeys.Count; i++)
            {
                if (!dicPanelCache.TryGetValue(listKeys[i], out UIPanelInfo info))
                    continue;

                dicPanelCache.Remove(listKeys[i]);
                listDetached.Add(info);
            }
        }
    }
}
