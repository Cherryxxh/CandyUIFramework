// 职责：窗口栈行为回归测试：隐藏旧窗口、返回恢复、关闭非栈顶不重跑 OnShow、重复打开置顶。
// 依赖：TestPanelBase 的 ShowCount/HideCount 与 UIManager.GetDebugSnapshot 的窗口栈行。
// 限制：断言依赖调试快照文本，快照格式变化需同步更新。
using NUnit.Framework;

namespace CandyUI.Tests
{
    /// <summary>
    /// 窗口栈：新窗口隐藏旧窗口、NavigateBack 关闭栈顶恢复上一页；
    /// 关闭非栈顶窗口不得重跑当前栈顶的 OnShow（89505fc 修复 2 的回归用例）。
    /// </summary>
    public sealed class WindowStackTests : UITestBase
    {
        /// <summary>
        /// 验证打开新窗口会隐藏上一个窗口并入栈，CanNavigateBack 随栈深度变为可用。
        /// </summary>
        [Test]
        public void OpenWindow_HidesPreviousWindow()
        {
            IgnoreEditModeObjectDestroyLogs();
            WindowATestPanel windowA = UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();
            Assert.AreEqual(UIPanelState.Visible, windowA.State);

            WindowBTestPanel windowB = UIManager.OpenWindowAsync<WindowBTestPanel>().GetAwaiter().GetResult();

            Assert.AreEqual(UIPanelState.Visible, windowB.State);
            Assert.AreEqual(UIPanelState.Hidden, windowA.State);
            Assert.IsFalse(windowA.gameObject.activeSelf);
            Assert.AreEqual(1, windowA.HideCount);
            Assert.IsTrue(UIManager.CanNavigateBack);
            StringAssert.Contains("窗口栈(2): WindowATestPanel -> WindowBTestPanel", GetDebugLine(1));
        }

        /// <summary>
        /// 验证 NavigateBack 关闭栈顶窗口并恢复上一页（重新 OnShow、状态回到 Visible）。
        /// </summary>
        [Test]
        public void NavigateBack_ClosesTopWindow_AndRestoresPrevious()
        {
            IgnoreEditModeObjectDestroyLogs();
            WindowATestPanel windowA = UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();
            WindowBTestPanel windowB = UIManager.OpenWindowAsync<WindowBTestPanel>().GetAwaiter().GetResult();

            UIManager.NavigateBack();

            Assert.IsFalse(UIManager.HasOpenedPanel<WindowBTestPanel>(), "NavigateBack 应关闭栈顶窗口");
            Assert.AreEqual(UIPanelState.Closed, windowB.State);
            Assert.IsTrue(UIManager.HasOpenedPanel<WindowATestPanel>());
            Assert.AreEqual(UIPanelState.Visible, windowA.State);
            Assert.AreEqual(2, windowA.ShowCount, "返回后上一页必须重新显示");
            Assert.IsFalse(UIManager.CanNavigateBack);
            StringAssert.Contains("窗口栈(1): WindowATestPanel", GetDebugLine(1));
        }

        /// <summary>
        /// 验证关闭栈底（非栈顶）窗口不会重跑当前栈顶的 OnShow（89505fc 修复 2）。
        /// </summary>
        [Test]
        public void CloseNonTopWindow_DoesNotReShowTopWindow()
        {
            IgnoreEditModeObjectDestroyLogs();
            WindowATestPanel windowA = UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();
            UIManager.OpenWindowAsync<WindowBTestPanel>().GetAwaiter().GetResult();
            WindowCTestPanel windowC = UIManager.OpenWindowAsync<WindowCTestPanel>().GetAwaiter().GetResult();
            Assert.AreEqual(1, windowC.ShowCount);

            // 栈底窗口 A 不是栈顶：关闭它不得重跑当前栈顶 C 的 OnShow。
            UIManager.Close(windowA);

            Assert.AreEqual(UIPanelState.Closed, windowA.State);
            Assert.IsTrue(UIManager.HasOpenedPanel<WindowCTestPanel>());
            Assert.AreEqual(UIPanelState.Visible, windowC.State);
            Assert.AreEqual(1, windowC.ShowCount, "关闭非栈顶窗口后，栈顶窗口的 OnShow 不应重跑（89505fc 修复 2）");
            Assert.AreEqual(0, windowC.HideCount);
            StringAssert.Contains("窗口栈(2): WindowBTestPanel -> WindowCTestPanel", GetDebugLine(1));
        }

        /// <summary>
        /// 验证关闭中间窗口后栈顶窗口保持可见且不重跑 OnShow，窗口栈正确移除该层。
        /// </summary>
        [Test]
        public void CloseMiddleWindow_DoesNotReShowTopWindow()
        {
            IgnoreEditModeObjectDestroyLogs();
            UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();
            WindowBTestPanel windowB = UIManager.OpenWindowAsync<WindowBTestPanel>().GetAwaiter().GetResult();
            WindowCTestPanel windowC = UIManager.OpenWindowAsync<WindowCTestPanel>().GetAwaiter().GetResult();

            UIManager.Close(windowB);

            Assert.AreEqual(UIPanelState.Closed, windowB.State);
            Assert.AreEqual(1, windowC.ShowCount, "关闭中间窗口后，栈顶窗口的 OnShow 不应重跑");
            Assert.AreEqual(UIPanelState.Visible, windowC.State);
            StringAssert.Contains("窗口栈(2): WindowATestPanel -> WindowCTestPanel", GetDebugLine(1));
        }

        /// <summary>
        /// 验证重复打开已存在的窗口会复用实例并移到栈顶，原栈顶窗口被隐藏。
        /// </summary>
        [Test]
        public void ReopenExistingWindow_MovesItToTop()
        {
            IgnoreEditModeObjectDestroyLogs();
            WindowATestPanel windowA = UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();
            WindowBTestPanel windowB = UIManager.OpenWindowAsync<WindowBTestPanel>().GetAwaiter().GetResult();
            Assert.AreEqual(UIPanelState.Hidden, windowA.State);
            Assert.AreEqual(1, windowB.ShowCount);

            WindowATestPanel windowAAgain = UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();

            Assert.AreSame(windowA, windowAAgain, "复用已打开窗口实例");
            Assert.AreEqual(UIPanelState.Visible, windowAAgain.State);
            Assert.AreEqual(UIPanelState.Hidden, windowB.State, "旧栈顶应被新栈顶隐藏");
            Assert.AreEqual(2, windowAAgain.ShowCount);
            Assert.AreEqual(1, windowAAgain.CreateCount);
            Assert.AreEqual(1, windowA.HideCount);
            StringAssert.Contains("窗口栈(2): WindowBTestPanel -> WindowATestPanel", GetDebugLine(1));
        }
    }
}
