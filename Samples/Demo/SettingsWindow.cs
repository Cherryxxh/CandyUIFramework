using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 示例：设置窗口（Window 层）。
    ///
    /// 演示：
    /// 1. 窗口栈：从主城打开它，主城被隐藏并压栈；点"返回"时恢复主城。
    /// 2. 弹窗：点"重置数据"弹出确认框，背景变暗且点不到设置窗口的按钮。
    /// 3. 关闭拦截：点"画质"制造一个"未保存"状态，再点返回/关闭，
    ///    会先弹确认框；取消则留在设置界面，确定才真正关掉。
    /// 4. 再压一层窗口："打开邮件"后栈深变成 3。
    /// </summary>
    [UIPanel("CandyUI/Demo/SettingsWindow", UILayer.Window)]
    public sealed class SettingsWindow : UIPanel
    {
        private bool _highQuality = true;
        private bool _dirty;
        private TMP_Text _qualityLabel;

        /// <summary>
        /// 窗口创建时搭界面；画质开关等临时状态存在实例字段里。
        /// </summary>
        protected override void OnCreate()
        {
            BuildView();
        }

        /// <summary>
        /// 搭设置界面：画质切换、重置数据、打开邮件和返回按钮。
        /// </summary>
        private void BuildView()
        {
            Image background = DemoUi.CreateImage("Background", transform,
                new Color(0.04f, 0.05f, 0.08f, 0.88f));
            DemoUi.Stretch(background.rectTransform);

            RectTransform card = DemoUi.CreateCard(transform, new Vector2(880f, 640f),
                new Color(0.16f, 0.18f, 0.24f, 0.99f));

            DemoUi.CreateTitle(card, "设置", 54, 235f);

            TMP_Text hint = DemoUi.CreateText("Hint", card,
                "点“画质”制造未保存状态，再点返回，会先弹确认框\n窗口栈：主城 -> 设置",
                22, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.sizeDelta = new Vector2(800f, 80f);
            hintRect.anchoredPosition = new Vector2(0f, 175f);

            Button quality = DemoUi.CreateButton("QualityButton", card, "画质：高（点击切换）",
                new Vector2(0f, 90f), new Color(0.28f, 0.45f, 0.35f, 1f), ToggleQuality);
            _qualityLabel = quality.GetComponentInChildren<TMP_Text>();

            DemoUi.CreateButton("ResetButton", card, "重置数据（弹确认框）",
                new Vector2(0f, -15f), new Color(0.55f, 0.25f, 0.28f, 1f), OpenConfirm);

            DemoUi.CreateButton("OpenMailButton", card, "打开邮件（栈深 +1）",
                new Vector2(0f, -120f), new Color(0.36f, 0.30f, 0.62f, 1f), OpenMail);

            DemoUi.CreateButton("BackButton", card, "返回（Back）",
                new Vector2(0f, -225f), new Color(0.22f, 0.42f, 0.78f, 1f), GoBack);
        }

        /// <summary>
        /// 切换画质并标记“未保存”，这样返回或关闭时 CanClose 会先弹确认框。
        /// </summary>
        private void ToggleQuality()
        {
            _highQuality = !_highQuality;
            _dirty = true;

            if (_qualityLabel != null)
                _qualityLabel.text = _highQuality ? "画质：高（点击切换）" : "画质：低（点击切换）";

            Debug.Log($"[CandyUI] 设置已修改（未保存）：画质 = {(_highQuality ? "高" : "低")}");
        }

        /// <summary>
        /// 关闭拦截：只有用户主动关闭（返回/关闭按钮）才会问这里。
        /// 有未保存修改时弹确认框并拒绝本次关闭；玩家确认后再 CloseSelf 就会放行。
        /// </summary>
        protected override bool CanClose(PanelCloseReason reason)
        {
            if (reason == PanelCloseReason.ForceClose || !_dirty)
                return true;

            OpenDiscardConfirm();
            return false;
        }

        /// <summary>
        /// 弹“放弃修改”确认框；点确定才清掉未保存标记并真正关闭设置。
        /// </summary>
        private async void OpenDiscardConfirm()
        {
            try
            {
                await UIManager.OpenPopupAsync<ConfirmPopup>(new ConfirmPopupData
                {
                    Message = "设置还没保存，确定离开吗？",
                    ConfirmText = "放弃修改",
                    CancelText = "继续编辑",
                    OnConfirm = () =>
                    {
                        _dirty = false;
                        CloseSelf();
                    },
                });
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 弹“重置本地数据”确认框；这里只演示传文案，关闭由框架默认处理。
        /// </summary>
        private async void OpenConfirm()
        {
            try
            {
                await UIManager.OpenPopupAsync<ConfirmPopup>(new ConfirmPopupData
                {
                    Message = "要重置本地数据吗？\n（点遮罩空白处 = 取消）",
                });
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 再压一层窗口打开邮件，演示窗口栈加深到 3 层。
        /// </summary>
        private async void OpenMail()
        {
            try
            {
                await UIManager.OpenWindowAsync<MailWindow>(new UIPanelData());
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 调用 NavigateBack() 返回；有未保存修改时会被 CanClose 拦下。
        /// </summary>
        private void GoBack()
        {
            // NavigateBack()：有弹窗先关弹窗，没弹窗就关当前窗口、恢复上一个窗口。
            // 当前窗口有未保存修改时，会先被 CanClose 拦下来弹确认框。
            UIManager.NavigateBack();
        }
    }
}
