using System;

namespace CandyUI
{
    /// <summary>
    /// 标记一个面板类，告诉框架三件事：
    /// 1. 这个面板的 Prefab 资源地址是什么（Address）；
    /// 2. 默认放在哪一层（Layer）；
    /// 3. 关闭后资源怎么处理（CachePolicy）。
    ///
    /// 为什么用"特性"而不是让每个面板重写属性？
    /// 因为这样面板类不需要 new、不需要在场景里预置、不需要手动注册，
    /// 框架用反射就能拿到"类 -> 地址"的映射。
    /// 新增一个界面时，只需要写一个类 + 一个 Prefab，不用改 UIManager。
    ///
    /// 例子：
    ///   [UIPanel("UI/BagPanel", UILayer.Window)]
    ///   public sealed class BagPanel : UIPanel { ... }
    ///
    ///   [UIPanel("UI/MainPanel", UILayer.Main, AssetCachePolicy.KeepAlive)]
    ///   public sealed class MainPanel : UIPanel { ... }
    ///
    /// 地址的含义由加载器决定：
    /// - ResourcesAssetLoader：相对任意 Resources 文件夹的路径，例如 "UI/BagPanel"；
    /// - 以后的 YooAssetLoader：YooAsset 的 Location，例如 "UI_BagPanel"。
    /// 面板代码不关心区别，这正是"资源加载可替换"的关键。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class UIPanelAttribute : Attribute
    {
        /// <summary>面板 Prefab 的资源地址，由具体加载器解释。</summary>
        public string PrefabAddress { get; }

        /// <summary>默认归属的 UI 层。打开时也可以临时覆盖。</summary>
        public UILayer Layer { get; }

        /// <summary>面板关闭后 Prefab 资源怎么处理。</summary>
        public AssetCachePolicy CachePolicy { get; }

        /// <summary>
        /// 仅对弹窗层（UILayer.Popup）有效：点击遮罩是否关闭这个弹窗。
        /// 默认 false —— 强制弹窗（公告、网络错误、重连提示）不应该被误关。
        /// </summary>
        public bool DismissOnMaskClick { get; }

        /// <summary>
        /// 仅对弹窗层有效：遮罩透明度。0 = 完全透明（只挡点击），1 = 全黑。
        /// 默认 0.6，能看见背景但注意力会落在弹窗上。
        /// </summary>
        public float MaskAlpha { get; }

        /// <summary>
        /// 仅对弹窗层有效：同时排队多个弹窗时，数值大的先显示。
        /// 例如网络错误 100、活动奖励 10、升级提示 0。
        /// </summary>
        public int PopupPriority { get; }

        /// <summary>
        /// 仅对弹窗层有效：是否允许同一个弹窗类型重复排队、连续显示多次。
        /// 默认 false —— 同类型弹窗再次打开时只刷新当前实例，队列里也按类型去重
        /// （适合"网络错误"这类不应该叠很多层的提示）。
        /// true 时每次 OpenPopupAsync 都会单独入队并显示一次（适合"奖励连弹三次"）。
        /// </summary>
        public bool AllowDuplicatePopup { get; }

        /// <summary>
        /// 关闭这个面板时，实例怎么处理。
        /// Destroy（默认）= 销毁实例并释放句柄；KeepInstance = 隐藏并保留实例，下次打开复用。
        /// </summary>
        public PanelCloseBehavior CloseBehavior { get; }

        /// <summary>
        /// 由 [UIPanel] 标注时调用：校验地址非空，并把遮罩透明度夹到 0~1，其余参数原样保存。
        /// </summary>
        public UIPanelAttribute(
            string prefabAddress,
            UILayer layer = UILayer.Main,
            AssetCachePolicy cachePolicy = AssetCachePolicy.ReleaseOnClose,
            bool dismissOnMaskClick = false,
            float maskAlpha = 0.6f,
            int popupPriority = 0,
            PanelCloseBehavior closeBehavior = PanelCloseBehavior.Destroy,
            bool allowDuplicatePopup = false)
        {
            if (string.IsNullOrEmpty(prefabAddress))
                throw new ArgumentException("UIPanel 的资源地址不能为空", nameof(prefabAddress));

            if (maskAlpha < 0f)
                maskAlpha = 0f;
            else if (maskAlpha > 1f)
                maskAlpha = 1f;

            PrefabAddress = prefabAddress;
            Layer = layer;
            CachePolicy = cachePolicy;
            DismissOnMaskClick = dismissOnMaskClick;
            MaskAlpha = maskAlpha;
            PopupPriority = popupPriority;
            CloseBehavior = closeBehavior;
            AllowDuplicatePopup = allowDuplicatePopup;
        }
    }
}
