using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 所有界面的基类，负责"生命周期"这一件事。
    ///
    /// 设计原则：UIPanel 只关心自己什么时候被创建、显示、隐藏、关闭，
    /// 不关心"资源从哪来、谁把我打开的、我上面还有谁"。
    /// 那些是 UIManager 的职责。职责分开，以后换加载器、加栈、加弹窗队列，
    /// 都不用改 Panel 子类。
    ///
    /// 子类按需重写这些钩子：
    ///   OnCreate  —— 只执行一次。在这里找控件、建列表、注册事件。
    ///   OnShow    —— 每次显示都执行。在这里根据 data 刷新界面。
    ///   OnHide    —— 每次隐藏都执行（面板没销毁，可能再次显示）。
    ///   OnClose   —— 面板被关闭、即将销毁前执行。
    ///   OnClosed  —— 本次关闭流程结束（含 KeepInstance 入缓存）时执行，可在这里做关闭后的收尾。
    ///   OnSleep / OnResume —— 只有 KeepInstance 面板会用到：进缓存/从缓存恢复时执行。
    ///                         这时的关闭不会触发 OnClose，所以退订全局事件要放在 OnSleep，
    ///                         重订放在 OnResume（没有 Presenter 的界面同样有用）。
    ///
    /// 常见错误：把"刷新界面"写在 OnCreate 里。这样第二次打开时数据不会更新。
    /// 正确做法：OnCreate 只做一次性的初始化，数据刷新放 OnShow。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class UIPanel : MonoBehaviour
    {
        #region 状态与属性

        /// <summary>当前所在层（由框架在创建时写入）。</summary>
        public UILayer Layer { get; internal set; }

        /// <summary>
        /// 创建这个面板的 UI 上下文（由 PanelFactory 在创建时写入）。
        /// null 表示没有绑定上下文（例如测试直接 new 出来的实例），关闭时退回静态默认门面。
        /// </summary>
        internal UIFramework Framework { get; set; }

        /// <summary>当前生命周期状态。</summary>
        public UIPanelState State { get; internal set; } = UIPanelState.None;

        /// <summary>本次显示携带的数据（由 UIManager 在 OnShow 前写入）。</summary>
        public UIPanelData Data { get; private set; }

        /// <summary>创建这个面板时使用的资源地址（调试用）。</summary>
        public string Address { get; internal set; }

        /// <summary>
        /// 这个面板的 Presenter（可能为 null：简单界面可以不用 Presenter）。
        /// 由框架在 OnCreate 之后通过 CreatePresenter 创建并绑定。
        /// </summary>
        public PresenterBase Presenter { get; private set; }

        /// <summary>
        /// 这个面板的进出场动画（可以为 null：瞬切）。
        /// 默认由 UIManager.DefaultTransition 指定；面板也可以重写 CreateTransition 单独指定。
        /// </summary>
        public IPanelTransition Transition { get; private set; }

        private bool _isCreated;
        private CancellationTokenSource _transitionCts;

        // 退场动画的 Task：隐藏流程会返回它，窗口切换等上层可以 await 到"旧窗口已经退场"。
        private Task _hideTask;

        // 关闭流程 Task：重复 Close 时复用，保证退场动画只播一次。
        private Task _closeTask;

        // 关闭退场专用 CTS：不能被"入场/隐藏"的转场取消逻辑波及。
        private CancellationTokenSource _closeCts;

        #endregion

        #region 转场配置

        /// <summary>
        /// 由框架调用：优先用面板自己重写的转场，没有就用全局默认转场。
        ///
        /// 注意：转场实例是"配置对象"，会被多个面板共享（默认值时），
        /// 所以实现里不要存"当前正在播的 Tween/CTS"这类运行时状态。
        /// </summary>
        internal void SetupTransition(IPanelTransition defaultTransition)
        {
            Transition = CreateTransition() ?? defaultTransition;
        }

        /// <summary>
        /// 面板自己的转场。默认返回 null（用 UIManager.DefaultTransition）。
        /// 例如：
        ///   protected override IPanelTransition CreateTransition()
        ///   {
        ///       return new FadeScaleTransition { InStartScale = 0.85f };
        ///   }
        /// </summary>
        protected virtual IPanelTransition CreateTransition()
        {
            return null;
        }

        #endregion

        #region 生命周期（框架内部调用）

        /// <summary>
        /// 由 UIManager 调用：实例化完成后执行一次 OnCreate。
        /// internal 是为了让业务代码无法乱调，保证生命周期只由框架驱动。
        /// </summary>
        internal void InternalCreate()
        {
            if (_isCreated)
                return;

            _isCreated = true;
            State = UIPanelState.Created;

            try
            {
                OnCreate();

                // OnCreate 里已经把控件建好了，这时创建 Presenter 并绑定，
                // 它就能在 OnBind 里安全地订阅 View 的事件。
                Presenter = CreatePresenter();
                if (Presenter != null)
                    Presenter.Bind(this);
            }
            catch
            {
                // 创建失败：退回"未创建"状态，由 PanelService 负责销毁实例并释放句柄。
                // 否则会留下"已经登记、但初始化到一半"的面板。
                Presenter = null;
                _isCreated = false;
                State = UIPanelState.None;
                throw;
            }
        }

        /// <summary>
        /// 由 UIManager 调用：显示面板并执行 OnShow。
        /// 若 OnShow / Presenter.OnShow 期间面板把自己关闭/隐藏了（例如调用 CloseSelf），
        /// 本次显示视为作废：不会置为 Visible、不播入场动画，并抛 OperationCanceledException，
        /// 由上层按"取消"语义撤销登记/推进队列。
        /// </summary>
        internal void InternalShow(UIPanelData data)
        {
            // 记下进入前的状态：OnShow/Presenter.OnShow 抛异常时按它回滚。
            // 不能一律回 Created —— 对已经 Visible 的面板重复 Open 时抛异常，
            // 会把本来正常显示的界面误隐藏/误判成未显示。
            UIPanelState stateBeforeShow = State;

            // 已经可见时（例如对已打开面板再 Open 一次）不重播入场动画，避免闪一下。
            bool wasVisible = stateBeforeShow == UIPanelState.Visible;

            Data = data;
            State = UIPanelState.Showing;

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            try
            {
                OnShow(data);

                if (Presenter != null)
                    Presenter.OnShow(data);

                // 关键约束：钩子（OnShow / Presenter.OnShow）里可能重入关闭自己
                // （例如 OnShow 里调用 CloseSelf）：此时 State 已经被 InternalClose /
                // InternalHide 改成 Closed / Hiding，面板甚至已经从打开表移除/销毁。
                // 这里绝不能再把它改回 Visible —— 否则会得到"Visible 但没人登记"的幽灵面板，
                // 弹窗场景里 _active / 遮罩 / 队列都会跟着卡死。
                // 用 OperationCanceledException 如实告诉调用方"本次显示已作废"：
                // 这正是 PanelService / 窗口 / 弹窗已有的"取消即作废"语义（各自回滚或推进队列）。
                if (State != UIPanelState.Showing)
                {
                    throw new OperationCanceledException(
                        $"[CandyUI] 面板 {GetType().Name} 在 OnShow 中关闭/隐藏了自己，本次显示已作废。");
                }

                State = UIPanelState.Visible;

                // 只有"从不可见 -> 可见"才播入场；
                // 已经是 Visible 的重复 Open 只刷新数据。
                if (!wasVisible)
                    PlayTransitionIn();
            }
            catch
            {
                // 关键约束：OnShow 里重入关闭时，InternalCloseAsync 已经把 State 置为 Closing、
                // 接管了退场动画。这里再回滚状态或 SetActive(false) 会打断动画，
                // 让关闭 Task 卡在等一个永远不会完成的转场上（对象被隐藏后协程会停）。
                // 所以直接上抛，让打开方按"取消"收尾，关闭流程继续自己完成。
                if (State == UIPanelState.Closing)
                    throw;

                // 显示失败：回到进入前的稳定状态，并取消可能已经开始播的入场动画。
                // 关键约束：不能把 State 留在 Showing —— 调用方（PanelService / 窗口 / 弹窗）
                // 依赖稳定状态决定"销毁还是复用"，停在中间态会让面板既显示不出来也关不掉。
                // 异常本身继续向上抛，由 PanelService 决定撤销登记/销毁实例。
                CancelTransition();

                if (State != UIPanelState.Closed)
                {
                    // Created / Hidden / Visible 都是稳定状态，原样恢复；
                    // 其他状态（None / Showing / Hiding）说明调用方状态不合法，统一回 Created。
                    if (stateBeforeShow == UIPanelState.Created
                        || stateBeforeShow == UIPanelState.Hidden
                        || stateBeforeShow == UIPanelState.Visible)
                    {
                        State = stateBeforeShow;
                    }
                    else
                    {
                        State = UIPanelState.Created;
                    }

                    if (State != UIPanelState.Visible)
                        gameObject.SetActive(false);
                }

                throw;
            }
        }

        /// <summary>由 UIManager 调用：隐藏面板（不销毁），执行 OnHide。</summary>
        internal void InternalHide()
        {
            // 允许 Showing -> Hiding：OnShow 里重入关闭（CloseSelf）时面板正处于 Showing。
            // 如果这里直接 return，关闭会被静默忽略，面板会卡在 Showing
            // （既显示不出来也关不掉，KeepInstance 还会把它以中间态塞进缓存）。
            if (State != UIPanelState.Visible && State != UIPanelState.Showing)
                return;

            State = UIPanelState.Hiding;
            OnHide();

            if (Presenter != null)
                Presenter.OnHide();

            // 这里取消的是"入场动画"（如果还在播）。
            CancelTransition();

            IPanelTransition transition = Transition;
            if (transition == null)
            {
                // 没配置转场：直接隐藏，行为和以前一样。
                gameObject.SetActive(false);
                State = UIPanelState.Hidden;
                _hideTask = Task.CompletedTask;
                return;
            }

            // 有转场：先播退场，播完再 SetActive(false)。
            // 用独立的 CTS，这样"窗口被快速切回来"时可以取消这次退场。
            _transitionCts = new CancellationTokenSource();
            _hideTask = RunTransitionOutAsync(transition, _transitionCts.Token);
        }

        /// <summary>
        /// 等待隐藏（含退场动画）完成；未在隐藏中时立即完成。
        /// 窗口切换用它保证"旧窗口退场播完再显示新窗口"。
        /// </summary>
        internal Task HideAsync()
        {
            if (State == UIPanelState.Hiding && _hideTask != null)
                return _hideTask;

            return Task.CompletedTask;
        }

        /// <summary>
        /// 由 UIManager 调用：关闭面板（播完退场后再销毁或入缓存），返回关闭流程 Task。
        /// 正在关闭时返回同一个 Task，保证退场动画只播一次；animateOut=false 时同步关完。
        /// </summary>
        internal Task InternalCloseAsync(PanelCloseReason reason, bool animateOut, bool destroyInstance)
        {
            if (State == UIPanelState.Closed)
                return Task.CompletedTask;

            // 只有"正在关闭"才复用旧 Task：KeepInstance 入缓存后同一个实例还会被复用/再次关闭，
            // 那时 State 是 Hidden/Visible，必须重新走一遍关闭流程（缓存 Task 会直接完成、什么都关不掉）。
            if (State == UIPanelState.Closing && _closeTask != null)
                return _closeTask;

            _closeTask = CloseCoreAsync(reason, animateOut, destroyInstance);
            return _closeTask;
        }

        /// <summary>
        /// 等待本次关闭流程结束（退场动画 + 销毁/入缓存）；未在关闭中时立即完成。
        /// </summary>
        public Task WaitForCloseAsync()
        {
            if (State == UIPanelState.Closing && _closeTask != null)
                return _closeTask;

            return Task.CompletedTask;
        }

        /// <summary>
        /// 关闭流程本体：先进入 Closing 并取消入场动画，播完退场后按 destroyInstance 销毁或入缓存。
        /// 动画异常/取消都不会中断关闭，保证面板最终一定会落到 Closed 或 Hidden。
        /// </summary>
        private async Task CloseCoreAsync(PanelCloseReason reason, bool animateOut, bool destroyInstance)
        {
            MarkClosing();

            if (animateOut && Transition != null)
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                _closeCts = cts;
                try
                {
                    await Transition.PlayOutAsync(this, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 退场被打断（例如对象即将销毁）视为直接完成，关闭流程继续。
                }
                catch (Exception exception)
                {
                    // 关闭流程不能被动画异常卡住：记录后继续。
                    Debug.LogException(exception);
                }
                finally
                {
                    cts.Dispose();

                    if (ReferenceEquals(_closeCts, cts))
                        _closeCts = null;
                }
            }

            if (this == null)
                return;

            gameObject.SetActive(false);

            if (destroyInstance)
            {
                InternalClose();
            }
            else
            {
                // KeepInstance：不触发 OnClose，进缓存前先执行 OnSleep。
                State = UIPanelState.Hidden;
                InternalSleep();
            }

            OnClosed();
        }

        /// <summary>
        /// 进入 Closing：必要时补触发 OnHide（Visible/Showing 才触发，Hidden/Hiding 已经触发过），
        /// 并取消正在播的入场/隐藏动画，把控制权交给关闭流程。
        /// </summary>
        private void MarkClosing()
        {
            UIPanelState previous = State;
            State = UIPanelState.Closing;

            if (previous == UIPanelState.Visible || previous == UIPanelState.Showing)
            {
                OnHide();

                if (Presenter != null)
                    Presenter.OnHide();
            }

            CancelTransition();
        }

        /// <summary>由 UIManager 调用：关闭面板，执行 OnClose。之后由 UIManager 决定是否销毁。</summary>
        internal void InternalClose()
        {
            if (State == UIPanelState.Closed)
                return;

            CancelTransition();
            OnClose();

            if (Presenter != null)
            {
                Presenter.OnClose();
                Presenter = null;
            }

            State = UIPanelState.Closed;
        }

        #endregion

        #region 转场播放

        /// <summary>入场：先取消旧转场，再播新的。</summary>
        private void PlayTransitionIn()
        {
            IPanelTransition transition = Transition;
            if (transition == null)
                return;

            // 可能上一次退场还没播完（比如窗口被快速切回来），先取消。
            CancelTransition();
            _transitionCts = new CancellationTokenSource();
            RunTransitionInAsync(transition, _transitionCts.Token);
        }

        /// <summary>
        /// 退场播完后才真正隐藏；期间面板又被 Show 时会取消这次隐藏。
        /// 返回的 Task 供 HideAsync 等待（取消也算完成，调用方不必区分）。
        /// </summary>
        private async Task RunTransitionOutAsync(IPanelTransition transition, CancellationToken cancellationToken)
        {
            try
            {
                await transition.PlayOutAsync(this, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 快速切回来 / 面板被销毁：正常取消，不打印错误。
                return;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            // 动画期间面板可能已经关闭/被重新显示，这里再确认一次状态。
            if (this == null || State != UIPanelState.Hiding)
                return;

            gameObject.SetActive(false);
            State = UIPanelState.Hidden;
        }

        /// <summary>入场动画的后台等待；入场不需要在完成后改状态，所以只负责吞取消异常。</summary>
        private async void RunTransitionInAsync(IPanelTransition transition, CancellationToken cancellationToken)
        {
            try
            {
                await transition.PlayInAsync(this, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 重复打开/关闭时取消，属于正常流程。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>取消正在播放的转场（入场和退场共用同一个 CTS）。</summary>
        private void CancelTransition()
        {
            if (_transitionCts == null)
                return;

            _transitionCts.Cancel();
            _transitionCts.Dispose();
            _transitionCts = null;
        }

        /// <summary>面板被销毁时兜底：取消转场与关闭动画，避免协程/回调还在等一个已经不存在的对象。</summary>
        private void OnDestroy()
        {
            CancelTransition();

            if (_closeCts != null)
            {
                _closeCts.Cancel();
                _closeCts.Dispose();
                _closeCts = null;
            }
        }

        #endregion

        #region 关闭与关闭拦截

        /// <summary>关闭自己（业务代码里点"关闭"按钮时用）。</summary>
        public void CloseSelf()
        {
            CloseSelf(PanelCloseReason.UserClose);
        }

        /// <summary>关闭自己，并指定关闭原因（一般用默认的 UserClose 即可）。</summary>
        public void CloseSelf(PanelCloseReason reason)
        {
            // 关闭路由回创建自己的那套上下文；没有上下文时退回到默认静态门面。
            if (Framework != null)
                Framework.Close(this, reason);
            else
                UIManager.Close(this, reason);
        }

        /// <summary>
        /// 关闭前的拦截钩子（给没有 Presenter 的简单界面用）：返回 false 表示"先别关"。
        /// 只对 PanelCloseReason.UserClose 调用；ForceClose 直接放行。
        /// </summary>
        protected virtual bool CanClose(PanelCloseReason reason)
        {
            return true;
        }

        /// <summary>
        /// 框架内部调用：先问界面本身，再问 Presenter（任何一方拒绝就不能关）。
        /// 只有用户主动关闭才会走到这里。
        /// </summary>
        internal bool InternalCanClose(PanelCloseReason reason)
        {
            // ForceClose 必须放行：切场景 / CloseAll / Reset 如果也被拦，
            // 界面会被带到下一个场景；更糟的是窗口栈可能已经被清空，返回键会彻底失效。
            if (reason == PanelCloseReason.ForceClose)
                return true;

            if (!CanClose(reason))
                return false;

            return Presenter == null || Presenter.InternalCanClose(reason);
        }

        #endregion

        #region 睡眠与唤醒

        /// <summary>
        /// KeepInstance 面板关闭、被放进实例缓存时调用。
        /// 顺序：先面板自身的 OnSleep，再 Presenter.OnSleep。
        /// 与 OnShow/OnHide/OnClose 的"先面板、后 Presenter"保持一致：
        /// 面板先把自己的状态和事件订阅收拾干净，Presenter 再基于稳定的 View 收尾。
        /// </summary>
        internal void InternalSleep()
        {
            OnSleep();

            if (Presenter != null)
                Presenter.Sleep();
        }

        /// <summary>
        /// 缓存实例被重新打开时调用，之后会紧接着走 InternalShow。
        /// 顺序与 InternalSleep 对称：先面板 OnResume，再 Presenter.Resume，
        /// 保证 Presenter 恢复业务监听时 View 已经就绪。
        /// </summary>
        internal void InternalResume()
        {
            OnResume();

            if (Presenter != null)
                Presenter.Resume();
        }

        #endregion

        #region Presenter

        /// <summary>
        /// 创建这个面板的 Presenter。默认返回 null（简单界面可以不用 Presenter）。
        ///
        /// 需要分离视图和逻辑的界面，在这里 new 一个 Presenter 子类：
        ///   protected override PresenterBase CreatePresenter()
        ///   {
        ///       return new BagPresenter();
        ///   }
        /// 框架会在 OnCreate 之后调用它，并把面板实例绑定给 Presenter。
        /// </summary>
        protected virtual PresenterBase CreatePresenter()
        {
            return null;
        }

        #endregion

        #region 子类钩子

        /// <summary>只执行一次：找控件、建列表、注册事件。</summary>
        protected virtual void OnCreate()
        {
        }

        /// <summary>每次显示时执行：根据 data 刷新界面。</summary>
        protected virtual void OnShow(UIPanelData data)
        {
        }

        /// <summary>每次隐藏时执行：停动画、停计时器、取消订阅等。</summary>
        protected virtual void OnHide()
        {
        }

        /// <summary>关闭前执行：释放自己创建的非托管资源、注销全局事件。</summary>
        protected virtual void OnClose()
        {
        }

        /// <summary>
        /// 本次关闭流程真正结束（退场动画播完、状态已落到 Closed 或 Hidden）时执行。
        /// 与 OnClose 的区别：OnClose 只在实例即将销毁时调用；OnClosed 在 KeepInstance 入缓存时也会触发。
        /// </summary>
        protected virtual void OnClosed()
        {
        }

        /// <summary>
        /// KeepInstance 面板被放进实例缓存时执行（OnClose 不会触发）。
        /// 没有 Presenter 的界面靠它退订全局事件，否则事件订阅会跟着缓存实例一直留着。
        /// </summary>
        protected virtual void OnSleep()
        {
        }

        /// <summary>
        /// 缓存实例被重新打开、即将 OnShow 时执行：在这里重新订阅 OnSleep 里退掉的事件。
        /// </summary>
        protected virtual void OnResume()
        {
        }

        #endregion
    }
}
