using System.Collections;
using TMPro;
using UnityEngine;

namespace CandyUI.Demo
{
    /// <summary>
    /// 示例：Toast 提示（Toast 层）。
    ///
    /// Toast 的特点：
    /// - 不属于窗口栈，也不属于弹窗队列，不会挡住任何操作（不参与返回）；
    /// - 层级比窗口/弹窗都高，显示在最上面；
    /// - 自己定时关闭，玩家不能点它。
    ///
    /// 适合"获得金币 +100""已复制到剪贴板"这类短暂提示。
    /// </summary>
    [UIPanel("CandyUI/Demo/ToastPanel", UILayer.Toast)]
    public sealed class ToastPanel : UIPanel
    {
        private const float ShowSeconds = 1.6f;

        private Coroutine _autoClose;

        /// <summary>
        /// Toast 创建时搭一次界面；它是临时提示，关闭后按默认行为销毁实例。
        /// </summary>
        protected override void OnCreate()
        {
            BuildView();
        }

        /// <summary>
        /// 每次显示都重新计时：连续触发 Toast 时刷新本条的存活时间，而不是叠出多个。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            // 连续触发 Toast 时，重新计时，而不是叠出很多个。
            if (_autoClose != null)
                StopCoroutine(_autoClose);

            _autoClose = StartCoroutine(AutoClose());
        }

        /// <summary>
        /// 隐藏时停掉自动关闭协程，避免面板不可见时被残留协程关闭。
        /// </summary>
        protected override void OnHide()
        {
            if (_autoClose != null)
            {
                StopCoroutine(_autoClose);
                _autoClose = null;
            }
        }

        /// <summary>
        /// 协程：等待固定时长后关闭自己；用 Realtime 等待，游戏暂停（timeScale=0）也能正常消失。
        /// </summary>
        private IEnumerator AutoClose()
        {
            // 用 Realtime：游戏暂停（timeScale = 0）时提示也能正常消失。
            yield return new WaitForSecondsRealtime(ShowSeconds);
            _autoClose = null;
            CloseSelf();
        }

        /// <summary>
        /// 用代码搭一条居中的 Toast 提示（一次性提示，不需要 Prefab 和美术资源）。
        /// </summary>
        private void BuildView()
        {
            RectTransform card = DemoUi.CreateCard(transform, new Vector2(860f, 180f),
                new Color(0.10f, 0.22f, 0.16f, 0.96f));
            card.anchoredPosition = new Vector2(0f, -330f);

            TMP_Text text = DemoUi.CreateText("Message", card,
                "Toast：这是一条提示，1.6 秒后自动消失（不参与窗口栈/弹窗队列）",
                26, TextAnchor.MiddleCenter);
            DemoUi.Stretch(text.rectTransform);
        }
    }
}
