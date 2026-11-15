using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// Demo 调试工具栏（独立 Canvas，不参与框架的窗口/弹窗管理）。
    ///
    /// 它解决三个问题：
    /// 1. 任意界面被关掉后，可以随时从工具栏重新打开（不用重进 Play）；
    /// 2. 实时显示框架状态：窗口栈、当前弹窗、弹窗队列、遮罩、缓存数量；
    /// 3. 把窗口/弹窗/Toast 各层的按钮放在一起，方便对比它们的行为。
    ///
    /// F1 显示/隐藏。它是调试工具，正式项目不需要，所以放在 Samples 而不是 Runtime。
    /// </summary>
    public sealed class DemoToolbar : MonoBehaviour
    {
        private static DemoToolbar s_instance;

        private Canvas _canvas;
        private TMP_Text _status;
        private float _refreshTimer;

        /// <summary>
        /// 懒创建常驻的调试工具栏（DontDestroyOnLoad）；重复调用返回同一实例。
        /// </summary>
        public static DemoToolbar Ensure()
        {
            if (s_instance != null)
                return s_instance;

            GameObject go = new GameObject("[CandyUI] DemoToolbar", typeof(RectTransform));
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<DemoToolbar>();
            s_instance.Build();
            return s_instance;
        }

        /// <summary>
        /// 单例兜底：场景里误放第二个工具栏时销毁自己，保证全局只有一个。
        /// </summary>
        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
        }

        /// <summary>
        /// 搭独立 Canvas（sortingOrder 1000）让工具栏盖在遮罩之上，再生成状态文本和按钮。
        /// </summary>
        private void Build()
        {
            // 独立的 Overlay Canvas：sortingOrder 比 UIRoot 的 Canvas 高，
            // 所以即使弹窗遮罩显示着，工具栏仍然在最上面、仍然可以点。
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            _status = DemoUi.CreateText("Status", transform, string.Empty, 20, TextAnchor.UpperLeft);
            _status.color = new Color(0.75f, 1f, 0.85f, 1f);
            RectTransform statusRect = _status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            statusRect.sizeDelta = new Vector2(1500f, 180f);
            statusRect.anchoredPosition = new Vector2(20f, -20f);

            Image bar = DemoUi.CreateImage("Bar", transform, new Color(0f, 0f, 0f, 0.75f));
            RectTransform barRect = bar.rectTransform;
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.sizeDelta = new Vector2(1560f, 124f);
            barRect.anchoredPosition = new Vector2(0f, 14f);

            string[] labels = { "主城", "设置", "邮件", "背包", "商店", "动效", "确认框", "奖励", "Toast", "返回", "关闭全部" };
            Action[] actions =
            {
                OpenHome, OpenSettings, OpenMail, OpenBag, OpenShop, OpenTweenDemo, OpenConfirm,
                OpenReward, OpenToast, NavigateBack, CloseAll,
            };

            const float width = 132f;
            const float height = 68f;
            const float gap = 6f;
            float total = labels.Length * width + (labels.Length - 1) * gap;
            float x = -total * 0.5f + width * 0.5f;

            for (int i = 0; i < labels.Length; i++)
            {
                CreateBarButton(barRect, labels[i], new Vector2(x, 0f), width, height, actions[i]);
                x += width + gap;
            }
        }

        /// <summary>
        /// 每 0.2 秒刷新一次框架状态快照；F1 切换显隐，用 unscaledDeltaTime 保证暂停时也能刷新。
        /// </summary>
        private void Update()
        {
            if (_status != null)
            {
                _refreshTimer -= Time.unscaledDeltaTime;
                if (_refreshTimer <= 0f)
                {
                    _refreshTimer = 0.2f;
                    _status.text = UIManager.GetDebugSnapshot()
                                   + "\nF1 显示/隐藏工具栏｜任何界面关掉后，都可以点上面的按钮重新打开";
                }
            }

            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame
                && _canvas != null)
            {
                _canvas.enabled = !_canvas.enabled;
            }
        }

        /// <summary>
        /// 销毁时清掉单例引用，方便重新 Play 或重建工具栏。
        /// </summary>
        private void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
        }

        /// <summary>
        /// 创建一个工具栏按钮（底图、文本、点击回调），样式统一在这里维护。
        /// </summary>
        private void CreateBarButton(RectTransform parent, string label, Vector2 position,
            float width, float height, Action onClick)
        {
            Image image = DemoUi.CreateImage("Btn_" + label, parent, new Color(0.20f, 0.32f, 0.48f, 1f));
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = position;

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            TMP_Text text = DemoUi.CreateText("Label", rect, label, 22, TextAnchor.MiddleCenter);
            DemoUi.Stretch(text.rectTransform);

            button.onClick.AddListener(() => onClick());
        }

        /// <summary>
        /// 打开主城窗口；用 Fire 包一层，避免 async 异常丢失。
        /// </summary>
        private void OpenHome()
        {
            Fire(() => UIManager.OpenWindowAsync<HomeWindow>());
        }

        /// <summary>
        /// 打开设置窗口，演示窗口入栈、主城被隐藏。
        /// </summary>
        private void OpenSettings()
        {
            Fire(() => UIManager.OpenWindowAsync<SettingsWindow>());
        }

        /// <summary>
        /// 打开邮件窗口，演示窗口栈继续加深。
        /// </summary>
        private void OpenMail()
        {
            Fire(() => UIManager.OpenWindowAsync<MailWindow>());
        }

        /// <summary>
        /// 打开背包并传 BagPanelData，演示“显示哪个页签由调用方决定”。
        /// </summary>
        private void OpenBag()
        {
            // 背包是 M3 示例：打开时传 BagPanelData，用来指定默认页签。
            Fire(() => UIManager.OpenWindowAsync<BagPanel>(new BagPanelData()));
        }

        /// <summary>
        /// 打开商店并传 ShopPanelData，演示更薄的 View 加 Presenter 承接逻辑。
        /// </summary>
        private void OpenShop()
        {
            // 商店也是 M3 示例：更小的 View，逻辑全在 ShopPresenter。
            Fire(() => UIManager.OpenWindowAsync<ShopPanel>(new ShopPanelData()));
        }

        /// <summary>
        /// 打开 DOTween 动效演示窗口。
        /// </summary>
        private void OpenTweenDemo()
        {
            // DOTween 动效示例：DOTween 转场 + 金币滚动 + 卡片 Punch + 方块序列错峰。
            Fire(() => UIManager.OpenWindowAsync<TweenDemoWindow>());
        }

        /// <summary>
        /// 以弹窗队列方式打开确认弹窗（带遮罩、挡住背景点击）。
        /// </summary>
        private void OpenConfirm()
        {
            Fire(() => UIManager.OpenPopupAsync<ConfirmPopup>());
        }

        /// <summary>
        /// 打开奖励弹窗；已有弹窗时会先排队，关闭上一个才显示。
        /// </summary>
        private void OpenReward()
        {
            Fire(() => UIManager.OpenPopupAsync<RewardPopup>());
        }

        /// <summary>
        /// 用 PlainPanel 方式打开 Toast，演示它不进窗口栈、也不进弹窗队列。
        /// </summary>
        private void OpenToast()
        {
            Fire(() => UIManager.OpenPlainPanelAsync<ToastPanel>());
        }

        /// <summary>
        /// 模拟 Esc 返回：有弹窗先关弹窗，否则关闭当前窗口并恢复上一个。
        /// </summary>
        private void NavigateBack()
        {
            UIManager.NavigateBack();
        }

        /// <summary>
        /// 关闭全部窗口和弹窗（ForceClose 不询问拦截），方便重来。
        /// </summary>
        private void CloseAll()
        {
            UIManager.CloseAll();
        }

        /// <summary>
        /// 统一执行异步打开操作并吞掉异常，async void 里的异常不会自动被捕获。
        /// </summary>
        private async void Fire(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                // async void 必须自己吞异常，否则异常会丢失。
                Debug.LogException(exception);
            }
        }
    }
}
