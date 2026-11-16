// 职责：PlayMode 测试的 YooAsset 编辑器模拟模式引导（只用于测试，不进运行时代码）。
// 依赖：YooAsset 3.0.7、BundleCollectorSetting 里的 DefaultPackage / UIDemo 分组。
// 限制：只支持编辑器（模拟构建需要 UnityEditor），真机联机模式不在这里覆盖。
using System;
using System.Threading.Tasks;
using CandyUI.YooAsset;
using UnityEngine;
using YooAsset;

namespace CandyUI.PlayModeTests
{
    /// <summary>
    /// 把 Demo 的 YooAsset 收集器做成"编辑器模拟包"，用来在测试里走真实的 YooAsset 加载链路。
    /// 初始化失败不抛异常，而是记录原因并让用例 Assert.Ignore，避免收集器没配好时整个套件红掉。
    /// </summary>
    internal static class YooAssetTestBootstrap
    {
        /// <summary>包名必须与 BundleCollectorSetting 里的一致。</summary>
        internal const string PackageName = "DefaultPackage";

        private static Task<bool> s_initTask;

        /// <summary>最近一次初始化失败的原因，供用例输出。</summary>
        internal static string Error { get; private set; }

        /// <summary>是否已成功把 YooAsset 加载器装进 UIManager。</summary>
        internal static bool IsReady { get; private set; }

        /// <summary>只初始化一次：同一批用例共享同一个 YooAsset 包与加载器。</summary>
        internal static Task<bool> EnsureInitializedAsync()
        {
            return s_initTask ?? (s_initTask = InitializeAsync());
        }

#if UNITY_EDITOR
        private static async Task<bool> InitializeAsync()
        {
            try
            {
                if (!YooAssets.IsInitialized)
                    YooAssets.Initialize();

                if (!YooAssets.TryGetPackage(PackageName, out ResourcePackage package))
                    package = YooAssets.CreatePackage(PackageName);

                if (package.InitializeStatus == EOperationStatus.None)
                {
                    // 与 DemoLauncher 完全相同的模拟构建流程，不落 StreamingAssets。
                    PackageBuildResult buildResult = EditorSimulateBuildInvoker.Build(
                        PackageName, (int)EBundleType.VirtualAssetBundle);

                    var options = new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters =
                            FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                                buildResult.PackageRootDirectory),
                    };

                    await package.InitializePackageAsync(options);

                    RequestPackageVersionOperation versionOp = package.RequestPackageVersionAsync();
                    await versionOp;
                    await package.LoadPackageManifestAsync(
                        new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
                }

                if (!package.PackageValid)
                {
                    Error = "YooAsset 包未就绪（PackageValid == false）。";
                    return false;
                }

                UIManager.InitializeAssetLoader(new YooAssetLoader(package));
                IsReady = true;
                Debug.Log($"[CandyUI.Test] YooAsset 模拟包已就绪：{PackageName}");
                return true;
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
                Debug.LogWarning("[CandyUI.Test] YooAsset 初始化失败：" + exception);
                return false;
            }
        }
#else
        private static Task<bool> InitializeAsync()
        {
            Error = "YooAsset 模拟模式只在编辑器可用。";
            return Task.FromResult(false);
        }
#endif
    }
}
