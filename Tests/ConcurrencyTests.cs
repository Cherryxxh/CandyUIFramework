// 职责：同一面板并发打开的回归测试，验证共享加载 Task 与各调用方 token 相互解耦。
// 依赖：FakeAssetLoader.ManualMode 控制加载完成时机，UITestBase 提供等待工具。
// 限制：只覆盖 EditMode 下的确定性时序，不覆盖真实帧循环。
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CandyUI.Tests
{
    /// <summary>
    /// 并发打开同一面板：共享加载 Task 与调用方 token 解耦（TaskWait.WithCancellation）。
    /// 第一个调用方取消 token 后，第二个调用方必须仍能拿到面板（89505fc 修复 3 的回归用例）。
    /// </summary>
    public sealed class ConcurrencyTests : UITestBase
    {
        private const string PlainAddress = "CandyUITests/PlainPanel";

        /// <summary>
        /// 验证第一个调用方取消 token 后，第二个调用方仍复用共享加载并拿到面板（89505fc 修复 3）。
        /// </summary>
        [UnityTest]
        public IEnumerator ConcurrentOpen_FirstCallerCancels_SecondStillGetsPanel()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;

            CancellationTokenSource cts = new CancellationTokenSource();
            Task<PlainTestPanel> firstTask = UIManager.OpenPlainPanelAsync<PlainTestPanel>(null, cts.Token);
            Task<PlainTestPanel> secondTask = UIManager.OpenPlainPanelAsync<PlainTestPanel>();

            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress), "并发打开同一面板只能发起一次加载");
            Assert.AreEqual(1, Loader.PendingCount);
            Assert.IsFalse(firstTask.IsCompleted);
            Assert.IsFalse(secondTask.IsCompleted);

            cts.Cancel();

            float deadline = BeginWait();
            while (!AdvanceWait(() => firstTask.IsCompleted, "第一个调用方收到取消", deadline)) yield return null;
            Assert.Catch<OperationCanceledException>(() => firstTask.GetAwaiter().GetResult());
            Assert.IsFalse(secondTask.IsCompleted, "第一个调用方取消 token 不应连带取消第二个调用方");

            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => secondTask.IsCompleted, "第二个调用方拿到面板", deadline)) yield return null;

            PlainTestPanel panel = secondTask.GetAwaiter().GetResult();
            Assert.NotNull(panel);
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            Assert.IsTrue(UIManager.HasOpenedPanel<PlainTestPanel>());
            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress), "第二个调用方复用了共享加载，不应重复加载");
            Assert.AreEqual(0, Loader.GetReleaseCount(PlainAddress));

            cts.Dispose();
        }

        /// <summary>
        /// 验证两个并发调用方等待同一面板时拿到同一个实例，且只走一次创建与显示。
        /// </summary>
        [UnityTest]
        public IEnumerator ConcurrentOpen_TwoCallers_GetSamePanelInstance()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;

            Task<PlainTestPanel> firstTask = UIManager.OpenPlainPanelAsync<PlainTestPanel>();
            Task<PlainTestPanel> secondTask = UIManager.OpenPlainPanelAsync<PlainTestPanel>();
            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress));

            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => firstTask.IsCompleted && secondTask.IsCompleted, "两个调用方都完成", deadline))
                yield return null;

            PlainTestPanel first = firstTask.GetAwaiter().GetResult();
            PlainTestPanel second = secondTask.GetAwaiter().GetResult();
            Assert.AreSame(first, second, "并发等待者应拿到同一个面板实例");
            Assert.AreEqual(1, Loader.GetCreatedCount(PlainAddress));
            Assert.AreEqual(1, first.CreateCount);
            Assert.AreEqual(1, first.ShowCount);
        }
    }
}
