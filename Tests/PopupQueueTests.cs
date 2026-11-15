// 职责：弹窗队列行为回归测试：遮罩/返回、同类型刷新、优先级排序、去重、取消排队、失效只推进一次。
// 依赖：FakeAssetLoader.ManualMode 与 UIManager.GetDebugSnapshot 的弹窗调试行。
// 限制：断言依赖 GetDebugSnapshot 的固定行格式（0=面板、1=窗口栈、2=弹窗），格式变化需同步更新。
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CandyUI.Tests
{
    /// <summary>
    /// 弹窗队列：遮罩/返回键、同类型刷新、按 PopupPriority 排序出队、同类型去重、
    /// 取消排队、CloseAllPopups 打断加载时"一次失效只推进一个弹窗"（89505fc 修复 1 的回归用例）。
    /// </summary>
    public sealed class PopupQueueTests : UITestBase
    {
        private const string PopupAAddress = "CandyUITests/PopupA";
        private const string PopupBAddress = "CandyUITests/PopupB";
        private const string PopupCAddress = "CandyUITests/PopupC";

        /// <summary>
        /// 验证首个弹窗显示时遮罩出现、可返回；NavigateBack 关闭弹窗后遮罩隐藏。
        /// </summary>
        [Test]
        public void OpenPopup_ShowsMask_AndNavigateBackCloses()
        {
            IgnoreEditModeObjectDestroyLogs();
            PopupATestPanel popup = UIManager.OpenPopupAsync<PopupATestPanel>().GetAwaiter().GetResult();

            Assert.NotNull(popup);
            Assert.AreEqual(UIPanelState.Visible, popup.State);
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup));
            Assert.IsTrue(UIManager.CanNavigateBack);
            StringAssert.Contains("弹窗: PopupATestPanel", GetDebugLine(2));
            StringAssert.Contains("遮罩: 显示", GetDebugLine(2));

            UIManager.NavigateBack();

            Assert.IsFalse(UIManager.HasOpenedPanel<PopupATestPanel>());
            Assert.AreEqual(UIPanelState.Closed, popup.State);
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Popup));
            Assert.IsFalse(UIManager.CanNavigateBack);
            StringAssert.Contains("弹窗: 无", GetDebugLine(2));
            StringAssert.Contains("遮罩: 隐藏", GetDebugLine(2));
        }

        /// <summary>
        /// 验证同类型弹窗已在显示时再次打开会刷新现有实例（重跑 OnShow）而不是入队。
        /// </summary>
        [Test]
        public void SameTypeWhileActive_RefreshesExistingPopup()
        {
            IgnoreEditModeObjectDestroyLogs();
            PopupATestPanel first = UIManager.OpenPopupAsync<PopupATestPanel>().GetAwaiter().GetResult();
            PopupATestPanel second = UIManager.OpenPopupAsync<PopupATestPanel>().GetAwaiter().GetResult();

            Assert.AreSame(first, second, "同类型弹窗已在显示时应复用实例而不是入队");
            Assert.AreEqual(2, first.ShowCount);
            Assert.AreEqual(1, Loader.GetLoadCount(PopupAAddress));
            StringAssert.Contains("队列(0)", GetDebugLine(2));
        }

        /// <summary>
        /// 验证 allowDuplicatePopup 的同类型请求会排队（返回 null），关闭当前后按队列逐个打开新实例。
        /// </summary>
        [UnityTest]
        public IEnumerator AllowDuplicatePopup_QueuesSameTypeTwice()
        {
            IgnoreEditModeObjectDestroyLogs();
            // 用异步（手动放行）加载：同地址"卸载后再加载"在同步完成 loader 下会踩到
            // AssetCache 的已知边界问题（见测试报告），这里改为真异步语义。
            Loader.ManualMode = true;

            Task<PopupDupTestPanel> firstTask = UIManager.OpenPopupAsync<PopupDupTestPanel>();
            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => firstTask.IsCompleted, "首个弹窗打开完成", deadline)) yield return null;
            PopupDupTestPanel first = firstTask.GetAwaiter().GetResult();
            Assert.NotNull(first);

            Task<PopupDupTestPanel> second = UIManager.OpenPopupAsync<PopupDupTestPanel>();
            Task<PopupDupTestPanel> third = UIManager.OpenPopupAsync<PopupDupTestPanel>();

            Assert.IsNull(second.Result, "allowDuplicatePopup=true 时第二个同类型请求应排队并返回 null");
            Assert.IsNull(third.Result);
            StringAssert.Contains("队列(2): PopupDupTestPanel(10), PopupDupTestPanel(10)", GetDebugLine(2));

            UIManager.CloseTopPopup();
            Assert.AreEqual(1, Loader.PendingCount, "关闭当前弹窗后应只推进队列一次并开始加载下一个");

            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => UIManager.IsPanelVisible<PopupDupTestPanel>(), "重复弹窗打开完成", deadline)) yield return null;

            PopupDupTestPanel reopened = UIManager.GetOpenedPanel<PopupDupTestPanel>();
            Assert.NotNull(reopened);
            Assert.AreNotSame(first, reopened, "队列中的重复弹窗应作为新实例打开");
            Assert.AreEqual(UIPanelState.Closed, first.State);
            StringAssert.Contains("弹窗: PopupDupTestPanel", GetDebugLine(2));
            StringAssert.Contains("队列(1)", GetDebugLine(2));
        }

        /// <summary>
        /// 验证队列按 PopupPriority 降序出队、同类型去重，且一次关闭只推进一个弹窗。
        /// </summary>
        [UnityTest]
        public IEnumerator Queue_SortsByPriority_DedupsSameType()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;

            Task<PopupATestPanel> taskA = UIManager.OpenPopupAsync<PopupATestPanel>();
            Assert.AreEqual(1, Loader.PendingCount);
            Assert.IsFalse(taskA.IsCompleted, "手动模式下首个弹窗应停在加载中");

            // 加载期间再请求 B/C，B 优先级最高（100），C 为 50；B 重复请求应被去重。
            Task<PopupBTestPanel> queuedB = UIManager.OpenPopupAsync<PopupBTestPanel>();
            Task<PopupCTestPanel> queuedC = UIManager.OpenPopupAsync<PopupCTestPanel>();
            Task<PopupBTestPanel> queuedBDuplicate = UIManager.OpenPopupAsync<PopupBTestPanel>();
            Assert.IsNull(queuedB.Result);
            Assert.IsNull(queuedC.Result);
            Assert.IsNull(queuedBDuplicate.Result);
            StringAssert.Contains("队列(2): PopupBTestPanel(100), PopupCTestPanel(50)", GetDebugLine(2),
                "队列应按 PopupPriority 降序排列，且同类型重复请求只保留一个");

            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => taskA.IsCompleted, "弹窗 A 打开完成", deadline)) yield return null;
            PopupATestPanel popupA = taskA.GetAwaiter().GetResult();
            Assert.AreEqual(UIPanelState.Visible, popupA.State);
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup));
            StringAssert.Contains("弹窗: PopupATestPanel", GetDebugLine(2));
            StringAssert.Contains("队列(2)", GetDebugLine(2));

            UIManager.CloseTopPopup();
            Assert.AreEqual(1, Loader.GetLoadCount(PopupBAddress), "关闭 A 后应推进队列，开始加载 B");
            Assert.AreEqual(0, Loader.GetLoadCount(PopupCAddress), "同一次推进不应同时开始加载 C");
            StringAssert.Contains("队列(1): PopupCTestPanel(50)", GetDebugLine(2));

            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => UIManager.IsPanelVisible<PopupBTestPanel>(), "弹窗 B 打开完成", deadline)) yield return null;
            Assert.IsFalse(UIManager.HasOpenedPanel<PopupATestPanel>());
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup));

            UIManager.CloseTopPopup();
            Assert.AreEqual(1, Loader.GetLoadCount(PopupCAddress));
            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => UIManager.IsPanelVisible<PopupCTestPanel>(), "弹窗 C 打开完成", deadline)) yield return null;
            Assert.IsFalse(UIManager.HasOpenedPanel<PopupBTestPanel>());
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup), "任意时刻只允许一个弹窗可见");
        }

        /// <summary>
        /// 验证取消排队请求的 token 会立刻把它移出队列，且不影响已在加载的弹窗。
        /// </summary>
        [UnityTest]
        public IEnumerator Queue_CancelTokenRemovesQueuedRequest()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;

            Task<PopupATestPanel> taskA = UIManager.OpenPopupAsync<PopupATestPanel>();
            CancellationTokenSource cts = new CancellationTokenSource();
            Task<PopupBTestPanel> queuedB = UIManager.OpenPopupAsync<PopupBTestPanel>(null, cts.Token);
            Assert.IsNull(queuedB.Result);
            StringAssert.Contains("队列(1): PopupBTestPanel(100)", GetDebugLine(2));

            cts.Cancel();

            StringAssert.Contains("队列(0)", GetDebugLine(2), "取消排队请求的 token 后应立刻把它从队列移除");
            Assert.AreEqual(0, Loader.GetLoadCount(PopupBAddress));

            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => taskA.IsCompleted, "弹窗 A 打开完成", deadline)) yield return null;
            Assert.IsTrue(UIManager.IsPanelVisible<PopupATestPanel>());

            UIManager.CloseTopPopup();
            StringAssert.Contains("遮罩: 隐藏", GetDebugLine(2), "队列已空时关闭最后一个弹窗应隐藏遮罩");
            cts.Dispose();
        }

        /// <summary>
        /// 验证 CloseAllPopups 只让在途加载失效并清空队列；失效时队列只能推进一次（89505fc 修复 1）。
        /// </summary>
        [UnityTest]
        public IEnumerator CloseAllPopups_InterruptsLoading_AdvancesQueueOnlyOnce()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;

            Task<PopupATestPanel> taskA = UIManager.OpenPopupAsync<PopupATestPanel>();
            Assert.AreEqual(1, Loader.PendingCount);
            Assert.IsFalse(taskA.IsCompleted);

            // CloseAllPopups 只做 generation++ 和清队列，不中断在途加载。
            UIManager.CloseAllPopups();
            // 在 A 的加载仍挂起时重新入队两个不同弹窗（B 优先级高、C 低）。
            Task<PopupBTestPanel> queuedB = UIManager.OpenPopupAsync<PopupBTestPanel>();
            Task<PopupCTestPanel> queuedC = UIManager.OpenPopupAsync<PopupCTestPanel>();
            Assert.IsNull(queuedB.Result);
            Assert.IsNull(queuedC.Result);
            StringAssert.Contains("队列(2): PopupBTestPanel(100), PopupCTestPanel(50)", GetDebugLine(2));

            // 完成 A 的加载：A 因 generation 失效被取消；队列只能推进一次（只开始加载 B）。
            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => taskA.IsCompleted, "弹窗 A 请求收到取消", deadline)) yield return null;
            Assert.Catch<OperationCanceledException>(() => taskA.GetAwaiter().GetResult());
            PumpOnce();
            Assert.AreEqual(1, Loader.GetLoadCount(PopupBAddress), "失效后应推进队列一次并开始加载 B");
            Assert.AreEqual(0, Loader.GetLoadCount(PopupCAddress), "缺少修复时同一次失效会推进两次并开始加载 C");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Popup), "失效窗口期不应有弹窗残留可见");
            StringAssert.Contains("队列(1): PopupCTestPanel(50)", GetDebugLine(2));

            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => UIManager.IsPanelVisible<PopupBTestPanel>(), "弹窗 B 打开完成", deadline)) yield return null;
            Assert.IsFalse(UIManager.HasOpenedPanel<PopupATestPanel>());
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup), "任意时刻只允许一个弹窗可见");

            UIManager.CloseTopPopup();
            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => UIManager.IsPanelVisible<PopupCTestPanel>(), "弹窗 C 打开完成", deadline)) yield return null;
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup), "任意时刻只允许一个弹窗可见");
            StringAssert.Contains("弹窗: PopupCTestPanel", GetDebugLine(2));
        }
    }
}
