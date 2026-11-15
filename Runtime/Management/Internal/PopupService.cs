using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 弹窗服务（框架内部）：管理"同一时间只显示一个、其余排队、带半透明遮罩"的弹窗。
    ///
    /// 规则：
    /// - 同一时间只有一个弹窗处于显示状态；
    /// - 已有弹窗（或正在加载弹窗）时，新请求带优先级进队列；
    /// - 当前弹窗关闭后，自动按优先级显示队列里的下一个；
    /// - 遮罩在第一个弹窗显示时创建，最后一个弹窗关闭后隐藏；
    /// - 点击遮罩是否关闭弹窗，由 [UIPanel(dismissOnMaskClick: ...)] 决定。
    ///
    /// 它只依赖 PanelService（实例化）和 UIRoot（遮罩），不知道窗口栈的存在。
    ///
    /// 外部不要直接使用本类；统一通过 UIManager 门面调用。
    /// </summary>
    internal sealed class PopupService
    {
        private readonly PanelService _panels;
        private readonly PopupQueue _queue = new PopupQueue();

        /// <summary>当前正在显示的弹窗（没有则为 null）。</summary>
        private UIPanel _active;

        /// <summary>弹窗是否正在加载中。防止同一帧连开两个都绕过队列。</summary>
        private bool _loading;

        /// <summary>弹窗遮罩。第一次打开弹窗时由 UIRoot 创建。</summary>
        private PopupMask _mask;

        /// <summary>
        /// 清理代号：CloseAllPopups / Reset 时 +1。
        /// 在途加载完成后发现代号变了，就放弃显示，避免"切场景后弹窗又冒出来"。
        /// </summary>
        private int _generation;

        #region 初始化

        /// <summary>
        /// 持有 PanelService 负责实例化与关闭；队列、活跃弹窗和遮罩状态由本类维护。
        /// </summary>
        internal PopupService(PanelService panels)
        {
            _panels = panels;
        }

        /// <summary>
        /// 重置弹窗状态：递增清理代号让在途加载作废，再清队列、活跃弹窗、加载标记与遮罩引用。
        /// </summary>
        internal void Reset()
        {
            // 让在途加载的弹窗失效。
            _generation++;

            _queue.Clear();
            _active = null;
            _loading = false;
            _mask = null;
        }

        #endregion

        #region 打开

        /// <summary>
        /// 打开一个弹窗。如果已有弹窗在显示或加载，本次请求入队并返回 null；
        /// 所以调用方通常只写 await，不需要依赖返回值。
        /// </summary>
        internal async Task<T> OpenPopupAsync<T>(
            UIPanelData data = null,
            CancellationToken cancellationToken = default) where T : UIPanel
        {
            cancellationToken.ThrowIfCancellationRequested();

            Type type = typeof(T);

            // 同类型弹窗已经在显示：默认刷新数据即可，不重复弹。
            // allowDuplicatePopup: true 的弹窗跳过这里，继续排队，从而可以连弹多次。
            // 正在播退场（Closing）的同类型弹窗不刷新：等它关完再由队列推进，避免"关闭动画中又显示"。
            if (_active != null && _active.GetType() == type && !AllowsDuplicate(type)
                && _active.State != UIPanelState.Closing)
            {
                _active.InternalShow(data);
                return (T)_active;
            }

            // 已有弹窗或正在加载：排队，不立即创建。
            if (_active != null || _loading)
            {
                bool allowDuplicate = AllowsDuplicate(type);
                if (allowDuplicate || !_queue.Contains(type))
                {
                    UIPanelAttribute queuedAttribute = _panels.GetAttribute(type);
                    _queue.Enqueue(
                        type,
                        data,
                        queuedAttribute == null ? 0 : queuedAttribute.PopupPriority,
                        cancellationToken,
                        (queuedData, ct) => ShowCoreAsync<T>(queuedData, ct));

                    CandyUILog.Info($"弹窗排队：{type.Name}（队列：{_queue.Describe()}）");
                }

                return null;
            }

            try
            {
                return (T)await ShowCoreAsync<T>(data, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 首弹直连路径没有外层 ShowNext 兜底：加载期间被取消/作废时，
                // 期间排进队列的弹窗还等着显示，必须在这里补一次推进。
                // 注意：ShowCoreAsync 自身绝不推进队列，推进入口只在这一层和 ShowNext，
                // 每次失效只会走其中之一，所以不会出现"推进两次、两个弹窗叠着显示"。
                ShowNext();
                throw;
            }
        }

        #endregion

        #region 关闭

        /// <summary>
        /// 关闭当前显示的弹窗；如果队列里还有，自动显示下一个。
        /// 返回 false 表示弹窗拒绝了关闭（CanClose），遮罩与队列都不变。
        /// </summary>
        internal bool CloseTopPopup()
        {
            return _active != null && ClosePopup(_active);
        }

        /// <summary>
        /// 发起关闭：返回 false 表示弹窗拒绝了关闭（CanClose），遮罩与队列都不变；
        /// 受理后保持 _active 跟踪，等关闭动画播完再清跟踪并推进队列（想等完成用 ClosePopupAsync）。
        /// </summary>
        internal bool ClosePopup(UIPanel popup, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (popup == null)
                return false;

            if (popup != _active)
            {
                // 不在显示中的弹窗：直接关掉，不影响队列。
                return _panels.ClosePanel(popup, reason);
            }

            if (!_panels.ClosePanel(popup, reason))
                return false;

            // 注意：这里不能立刻 _active = null / ShowNext：两个弹窗的动画会叠在一起。
            _ = FinishActiveCloseAsync(popup);
            return true;
        }

        /// <summary>
        /// 关闭指定弹窗并等待"关闭动画播完 + 队列推进/遮罩收起"完成，返回是否关成功。
        /// </summary>
        internal async Task<bool> ClosePopupAsync(UIPanel popup, PanelCloseReason reason = PanelCloseReason.UserClose)
        {
            if (popup == null)
                return false;

            if (popup != _active)
                return await _panels.ClosePanelAsync(popup, reason);

            if (!_panels.ClosePanel(popup, reason))
                return false;

            await FinishActiveCloseAsync(popup);
            return true;
        }

        /// <summary>关闭所有弹窗并清空队列（例如切场景时）。强制关闭，不询问拦截钩子、不等动画。</summary>
        internal void CloseAllPopups()
        {
            // 让在途加载的弹窗失效，防止 CloseAllPopups 之后它又显示出来。
            _generation++;

            _queue.Clear();

            if (_active != null)
            {
                UIPanel popup = _active;
                bool closed = _panels.ClosePanelImmediate(popup, PanelCloseReason.ForceClose);

                // 只有确认关闭成功才清跟踪，避免"还在屏幕上但没人认识"的孤儿弹窗。
                // 已经在播退场的弹窗（Closing）也没法再加速，等它自己关完即可，这里同样放弃跟踪。
                if (closed || popup == null || popup.State == UIPanelState.Closing)
                {
                    _active = null;
                }
                else
                {
                    CandyUILog.Warn($"弹窗关闭失败，已恢复跟踪：{popup.GetType().Name}");
                }
            }

            if (_active == null)
                HideMask(immediate: true);
        }

        #endregion

        #region 查询

        /// <summary>当前是否有弹窗在显示（NavigateBack 优先关弹窗用）。</summary>
        internal bool HasActive
        {
            get { return _active != null; }
        }

        /// <summary>某个实例是否是当前显示的弹窗。</summary>
        internal bool IsActive(UIPanel panel)
        {
            return panel != null && panel == _active;
        }

        #endregion

        #region 调试

        /// <summary>调试面板用：一行描述当前弹窗、队列与遮罩。</summary>
        internal string GetDebugSnapshot()
        {
            string active = _active == null ? "无" : _active.GetType().Name;
            string mask = _mask != null && _mask.IsVisible ? "显示" : "隐藏";
            return $"弹窗: {active} | 队列({_queue.Count}): {_queue.Describe()} | 遮罩: {mask}";
        }

        #endregion

        #region 内部实现

        /// <summary>
        /// 弹窗显示核心：加载、校验清理代号后置为活跃并显示遮罩；自身绝不推进队列，推进只在两条外层路径各做一次。
        /// </summary>
        private async Task<UIPanel> ShowCoreAsync<T>(
            UIPanelData data,
            CancellationToken cancellationToken) where T : UIPanel
        {
            UIPanelAttribute attribute = _panels.GetAttribute(typeof(T));
            int generation = _generation;
            _loading = true;

            try
            {
                UIPanel panel;
                try
                {
                    panel = await _panels.OpenPanelAsync<T>(data, UILayer.Popup, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 取消只放弃"这一次等待"，PanelService 的共享创建不会被中断：
                    // 面板稍后仍会被创建并 InternalShow，但此时 _active 没赋值、遮罩没显示、
                    // 也没人跟踪它 —— 这就是幽灵弹窗，之后打开别的弹窗会和它同屏。
                    // 收尾已收敛到 PanelService：最后一个等待者取消时由它登记后台销毁，
                    // 这里只负责把异常抛给调用方；绝不推进队列 —— 推进只由外层 ShowNext / OpenPopupAsync 各做一次。
                    throw;
                }

                if (generation != _generation)
                {
                    // 期间执行过 CloseAllPopups / Reset：这个弹窗已经过期，关掉它。
                    _panels.ClosePanel(panel, PanelCloseReason.ForceClose);
                    CandyUILog.Info($"弹窗打开已取消：{typeof(T).Name}（期间执行了 CloseAllPopups / Reset）");

                    // 这里只抛异常，绝不自己 ShowNext()：
                    // 本方法被两条路径共用 —— 队列路径由外层 ShowNext 的 catch 推进一次，
                    // 首弹直连路径由 OpenPopupAsync 的 catch 推进一次。
                    // 如果这里再推进，同一次失效会推进两次：两个弹窗同时显示、
                    // _active 被后一个覆盖，前一个变成没人跟踪的孤儿（遮罩也对不上）。
                    throw new PopupOpeningInvalidatedException(
                        $"[CandyUI] 弹窗打开已取消：{typeof(T).Name}");
                }

                _active = panel;

                PopupMask mask = EnsureMask();
                mask.SetClickHandler(OnMaskClicked);
                float alpha = attribute == null ? 0.6f : attribute.MaskAlpha;

                // 淡入不跟随本次打开的取消令牌：弹窗已经显示出来了，动画应完整播完，
                // 关闭/切场景时由 HideMask 取消。
                _ = mask.ShowAsync(alpha, CancellationToken.None);
                CandyUILog.Info($"弹窗显示：{typeof(T).Name}（遮罩 {alpha:0.00}，队列剩余 {_queue.Count}）");

                return panel;
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// 等当前弹窗关闭动画播完：确认它仍是跟踪对象后清 _active，再推进队列或收起遮罩。
        /// 期间若被 CloseAllPopups/新弹窗接管，就不要再碰状态（谁接管谁负责）。
        /// </summary>
        private async Task FinishActiveCloseAsync(UIPanel popup)
        {
            try
            {
                await _panels.WaitCloseAsync(popup);
            }
            catch (Exception exception)
            {
                CandyUILog.Warn($"等待弹窗关闭完成时出错：{popup.GetType().Name}（{exception.Message}）");
                Debug.LogException(exception);
            }

            if (!ReferenceEquals(_active, popup) || popup == null)
                return;

            _active = null;
            CandyUILog.Info($"弹窗关闭：{popup.GetType().Name}（队列剩余 {_queue.Count}）");

            if (_queue.Count > 0)
            {
                ShowNext();
            }
            else
            {
                HideMask();
            }
        }

        /// <summary>队列里的下一个弹窗加载完成后继续显示。</summary>
        private async void ShowNext()
        {
            if (_queue.Count == 0)
            {
                HideMask();
                return;
            }

            PopupRequest request = _queue.Dequeue();
            bool opened = false;

            // 出队前就被取消：跳过它，继续下一个。
            if (request.Token.IsCancellationRequested)
            {
                CandyUILog.Info($"弹窗已取消：{request.PanelType.Name}（队列剩余 {_queue.Count}）");
            }
            else
            {
                try
                {
                    await request.Open(request.Data, request.Token);
                    opened = true;
                }
                catch (OperationCanceledException)
                {
                    // 加载期间被取消 / 被 CloseAllPopups 作废：不算错误，继续下一个。
                    CandyUILog.Info($"弹窗已取消：{request.PanelType.Name}");
                }
                catch (Exception exception)
                {
                    // async void 不能把异常抛给调用方，只能记日志并继续下一个；
                    // 否则一个弹窗加载失败会让后面的全部卡住。
                    Debug.LogException(exception);
                }
            }

            // 整个方法里只有这一处"继续推进"：成功显示时不动队列，
            // 取消/作废/失败时都只推进一次，避免同一次失败触发两次 ShowNext。
            if (!opened)
                ShowNext();
        }

        /// <summary>
        /// 弹窗在加载期间被 CloseAllPopups / Reset 作废时抛出。
        /// 继承 OperationCanceledException：调用方原有的 catch (OperationCanceledException)
        /// 语义不变，同时让"失效后的队列推进"只由外层统一处理。
        /// </summary>
        private sealed class PopupOpeningInvalidatedException : OperationCanceledException
        {
            /// <summary>
            /// 用说明信息构造异常；沿用 OperationCanceledException 语义，让上层按取消处理已作废的打开。
            /// </summary>
            internal PopupOpeningInvalidatedException(string message) : base(message)
            {
            }
        }

        /// <summary>该类型弹窗是否允许重复排队显示（[UIPanel(..., allowDuplicatePopup: true)]）。</summary>
        private bool AllowsDuplicate(Type type)
        {
            UIPanelAttribute attribute = _panels.GetAttribute(type);
            return attribute != null && attribute.AllowDuplicatePopup;
        }

        /// <summary>
        /// 遮罩点击回调：只有当前弹窗声明 dismissOnMaskClick 时才关闭它。
        /// </summary>
        private void OnMaskClicked()
        {
            if (_active == null)
                return;

            UIPanelAttribute attribute = _panels.GetAttribute(_active.GetType());
            if (attribute != null && attribute.DismissOnMaskClick)
                CloseTopPopup();
        }

        /// <summary>
        /// 懒创建并复用弹窗遮罩（由 UIRoot 提供），首次弹窗显示时才真正创建。
        /// </summary>
        private PopupMask EnsureMask()
        {
            if (_mask != null)
                return _mask;

            UIRoot root = UIRoot.EnsureUIRoot();
            _mask = root.EnsurePopupMask();
            return _mask;
        }

        /// <summary>
        /// 隐藏遮罩；遮罩尚未创建时什么都不做。immediate=true 时立即隐藏（强制关闭路径不等淡出）。
        /// </summary>
        private void HideMask(bool immediate = false)
        {
            if (_mask == null)
                return;

            if (immediate)
                _mask.Hide();
            else
                _ = _mask.HideAsync();
        }

        #endregion

    }
}
