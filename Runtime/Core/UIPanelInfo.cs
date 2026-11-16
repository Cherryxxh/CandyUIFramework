using System;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 框架内部使用的"面板档案"：记录一个已打开面板的全部信息。
    ///
    /// 为什么不直接把面板塞进字典里？
    /// 因为关闭一个面板需要同时处理：面板脚本、GameObject、资源句柄、类型、层。
    /// 这些信息放在一起，关闭逻辑才不会漏释放资源（资源泄漏最常见的来源）。
    /// </summary>
    internal sealed class UIPanelInfo
    {
        public Type PanelType;
        public string Address;
        public UILayer Layer;
        public UIPanel Panel;
        public GameObject GameObject;
        public IAssetHandle Handle;

        /// <summary>
        /// 实例键（类型 + 实例名），与打开表/缓存表的 key 一致；关闭与 KeepInstance 写回缓存都按它定位。
        /// </summary>
        internal PanelKey Key { get; set; }

        /// <summary>
        /// 登记到打开表时的递增序号：Dictionary 迭代顺序不保证插入顺序，
        /// 同类型多实例的按打开顺序返回依赖它排序。
        /// </summary>
        internal long OpenSequence { get; set; }

        /// <summary>
        /// Prefab 根是否自带 Canvas（检测时只查根节点，不代加、不改排序/缩放）。
        /// 仅用于调试快照区分独立 Canvas 面板，不参与打开/关闭流程。
        /// </summary>
        internal bool HasOwnCanvas;
    }
}
