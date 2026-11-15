using System.Threading;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 单个面板的进出场动画接口。
    ///
    /// 设计原则：转场只负责"表现"，不负责生命周期。
    /// 框架会在合适的时机调用它：
    ///   - PlayInAsync：面板已经显示（OnShow 已执行）之后；
    ///   - PlayOutAsync：面板即将隐藏 / 销毁之前。
    ///
    /// 默认实现见 FadeScaleTransition（渐变 + 缩放，纯 Unity，不依赖 DOTween）。
    /// 想做别的效果（滑入、翻页、遮罩淡入）只要实现这个接口，
    /// 然后设置 UIManager.DefaultTransition，或在面板里重写 CreateTransition。
    ///
    /// 注意事项（实现方必读）：
    ///   1. 不要自己 Destroy / SetActive，也不要改 State，那些由 UIPanel 统一处理；
    ///   2. 动画期间建议 blocksRaycasts=false，防止玩家点到"正在飞出屏幕"的界面；
    ///   3. 转场实例默认是共享的配置对象，不要在里面保存"当前 Tween/CTS"之类的运行时状态；
    ///   4. 取消时要尽快返回（抛 OperationCanceledException 或直接完成），不要挂住 await。
    /// </summary>
    public interface IPanelTransition
    {
        /// <summary>播放进场动画；cancellationToken 取消时应尽快结束。</summary>
        Task PlayInAsync(UIPanel panel, CancellationToken cancellationToken);

        /// <summary>播放退场动画；cancellationToken 取消时应尽快结束。</summary>
        Task PlayOutAsync(UIPanel panel, CancellationToken cancellationToken);
    }
}
