// 职责：面板打开/关闭/KeepInstance 复用/OnShow 异常回滚的生命周期回归测试。
// 依赖：FakeAssetLoader 与 TestPanelBase 的计数、事件轨迹。
// 限制：EditMode 下 GameObject 不会被真正销毁，因此只断言框架可观察状态。
using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CandyUI.Tests
{
    /// <summary>
    /// 面板生命周期：打开 → Visible；ForceClose 销毁并释放句柄；CanClose 拦截 UserClose；
    /// KeepInstance 复用实例并走 OnSleep/OnResume；OnShow 抛异常时状态回滚。
    /// </summary>
    public sealed class PanelLifecycleTests : UITestBase
    {
        private const string PlainAddress = "CandyUITests/PlainPanel";
        private const string KeepAliveAddress = "CandyUITests/KeepInstancePanel";

        /// <summary>
        /// 验证普通面板打开后状态为 Visible 并登记进打开表，资源只加载、创建一次。
        /// </summary>
        [Test]
        public void OpenPlainPanel_BecomesVisible_AndRegisters()
        {
            IgnoreEditModeObjectDestroyLogs();
            PlainTestPanel panel = UIManager.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();

            Assert.NotNull(panel);
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.IsTrue(UIManager.HasOpenedPanel<PlainTestPanel>());
            Assert.IsTrue(UIManager.IsPanelVisible<PlainTestPanel>());
            Assert.AreSame(panel, UIManager.GetOpenedPanel<PlainTestPanel>());
            Assert.AreEqual(UILayer.Main, panel.Layer);
            Assert.AreEqual(1, panel.CreateCount);
            Assert.AreEqual(1, panel.ShowCount);
            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress));
            Assert.AreEqual(1, Loader.GetCreatedCount(PlainAddress));
            StringAssert.Contains("加载中 0", GetDebugLine(0));
        }

        /// <summary>
        /// 验证 ForceClose 走销毁路径：状态 Closed、从打开表注销并释放资源句柄。
        /// </summary>
        [Test]
        public void Close_ForceClose_ClosesPanel_AndReleasesHandle()
        {
            IgnoreEditModeObjectDestroyLogs();
            PlainTestPanel panel = UIManager.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();

            UIManager.Close(panel, PanelCloseReason.ForceClose);

            Assert.AreEqual(UIPanelState.Closed, panel.State);
            Assert.IsFalse(panel.gameObject.activeSelf);
            Assert.IsFalse(UIManager.HasOpenedPanel<PlainTestPanel>());
            Assert.AreEqual(1, panel.CloseCount);
            Assert.AreEqual(1, panel.HideCount);
            // EditMode 下 GameObject 不会被真正销毁，因此这里断言"销毁路径"的框架可观察后果：句柄已释放、注册表已清。
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress), "面板关闭后资源句柄应被释放");
            StringAssert.Contains("面板: 打开 0", GetDebugLine(0));
        }

        /// <summary>
        /// 验证 CanClose 拦截 UserClose 后面板保持可见，而 ForceClose 必须绕过拦截。
        /// </summary>
        [Test]
        public void Close_UserClose_BlockedByCanClose_PanelStaysVisible()
        {
            IgnoreEditModeObjectDestroyLogs();
            CloseGuardTestPanel panel = UIManager.OpenPlainPanelAsync<CloseGuardTestPanel>().GetAwaiter().GetResult();

            UIManager.Close(panel, PanelCloseReason.UserClose);

            Assert.IsTrue(UIManager.HasOpenedPanel<CloseGuardTestPanel>(), "被 CanClose 拦截后面板仍在打开表里");
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.AreEqual(0, panel.CloseCount);
            Assert.AreEqual(0, panel.HideCount);
            Assert.AreEqual(0, Loader.GetReleaseCount("CandyUITests/CloseGuardPanel"));

            // ForceClose 必须绕过 CanClose。
            UIManager.Close(panel, PanelCloseReason.ForceClose);
            Assert.IsFalse(UIManager.HasOpenedPanel<CloseGuardTestPanel>());
            Assert.AreEqual(UIPanelState.Closed, panel.State);
            Assert.AreEqual(1, panel.CloseCount);
        }

        /// <summary>
        /// 验证 KeepInstance 关闭只休眠（OnSleep），重开复用实例并依次走 OnResume → OnShow。
        /// </summary>
        [Test]
        public void KeepInstance_CloseSleeps_ReopenResumesSameInstance()
        {
            IgnoreEditModeObjectDestroyLogs();
            KeepInstanceTestPanel first = UIManager.OpenPlainPanelAsync<KeepInstanceTestPanel>().GetAwaiter().GetResult();
            first.HideCount = 0;

            UIManager.Close(first, PanelCloseReason.UserClose);

            Assert.AreEqual(UIPanelState.Hidden, first.State);
            Assert.IsFalse(first.gameObject.activeSelf);
            Assert.AreEqual(1, first.HideCount);
            Assert.AreEqual(1, first.SleepCount, "KeepInstance 关闭必须回调 OnSleep");
            Assert.AreEqual(0, first.CloseCount, "KeepInstance 关闭不应走 OnClose/销毁路径");
            Assert.IsFalse(UIManager.HasOpenedPanel<KeepInstanceTestPanel>(), "关闭后应从打开表移出");
            Assert.AreEqual(0, Loader.GetReleaseCount(KeepAliveAddress), "KeepInstance 关闭不释放资源句柄");
            StringAssert.Contains("缓存实例 1", GetDebugLine(0));

            KeepInstanceTestPanel second = UIManager.OpenPlainPanelAsync<KeepInstanceTestPanel>().GetAwaiter().GetResult();

            Assert.AreSame(first, second, "KeepInstance 必须复用同一个面板实例");
            Assert.AreEqual(1, second.CreateCount, "复用实例不应重新 OnCreate");
            Assert.AreEqual(1, Loader.GetLoadCount(KeepAliveAddress), "复用实例不应重新加载资源");
            Assert.AreEqual(UIPanelState.Visible, second.State);
            Assert.AreEqual(1, second.ResumeCount, "再次打开必须先回调 OnResume");
            Assert.AreEqual(2, second.ShowCount);
            StringAssert.AreEqualIgnoringCase("Create,Show,Hide,Sleep,Resume,Show", second.EventTrace,
                "生命周期顺序应为 Sleep → Resume → Show");
        }

        /// <summary>
        /// 验证 OnShow 抛异常时状态回滚为 Created、隐藏实例并释放句柄，之后重试能成功打开。
        /// </summary>
        [UnityTest]
        public IEnumerator OnShowThrows_RollsBackState_AndCleansUp()
        {
            IgnoreEditModeObjectDestroyLogs();
            // 手动模式：加载完成后由测试显式放行，让 OnShow 抛异常发生在异步链路上；
            // 也避免用"同步完成 loader"触发 AssetCache 的已知边界问题（见测试报告）。
            Loader.ManualMode = true;

            Task<ThrowOnShowTestPanel> failedTask = UIManager.OpenPlainPanelAsync<ThrowOnShowTestPanel>();
            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => failedTask.IsCompleted, "OnShow 异常返回", deadline)) yield return null;
            Assert.Throws<InvalidOperationException>(() => failedTask.GetAwaiter().GetResult());

            Assert.IsFalse(UIManager.HasOpenedPanel<ThrowOnShowTestPanel>(), "失败的面板不应留在打开表");
            Assert.AreEqual(0, TestPanelBase.CountVisible(UILayer.Main), "失败的面板不应可见");
            Assert.AreEqual(1, Loader.GetReleaseCount("CandyUITests/ThrowOnShowPanel"), "失败路径必须释放已加载句柄");
            StringAssert.Contains("面板: 打开 0", GetDebugLine(0));
            StringAssert.Contains("加载中 0", GetDebugLine(0));

            // EditMode 下 Object.Destroy 不生效，可以从残留实例检查内部状态回滚是否正确
            // （旧实现会停留在 Showing；修复后按 stateBeforeShow=Created 回滚且 SetActive(false)）。
            ThrowOnShowTestPanel leftover = null;
            foreach (ThrowOnShowTestPanel candidate in UnityEngine.Object.FindObjectsOfType<ThrowOnShowTestPanel>(true))
            {
                if (candidate.State == UIPanelState.None) continue; // 跳过 FakeAssetLoader 造的模板对象
                leftover = candidate;
                break;
            }

            Assert.NotNull(leftover, "找不到 OnShow 失败后的残留实例（EditMode 下 Object.Destroy 无效，应仍存在）");
            Assert.AreEqual(UIPanelState.Created, leftover.State, "OnShow 异常后 State 不应停留在 Showing");
            Assert.IsFalse(leftover.gameObject.activeSelf, "状态回滚后应隐藏实例");

            // 修复后再次打开应可成功（失败路径已清理 loading/opened 注册）。
            Task<ThrowOnShowTestPanel> retryTask = UIManager.OpenPlainPanelAsync<ThrowOnShowTestPanel>();
            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => retryTask.IsCompleted, "重试打开完成", deadline)) yield return null;

            ThrowOnShowTestPanel retried = retryTask.GetAwaiter().GetResult();
            Assert.NotNull(retried);
            Assert.AreEqual(UIPanelState.Visible, retried.State);
        }
    }
}
