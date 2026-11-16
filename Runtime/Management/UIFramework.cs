using System;
using System.Threading;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// UI 框架实例门面：真正持有服务引用的实现，UIManager 只是面向默认上下文的静态薄壳。
    /// 默认上下文（Default）包住 UIManager 的三个静态服务，仍用 UIRoot.EnsureUIRoot() 解析根节点；
    /// UIFramework.Create 创建额外上下文，各自一套服务 / AssetCache / UIRoot，互不共享。
    /// 方法签名与语义和 UIManager 一一对应；Dispose 释放上下文，默认上下文的 Root 不销毁。
    /// </summary>
    public sealed class UIFramework : IDisposable
    {
        private readonly PanelService _panels;

        private readonly WindowService _windows;

        private readonly PopupService _popups;

        /// <summary>显式传入或首次访问时解析出的根节点；null 表示还没解析。</summary>
        private UIRoot _root;

        /// <summary>是否已 Dispose；重复 Dispose 幂等，之后打开/关闭类 API 抛 ObjectDisposedException。</summary>
        private bool _disposed;

        /// <summary>默认上下文：懒创建，包住 UIManager 的三个静态服务。</summary>
        public static UIFramework Default
        {
            get { return UIManager.DefaultFramework; }
        }

        /// <summary>本实例是否默认上下文：UIFramework.Default 为 true，Create 创建的为 false。</summary>
        public bool IsDefault { get; private set; }

        /// <summary>
        /// 创建一套独立的 UI 上下文：新建三个服务与 UIRoot（非法配置按 UIRoot.Create 的规则回退并打 Warning），
        /// 每次调用得到互不共享的 AssetCache 与根节点；loader 为 null 时首次打开界面再懒创建加载器。
        /// </summary>
        public static UIFramework Create(UIRootConfig config = default, IAssetLoader loader = null)
        {
            PanelService panels = new PanelService();
            WindowService windows = new WindowService(panels);
            PopupService popups = new PopupService(panels);
            UIRoot root = UIRoot.Create(config);

            UIFramework framework = new UIFramework(panels, windows, popups, root);
            if (loader != null)
                framework.InitializeAssetLoader(loader);

            return framework;
        }

        /// <summary>本上下文的 UI 根节点；Create 时显式创建，默认上下文首次访问才 EnsureUIRoot 并缓存。</summary>
        public UIRoot Root
        {
            get
            {
                ThrowIfDisposed();
                if (_root == null)
                    _root = UIRoot.EnsureUIRoot();

                return _root;
            }
        }

        /// <summary>当前使用的资源加载器。</summary>
        public IAssetLoader AssetLoader
        {
            get { return _panels.AssetLoader; }
        }

        /// <summary>是否已经有资源加载器（首次打开面板后会懒创建，之后恒为 true）。</summary>
        public bool IsLoaderReady
        {
            get { return _panels.IsLoaderReady; }
        }

        /// <summary>全局默认转场（null = 瞬切）；面板重写 CreateTransition 可单独覆盖。</summary>
        public IPanelTransition DefaultTransition
        {
            get { return _panels.DefaultTransition; }
            set { _panels.DefaultTransition = value; }
        }

        /// <summary>本上下文的面板服务（内部）。</summary>
        internal PanelService Panels
        {
            get { return _panels; }
        }

        /// <summary>本上下文的窗口服务（内部）。</summary>
        internal WindowService Windows
        {
            get { return _windows; }
        }

        /// <summary>本上下文的弹窗服务（内部）。</summary>
        internal PopupService Popups
        {
            get { return _popups; }
        }

        /// <summary>
        /// 组装一个实例：绑定面板服务（关闭自路由用），记录可选 Root 与默认上下文标记；不创建 UIRoot。
        /// </summary>
        internal UIFramework(
            PanelService panels,
            WindowService windows,
            PopupService popups,
            UIRoot root = null,
            bool isDefault = false)
        {
            if (panels == null)
                throw new ArgumentNullException(nameof(panels));
            if (windows == null)
                throw new ArgumentNullException(nameof(windows));
            if (popups == null)
                throw new ArgumentNullException(nameof(popups));

            _panels = panels;
            _windows = windows;
            _popups = popups;
            _root = root;
            IsDefault = isDefault;

            // 面板创建后要能找回所属上下文；显式传 null 表示不绑定（保留旧的静态门面兜底）。
            _panels.BindFramework(this);
        }

        /// <summary>注入资源加载器；不调用时第一次打开面板会懒创建 ResourcesAssetLoader。</summary>
        public void InitializeAssetLoader(IAssetLoader loader)
        {
            ThrowIfDisposed();
            _panels.Initialize(loader);
        }

        /// <summary>
        /// 释放本上下文：关闭所有界面并重置面板服务（释放缓存与加载器）。
        /// 非默认上下文额外销毁自己创建的 Root；默认上下文的 Root 可能被其他代码持有，不销毁。
        /// 重复调用幂等；Dispose 后打开/关闭类 API 抛 ObjectDisposedException，只读查询返回清理后的空状态。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            CloseAll();
            _panels.Reset();
            ReleaseRoot();
            _disposed = true;
        }

        /// <summary>非默认上下文销毁自己创建的 Root；默认上下文的 Root 只打 Info 不销毁。</summary>
        private void ReleaseRoot()
        {
            UIRoot root = _root;
            _root = null;

            if (IsDefault)
            {
                CandyUILog.Info("默认上下文的 Root 不会被销毁（可能被其他代码或后续静态 API 继续使用）。");
                return;
            }

            if (root != null)
                UnityEngine.Object.Destroy(root.gameObject);
        }

        /// <summary>Dispose 后拒绝打开/关闭类调用，避免在本已释放的上下文上又懒创建 Root 或加载器。</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UIFramework));
        }

        #region 打开

        /// <summary>打开（或复用）平铺层面的面板；窗口/弹窗请用对应方法。</summary>
        public async Task<T> OpenPlainPanelAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default)
            where T : UIPanel
        {
            ThrowIfDisposed();

            UIPanelAttribute attribute = _panels.GetAttribute(typeof(T));
            if (attribute != null
                && (attribute.Layer == UILayer.Window || attribute.Layer == UILayer.Popup))
            {
                throw new InvalidOperationException(
                    $"[CandyUI] {typeof(T).Name} 声明在 {attribute.Layer} 层，" +
                    "打开窗口请用 OpenWindowAsync，打开弹窗请用 OpenPopupAsync。");
            }

            return (T)await _panels.OpenPanelAsync<T>(data, null, cancellationToken);
        }

        /// <summary>打开一个窗口（Window 层）：当前窗口隐藏入栈，不销毁。</summary>
        public async Task<T> OpenWindowAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default) where T : UIPanel
        {
            ThrowIfDisposed();

            return (T)await _windows.OpenWindowAsync<T>(data, cancellationToken);
        }

        /// <summary>打开一个弹窗（Popup 层）；已有弹窗时排队并返回 null。</summary>
        public async Task<T> OpenPopupAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default) where T : UIPanel
        {
            ThrowIfDisposed();

            return (T)await _popups.OpenPopupAsync<T>(data, cancellationToken);
        }

        #endregion

        #region 关闭

        /// <summary>发起关闭；按身份路由：弹窗 → 窗口 → 普通面板，不等待退场动画。</summary>
        public void Close(UIPanel panel, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            ThrowIfDisposed();

            if (panel == null)
                return;

            if (_popups.IsActive(panel))
            {
                _popups.ClosePopup(panel, reason);
                return;
            }

            if (_windows.Contains(panel))
            {
                _windows.CloseWindow(panel, reason);
                return;
            }

            _panels.ClosePanel(panel, reason);
        }

        /// <summary>关闭并等待完成；路由规则与 Close 相同，被拦截或未打开返回 false。</summary>
        public async Task<bool> CloseAsync(UIPanel panel, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            ThrowIfDisposed();

            if (panel == null)
                return false;

            if (_popups.IsActive(panel))
                return await _popups.ClosePopupAsync(panel, reason);

            if (_windows.Contains(panel))
                return await _windows.CloseWindowAsync(panel, reason);

            return await _panels.ClosePanelAsync(panel, reason);
        }

        /// <summary>等待面板本次关闭流程结束；未在关闭中时立即完成。</summary>
        public Task WaitForCloseAsync(UIPanel panel)
        {
            ThrowIfDisposed();

            return _panels.WaitCloseAsync(panel);
        }

        /// <summary>关闭指定类型的面板；没打开时什么都不做。</summary>
        public void CloseOfType<T>(PanelCloseReason reason = PanelCloseReason.UserClose)
            where T : UIPanel
        {
            ThrowIfDisposed();

            if (_panels.TryGetOpenedOfType(typeof(T), out UIPanelInfo info)
                && info.Panel != null)
            {
                Close(info.Panel, reason);
            }
        }

        /// <summary>关闭当前窗口并恢复上一个窗口（如果有）。</summary>
        public void CloseCurrentWindow(PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            ThrowIfDisposed();

            _windows.CloseCurrentWindow(reason);
        }

        /// <summary>关闭当前显示的弹窗；队列里还有就自动显示下一个。</summary>
        public void CloseTopPopup()
        {
            ThrowIfDisposed();

            _popups.CloseTopPopup();
        }

        /// <summary>关闭指定弹窗。</summary>
        public void ClosePopup(UIPanel popup)
        {
            ThrowIfDisposed();

            _popups.ClosePopup(popup);
        }

        /// <summary>关闭所有弹窗并清空队列（切场景时）。</summary>
        public void CloseAllPopups()
        {
            ThrowIfDisposed();

            _popups.CloseAllPopups();
        }

        /// <summary>返回：优先关弹窗；没有弹窗时关闭当前窗口并恢复上一个。</summary>
        public void NavigateBack()
        {
            ThrowIfDisposed();

            if (_popups.HasActive)
            {
                _popups.CloseTopPopup();
                return;
            }

            _windows.CloseCurrentWindow();
        }

        /// <summary>关闭所有面板（切场景、退出战斗时常用）；立即关闭，不等退场动画。</summary>
        public void CloseAll()
        {
            ThrowIfDisposed();

            // ForceClose 不会被界面拦截，所以先关弹窗和面板、最后清窗口栈，
            // 避免出现"栈已经清空、窗口实例却还活着"的孤儿。
            _popups.CloseAllPopups();
            _panels.CloseAllPanels();
            _windows.Clear();
        }

        #endregion

        #region 显示与隐藏

        /// <summary>隐藏已打开的面板（只 SetActive(false)）；弹窗与非栈顶窗口会被拒绝。</summary>
        public bool TryHidePanel(UIPanel panel)
        {
            ThrowIfDisposed();

            if (panel == null)
                return false;

            if (_popups.IsActive(panel))
            {
                CandyUILog.Warn(
                    $"弹窗不能隐藏：{panel.GetType().Name}。请使用 CloseTopPopup/ClosePopup，否则遮罩和队列会不一致。");
                return false;
            }

            if (_windows.Contains(panel) && !_windows.IsTop(panel))
            {
                CandyUILog.Warn(
                    $"窗口 {panel.GetType().Name} 不是栈顶，隐藏它会让下面的窗口露出来。请先返回。");
                return false;
            }

            return _panels.TryHide(panel);
        }

        /// <summary>隐藏指定类型的面板；没打开或隐藏失败时返回 false。</summary>
        public bool TryHidePanel<T>() where T : UIPanel
        {
            ThrowIfDisposed();

            return TryHidePanel(_panels.GetOpenedOfPanel<T>());
        }

        /// <summary>显示已打开但被隐藏的面板，可携带新数据；不是栈顶窗口时返回 false。</summary>
        public bool TryShowPanel(UIPanel panel, UIPanelData data = null)
        {
            ThrowIfDisposed();

            if (panel == null)
                return false;

            // 非栈顶的窗口被隐藏着，直接显示会出现两个窗口同时可见。
            if (_windows.Contains(panel) && !_windows.IsTop(panel))
            {
                CandyUILog.Warn(
                    $"窗口 {panel.GetType().Name} 不是栈顶，请先 NavigateBack 返回，或者用 OpenWindowAsync 切换到它。");
                return false;
            }

            return _panels.TryShow(panel, data);
        }

        /// <summary>显示指定类型的面板；没打开时返回 false。</summary>
        public bool TryShowPanel<T>(UIPanelData data = null) where T : UIPanel
        {
            ThrowIfDisposed();

            return TryShowPanel(_panels.GetOpenedOfPanel<T>(), data);
        }

        #endregion

        #region 查询

        /// <summary>是否有可返回内容：有弹窗，或窗口栈里不止一个窗口。</summary>
        public bool CanNavigateBack
        {
            get { return _popups.HasActive || _windows.CanGoBack; }
        }

        /// <summary>获取已打开的面板实例，没有则返回 null。</summary>
        public T GetOpenedPanel<T>() where T : UIPanel
        {
            return _panels.GetOpenedOfPanel<T>();
        }

        /// <summary>面板是否处于打开状态（不包含已关闭）。</summary>
        public bool HasOpenedPanel<T>() where T : UIPanel
        {
            return _panels.IsOpen<T>();
        }

        /// <summary>面板是否可见（打开且没有被隐藏）。</summary>
        public bool IsPanelVisible<T>() where T : UIPanel
        {
            return _panels.IsVisible<T>();
        }

        #endregion

        #region 资源缓存

        /// <summary>预加载面板 Prefab（KeepAlive）但不打开界面。</summary>
        public async Task PreloadAsync<T>(CancellationToken cancellationToken = default)
            where T : UIPanel
        {
            ThrowIfDisposed();

            await _panels.PreloadAsync<T>(cancellationToken);
        }

        /// <summary>清空所有"关闭后保留实例"的面板缓存，真正销毁它们并释放句柄。</summary>
        public void ClearPanelCache()
        {
            ThrowIfDisposed();

            _panels.ClearCache();
        }

        /// <summary>清空指定类型的保留实例。</summary>
        public void ClearPanelCache<T>() where T : UIPanel
        {
            ThrowIfDisposed();

            _panels.ClearCache<T>();
        }

        #endregion

        #region 调试

        /// <summary>获取当前状态的多行摘要：面板 / 窗口栈 / 弹窗队列。</summary>
        public string GetDebugSnapshot()
        {
            return _panels.GetDebugSnapshot()
                   + "\n" + _windows.GetDebugSnapshot()
                   + "\n" + _popups.GetDebugSnapshot();
        }

        #endregion
    }
}
