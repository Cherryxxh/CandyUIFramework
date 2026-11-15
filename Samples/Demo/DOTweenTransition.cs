using System.Threading;
using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace CandyUI.Demo
{
    /// <summary>
    /// DOTween 版界面转场（示例，放在 Samples 里，不进入 Runtime）。
    ///
    /// 为什么不做进框架核心？
    ///   Runtime 不依赖任何第三方插件，拷到新工程就能用；
    ///   DOTween 只是"可选的增强"，想用就换这一行：
    ///     UIManager.DefaultTransition = new DOTweenTransition();
    ///
    /// 和 FadeScaleTransition 的区别：
    ///   - 用 DOTween.Sequence 组合动画，缓动可以随便换（OutBack / OutBounce ...）；
    ///   - 想加位移、旋转、来回弹，只要往 Sequence 里 Join 就行；
    ///   - 代价是多一个插件依赖。
    /// </summary>
    public sealed class DOTweenTransition : IPanelTransition
    {
        #region 可调参数

        public float InDuration = 0.28f;
        public float OutDuration = 0.18f;
        public float InStartScale = 0.92f;
        public float OutEndScale = 0.97f;

        /// <summary>入场缓动（OutBack 会有轻微"回弹"）。</summary>
        public Ease InEase = Ease.OutBack;

        /// <summary>退场缓动。</summary>
        public Ease OutEase = Ease.InQuad;

        #endregion

        #region 进出场

        /// <summary>
        /// 入场：透明度 0→1 与缩放 0.92→1 同时进行（Sequence.Join）。
        /// SetUpdate(true) 表示不受 Time.timeScale 影响：暂停菜单里也能正常播。
        /// </summary>
        public async Task PlayInAsync(UIPanel panel, CancellationToken cancellationToken)
        {
            CanvasGroup group = EnsureCanvasGroup(panel);
            group.alpha = 0f;
            group.blocksRaycasts = false;
            panel.transform.localScale = Vector3.one * InStartScale;

            Sequence sequence = DOTween.Sequence()
                .Join(DOTween.To(() => group.alpha, value => group.alpha = value, 1f, InDuration)
                    .SetEase(InEase))
                .Join(panel.transform.DOScale(Vector3.one, InDuration).SetEase(InEase))
                .SetUpdate(true);

            try
            {
                await WaitAsync(sequence, cancellationToken);
            }
            finally
            {
                // 被取消（面板又被关掉了）时不要抢着复位，交给下一次进场处理。
                if (!cancellationToken.IsCancellationRequested)
                {
                    group.alpha = 1f;
                    group.blocksRaycasts = true;
                    panel.transform.localScale = Vector3.one;
                }
            }
        }

        /// <summary>退场：透明度 1→0 与缩放 1→0.97 同时进行；期间禁止点击。</summary>
        public async Task PlayOutAsync(UIPanel panel, CancellationToken cancellationToken)
        {
            CanvasGroup group = EnsureCanvasGroup(panel);
            group.blocksRaycasts = false;

            Sequence sequence = DOTween.Sequence()
                .Join(DOTween.To(() => group.alpha, value => group.alpha = value, 0f, OutDuration)
                    .SetEase(OutEase))
                .Join(panel.transform.DOScale(Vector3.one * OutEndScale, OutDuration).SetEase(OutEase))
                .SetUpdate(true);

            await WaitAsync(sequence, cancellationToken);
        }

        #endregion

        #region 工具方法

        /// <summary>面板没有 CanvasGroup 时临时补一个，用于整体透明度和射线控制。</summary>
        private static CanvasGroup EnsureCanvasGroup(UIPanel panel)
        {
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group == null)
                group = panel.gameObject.AddComponent<CanvasGroup>();
            return group;
        }

        /// <summary>
        /// 把 Tween 包成 Task：完成/被 Kill/被取消时都能返回，避免 await 永远不结束。
        /// 这是用 DOTween 接 async 代码时最容易踩的坑。
        ///
        /// 用 OnKill 兜底：外部如果直接 tween.Kill()（例如面板被销毁），
        /// Task 也必须完成，否则 UIPanel 的 async void 会永远挂在那里。
        /// </summary>
        private static async Task WaitAsync(Tween tween, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

            tween.OnComplete(() => completion.TrySetResult(true));
            tween.OnKill(() => completion.TrySetCanceled());

            if (cancellationToken.CanBeCanceled)
            {
                // 取消时先 Kill（会触发 OnKill 完成 Task）；如果 Tween 已经结束，
                // 就直接把 Task 标记为取消，保证 await 一定返回。
                using (cancellationToken.Register(() =>
                       {
                           if (tween.IsActive())
                               tween.Kill();
                           else
                               completion.TrySetCanceled();
                       }))
                {
                    await completion.Task;
                }

                return;
            }

            await completion.Task;
        }

        #endregion
    }
}
