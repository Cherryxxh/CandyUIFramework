using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CandyUI
{
    /// <summary>
    /// UI 根节点：整个界面系统的"地基"，负责创建 Canvas、适配、层级容器。
    ///
    /// 为什么需要一个自动创建的 Root？
    /// 新手项目最常见的做法是每个场景手动摆一个 Canvas。结果：
    /// - 切场景时 UI 被销毁，正在播放的界面断掉；
    /// - 不同场景 Canvas 参数不一致，UI 忽大忽小；
    /// - 层级顺序靠手调 SiblingIndex，容易乱。
    /// CandyUI 的做法：第一次打开界面时自动创建 [CandyUI] UIRoot，
    /// 设置 DontDestroyOnLoad，所有界面都挂到它的层容器下。切场景 UI 不会消失，
    /// 适配参数只维护一份。
    ///
    /// 层级结构（兄弟节点顺序即渲染顺序，越往下越靠上）：
    /// [CandyUI] UIRoot (Canvas + CanvasScaler + GraphicRaycaster)
    ///   ├─ Background
    ///   ├─ Main
    ///   ├─ Window
    ///   ├─ Mask      （弹窗遮罩，只有一张 Image）
    ///   ├─ Popup
    ///   ├─ Toast
    ///   └─ Top
    ///
    /// 常见游戏例子：主城界面(Main) 上打开背包(Window)，背包上弹"是否出售"确认框(Popup)，
    /// 屏幕顶部再飘一行"获得金币"(Toast)，最上面盖一个全屏 Loading(Top)。
    /// 它们各自的父子关系固定，永远不用担心谁盖住谁。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        /// <summary>当前 UI 根节点。没有时用 EnsureUIRoot() 创建（框架内部）。</summary>
        internal static UIRoot Instance { get; private set; }

        /// <summary>根 Canvas。</summary>
        public Canvas Canvas { get; private set; }

        /// <summary>根 RectTransform。</summary>
        public RectTransform RootRect { get; private set; }

        /// <summary>弹窗遮罩（第一次打开弹窗时创建，业务代码一般不需要直接操作）。</summary>
        internal PopupMask Mask { get; private set; }

        private readonly Dictionary<UILayer, RectTransform> _dicLayerRoots =
            new Dictionary<UILayer, RectTransform>();

        private bool _isSetup;

        #region 创建与生命周期

        /// <summary>
        /// 拿到 UI 根节点；没有就创建一个。
        /// 所有界面打开前都会调用它，业务代码一般不需要直接调用。
        /// </summary>
        internal static UIRoot EnsureUIRoot()
        {
            if (Instance != null)
                return Instance;

            // 场景里可能已经手动放了 UIRoot（比如美术同学摆好的），优先复用它。
            UIRoot[] roots = Object.FindObjectsOfType<UIRoot>(true);
            if (roots.Length > 0)
            {
                roots[0].InitUIRoot();
                return roots[0];
            }

            GameObject go = new GameObject("[CandyUI] UIRoot", typeof(RectTransform));
            Object.DontDestroyOnLoad(go);
            UIRoot root = go.AddComponent<UIRoot>();
            root.InitUIRoot();
            return root;
        }

        /// <summary>
        /// 场景里手动挂载时在 Awake 阶段初始化根节点；与动态创建路径共用幂等的 InitUIRoot。
        /// </summary>
        private void Awake()
        {
            InitUIRoot();
        }

        /// <summary>
        /// 初始化根节点。做成幂等的：重复调用不会重复建 Canvas/层容器。
        /// </summary>
        internal void InitUIRoot()
        {
            if (_isSetup)
                return;

            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CandyUI] 场景里存在多个 UIRoot，已销毁重复的那个。");
                Destroy(gameObject);
                return;
            }

            _isSetup = true;
            Instance = this;
            RootRect = (RectTransform)transform;

            if (transform.parent == null && Application.isPlaying)
                Object.DontDestroyOnLoad(gameObject);

            SetupCanvas();
            EnsureEventSystem();
            BuildLayerRoots();
        }

        #endregion

        #region 画布与层


        /// <summary>
        /// 配置根 Canvas/CanvasScaler/GraphicRaycaster：参考分辨率 1920x1080、match=0.5，横竖屏都不易变形，缺失组件自动补挂。
        /// </summary>
        private void SetupCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            Canvas = canvas;

            // CanvasScaler：让 UI 在不同分辨率下按比例缩放。
            // 1920x1080 是 PC/竖屏手游常见的参考分辨率；match=0.5 表示
            // 宽高变化各占一半权重，横竖屏都不至于变形太厉害。
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        /// <summary>
        /// 创建每个层对应的容器节点。容器本身不渲染任何东西，只是用来排序。
        /// </summary>
        private void BuildLayerRoots()
        {
            _dicLayerRoots.Clear();

            // Enum.GetValues 按枚举数值升序返回，正好等于从下往上。
            // 后创建的是后面的兄弟节点，在 UGUI 里会渲染在上面，顺序刚好正确。
            foreach (UILayer layer in System.Enum.GetValues(typeof(UILayer)))
            {
                Transform existing = transform.Find(layer.ToString());
                RectTransform rect;

                if (existing != null && existing is RectTransform existingRect)
                {
                    rect = existingRect;
                }
                else
                {
                    GameObject go = new GameObject(layer.ToString(), typeof(RectTransform));
                    rect = (RectTransform)go.transform;
                    rect.SetParent(RootRect, false);
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    rect.localScale = Vector3.one;
                }

                _dicLayerRoots[layer] = rect;
            }
        }

        /// <summary>拿到某一层的容器，面板会被实例化到它下面（框架内部）。</summary>
        internal RectTransform GetLayerRoot(UILayer layer)
        {
            if (_dicLayerRoots.TryGetValue(layer, out RectTransform rect))
                return rect;

            // 防御：可能有人先 GetLayerRoot 再 InitUIRoot。
            InitUIRoot();
            if (_dicLayerRoots.TryGetValue(layer, out rect))
                return rect;

            Debug.LogError($"[CandyUI] 找不到层 {layer} 的容器，回退到 UIRoot 根节点。");
            return RootRect;
        }

        #endregion

        #region 弹窗遮罩


        /// <summary>
        /// 拿到弹窗遮罩；没有就在 Mask 层创建唯一的一张。
        /// UIManager 第一次打开弹窗时调用，业务代码不需要直接用它。
        /// </summary>
        internal PopupMask EnsurePopupMask()
        {
            if (Mask != null)
                return Mask;

            RectTransform maskRoot = GetLayerRoot(UILayer.Mask);
            Mask = PopupMask.Create(maskRoot);
            return Mask;
        }

        #endregion

        #region 输入


        /// <summary>
        /// 保证场景里有 EventSystem，否则 Button 点不动。
        ///
        /// 本项目只使用新版 Input System（Project Settings → Active Input Handling =
        /// Input System Package (New)），所以统一挂 InputSystemUIInputModule。
        ///
        /// 注意：代码创建组件不会走 Inspector 的 Reset()，必须手动
        /// AssignDefaultActions()，否则点按/导航/提交这些 UI 动作是空的，
        /// 界面照样点不动。
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                EnsureInputModule(EventSystem.current.gameObject);
                return;
            }

            EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
            if (systems.Length > 0)
            {
                EnsureInputModule(systems[0].gameObject);
                return;
            }

            GameObject go = new GameObject("[CandyUI] EventSystem", typeof(EventSystem));
            Object.DontDestroyOnLoad(go);
            EnsureInputModule(go);
        }

        /// <summary>
        /// 给 EventSystem 挂上新版输入模块，并清掉可能残留的旧 StandaloneInputModule。
        /// 注意：模块必须和 EventSystem 在同一个 GameObject 上。
        /// </summary>
        private static void EnsureInputModule(GameObject eventSystemGo)
        {
            // 旧模块每帧读 UnityEngine.Input，在新输入模式下会抛异常，必须先移除。
            // Destroy 帧末才生效，所以先 enabled = false 立即停止它的 Update。
            StandaloneInputModule legacyModule = eventSystemGo.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                legacyModule.enabled = false;
                Object.Destroy(legacyModule);
            }

            InputSystemUIInputModule module = eventSystemGo.GetComponent<InputSystemUIInputModule>();
            if (module == null)
                module = eventSystemGo.AddComponent<InputSystemUIInputModule>();

            // 绑定默认 UI 动作（Point/Click/Navigate/Submit/Cancel/ScrollWheel 等）。
            module.AssignDefaultActions();
        }

        #endregion

    }
}
