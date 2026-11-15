using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 示例：确认弹窗（Popup 层）。
    ///
    /// 演示四件事：
    /// 1. 弹窗上方会盖一张半透明遮罩，背景变暗；
    /// 2. 遮罩挡住点击，弹窗后面的按钮点不到；
    /// 3. DismissOnMaskClick = true：点遮罩空白处也能关闭（取消）；
    /// 4. 通过 ConfirmPopupData 传文本和确定/取消回调。
    ///
    /// 注意：强制弹窗（公告/网络错误）应该保持 DismissOnMaskClick = false，
    /// 并且 MaskAlpha 可以设到 0.8 左右，避免玩家误触。
    /// </summary>
    [UIPanel("CandyUI/Demo/ConfirmPopup", UILayer.Popup, dismissOnMaskClick: true)]
    public sealed class ConfirmPopup : UIPanel
    {
        private TMP_Text _message;
        private TMP_Text _confirmLabel;
        private TMP_Text _cancelLabel;
        private ConfirmPopupData _data;

        /// <summary>
        /// 弹窗创建时搭界面；文本控件的引用只在这里取一次，之后复用。
        /// </summary>
        protected override void OnCreate()
        {
            BuildView();
        }

        /// <summary>
        /// 用代码搭确认框的卡片和确定、取消按钮，点击回调分别指向 Confirm 和 Cancel。
        /// </summary>
        private void BuildView()
        {
            RectTransform card = DemoUi.CreateCard(transform, new Vector2(780f, 440f),
                new Color(0.18f, 0.20f, 0.27f, 1f));

            DemoUi.CreateTitle(card, "确认", 44, 135f);

            _message = DemoUi.CreateText("Message", card,
                "确定吗？", 28, TextAnchor.MiddleCenter);
            RectTransform messageRect = _message.rectTransform;
            messageRect.anchorMin = new Vector2(0.5f, 0.5f);
            messageRect.anchorMax = new Vector2(0.5f, 0.5f);
            messageRect.sizeDelta = new Vector2(640f, 130f);
            messageRect.anchoredPosition = new Vector2(0f, 0f);

            Button confirm = DemoUi.CreateButton("ConfirmButton", card, "确定",
                new Vector2(-175f, -125f), new Color(0.30f, 0.55f, 0.35f, 1f), Confirm);
            confirm.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 80f);
            _confirmLabel = confirm.GetComponentInChildren<TMP_Text>();

            Button cancel = DemoUi.CreateButton("CancelButton", card, "取消",
                new Vector2(175f, -125f), new Color(0.35f, 0.37f, 0.42f, 1f), Cancel);
            cancel.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 80f);
            _cancelLabel = cancel.GetComponentInChildren<TMP_Text>();
        }

        /// <summary>
        /// 每次打开时把 ConfirmPopupData 的文案填进界面；没传数据则用默认文案。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            _data = data as ConfirmPopupData;

            _message.text = _data != null ? _data.Message : "确定吗？";
            if (_confirmLabel != null)
                _confirmLabel.text = _data != null ? _data.ConfirmText : "确定";
            if (_cancelLabel != null)
                _cancelLabel.text = _data != null ? _data.CancelText : "取消";
        }

        /// <summary>
        /// 点“确定”：先关闭自己再执行回调，避免回调里打开新界面时栈和队列状态错乱。
        /// </summary>
        private void Confirm()
        {
            ConfirmPopupData data = _data;
            // 先关闭自己，再执行回调：回调里可能会打开别的界面/销毁父界面。
            CloseSelf();
            data?.OnConfirm?.Invoke();
        }

        /// <summary>
        /// 点“取消”：关闭后执行 OnCancel；点遮罩关闭不触发这里，避免误触执行取消逻辑。
        /// </summary>
        private void Cancel()
        {
            ConfirmPopupData data = _data;
            CloseSelf();
            data?.OnCancel?.Invoke();
        }
    }
}
