namespace CandyUI
{
    /// <summary>
    /// Presenter（表现逻辑）基类：把"业务逻辑"从界面类里拆出来的地方。
    ///
    /// 分工：
    ///   View（UIPanel 子类）  —— 只负责控件、显示数据、把用户操作转发给 Presenter；
    ///   Presenter（本类）     —— 纯 C#，不继承 MonoBehaviour，处理业务逻辑，调用 View 的方法刷新界面；
    ///   Model/Service         —— 数据来源（存档、配置、网络请求的结果）。
    ///
    /// 为什么 Presenter 不继承 MonoBehaviour：
    ///   1. 不依赖场景，可以直接 new 出来，也容易写单元测试；
    ///   2. 不会因为 GameObject 被销毁而出现"半死"状态；
    ///   3. 业务逻辑和 Unity 生命周期解耦，换 UI 方案时逻辑可以原样保留。
    ///
    /// 生命周期（由框架调用，子类按需重写）：
    ///   OnBind   —— 面板 OnCreate 之后调用一次，此时 View 已经可以用了，适合订阅 UI 事件；
    ///   OnShow   —— 每次面板显示都会调用，参数是本次打开传进来的 UIPanelData；
    ///   OnHide   —— 面板被隐藏时调用（面板没有销毁）；
    ///   OnClose  —— 面板即将销毁时调用，记得在这里退订事件；
    ///   OnSleep  —— KeepInstance 面板关闭、被放进实例缓存时调用；
    ///   OnResume —— 缓存实例被重新打开、即将 OnShow 时调用。
    ///
    /// 实例复用的注意点：
    ///   如果面板配置了 closeBehavior: KeepInstance，面板关闭时实例和 Presenter 都不会销毁，
    ///   OnClose 可能永远不触发。所以：
    ///     - "刷新数据"必须写在 OnShow 里，不能写在构造函数里；
    ///     - 订阅了全局事件（信号总线、货币变化等）的话，要在 OnHide/OnSleep 里退订、
    ///       在 OnShow/OnResume 里重新订阅，不能只依赖 OnClose。
    /// </summary>
    public abstract class PresenterBase
    {
        /// <summary>对应的 View（面板实例）。Bind 之后才可用。</summary>
        protected UIPanel View { get; private set; }

        /// <summary>是否已经绑定到 View。</summary>
        protected bool IsBound
        {
            get { return View != null; }
        }

        /// <summary>由框架调用：把 Presenter 和面板实例绑在一起。</summary>
        internal void Bind(UIPanel view)
        {
            View = view;
            OnBind();
        }

        /// <summary>绑定完成时调用一次：在这里订阅 View 的事件。</summary>
        protected virtual void OnBind()
        {
        }

        /// <summary>每次面板显示时调用：在这里拉数据并刷新 View。</summary>
        public virtual void OnShow(UIPanelData data)
        {
        }

        /// <summary>面板被隐藏时调用（实例和 Presenter 都还在）。</summary>
        public virtual void OnHide()
        {
        }

        /// <summary>
        /// KeepInstance 面板关闭、被放进实例缓存时调用。
        /// 此时不会走 OnClose，长期/全局的订阅请在这里退订，或者在 OnHide 里统一处理。
        /// </summary>
        public virtual void OnSleep()
        {
        }

        /// <summary>缓存实例被重新打开、即将 OnShow 时调用。可在 OnShow 之前做恢复工作（如重新订阅）。</summary>
        public virtual void OnResume()
        {
        }

        /// <summary>框架内部调用：面板进入实例缓存。</summary>
        internal void Sleep()
        {
            OnSleep();
        }

        /// <summary>框架内部调用：缓存实例被重新打开。</summary>
        internal void Resume()
        {
            OnResume();
        }

        /// <summary>面板即将销毁时调用：退订事件、保存临时状态等。</summary>
        public virtual void OnClose()
        {
        }

        /// <summary>
        /// 关闭前的拦截钩子：返回 false 表示"先别关"。
        ///
        /// 只对 PanelCloseReason.UserClose 调用；ForceClose（CloseAll/切场景）直接跳过。
        /// 典型用法：有未保存的修改时弹确认框，先返回 false；玩家确认后再调 View.CloseSelf()，
        /// 第二次进入这里时条件已经不成立，就会放行。
        /// </summary>
        protected virtual bool CanClose(PanelCloseReason reason)
        {
            return true;
        }

        /// <summary>框架内部调用：把拦截结果传给 UIPanel。</summary>
        internal bool InternalCanClose(PanelCloseReason reason)
        {
            // ForceClose 不询问，保证切场景 / CloseAll 一定关得掉。
            if (reason == PanelCloseReason.ForceClose)
                return true;

            return CanClose(reason);
        }
    }
}
