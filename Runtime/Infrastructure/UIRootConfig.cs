// 职责：UIRoot 的可配置项（渲染模式 / 相机 / 参考分辨率 / 匹配权重 / 排序 / 常驻 / EventSystem）与默认值、校验回退规则。
// 流程：调用方填字段（或拿 Default）→ UIRoot.Create 或 UIRoot.InitUIRoot 调 Validate → 用修正后的副本建 Canvas。
// 依赖：UnityEngine（RenderMode / Camera / Vector2 / Mathf）；不依赖框架其他类型。
// 限制：Validate 只修浮点/引用，不改排序与常驻；世界空间模式同样要求 WorldCamera，缺相机一律回退 Overlay。
using System;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// UI 根节点（UIRoot）的可配置项。
    /// 默认值等价于改造前写死的 ScreenSpaceOverlay + 1920x1080 + match 0.5 + 跨场景常驻；
    /// 用 UIRoot.Create(config) 程序化创建，或把 UIRoot 组件放进场景后在 Inspector 里改字段。
    /// 注意：EditMode 下 Application.isPlaying 为 false，常驻（DontDestroyOnLoad）会被跳过，这是预期行为。
    /// </summary>
    [Serializable]
    public struct UIRootConfig
    {
        /// <summary>根 Canvas 的渲染模式；默认 ScreenSpaceOverlay。</summary>
        public RenderMode RenderMode;

        /// <summary>ScreenSpaceCamera / WorldSpace 使用的渲染相机；Overlay 模式忽略，其他模式为必填。</summary>
        public Camera WorldCamera;

        /// <summary>CanvasScaler 的参考分辨率；任一维小于等于 0 时回退到 1920x1080。</summary>
        public Vector2 ReferenceResolution;

        /// <summary>CanvasScaler 的宽高匹配权重；越界会 clamp 到 [0,1]，默认 0.5。</summary>
        public float MatchWidthOrHeight;

        /// <summary>根 Canvas 的 sortingOrder，用于和场景里其他 Canvas 排序；默认 0。</summary>
        public int SortingOrder;

        /// <summary>是否跨场景常驻（DontDestroyOnLoad）；默认 true，只在运行时生效。</summary>
        public bool DontDestroyOnLoad;

        /// <summary>场景里没有 EventSystem 时是否自动创建；默认 true，关闭后 UI 将无法点击与导航。</summary>
        public bool CreateEventSystemIfMissing;

        /// <summary>根节点 GameObject 名称；空字符串回退到 "[CandyUI] UIRoot"。</summary>
        public string Name;

        /// <summary>推荐默认值：Overlay、1920x1080、match 0.5、常驻、缺 EventSystem 时自动创建。</summary>
        public static UIRootConfig Default
        {
            get
            {
                return new UIRootConfig
                {
                    RenderMode = RenderMode.ScreenSpaceOverlay,
                    WorldCamera = null,
                    ReferenceResolution = new Vector2(1920f, 1080f),
                    MatchWidthOrHeight = 0.5f,
                    SortingOrder = 0,
                    DontDestroyOnLoad = true,
                    CreateEventSystemIfMissing = true,
                    Name = "[CandyUI] UIRoot",
                };
            }
        }

        /// <summary>
        /// 校验并修正非法配置：分辨率回退 1920x1080、match 钳制到 [0,1]、空名补默认名、
        /// 非 Overlay 缺相机回退 Overlay 并给出警告。返回修正后的副本，warning 为 null 表示无需提示。
        /// </summary>
        internal UIRootConfig Validate(out string warning)
        {
            warning = null;
            UIRootConfig config = this;

            if (config.ReferenceResolution.x <= 0f || config.ReferenceResolution.y <= 0f)
                config.ReferenceResolution = new Vector2(1920f, 1080f);

            config.MatchWidthOrHeight = Mathf.Clamp01(config.MatchWidthOrHeight);

            if (string.IsNullOrEmpty(config.Name))
                config.Name = "[CandyUI] UIRoot";

            // Overlay 不需要相机；ScreenSpaceCamera / WorldSpace 缺相机时 Canvas 无法正常渲染与拾取。
            if (config.RenderMode != RenderMode.ScreenSpaceOverlay && config.WorldCamera == null)
            {
                warning = $"配置要求 {config.RenderMode} 但没有指定 WorldCamera，已回退到 ScreenSpaceOverlay。";
                config.RenderMode = RenderMode.ScreenSpaceOverlay;
            }

            return config;
        }
    }
}
