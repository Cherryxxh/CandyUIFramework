using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CandyUI.Demo
{
    /// <summary>
    /// 按钮"按下去缩小、松手弹回"的 DOTween 反馈（示例）。
    ///
    /// 为什么单独写一个组件，而不是改 Button？
    ///   Button 只负责"点击"，按压反馈是表现层的事；
    ///   做成组件后，任何按钮挂上就有反馈，不需要继承或改 UGUI 源码。
    ///
    /// 用法：DemoUi.CreateButton 会自动挂这个组件；
    /// 也可以手动加给任意带 RectTransform 的按钮。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TweenPressButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        #region 参数与状态

        /// <summary>按下时缩到原尺寸的 92%。</summary>
        private const float PressedScale = 0.92f;

        /// <summary>按下缩放时长（要短，才有"跟手"的感觉）。</summary>
        private const float PressDuration = 0.08f;

        /// <summary>松手回弹时长（比按下长，用 OutBack 产生弹性）。</summary>
        private const float ReleaseDuration = 0.22f;

        /// <summary>记录初始缩放，松手时回到这个值（兼容被布局/动画改过 scale 的按钮）。</summary>
        private Vector3 _baseScale = Vector3.one;

        private bool _ready;

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 记录按钮初始缩放；松手回到这个值，兼容被布局或其它动画改过 scale 的按钮。
        /// </summary>
        private void Awake()
        {
            _baseScale = transform.localScale;
            _ready = true;
        }

        /// <summary>禁用时强制复位：避免按钮在"按下状态"被隐藏后，再出现时还是缩小的。</summary>
        private void OnDisable()
        {
            transform.DOKill();
            transform.localScale = _baseScale;
        }

        #endregion

        #region 指针反馈

        /// <summary>
        /// 按下时缩到 92%；先 Kill 上一个动画，快速连点也不会多个 Tween 抢改 scale。
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_ready)
                return;

            // 先 Kill 上一次动画，避免快速连点时多个 Tween 抢着改 scale。
            transform.DOKill();
            transform.DOScale(_baseScale * PressedScale, PressDuration).SetUpdate(true);
        }

        /// <summary>
        /// 松手回弹到初始缩放，用 OutBack 缓动做出轻微弹性。
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_ready)
                return;

            transform.DOKill();
            transform.DOScale(_baseScale, ReleaseDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        #endregion
    }
}
