// 职责：YooAsset 3.x 资源包到 CandyUI IAssetLoader 的适配器，按 [UIPanel] 地址加载 UI Prefab。
// 依赖：已初始化并加载好清单的 ResourcePackage，地址必须与 [UIPanel] 字符串一致。
// 限制：YooAsset 的 await 不支持中途取消，加载结束后再检查 token 并把句柄还回去。
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace CandyUI.YooAsset
{
    /// <summary>
    /// CandyUI 的资源加载器在 YooAsset 3.x 上的实现。
    ///
    /// 用法（初始化顺序）：
    /// 1. <c>YooAssets.Initialize();</c>
    /// 2. <c>var package = YooAssets.CreatePackage("DefaultPackage");</c>
    /// 3. 按运行模式初始化 package，例如编辑器模拟模式：
    ///    <code>
    ///    var buildResult = EditorSimulateBuildInvoker.Build("DefaultPackage", (int)EBundleType.VirtualAssetBundle);
    ///    var options = new EditorSimulateModeOptions
    ///    {
    ///        EditorFileSystemParameters =
    ///            FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory)
    ///    };
    ///    await package.InitializePackageAsync(options);
    ///    </code>
    ///    真机（联机模式）则用 <see cref="HostPlayModeOptions"/> +
    ///    <c>FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()</c> +
    ///    <c>FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService)</c>。
    /// 4. 请求版本并加载清单：
    ///    <code>
    ///    var versionOp = await package.RequestPackageVersionAsync();
    ///    var manifestOp = await package.LoadPackageManifestAsync(
    ///        new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
    ///    </code>
    /// 5. 交给 CandyUI：
    ///    <code>UIManager.InitializeAssetLoader(new YooAssetLoader(package));</code>
    ///
    /// 地址规则：<c>[UIPanel("UI/BagPanel")]</c> 里的字符串就是 YooAsset 的 location，
    /// 所以这些 Prefab 不能再放在 Resources 目录下，需要打进 YooAsset 的资源包。
    /// </summary>
    public sealed class YooAssetLoader : IAssetLoader
    {
        private readonly ResourcePackage mPackage;

        /// <summary>
        /// 校验并保存 YooAsset 资源包，后续按 [UIPanel] 地址从该包加载资源。
        /// </summary>
        public YooAssetLoader(ResourcePackage package)
        {
            mPackage = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>被适配的 YooAsset 资源包。</summary>
        public ResourcePackage Package => mPackage;

        /// <summary>
        /// 从 YooAsset 包异步加载 UI Prefab；完成后再次检查取消，取消或失败时归还已加载句柄。
        /// </summary>
        /// <inheritdoc />
        public async Task<IAssetHandle> LoadAssetAsync(string loadAddress, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(loadAddress))
                throw new ArgumentException("资源地址不能为空。", nameof(loadAddress));
            if (mPackage == null || mPackage.PackageValid == false)
                throw new InvalidOperationException("YooAsset 资源包尚未初始化，不能加载资源。");

            cancellationToken.ThrowIfCancellationRequested();

            AssetHandle handle = null;
            try
            {
                handle = mPackage.LoadAssetAsync<GameObject>(loadAddress);
                await handle;

                // YooAsset 的 await 不能中途取消：加载结束后再检查一次，
                // 取消时把刚加载出来的句柄还回去，避免泄漏。
                cancellationToken.ThrowIfCancellationRequested();

                if (handle.Status != EOperationStatus.Succeeded)
                    throw new InvalidOperationException($"YooAsset 加载失败：{loadAddress}（{handle.Error}）");
                if (handle.AssetObject == null)
                    throw new InvalidOperationException($"YooAsset 加载结果为空：{loadAddress}");

                return new YooAssetHandle(handle);
            }
            catch
            {
                if (handle != null && handle.IsValid)
                    handle.Release();
                throw;
            }
        }
    }
}
