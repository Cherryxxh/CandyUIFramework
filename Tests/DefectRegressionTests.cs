// 职责：阶段5 修复缺陷的正式回归用例集，每个方法锁一个缺陷的触发条件与收尾行为。
// 依赖：FakeAssetLoader.ManualMode 构造“加载挂起/取消”时序，UITestBase 提供反射与等待工具。
// 限制：断言基于可观察状态（注册表/State/句柄计数），不依赖 EditMode 下无效的 GameObject 销毁。
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CandyUI.Tests
{
    /// <summary>
    /// 阶段5 修复的 5 个缺陷的正式回归用例。
    /// 修复前（3e52865）这些用例全红，修复后（e8e847f）全绿；命名与注释标明各自回归的缺陷与触发条件。
    /// 行为契约（fixer2 声明的行为变更）：
    /// 1) OnShow/Presenter.OnShow 里关闭自己后，Open*Async 抛 OperationCanceledException，不再返回已关闭实例；
    /// 2) KeepInstance 缓存重开失败时，实例回滚到缓存表，并补调一次 OnSleep 与 OnResume 成对；
    /// 3) 弹窗首开被取消后，共享创建完成的实例必须被收尾释放（不留幽灵弹窗）；
    /// 4) AssetCache 中取消的等待者不得影响其它等待者；同步完成 loader 不留陈旧加载登记。
    /// </summary>
    public sealed class DefectRegressionTests : UITestBase
    {
        private const string PlainAddress = "CandyUITests/PlainPanel";
        private const string PopupAAddress = "CandyUITests/PopupA";
        private const string ReentrantAddress = "CandyUITests/ReentrantClosePanel";
        private const string KeepThrowAddress = "CandyUITests/KeepInstanceThrowPanel";
        private const string WindowAAddress = "CandyUITests/WindowA";
        private const string WindowBAddress = "CandyUITests/WindowB";

        /// <summary>AssetCache 是 internal 类型，Loaded 通过基类反射辅助调用，签名保持与框架一致。</summary>
        private static Task<IAssetHandle> LoadAsync(
            object cache, string address, AssetCachePolicy policy, CancellationToken cancellationToken)
        {
            return (Task<IAssetHandle>)InvokeInstanceMethod(
                cache, "LoadAsync", address, policy, cancellationToken);
        }

        /// <summary>
        /// 读取缓存条目数，用于断言加载完成与最后一个引用释放后的缓存状态。
        /// </summary>
        private static int GetCacheCount(object cache)
        {
            return (int)GetInstanceProperty(cache, "Count");
        }

        /// <summary>
        /// 读取加载中登记数，用于锁住“同步完成 loader 不留陈旧登记”（缺陷 #5）等回归。
        /// </summary>
        private static int GetLoadingCount(object cache)
        {
            return (int)GetInstanceProperty(cache, "LoadingCount");
        }

        /// <summary>
        /// 缺陷 #1（高，弹窗幽灵实例）：弹窗首次加载期间唯一调用方取消 token，共享创建仍会完成并显示该弹窗，
        /// 而 PopupService 已抛 OCE、_active 未设、遮罩未显示，没人跟踪这个"幽灵"；再开别的弹窗会两个弹窗同屏。
        /// 修复后：共享创建结束后由 CleanupAbandonedOpening 收尾（ForceClose + 从打开表/句柄清理）。
        /// </summary>
        [UnityTest]
        public IEnumerator Defect1_CancelledPopupLoad_LeavesNoGhostPopup()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Task<PopupATestPanel> cancelled = UIManager.OpenPopupAsync<PopupATestPanel>(null, cts.Token);
                Assert.AreEqual(1, Loader.PendingCount, "首弹应处于挂起加载中（先别放行加载）");
                Assert.IsFalse(cancelled.IsCompleted, "加载未完成时 OpenPopupAsync 不应完成");

                cts.Cancel();

                float deadline = BeginWait();
                while (!AdvanceWait(() => cancelled.IsCompleted, "取消的弹窗打开返回", deadline))
                    yield return null;

                Assert.Catch<OperationCanceledException>(
                    () => cancelled.GetAwaiter().GetResult(), "唯一调用方取消后应收到 OperationCanceledException");
            }

            Assert.IsFalse(UIManager.HasOpenedPanel<PopupATestPanel>(), "取消后不该有已登记弹窗");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Popup), "取消后不应有可见弹窗");

            // 放行共享加载：修复前会在这里创建一个 Visible 但没人跟踪的幽灵弹窗。
            Loader.CompleteNext();

            float cleanupDeadline = BeginWait();
            while (!AdvanceWait(
                       () => !UIManager.HasOpenedPanel<PopupATestPanel>()
                             && Loader.GetReleaseCount(PopupAAddress) >= 1,
                       "取消打开的共享创建收尾（清理幽灵弹窗）",
                       cleanupDeadline))
                yield return null;

            Assert.IsFalse(UIManager.HasOpenedPanel<PopupATestPanel>(), "幽灵弹窗必须从打开表清理");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Popup), "幽灵弹窗不能变成可见实例");
            Assert.AreEqual(1, Loader.GetReleaseCount(PopupAAddress), "幽灵弹窗句柄必须且只能释放一次");
            StringAssert.Contains("面板: 打开 0", GetDebugLine(0));
            StringAssert.Contains("弹窗: 无", GetDebugLine(2));
            StringAssert.Contains("遮罩: 隐藏", GetDebugLine(2));

            // 再开别的弹窗：修复前会两个弹窗同屏。
            Task<PopupBTestPanel> next = UIManager.OpenPopupAsync<PopupBTestPanel>();
            Assert.AreEqual(1, Loader.PendingCount, "第二个弹窗应只有一次挂起加载");
            Loader.CompleteNext();

            float openDeadline = BeginWait();
            while (!AdvanceWait(() => next.IsCompleted, "第二个弹窗打开完成", openDeadline))
                yield return null;

            PopupBTestPanel popupB = next.GetAwaiter().GetResult();
            Assert.NotNull(popupB, "取消收尾后再次打开弹窗应成功");
            Assert.AreEqual(UIPanelState.Visible, popupB.State);
            Assert.IsTrue(UIManager.HasOpenedPanel<PopupBTestPanel>());
            Assert.IsFalse(UIManager.HasOpenedPanel<PopupATestPanel>(), "被取消的旧弹窗不能复活");
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup), "同一时刻只能有一个弹窗可见");
            StringAssert.Contains("弹窗: PopupBTestPanel", GetDebugLine(2));
        }

        /// <summary>
        /// 缺陷 #2（中，AssetCache 并发取消竞态）：A 首次加载中取消，B 同时等待同一地址；
        /// 修复前 A 的取消分支把整个条目标记为"完成后释放"，B 恢复后看到 Handle==null 抛"条目已失效"。
        /// 修复后：取消者只注销自己的等待，B 仍能拿到有效句柄。
        /// </summary>
        [UnityTest]
        public IEnumerator Defect2_CancelledWaiter_DoesNotBreakOtherWaiter()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;
            object cache = GetAssetCache();

            Task<IAssetHandle> cancelled;
            Task<IAssetHandle> other;
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cancelled = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose, cts.Token);
                other = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose, CancellationToken.None);

                Assert.AreEqual(1, Loader.PendingCount, "同一地址只应发起一次底层加载");
                Assert.AreEqual(1, GetLoadingCount(cache), "加载中登记应为 1");
                Assert.IsFalse(cancelled.IsCompleted);
                Assert.IsFalse(other.IsCompleted);

                cts.Cancel();

                float cancelDeadline = BeginWait();
                while (!AdvanceWait(() => cancelled.IsCompleted, "取消的加载返回", cancelDeadline))
                    yield return null;

                Assert.Catch<OperationCanceledException>(
                    () => cancelled.GetAwaiter().GetResult(), "带已取消 token 的等待应抛 OperationCanceledException");
                Assert.IsFalse(other.IsCompleted, "另一个等待者不能跟着被取消");
            }

            Loader.CompleteNext();

            float loadDeadline = BeginWait();
            while (!AdvanceWait(() => other.IsCompleted, "仍在等待的加载完成", loadDeadline))
                yield return null;

            // 修复前：这里抛 InvalidOperationException("[CandyUI] 资源缓存条目已失效：...")。
            IAssetHandle handle = other.GetAwaiter().GetResult();
            Assert.NotNull(handle, "取消者离开后，剩余等待者仍应拿到有效句柄");
            Assert.AreEqual(0, GetLoadingCount(cache), "加载完成后不应残留加载中登记");
            Assert.AreEqual(1, GetCacheCount(cache), "加载完成后缓存应有 1 个条目");

            handle.Release();
            Assert.AreEqual(0, GetCacheCount(cache), "最后一个引用释放后应卸载条目");
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress), "底层句柄应释放一次");
        }

        /// <summary>
        /// 缺陷 #3（中，OnShow 内重入关闭 - 普通面板）：OnShow 里 CloseSelf 后，修复前 InternalShow 仍把
        /// State 置回 Visible 并播入场，但面板已不在打开表；修复后本次显示作废、抛 OCE、清理句柄。
        /// </summary>
        [Test]
        public void Defect3_OnShowSelfClose_BecomesCancelledShow_NotVisibleGhost()
        {
            IgnoreEditModeObjectDestroyLogs();
            ReentrantCloseTestPanel.CloseOnNextShow = true;

            Task<ReentrantCloseTestPanel> failed = UIManager.OpenPlainPanelAsync<ReentrantCloseTestPanel>();

            Assert.Catch<OperationCanceledException>(
                () => failed.GetAwaiter().GetResult(),
                "OnShow 里关闭自己后，OpenPlainPanelAsync 应按取消语义抛 OperationCanceledException，不能返回已关闭实例");
            Assert.IsFalse(UIManager.HasOpenedPanel<ReentrantCloseTestPanel>(), "作废的显示不能留在打开表");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Main), "自己关掉的面板不能可见");
            Assert.AreEqual(1, Loader.GetReleaseCount(ReentrantAddress), "作废的显示必须释放句柄");
            StringAssert.Contains("面板: 打开 0", GetDebugLine(0));
            StringAssert.Contains("加载中 0", GetDebugLine(0));

            // 开关已复位：再开一次应正常，且不残留上一次的登记。
            ReentrantCloseTestPanel retried =
                UIManager.OpenPlainPanelAsync<ReentrantCloseTestPanel>().GetAwaiter().GetResult();
            Assert.NotNull(retried);
            Assert.AreEqual(UIPanelState.Visible, retried.State);
            Assert.IsTrue(UIManager.HasOpenedPanel<ReentrantCloseTestPanel>());
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Main));
            Assert.AreEqual(1, retried.ShowCount);
        }

        /// <summary>
        /// 缺陷 #3（弹窗场景）：OnShow 里关闭自己时，修复前 _active 指向未登记面板，CloseTopPopup 卡住、
        /// 遮罩与队列滞留；修复后弹窗服务不卡住，下一个弹窗能立即显示。
        /// </summary>
        [Test]
        public void Defect3_PopupOnShowSelfClose_DoesNotStallPopupService()
        {
            IgnoreEditModeObjectDestroyLogs();
            ReentrantClosePopupTestPanel.CloseOnNextShow = true;

            Task<ReentrantClosePopupTestPanel> failed =
                UIManager.OpenPopupAsync<ReentrantClosePopupTestPanel>();

            Assert.Catch<OperationCanceledException>(
                () => failed.GetAwaiter().GetResult(),
                "弹窗 OnShow 里关闭自己后，OpenPopupAsync 应按取消语义抛 OperationCanceledException");

            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Popup), "作废弹窗不能可见");
            StringAssert.Contains("弹窗: 无", GetDebugLine(2));
            StringAssert.Contains("遮罩: 隐藏", GetDebugLine(2));

            // 弹窗服务没有卡在 _loading：下一个弹窗应能立即显示。
            PopupATestPanel next = UIManager.OpenPopupAsync<PopupATestPanel>().GetAwaiter().GetResult();
            Assert.NotNull(next, "作废弹窗收尾后，下一个弹窗应能正常打开");
            Assert.AreEqual(UIPanelState.Visible, next.State);
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Popup));
            StringAssert.Contains("弹窗: PopupATestPanel", GetDebugLine(2));
            UIManager.CloseTopPopup();
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Popup));
        }

        /// <summary>
        /// 缺陷 #4（中，KeepInstance 缓存重开失败无回滚）：缓存分支把实例移入打开表后 OnShow 抛异常，
        /// 修复前实例留在打开表（Hidden/inactive）、既不在缓存表也没释放句柄；
        /// 修复后回滚到缓存表并补调一次 OnSleep（与 InternalResume 成对），再次打开复用原实例。
        /// </summary>
        [Test]
        public void Defect4_KeepInstanceReopenThrows_RollsBackToCache()
        {
            IgnoreEditModeObjectDestroyLogs();

            KeepInstanceThrowOnShowTestPanel first =
                UIManager.OpenPlainPanelAsync<KeepInstanceThrowOnShowTestPanel>().GetAwaiter().GetResult();
            Assert.NotNull(first);
            UIManager.Close(first, PanelCloseReason.UserClose);
            Assert.IsFalse(UIManager.HasOpenedPanel<KeepInstanceThrowOnShowTestPanel>());
            StringAssert.Contains("缓存实例 1", GetDebugLine(0));
            Assert.AreEqual(1, first.SleepCount, "KeepInstance 关闭应调用一次 OnSleep");

            KeepInstanceThrowOnShowTestPanel.ThrowOnNextShow = true;

            Task<KeepInstanceThrowOnShowTestPanel> reopened =
                UIManager.OpenPlainPanelAsync<KeepInstanceThrowOnShowTestPanel>();
            Assert.Throws<InvalidOperationException>(() => reopened.GetAwaiter().GetResult(),
                "重开的 OnShow 抛异常时应把异常原样抛给调用方");

            // 修复前：实例留在打开表（Hidden/inactive），既不在缓存表，也不释放句柄。
            Assert.IsFalse(UIManager.HasOpenedPanel<KeepInstanceThrowOnShowTestPanel>(),
                "失败的缓存重开不能把实例留在打开表");
            StringAssert.Contains("面板: 打开 0", GetDebugLine(0));
            StringAssert.Contains("缓存实例 1", GetDebugLine(0), "失败后实例应回滚到缓存表");
            Assert.AreEqual(0, Loader.GetReleaseCount(KeepThrowAddress), "KeepInstance 缓存期间句柄不应释放");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Main));
            Assert.AreEqual(1, first.ResumeCount, "失败的重开调用了一次 OnResume");
            Assert.AreEqual(2, first.SleepCount, "回滚应补调一次 OnSleep，与 OnResume 成对");

            // 再开一次应复用同一个缓存实例。
            KeepInstanceThrowOnShowTestPanel retried =
                UIManager.OpenPlainPanelAsync<KeepInstanceThrowOnShowTestPanel>().GetAwaiter().GetResult();
            Assert.AreSame(first, retried, "失败回滚后应复用原缓存实例");
            Assert.AreEqual(UIPanelState.Visible, retried.State);
            Assert.AreEqual(1, retried.CreateCount, "复用缓存实例不应重新走 OnCreate");
            Assert.AreEqual(1, Loader.GetLoadCount(KeepThrowAddress), "复用缓存实例不应重新加载");
            Assert.AreEqual(2, retried.ResumeCount);
        }

        /// <summary>
        /// 缺陷 #5（高，同步完成 loader 的陈旧 _dicLoading 登记）：首次加载时 loader 同步完成，
        /// LoadAndStoreAsync 的 finally 先移除登记、外层赋值又把已完成的 task 登记回去；
        /// 条目释放到 0 后再次加载会命中陈旧登记并抛"资源缓存条目已失效"。修复后不留已完成登记。
        /// </summary>
        [Test]
        public void Defect5_SyncLoader_DoesNotLeaveStaleLoadingRegistration()
        {
            IgnoreEditModeObjectDestroyLogs();
            object cache = GetAssetCache();

            IAssetHandle first = LoadAsync(
                    cache, PlainAddress, AssetCachePolicy.ReleaseOnClose, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.NotNull(first);
            Assert.AreEqual(0, GetLoadingCount(cache), "同步完成后不能留下 _dicLoading 登记");

            first.Release();
            Assert.AreEqual(0, GetCacheCount(cache));

            // 修复前：这里命中陈旧登记，抛 InvalidOperationException("[CandyUI] 资源缓存条目已失效：..."）。
            IAssetHandle second = LoadAsync(
                    cache, PlainAddress, AssetCachePolicy.ReleaseOnClose, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.NotNull(second, "归零卸载后再次加载应重新走底层加载，不能命中新的陈旧登记");
            Assert.AreEqual(2, Loader.GetLoadCount(PlainAddress), "应触发第二次底层加载");
            Assert.AreEqual(1, GetCacheCount(cache));

            second.Release();
            Assert.AreEqual(0, GetCacheCount(cache));
            Assert.AreEqual(2, Loader.GetReleaseCount(PlainAddress));
        }

        /// <summary>
        /// 缺陷 #6（高，普通面板幽灵实例）：普通面板首次加载期间唯一调用方取消 token，
        /// 共享创建仍会完成并 InternalShow 面板，而已取消的调用方拿不到引用；修复前没有调用方
        /// 调用 CleanupAbandonedOpening，面板以"已打开"停在打开表里，既没人跟踪也没法按预期关闭。
        /// 修复后：取消由 PanelService 内部统一收尾（最后一个等待者取消 → 共享创建结束后
        /// 走 DestroyInfo 销毁），打开表 / 可见性 / 句柄 / 调试面板全部干净，且下一次打开不受残留标记影响。
        /// </summary>
        [UnityTest]
        public IEnumerator Defect6_CancelledPlainPanelOpen_LeavesNoPanel()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Task<PlainTestPanel> cancelled = UIManager.OpenPlainPanelAsync<PlainTestPanel>(null, cts.Token);
                Assert.AreEqual(1, Loader.PendingCount, "普通面板应处于挂起加载中（先别放行加载）");
                Assert.IsFalse(cancelled.IsCompleted, "加载未完成时 OpenPlainPanelAsync 不应完成");

                cts.Cancel();

                float cancelDeadline = BeginWait();
                while (!AdvanceWait(() => cancelled.IsCompleted, "取消的普通面板打开返回", cancelDeadline))
                    yield return null;

                Assert.Catch<OperationCanceledException>(
                    () => cancelled.GetAwaiter().GetResult(), "唯一调用方取消后应收到 OperationCanceledException");
            }

            Assert.IsFalse(UIManager.HasOpenedPanel<PlainTestPanel>(), "取消后不应登记普通面板");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Main), "取消后不应有可见普通面板");
            StringAssert.Contains("加载中 1", GetDebugLine(0), "共享创建仍应继续（取消不打断底层加载）");

            // 放行共享加载：修复前这里会创建一个 Visible 但没人跟踪、也没人释放的幽灵普通面板。
            Loader.CompleteNext();

            float cleanupDeadline = BeginWait();
            while (!AdvanceWait(
                       () => !UIManager.HasOpenedPanel<PlainTestPanel>()
                             && Loader.GetReleaseCount(PlainAddress) >= 1
                             && TestPanelBase.CountVisible(UILayer.Main) == 0,
                       "取消打开的共享创建收尾（清理幽灵普通面板）",
                       cleanupDeadline))
                yield return null;

            Assert.IsFalse(UIManager.HasOpenedPanel<PlainTestPanel>(), "幽灵普通面板必须从打开表清理");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Main), "幽灵普通面板不能变成可见实例");
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress), "幽灵普通面板句柄必须且只能释放一次");
            StringAssert.Contains("面板: 打开 0", GetDebugLine(0));
            StringAssert.Contains("加载中 0", GetDebugLine(0));
            StringAssert.Contains("缓存实例 0", GetDebugLine(0), "收尾必须走销毁路径，不能进 KeepInstance 缓存");

            // 再开一次：修复前打开表里的幽灵会影响后续；修复后应重新创建并正常显示。
            Task<PlainTestPanel> reopened = UIManager.OpenPlainPanelAsync<PlainTestPanel>();
            Assert.AreEqual(1, Loader.PendingCount, "第二次打开应只有一次挂起加载");
            Loader.CompleteNext();

            float openDeadline = BeginWait();
            while (!AdvanceWait(() => reopened.IsCompleted, "再次打开普通面板完成", openDeadline))
                yield return null;

            PlainTestPanel panel = reopened.GetAwaiter().GetResult();
            Assert.NotNull(panel, "取消收尾后再次打开普通面板应成功");
            Assert.AreEqual(UIPanelState.Visible, panel.State, "再次打开的面板应可见");
            Assert.IsTrue(UIManager.HasOpenedPanel<PlainTestPanel>(), "再次打开的面板应登记在打开表");
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Main), "同一时刻只应有 1 个可见普通面板");
            Assert.AreEqual(1, panel.CreateCount, "再次打开必须重新创建（旧实例已被销毁，不能复用幽灵）");
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress), "新实例的句柄不能跟着被收尾释放");
            StringAssert.Contains("面板: 打开 1", GetDebugLine(0));
        }

        /// <summary>
        /// 缺陷 #6（高，窗口幽灵实例）：WindowService.OpenWindowAsync 先 await PanelService 打开拿到
        /// panel，之后才 hide 上一个窗口并 push 进栈。唯一等待者取消时共享创建仍会完成并 InternalShow
        /// 窗口，但永远不会入栈：Peek() 拿不到它，TryHidePanel 因"非栈顶"拒绝，NavigateBack 只会关掉
        /// 下面那个窗口。修复后取消由 PanelService 统一收尾，窗口栈与上一个窗口的显示状态不受影响。
        /// </summary>
        [UnityTest]
        public IEnumerator Defect6_CancelledWindowOpen_LeavesNoWindowAndStackUnchanged()
        {
            IgnoreEditModeObjectDestroyLogs();

            WindowATestPanel windowA =
                UIManager.OpenWindowAsync<WindowATestPanel>().GetAwaiter().GetResult();
            Assert.NotNull(windowA);
            StringAssert.Contains("窗口栈(1): WindowATestPanel", GetDebugLine(1));
            Assert.IsFalse(UIManager.CanNavigateBack, "只有一层窗口时不应可返回");

            Loader.ManualMode = true;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Task<WindowBTestPanel> cancelled = UIManager.OpenWindowAsync<WindowBTestPanel>(null, cts.Token);
                Assert.AreEqual(1, Loader.PendingCount, "窗口 B 应处于挂起加载中");
                Assert.IsFalse(cancelled.IsCompleted, "加载未完成时 OpenWindowAsync 不应完成");

                cts.Cancel();

                float cancelDeadline = BeginWait();
                while (!AdvanceWait(() => cancelled.IsCompleted, "取消的窗口打开返回", cancelDeadline))
                    yield return null;

                Assert.Catch<OperationCanceledException>(
                    () => cancelled.GetAwaiter().GetResult(), "唯一调用方取消后应收到 OperationCanceledException");
            }

            // 取消时 push / hide 都还没执行：栈与上一个窗口的显示状态必须原样。
            StringAssert.Contains("窗口栈(1): WindowATestPanel", GetDebugLine(1), "取消后窗口栈不能变");
            Assert.IsFalse(UIManager.CanNavigateBack);
            Assert.AreEqual(UIPanelState.Visible, windowA.State, "上一个窗口不能被隐藏");
            Assert.AreEqual(1, windowA.ShowCount, "上一个窗口不应被重新显示");
            Assert.AreEqual(0, windowA.HideCount, "上一个窗口不应被隐藏");
            Assert.IsFalse(UIManager.HasOpenedPanel<WindowBTestPanel>(), "取消后不应登记窗口 B");
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Window), "此时只应有窗口 A 可见");

            // 放行共享加载：修复前窗口 B 会创建并显示，但永远不入栈（幽灵窗口）。
            Loader.CompleteNext();

            float cleanupDeadline = BeginWait();
            while (!AdvanceWait(
                       () => !UIManager.HasOpenedPanel<WindowBTestPanel>()
                             && Loader.GetReleaseCount(WindowBAddress) >= 1
                             && TestPanelBase.CountVisible(UILayer.Window) == 1,
                       "取消打开的共享创建收尾（清理幽灵窗口）",
                       cleanupDeadline))
                yield return null;

            StringAssert.Contains("窗口栈(1): WindowATestPanel", GetDebugLine(1), "幽灵窗口不能入栈");
            Assert.IsFalse(UIManager.HasOpenedPanel<WindowBTestPanel>(), "幽灵窗口必须从打开表清理");
            Assert.AreEqual(1, Loader.GetReleaseCount(WindowBAddress), "幽灵窗口句柄必须且只能释放一次");
            Assert.AreEqual(UIPanelState.Visible, windowA.State, "收尾后上一个窗口应保持可见");
            Assert.AreEqual(1, windowA.ShowCount, "收尾不应重新显示上一个窗口");
            Assert.AreEqual(0, windowA.HideCount, "收尾不应隐藏上一个窗口");
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Window), "收尾后只应有窗口 A 可见");

            // 再正常打开窗口 B：这次应正常入栈并隐藏 A；返回后 A 恢复。
            Task<WindowBTestPanel> reopened = UIManager.OpenWindowAsync<WindowBTestPanel>();
            Assert.AreEqual(1, Loader.PendingCount, "再次打开窗口 B 应只有一次挂起加载");
            Loader.CompleteNext();

            float openDeadline = BeginWait();
            while (!AdvanceWait(() => reopened.IsCompleted, "再次打开窗口 B 完成", openDeadline))
                yield return null;

            WindowBTestPanel windowB = reopened.GetAwaiter().GetResult();
            Assert.NotNull(windowB, "取消收尾后再次打开窗口 B 应成功");
            StringAssert.Contains("窗口栈(2): WindowATestPanel -> WindowBTestPanel", GetDebugLine(1));
            Assert.AreEqual(UIPanelState.Hidden, windowA.State, "打开新窗口应隐藏上一个窗口");
            Assert.AreEqual(1, windowA.HideCount);
            Assert.AreEqual(UIPanelState.Visible, windowB.State);
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Window), "同一时刻只应有栈顶窗口可见");

            UIManager.NavigateBack();

            StringAssert.Contains("窗口栈(1): WindowATestPanel", GetDebugLine(1));
            Assert.IsFalse(UIManager.HasOpenedPanel<WindowBTestPanel>(), "返回后窗口 B 应关闭");
            Assert.AreEqual(UIPanelState.Visible, windowA.State, "返回后上一个窗口应恢复显示");
            Assert.AreEqual(2, windowA.ShowCount, "返回时应重新显示上一个窗口");
            Assert.AreEqual(1, TestPanelBase.CountVisible(UILayer.Window));
            Assert.AreEqual(2, Loader.GetReleaseCount(WindowBAddress), "窗口 B 关闭后句柄累计释放两次");
        }
    }
}
