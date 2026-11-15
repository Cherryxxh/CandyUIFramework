using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using YooAsset;
using CandyUI.YooAsset;

namespace CandyUI.Demo
{
    /// <summary>
    /// Demo 启动器：模拟"游戏启动时打开第一个界面"。
    ///
    /// 建议的观察顺序：
    /// 1. Play 后底部工具栏显示：窗口栈(1): HomeWindow；
    /// 2. 点"打开设置"：栈变成 HomeWindow -> SettingsWindow，主城隐藏；
    /// 3. 点"重置数据"：弹窗显示，遮罩出现，主城/设置都点不动；
    /// 4. 点"连弹两个弹窗"：确认框先显示，奖励弹窗进入队列（看工具栏的队列数字）；
    /// 5. 关闭确认框：奖励弹窗自动弹出；
    /// 6. 点"返回/Esc"逐层出栈；
    /// 7. 关掉任何界面后，都可以从底部工具栏重新打开。
    ///
    /// F1 显示/隐藏底部工具栏。
    /// </summary>
    public sealed class DemoLauncher : MonoBehaviour
    {
        /// <summary>
        /// Demo 入口：初始化资源和默认转场、建工具栏、预加载面板，最后打开主城。
        /// </summary>
        private async void Start()
        {
            // 资源系统是"可替换"的：先尝试 YooAsset；失败则回退 Resources。
            // 下面所有界面代码不用关心资源从哪来。
            await InitializeAssetLoaderAsync();

            // 全局默认转场：用 DOTween 版（示例）。
            // 想回到纯 Unity 版就换成：new FadeScaleTransition()
            // 不设置就是瞬切；单个面板还能重写 CreateTransition 覆盖这里。
            UIManager.DefaultTransition = new DOTweenTransition();

            // 调试工具栏：独立 Canvas，不参与框架管理，任何界面关了都能从它重新打开。
            DemoToolbar.Ensure();

            // 预加载：先把 Prefab 读进缓存（KeepAlive），
            // 这样第一次打开界面不会卡在资源读取上。
            // 这里分开 try/catch：某个界面还没生成 Prefab 时（比如刚改了 DemoSetup），
            // 只警告、不阻塞其它界面，避免整个 Demo 打不开。
            await PreloadAsync<HomeWindow>();
            await PreloadAsync<SettingsWindow>();
            await PreloadAsync<MailWindow>();
            await PreloadAsync<BagPanel>();
            await PreloadAsync<ShopPanel>();
            await PreloadAsync<ConfirmPopup>();
            await PreloadAsync<RewardPopup>();
            await PreloadAsync<ToastPanel>();
            await PreloadAsync<TweenDemoWindow>();

            try
            {
                await UIManager.OpenWindowAsync<HomeWindow>(new UIPanelData());
            }
            catch (System.Exception exception)
            {
                // async void 里的异常不会被自动捕获，必须自己 try/catch，
                // 否则只会看到"什么都没发生"。
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 初始化资源加载器：
        /// 编辑器里优先走 YooAsset 的模拟模式（无需真正打 AB 包，Play 前自动 SimulateBuild）；
        /// 任何一步失败（没配收集器、包名不对、YooAsset 异常）都回退 Resources，
        /// 保证 Demo 永远能跑起来、不至于因为资源系统配置把人挡在门外。
        /// </summary>
        private static async Task InitializeAssetLoaderAsync()
        {
#if UNITY_EDITOR
            try
            {
                const string packageName = "DefaultPackage";
                if (!YooAssets.IsInitialized)
                    YooAssets.Initialize();

                // 包已存在就复用（同一个 Editor 会话里重复 Play 的情况）。
                if (!YooAssets.TryGetPackage(packageName, out ResourcePackage package))
                    package = YooAssets.CreatePackage(packageName);

                if (package.InitializeStatus == EOperationStatus.None)
                {
                    // 编辑器模拟构建：把收集器里配置的资源做成虚拟包（不落 StreamingAssets）。
                    PackageBuildResult buildResult = EditorSimulateBuildInvoker.Build(
                        packageName, (int)EBundleType.VirtualAssetBundle);

                    var options = new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters =
                            FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                                buildResult.PackageRootDirectory),
                    };

                    await package.InitializePackageAsync(options);

                    // 3.0.7 的操作对象是"非泛型 awaitable"：先拿 op，再 await（await 的结果是 void），
                    // 版本号要在 await 之后从 op.PackageVersion 读，不能写 var version = await op。
                    RequestPackageVersionOperation versionOp = package.RequestPackageVersionAsync();
                    await versionOp;
                    await package.LoadPackageManifestAsync(
                        new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
                }

                if (package.PackageValid)
                {
                    UIManager.InitializeAssetLoader(new YooAssetLoader(package));
                    Debug.Log("[CandyUI] 资源加载：YooAsset（编辑器模拟模式，包 " + packageName + "）");
                    return;
                }

                Debug.LogWarning("[CandyUI] YooAsset 包未就绪，回退到 Resources。");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[CandyUI] YooAsset 初始化失败，回退到 Resources：" + exception);
            }
#endif
            UIManager.InitializeAssetLoader(new ResourcesAssetLoader());
        }

        /// <summary>
        /// 预加载指定面板的 Prefab；失败只警告不中断，避免某个界面缺失拖垮整个 Demo。
        /// </summary>
        private static async Task PreloadAsync<T>() where T : UIPanel
        {
            try
            {
                await UIManager.PreloadAsync<T>();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[CandyUI] 预加载 {typeof(T).Name} 失败（不影响其它界面）：{exception.Message}");
            }
        }

        /// <summary>
        /// 每帧检测 Esc：只在能返回时 NavigateBack，根窗口不响应，避免退回黑屏。
        /// </summary>
        private void Update()
        {
            // 新版 Input System：不再用 UnityEngine.Input。
            // Keyboard.current 可能为 null（没有键盘/设备未就绪），先判空。
            // Esc = 返回：有弹窗先关弹窗，否则关当前窗口并恢复上一个。
            // CanNavigateBack 为 false 时（只剩根窗口）不响应，避免退回黑屏。
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
                && UIManager.CanNavigateBack)
            {
                UIManager.NavigateBack();
            }
        }
    }
}
