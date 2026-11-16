using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CandyUI.Tests
{
    /// <summary>
    /// 同类型多实例（PanelKey = 类型 + 实例名）的用例：不同实例名并存、相同实例名复用、
    /// 默认实例（不传实例名）保持旧的单实例语义；窗口层验证同一类型多实例可同时在栈里。
    /// </summary>
    public sealed class MultiInstanceTests : UITestBase
    {
        private const string MultiInstanceAddress = "CandyUITests/MultiInstancePanel";
        private const string MultiInstanceKeepInstanceAddress = "CandyUITests/MultiInstanceKeepInstancePanel";

        /// <summary>同一类型两个不同实例名：同时打开，各自独立，节点名带实例名后缀。</summary>
        [Test]
        public async Task OpenPlainPanel_TwoInstanceNames_CreatesSeparateInstances()
        {
            IgnoreEditModeObjectDestroyLogs();

            MultiInstanceTestPanel first = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("a");
            MultiInstanceTestPanel second = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("b");

            Assert.AreNotSame(first, second);
            Assert.IsTrue(UIManager.HasOpenedPanel<MultiInstanceTestPanel>("a"));
            Assert.IsTrue(UIManager.HasOpenedPanel<MultiInstanceTestPanel>("b"));
            Assert.IsTrue(UIManager.IsPanelVisible<MultiInstanceTestPanel>("a"));
            Assert.IsTrue(UIManager.IsPanelVisible<MultiInstanceTestPanel>("b"));
            Assert.AreEqual(2, UIManager.GetOpenedPanels<MultiInstanceTestPanel>().Count);
            Assert.AreEqual("MultiInstanceTestPanel_a", first.gameObject.name);
            Assert.AreEqual("MultiInstanceTestPanel_b", second.gameObject.name);

            // 命名实例不算默认实例：不传实例名的查询不应该命中它们。
            Assert.IsNull(UIManager.GetOpenedPanel<MultiInstanceTestPanel>());
            Assert.IsFalse(UIManager.HasOpenedPanel<MultiInstanceTestPanel>());
        }

        /// <summary>同一类型同一实例名重复打开：复用同一个实例，不重复创建。</summary>
        [Test]
        public async Task OpenPlainPanel_SameInstanceName_ReusesSameInstance()
        {
            IgnoreEditModeObjectDestroyLogs();

            MultiInstanceTestPanel first = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("slot");
            MultiInstanceTestPanel second = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("slot");

            Assert.AreSame(first, second);
            Assert.AreEqual(1, second.CreateCount);
            Assert.AreEqual(2, second.ShowCount);
            Assert.AreEqual(1, UIManager.GetOpenedPanels<MultiInstanceTestPanel>().Count);
            Assert.AreEqual(1, Loader.GetLoadCount(MultiInstanceAddress));
        }

        /// <summary>关掉其中一个命名实例，另一个仍然打开且可见。</summary>
        [Test]
        public async Task CloseOneNamedInstance_LeavesOtherOpenAndVisible()
        {
            IgnoreEditModeObjectDestroyLogs();

            MultiInstanceTestPanel first = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("a");
            MultiInstanceTestPanel second = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("b");

            UIManager.Close(first);
            PumpOnce();

            Assert.IsFalse(UIManager.HasOpenedPanel<MultiInstanceTestPanel>("a"));
            Assert.IsTrue(UIManager.HasOpenedPanel<MultiInstanceTestPanel>("b"));
            Assert.IsTrue(UIManager.IsPanelVisible<MultiInstanceTestPanel>("b"));
            Assert.AreSame(second, UIManager.GetOpenedPanel<MultiInstanceTestPanel>("b"));
            Assert.AreEqual(1, UIManager.GetOpenedPanels<MultiInstanceTestPanel>().Count);
            Assert.AreEqual(UIPanelState.Closed, first.State);
        }

        /// <summary>不传实例名的旧 API 仍是单实例语义：连续两次打开返回同一个默认实例。</summary>
        [Test]
        public async Task OpenPlainPanel_WithoutInstanceName_KeepsSingleInstance()
        {
            IgnoreEditModeObjectDestroyLogs();

            MultiInstanceTestPanel first = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>();
            MultiInstanceTestPanel second = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>();

            Assert.AreSame(first, second);
            Assert.AreEqual(1, UIManager.GetOpenedPanels<MultiInstanceTestPanel>().Count);
            Assert.AreEqual("MultiInstanceTestPanel", second.gameObject.name);
            Assert.IsTrue(UIManager.HasOpenedPanel<MultiInstanceTestPanel>());
            Assert.IsTrue(UIManager.HasOpenedPanel<MultiInstanceTestPanel>(null));
        }

        /// <summary>CloseOfType 关掉该类型的全部实例（默认实例与命名实例）。</summary>
        [Test]
        public async Task CloseOfType_ClosesAllNamedInstances()
        {
            IgnoreEditModeObjectDestroyLogs();

            MultiInstanceTestPanel first = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("a");
            MultiInstanceTestPanel second = await UIManager.OpenPlainPanelAsync<MultiInstanceTestPanel>("b");

            UIManager.CloseOfType<MultiInstanceTestPanel>();
            PumpOnce();

            Assert.AreEqual(0, UIManager.GetOpenedPanels<MultiInstanceTestPanel>().Count);
            Assert.IsFalse(UIManager.HasOpenedPanel<MultiInstanceTestPanel>("a"));
            Assert.IsFalse(UIManager.HasOpenedPanel<MultiInstanceTestPanel>("b"));
            Assert.AreEqual(UIPanelState.Closed, first.State);
            Assert.AreEqual(UIPanelState.Closed, second.State);
        }

        /// <summary>KeepInstance + 命名实例：同名复用同一 GameObject，换名则新建。</summary>
        [Test]
        public async Task KeepInstance_NamedInstance_ReusesSameGameObject()
        {
            IgnoreEditModeObjectDestroyLogs();

            MultiInstanceKeepInstanceTestPanel first =
                await UIManager.OpenPlainPanelAsync<MultiInstanceKeepInstanceTestPanel>("slot");
            int instanceId = first.gameObject.GetInstanceID();

            UIManager.Close(first);
            PumpOnce();
            Assert.AreEqual(0, UIManager.GetOpenedPanels<MultiInstanceKeepInstanceTestPanel>().Count);
            Assert.AreEqual(0, Loader.GetReleaseCount(MultiInstanceKeepInstanceAddress));

            MultiInstanceKeepInstanceTestPanel reused =
                await UIManager.OpenPlainPanelAsync<MultiInstanceKeepInstanceTestPanel>("slot");
            Assert.AreSame(first, reused);
            Assert.AreEqual(instanceId, reused.gameObject.GetInstanceID());
            Assert.AreEqual(1, reused.CreateCount);
            Assert.AreEqual(1, reused.SleepCount);

            MultiInstanceKeepInstanceTestPanel other =
                await UIManager.OpenPlainPanelAsync<MultiInstanceKeepInstanceTestPanel>("other");
            Assert.AreNotSame(reused, other);
            Assert.AreNotEqual(instanceId, other.gameObject.GetInstanceID());
            Assert.AreEqual(2, UIManager.GetOpenedPanels<MultiInstanceKeepInstanceTestPanel>().Count);
        }

        /// <summary>窗口层：同一窗口类型的不同实例名可以同时在窗口栈里，旧的开在下面保持隐藏。</summary>
        [Test]
        public async Task OpenWindow_TwoInstanceNames_CoexistsInStack()
        {
            IgnoreEditModeObjectDestroyLogs();

            MultiInstanceWindowTestPanel left = await UIManager.OpenWindowAsync<MultiInstanceWindowTestPanel>("left");
            MultiInstanceWindowTestPanel right = await UIManager.OpenWindowAsync<MultiInstanceWindowTestPanel>("right");

            Assert.AreNotSame(left, right);
            Assert.IsTrue(UIManager.HasOpenedPanel<MultiInstanceWindowTestPanel>("left"));
            Assert.IsTrue(UIManager.HasOpenedPanel<MultiInstanceWindowTestPanel>("right"));
            Assert.AreEqual(2, UIManager.GetOpenedPanels<MultiInstanceWindowTestPanel>().Count);
            Assert.IsFalse(UIManager.IsPanelVisible<MultiInstanceWindowTestPanel>("left"));
            Assert.IsTrue(UIManager.IsPanelVisible<MultiInstanceWindowTestPanel>("right"));
            StringAssert.Contains("窗口栈(2):", GetDebugLine(1));

            UIManager.Close(right);
            PumpOnce();

            Assert.IsFalse(UIManager.HasOpenedPanel<MultiInstanceWindowTestPanel>("right"));
            Assert.IsTrue(UIManager.IsPanelVisible<MultiInstanceWindowTestPanel>("left"));
            StringAssert.Contains("窗口栈(1):", GetDebugLine(1));
        }
    }
}
