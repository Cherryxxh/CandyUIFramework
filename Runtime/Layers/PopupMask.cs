using System;
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
    /// - 有弹窗显示 -> Show()；
    /// - 弹窗全部关闭 -> Hide()；
    /// - 多个弹窗排队 -> 保持显示，不重复叠加。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class PopupMask : MonoBehaviour, IPointerClickHandler
    {
        private Image _image;
        private Action _onClick;

        /// <summary>遮罩当前是否显示。</summary>
        public bool IsVisible
        {
            get { return gameObject.activeSelf; }
        }

        /// <summary>
        /// 由 UIRoot 调用：在 Mask 层容器下创建唯一的一张遮罩。
        /// 创建后默认隐藏，等第一个弹窗打开时再显示。
        /// </summary>
        internal static PopupMask Create(RectTransform parent)
        {
            GameObject go = new GameObject("PopupMask",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(PopupMask));

            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);

            PopupMask mask = go.GetComponent<PopupMask>();
            mask.Init();
            go.SetActive(false);
            return mask;
        }

        /// <summary>
        /// 补齐 Image 组件并设为黑色半透明、开启射线检测；Create 和 Show 发现组件缺失时都会调用。
        /// </summary>
        private void Init()
        {
            if (_image == null)
                _image = GetComponent<Image>();

            if (_image == null)
                _image = gameObject.AddComponent<Image>();

            // 黑色、半透明；具体透明度在 Show(alpha) 时由弹窗配置决定。
            _image.color = new Color(0f, 0f, 0f, 0.6f);

            // 关键：接收射线。没有它，点击会穿过遮罩落到下面的窗口/主界面上。
            _image.raycastTarget = true;
        }

        /// <summary>显示遮罩，并设置本次的透明度。</summary>
        public void Show(float alpha)
        {
            if (_image == null)
                Init();

            _image.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
            gameObject.SetActive(true);

            // 保证遮罩在同层里排最后（虽然 Mask 层只有它一个，属于防御性写法）。
            transform.SetAsLastSibling();
        }

        /// <summary>隐藏遮罩。</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
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
