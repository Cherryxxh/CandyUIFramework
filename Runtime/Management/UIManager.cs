using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 界面管理器 —— 框架唯一的对外入口（门面 / Facade）。
    ///
    /// 为什么要有门面？
    /// 如果所有逻辑都堆在这一个类里，文件会越来越长，而且面板、窗口、弹窗三套状态
    /// 互相纠缠，改窗口栈很容易碰坏弹窗队列。所以内部拆成三个服务：
    ///   PanelService  —— 面板的加载/实例化/生命周期/注册表（底层）
    ///   WindowService —— 窗口显示一个、隐藏一个的历史栈（依赖 Panel）
    ///   PopupService  —— 弹窗队列 + 遮罩（依赖 Panel）
    ///
    /// 依赖方向是单向的：Window/Popup -> Panel -> AssetCache。
    /// 门面只做三件事：转发调用、按"身份"路由 Close、统一 Reset。
    /// 公开 API 仍然只在这一个类里，业务代码不需要认识任何 Service。
    ///
    /// 典型调用（"点背包按钮"）：
    ///   await UIManager.OpenWindowAsync<BagPanel>(new BagPanelData { DefaultTab = 0 });
    ///
    /// 打开流程：
    ///   查已打开表 -> 已在则直接复用并 OnShow（防连点、防重复实例化）
    ///   -> 正在加载则 await 同一个 Task（两次点击只加载一次）
    ///   -> AssetCache 加载 Prefab（命中缓存直接返回）
    ///   -> 实例化到对应层 -> OnCreate(仅一次) -> OnShow(data)
    /// </summary>
    public static class UIManager
    {
        private static readonly PanelService s_panels = new PanelService();

        private static readonly WindowService s_windows = new WindowService(s_panels);

        private static readonly PopupService s_popups = new PopupService(s_panels);

        #region 初始化

        /// <summary>当前使用的资源加载器。</summary>
        public static IAssetLoader AssetLoader
        {
            get { return s_panels.AssetLoader; }
        }

        /// <summary>
        /// 是否已经有资源加载器。
        /// 首次打开界面时会懒创建 ResourcesAssetLoader，所以这个值在用过一次之后
        /// 也会是 true —— 它不代表你显式调用过 InitializeAssetLoader。
        /// </summary>
        public static bool IsLoaderReady
        {
            get { return s_panels.IsLoaderReady; }
        }

        /// <summary>
        /// 全局默认的界面转场（null = 瞬切）。在启动脚本里设置一次即可：
        ///   UIManager.DefaultTransition = new FadeScaleTransition();
        /// 面板重写 CreateTransition 可以单独覆盖；面板返回 null 时用这里的默认值。
        /// </summary>
        public static IPanelTransition DefaultTransition
        {
            get { return s_panels.DefaultTransition; }
            set { s_panels.DefaultTransition = value; }
        }

        /// <summary>
        /// 注入资源加载器。整个框架切换资源系统的唯一入口。
        /// 不调用也可以：第一次打开面板时会自动使用 ResourcesAssetLoader。
        ///
        /// 注意：如果此时还有面板处于打开状态，切换会先释放旧缓存的全部引用，
        /// 这些面板持有的句柄会失效。所以切换应发生在打开任何界面之前，
        /// 或者在 CloseAll() 之后。
        /// </summary>
        public static void InitializeAssetLoader(IAssetLoader assetLoader)
        {
            s_panels.Initialize(assetLoader);
        }

        /// <summary>
        /// 关闭 Domain Reload 时（Enter Play Mode Options），静态字段不会自动清空，
        /// 会导致第二次进入 Play 时还拿着上一次的面板引用。这里主动重置三个服务。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_panels.Reset();
            s_windows.Reset();
            s_popups.Reset();
        }

        #endregion

        #region 打开

        /// <summary>
        /// 打开（或复用）一个普通面板（Main / Toast / Top / Background 层）。
        ///
        /// 窗口请用 OpenWindowAsync，弹窗请用 OpenPopupAsync —— 它们还要维护
        /// 窗口栈、弹窗队列和遮罩，直接从底层打开会绕过这些状态。
        /// 如果面板特性声明的层是 Window/Popup，这里会直接抛异常。
        /// </summary>
        /// <param name="data">本次显示携带的数据，可为 null。</param>
        /// <param name="cancellationToken">
        /// 取消"等待加载"。已经打开/已经加载完的面板不受影响；
        /// 取消后抛 OperationCanceledException，底层加载不会被中断。如果取消的是唯一等待者，
        /// 共享创建完成后实例会被立即销毁（不留没人跟踪的"幽灵"面板）；还有别的等待者时不受影响。
        /// </param>
        /// <exception cref="InvalidOperationException">面板声明的层是 Window 或 Popup。</exception>
        /// <exception cref="OperationCanceledException">
        /// 等待被取消；或者面板在 OnShow 里关闭了自己（本次打开按作废处理，不会返回已关闭的实例）。
        /// </exception>
        public static async Task<T> OpenPlainPanelAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default)
            where T : UIPanel
        {
            UIPanelAttribute attribute = s_panels.GetAttribute(typeof(T));
            if (attribute != null
                && (attribute.Layer == UILayer.Window || attribute.Layer == UILayer.Popup))
            {
                throw new InvalidOperationException(
                    $"[CandyUI] {typeof(T).Name} 声明在 {attribute.Layer} 层，" +
                    "打开窗口请用 OpenWindowAsync，打开弹窗请用 OpenPopupAsync。");
            }

            return (T)await s_panels.OpenPanelAsync<T>(data, null, cancellationToken);
        }

        /// <summary>
        /// 打开一个"窗口"（UILayer.Window）。当前窗口会被隐藏并入栈，不会销毁。
        /// 玩家点返回时调用 NavigateBack()，就能回到上一个窗口，数据/滚动位置都还在。
        ///
        /// 例子：主城(HomeWindow) -> 打开设置(SettingsWindow) -> NavigateBack() 回到主城。
        ///
        /// 窗口在 OnShow 里关闭了自己时，本次打开按"作废"处理（抛 OperationCanceledException），
        /// 不会入栈，也不会留下没人跟踪的窗口实例。
        /// 取消"等待加载"（唯一等待者）时同样抛 OperationCanceledException：窗口栈不变，
        /// 当前窗口的显示状态也不受影响，共享创建完成后实例会被 PanelService 收尾销毁。
        /// </summary>
        public static async Task<T> OpenWindowAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default) where T : UIPanel
        {
            return (T)await s_windows.OpenWindowAsync<T>(data, cancellationToken);
        }

        /// <summary>
        /// 打开一个弹窗（UILayer.Popup）。
        ///
        /// 如果已经有弹窗在显示或正在加载，本次请求会进入队列，
        /// 等前面的弹窗关闭后按优先级自动显示 —— 此时返回值是 null。
        /// 所以调用方通常只写 await，不需要依赖返回值。
        ///
        /// 弹窗在 OnShow 里关闭了自己时，本次打开按"作废"处理（抛 OperationCanceledException），
        /// 队列会继续推进到下一个弹窗，不会留下没人跟踪的实例。
        /// </summary>
        public static async Task<T> OpenPopupAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default) where T : UIPanel
        {
            return (T)await s_popups.OpenPopupAsync<T>(data, cancellationToken);
        }

        #endregion

        #region 关闭

        /// <summary>
        /// 发起关闭一个面板实例（后台播退场动画，播完再销毁或入缓存）；想等真正关完请用 CloseAsync。
        /// 门面按它的身份路由：
        /// - 弹窗：关闭后自动显示队列里的下一个；
        /// - 窗口：关闭后恢复上一个窗口；
        /// - 普通面板：直接销毁。
        /// 业务代码点"关闭"统一调这个（UIPanel.CloseSelf 也走这里）。
        ///
        /// reason 为 UserClose 时，界面可以在 CanClose 里拒绝关闭；
        /// ForceClose 用于切场景/关闭全部，直接放行。
        /// </summary>
        public static void Close(UIPanel panel, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (panel == null)
                return;

            if (s_popups.IsActive(panel))
            {
                s_popups.ClosePopup(panel, reason);
                return;
            }

            if (s_windows.Contains(panel))
            {
                s_windows.CloseWindow(panel, reason);
                return;
            }

            s_panels.ClosePanel(panel, reason);
        }

        /// <summary>
        /// 关闭一个面板并等待关闭完成（退场动画播完、实例已销毁或已入缓存），返回是否关成功。
        /// 路由规则与 Close 相同；被 CanClose 拦截或不在打开状态时返回 false。
        /// </summary>
        public static async Task<bool> CloseAsync(UIPanel panel, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (panel == null)
                return false;

            if (s_popups.IsActive(panel))
                return await s_popups.ClosePopupAsync(panel, reason);

            if (s_windows.Contains(panel))
                return await s_windows.CloseWindowAsync(panel, reason);

            return await s_panels.ClosePanelAsync(panel, reason);
        }

        /// <summary>
        /// 等待面板本次关闭流程结束（退场动画播完、已销毁或已入缓存）；未在关闭中时立即完成。
        /// </summary>
        public static Task WaitForCloseAsync(UIPanel panel)
        {
            return s_panels.WaitCloseAsync(panel);
        }

        /// <summary>关闭指定类型的面板（没打开时什么都不做）。</summary>
        public static void CloseOfType<T>(PanelCloseReason reason = PanelCloseReason.UserClose)
            where T : UIPanel
        {
            if (s_panels.TryGetOpenedOfType(typeof(T), out UIPanelInfo info)
                && info.Panel != null)
            {
                Close(info.Panel, reason);
            }
        }

        /// <summary>
        /// 关闭当前窗口，并恢复上一个窗口（如果有）。
        /// 界面自己的"关闭"按钮可以调它；左上角"返回"更推荐调 NavigateBack()。
        /// 窗口可以在 CanClose 里拒绝关闭（比如有未保存的修改）。
        /// </summary>
        public static void CloseCurrentWindow(PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            s_windows.CloseCurrentWindow(reason);
        }

        /// <summary>关闭当前显示的弹窗；如果队列里还有，自动显示下一个。</summary>
        public static void CloseTopPopup()
        {
            s_popups.CloseTopPopup();
        }

        /// <summary>关闭指定弹窗（"确定/取消"按钮里调 CloseSelf 也会走到这里）。</summary>
        public static void ClosePopup(UIPanel popup)
        {
            s_popups.ClosePopup(popup);
        }

        /// <summary>关闭所有弹窗并清空队列（例如切场景时）。</summary>
        public static void CloseAllPopups()
        {
            s_popups.CloseAllPopups();
        }

        /// <summary>
        /// "返回"：优先关弹窗；没有弹窗时关闭当前窗口并恢复上一个。
        /// 适合绑定 Android 返回键、Esc、界面上的返回按钮。
        /// </summary>
        public static void NavigateBack()
        {
            if (s_popups.HasActive)
            {
                s_popups.CloseTopPopup();
                return;
            }

            s_windows.CloseCurrentWindow();
        }

        /// <summary>关闭所有面板（切场景、退出战斗时常用）；立即关闭，不等退场动画。</summary>
        public static void CloseAll()
        {
            // ForceClose 不会被界面拦截，所以先关弹窗和面板、最后清窗口栈，
            // 避免出现"栈已经清空、窗口实例却还活着"的孤儿。
            s_popups.CloseAllPopups();
            s_panels.CloseAllPanels();
            s_windows.Clear();
        }

        #endregion

        #region 显示与隐藏

        // ---- 显示 / 隐藏（不销毁实例） -------------------------------------
        //
        // 关闭（Close）会销毁实例；显示/隐藏只是 SetActive，
        // 常用于"切页签""打开背包时临时挡住 HUD"这类高频操作。

        /// <summary>
        /// 隐藏一个已经打开的面板：只 SetActive(false)，实例和句柄都保留。
        /// 之后可以调用 TryShowPanel 再显示回来。
        /// 返回 false 表示没有隐藏成功（没打开 / 状态不是 Visible / 是弹窗 / 不是栈顶窗口）。
        ///
        /// 弹窗不能这样隐藏：隐藏后遮罩和队列状态会对不上，
        /// 请改用 CloseTopPopup / ClosePopup。
        /// </summary>
        public static bool TryHidePanel(UIPanel panel)
        {
            if (panel == null)
                return false;

            if (s_popups.IsActive(panel))
            {
                CandyUILog.Warn(
                    $"弹窗不能隐藏：{panel.GetType().Name}。请使用 CloseTopPopup/ClosePopup，否则遮罩和队列会不一致。");
                return false;
            }

            if (s_windows.Contains(panel) && !s_windows.IsTop(panel))
            {
                CandyUILog.Warn(
                    $"窗口 {panel.GetType().Name} 不是栈顶，隐藏它会让下面的窗口露出来。请先返回。");
                return false;
            }

            return s_panels.TryHide(panel);
        }

        /// <summary>隐藏指定类型的面板；没打开或隐藏失败时返回 false。</summary>
        public static bool TryHidePanel<T>() where T : UIPanel
        {
            return TryHidePanel(s_panels.GetOpenedOfPanel<T>());
        }

        /// <summary>
        /// 显示一个已经打开但被隐藏的面板，可以顺带传新的数据。
        /// 已经可见时返回 true；没打开 / 已关闭 / 不是栈顶窗口时返回 false。
        /// </summary>
        public static bool TryShowPanel(UIPanel panel, UIPanelData data = null)
        {
            if (panel == null)
                return false;

            // 非栈顶的窗口被隐藏着，直接显示会出现两个窗口同时可见。
            if (s_windows.Contains(panel) && !s_windows.IsTop(panel))
            {
                CandyUILog.Warn(
                    $"窗口 {panel.GetType().Name} 不是栈顶，请先 NavigateBack 返回，或者用 OpenWindowAsync 切换到它。");
                return false;
            }

            return s_panels.TryShow(panel, data);
        }

        /// <summary>显示指定类型的面板；没打开时返回 false。</summary>
        public static bool TryShowPanel<T>(UIPanelData data = null) where T : UIPanel
        {
            return TryShowPanel(s_panels.GetOpenedOfPanel<T>(), data);
        }

        #endregion

        #region 查询

        /// <summary>
        /// 当前是否有"可以返回"的内容：有弹窗，或窗口栈里不止一个窗口。
        /// 根窗口（比如主城）不响应返回，避免把游戏退到黑屏。
        /// 绑 Android 返回键时可以写：if (UIManager.CanNavigateBack) UIManager.NavigateBack();
        /// </summary>
        public static bool CanNavigateBack
        {
            get { return s_popups.HasActive || s_windows.CanGoBack; }
        }

        /// <summary>获取已打开的面板实例，没有则返回 null。</summary>
        public static T GetOpenedPanel<T>() where T : UIPanel
        {
            return s_panels.GetOpenedOfPanel<T>();
        }

        /// <summary>面板是否处于打开状态（不包含已关闭）。</summary>
        public static bool HasOpenedPanel<T>() where T : UIPanel
        {
            return s_panels.IsOpen<T>();
        }

        /// <summary>面板是否可见（打开且没有被隐藏）。</summary>
        public static bool IsPanelVisible<T>() where T : UIPanel
        {
            return s_panels.IsVisible<T>();
        }

        #endregion

        #region 资源缓存

        /// <summary>
        /// 预加载面板 Prefab，但不打开界面。
        ///
        /// 适合"登录界面还开着，先悄悄把主城资源读进来"这种场景。
        /// 预加载使用的是 KeepAlive 策略：加载完就常驻缓存，之后第一次 OpenPlainPanelAsync
        /// 不用再等资源读取。
        /// </summary>
        public static async Task PreloadAsync<T>(CancellationToken cancellationToken = default)
            where T : UIPanel
        {
            await s_panels.PreloadAsync<T>(cancellationToken);
        }

        /// <summary>
        /// 清空所有"关闭后保留实例"的面板缓存，真正销毁它们并释放句柄。
        /// 切场景、内存吃紧时调用。
        /// </summary>
        public static void ClearPanelCache()
        {
            s_panels.ClearCache();
        }

        /// <summary>清空指定类型的保留实例。</summary>
        public static void ClearPanelCache<T>() where T : UIPanel
        {
            s_panels.ClearCache<T>();
        }

        #endregion

        #region 调试

        /// <summary>
        /// 获取框架当前状态的多行摘要：窗口栈 / 当前弹窗与队列 / 遮罩 / 面板与缓存数量。
        ///
        /// 调试用：Demo 的底部工具栏每 0.2 秒刷新一次，能直接看到
        /// "打开设置后栈深变成 2""弹窗排队 1 个""遮罩显示/隐藏"这些变化。
        /// 正式项目可以把它接到自己的调试面板里。
        /// </summary>
        public static string GetDebugSnapshot()
        {
            return s_panels.GetDebugSnapshot()
                   + "\n" + s_windows.GetDebugSnapshot()
                   + "\n" + s_popups.GetDebugSnapshot();
        }

        /// <summary>
        /// 是否输出框架日志（统一前缀 "[CandyUI]"）。
        /// 默认开启，方便学习和排查；正式包可以设为 false 减少日志量。
        /// </summary>
        public static bool DebugLogEnabled
        {
            get { return CandyUILog.Enabled; }
            set { CandyUILog.Enabled = value; }
        }

        #endregion

    }
}
