using System;

namespace CandyUI.Demo
{
    /// <summary>
    /// 确认弹窗的数据：文本 + 确定/取消回调。
    /// 回调是"弹窗被点确定/取消"时执行的业务动作，由打开方自己决定做什么。
    /// </summary>
    public sealed class ConfirmPopupData : UIPanelData
    {
        public string Message = "确定吗？";
        public string ConfirmText = "确定";
        public string CancelText = "取消";

        /// <summary>点"确定"时执行（在弹窗关闭前）。</summary>
        public Action OnConfirm;

        /// <summary>点"取消"时执行（点遮罩关闭不会触发，避免误触时执行取消逻辑）。</summary>
        public Action OnCancel;
    }
}
