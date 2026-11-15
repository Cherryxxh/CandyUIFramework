using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 示例面板：用来验证 M1 的"加载 -> 实例化 -> 生命周期"是否跑通。
    ///
    /// 注意：这里的 UI 是代码搭出来的，只是为了让 Demo 不依赖美术资源。
    /// 正式项目里界面应该做成 Prefab（美术/策划可在 Editor 里摆），
    /// 脚本只持 [SerializeField] 控件引用 —— 这是 M3 视图/逻辑分离要讲的。
    /// </summary>
    [UIPanel("CandyUI/Demo/HelloPanel", UILayer.Main)]
    public sealed class HelloPanel : UIPanel
    {
        private TMP_Text _stateText;
        private int _showCount;

        /// <summary>
        /// M1 生命周期示例：OnCreate 只在实例创建时执行一次，适合建控件。
        /// </summary>
        protected override void OnCreate()
        {
            // OnCreate 只执行一次：适合"找控件、建列表、注册全局事件"。
            Debug.Log("[CandyUI] HelloPanel.OnCreate —— 只执行一次");
            BuildView();
        }

        /// <summary>
        /// 每次显示都执行；把层、状态和第几次显示写到文本上，直观看到生命周期。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            // OnShow 每次打开都会执行：适合"根据数据刷新界面"。
            _showCount++;
            _stateText.text =
                $"当前层: {Layer}\n" +
                $"生命周期状态: {State}\n" +
                $"第 {_showCount} 次 OnShow\n" +
                $"data: {(data == null ? "null" : data.GetType().Name)}";
        }

        /// <summary>
        /// 隐藏时只打日志：面板没被销毁，再次显示还会走 OnShow。
        /// </summary>
        protected override void OnHide()
        {
            Debug.Log("[CandyUI] HelloPanel.OnHide —— 隐藏但没销毁");
        }

        /// <summary>
        /// 关闭（销毁）前打日志，用来确认实例和资源被回收。
        /// </summary>
        protected override void OnClose()
        {
            Debug.Log("[CandyUI] HelloPanel.OnClose —— 即将销毁");
        }

        /// <summary>
        /// 用代码搭演示面板（正式项目应换成 Prefab 加序列化控件引用）。
        /// </summary>
        private void BuildView()
        {
            Image background = CreateImage("Background", transform, new Color(0.05f, 0.06f, 0.09f, 0.75f));
            Stretch(background.rectTransform);

            Image card = CreateImage("Card", transform, new Color(0.15f, 0.17f, 0.22f, 0.98f));
            RectTransform cardRect = card.rectTransform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(760f, 460f);
            cardRect.anchoredPosition = Vector2.zero;

            TMP_Text title = CreateText("Title", cardRect, "CandyUI · Hello Panel", 40, TextAnchor.MiddleCenter);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(700f, 60f);
            titleRect.anchoredPosition = new Vector2(0f, 150f);

            _stateText = CreateText("State", cardRect, string.Empty, 28, TextAnchor.MiddleCenter);
            RectTransform stateRect = _stateText.rectTransform;
            stateRect.anchorMin = new Vector2(0.5f, 0.5f);
            stateRect.anchorMax = new Vector2(0.5f, 0.5f);
            stateRect.sizeDelta = new Vector2(700f, 150f);
            stateRect.anchoredPosition = new Vector2(0f, 20f);

            TMP_Text hint = CreateText("Hint", cardRect, "按 Esc 也能关闭", 20, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.45f);
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.sizeDelta = new Vector2(700f, 32f);
            hintRect.anchoredPosition = new Vector2(0f, -70f);

            CreateButton("ReopenButton", cardRect, "再打开一次（复用实例）",
                new Vector2(-170f, -160f), new Color(0.22f, 0.42f, 0.78f, 1f), Reopen);
            CreateButton("CloseButton", cardRect, "关闭",
                new Vector2(170f, -160f), new Color(0.55f, 0.25f, 0.28f, 1f), CloseSelf);
        }

        /// <summary>
        /// 再次打开同一个面板：不会重新加载 Prefab，只会再走一次 OnShow。
        /// </summary>
        private async void Reopen()
        {
            try
            {
                // 面板已经打开：不会重新加载 Prefab，只会再走一次 OnShow。
                await UIManager.OpenPlainPanelAsync<HelloPanel>(new UIPanelData());
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 本类内部的建 Image 小工具；故意不依赖 DemoUi，让这个示例自包含。
        /// </summary>
        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>
        /// 本类内部的建 TMP 文本小工具，字体用 TMP Settings 的默认字体资源。
        /// </summary>
        private static TMP_Text CreateText(string name, Transform parent, string content, int fontSize,
            TextAnchor anchor)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TMP_Text text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = ToTextAlignment(anchor);
            text.color = Color.white;
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        /// <summary>
        /// 本类内部的建按钮小工具：底图、文本和点击回调。
        /// </summary>
        private static Button CreateButton(string name, Transform parent, string label,
            Vector2 position, Color color, System.Action onClick)
        {
            Image image = CreateImage(name, parent, color);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(300f, 64f);
            rect.anchoredPosition = position;

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            TMP_Text text = CreateText("Label", rect, label, 24, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);

            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>
        /// 把 RectTransform 铺满父节点，让文本填满按钮或背景。
        /// </summary>
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 把旧版 TextAnchor 映射成 TMP 的对齐枚举，保持原有九宫格对齐外观。
        /// </summary>
        private static TextAlignmentOptions ToTextAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }
    }
}
