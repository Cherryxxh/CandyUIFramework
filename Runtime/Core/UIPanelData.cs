using System;

namespace CandyUI
{
    /// <summary>
    /// 面板数据的基类：每次"打开"面板时传给 OnShow 的参数。
    ///
    /// 为什么需要它？
    /// 同一个面板可能在不同场景下显示不同内容。比如背包面板：
    /// 从主界面点开时默认显示"道具"页，从商店"选择材料"时打开则要显示"材料"页并进入选择模式。
    /// 这些差异就是"这次打开的数据"，用 UIPanelData 子类表达。
    ///
    /// 用法示例：
    ///   public sealed class BagPanelData : UIPanelData
    ///   {
    ///       public int DefaultTab;      // 默认页签
    ///       public bool SelectMode;     // 是否选择模式
    ///   }
    ///
    ///   await UIManager.OpenWindowAsync<BagPanel>(new BagPanelData { DefaultTab = 2, SelectMode = true });
    ///
    /// 注意：它是"每次打开"的参数，不是面板的长期状态。
    /// 面板自己的长期数据（比如滚动位置）应该由面板或 Presenter 自己保存。
    /// </summary>
    [Serializable]
    public class UIPanelData
    {
    }
}
