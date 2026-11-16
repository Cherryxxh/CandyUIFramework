// 职责：关闭流程竞态回归用例——清缓存与关闭动画、Closing 面板重复关闭、关窗动画期间重开窗口。
// 依赖：UITestBase 的 SetUp/TearDown/AdvanceWait 等工具，ControllableTransition 手动放行退场动画。
// 限制：用例必须放行假转场，否则关闭 Task 永远挂起；断言只依赖可观察状态，不依赖 EditMode 下无效的 GameObject 销毁。
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CandyUI.Tests
{
    /// <summary>
    /// 三个关闭流程竞态缺陷的回归用例，覆盖清缓存与关闭动画、Closing 面板重复关闭、关窗动画期间开新窗口三种时序。
    /// </summary>
    public sealed class CloseRaceTests : UITestBase
    {
        private const string KeepInstanceAddress = "CandyUITests/KeepInstancePanel";

        /// <summary>
        /// 反射把可控假转场挂到 UIPanel.Transition 的自动属性字段上；
        /// 测试程序集没有 InternalsVisibleTo，只能走反射，不改运行时代码可见性。
        /// </summary>
        private static ControllableTransition AttachControllableTransition(UIPanel panel)
        {
            FieldInfo field = typeof(UIPanel).GetField(
                "<Transition>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, "找不到 UIPanel.Transition 的自动属性字段");
            ControllableTransition transition = new ControllableTransition();
            field.SetValue(panel, transition);
            return transition;
        }

        /// <summary>
        /// bug①：KeepInstance 面板关闭动画播放期间清空缓存，动画结束后必须销毁实例，不能把缓存写回来。
        /// </summary>
        [UnityTest]
        public IEnumerator ClearPanelCache_DuringClosingAnimation_DestroysInsteadOfCaching()
        {
            IgnoreEditModeObjectDestroyLogs();

            KeepInstanceTestPanel panel = UIManager
                .OpenPlainPanelAsync<KeepInstanceTestPanel>()
                .GetAwaiter().GetResult();
            Assert.NotNull(panel, "KeepInstance 面板应打开成功");

            ControllableTransition transition = AttachControllableTransition(panel);
            UIManager.Close(panel);
            Assert.AreEqual(UIPanelState.Closing, panel.State, "发起关闭后应停在 Closing，等待退场动画");
            Assert.AreEqual(1, transition.PlayOutCount, "关闭应播放一次退场动画");

            // 关键时序：关闭动画还在播时清空缓存，动画结束后的写回必须被缓存代号拦下。
            UIManager.ClearPanelCache();
            transition.CompleteOut();

            float deadline = BeginWait();
            while (!AdvanceWait(
                       () => panel.State == UIPanelState.Closed
                             && Loader.GetReleaseCount(KeepInstanceAddress) == 1,
                       "关闭动画播完后实例应被销毁并释放句柄",
                       deadline))
                yield return null;

            Assert.AreEqual(UIPanelState.Closed, panel.State, "清缓存后关闭应走销毁路径（State=Closed）");
            Assert.AreEqual(1, Loader.GetReleaseCount(KeepInstanceAddress), "实例销毁必须释放句柄，且只释放一次");
            StringAssert.Contains("缓存实例 0", GetDebugLine(0), "清缓存后不允许 KeepInstance 实例再写回缓存");
            Assert.IsFalse(UIManager.HasOpenedPanel<KeepInstanceTestPanel>(), "销毁后不应残留在打开表");
        }

        /// <summary>
        /// bug②：对正在 Closing 的面板再次 await ClosePanelAsync，应等本次关闭完成并返回 true，且不重复发起关闭。
        /// </summary>
        [UnityTest]
        public IEnumerator ClosePanelAsync_OnClosingPanel_CompletesAndReturnsTrue()
        {
            IgnoreEditModeObjectDestroyLogs();

            PlainTestPanel panel = UIManager
                .OpenPlainPanelAsync<PlainTestPanel>()
                .GetAwaiter().GetResult();
            Assert.NotNull(panel, "普通面板应打开成功");

            ControllableTransition transition = AttachControllableTransition(panel);
            UIManager.Close(panel);
            Assert.AreEqual(UIPanelState.Closing, panel.State, "首次关闭后应进入 Closing");

            // 对正在关闭的面板再次 await 关闭：应等待本次关闭完成，而不是返回 false。
            Task<bool> secondClose = UIManager.CloseAsync(panel);
            yield return null;
            Assert.IsFalse(secondClose.IsCompleted, "首次关闭动画未完成时，第二次关闭应处于等待状态");

            transition.CompleteOut();

            float deadline = BeginWait();
            while (!AdvanceWait(
                       () => secondClose.IsCompleted,
                       "对 Closing 面板的第二次关闭应等本次关闭完成后返回",
                       deadline))
                yield return null;

            Assert.IsTrue(secondClose.GetAwaiter().GetResult(), "对正在关闭的面板 CloseAsync 应返回 true（已受理）");
            Assert.AreEqual(1, transition.PlayOutCount, "重复关闭不应重复播放退场动画");
            Assert.AreEqual(1, panel.CloseCount, "关闭动作只应发生一次（OnClose 只调一次）");
            Assert.AreEqual(UIPanelState.Closed, panel.State, "关闭完成后面板应为 Closed");
        }

        /// <summary>
        /// bug③：关 C 的动画期间打开 D，关闭完成后栈序应为 A→B→D，且同一时刻只有一个窗口可见。
        /// </summary>
        [UnityTest]
        public IEnumerator OpenWindowDuringCloseAnimation_KeepsStackOrder()
        {
            IgnoreEditModeObjectDestroyLogs();

            UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();
            UIManager.OpenWindowAsync<WindowBTestPanel>().GetAwaiter().GetResult();
            WindowCTestPanel windowC = UIManager.OpenWindowAsync<WindowCTestPanel>().GetAwaiter().GetResult();
            StringAssert.Contains(
                "窗口栈(3): WindowATestPanel -> WindowBTestPanel -> WindowCTestPanel",
                GetDebugLine(1),
                "初始栈序应为 A -> B -> C");

            ControllableTransition transition = AttachControllableTransition(windowC);
            UIManager.Close(windowC);
            Assert.AreEqual(UIPanelState.Closing, windowC.State, "关闭 C 后应停在 Closing，等待退场动画");
            StringAssert.Contains(
                "窗口栈(2): WindowATestPanel -> WindowBTestPanel",
                GetDebugLine(1),
                "C 受理关闭后应立即出栈");

            // 关闭动画还没播完就打开 D：D 必须等恢复流程结束再压栈，否则会被随后恢复的 B 覆盖。
            Task<WindowDTestPanel> openD = UIManager.OpenWindowAsync<WindowDTestPanel>();
            yield return null;
            Assert.IsFalse(openD.IsCompleted, "关闭动画未播完时，新窗口打开应等待 pending close 结束");

            transition.CompleteOut();

            float deadline = BeginWait();
            while (!AdvanceWait(
                       () => openD.IsCompleted && UIManager.IsPanelVisible<WindowDTestPanel>(),
                       "关闭动画播完后新窗口应打开并显示",
                       deadline))
                yield return null;

            WindowDTestPanel windowD = openD.GetAwaiter().GetResult();
            Assert.NotNull(windowD, "D 应打开成功");
            Assert.AreEqual(UIPanelState.Closed, windowC.State, "C 应关闭完成");
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Window), "同一时间只允许一个窗口可见");
            Assert.IsTrue(UIManager.IsPanelVisible<WindowDTestPanel>(), "可见窗口应为 D");
            Assert.IsFalse(UIManager.IsPanelVisible<WindowATestPanel>(), "A 不应被重新显示");
            Assert.IsFalse(UIManager.IsPanelVisible<WindowBTestPanel>(), "B 不应被重新显示");
            StringAssert.Contains(
                "窗口栈(3): WindowATestPanel -> WindowBTestPanel -> WindowDTestPanel",
                GetDebugLine(1),
                "最终栈序应为 A -> B -> D，不出现中间窗口被重新 show");
        }
    }
}
