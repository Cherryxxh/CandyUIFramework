using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// Demo 界面的代码搭 UI 工具。
    ///
    /// 正式项目应该用 Prefab + [SerializeField] 引用控件（M3 视图/逻辑分离会讲），
    /// 这里用代码搭只是为了不依赖美术资源，让示例拷进工程就能直接跑。
    /// </summary>
    internal static class DemoUi
    {
        /// <summary>
        /// 用代码创建一个带 RectTransform 的 Image；Demo 不依赖美术资源，界面全部代码搭。
        /// </summary>
        public static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>
        /// 创建 TextMeshProUGUI 文本并设好字号、对齐和回退方式；字体用 TMP Settings
        /// 里的默认字体资源（LiberationSans SDF），不再手动取内置字体。
        /// </summary>
        public static TMP_Text CreateText(string name, Transform parent, string content, int fontSize,
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
        /// 创建默认尺寸按钮，内部转调带尺寸的重载。
        /// </summary>
        public static Button CreateButton(string name, Transform parent, string label,
            Vector2 position, Color color, System.Action onClick)
        {
            return CreateButton(name, parent, label, position, new Vector2(340f, 80f), color, onClick);
        }

        /// <summary>带尺寸的按钮：格子、页签这类小按钮用这个重载。</summary>
        public static Button CreateButton(string name, Transform parent, string label,
            Vector2 position, Vector2 size, Color color, System.Action onClick)
        {
            Image image = CreateImage(name, parent, color);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            // Demo 里所有按钮统一加"按压缩放"反馈（DOTween 示例）。
            image.gameObject.AddComponent<TweenPressButton>();

            TMP_Text text = CreateText("Label", rect, label, 26, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);

            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>创建一个居中的卡片，并返回它的 RectTransform。</summary>
        public static RectTransform CreateCard(Transform parent, Vector2 size, Color color)
        {
            Image card = CreateImage("Card", parent, color);
            RectTransform rect = card.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>创建一个标题文本，放到卡片顶部。</summary>
        public static TMP_Text CreateTitle(RectTransform card, string content, int fontSize, float y)
        {
            TMP_Text title = CreateText("Title", card, content, fontSize, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyles.Bold;
            RectTransform rect = title.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(card.sizeDelta.x - 80f, 70f);
            rect.anchoredPosition = new Vector2(0f, y);
            return title;
        }

        /// <summary>
        /// 把 RectTransform 铺满父节点（锚点 0~1、四边偏移归零）。
        /// </summary>
        public static void Stretch(RectTransform rect)
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
