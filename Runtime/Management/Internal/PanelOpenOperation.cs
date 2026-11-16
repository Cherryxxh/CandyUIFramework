using System;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 一次"进行中的面板打开"的全部状态：共享创建 Task、等待者数量、废弃标记。
    ///
    /// 为什么把三张表的记账收到一个对象里？原来的 _dicLoading / _dicOpeningWaiters /
    /// _hsAbandonedOpenings 描述的是同一件事的三面，取消打开的竞态修复就是在三张表之间来回补漏；
    /// 收敛成对象后，一次打开从开始到收尾只有一个所有者，加字段不会只改到其中两张表。
    ///
    /// 生命周期：由 PanelOpener 创建并放入字典。只要「Task 仍登记 / 还有等待者 / 废弃待收尾」
    /// 任一成立，对象就留在字典里；三者都消失后才移除。
    /// 依赖方向：PanelOpener -> PanelOpenOperation（纯状态，不认识框架其他部分）。
    /// </summary>
    internal sealed class PanelOpenOperation
    {
        /// <summary>实例键（类型 + 实例名，字典的 key），收尾回调按它定位要关闭的面板实例。</summary>
        internal readonly PanelKey Key;

        /// <summary>共享创建 Task（用 CancellationToken.None 创建，不绑定任何调用方）。为空表示已结束/未登记。</summary>
        internal Task<UIPanel> Task;

        /// <summary>正在等这个共享 Task 的调用方数量（含创建者）。</summary>
        internal int WaiterCount;

        /// <summary>
        /// 最后一个等待者已取消、等待共享创建结束后收尾。
        /// 任何"会拿到面板"的路径都必须先清掉它，否则后台收尾会把刚交付的实例销毁。
        /// </summary>
        internal bool IsAbandoned;

        /// <summary>面板类型（Key.PanelType 的快捷访问，日志与关闭回调仍按类型描述）。</summary>
        internal Type PanelType
        {
            get { return Key.PanelType; }
        }

        /// <summary>
        /// 用实例键构造一次打开的状态对象；键同时作为字典 key 和收尾定位依据。
        /// </summary>
        internal PanelOpenOperation(PanelKey key)
        {
            Key = key;
        }

        /// <summary>
        /// 等待者计数 +1；每个 await 这次打开的调用方（含创建者）都要登记。
        /// </summary>
        internal void AddWaiter()
        {
            WaiterCount++;
        }

        /// <summary>
        /// 等待者计数 -1（不会减成负数）；调用方取消或拿到面板后调用。
        /// </summary>
        internal void RemoveWaiter()
        {
            if (WaiterCount > 0)
                WaiterCount--;
        }

        /// <summary>是否还有人等这次打开；为 false 是收尾销毁幽灵实例的前提之一。</summary>
        internal bool HasWaiters
        {
            get { return WaiterCount > 0; }
        }
    }
}
