using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 示例：主城窗口（Window 层）。
    ///
    /// 它演示五件事：
    /// 1. 打开另一个窗口时，主城被隐藏并入栈（栈深 +1）；
    /// 2. 从设置窗口 NavigateBack() 回来时，主城原样恢复（不会重新加载）；
    /// 3. 打开弹窗时，主城变暗且点不动（遮罩挡住）；
    /// 4. Toast 是自己定时消失的提示，不参与窗口栈；
    /// 5. 关闭后保留实例（closeBehavior: KeepInstance）：关掉再开 OnCreate 不会重跑，
    ///    也可以直接 TryHidePanel/TryShowPanel 做"隐藏但不关闭"。
    ///
    /// 底部调试工具栏可以随时把任何界面重新打开，也可以实时看到
    /// 窗口栈 / 弹窗队列 / 遮罩 / 缓存 的变化。
    /// </summary>
    [UIPanel("CandyUI/Demo/HomeWindow", UILayer.Window,
        closeBehavior: PanelCloseBehavior.KeepInstance)]
    public sealed class HomeWindow : UIPanel
    {
        /// <summary>
        /// 窗口创建时搭界面；KeepInstance 下只执行一次，关掉再开会复用实例。
        /// </summary>
        protected override void OnCreate()
        {
            BuildView();
        }

        /// <summary>
        /// 搭主城按钮列表，每个按钮演示框架的一种行为（窗口栈、弹窗队列、Toast、隐藏）。
        /// </summary>
        private void BuildView()
        {
            Image background = DemoUi.CreateImage("Background", transform,
                new Color(0.04f, 0.05f, 0.08f, 0.90f));
            DemoUi.Stretch(background.rectTransform);

            RectTransform card = DemoUi.CreateCard(transform, new Vector2(980f, 860f),
                new Color(0.15f, 0.17f, 0.22f, 0.99f));

            DemoUi.CreateTitle(card, "CandyUI · 主城", 56, 355f);

            TMP_Text hint = DemoUi.CreateText("Hint", card,
                "窗口栈 / 弹窗队列 / Toast — 底部工具栏可随时唤起任意界面",
                24, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.sizeDelta = new Vector2(840f, 60f);
            hintRect.anchoredPosition = new Vector2(0f, 288f);

            DemoUi.CreateButton("OpenSettingsButton", card, "打开设置（新窗口）",
                new Vector2(0f, 155f), new Color(0.22f, 0.42f, 0.78f, 1f), OpenSettings);

            DemoUi.CreateButton("OpenMailButton", card, "打开邮件（再压一层窗口）",
                new Vector2(0f, 55f), new Color(0.36f, 0.30f, 0.62f, 1f), OpenMail);

            DemoUi.CreateButton("QueuePopupButton", card, "连弹两个弹窗（队列）",
                new Vector2(0f, -45f), new Color(0.30f, 0.55f, 0.35f, 1f), OpenQueuedPopups);

            DemoUi.CreateButton("ToastButton", card, "弹一个 Toast（不参与栈）",
                new Vector2(0f, -145f), new Color(0.62f, 0.45f, 0.18f, 1f), ShowToast);

            DemoUi.CreateButton("HideButton", card, "隐藏主城",
                new Vector2(-250f, -245f), new Color(0.30f, 0.36f, 0.42f, 1f), HideHome);

            DemoUi.CreateButton("ShowButton", card, "显示主城",
                new Vector2(250f, -245f), new Color(0.26f, 0.42f, 0.36f, 1f), ShowHome);

            DemoUi.CreateButton("CloseButton", card, "关闭主城（保留实例）",
                new Vector2(0f, -365f), new Color(0.55f, 0.25f, 0.28f, 1f), CloseSelf);
        }

        /// <summary>
        /// 只 SetActive(false) 不销毁，实例和句柄都保留，可再用 TryShowPanel 显示。
        /// </summary>
        private void HideHome()
        {
            UIManager.TryHidePanel<HomeWindow>();
        }

        /// <summary>
        /// 把隐藏的主城重新显示出来，不会重新走 Create 或加载资源。
        /// </summary>
        private void ShowHome()
        {
            UIManager.TryShowPanel<HomeWindow>();
        }

        /// <summary>
        /// 打开设置窗口：主城入栈隐藏，返回时原样恢复。
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
        /// 打开邮件窗口：再压一层窗口，演示栈深继续增加。
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
        /// 连开两个弹窗：第一个立即显示，第二个进队列，关掉第一个后自动弹出。
        /// </summary>
        private async void OpenQueuedPopups()
        {
            try
            {
                // 第一个弹窗立即显示；第二个因为已有弹窗，进入队列。
                // 关闭第一个后，第二个会自动弹出来（观察底部工具栏的"队列"数字）。
                await UIManager.OpenPopupAsync<ConfirmPopup>(new UIPanelData());
                await UIManager.OpenPopupAsync<RewardPopup>(new UIPanelData());
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 用 PlainPanel 打开 Toast：不参与窗口栈，也不进弹窗队列。
        /// </summary>
        private async void ShowToast()
        {
            try
            {
                await UIManager.OpenPlainPanelAsync<ToastPanel>();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
