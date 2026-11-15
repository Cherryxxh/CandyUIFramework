// 职责：AssetCache 引用计数与并发加载语义的回归测试（反射直测 internal 类型）。
// 依赖：FakeAssetLoader 的 ManualMode 与统计接口，以及 UITestBase 的反射辅助方法。
// 限制：同步完成 loader 的已知边界由 DefectRegressionTests 覆盖，这里专注引用计数与并发语义。
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CandyUI.Tests
{
    /// <summary>
    /// AssetCache（PanelService 内部，internal 类型，用反射直测）：
    /// 同地址并发/连续加载只触发一次底层加载、引用计数、KeepAlive 固定不卸载、归零才卸载；
    /// 以及通过公开门面（PreloadAsync + 面板开关）验证同样的语义。
    /// </summary>
    public sealed class AssetCacheTests : UITestBase
    {
        private const string PlainAddress = "CandyUITests/PlainPanel";

        /// <summary>
        /// 反射调用 AssetCache.LoadAsync（签名与框架一致），便于直接按指定 policy 加载句柄。
        /// </summary>
        private static Task<IAssetHandle> LoadAsync(object cache, string address, AssetCachePolicy policy)
        {
            return (Task<IAssetHandle>)InvokeInstanceMethod(cache, "LoadAsync", address, policy, CancellationToken.None);
        }

        /// <summary>
        /// 读取缓存条目数，用于断言引用计数归零后条目被移除。
        /// </summary>
        private static int GetCount(object cache)
        {
            return (int)GetInstanceProperty(cache, "Count");
        }

        /// <summary>
        /// 读取加载中登记数，用于断言同地址并发只保留一次底层加载。
        /// </summary>
        private static int GetLoadingCount(object cache)
        {
            return (int)GetInstanceProperty(cache, "LoadingCount");
        }

        /// <summary>
        /// 验证同地址两次加载只触发一次底层加载，且引用计数归零才移除条目并释放句柄。
        /// </summary>
        [Test]
        public void SameAddress_TwoLoads_OnlyOneUnderlyingLoad_AndReleaseAtZero()
        {
            IgnoreEditModeObjectDestroyLogs();
            object cache = GetAssetCache();

            IAssetHandle first = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose).GetAwaiter().GetResult();
            IAssetHandle second = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose).GetAwaiter().GetResult();

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress), "同地址两次 LoadAsync 只能触发一次底层加载");
            Assert.AreEqual(1, GetCount(cache), "缓存里应只有一条记录");
            Assert.AreEqual(1, Loader.GetCreatedCount(PlainAddress));

            first.Release();
            Assert.AreEqual(1, GetCount(cache), "引用计数未归零不应卸载");
            Assert.AreEqual(0, Loader.GetReleaseCount(PlainAddress));

            second.Release();
            Assert.AreEqual(0, GetCount(cache), "引用计数归零应移除缓存条目");
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress), "引用计数归零应释放底层句柄");
        }

        /// <summary>
        /// 验证 KeepAlive 策略固定资源：释放引用不卸载，ReleaseAll 才真正释放并清空条目。
        /// </summary>
        [Test]
        public void KeepAlive_ReleaseDoesNotUnload_UntilReleaseAll()
        {
            IgnoreEditModeObjectDestroyLogs();
            object cache = GetAssetCache();

            IAssetHandle keepAlive = LoadAsync(cache, PlainAddress, AssetCachePolicy.KeepAlive).GetAwaiter().GetResult();
            keepAlive.Release();

            Assert.AreEqual(1, GetCount(cache), "KeepAlive 的引用释放后缓存条目应保留");
            Assert.AreEqual(0, Loader.GetReleaseCount(PlainAddress), "KeepAlive 释放引用后不应卸载底层资源");

            // 再以 ReleaseOnClose 加载并释放：Pin 仍然生效，不卸载。
            IAssetHandle normal = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose).GetAwaiter().GetResult();
            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress), "KeepAlive 固定期间再次加载应命中缓存");
            normal.Release();
            Assert.AreEqual(1, GetCount(cache));
            Assert.AreEqual(0, Loader.GetReleaseCount(PlainAddress));

            InvokeInstanceMethod(cache, "ReleaseAll");
            Assert.AreEqual(0, GetCount(cache), "ReleaseAll 应清空缓存条目");
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress), "ReleaseAll 应释放被 KeepAlive 固定的资源");
        }

        /// <summary>
        /// 验证引用归零卸载后再次加载会重新走底层加载，旧条目不会残留。
        /// </summary>
        [UnityTest]
        public IEnumerator ReleaseToZero_ThenLoadAgain_TriggersNewUnderlyingLoad()
        {
            IgnoreEditModeObjectDestroyLogs();
            // 用 ManualMode 让两次加载都真正异步完成：
            // AssetCache 对"同步完成"的 IAssetLoader 会有一个已知问题（_dicLoading 登记晚于其 finally 移除），
            // 这里不覆盖该边界，避免把框架实现细节当成缓存语义来断言。
            Loader.ManualMode = true;
            object cache = GetAssetCache();

            Task<IAssetHandle> firstTask = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose);
            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => firstTask.IsCompleted, "第一次加载完成", deadline)) yield return null;

            IAssetHandle first = firstTask.GetAwaiter().GetResult();
            first.Release();
            Assert.AreEqual(0, GetCount(cache), "引用计数归零应移除缓存条目");

            Task<IAssetHandle> secondTask = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose);
            Assert.AreEqual(2, Loader.GetLoadCount(PlainAddress), "归零卸载后再次加载应重新走底层加载");

            Loader.CompleteNext();
            deadline = BeginWait();
            while (!AdvanceWait(() => secondTask.IsCompleted, "第二次加载完成", deadline)) yield return null;

            IAssetHandle second = secondTask.GetAwaiter().GetResult();
            Assert.NotNull(second);
            Assert.AreEqual(1, GetCount(cache));
            second.Release();
            Assert.AreEqual(2, Loader.GetReleaseCount(PlainAddress));
        }

        /// <summary>
        /// 验证同地址并发加载共享一次底层加载，两个等待者各拿到有效句柄并按引用计数释放。
        /// </summary>
        [UnityTest]
        public IEnumerator ConcurrentLoads_SingleUnderlyingLoad_BothGetHandle()
        {
            IgnoreEditModeObjectDestroyLogs();
            Loader.ManualMode = true;
            object cache = GetAssetCache();

            Task<IAssetHandle> firstTask = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose);
            Task<IAssetHandle> secondTask = LoadAsync(cache, PlainAddress, AssetCachePolicy.ReleaseOnClose);

            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress), "同地址并发加载只能有一次底层加载");
            Assert.AreEqual(1, GetLoadingCount(cache));
            Assert.IsFalse(firstTask.IsCompleted);
            Assert.IsFalse(secondTask.IsCompleted);

            Loader.CompleteNext();
            float deadline = BeginWait();
            while (!AdvanceWait(() => firstTask.IsCompleted && secondTask.IsCompleted, "两个并发加载请求完成", deadline))
                yield return null;

            IAssetHandle first = firstTask.GetAwaiter().GetResult();
            IAssetHandle second = secondTask.GetAwaiter().GetResult();
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.AreEqual(0, GetLoadingCount(cache));
            Assert.AreEqual(1, GetCount(cache));

            first.Release();
            Assert.AreEqual(1, GetCount(cache));
            Assert.AreEqual(0, Loader.GetReleaseCount(PlainAddress));
            second.Release();
            Assert.AreEqual(0, GetCount(cache));
            Assert.AreEqual(1, Loader.GetReleaseCount(PlainAddress));
        }

        /// <summary>
        /// 验证公开门面：KeepAlive 预加载过的面板打开时命中缓存，关闭后资源也不卸载。
        /// </summary>
        [Test]
        public void Preload_KeepAlive_PanelCloseDoesNotUnload()
        {
            IgnoreEditModeObjectDestroyLogs();
            UIManager.PreloadAsync<PlainTestPanel>().GetAwaiter().GetResult();
            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress));
            Assert.AreEqual(1, GetCount(GetAssetCache()));

            PlainTestPanel panel = UIManager.OpenPlainPanelAsync<PlainTestPanel>().GetAwaiter().GetResult();
            Assert.AreEqual(1, Loader.GetLoadCount(PlainAddress), "预加载过的面板打开时应命中资源缓存");

            UIManager.Close(panel, PanelCloseReason.ForceClose);

            Assert.AreEqual(0, Loader.GetReleaseCount(PlainAddress), "KeepAlive 预加载的资源在面板关闭后不应卸载");
            Assert.AreEqual(1, GetCount(GetAssetCache()));
            Assert.IsFalse(UIManager.HasOpenedPanel<PlainTestPanel>());
        }
    }
}
