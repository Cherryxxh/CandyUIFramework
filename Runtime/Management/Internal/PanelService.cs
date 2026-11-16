using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace CandyUI
{
    /// <summary>
    /// 面板服务（框架内部）：已打开/缓存实例注册表、打开编排、关闭/显示隐藏/查询入口。
    /// 在途打开状态（去重、等待者、取消收尾）在 PanelOpener；"地址 -> 场景实例"的创建与销毁在 PanelFactory，
    /// 关闭编排（等退场动画再销毁/入缓存）在 PanelCloser，三者都不认识注册表，只通过本类传入的委托协作。
    /// 依赖方向：WindowService/PopupService -> PanelService -> PanelOpener / PanelFactory / PanelCloser -> AssetCache / UIRoot。
    /// </summary>
    internal sealed class PanelService
    {
        private readonly Dictionary<Type, UIPanelInfo> _dicOpenedPanels = new Dictionary<Type, UIPanelInfo>();
        private readonly Dictionary<Type, UIPanelInfo> _dicPanelCache = new Dictionary<Type, UIPanelInfo>();
        private readonly Dictionary<Type, UIPanelAttribute> _dicAttributes = new Dictionary<Type, UIPanelAttribute>();
        private readonly PanelOpener _opener;
        private readonly PanelFactory _factory;
        private readonly PanelCloser _closer;
        private IAssetLoader _assetLoader;
        private AssetCache _cache;
        /// <summary>清理代号：CloseAll / Reset 时 +1；在途加载完成时发现代号变了就放弃创建，避免旧界面复活。</summary>
        private int _generation;
        /// <summary>
        /// 缓存代号：清空实例缓存时 +1；KeepInstance 关闭流程写回缓存前发现代号变了就改走销毁路径，
        /// 避免"清缓存时正在播关闭动画"的实例在动画结束后把已清空的缓存写回来。与 _generation 各管一件事。
        /// </summary>
        private int _cacheGeneration;
        /// <summary>
        /// 这套服务所属的 UI 上下文；由 UIFramework 构造时绑定，null 表示还没绑定。
        /// PanelFactory 创建面板时把引用写进 UIPanel，关闭自路由才能回到正确的上下文。
        /// </summary>
        internal UIFramework Framework { get; private set; }
        /// <summary>
        /// 绑定（或解绑，传 null）所属的 UI 上下文；只由 UIFramework 构造时调用，不做其他状态变更。
        /// </summary>
        internal void BindFramework(UIFramework framework)
        {
            Framework = framework;
        }
        /// <summary>
        /// 装配三个协作者：在途打开状态交给 PanelOpener，实例创建交给 PanelFactory，
        /// 关闭编排交给 PanelCloser；本类只持有注册表与对外入口。
        /// </summary>
        internal PanelService()
        {
            _opener = new PanelOpener(CloseAbandonedPanel);
            _factory = new PanelFactory(this, GetAttribute, GetCache, () => _generation, () => DefaultTransition);
            _closer = new PanelCloser(
                _dicOpenedPanels, _dicPanelCache, _factory, () => _generation, () => _cacheGeneration);
        }
        #region 初始化
        /// <summary>全局默认转场；面板重写的 CreateTransition 优先，返回 null 时用它（null = 瞬切）。</summary>
        internal IPanelTransition DefaultTransition { get; set; }
        /// <summary>当前资源加载器；尚未注入时为 null。</summary>
        internal IAssetLoader AssetLoader => _assetLoader;
        /// <summary>是否已有资源加载器（首次打开会懒创建，之后恒为 true）。</summary>
        internal bool IsLoaderReady => _assetLoader != null;

        /// <summary>
        /// 注入（或切换）资源加载器；旧缓存的实例与句柄先全部销毁，避免面板继续持有失效资源。
        /// </summary>
        internal void Initialize(IAssetLoader assetLoader)
        {
            if (assetLoader == null)
                throw new ArgumentNullException(nameof(assetLoader));

            // 缓存实例持有旧加载器的句柄，必须先把它们销毁。
            ClearCache();
            _cache?.ReleaseAll();
            _cache = null;
            _assetLoader = assetLoader;
            _cache = new AssetCache(assetLoader);
        }

        /// <summary>
        /// 懒创建资源缓存；未显式注入加载器时退回 ResourcesAssetLoader，保证第一次打开就能用。
        /// </summary>
        internal AssetCache GetCache()
        {
            if (_cache == null)
                _cache = new AssetCache(_assetLoader ??= new ResourcesAssetLoader());
            return _cache;
        }

        /// <summary>
        /// 全量重置：递增清理代号让在途加载作废，再清实例缓存、打开表、特性缓存与加载器。
        /// </summary>
        internal void Reset()
        {
            _generation++; // 让在途加载在完成时放弃创建。
            ClearCache(); // 内部会递增缓存代号，让正在播关闭动画的 KeepInstance 实例也改走销毁路径。
            _dicOpenedPanels.Clear();
            _opener.Reset();
            _dicAttributes.Clear();
            _cache?.ReleaseAll();
            _cache = null;
            _assetLoader = null;
            DefaultTransition = null;
        }
        #endregion
        #region 打开
        /// <summary>
        /// 打开编排：依次尝试复用已打开、复用缓存实例、等待在途创建、新建；调用方取消只放弃自己的等待。
        /// </summary>
        internal async Task<UIPanel> OpenPanelAsync<T>(
            UIPanelData data = null,
            UILayer? layer = null,
            CancellationToken cancellationToken = default)
            where T : UIPanel
        {
            cancellationToken.ThrowIfCancellationRequested();
            Type type = typeof(T);
            // 1. 已经打开过：复用实例，重新 Show（不会重新加载 Prefab）。
            if (_dicOpenedPanels.TryGetValue(type, out UIPanelInfo opened)
                && opened.Panel != null
                && opened.Panel.State != UIPanelState.Closed)
            {
                // 不变量：任何会拿到面板的路径都必须先清废弃标记，否则上一次取消留下的
                // 后台收尾可能在本次 return 之后销毁刚交付的实例。
                _opener.ClearAbandoned(type);
                opened.Panel.InternalShow(data);
                CandyUILog.Info($"复用已打开面板：{type.Name}（不重新实例化）");
                return opened.Panel;
            }
            // 2. 关闭后缓存下来的实例：直接拿出来重新 Show，不重新加载/实例化。
            if (_dicPanelCache.TryGetValue(type, out UIPanelInfo cached))
            {
                _dicPanelCache.Remove(type);
                _dicOpenedPanels[type] = cached;
                _opener.ClearAbandoned(type);
                if (layer.HasValue && layer.Value != cached.Layer)
                {
                    CandyUILog.Warn($"缓存实例 {type.Name} 的层从 {cached.Layer} 变为 {layer.Value}，" +
                                   "暂不支持移动层，仍使用原层。");
                }

                try
                {
                    cached.Panel.InternalResume();
                    cached.Panel.InternalShow(data);
                }
                catch
                {
                    // 恢复成"重新打开之前"：实例回缓存表、不登记打开表。
                    // 已经 InternalResume 过，失败要补 InternalSleep，否则下次打开会重复 OnResume。
                    if (_dicOpenedPanels.TryGetValue(type, out UIPanelInfo reopening)
                        && ReferenceEquals(reopening, cached))
                    {
                        _dicOpenedPanels.Remove(type);
                        if (cached.Panel != null)
                            cached.Panel.InternalSleep();
                        _dicPanelCache[type] = cached;
                    }

                    throw;
                }

                CandyUILog.Info($"复用缓存实例：{type.Name}（OnCreate 不会重跑，实例缓存剩 {_dicPanelCache.Count}）");
                return cached.Panel;
            }

            // 3/4. 共享 Task 不绑定任何调用方 token：取消只放弃自己的等待，不能取消底层加载/实例化。
            PanelOpenOperation operation;
            bool beganNew = false;
            if (_opener.TryGetOperation(type, out operation))
            {
                _opener.ClearAbandoned(type); // 3. 正在加载：等同一个 Task。
            }
            else
            {
                // 4. 真正创建。开始新的创建 = 这个类型又有人要了：先清掉上一次取消可能残留的标记。
                _opener.ClearAbandoned(type);
                Task<UIPanel> task = CreatePanelAsync<T>(data, layer, CancellationToken.None);
                operation = _opener.BeginOperation(type, task);
                beganNew = true;
            }

            _opener.AddWaiter(operation); // 创建者自己也是一个等待者：判断"还有没有别人要"时必须算进去。
            // 先取 openingTask 再调度清理：Task 同步完成时清理会立刻清空 operation.Task，之后读会拿到 null。
            Task<UIPanel> openingTask = operation.Task;
            // 登记清理不能放在某个调用方的 finally 里：该调用方取消后会提前返回，可能把其他
            // 等待者还在用的登记删掉。由创建者等 Task 真正结束后按引用移除。
            if (beganNew)
                _ = _opener.UnregisterWhenDoneAsync(operation, openingTask);

            try
            {
                UIPanel panel = await TaskWait.WithCancellation(openingTask, cancellationToken);
                // 面板已经交给本次调用方：再清一次废弃标记（防"取消与交付"交错）。
                _opener.ClearAbandoned(type);
                _opener.RemoveWaiter(operation);
                return panel;
            }
            catch (OperationCanceledException)
            {
                // 共享 Task 仍会创建并 Show 面板；若本次是最后一个等待者，这个面板没人能拿到（幽灵）。
                _opener.AbandonIfLastWaiter(operation);
                throw;
            }
            catch
            {
                _opener.RemoveWaiter(operation);
                throw;
            }
        }

        /// <summary>
        /// 真正创建面板：工厂返回实例后先登记打开表再 OnShow（OnShow 里 CloseSelf 才能找到自己），失败时回滚。
        /// </summary>
        private async Task<UIPanel> CreatePanelAsync<T>(
            UIPanelData data,
            UILayer? layerOverride,
            CancellationToken cancellationToken)
            where T : UIPanel
        {
            Type type = typeof(T);
            UIPanelInfo info = await _factory.CreateAsync(type, layerOverride, cancellationToken);
            // 先登记再 Show：OnShow 里重入关闭（CloseSelf）要能在打开表里找到自己并走正常关闭。
            _dicOpenedPanels[type] = info;
            try
            {
                info.Panel.InternalShow(data);
            }
            catch
            {
                // OnShow 重入关闭可能已经把面板移出打开表、甚至销毁并释放了句柄；
                // 只在"仍然登记的是它"时才由这里收尾，否则会重复 Destroy / 重复 Release。
                if (_dicOpenedPanels.TryGetValue(type, out UIPanelInfo openedInfo)
                    && ReferenceEquals(openedInfo, info))
                {
                    _dicOpenedPanels.Remove(type);
                    Object.Destroy(info.GameObject);
                    info.Handle.Release();
                }
                else if (_dicPanelCache.TryGetValue(type, out UIPanelInfo cachedInfo)
                         && ReferenceEquals(cachedInfo, info))
                {
                    // OnShow 里重入关闭走了 KeepInstance 缓存：这次 Show 已经作废，缓存实例不能留。
                    _dicPanelCache.Remove(type);
                    _factory.Destroy(cachedInfo);
                }

                throw;
            }

            CandyUILog.Info($"面板创建完成：{type.Name}（层 {info.Layer}，实例已加入打开表）");
            return info.Panel;
        }

        /// <summary>PanelOpener 确认"废弃标记仍在 && 没有等待者"后回调：销毁没人能拿到的面板（强制关闭语义，幂等）。</summary>
        private void CloseAbandonedPanel(Type type)
        {
            if (!_dicOpenedPanels.TryGetValue(type, out UIPanelInfo info))
                return;
            _dicOpenedPanels.Remove(type);
            _factory.Destroy(info);
            CandyUILog.Info($"已取消的打开请求收尾：{type.Name}（创建后没有调用方等待，已销毁，避免幽灵实例）");
        }
        #endregion
        #region 关闭与显示
        /// <summary>
        /// 发起关闭：受理后立即移出打开表，退场动画播完再销毁或入缓存；被拦截/未打开返回 false。
        /// 只是"受理"——想等真正关完请用 ClosePanelAsync 或 WaitCloseAsync。
        /// </summary>
        internal bool ClosePanel(UIPanel panel, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (panel == null)
                return false;

            // 已经在关闭中：关闭已受理，直接返回 true，避免重复发起关闭把退场动画播两遍。
            if (panel.State == UIPanelState.Closing)
                return true;

            Type type = panel.GetType();
            if (_dicOpenedPanels.TryGetValue(type, out UIPanelInfo info) && info.Panel == panel)
                return _closer.TryClose(info, reason);

            // 已经关闭、但实例还留在缓存里：用户又点了关闭，就顺手销毁缓存。
            if (_dicPanelCache.TryGetValue(type, out UIPanelInfo cached) && cached.Panel == panel)
            {
                _dicPanelCache.Remove(type);
                _factory.Destroy(cached);
                CandyUILog.Info($"销毁缓存实例：{type.Name}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 发起关闭并等待关闭完成（退场动画结束、实例已销毁或已入缓存），返回是否关成功。
        /// </summary>
        internal async Task<bool> ClosePanelAsync(UIPanel panel, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (panel == null)
                return false;

            // 正在播退场：不能再"受理"一次，等本次关闭完成即可，调用方依然能 await 到关闭结束。
            if (panel.State == UIPanelState.Closing)
            {
                await WaitCloseAsync(panel);
                return true;
            }

            Type type = panel.GetType();
            if (_dicOpenedPanels.TryGetValue(type, out UIPanelInfo info) && info.Panel == panel)
                return await _closer.TryCloseAsync(info, reason);

            if (_dicPanelCache.TryGetValue(type, out UIPanelInfo cached) && cached.Panel == panel)
                return ClosePanel(panel, reason);

            return false;
        }

        /// <summary>等待面板本次关闭流程结束；未在关闭中时立即完成。</summary>
        internal Task WaitCloseAsync(UIPanel panel)
        {
            return panel != null ? panel.WaitForCloseAsync() : Task.CompletedTask;
        }

        /// <summary>
        /// 强制关闭指定面板：不播退场动画，立即销毁实例并释放句柄（清空弹窗/切场景等即时路径用）。
        /// </summary>
        internal bool ClosePanelImmediate(UIPanel panel, PanelCloseReason reason = PanelCloseReason.ForceClose)
        {
            if (panel == null)
                return false;

            Type type = panel.GetType();
            if (_dicOpenedPanels.TryGetValue(type, out UIPanelInfo info) && info.Panel == panel)
            {
                _closer.CloseImmediate(info, reason);
                return true;
            }

            if (_dicPanelCache.TryGetValue(type, out UIPanelInfo cached) && cached.Panel == panel)
            {
                _dicPanelCache.Remove(type);
                _factory.Destroy(cached);
                CandyUILog.Info($"销毁缓存实例：{type.Name}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 按类型关闭面板；该类型没有打开时直接返回 false。
        /// </summary>
        internal bool ClosePanelOfType<T>(PanelCloseReason reason = PanelCloseReason.UserClose) where T : UIPanel
            => _dicOpenedPanels.TryGetValue(typeof(T), out UIPanelInfo info) && info.Panel != null
               && ClosePanel(info.Panel, reason);

        /// <summary>
        /// 强制关闭所有面板并清空实例缓存，同时递增清理代号让在途加载作废（切场景用）。
        /// </summary>
        internal void CloseAllPanels()
        {
            _generation++; // 让在途加载在完成后放弃创建（防止切场景后旧界面复活）。

            // 复制一份再遍历：关闭会修改 _dicOpenedPanels；关闭全部是强制关闭，不问拦截钩子、也不播退场动画。
            List<UIPanelInfo> snapshot = new List<UIPanelInfo>(_dicOpenedPanels.Values);
            for (int i = 0; i < snapshot.Count; i++)
                _closer.CloseImmediate(snapshot[i], PanelCloseReason.ForceClose);

            _dicOpenedPanels.Clear();
            ClearCache(); // 别把"关闭后保留实例"的缓存留到下一个场景；同时递增缓存代号让在途关闭改走销毁。
        }

        /// <summary>
        /// 把已打开且处于 Visible 的面板隐藏：只 SetActive(false)，实例与句柄都保留，可再用 TryShow 恢复。
        /// </summary>
        internal bool TryHide(UIPanel panel)
        {
            if (panel == null)
                return false;

            if (!_dicOpenedPanels.TryGetValue(panel.GetType(), out UIPanelInfo info)
                || info.Panel != panel)
            {
                CandyUILog.Warn($"隐藏失败：{panel.GetType().Name} 当前没有打开。");
                return false;
            }

            if (panel.State != UIPanelState.Visible)
            {
                CandyUILog.Warn($"隐藏失败：{panel.GetType().Name} 状态为 {panel.State}，只有 Visible 才能隐藏。");
                return false;
            }

            panel.InternalHide();
            CandyUILog.Info($"面板隐藏：{panel.GetType().Name}（实例保留，可调用 TryShowPanel 再次显示）");
            return true;
        }

        /// <summary>
        /// 重新显示已打开但被隐藏的面板，可携带新数据；Closed 的面板必须先重新打开。
        /// </summary>
        internal bool TryShow(UIPanel panel, UIPanelData data)
        {
            if (panel == null)
                return false;

            if (!_dicOpenedPanels.TryGetValue(panel.GetType(), out UIPanelInfo info)
                || info.Panel != panel)
            {
                CandyUILog.Warn($"显示失败：{panel.GetType().Name} 当前没有打开。");
                return false;
            }

            if (panel.State == UIPanelState.Closed)
            {
                CandyUILog.Warn($"显示失败：{panel.GetType().Name} 已经关闭，请用 OpenPlainPanelAsync 重新打开。");
                return false;
            }

            if (panel.State == UIPanelState.Visible)
            {
                CandyUILog.Info($"面板已经是显示状态：{panel.GetType().Name}");
                return true;
            }

            panel.InternalShow(data);
            CandyUILog.Info($"面板显示：{panel.GetType().Name}");
            return true;
        }
        #endregion
        #region 查询
        /// <summary>
        /// 指定类型面板是否已打开且可见；注册表里没有或状态不是 Visible 都返回 false。
        /// </summary>
        internal bool IsVisible<T>() where T : UIPanel
            => _dicOpenedPanels.TryGetValue(typeof(T), out UIPanelInfo info) && info.Panel != null
               && info.Panel.State == UIPanelState.Visible;

        /// <summary>
        /// 指定类型面板是否处于打开状态：隐藏也算打开，Closed 不算。
        /// </summary>
        internal bool IsOpen<T>() where T : UIPanel
            => _dicOpenedPanels.TryGetValue(typeof(T), out UIPanelInfo info) && info.Panel != null
               && info.Panel.State != UIPanelState.Closed;

        /// <summary>查询某类型是否已经打开；窗口服务用它判断"复用还是新建"。</summary>
        internal bool TryGetOpenedOfType(Type type, out UIPanelInfo info)
            => _dicOpenedPanels.TryGetValue(type, out info);

        /// <summary>
        /// 取指定类型的已打开面板实例，没打开或类型不匹配时返回 null。
        /// </summary>
        internal T GetOpenedOfPanel<T>() where T : UIPanel
            => _dicOpenedPanels.TryGetValue(typeof(T), out UIPanelInfo info) ? info.Panel as T : null;

        /// <summary>
        /// 取面板的 [UIPanel] 特性并缓存；类型未标注时返回 null，打开路径会据此抛错。
        /// </summary>
        internal UIPanelAttribute GetAttribute(Type type)
        {
            if (_dicAttributes.TryGetValue(type, out UIPanelAttribute attribute))
                return attribute;

            attribute = Attribute.GetCustomAttribute(type, typeof(UIPanelAttribute), false) as UIPanelAttribute;
            _dicAttributes[type] = attribute;
            return attribute;
        }
        #endregion
        #region 资源缓存与调试
        /// <summary>
        /// 转发工厂预加载 Prefab（KeepAlive 策略），只读资源不打开界面。
        /// </summary>
        internal Task PreloadAsync<T>(CancellationToken cancellationToken = default) where T : UIPanel
            => _factory.PreloadAsync<T>(cancellationToken);

        /// <summary>
        /// 销毁全部“关闭后保留实例”的缓存面板并释放句柄；缓存为空时直接返回。
        /// </summary>
        internal void ClearCache()
        {
            // 先递增缓存代号（即使当前缓存为空）：关闭动画中的 KeepInstance 面板还没入缓存，
            // 必须在它完成前让代号变化，否则动画结束后它会把实例写回已清空的缓存。
            _cacheGeneration++;
            if (_dicPanelCache.Count == 0)
                return;
            List<UIPanelInfo> snapshot = new List<UIPanelInfo>(_dicPanelCache.Values);
            _dicPanelCache.Clear();
            for (int i = 0; i < snapshot.Count; i++)
                _factory.Destroy(snapshot[i]);
            CandyUILog.Info("面板实例缓存已清空");
        }

        /// <summary>
        /// 只销毁指定类型的缓存实例；该类型没有缓存时什么都不做。
        /// </summary>
        internal void ClearCache<T>() where T : UIPanel
        {
            // 同 ClearCache：先失效在途关闭流程，再处理已缓存的实例。
            _cacheGeneration++;
            Type type = typeof(T);
            if (!_dicPanelCache.TryGetValue(type, out UIPanelInfo info))
                return;
            _dicPanelCache.Remove(type);
            _factory.Destroy(info);
            CandyUILog.Info($"销毁缓存实例：{type.Name}");
        }

        /// <summary>
        /// 拼一行面板状态摘要：打开数、在途加载数、实例缓存数、资源缓存数与独立 Canvas 面板数。
        /// </summary>
        internal string GetDebugSnapshot()
        {
            int ownCanvasCount = 0;
            foreach (UIPanelInfo info in _dicOpenedPanels.Values)
            {
                if (info.HasOwnCanvas)
                    ownCanvasCount++;
            }

            return $"面板: 打开 {_dicOpenedPanels.Count} | 加载中 {_opener.LoadingCount} | " +
                   $"缓存实例 {_dicPanelCache.Count} | 资源缓存 {_cache?.Count ?? 0}（加载中 {_cache?.LoadingCount ?? 0}） | " +
                   $"独立Canvas {ownCanvasCount}";
        }
        #endregion
    }
}
