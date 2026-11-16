// 职责：UIRootConfig / UIRoot.Create 的 EditMode 回归测试（渲染模式、相机、参考分辨率、排序、回退与 EventSystem 开关）。
// 依赖：UIRoot.Create 先校验配置再写序列化字段；EditMode 不触发 Awake，Create 内部会显式补一次 InitUIRoot。
// 限制：EditMode 下 Application.isPlaying=false，DontDestroyOnLoad 会被跳过；创建的 root / camera 必须在 TearDown 里 DestroyImmediate。
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CandyUI.Tests
{
    /// <summary>
    /// 验证 UIRoot 由写死参数改为可配置后：Create 能应用渲染模式/相机/参考分辨率/匹配权重/排序，
    /// 缺相机与非法分辨率按约定回退，且“不自动创建 EventSystem”的开关生效。
    /// </summary>
    public sealed class UIRootConfigTests : UITestBase
    {
        private readonly List<Object> _listCreated = new List<Object>();

        /// <summary>清理本用例创建的临时对象；断言失败时 TearDown 也会兜底执行。</summary>
        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (Object created in _listCreated)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            _listCreated.Clear();
        }

        /// <summary>屏幕空间相机模式：renderMode 与 worldCamera 都应应用配置值。</summary>
        [Test]
        public void Create_WithCameraRenderMode_AppliesRenderModeAndCamera()
        {
            Camera camera = CreateCamera();
            UIRootConfig config = UIRootConfig.Default;
            config.RenderMode = RenderMode.ScreenSpaceCamera;
            config.WorldCamera = camera;

            UIRoot root = CreateRoot(config);

            Assert.NotNull(root.Canvas);
            Assert.AreEqual(RenderMode.ScreenSpaceCamera, root.Canvas.renderMode);
            Assert.AreSame(camera, root.Canvas.worldCamera, "Canvas.worldCamera 应为配置的相机");
        }

        /// <summary>自定义参考分辨率与匹配权重：CanvasScaler 三项都应生效。</summary>
        [Test]
        public void Create_WithCustomReferenceResolution_AppliesScaler()
        {
            UIRootConfig config = UIRootConfig.Default;
            config.ReferenceResolution = new Vector2(1280f, 720f);
            config.MatchWidthOrHeight = 0.75f;

            UIRoot root = CreateRoot(config);
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();

            Assert.NotNull(scaler, "UIRoot 应自动补挂 CanvasScaler");
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(new Vector2(1280f, 720f), scaler.referenceResolution);
            Assert.AreEqual(0.75f, scaler.matchWidthOrHeight, 0.0001f);
        }

        /// <summary>排序号：根 Canvas 的 sortingOrder 应等于配置值。</summary>
        [Test]
        public void Create_WithSortingOrder_AppliesToCanvas()
        {
            UIRootConfig config = UIRootConfig.Default;
            config.SortingOrder = 500;

            UIRoot root = CreateRoot(config);

            Assert.NotNull(root.Canvas);
            Assert.AreEqual(500, root.Canvas.sortingOrder);
        }

        /// <summary>相机模式但未指定相机：打警告并回退到 ScreenSpaceOverlay。</summary>
        [Test]
        public void Create_CameraModeWithoutCamera_FallsBackToOverlay()
        {
            LogAssert.ignoreFailingMessages = false;
            // 正则同时兼容"整条必须匹配"和"子串匹配"两种实现：前后各加 .*。
            LogAssert.Expect(LogType.Warning, new Regex("^.*没有指定 WorldCamera.*$"));

            UIRootConfig config = UIRootConfig.Default;
            config.RenderMode = RenderMode.ScreenSpaceCamera;
            config.WorldCamera = null;

            UIRoot root = CreateRoot(config);

            Assert.NotNull(root.Canvas);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, root.Canvas.renderMode, "缺 WorldCamera 应回退到 Overlay");
            Assert.IsNull(root.Canvas.worldCamera, "回退 Overlay 后不应保留相机引用");
        }

        /// <summary>非法分辨率：任一维 &lt;= 0 时回退到默认 1920x1080，并保留默认根节点名。</summary>
        [Test]
        public void Create_WithInvalidResolution_FallsBackToDefault()
        {
            UIRootConfig config = UIRootConfig.Default;
            config.ReferenceResolution = new Vector2(0f, 0f);

            UIRoot root = CreateRoot(config);
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();

            Assert.NotNull(scaler);
            Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution, "非法分辨率应回退到 1920x1080");
            Assert.AreEqual("[CandyUI] UIRoot", root.name, "默认根节点名应为 [CandyUI] UIRoot");
        }

        /// <summary>关闭自动创建 EventSystem：场景里没有 EventSystem 时不应新建，但 UIRoot 仍完成初始化。</summary>
        [Test]
        public void Create_WithEventSystemDisabled_DoesNotCreateEventSystem()
        {
            DestroyExistingEventSystems();
            Assert.AreEqual(0, Object.FindObjectsOfType<EventSystem>(true).Length, "前置条件：场景内不应再有 EventSystem");

            UIRootConfig config = UIRootConfig.Default;
            config.CreateEventSystemIfMissing = false;

            UIRoot root = CreateRoot(config);

            Assert.NotNull(root.Canvas, "不自动创建 EventSystem 不应影响 UIRoot 初始化");
            Assert.AreEqual(0, Object.FindObjectsOfType<EventSystem>(true).Length,
                "CreateEventSystemIfMissing=false 时不应自动创建 EventSystem");
        }

        /// <summary>创建并登记临时 Camera，用例结束统一 DestroyImmediate。</summary>
        private Camera CreateCamera()
        {
            Camera camera = new GameObject("UIRootConfigTests_Camera").AddComponent<Camera>();
            _listCreated.Add(camera.gameObject);
            return camera;
        }

        /// <summary>按配置创建 root 并登记清理，避免污染场景与 UIRoot.Instance。</summary>
        private UIRoot CreateRoot(UIRootConfig config)
        {
            UIRoot root = UIRoot.Create(config);
            _listCreated.Add(root.gameObject);
            return root;
        }

        /// <summary>销毁场景里已有的 EventSystem，构造“EventSystem 缺失”的前置条件。</summary>
        private static void DestroyExistingEventSystems()
        {
            EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
            foreach (EventSystem system in systems)
                Object.DestroyImmediate(system.gameObject);
        }
    }
}
