using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 示例：邮件窗口（Window 层）。
    ///
    /// 用来演示"窗口栈加深"：
    ///   主城 -> 邮件（栈深 2）-> 设置（栈深 3）-> NavigateBack() 回到邮件 -> NavigateBack() 回到主城。
    /// 每封邮件点开是一个确认弹窗，演示"弹窗盖在窗口上，关掉后原窗口还在"。
    /// </summary>
    [UIPanel("CandyUI/Demo/MailWindow", UILayer.Window)]
    public sealed class MailWindow : UIPanel
    {
        /// <summary>
        /// 窗口创建时搭界面；邮件内容写死，只演示栈行为。
        /// </summary>
        protected override void OnCreate()
        {
            BuildView();
        }

        /// <summary>
        /// 搭邮件窗口：说明文字、3 行邮件和打开设置、返回按钮。
        /// </summary>
        private void BuildView()
        {
            Image background = DemoUi.CreateImage("Background", transform,
                new Color(0.04f, 0.05f, 0.08f, 0.88f));
            DemoUi.Stretch(background.rectTransform);

            RectTransform card = DemoUi.CreateCard(transform, new Vector2(940f, 740f),
                new Color(0.13f, 0.16f, 0.22f, 0.99f));

            DemoUi.CreateTitle(card, "邮件", 56, 275f);

            TMP_Text hint = DemoUi.CreateText("Hint", card,
                "窗口栈：主城 -> 邮件\n点邮件 = 弹窗；点「打开设置」= 栈深变成 3",
                24, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.sizeDelta = new Vector2(820f, 90f);
            hintRect.anchoredPosition = new Vector2(0f, 190f);

            CreateMailRow(card, "系统邮件", "欢迎来到 CandyUI 示例", 95f);
            CreateMailRow(card, "活动邮件", "登录奖励 x100 金币", 10f);
            CreateMailRow(card, "好友邮件", "你的好友送你一份礼物", -75f);

            Button openSettings = DemoUi.CreateButton("OpenSettingsButton", card, "打开设置（栈深 +1）",
                new Vector2(0f, -200f), new Color(0.36f, 0.30f, 0.62f, 1f), OpenSettings);
            openSettings.GetComponent<RectTransform>().sizeDelta = new Vector2(400f, 80f);

            Button back = DemoUi.CreateButton("BackButton", card, "返回（Back）",
                new Vector2(0f, -305f), new Color(0.22f, 0.42f, 0.78f, 1f), GoBack);
            back.GetComponent<RectTransform>().sizeDelta = new Vector2(400f, 80f);
        }

        /// <summary>
        /// 创建一行邮件按钮；点击统一弹确认框，演示“弹窗盖在窗口上”。
        /// </summary>
        private void CreateMailRow(RectTransform card, string title, string content, float y)
        {
            Button row = DemoUi.CreateButton("Mail_" + title, card, title + "    " + content,
                new Vector2(0f, y), new Color(0.20f, 0.24f, 0.32f, 1f), OpenMail);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(840f, 68f);
        }

        /// <summary>
        /// 点邮件行：打开确认弹窗；关掉弹窗后邮件窗口仍在栈顶。
        /// </summary>
        private async void OpenMail()
        {
            try
            {
                await UIManager.OpenPopupAsync<ConfirmPopup>(new UIPanelData());
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 再压一层窗口打开设置（栈深 +1），用来观察 NavigateBack 逐层回退。
        /// </summary>
        private async void OpenSettings()
        {
            try
            {
                await UIManager.OpenWindowAsync<SettingsWindow>(new UIPanelData());
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 调用 NavigateBack() 返回上一层，等价于按 Esc。
        /// </summary>
        private void GoBack()
        {
            UIManager.NavigateBack();
        }
    }
}
