using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 示例：奖励弹窗（Popup 层）。
    ///
    /// 和 ConfirmPopup 一起演示"弹窗队列"：
    /// 先弹确认框，再请求奖励弹窗 —— 奖励弹窗会排队，
    /// 关闭确认框后自动弹出。
    /// PopupPriority = 10 让它比默认弹窗更优先（同时排队时先显示）。
    ///
    /// 注意：这个"橘色"弹窗即使关掉，也可以随时点底部的"奖励"按钮再唤出来。
    /// </summary>
    [UIPanel("CandyUI/Demo/RewardPopup", UILayer.Popup, popupPriority: 10, maskAlpha: 0.5f)]
    public sealed class RewardPopup : UIPanel
    {
        /// <summary>
        /// 弹窗创建时搭一次界面；之后每次打开只刷新数据，不重建控件。
        /// </summary>
        protected override void OnCreate()
        {
            BuildView();
        }

        /// <summary>
        /// 用代码搭奖励弹窗的卡片、文本和领取按钮（Demo 故意不依赖 Prefab 和美术资源）。
        /// </summary>
        private void BuildView()
        {
            RectTransform card = DemoUi.CreateCard(transform, new Vector2(780f, 440f),
                new Color(0.32f, 0.24f, 0.10f, 1f));

            TMP_Text title = DemoUi.CreateTitle(card, "获得奖励", 46, 135f);
            title.color = new Color(1f, 0.86f, 0.45f, 1f);

            TMP_Text message = DemoUi.CreateText("Message", card,
                "金币 x100", 34, TextAnchor.MiddleCenter);
            RectTransform messageRect = message.rectTransform;
            messageRect.anchorMin = new Vector2(0.5f, 0.5f);
            messageRect.anchorMax = new Vector2(0.5f, 0.5f);
            messageRect.sizeDelta = new Vector2(620f, 90f);
            messageRect.anchoredPosition = new Vector2(0f, 0f);

            Button receive = DemoUi.CreateButton("ReceiveButton", card, "领取",
                new Vector2(0f, -125f), new Color(0.30f, 0.55f, 0.35f, 1f), Receive);
            receive.GetComponent<RectTransform>().sizeDelta = new Vector2(360f, 84f);
        }

        /// <summary>
        /// 点“领取”的回调：打日志后关闭自己，关闭后弹窗队列才会处理下一个。
        /// </summary>
        private void Receive()
        {
            Debug.Log("[CandyUI] RewardPopup：领取");
            CloseSelf();
        }
    }
}
