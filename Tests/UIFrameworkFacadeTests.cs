// 职责：UIFramework 实例门面的 EditMode 用例，守住 Default 与静态 UIManager 共用同一套服务、状态互通、关闭自路由。
// 依赖：UIFramework.Default、UIManager 静态 API、测试基类的反射辅助（internal 成员不可直接访问）。
// 限制：仅在 EditMode 下通过 FakeAssetLoader（Immediate 模式）同步完成打开/关闭，不依赖真实资源与帧推进。
using NUnit.Framework;

namespace CandyUI.Tests
{
    /// <summary>
    /// UIFramework 门面用例：
    /// 1) Default 与 UIManager 的三个服务是同一对象；
    /// 2) 实例 API 打开/关闭会被静态 API 看到；
    /// 3) 面板 CloseSelf 路由回创建它的上下文；
    /// 4) 两个门面的调试快照一致。
    /// </summary>
    public sealed class UIFrameworkFacadeTests : UITestBase
    {
        /// <summary>
        /// 默认上下文与静态门面必须包住同一套 Panel/Window/Popup 服务，否则状态会分裂成两份。
        /// </summary>
        [Test]
        public void Default_UsesSameServicesAsUIManager()
        {
            IgnoreEditModeObjectDestroyLogs();

            UIFramework framework = UIFramework.Default;
            Assert.NotNull(framework, "UIFramework.Default 不应为 null");

            Assert.AreSame(
                GetStaticField(typeof(UIManager), "s_panels"),
                GetInstanceProperty(framework, "Panels"),
                "UIFramework.Default.Panels 与 UIManager.s_panels 必须是同一对象");
            Assert.AreSame(
                GetStaticField(typeof(UIManager), "s_windows"),
                GetInstanceProperty(framework, "Windows"),
                "UIFramework.Default.Windows 与 UIManager.s_windows 必须是同一对象");
            Assert.AreSame(
                GetStaticField(typeof(UIManager), "s_popups"),
                GetInstanceProperty(framework, "Popups"),
                "UIFramework.Default.Popups 与 UIManager.s_popups 必须是同一对象");
        }

        /// <summary>
        /// 用实例 API 打开与关闭面板时，静态 UIManager 查询到的状态必须同步变化（同一套服务）。
        /// </summary>
        [Test]
        public void Default_OpenAndClose_ThroughInstanceApi_ReflectedInUIManagerState()
        {
            IgnoreEditModeObjectDestroyLogs();

            UIFramework framework = UIFramework.Default;

            PlainTestPanel panel = framework.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();
            Assert.NotNull(panel, "实例 API 应能打开测试面板");
            Assert.IsTrue(
                UIManager.HasOpenedPanel<PlainTestPanel>(),
                "UIFramework.Default 打开后，UIManager.HasOpenedPanel 应为 true（共用同一 PanelService）");

            framework.Close(panel);
            Assert.IsFalse(
                UIManager.HasOpenedPanel<PlainTestPanel>(),
                "UIFramework.Default 关闭后，UIManager.HasOpenedPanel 应为 false");
        }

        /// <summary>
        /// 面板记录的是创建它的上下文：CloseSelf 必须路由回 UIFramework.Default，而不是走错门面。
        /// </summary>
        [Test]
        public void PanelCloseSelf_RoutesToOwningFramework()
        {
            IgnoreEditModeObjectDestroyLogs();

            PlainTestPanel panel = UIManager.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();
            Assert.NotNull(panel, "静态 API 应能打开测试面板");

            object owner = GetInstanceProperty(panel, "Framework");
            Assert.AreSame(UIFramework.Default, owner, "面板应记录创建它的 UIFramework 上下文");

            panel.CloseSelf();

            Assert.IsTrue(
                panel.State == UIPanelState.Closing || panel.State == UIPanelState.Closed,
                $"CloseSelf 后面板应进入 Closing/Closed，实际为 {panel.State}");
            Assert.IsFalse(
                UIManager.HasOpenedPanel<PlainTestPanel>(),
                "CloseSelf 应路由到所属上下文并真正关闭面板，打开表里不应再有它");
        }

        /// <summary>
        /// GetDebugSnapshot 两个门面必须输出完全一致的三行摘要。
        /// </summary>
        [Test]
        public void Default_GetDebugSnapshot_MatchesUIManager()
        {
            IgnoreEditModeObjectDestroyLogs();

            UIManager.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();

            Assert.AreEqual(
                UIManager.GetDebugSnapshot(),
                UIFramework.Default.GetDebugSnapshot(),
                "实例门面与静态门面的调试快照应完全一致");
        }
    }
}
