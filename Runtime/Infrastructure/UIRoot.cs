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
    /// 根节点参数现在可配置（UIRootConfig）：渲染模式、相机、参考分辨率、匹配权重、排序、是否常驻。
    /// 既可以在 Inspector 里改组件字段，也可以用 UIRoot.Create(config) 在代码里创建；
    /// 多套 UI 上下文时只有第一个 root 占据 Instance，其余 root 各自独立工作并打一条 Info。
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
        /// <summary>当前 UI 根节点：只由第一个完成初始化的 root 占据，供框架内部使用。</summary>
        internal static UIRoot Instance { get; private set; }

        /// <summary>根 Canvas。</summary>
        public Canvas Canvas { get; private set; }

        /// <summary>根 RectTransform。</summary>
        public RectTransform RootRect { get; private set; }

        /// <summary>当前生效的配置：由 Inspector 序列化字段（以及根节点名）构造并校验后的副本。</summary>
        public UIRootConfig Config
        {
            get { return BuildConfig(out _); }
        }

        /// <summary>弹窗遮罩（第一次打开弹窗时创建，业务代码一般不需要直接操作）。</summary>
        internal PopupMask Mask { get; private set; }

        [Header("根 Canvas")]
        [Tooltip("根 Canvas 的渲染模式；默认 ScreenSpaceOverlay，选 ScreenSpaceCamera / WorldSpace 时必须配 WorldCamera。")]
        [SerializeField] private RenderMode _renderMode = RenderMode.ScreenSpaceOverlay;

        [Tooltip("ScreenSpaceCamera / WorldSpace 使用的渲染相机；Overlay 模式忽略，其他模式缺失会回退 Overlay 并警告。")]
        [SerializeField] private Camera _worldCamera;

        [Header("适配")]
        [Tooltip("CanvasScaler 参考分辨率；默认 1920x1080，任一维小于等于 0 时回退到默认值。")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("CanvasScaler 宽高匹配权重 [0,1]；默认 0.5，越界会自动 clamp。")]
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;

        [Header("排序与生命周期")]
        [Tooltip("根 Canvas 的 sortingOrder，用于和场景里其他 Canvas 排序；默认 0。")]
        [SerializeField] private int _sortingOrder;

        [Tooltip("是否跨场景常驻（DontDestroyOnLoad）；默认开启，只在运行时生效。")]
        [SerializeField] private bool _dontDestroyOnLoad = true;

        [Tooltip("场景里没有 EventSystem 时是否自动创建；默认开启，关闭后 UI 将无法点击与导航。")]
        [SerializeField] private bool _createEventSystemIfMissing = true;

        private readonly Dictionary<UILayer, RectTransform> _dicLayerRoots =
            new Dictionary<UILayer, RectTransform>();

        private bool _isSetup;

        #region 创建与生命周期

        /// <summary>拿到 UI 根节点；没有就按 UIRootConfig.Default 新建，业务代码一般不需要直接调用。</summary>
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

            return Create(UIRootConfig.Default);
        }

        /// <summary>场景里手动挂载时在 Awake 阶段初始化根节点；与动态创建路径共用幂等的 InitUIRoot。</summary>
        private void Awake()
        {
            InitUIRoot();
        }

        /// <summary>
        /// 按配置创建并初始化一个独立 UIRoot：先关激活再挂组件写字段，保证 Awake 读到的配置完整。
        /// 第一个完成初始化的 root 占据 Instance，后续 root 只打 Info；EditMode 不触发 Awake，故补一次 InitUIRoot。
        /// </summary>
        public static UIRoot Create(UIRootConfig config)
        {
            config = config.Validate(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning("[CandyUI] " + warning);

            GameObject go = new GameObject(config.Name, typeof(RectTransform));
            go.SetActive(false);
            UIRoot root = go.AddComponent<UIRoot>();
            root.ApplyConfig(config);
            go.SetActive(true);

            // Play 模式下上面的 SetActive 已触发 Awake 完成初始化；EditMode（编辑器/EditMode 测试）不会触发 Awake，这里补一次。
            root.InitUIRoot();
            return root;
        }

        /// <summary>
        /// 初始化根节点（幂等）：应用配置、建 Canvas/EventSystem/层容器。
        /// Instance 只由第一个初始化的 root 占据；后续 root 仍会完成自身初始化，但不覆盖 Instance、也不再销毁自己。
        /// </summary>
        internal void InitUIRoot()
        {
            if (_isSetup)
                return;

            _isSetup = true;
            RootRect = (RectTransform)transform;

            UIRootConfig config = BuildConfig(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning("[CandyUI] " + warning);

            if (Instance == null)
                Instance = this;
            else if (Instance != this)
                Debug.Log($"[CandyUI] 检测到额外的 UIRoot（多套 UI 上下文）：{name}");

            if (transform.parent == null && Application.isPlaying && config.DontDestroyOnLoad)
                Object.DontDestroyOnLoad(gameObject);

            SetupCanvas();
            EnsureEventSystem(config.CreateEventSystemIfMissing);
            BuildLayerRoots();
        }

        /// <summary>把配置写进序列化字段，供 Awake/InitUIRoot 读取；只在 Create 内部调用。</summary>
        private void ApplyConfig(UIRootConfig config)
        {
            _renderMode = config.RenderMode;
            _worldCamera = config.WorldCamera;
            _referenceResolution = config.ReferenceResolution;
            _matchWidthOrHeight = config.MatchWidthOrHeight;
            _sortingOrder = config.SortingOrder;
            _dontDestroyOnLoad = config.DontDestroyOnLoad;
            _createEventSystemIfMissing = config.CreateEventSystemIfMissing;
        }

        /// <summary>由序列化字段（以及根节点名）构造配置并校验；warning 交给调用方统一打日志。</summary>
        private UIRootConfig BuildConfig(out string warning)
        {
            UIRootConfig config = new UIRootConfig
            {
                RenderMode = _renderMode,
                WorldCamera = _worldCamera,
                ReferenceResolution = _referenceResolution,
                MatchWidthOrHeight = _matchWidthOrHeight,
                SortingOrder = _sortingOrder,
                DontDestroyOnLoad = _dontDestroyOnLoad,
                CreateEventSystemIfMissing = _createEventSystemIfMissing,
                Name = gameObject.name,
            };
            return config.Validate(out warning);
        }

        #endregion

        #region 画布与层

        /// <summary>按 Config 配置根 Canvas/CanvasScaler/GraphicRaycaster；缺失组件自动补挂，已有组件不强改。</summary>
        private void SetupCanvas()
        {
            UIRootConfig config = Config;

            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();

            canvas.renderMode = config.RenderMode;
            // Overlay 模式不需要相机；显式置 null，避免 Inspector 里残留的旧引用让 Canvas 处于半配置状态。
            canvas.worldCamera = config.RenderMode == RenderMode.ScreenSpaceOverlay ? null : config.WorldCamera;
            canvas.sortingOrder = config.SortingOrder;
            canvas.pixelPerfect = false;
            Canvas = canvas;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = config.ReferenceResolution;
            scaler.matchWidthOrHeight = config.MatchWidthOrHeight;

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
        /// 保证场景里有 EventSystem（否则 Button 点不动）：已有就复用并补齐新版输入模块；
        /// createIfMissing 为 false 且场景里没有时只打一条 Info，不创建（本工程只用新 Input System）。
        /// </summary>
        private static void EnsureEventSystem(bool createIfMissing)
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

            if (!createIfMissing)
            {
                Debug.Log("[CandyUI] 场景中没有 EventSystem，且配置要求不自动创建，UI 点击/导航将不可用。");
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
