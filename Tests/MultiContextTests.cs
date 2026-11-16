// 职责：验证 UIFramework.Create 创建的多套 UI 上下文完全独立：根节点、三个服务、资源缓存互不共享。
// 流程：每个用例新建两套上下文 → 各自打开/关闭同一面板类型 → 断言状态、根节点、加载/释放计数互不影响。
// 依赖：UITestBase 负责默认上下文与 EditMode 环境；FakeAssetLoader 每个用例独立实例，禁止共用。
// 限制：EditMode 下 Object.Destroy 不会真正销毁，Dispose 后的根节点用 DestroyImmediate 兜底清理。
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CandyUI.Tests
{
    /// <summary>
    /// 多套 UI 上下文的隔离性测试：额外创建的上下文一律在 finally 里 Dispose，避免残留根节点与面板。
    /// </summary>
    public sealed class MultiContextTests : UITestBase
    {
        private const string PlainPanelAddress = "CandyUITests/PlainPanel";

        /// <summary>反射读 UIRoot.Instance（internal 静态属性），用于确认创建新上下文不会改写全局根。</summary>
        private static UIRoot GetUIRootInstance()
        {
            PropertyInfo property = typeof(UIRoot).GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(property, "找不到 UIRoot.Instance（internal 静态属性）");
            return property.GetValue(null) as UIRoot;
        }

        /// <summary>同步等待打开 PlainTestPanel，避免每个用例重复写等待样板。</summary>
        private static PlainTestPanel OpenPlain(UIFramework framework)
        {
            return framework.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();
        }

        /// <summary>同步等待关闭面板，返回框架报告的关闭结果。</summary>
        private static bool ClosePanel(UIFramework framework, UIPanel panel)
        {
            return framework.CloseAsync(panel).GetAwaiter().GetResult();
        }

        /// <summary>为单个用例新建独立加载器；构造参数与 UITestBase.Loader 保持一致。</summary>
        private static FakeAssetLoader NewLoader()
        {
            return new FakeAssetLoader(typeof(UITestBase).Assembly);
        }

        /// <summary>用例收尾：销毁仍存在的根节点，避免 EditMode 下 Destroy 不生效留下残留对象。</summary>
        private static void DestroyRootIfAlive(UIRoot root)
        {
            if (root == null)
                return;

            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }

        [Test]
        public void Create_TwoContexts_OpenSamePanelType_Independently()
        {
            IgnoreEditModeObjectDestroyLogs();
            FakeAssetLoader loaderA = NewLoader();
            FakeAssetLoader loaderB = NewLoader();
            UIFramework a = null;
            UIFramework b = null;
            UIRoot rootA = null;
            UIRoot rootB = null;
            try
            {
                a = UIFramework.Create(default, loaderA);
                b = UIFramework.Create(default, loaderB);
                rootA = a.Root;
                rootB = b.Root;

                Assert.IsTrue(UIFramework.Default.IsDefault, "默认上下文应标记 IsDefault");
                Assert.IsFalse(a.IsDefault, "Create 创建的上下文不应标记 IsDefault");

                PlainTestPanel panelA = OpenPlain(a);
                PlainTestPanel panelB = OpenPlain(b);

                Assert.AreNotSame(panelA, panelB, "两套上下文应各自实例化同一个面板类型");
                Assert.AreNotSame(panelA.gameObject, panelB.gameObject);
                Assert.IsTrue(a.HasOpenedPanel<PlainTestPanel>());
                Assert.IsTrue(b.HasOpenedPanel<PlainTestPanel>());
                Assert.IsTrue(a.IsPanelVisible<PlainTestPanel>());
                Assert.IsTrue(b.IsPanelVisible<PlainTestPanel>());
                Assert.IsFalse(UIManager.HasOpenedPanel<PlainTestPanel>(), "默认上下文不应看到其它上下文的界面");
                Assert.IsFalse(UIManager.IsPanelVisible<PlainTestPanel>());
            }
            finally
            {
                a?.Dispose();
                b?.Dispose();
                DestroyRootIfAlive(rootA);
                DestroyRootIfAlive(rootB);
            }
        }

        [Test]
        public void CloseInOneContext_DoesNotAffectTheOther()
        {
            IgnoreEditModeObjectDestroyLogs();
            FakeAssetLoader loaderA = NewLoader();
            FakeAssetLoader loaderB = NewLoader();
            UIFramework a = null;
            UIFramework b = null;
            UIRoot rootA = null;
            UIRoot rootB = null;
            try
            {
                a = UIFramework.Create(default, loaderA);
                b = UIFramework.Create(default, loaderB);
                rootA = a.Root;
                rootB = b.Root;

                PlainTestPanel panelA = OpenPlain(a);
                PlainTestPanel panelB = OpenPlain(b);

                Assert.IsTrue(ClosePanel(a, panelA), "A 上下文应成功关闭自己的面板");
                Assert.IsFalse(a.HasOpenedPanel<PlainTestPanel>());
                Assert.IsFalse(a.IsPanelVisible<PlainTestPanel>());
                Assert.IsTrue(b.HasOpenedPanel<PlainTestPanel>(), "关闭 A 不应影响 B 的打开状态");
                Assert.IsTrue(b.IsPanelVisible<PlainTestPanel>(), "关闭 A 不应影响 B 的可见状态");
                Assert.IsNotNull(b.GetOpenedPanel<PlainTestPanel>());
                Assert.IsNotNull(panelB);
                Assert.AreEqual(UIPanelState.Visible, panelB.State);
            }
            finally
            {
                a?.Dispose();
                b?.Dispose();
                DestroyRootIfAlive(rootA);
                DestroyRootIfAlive(rootB);
            }
        }

        [Test]
        public void TwoContexts_HaveIndependentRootsAndCanvases()
        {
            IgnoreEditModeObjectDestroyLogs();
            UIRoot defaultRoot = UIFramework.Default.Root;
            Assert.AreSame(defaultRoot, GetUIRootInstance(), "默认上下文应使用全局 UIRoot.Instance");

            FakeAssetLoader loaderA = NewLoader();
            FakeAssetLoader loaderB = NewLoader();
            UIFramework a = null;
            UIFramework b = null;
            UIRoot rootA = null;
            UIRoot rootB = null;
            try
            {
                a = UIFramework.Create(default, loaderA);
                b = UIFramework.Create(default, loaderB);
                rootA = a.Root;
                rootB = b.Root;

                Assert.AreNotSame(rootA, rootB, "两套上下文应有各自的根节点");
                Assert.AreNotSame(defaultRoot, rootA);
                Assert.IsNotNull(rootA.Canvas, "上下文的根节点应带 Canvas");
                Assert.IsNotNull(rootB.Canvas, "上下文的根节点应带 Canvas");
                Assert.AreNotSame(rootA.Canvas, rootB.Canvas, "两套上下文的 Canvas 不应共享");
                Assert.AreSame(defaultRoot, GetUIRootInstance(), "创建额外上下文不应改写 UIRoot.Instance");
                Assert.AreSame(defaultRoot, UIFramework.Default.Root);
            }
            finally
            {
                a?.Dispose();
                b?.Dispose();
                DestroyRootIfAlive(rootA);
                DestroyRootIfAlive(rootB);
            }
        }

        [Test]
        public void TwoContexts_HaveIndependentAssetCaches()
        {
            IgnoreEditModeObjectDestroyLogs();
            FakeAssetLoader loaderA = NewLoader();
            FakeAssetLoader loaderB = NewLoader();
            UIFramework a = null;
            UIFramework b = null;
            UIRoot rootA = null;
            UIRoot rootB = null;
            try
            {
                a = UIFramework.Create(default, loaderA);
                b = UIFramework.Create(default, loaderB);
                rootA = a.Root;
                rootB = b.Root;

                PlainTestPanel panelA = OpenPlain(a);
                Assert.AreEqual(1, loaderA.GetLoadCount(PlainPanelAddress));
                Assert.AreEqual(0, loaderB.GetLoadCount(PlainPanelAddress), "A 的加载不应落到 B 的缓存");
                Assert.IsFalse(b.HasOpenedPanel<PlainTestPanel>());

                PlainTestPanel panelB = OpenPlain(b);
                Assert.AreEqual(1, loaderA.GetLoadCount(PlainPanelAddress));
                Assert.AreEqual(1, loaderB.GetLoadCount(PlainPanelAddress));
                Assert.AreNotSame(panelA, panelB);

                Assert.IsTrue(ClosePanel(a, panelA));
                Assert.AreEqual(1, loaderA.GetReleaseCount(PlainPanelAddress), "关闭 A 应释放 A 自己的句柄");
                Assert.AreEqual(0, loaderB.GetReleaseCount(PlainPanelAddress), "B 的句柄不应被 A 的关闭释放");
                Assert.IsTrue(b.HasOpenedPanel<PlainTestPanel>());
                Assert.IsTrue(b.IsPanelVisible<PlainTestPanel>());
                Assert.IsNotNull(panelB);
                Assert.AreEqual(UIPanelState.Visible, panelB.State);
            }
            finally
            {
                a?.Dispose();
                b?.Dispose();
                DestroyRootIfAlive(rootA);
                DestroyRootIfAlive(rootB);
            }
        }

        [Test]
        public void Dispose_ClosesPanelsAndReleasesResources()
        {
            IgnoreEditModeObjectDestroyLogs();
            FakeAssetLoader loader = NewLoader();
            UIFramework framework = null;
            UIRoot root = null;
            try
            {
                framework = UIFramework.Create(default, loader);
                Assert.IsFalse(framework.IsDefault);
                root = framework.Root;

                PlainTestPanel panel = OpenPlain(framework);
                Assert.IsNotNull(panel);
                Assert.AreEqual(0, loader.GetReleaseCount(PlainPanelAddress));

                framework.Dispose();

                Assert.IsFalse(framework.HasOpenedPanel<PlainTestPanel>(), "Dispose 后打开表应清空");
                Assert.IsFalse(framework.IsPanelVisible<PlainTestPanel>());
                Assert.AreEqual(1, loader.GetReleaseCount(PlainPanelAddress), "Dispose 应释放面板资源句柄");

                framework.Dispose();
                Assert.Throws<ObjectDisposedException>(() => framework.CloseAll());
                Assert.Throws<ObjectDisposedException>(
                    () => { framework.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult(); });
                Assert.Throws<ObjectDisposedException>(() => { _ = framework.Root; });
            }
            finally
            {
                framework?.Dispose();
                DestroyRootIfAlive(root);
            }
        }
    }
}
