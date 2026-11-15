using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// DOTween 动效演示界面。
    ///
    /// 演示四件事：
    /// 1. 面板转场：重写 CreateTransition，用 DOTweenTransition（比全局默认更弹）；
    /// 2. 数字滚动：金币 0 -> 125 -> 250，用 DOTween.To 插值；
    /// 3. 卡片 Punch：整张卡片弹一下（DOPunchScale）；
    /// 4. 序列动画：5 个方块逐个放大 + 淡入（DOTween.Sequence.Insert 控制错峰）。
    ///
    /// 所有按钮还统一挂了 TweenPressButton（按下去缩小、松手弹回）。
    ///
    /// 注意：这是"业务层怎么用 DOTween"的示例，不是框架核心；
    /// 正式项目里界面自己动效都这么写，框架不参与。
    /// </summary>
    [UIPanel("CandyUI/Demo/TweenDemoWindow", UILayer.Window)]
    public sealed class TweenDemoWindow : UIPanel
    {
        #region 字段与状态

        private RectTransform _card;
        private TMP_Text _goldText;
        private Image[] _squares;

        /// <summary>逻辑金币数（每次点 +125）。</summary>
        private int _gold;

        /// <summary>当前正在显示的金币数；用 float 才能平滑滚动。</summary>
        private float _displayGold;

        #endregion

        #region 创建界面

        /// <summary>
        /// 面板创建时搭一次界面：卡片、金币文本、5 个方块和 4 个按钮；之后只刷新文本和做动效。
        /// </summary>
        protected override void OnCreate()
        {
            _card = DemoUi.CreateCard(transform, new Vector2(1120f, 820f),
                new Color(0.10f, 0.13f, 0.20f, 0.98f));

            DemoUi.CreateTitle(_card, "DOTween 动效演示", 54, 340);

            TMP_Text hint = DemoUi.CreateText("Hint", _card,
                "金币滚动 / 卡片 Punch / 序列逐个出现 / 按钮按压缩放", 22, TextAnchor.MiddleCenter);
            hint.color = new Color(0.75f, 0.85f, 1f, 1f);
            hint.rectTransform.sizeDelta = new Vector2(1000f, 40f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 278f);

            _goldText = DemoUi.CreateText("Gold", _card, "0", 46, TextAnchor.MiddleCenter);
            _goldText.color = new Color(1f, 0.86f, 0.45f, 1f);
            _goldText.fontStyle = FontStyles.Bold;
            _goldText.rectTransform.sizeDelta = new Vector2(700f, 80f);
            _goldText.rectTransform.anchoredPosition = new Vector2(0f, 205f);

            CreateSquares();

            DemoUi.CreateButton("AddGold", _card, "金币 +125", new Vector2(-320f, -70f),
                new Vector2(280f, 84f), new Color(0.24f, 0.45f, 0.78f, 1f), () => TweenGold(125));

            DemoUi.CreateButton("Punch", _card, "弹一下", new Vector2(0f, -70f),
                new Vector2(280f, 84f), new Color(0.28f, 0.62f, 0.42f, 1f), PunchCard);

            DemoUi.CreateButton("Stagger", _card, "逐个出现", new Vector2(320f, -70f),
                new Vector2(280f, 84f), new Color(0.72f, 0.50f, 0.18f, 1f), PlayStagger);

            DemoUi.CreateButton("Close", _card, "关闭", new Vector2(0f, -290f),
                new Vector2(300f, 84f), new Color(0.55f, 0.22f, 0.22f, 1f), () => CloseSelf());
        }

        /// <summary>创建 5 个彩色方块，并排居中；初始 alpha 0.15、scale 0.6（等"逐个出现"按钮把它们播出来）。</summary>
        private void CreateSquares()
        {
            _squares = new Image[5];
            Color[] colors =
            {
                new Color(0.91f, 0.30f, 0.24f, 1f),
                new Color(0.95f, 0.61f, 0.16f, 1f),
                new Color(0.94f, 0.85f, 0.25f, 1f),
                new Color(0.30f, 0.75f, 0.45f, 1f),
                new Color(0.27f, 0.56f, 0.85f, 1f),
            };

            const float size = 128f;
            const float gap = 20f;
            float total = 5 * size + 4 * gap;
            float x = -total * 0.5f + size * 0.5f;

            for (int i = 0; i < _squares.Length; i++)
            {
                Color color = colors[i];
                color.a = 0.15f;

                Image square = DemoUi.CreateImage("Square_" + i, _card, color);
                RectTransform rect = square.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = new Vector2(x, 80f);
                square.transform.localScale = Vector3.one * 0.6f;

                _squares[i] = square;
                x += size + gap;
            }
        }

        #endregion

        #region 转场

        /// <summary>
        /// 这个面板单独用 DOTween 转场，覆盖 UIManager.DefaultTransition。
        /// 时长更长、起始缩得更小、OutBack 更弹，用于对比默认 FadeScaleTransition。
        /// </summary>
        protected override IPanelTransition CreateTransition()
        {
            return new DOTweenTransition
            {
                InDuration = 0.38f,
                OutDuration = 0.22f,
                InStartScale = 0.86f,
                InEase = Ease.OutBack,
            };
        }

        #endregion

        #region 动效演示

        /// <summary>
        /// 数字滚动：用 DOTween.To 把 _displayGold 从当前值插到目标值，
        /// 每帧回调里四舍五入后写到 TMP 文本上。
        /// 结束后再精确写一次，避免浮点误差（比如最后显示 124）。
        /// </summary>
        private void TweenGold(int amount)
        {
            _gold += amount;
            int target = _gold;

            DOTween.To(() => _displayGold, value =>
                {
                    _displayGold = value;
                    _goldText.text = Mathf.RoundToInt(value).ToString();
                }, target, 0.45f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true) // 不受 timeScale 影响
                .OnComplete(() =>
                {
                    _displayGold = target;
                    _goldText.text = target.ToString();
                });
        }

        /// <summary>
        /// 整张卡片弹一下：DOPunchScale 会自动"放大再回来"，
        /// 后两个参数是振动次数与衰减（8 次、0.8）。
        /// 先 DOKill 防止连点叠加多个 Tween。
        /// </summary>
        private void PunchCard()
        {
            _card.DOKill();
            _card.DOPunchScale(Vector3.one * 0.05f, 0.35f, 8, 0.8f)
                .SetUpdate(true);
        }

        /// <summary>
        /// 序列动画：一个 Sequence 里用 Insert(delay, tween) 把 5 个方块错峰 0.07 秒，
        /// 每个方块同时做 scale 0.6→1（OutBack）和 alpha 0.15→1。
        /// 这就是"抽卡/奖励逐个弹出来"的常用写法。
        /// </summary>
        private void PlayStagger()
        {
            Sequence sequence = DOTween.Sequence().SetUpdate(true);

            for (int i = 0; i < _squares.Length; i++)
            {
                Image square = _squares[i];
                square.transform.DOKill();

                Color from = new Color(square.color.r, square.color.g, square.color.b, 0.15f);
                Color to = new Color(from.r, from.g, from.b, 1f);

                square.color = from;
                square.transform.localScale = Vector3.one * 0.6f;

                float delay = i * 0.07f;
                sequence.Insert(delay,
                    square.transform.DOScale(Vector3.one, 0.30f).SetEase(Ease.OutBack));
                sequence.Insert(delay,
                    DOTween.To(() => square.color, value => square.color = value, to, 0.25f));
            }
        }

        #endregion
    }
}
