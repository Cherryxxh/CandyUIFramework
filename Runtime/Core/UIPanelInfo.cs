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
    }
}
