using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 默认加载器：用 Resources.LoadAsync 加载 Prefab。
    ///
    /// 为什么默认用 Resources？
    /// 因为它零配置、任何 Unity 工程都能跑，适合先把框架跑通、做 Demo。
    /// 但正式项目不建议大量使用 Resources：它会把资源全部打进包体、无法热更、
    /// 没有引用计数。所以这里把它做成"可替换实现"而不是写死在 UIManager 里。
    ///
    /// 以后的 YooAssetLoader 只要实现 IAssetLoader：
    ///   - LoadAssetAsync 里调用 YooAssets.LoadAssetAsync<GameObject>(address)；
    ///   - 返回的句柄在 Release 时调用 handle.Release()；
    ///   - 业务入口把 UIManager.InitializeAssetLoader(new ResourcesAssetLoader()) 换成 YooAssetLoader 即可。
    /// 面板代码、UIManager 一行都不用改。
    ///
    /// 注意：缓存和引用计数不在这里做。加载器只负责"把地址变成一个句柄"，
    /// 缓存由框架层的 AssetCache 统一处理，这样换任何加载器都自动有缓存。
    /// </summary>
    public sealed class ResourcesAssetLoader : IAssetLoader
    {
        /// <summary>
        /// 用 Resources.LoadAsync 异步加载 Prefab；地址为空或找不到时抛异常，取消只中止等待、不中止底层请求。
        /// </summary>
        public Task<IAssetHandle> LoadAssetAsync(string loadAddress, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(loadAddress))
                throw new ArgumentException("资源地址不能为空", nameof(loadAddress));

            // 已经取消：直接返回一个"已取消"的 Task，不再发请求。
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<IAssetHandle>(cancellationToken);

            // TaskCompletionSource：把 Unity 的回调式异步 API 包装成 Task。
            // 这是 Unity 里接入 async/await 最常用的桥接方式。
            TaskCompletionSource<IAssetHandle> tcs = new TaskCompletionSource<IAssetHandle>();

            ResourceRequest request = Resources.LoadAsync<GameObject>(loadAddress);
            CancellationTokenRegistration registration = default;

            if (cancellationToken.CanBeCanceled)
            {
                // 取消只影响"等待"：Task 变成 Canceled，底层 Resources 请求仍会跑完。
                registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            }

            request.completed += _ =>
            {
                registration.Dispose();

                // 已经取消/完成：不要再用结果覆盖 Task 状态。
                if (tcs.Task.IsCompleted)
                    return;

                GameObject prefab = request.asset as GameObject;

                if (prefab == null)
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"[CandyUI] Resources 里找不到地址为 \"{loadAddress}\" 的 Prefab。" +
                        "请确认它放在任意 Resources 文件夹下，且地址不带扩展名。"));
                    return;
                }

                tcs.TrySetResult(new ResourcesAssetHandle(prefab));
            };

            return tcs.Task;
        }

        /// <summary>
        /// Resources 加载出来的资源句柄。
        ///
        /// 这是本加载器的实现细节，外面不应该看到，所以做成 private 嵌套类，
        /// 和宿主类放在同一个文件里——这是 C# 中嵌套类型的常见用法。
        /// Resources 资源由引擎统一管理，没有引用计数可以减，所以 Release 是空实现；
        /// 换成 YooAsset 后，这里就是真正调用 handle.Release() 的地方。
        /// </summary>
        private sealed class ResourcesAssetHandle : IAssetHandle
        {
            /// <summary>
            /// 保存加载完成的资源对象；外部拿不到这个私有类型，句柄只由 LoadAssetAsync 创建。
            /// </summary>
            public ResourcesAssetHandle(UnityEngine.Object asset)
            {
                Asset = asset;
            }

            public UnityEngine.Object Asset { get; }

            public bool IsValid
            {
                get { return Asset != null; }
            }

            /// <summary>
            /// 空实现：Resources 资源由引擎统一卸载；换成 YooAsset 时在这里调用底层句柄的 Release()。
            /// </summary>
            public void Release()
            {
                // Resources 资源不能主动卸载，由引擎在切场景/UnloadUnusedAssets 时处理。
                // YooAsset 的句柄会在这里调用 handle.Release()。
            }
        }
    }
}
