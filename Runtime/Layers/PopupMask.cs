using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CandyUI
{
    /// <summary>
    /// 弹窗遮罩：一张盖住全屏的半透明黑色 Image，位于 Window 层之上、Popup 层之下。
    ///
    /// 它有三个作用：
    /// 1. 视觉聚焦：把背景压暗，让玩家的注意力落在弹窗上；
    /// 2. 挡住点击：raycastTarget = true，吞掉所有点击，防止点到弹窗背后的按钮；
    /// 3. 点击关闭：可选，点击遮罩关闭最上层弹窗（由 UIPanelAttribute.DismissOnMaskClick 决定）。
    ///
    /// 注意：它是半透明的（alpha 一般 0.5~0.7），不是黑屏。
    /// 全黑（alpha = 1）通常用于 Loading / 场景转场，不属于弹窗遮罩。
    ///
    /// 同一时间只维护一张遮罩：
    /// - 有弹窗显示 -> ShowAsync() 淡入；
    /// - 弹窗全部关闭 -> HideAsync() 淡出后隐藏；
    /// - 多个弹窗排队 -> 保持显示，不重复叠加。
    /// 淡入淡出只改 CanvasGroup.alpha，不碰 Image 颜色（颜色决定本次遮罩的目标浓度）。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class PopupMask : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>淡入/淡出时长（秒，不受 timeScale 影响的线性过渡）。</summary>
        private const float FadeDuration = 0.12f;

        private Image _image;
        private CanvasGroup _canvasGroup;
        private Action _onClick;

        /// <summary>目标浓度：0 表示应当隐藏，大于 0 表示应当显示（淡入淡出过程中立即生效）。</summary>
        private float _targetAlpha;

        /// <summary>当前淡入淡出版本号：新的一次 Show/Hide 会让旧循环在下一帧退出。</summary>
        private int _fadeVersion;

        private CancellationTokenSource _fadeCts;

        /// <summary>遮罩当前是否应当显示（淡出开始后立即为 false，调试快照与测试据此判断）。</summary>
        internal bool IsVisible
        {
            get { return gameObject.activeSelf && _targetAlpha > 0f; }
        }

        /// <summary>
        /// 由 UIRoot 调用：在 Mask 层容器下创建唯一的一张遮罩。
        /// 创建后默认隐藏，等第一个弹窗打开时再显示。
        /// </summary>
        internal static PopupMask Create(RectTransform parent)
        {
            GameObject go = new GameObject("PopupMask",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup), typeof(PopupMask));

            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);

            PopupMask mask = go.GetComponent<PopupMask>();
            mask.Init();
            go.SetActive(false);
            return mask;
        }

        /// <summary>
        /// 补齐 Image / CanvasGroup 组件并设为黑色半透明、开启射线检测；Create 与首次 Show 都会调用。
        /// </summary>
        private void Init()
        {
            if (_image == null)
                _image = GetComponent<Image>();

            if (_image == null)
                _image = gameObject.AddComponent<Image>();

            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();

            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // 黑色、半透明；具体透明度在 Show(alpha) 时由弹窗配置决定。
            _image.color = new Color(0f, 0f, 0f, 0.6f);

            // 关键：接收射线。没有它，点击会穿过遮罩落到下面的窗口/主界面上。
            _image.raycastTarget = true;
        }

        /// <summary>同步显示遮罩并立即设为指定透明度（调试与强制路径用，不播淡入）。</summary>
        public void Show(float alpha)
        {
            if (_image == null)
                Init();

            CancelFade();
            _targetAlpha = Mathf.Clamp01(alpha);
            _image.color = new Color(0f, 0f, 0f, _targetAlpha);
            ApplyGroupAlpha(1f);

            gameObject.SetActive(true);

            // 保证遮罩在同层里排最后（虽然 Mask 层只有它一个，属于防御性写法）。
            transform.SetAsLastSibling();
        }

        /// <summary>同步隐藏遮罩（调试与强制路径用，不播淡出）。</summary>
        public void Hide()
        {
            CancelFade();
            _targetAlpha = 0f;

            if (gameObject.activeSelf)
            {
                ApplyGroupAlpha(0f);
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 淡入到指定透明度：从当前 CanvasGroup.alpha 起步，淡入中再次调用可平滑接管。
        /// 未运行（编辑模式）或对象不可用时直接设值，不启动逐帧循环。
        /// </summary>
        public Task ShowAsync(float alpha, CancellationToken cancellationToken)
        {
            if (_image == null)
                Init();

            CancelFade();
            _targetAlpha = Mathf.Clamp01(alpha);
            _image.color = new Color(0f, 0f, 0f, _targetAlpha);

            float from = gameObject.activeSelf ? _canvasGroup.alpha : 0f;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                ApplyGroupAlpha(1f);
                return Task.CompletedTask;
            }

            return FadeAsync(from, 1f, hideWhenDone: false, cancellationToken);
        }

        /// <summary>
        /// 淡出并隐藏遮罩；开始淡出时 IsVisible 立即变为 false。
        /// 未运行（编辑模式）或对象不可用时直接隐藏，不启动逐帧循环。
        /// </summary>
        public Task HideAsync()
        {
            _targetAlpha = 0f;
            CancelFade();

            if (!gameObject.activeSelf)
                return Task.CompletedTask;

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                ApplyGroupAlpha(0f);
                gameObject.SetActive(false);
                return Task.CompletedTask;
            }

            return FadeAsync(_canvasGroup.alpha, 0f, hideWhenDone: true, CancellationToken.None);
        }

        /// <summary>设置点击遮罩的回调。传 null 表示点击不做事（但仍会挡住点击）。</summary>
        public void SetClickHandler(Action onClick)
        {
            _onClick = onClick;
        }

        /// <summary>点击遮罩时触发。是否真正关闭弹窗由 UIManager 判断配置。</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (_onClick != null)
                _onClick();
        }

        /// <summary>逐帧插值 CanvasGroup.alpha；被新的 Show/Hide 打断时由版本号提前退出。</summary>
        private async Task FadeAsync(float from, float to, bool hideWhenDone, CancellationToken cancellationToken)
        {
            int version = ++_fadeVersion;
            _fadeCts = new CancellationTokenSource();

            float elapsed = 0f;
            ApplyGroupAlpha(from);

            try
            {
                while (elapsed < FadeDuration)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 每帧推进：Task.Yield 回到主线程同步上下文，和协程一样按帧执行。
                    await Task.Yield();

                    if (version != _fadeVersion)
                        return;

                    elapsed += Time.unscaledDeltaTime;
                    ApplyGroupAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / FadeDuration)));
                }

                if (version != _fadeVersion)
                    return;

                ApplyGroupAlpha(to);
                if (hideWhenDone)
                    gameObject.SetActive(false);
            }
            catch (OperationCanceledException)
            {
                // 被新的 Show/Hide 接管：什么都不做，状态由接管方负责。
            }
            finally
            {
                if (version == _fadeVersion)
                {
                    _fadeCts.Dispose();
                    _fadeCts = null;
                }
            }
        }

        /// <summary>取消正在进行的淡入淡出：版本号自增让旧循环退出，同时取消它挂着的令牌。</summary>
        private void CancelFade()
        {
            _fadeVersion++;
            if (_fadeCts == null)
                return;

            _fadeCts.Cancel();
            _fadeCts.Dispose();
            _fadeCts = null;
        }

        /// <summary>把 fade 进度写到 CanvasGroup；组件缺失时补一次，避免空引用打断关闭流程。</summary>
        private void ApplyGroupAlpha(float alpha)
        {
            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();

            if (_canvasGroup != null)
                _canvasGroup.alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>对象被外部隐藏时停止淡入淡出，避免循环继续操作不可见对象。</summary>
        private void OnDisable()
        {
            CancelFade();
        }

        /// <summary>
        /// 把 RectTransform 拉伸到铺满父节点：遮罩必须盖住全屏，否则背后的按钮仍可能被点到。
        /// </summary>
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
