// 职责：面板 Prefab 根自带 Canvas 时的回归测试（独立 Canvas 面板 + 普通面板对照）。
// 依赖：FakeAssetLoader.OwnCanvasTypes 现场造“根带 Canvas”的 Prefab，UIPanelInfo 用反射读取。
// 限制：EditMode 下 GameObject 不会被真正销毁，只断言框架可观察状态（层容器父子关系、句柄释放）。
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Tests
{
    /// <summary>
    /// 验证“面板 Prefab 根自带 Canvas”这一约定：框架只检测、不代加、不改排序，
    /// 面板仍实例化在声明层容器下并成为嵌套 Canvas；普通面板路径保持不变。
    /// </summary>
    public sealed class CanvasPanelTests : UITestBase
    {
        private const string OwnCanvasAddress = "CandyUITests/OwnCanvasPanel";
        private const string PlainAddress = "CandyUITests/PlainPanel";

        /// <summary>
        /// 根带 Canvas 的面板：打开成功、仍挂在声明层容器下、框架不补缩放/射线组件也不改排序，
        /// 快照计入“独立Canvas 1”；关闭后走销毁路径（注销并释放句柄）。
        /// </summary>
        [Test]
        public void OpenOwnCanvasPanel_KeepsNestedCanvas_UnderDeclaredLayer()
        {
            IgnoreEditModeObjectDestroyLogs();
            OwnCanvasTestPanel.AddCanvasToPrefab = true;
            OwnCanvasTestPanel.SyncCanvasSwitch();

            OwnCanvasTestPanel panel = UIManager.OpenPlainPanelAsync<OwnCanvasTestPanel>().GetAwaiter().GetResult();

            Assert.NotNull(panel);
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            Assert.IsTrue(UIManager.HasOpenedPanel<OwnCanvasTestPanel>());
            Assert.AreEqual(UILayer.Main, panel.Layer);
            Assert.AreEqual(UILayer.Main.ToString(), panel.transform.parent.name,
                "自带 Canvas 的面板也必须实例化在声明层容器下，不能被搬到别处");
            Assert.IsTrue(GetHasOwnCanvas(typeof(OwnCanvasTestPanel)), "UIPanelInfo.HasOwnCanvas 应为 true");

            Canvas canvas = panel.GetComponent<Canvas>();
            Assert.NotNull(canvas, "Prefab 根自带的 Canvas 必须原样保留");
            Assert.IsNull(panel.GetComponent<CanvasScaler>(), "框架不应给面板补 CanvasScaler");
            Assert.IsNull(panel.GetComponent<GraphicRaycaster>(), "框架不应给面板补 GraphicRaycaster");
            Assert.IsFalse(canvas.overrideSorting, "框架不应改动自带 Canvas 的排序开关");
            Assert.AreEqual(0, canvas.sortingOrder, "框架不应改动自带 Canvas 的 sortingOrder");

            StringAssert.Contains("独立Canvas 1", GetDebugLine(0));
            Assert.AreEqual(1, Loader.GetLoadCount(OwnCanvasAddress));
            Assert.AreEqual(1, Loader.GetCreatedCount(OwnCanvasAddress));

            UIManager.Close(panel, PanelCloseReason.ForceClose);

            Assert.AreEqual(UIPanelState.Closed, panel.State);
            Assert.IsFalse(UIManager.HasOpenedPanel<OwnCanvasTestPanel>());
            // EditMode 下 Object.Destroy 不生效，用“句柄已释放 + 打开表已清”证明走了销毁路径。
            Assert.AreEqual(1, Loader.GetReleaseCount(OwnCanvasAddress), "关闭后应释放资源句柄，走销毁路径");
            StringAssert.Contains("独立Canvas 0", GetDebugLine(0));
        }

        /// <summary>
        /// 普通面板（根不带 Canvas）：打开/关闭行为与改动前一致，HasOwnCanvas 为 false，
        /// 快照里的独立 Canvas 数保持 0。
        /// </summary>
        [Test]
        public void OpenPlainPanel_KeepsNormalPath_WithoutOwnCanvas()
        {
            IgnoreEditModeObjectDestroyLogs();
            PlainTestPanel panel = UIManager.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();

            Assert.NotNull(panel);
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            Assert.IsTrue(UIManager.HasOpenedPanel<PlainTestPanel>());
            Assert.AreEqual(UILayer.Main, panel.Layer);
            Assert.AreEqual(UILayer.Main.ToString(), panel.transform.parent.name);
            Assert.IsNull(panel.GetComponent<Canvas>(), "普通面板的根不应凭空多出 Canvas");
            Assert.IsFalse(GetHasOwnCanvas(typeof(PlainTestPanel)), "普通面板的 HasOwnCanvas 应为 false");
            StringAssert.Contains("独立Canvas 0", GetDebugLine(0));

            UIManager.Close(panel, PanelCloseReason.ForceClose);

            Assert.AreEqual(UIPanelState.Closed, panel.State);
            Assert.IsFalse(UIManager.HasOpenedPanel<PlainTestPanel>());
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress));
        }

        /// <summary>
        /// 反射读取已打开面板档案（internal UIPanelInfo）里的 HasOwnCanvas 值。
        /// </summary>
        private static bool GetHasOwnCanvas(Type panelType)
        {
            object panelService = GetStaticField(typeof(UIManager), "s_panels");
            object dicOpenedPanels = GetInstanceField(panelService, "_dicOpenedPanels");
            IDictionary panels = dicOpenedPanels as IDictionary;
            Assert.NotNull(panels, "PanelService._dicOpenedPanels 不是字典，反射辅助需要同步更新");

            object info = panels[panelType];
            Assert.NotNull(info, $"面板 {panelType.Name} 不在打开表里，无法读取 HasOwnCanvas");
            object hasOwnCanvas = GetInstanceField(info, "HasOwnCanvas");
            return (bool)hasOwnCanvas;
        }
    }
}
