// 职责：测试专用面板类型集合，记录生命周期钩子顺序，并携带复现缺陷所需的静态开关。
// 依赖：框架 UIPanel 生命周期与 [UIPanel] 地址约定，供 FakeAssetLoader 按地址实例化。
// 限制：静态开关必须靠 ResetSwitches 在用例间复位，否则会跨用例污染。
using System.Collections.Generic;
using UnityEngine;

namespace CandyUI.Tests
{
    /// <summary>
    /// 测试面板基类：记录生命周期钩子的调用次数与顺序（Events），
    /// 子类只需继承并叠加 [UIPanel] 标记即可被 FakeAssetLoader 识别。
    /// </summary>
    public abstract class TestPanelBase : UIPanel
    {
        public readonly List<string> Events = new List<string>();
        public int CreateCount;
        public int ShowCount;
        public int HideCount;
        public int CloseCount;
        public int SleepCount;
        public int ResumeCount;

        /// <summary>生命周期事件按发生顺序拼接的轨迹，用于断言 Sleep → Resume → Show 的顺序。</summary>
        public string EventTrace { get { return string.Join(",", Events); } }

        /// <summary>
        /// 记录 Create 事件并计数，供用例断言 OnCreate 在实例生命周期内只调用一次。
        /// </summary>
        protected override void OnCreate()
        {
            CreateCount++;
            Events.Add("Create");
        }

        /// <summary>
        /// 记录 Show 事件并计数；子类通过 base.OnShow 叠加自己的行为后再断言调用次数。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            ShowCount++;
            Events.Add("Show");
        }

        /// <summary>
        /// 记录 Hide 事件并计数，用于断言窗口被遮挡或 KeepInstance 关闭时的表现。
        /// </summary>
        protected override void OnHide()
        {
            HideCount++;
            Events.Add("Hide");
        }

        /// <summary>
        /// 记录 Close 事件并计数，用于区分销毁关闭与 KeepInstance 的休眠关闭。
        /// </summary>
        protected override void OnClose()
        {
            CloseCount++;
            Events.Add("Close");
        }

        /// <summary>
        /// 记录 Sleep 事件并计数；KeepInstance 关闭时应与 OnResume 成对出现。
        /// </summary>
        protected override void OnSleep()
        {
            SleepCount++;
            Events.Add("Sleep");
        }

        /// <summary>
        /// 记录 Resume 事件并计数；缓存实例重开时应先于 OnShow 调用。
        /// </summary>
        protected override void OnResume()
        {
            ResumeCount++;
            Events.Add("Resume");
        }

        /// <summary>统计指定层上处于 Visible 且激活状态的面板实例数（用于断言"同时可见弹窗唯一"）。</summary>
        public static int CountVisible(UILayer layer)
        {
            int count = 0;
            foreach (TestPanelBase panel in Object.FindObjectsOfType<TestPanelBase>(true))
            {
                if (panel == null) continue;
                if (panel.Layer != layer) continue;
                if (panel.State != UIPanelState.Visible) continue;
                if (!panel.gameObject.activeSelf) continue;
                count++;
            }
            return count;
        }

        /// <summary>用例开始时复位静态开关，避免跨用例污染。</summary>
        public static void ResetSwitches()
        {
            CloseGuardTestPanel.AllowUserClose = false;
            ThrowOnShowTestPanel.ThrowOnShow = true;
            ReentrantCloseTestPanel.CloseOnNextShow = false;
            ReentrantClosePopupTestPanel.CloseOnNextShow = false;
            KeepInstanceThrowOnShowTestPanel.ThrowOnNextShow = false;
            OwnCanvasTestPanel.AddCanvasToPrefab = false;
            OwnCanvasTestPanel.SyncCanvasSwitch();
        }
    }

    [UIPanel("CandyUITests/PlainPanel")]
    public sealed class PlainTestPanel : TestPanelBase
    {
    }

    /// <summary>
    /// Prefab 根自带 Canvas 的测试面板；静态开关决定 FakeAssetLoader 造 Prefab 时是否真的挂 Canvas。
    /// </summary>
    [UIPanel("CandyUITests/OwnCanvasPanel")]
    public sealed class OwnCanvasTestPanel : TestPanelBase
    {
        /// <summary>true 时该类型的测试 Prefab 根带 Canvas；用例可在打开前打开/关闭该开关做对照。</summary>
        public static bool AddCanvasToPrefab;

        /// <summary>
        /// 按当前静态开关把本类型登记/移出 FakeAssetLoader 的自带 Canvas 类型集合。
        /// </summary>
        public static void SyncCanvasSwitch()
        {
            if (AddCanvasToPrefab)
                FakeAssetLoader.OwnCanvasTypes.Add(typeof(OwnCanvasTestPanel));
            else
                FakeAssetLoader.OwnCanvasTypes.Remove(typeof(OwnCanvasTestPanel));
        }
    }

    /// <summary>UserClose 会被 CanClose 拦截的面板；ForceClose 必须能绕过。</summary>
    [UIPanel("CandyUITests/CloseGuardPanel")]
    public sealed class CloseGuardTestPanel : TestPanelBase
    {
        public static bool AllowUserClose;

        /// <summary>
        /// 仅当 AllowUserClose 为 true 时放行 UserClose，用于验证 CanClose 拦截与 ForceClose 绕过。
        /// </summary>
        protected override bool CanClose(PanelCloseReason reason)
        {
            return AllowUserClose;
        }
    }

    /// <summary>关闭时保留实例（State=Hidden + OnSleep），再次打开走 OnResume + OnShow。</summary>
    [UIPanel("CandyUITests/KeepInstancePanel", closeBehavior: PanelCloseBehavior.KeepInstance)]
    public sealed class KeepInstanceTestPanel : TestPanelBase
    {
    }

    [UIPanel("CandyUITests/WindowA", UILayer.Window)]
    public sealed class WindowATestPanel : TestPanelBase
    {
    }

    [UIPanel("CandyUITests/WindowB", UILayer.Window)]
    public sealed class WindowBTestPanel : TestPanelBase
    {
    }

    [UIPanel("CandyUITests/WindowC", UILayer.Window)]
    public sealed class WindowCTestPanel : TestPanelBase
    {
    }

    /// <summary>窗口 D：只用于"关闭 C 的动画期间打开新窗口"的竞态用例。</summary>
    [UIPanel("CandyUITests/WindowD", UILayer.Window)]
    public sealed class WindowDTestPanel : TestPanelBase
    {
    }

    [UIPanel("CandyUITests/PopupA", UILayer.Popup, popupPriority: 0)]
    public sealed class PopupATestPanel : TestPanelBase
    {
    }

    [UIPanel("CandyUITests/PopupB", UILayer.Popup, popupPriority: 100)]
    public sealed class PopupBTestPanel : TestPanelBase
    {
    }

    [UIPanel("CandyUITests/PopupC", UILayer.Popup, popupPriority: 50)]
    public sealed class PopupCTestPanel : TestPanelBase
    {
    }

    /// <summary>允许同类型重复入队（allowDuplicatePopup）。</summary>
    [UIPanel("CandyUITests/PopupDup", UILayer.Popup, popupPriority: 10, allowDuplicatePopup: true)]
    public sealed class PopupDupTestPanel : TestPanelBase
    {
    }

    /// <summary>首次 OnShow 抛异常，用于验证 InternalShow 的状态回滚。</summary>
    [UIPanel("CandyUITests/ThrowOnShowPanel")]
    public sealed class ThrowOnShowTestPanel : TestPanelBase
    {
        public static bool ThrowOnShow = true;

        /// <summary>
        /// 首次显示时故意抛异常（开关随即复位），用于验证 InternalShow 的状态回滚与清理。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            base.OnShow(data);
            if (!ThrowOnShow) return;
            ThrowOnShow = false;
            throw new System.InvalidOperationException("[Test] OnShow 故意抛出的异常");
        }
    }

    /// <summary>OnShow 里调用 CloseSelf 的普通面板（缺陷 #3 普通面板场景）。</summary>
    [UIPanel("CandyUITests/ReentrantClosePanel")]
    public sealed class ReentrantCloseTestPanel : TestPanelBase
    {
        public static bool CloseOnNextShow;

        /// <summary>
        /// 开关打开时在 OnShow 内调用 CloseSelf，复现“显示过程中重入关闭”（缺陷 #3 普通面板场景）。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            base.OnShow(data);
            if (!CloseOnNextShow)
                return;

            CloseOnNextShow = false;
            CloseSelf();
        }
    }

    /// <summary>OnShow 里调用 CloseSelf 的弹窗（缺陷 #3 弹窗场景：_active / 遮罩 / 队列不能卡住）。</summary>
    [UIPanel("CandyUITests/ReentrantClosePopup", UILayer.Popup, popupPriority: 5)]
    public sealed class ReentrantClosePopupTestPanel : TestPanelBase
    {
        public static bool CloseOnNextShow;

        /// <summary>
        /// 开关打开时在 OnShow 内关闭自己，验证弹窗服务不因 _active/遮罩未清而卡住（缺陷 #3 弹窗场景）。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            base.OnShow(data);
            if (!CloseOnNextShow)
                return;

            CloseOnNextShow = false;
            CloseSelf();
        }
    }

    /// <summary>KeepInstance 面板，可在重开后的第一次 OnShow 抛异常（缺陷 #4 缓存重开回滚）。</summary>
    [UIPanel("CandyUITests/KeepInstanceThrowPanel", closeBehavior: PanelCloseBehavior.KeepInstance)]
    public sealed class KeepInstanceThrowOnShowTestPanel : TestPanelBase
    {
        public static bool ThrowOnNextShow;

        /// <summary>
        /// 开关打开时在缓存重开的 OnShow 中抛异常，验证实例回滚到缓存表（缺陷 #4）。
        /// </summary>
        protected override void OnShow(UIPanelData data)
        {
            base.OnShow(data);
            if (!ThrowOnNextShow)
                return;

            ThrowOnNextShow = false;
            throw new System.InvalidOperationException("[Test] KeepInstance 重开 OnShow 故意抛出的异常");
        }
    }

    /// <summary>同类型多实例用例用普通面板：不同实例名可同时打开。</summary>
    [UIPanel("CandyUITests/MultiInstancePanel")]
    public sealed class MultiInstanceTestPanel : TestPanelBase
    {
    }

    /// <summary>同类型多实例用例用 KeepInstance 面板：关闭后按实例名缓存，可复用同一实例。</summary>
    [UIPanel("CandyUITests/MultiInstanceKeepInstancePanel", closeBehavior: PanelCloseBehavior.KeepInstance)]
    public sealed class MultiInstanceKeepInstanceTestPanel : TestPanelBase
    {
    }

    /// <summary>同类型多实例用例用窗口面板：不同实例名可同时存在于窗口栈。</summary>
    [UIPanel("CandyUITests/MultiInstanceWindow", UILayer.Window)]
    public sealed class MultiInstanceWindowTestPanel : TestPanelBase
    {
    }
}
