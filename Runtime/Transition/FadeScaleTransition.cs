using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 默认转场：透明度 + 缩放（纯 Unity 实现，不依赖 DOTween）。
    ///
    /// 入场：alpha 0 → 1，scale 0.96 → 1，0.22 秒，easeOutCubic。
    /// 退场：alpha 1 → 0，scale 1 → 0.97，0.15 秒，easeInQuad。
    ///
    /// 面板无论是 Prefab 还是代码搭出来的，动画都作用在面板的根 RectTransform 上，
    /// 不会影响子节点布局。
    ///
    /// 用法（在启动脚本里设置一次，所有面板生效）：
    ///   UIManager.DefaultTransition = new FadeScaleTransition();
    /// 想调参数：
    ///   new FadeScaleTransition { InDuration = 0.3f, InStartScale = 0.9f }
    /// 某个面板想用别的效果：
    ///   protected override IPanelTransition CreateTransition() => new SlideTransition();
    ///
    /// 为什么不用 DOTween：Runtime 不依赖第三方插件，新工程拷进去就能跑。
    /// 想要更弹的效果，见 Samples/Demo/DOTweenTransition.cs（可选增强）。
    /// </summary>
    public sealed class FadeScaleTransition : IPanelTransition
    {
        #region 可调参数

        /// <summary>入场时长（秒）。</summary>
        public float InDuration = 0.22f;

        /// <summary>退场时长（秒）。</summary>
        public float OutDuration = 0.15f;

        /// <summary>入场时的起始缩放（1 表示不缩放）。</summary>
        public float InStartScale = 0.96f;

        /// <summary>退场结束时的缩放。</summary>
        public float OutEndScale = 0.97f;

        #endregion

        #region 接口实现

        /// <summary>入场：从"透明 + 略小"过渡到正常状态。</summary>
        public Task PlayInAsync(UIPanel panel, CancellationToken cancellationToken)
        {
            return Run(panel, true, cancellationToken);
        }

        /// <summary>退场：从正常状态过渡到"透明 + 略小"。</summary>
        public Task PlayOutAsync(UIPanel panel, CancellationToken cancellationToken)
        {
            return Run(panel, false, cancellationToken);
        }

        #endregion

        #region 协程实现

        /// <summary>
        /// 统一入口：准备 CanvasGroup / 初始状态，然后启动协程。
        /// 返回的 Task 在动画播完（或取消）时完成，UIPanel 用 await 等待它。
        /// </summary>
        private Task Run(UIPanel panel, bool isIn, CancellationToken cancellationToken)
        {
            // TaskCompletionSource：把"协程结束"这个事件桥接成 Task，
            // 这样调用方可以 await，而不需要关心协程。
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

            // 面板可能已经被销毁（例如加载中就被 CloseAll 了），这时直接取消即可。
            if (panel == null || panel.gameObject == null || cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled();
                return completion.Task;
            }

            // CanvasGroup 用来整体控制透明度与射线检测；
            // 没有就临时加一个，避免要求每个 Prefab 手动挂。
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group == null)
                group = panel.gameObject.AddComponent<CanvasGroup>();

            if (isIn)
            {
                // 从"透明 + 略小"开始；blocksRaycasts=false 防止动画期间被误点。
                group.alpha = 0f;
                group.blocksRaycasts = false;
                panel.transform.localScale = Vector3.one * InStartScale;
            }

            // 时长至少 0.01 秒，避免除零/瞬间完成导致状态错乱。
            float duration = Mathf.Max(0.01f, isIn ? InDuration : OutDuration);
            CancellationTokenRegistration registration =
                cancellationToken.Register(() => completion.TrySetCanceled());

            panel.StartCoroutine(Animate(panel, group, isIn, duration, registration, completion));
            return completion.Task;
        }

        /// <summary>逐帧插值：alpha 与 scale 同时变化。</summary>
        private IEnumerator Animate(
            UIPanel panel,
            CanvasGroup group,
            bool isIn,
            float duration,
            CancellationTokenRegistration registration,
            TaskCompletionSource<bool> completion)
        {
            float fromAlpha = group.alpha;
            float toAlpha = isIn ? 1f : 0f;
            Vector3 fromScale = panel.transform.localScale;
            Vector3 toScale = isIn ? Vector3.one : Vector3.one * OutEndScale;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                // 被取消（重复打开/关闭、面板销毁）时立刻停。
                if (completion.Task.IsCanceled)
                    yield break;

                // 用 unscaledDeltaTime：即使游戏 timeScale=0（暂停），界面转场也照常播。
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = isIn ? EaseOutCubic(t) : EaseInQuad(t);

                group.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
                panel.transform.localScale = Vector3.LerpUnclamped(fromScale, toScale, eased);
                yield return null;
            }

            if (completion.Task.IsCanceled)
                yield break;

            // 收尾：把值精确写到终点，避免浮点误差导致 alpha=0.999 之类的问题。
            group.alpha = toAlpha;
            panel.transform.localScale = toScale;
            // 入场结束后恢复点击；退场期间保持不可点。
            group.blocksRaycasts = isIn;

            registration.Dispose();
            completion.TrySetResult(true);
        }

        #endregion

        #region 缓动函数

        /// <summary>easeOutCubic：先快后慢，适合入场。</summary>
        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - inverse * inverse * inverse;
        }

        /// <summary>easeInQuad：先慢后快，适合退场。</summary>
        private static float EaseInQuad(float t)
        {
            return t * t;
        }

        #endregion
    }
}
