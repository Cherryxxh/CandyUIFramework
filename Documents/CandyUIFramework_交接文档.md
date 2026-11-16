# CandyUIFramework 框架交接文档

> 仓库：`D:\unity guoji\unity project guoji\CandyUIFramework`（UPM 包 `com.cherryxxh.candyui`）
> 示例工程：`D:\unity guoji\unity project guoji\MakeUiFrame`（Demo 场景、Demo 脚本与 YooAsset 适配器在那边）
> 版本：2026-11-16（从主工程 subtree split 独立为包仓库；HEAD 以 `git log` 为准）
> 面向：接手这个包的人（需要会一点 C# 和 Unity，不要求看过开发过程）

---

## 1. 这个项目是什么

一套**自用、轻量、模块化**的 Unity UI 框架，目标是"开新游戏项目时，作为 UPM 包（`com.cherryxxh.candyui`）装进新工程，就能快速写界面"。

它负责的事情：

- 界面的打开 / 关闭 / 显示 / 隐藏 / 返回
- 界面的生命周期（创建、显示、隐藏、关闭时播完退场再销毁/入缓存、休眠、唤醒）
- 层（Layer）与渲染顺序
- 异步加载 + 资源缓存 + 引用计数 + 引用切换（Resources / YooAsset 等）
- 窗口栈（同时只显示一个窗口，返回上一个）
- 弹窗队列 + 半透明遮罩 + 点击遮罩关闭
- 视图/逻辑分离（MVP：Panel / Presenter / Service）
- 面板转场动画（默认纯协程实现，可选 DOTween 实现）
- 关闭拦截（"设置还没保存，确定离开吗？"）
- 调试日志与状态快照

它**不负责**：控件库（按钮、格子、虚拟列表）、红点、多语言、UI 代码生成工具、完整游戏业务。这些属于"业务零件"，不放进框架包，按需加在宿主工程的业务目录（示例工程 MakeUiFrame 里同类扩展见 `Assets/CandyUI.YooAsset`）。

目录名是 **CandyUIFramework**，命名空间和资源地址前缀是 **CandyUI**——为什么这样拆，见第 13 节。

---

## 2. 环境与依赖

| 项 | 值 |
|---|---|
| Unity | 2022.3.62f3 |
| 渲染 | URP 14.0.12 |
| UI | UGUI（com.unity.ugui 1.0.0）+ TextMeshPro 包 3.0.7 |
| 输入 | **只支持新 Input System**（包依赖 com.unity.inputsystem ≥1.7.0；示例工程用 1.14.2。Active Input Handling = Input System Package (New)） |
| 补间 | DOTween 1.3.045（示例工程 MakeUiFrame 的 `Assets/External/Demigiant/DOTween`），可选，仅示例转场使用 |
| 资源 | 内置 Resources（默认、框架自带）+ YooAsset 3.0.7 适配器（本包不内置，见示例工程 MakeUiFrame 的 `Assets/CandyUI.YooAsset`） |
| Demo 场景 | 见示例工程 MakeUiFrame（Demo 场景、Demo 脚本与 YooAsset 适配器均不在本包） |
| 关系图 | `D:\AUse\Project graph\Proj\CandyUIFramework框架关系图.prg`（Project Graph 4.3.7 打开） |

注意：
- TMP Essentials **不在本包内**：宿主工程首次使用 TMP 时要执行 `Window > TextMeshPro > Import TMP Essential Resources`（示例工程把它放在 `Assets/External/TextMesh Pro`，含 `Resources/TMP Settings.asset` 与 LiberationSans SDF）。示例 Demo 的文本全部用 `TextMeshProUGUI`；没导 TMP Essentials 文字会空白。
- `CandyUIFramework.Runtime` 不引用 TMP：框架本身不创建文字控件，只有示例 Demo 用。示例 Demo 编进宿主工程的 Assembly-CSharp，能直接引用 `Unity.TextMeshPro`。
- 输入系统是"仅新"：老项目接入时要在 Player Settings 里切到 New，并装 Input System 包；`UIRoot` 会自动创建 `InputSystemUIInputModule`。
- DOTween 不是框架必需依赖：`CandyUIFramework.Runtime` 不引用它；只有示例工程的 `DOTweenTransition` 用它。

### 2.1 安装方式（UPM 包）

本仓库根目录就是 UPM 包（`package.json` 在根上），三种安装方式：

1. **Git URL**（发布后推荐）：宿主工程 `Packages/manifest.json` 里加
   `"com.cherryxxh.candyui": "https://github.com/Cherryxxh/CandyUIFramework.git#v1.0.0"`；
   或在 Package Manager 窗口 `+` → **Add package from git URL…** 粘贴该 URL。
2. **本地 file: 包**（开发/需要改包时）：
   `"com.cherryxxh.candyui": "file:D:/unity guoji/unity project guoji/CandyUIFramework"`。
   需要重新生成 `PanelKeys.g.cs` 时必须用这种方式或嵌入式安装：Git URL 安装的包落在 `Library/PackageCache`（只读），生成器会跳过写入并打告警。
3. **嵌入式包**：把本仓库拷到宿主工程 `Packages/com.cherryxxh.candyui/` 下，随宿主工程一起提交。

包版本 1.0.0，依赖 ugui 1.0.0 / TextMeshPro 3.0.7 / Input System 1.7.0，由 Package Manager 自动拉取；上手代码见包根 `README.md` 的「快速开始」。

---

## 3. 五分钟跑起来（示例工程 MakeUiFrame）

> Demo 场景、Demo 脚本与两个 Setup 菜单都随示例移到 MakeUiFrame，本包只保留框架代码 + 生成器/校验器。

1. 用 Unity 打开示例工程 MakeUiFrame，等编译完成（Console 无红字）。
2. 菜单 **CandyUI / 1. Setup Demo (Create Prefabs + Scene)**：生成 Demo 用到的全部 Prefab 和 `CandyUIDemo.unity`。
3. 菜单 **CandyUI / 2. Setup YooAsset Demo Package**：生成 YooAsset 收集器配置（包 `DefaultPackage`、分组 `UIDemo`、收集 Demo 的 `Resources` 目录、自定义地址规则）。
4. 打开 Demo 场景 `CandyUIDemo.unity`，Play。
5. 左下角工具栏（或按 `F1` 显示/隐藏）可以随时打开任意界面：
   主城 / 设置 / 邮件 / 背包 / 商店 / 确认框 / 奖励 / Toast / 动效 / 返回 / 关闭全部。
6. 看 Console：第一行会告诉你资源系统走的是 YooAsset 还是 Resources。
   正常应看到：`[CandyUI] 资源加载：YooAsset（编辑器模拟模式，包 DefaultPackage）`。
   如果配置失败会自动回退 Resources，日志里有 `[CandyUI] YooAsset 初始化失败…`。

Demo 里的键盘操作：`Esc` = 返回（`UIManager.NavigateBack()`）。

---

## 4. 目录结构

```
com.cherryxxh.candyui/              # 本仓库根目录 = UPM 包根
├── package.json                    # 包名/版本/依赖声明
├── README.md / CHANGELOG.md
├── Runtime/                        # 运行时程序集 CandyUIFramework.Runtime
│   ├── Core/                       # 纯数据/契约/特性/枚举
│   │   ├── UILayer.cs              # 层枚举（含 Mask）
│   │   ├── UIPanelState.cs         # 状态机
│   │   ├── UIPanelData.cs          # 打开参数基类
│   │   ├── UIPanelAttribute.cs     # [UIPanel(...)] 类特性
│   │   ├── UIPanelInfo.cs          # 已打开面板的内部档案
│   │   ├── PanelCloseBehavior.cs   # Destroy / KeepInstance
│   │   ├── PanelCloseReason.cs     # UserClose / ForceClose
│   │   └── Generated/
│   │       └── PanelKeys.g.cs      # 生成文件（提交进仓库、勿手改，见 §11.6）
│   ├── Panel/                      # 面板与 Presenter 基类
│   │   ├── UIPanel.cs              # 所有界面基类 + 生命周期
│   │   └── PresenterBase.cs        # MVP 的 Presenter 基类
│   ├── Management/                 # 门面与内部服务
│   │   ├── UIManager.cs            # 唯一公开入口（Facade）
│   │   ├── UIFramework.cs          # 实例门面 / 多上下文
│   │   └── Internal/
│   │       ├── PanelService.cs     # 面板注册表/关闭/缓存/查询（对外编排）
│   │       ├── PanelCloser.cs      # 关闭编排：播退场 → 销毁 / KeepInstance 入缓存
│   │       ├── PanelOpener.cs      # 在途打开：去重、等待者、取消收尾
│   │       ├── PanelFactory.cs     # 地址 → 实例：加载/实例化/校验/转场
│   │       ├── PanelOpenOperation.cs # 一次在途打开的共享状态
│   │       ├── WindowService.cs    # 窗口栈
│   │       └── PopupService.cs     # 弹窗队列/遮罩/优先级
│   ├── Layers/                     # 窗口/弹窗的辅助结构
│   │   ├── WindowStack.cs
│   │   ├── PopupQueue.cs
│   │   └── PopupMask.cs
│   ├── Transition/                 # 转场动画
│   │   ├── IPanelTransition.cs
│   │   └── FadeScaleTransition.cs  # 默认实现（纯协程，无第三方）
│   ├── Loading/                    # 资源加载抽象
│   │   ├── IAssetLoader.cs
│   │   ├── IAssetHandle.cs
│   │   ├── AssetCache.cs           # 框架级缓存 + 引用计数
│   │   ├── AssetCachePolicy.cs     # ReleaseOnClose / KeepAlive
│   │   └── ResourcesAssetLoader.cs # 默认实现
│   ├── Infrastructure/
│   │   ├── UIRoot.cs               # 自动建 Canvas/EventSystem/层容器
│   │   ├── UIRootConfig.cs         # 根节点配置（渲染模式/相机/分辨率/排序/常驻）
│   │   └── CandyUILog.cs           # 统一日志前缀/开关
│   └── CandyUIFramework.Runtime.asmdef
├── Editor/                         # 编辑器程序集 CandyUIFramework.Editor（Editor-only）
│   ├── CandyUIFramework.Editor.asmdef
│   ├── PanelKeyGenerator.cs        # 菜单：生成面板地址常量
│   └── PanelValidator.cs           # 菜单/命令行：面板引用校验
├── Tests/                          # EditMode 测试程序集 CandyUIFramework.Tests
│   ├── CandyUIFramework.Tests.asmdef
│   ├── TestSupport/                # FakeAssetLoader / ControllableTransition / TestPanels / UITestBase
│   └── ...                         # EditMode 用例（生命周期/窗口栈/弹窗/缓存/竞态/多上下文…）
└── Documents/
    └── CandyUIFramework_交接文档.md
```

上面是唯一一份实现；不要另存一份到宿主工程的 `Assets/` 下维护（见第 13 节）。示例（Demo 场景 / Demo 脚本 / YooAsset 适配器）不在这里，见示例工程 MakeUiFrame。

---

## 5. 架构总览

依赖方向是单向的：

```
业务代码 (Demo / 你的游戏)
        │  只调用
        ▼
UIManager  ←── 默认上下文的静态薄壳（static，业务只需要认识它）
        │  一行转发
        ▼
UIFramework.Default ←── 实例门面（真正的实现：打开/关闭/按身份路由/查询/调试）
        │  转发
        ├── PanelService   （面板表、实例缓存、关闭拦截、对外编排）
        │     ├── PanelOpener  （在途打开：去重、等待者计数、取消废弃收尾）
        │     └── PanelFactory （地址 → 场景实例：加载/实例化/校验/转场）
        ├── WindowService  （窗口栈：同时一个窗口，NavigateBack 恢复上一个）
        └── PopupService   （弹窗队列、优先级、遮罩、点击关闭）
                 │
                 ▼
             AssetCache （地址→句柄缓存、引用计数、KeepAlive）
                 │
                 ▼
             IAssetLoader  ←── ResourcesAssetLoader / YooAssetLoader / 你自己的
```

为什么拆成这样：

- **静态薄壳 + 实例门面 + 服务**：`UIManager` 只把静态调用转发给 `UIFramework.Default`（兼容旧业务，签名不变），`UIFramework` 负责"按身份路由"（关闭时判断这是弹窗、窗口还是普通面板）；状态仍在三个服务里，单个文件不会长成 700 行。
- **打开流程三层**：`PanelOpener` 只认识"一次在途打开"，`PanelFactory` 只认识"地址 → 实例"，`PanelService` 做登记与对外编排；原来描述同一件事的三张表（加载中 / 等待者 / 废弃标记）收敛成一个 `PanelOpenOperation`。
- **接口在变化点**：资源来源会变（Resources/AB/YooAsset/远程），所以只有 `IAssetLoader`/`IAssetHandle` 是接口；界面生命周期稳定，所以 `UIPanel` 是抽象类。
- **核心零插件依赖**：`CandyUIFramework.Runtime` 只引用 `UnityEngine.UI` 和 `Unity.InputSystem`；DOTween、YooAsset 的实现在示例工程 MakeUiFrame，本包不含 `Extras`/`Samples`。

**门面与实例（2026-11："静态门面 → 实例门面"）**

`UIManager` 现在只是面向"默认上下文"的静态薄壳：三个静态服务（`s_panels` / `s_windows` / `s_popups`）与全部 public API 原样保留，业务代码继续按 `UIManager.XXX` 写即可，行为不变。真正的实现搬到了实例门面 `UIFramework`：

- `UIFramework.Default` 懒创建并包住同一套三个服务，`UIManager` 的每个 public 成员都只是一行转发；两边状态互通（同一套服务）。
- 打开/关闭/显示隐藏/查询/缓存/调试的实现与"按身份路由关闭"（弹窗 → 窗口 → 普通面板）都在 `UIFramework` 实例方法里。
- 面板会记录创建它的上下文（`UIPanel.Framework`），`CloseSelf` 据此路由回所属的 `UIFramework`；没有上下文时退回 `UIManager`。
- 当前阶段对外保证"默认上下文 + 多套独立上下文"：`UIFramework.Create` 与独立 Root 见 §6.11。

---

## 6. 核心机制

### 6.1 层（UILayer）

| 枚举 | 值 | 用途 |
|---|---|---|
| `Background` | 0 | 战斗 HUD 底图、背景 |
| `Main` | 100 | 主城、主菜单、常驻界面 |
| `Window` | 200 | 背包、设置、邮件等"一次只看一个"的窗口 |
| `Mask` | 250 | 弹窗遮罩（框架内部用） |
| `Popup` | 300 | 确认框、奖励框 |
| `Toast` | 400 | 飘字、轻提示 |
| `Top` | 500 | 全屏 Loading、强制引导 |

数字故意留间隔，方便以后插层。层容器是 `UIRoot` 下自动创建的拉伸节点，**兄弟顺序就是渲染顺序**。

### 6.2 生命周期与状态机

`UIPanel` 的钩子（子类按需重写）：

| 钩子 | 调用时机 | 典型用途 |
|---|---|---|
| `OnCreate()` | 实例创建后，**只一次** | 搭 UI、取控件引用、订阅 View 内部事件 |
| `OnShow(UIPanelData)` | **每次显示** | 刷新数据、重置选中项、播放入场逻辑 |
| `OnHide()` | 每次隐藏/关闭前 | 停止动画、清理临时状态 |
| `OnClose()` | 真正销毁前（Destroy 模式） | 退订全局事件、释放非资源对象 |
| `OnClosed()` | 本次关闭流程结束（退场播完，实例已销毁或已进缓存） | 关闭后的收尾；KeepInstance 入缓存也会触发 |
| `OnSleep()` | KeepInstance 关闭时 | 清理这次打开才需要的东西（全局订阅等） |
| `OnResume()` | KeepInstance 再次打开时 | 恢复监听（如果需要） |

`UIPanel` 自己也有对应的 `OnSleep` / `OnResume`（上表），没有 Presenter 的界面同样能在休眠时退订全局事件。`PresenterBase` 有对应的 `OnBind / OnShow / OnHide / OnSleep / OnResume / OnClose`，由 `UIPanel` 自动转发；转发顺序统一是"**先面板钩子、后 Presenter 钩子**"（OnShow / OnHide / OnClose / OnSleep / OnResume 都一致）——面板先把自己的状态和事件订阅收拾好，Presenter 再基于已经稳定的 View 收尾或恢复。

状态机（`UIPanelState`）：`None → Created → Showing → Visible → Hiding → Hidden → Closed`；关闭时（有转场）先进入 `Closing` 播退场，播完再落到 `Closed`（销毁）或 `Hidden`（KeepInstance 入缓存）。`Loading` 是预留态。

规则：`Internal*` 方法都是 `internal`，业务不能直接调；只能通过 `UIManager` 或 `CloseSelf()`。

### 6.3 打开一个界面时发生了什么

以 `await UIManager.OpenWindowAsync<BagPanel>(data)` 为例：

1. 读 `BagPanel` 上的 `[UIPanel("CandyUI/Demo/BagPanel", UILayer.Window)]`。
2. `PanelService` 查"已打开表"：已存在就直接 `OnShow`，不会重复创建。
3. 查"加载中表"：正在加载就等待同一个 Task（防连点；各等待方用自己的 token 取消等待，不取消共享加载）。
4. 查 KeepInstance 缓存：命中就唤醒旧实例（`OnResume` → `OnShow`），不重新加载。
5. 都没有 → `AssetCache.LoadAsync(地址, 策略)` 加载 Prefab → `Instantiate` 到对应层容器 → `InternalCreate()`（OnCreate + 创建 Presenter）→ `InternalShow()`（OnShow）。
6. `WindowService` 先隐藏旧窗口并等它播完退场（`HideAsync`），再把新窗口压栈显示。

关闭同样有先后：`PanelService` 先判定 `CanClose` 并从打开表移除，再让面板进入 `Closing` 播退场；播完才 `OnClose` + 销毁 + 释放句柄，或（KeepInstance）`OnSleep` 进缓存。窗口恢复上一个、弹窗推进队列都排在关闭动画播完之后。想等"真正关完"，用 `await UIManager.CloseAsync(panel)` 或 `UIManager.WaitForCloseAsync(panel)`。`Close` / `CloseAsync` 对已经在关闭中的面板是幂等的：直接复用同一个关闭 Task，不会重播退场动画。

### 6.4 关闭、KeepInstance、关闭拦截

| 方式 | 效果 |
|---|---|
| `CloseSelf()` / `UIManager.Close(panel)` | 发起关闭：询问 `CanClose`；受理后立即从打开表移除，后台播退场动画，播完再 OnClose + 销毁 + 句柄 Release（KeepInstance 则 OnSleep 入缓存，不触发 OnClose，触发 OnClosed）。对已经在 `Closing` 的面板直接返回（已受理），不重复发起关闭 |
| `await UIManager.CloseAsync(panel)` | 同上，但会等退场动画播完、实例真正关闭完成后返回（`Task<bool>`，被拦截时不关并返回 false）。若面板已在 `Closing`，则等本次关闭结束再返回 true（不会重复发起关闭） |
| `UIManager.WaitForCloseAsync(panel)` | 只等"正在关闭"的面板关完，不改关闭动作；不在关闭中则立即完成 |
| `TryHidePanel(panel)` | 只 `SetActive(false)`，实例仍在打开表；`HasOpenedPanel=true`、`IsPanelVisible=false` |
| `TryShowPanel(panel)` | 重新显示已打开/已隐藏的实例 |
| `NavigateBack()` | 有弹窗先关弹窗；否则关当前窗口并恢复上一个窗口 |
| `CloseAll()` | 强制关闭所有（ForceClose，不询问拦截） |
| `ClearPanelCache()` / `ClearPanelCache<T>()` | 清空 KeepInstance 实例缓存并递增缓存代号；若某实例正在播关闭动画，动画结束后按"缓存已清空"直接销毁，不再回写缓存 |

**KeepInstance**：在特性里写 `closeBehavior: PanelCloseBehavior.KeepInstance`，关闭时只 `OnHide`（+ `OnSleep`：先面板 `OnSleep`，再 `Presenter.OnSleep`）并进缓存，下次打开复用实例（`OnResume` → `OnShow`）。适合背包、商店这种高频界面。注意：这种模式下 `OnClose` 可能永远不触发，所以全局事件订阅要放 `OnHide`/`OnSleep` 里退订、在 `OnResume` 里重新订阅（没有 Presenter 的界面就用 `UIPanel.OnSleep` / `UIPanel.OnResume`）。关闭时会先播退场动画再进缓存；本次关闭流程真正结束时回调 `OnClosed`（Destroy 与 KeepInstance 两条路径都会触发）。

**关闭拦截**：重写 `CanClose(PanelCloseReason reason)`，返回 `false` 表示"先别关"：

```csharp
protected override bool CanClose(PanelCloseReason reason)
{
    if (reason == PanelCloseReason.ForceClose || !_dirty) return true;

    // 典型做法：弹确认框，用户确认后把 dirty 清掉再 CloseSelf()
    _ = UIManager.OpenPopupAsync<ConfirmPopup>(new ConfirmPopupData
    {
        Message = "设置还没保存，确定离开吗？",
        OnConfirm = () => { _dirty = false; CloseSelf(); },
    });
    return false;
}
```

`CloseAll` / 切场景 / Reset 走 `ForceClose`，**不会**被拦截（否则界面永远关不掉），并且**不等退场动画**直接同步关闭（切场景不能等动画）。

### 6.5 窗口栈 / 弹窗队列 / 遮罩

- **窗口（Window 层）**：`OpenWindowAsync` 会隐藏上一个窗口并压栈；`NavigateBack()` 或 `CloseCurrentWindow()` 关闭当前窗口后恢复上一个。适合"主城 → 背包 → 设置"这种链条。切换/关窗都会先等旧窗口的退场动画播完，不会把 Out 截断。关窗动画期间打开新窗口时，`OpenWindowAsync` 会先等上一次"恢复上一个窗口"的流程结束；恢复时以实时栈顶为准，栈顶已可见则不再重复显示，避免两个窗口同时可见。
- **弹窗（Popup 层）**：`OpenPopupAsync` 维护一个队列，一次只显示一个，带优先级（`popupPriority` 越大越先显示）。可以配置半透明遮罩、点击遮罩关闭、遮罩透明度。弹窗关闭会等关闭动画播完再推进队列或收起遮罩，避免两个弹窗动画重叠。
- 遮罩（`PopupMask`）带 `CanvasGroup`，显示/隐藏都是约 0.12 秒的线性淡入淡出（`Time.unscaledDeltaTime`，不受 timeScale 影响）；淡出开始就视为"已隐藏"（`IsVisible=false`）。工程暂停或对象 inactive 时直接设值，保证强制路径同步生效。
- 同类型弹窗默认"刷新已有实例"而不是"再开一个"；需要连弹（如奖励连发）时在特性上加 `allowDuplicatePopup: true`。
- 排队中的弹窗，`OpenPopupAsync` 会返回 `null`（框架不等待真正显示）；可以用 `CancellationToken` 取消排队。

### 6.6 资源加载与缓存

接口（`CandyUIFramework.Runtime` 唯一认识的资源契约）：

```csharp
public interface IAssetLoader
{
    Task<IAssetHandle> LoadAssetAsync(string loadAddress, CancellationToken cancellationToken = default);
}

public interface IAssetHandle
{
    Object Asset { get; }
    bool IsValid { get; }
    void Release();   // 引用计数 -1；归零后由缓存决定是否卸载
}
```

- `AssetCache` 在 loader 之上再包一层：地址 → 句柄 + 引用计数 + 策略。
- `AssetCachePolicy.ReleaseOnClose`（默认）：界面关闭时释放一次引用（`handle.Release()`），计数归零就卸载。
- `AssetCachePolicy.KeepAlive`：常驻内存，不随关闭释放（需要显式清理或换 loader 时 `ReleaseAll`）。
- `PreloadAsync<T>()`：只加载资源、不创建界面实例（常用于登录/主城阶段预热背包、商店）。
- 换资源系统只改一行：`UIManager.InitializeAssetLoader(new YooAssetLoader(package));`，界面代码零改动。

### 6.7 MVP（视图 / 逻辑分离）

- **Panel（View）**：只负责"长什么样"。持有 `[SerializeField]`/代码创建的控件引用，暴露 `ShowXxx(...)`、`SetXxx(...)` 之类方法；不写业务判断。
- **Presenter**：纯 C# 类。订阅 View 的事件，调 Service 执行业务，再调 View 刷新。
- **Service**：业务数据/规则（背包、商店、玩家数据），可以是纯 C# 或 MonoBehaviour 单例。

真正有业务状态时才需要 Presenter；纯展示界面一个 `UIPanel` 就够，不要为了 MVP 而 MVP。

### 6.8 转场动画

```csharp
public interface IPanelTransition
{
    Task PlayInAsync(UIPanel panel, CancellationToken cancellationToken);
    Task PlayOutAsync(UIPanel panel, CancellationToken cancellationToken);
}
```

- 框架默认**没有转场**（瞬时切换）：`UIManager.DefaultTransition` 初始为 null。Demo 启动时设为 `new DOTweenTransition()`；想用纯协程方案就设 `new FadeScaleTransition()`。
- 单个面板可以重写 `CreateTransition()` 返回自己的实现。
- 转场只负责"表现"：不改状态、不销毁对象；框架在 OnShow 后播 In；隐藏/关闭时播 Out，关闭会等 Out 播完再销毁或入缓存。强制关闭（`CloseAll` / `ClearPanelCache` / `Reset` / 打开失败回滚）不播 Out。
- 转场实例会被多个面板共享，实现里不要存 Tween/CTS 等"跟某个面板绑定"的状态。

### 6.9 让一个面板独立成一个 Canvas（手动）

框架**不会**自动给面板加 Canvas：是否需要独立 Canvas 取决于你的性能目标（缩小 rebuild 范围 vs 控制 draw call），框架不替业务猜。

做法（只改 Prefab，不改代码）：

1. 在面板 Prefab 的**根节点**加一个 `Canvas` 组件（框架只检测根节点，子节点上加 Canvas 不算）。
2. **不要**加 `CanvasScaler` / `GraphicRaycaster`：缩放由 UIRoot 的根 Canvas 决定，射线由根 Canvas 的 GraphicRaycaster 统一处理。
3. 不需要新层、新容器：框架仍把实例化到 `UILayer` 对应的层容器下，它自然成为根 Canvas 下的嵌套 Canvas。

排序与交互：

- 嵌套 Canvas 默认按层级顺序（Hierarchy 顺序）参与渲染，和普通面板一致；框架不改 `renderMode` / `overrideSorting` / `sortingOrder` / `pixelPerfect`。
- 需要比层级顺序更细的排序时，自己打开 `overrideSorting` 并设置 `sortingOrder`；同时给该 Canvas 自己配一个 `GraphicRaycaster`，否则会出现"画面在上、点击命中在下"。
- 遮罩语义不变：弹窗遮罩仍是 Mask 层的独立对象，不依赖面板是不是独立 Canvas。

性能取舍：

- 独立 Canvas 的好处是它的重建（rebuild）不会把根 Canvas 下其他界面一起标脏，频繁变化的界面可以明显降低重建尖峰。
- 代价是该 Canvas 会打断根 Canvas 的合批，draw call 可能增加；是否需要、收益多大必须用 Profiler 实测（Profiler 看 `Canvas.SendWillRenderCanvases`，Frame Debugger 看 batch 数）。
- 打开时控制台会打一条 Info：`面板 Xxx 自带 Canvas，作为独立 Canvas 挂在 Xxx 层（框架不修改其排序与缩放）`。

已知限制：

- 框架只做检测：不校验你配得对不对、不帮你加 Canvas/CanvasScaler/GraphicRaycaster、不帮你排序，也不会因为缺组件而报错。
- 检测只看 Prefab 根节点；根上没有 Canvas、只有子节点有，框架会当作普通面板处理。
- `UIPanelInfo.HasOwnCanvas` 只用于调试快照（`UIManager.GetDebugSnapshot()` 面板行末尾的 `独立Canvas n`），不参与打开/关闭逻辑。

### 6.10 根节点配置 UIRootConfig

UIRoot 的根 Canvas 参数不再写死，统一由 `UIRootConfig` 描述（`Runtime/Infrastructure/UIRootConfig.cs`）：

```csharp
[Serializable]
public struct UIRootConfig
{
    public RenderMode RenderMode;             // 默认 ScreenSpaceOverlay
    public Camera WorldCamera;                // ScreenSpaceCamera / WorldSpace 必填
    public Vector2 ReferenceResolution;       // 默认 (1920, 1080)
    public float MatchWidthOrHeight;          // 默认 0.5，clamp 到 [0,1]
    public int SortingOrder;                  // 根 Canvas 排序号，默认 0
    public bool DontDestroyOnLoad;            // 默认 true
    public bool CreateEventSystemIfMissing;   // 默认 true
    public string Name;                       // 根节点名，默认 "[CandyUI] UIRoot"
}
```

| 字段 | 类型 | 默认值 | 说明 / 回退规则 |
|---|---|---|---|
| `RenderMode` | `RenderMode` | `ScreenSpaceOverlay` | 根 Canvas 渲染模式；`ScreenSpaceCamera` / `WorldSpace` 必须配 `WorldCamera` |
| `WorldCamera` | `Camera` | `null` | Overlay 模式忽略（框架会强制置 null）；非 Overlay 缺失时打 Warning 并回退 Overlay |
| `ReferenceResolution` | `Vector2` | `(1920, 1080)` | `CanvasScaler` 参考分辨率；任一维 `<= 0` 回退默认值 |
| `MatchWidthOrHeight` | `float` | `0.5` | 宽高匹配权重；越界 clamp 到 `[0,1]` |
| `SortingOrder` | `int` | `0` | 根 Canvas 的 `sortingOrder`，用于和场景里其他 Canvas 排序 |
| `DontDestroyOnLoad` | `bool` | `true` | 是否跨场景常驻；只在运行时且 `transform.parent == null` 时生效（EditMode 下跳过，属预期） |
| `CreateEventSystemIfMissing` | `bool` | `true` | 场景无 EventSystem 时是否自动创建；`false` 时只打一条 Info，UI 将无法点击/导航 |
| `Name` | `string` | `"[CandyUI] UIRoot"` | 根节点 GameObject 名；空字符串回退默认名 |

两种用法：

1. **场景里手动摆**：把 `UIRoot` 组件挂到场景节点上，在 Inspector 里改上述字段；`Awake` 按字段初始化（`UIRootConfig.Validate` 会修正非法值并打 Warning）。
2. **代码创建**：`UIRoot.Create(config)`，组件走"先 `SetActive(false)` 挂组件写字段、再 `SetActive(true)`"的路径，保证 `Awake` 读到的配置完整；EditMode 不触发 `Awake`，`Create` 内部会补一次 `InitUIRoot()`。可创建多套独立 root：第一个完成初始化的 root 占据 `UIRoot.Instance`（框架内部使用），后续 root 不再销毁自己、只打一条 Info，各自维护自己的 Canvas / 层容器 / 弹窗遮罩。

示例 1：屏幕空间相机模式（3D 场景里把 UI 渲染到指定相机）

```csharp
UIRootConfig config = UIRootConfig.Default;   // 其余保持默认
config.RenderMode = RenderMode.ScreenSpaceCamera;
config.WorldCamera = mainCamera;              // 必须给，否则回退 Overlay 并打警告
config.SortingOrder = 100;                    // 需要时和场景里其他 Canvas 排序
UIRoot root = UIRoot.Create(config);
```

示例 2：世界空间（把 UI 当 3D 物件摆在场景里）

```csharp
UIRootConfig config = UIRootConfig.Default;
config.RenderMode = RenderMode.WorldSpace;
config.WorldCamera = mainCamera;              // 世界空间同样要求相机（缺相机会回退 Overlay）
config.DontDestroyOnLoad = false;
UIRoot root = UIRoot.Create(config);

// 世界空间 Canvas 的尺寸/位置/朝向由业务自己定，框架不修改
root.RootRect.sizeDelta = new Vector2(1920f, 1080f);
root.RootRect.localScale = Vector3.one * 0.002f;
root.RootRect.SetPositionAndRotation(
    mainCamera.transform.position + mainCamera.transform.forward * 3f,
    mainCamera.transform.rotation);
```

注意：

- `UIRoot.EnsureUIRoot()`（框架第一次打开面板时调用）保持原行为：有 `Instance` 用 `Instance` → 复用场景里已有的 → 按 `UIRootConfig.Default` 新建。想让框架用自定义配置，请在第一次打开面板前自己 `UIRoot.Create(config)`，或场景里摆好 UIRoot 让它的 `Instance` 先初始化。
- `CreateEventSystemIfMissing = false` 只影响"没有 EventSystem 时是否新建"，已有 EventSystem 仍会复用并补新版输入模块。
- 需要"每套界面各自独立"时，用 `UIFramework.Create(config, loader)` 一步建好根节点 + 三个服务 + AssetCache（见 §6.11）；`UIManager` 静态 API 始终只操作默认上下文。

### 6.11 多套 UI 上下文（UIFramework.Create）

`UIFramework.Create(config, loader)` 创建一套完全独立的 UI 上下文：自己的 `UIRoot`（Canvas / CanvasScaler / 层容器 / 弹窗遮罩，按传入的 `UIRootConfig` 配置）、自己的 `PanelService` / `WindowService` / `PopupService` 三个服务、自己的 `AssetCache`。打开表、窗口栈、弹窗队列、缓存引用计数、资源加载器全部按上下文隔离，互不影响。

适用场景：

- 世界空间血条 / 3D 世界 UI（`RenderMode.WorldSpace` 或 `ScreenSpaceCamera`），与屏幕 UI 各用一套配置；
- 分屏、双人同屏：两侧各一套独立 UI，互不抢窗口栈/弹窗队列；
- 内嵌小游戏、局部子场景：独立加载/释放整套界面，不影响主 UI；
- 调试 UI、临时活动窗口：随时创建、随时 `Dispose`，不污染主上下文。

```csharp
UIRootConfig config = UIRootConfig.Default;
config.RenderMode = RenderMode.WorldSpace;
config.WorldCamera = worldCamera;
config.DontDestroyOnLoad = false;

UIFramework worldUi = UIFramework.Create(config, loader);          // 每套上下文传独立 loader
UIPanel panel = await worldUi.OpenPlainPanelAsync<HealthBarPanel>();
worldUi.CloseAll();
worldUi.Dispose();                                                 // 关闭面板 + 释放缓存/加载器 + 销毁自己创建的 UIRoot
```

隔离与共享边界：

- **隔离**：`Root` / Canvas / 缩放 / 层容器 / 弹窗遮罩、三个服务、打开表、窗口栈、弹窗队列、`AssetCache` 引用计数、资源加载器；
- **共享**：`EventSystem` 全局仍只有一个——`UIRoot` 初始化时复用 `EventSystem.current` 或场景已有实例，多套上下文不会各建一个（多个 `EventSystem` 会互相抢输入）；
- **默认上下文**：`UIFramework.Default` 与 `UIManager` 的所有静态 API 只操作默认上下文，仍按 `UIRoot.EnsureUIRoot()` 解析根节点；其它上下文的面板不会出现在 `UIManager` 的查询里。

生命周期语义：

- `IsDefault`：`UIFramework.Default` 为 `true`，`Create` 创建的为 `false`；
- `Dispose()`：关闭该上下文所有面板 → `Panels.Reset()`（释放缓存与加载器）→ 销毁自己创建的 `UIRoot`；重复 `Dispose()` 幂等；
- `Dispose()` 之后：打开/关闭/显示隐藏/缓存/`Root` 抛 `ObjectDisposedException`，只读查询返回清理后的空状态；
- **默认上下文不允许 `Dispose()`**：`UIFramework.Default.Dispose()` 直接抛 `InvalidOperationException`。静态 API 一直依赖它，释放后没有重建入口，与其留下"半死"状态，不如在第一次调用就报错。需要独立生命周期请用 `Create`。

已知限制：`Create` 出来的一套上下文只能显式 `Dispose`，没有自动回收；`EventSystem` 仍全局唯一；每套上下文如果都传 `CreateEventSystemIfMissing = true` 且场景还没 EventSystem，第一个初始化的会创建它，后续复用同一个（它是否 `DontDestroyOnLoad` 跟随该上下文的 `UIRootConfig.DontDestroyOnLoad`）。

### 6.12 同类型多实例（实例名）

同一个面板 / 窗口类型可以同时开多个实例，用"实例名"区分：

```csharp
ShopPanel smith = await UIManager.OpenPlainPanelAsync<ShopPanel>("npc_smith");
ShopPanel merchant = await UIManager.OpenPlainPanelAsync<ShopPanel>("npc_merchant");

UIManager.IsPanelVisible<ShopPanel>("npc_smith");   // true
UIManager.GetOpenedPanels<ShopPanel>();             // 按打开顺序返回两个
UIManager.Close(merchant);                          // 只关商人这一个
```

规则：

- `PanelKey = (类型, 实例名)` 是打开表 / KeepInstance 缓存 / 在途打开表的唯一索引；实例名 `null` 或空白 = 默认实例，与不传实例名的旧 API **完全等价**，旧代码零改动。
- 实例名会写进节点名：`ShopPanel_npc_smith`（默认实例仍是 `ShopPanel`），方便在 Hierarchy 里区分。
- 查询：`GetOpenedPanel<T>(name)`、`HasOpenedPanel<T>(name)`、`IsPanelVisible<T>(name)`、`TryHidePanel<T>(name)`、`TryShowPanel<T>(name, data)`；`GetOpenedPanels<T>()` 按打开顺序返回该类型全部实例。不传名字只查默认实例。
- `CloseOfType<T>()` 关闭该类型**全部**实例（默认 + 命名）。
- 窗口：`OpenWindowAsync<T>(instanceName)`，同类型不同实例名可以同时在窗口栈里，各自独立入栈 / 返回。
- 弹窗仍按队列显示，不支持多实例；需要连弹用 `[UIPanel(..., allowDuplicatePopup: true)]`。
- KeepInstance：按实例名分别缓存，同名复用同一实例，换名新建。
- 注意：`OpenPlainPanelAsync<T>(null)` 这种"单个 null 字面量"调用会同时匹配 `UIPanelData` 与 `string` 两个重载（CS0121 编译歧义）；写成 `OpenPlainPanelAsync<T>(data: null)` 或显式 `(UIPanelData)null` 即可。

---

## 7. 公开 API 速查（UIManager）

> 完整列表看 `Runtime/Management/UIManager.cs`，以下按用途分组。

| 用途 | API |
|---|---|
| 初始化 | `InitializeAssetLoader(IAssetLoader)`、`AssetLoader`、`IsLoaderReady` |
| 打开面板（Main/Toast/Top 等平铺层，不进栈、不走弹窗队列） | `OpenPlainPanelAsync<T>(data = null, ct = default)`、`OpenPlainPanelAsync<T>(instanceName, data = null, ct = default)`（多实例，见 §6.12） |
| 预加载 | `PreloadAsync<T>(ct = default)` |
| 打开窗口（Window 层） | `OpenWindowAsync<T>(data = null, ct = default)`、`OpenWindowAsync<T>(instanceName, data = null, ct = default)` |
| 打开弹窗（Popup 层） | `OpenPopupAsync<T>(data = null, priority 由特性决定, ct = default)` |
| 关闭 | `Close(panel, reason = UserClose)`（发起，后台播退场）、`CloseAsync(panel, reason)`（等真正关完）、`WaitForCloseAsync(panel)`、`CloseOfType<T>(reason)`（关该类型全部实例） |
| 返回 | `NavigateBack()`、`CanNavigateBack`、`CloseCurrentWindow(reason)` |
| 弹窗 | `CloseTopPopup()`、`ClosePopup(popup)`、`CloseAllPopups()` |
| 显示/隐藏 | `TryHidePanel(panel)` / `TryHidePanel<T>()` / `TryHidePanel<T>(instanceName)` / `TryShowPanel(panel, data)` / `TryShowPanel<T>(data)` / `TryShowPanel<T>(instanceName, data)`（都返回 `bool`；弹窗、非栈顶窗口会被拒绝） |
| 查询 | `GetOpenedPanel<T>()` / `GetOpenedPanel<T>(instanceName)`、`GetOpenedPanels<T>()`（按打开顺序）、`HasOpenedPanel<T>()` / `(instanceName)`、`IsPanelVisible<T>()` / `(instanceName)` |
| 缓存 | `ClearPanelCache()`、`ClearPanelCache<T>()` |
| 全部关闭 | `CloseAll()` |
| 调试 | `GetDebugSnapshot()`、`DebugLogEnabled`、`DefaultTransition` |

`OpenPlainPanelAsync` 如果发现目标特性的层是 `Window`/`Popup`，会直接抛 `InvalidOperationException`——防止绕过窗口栈/遮罩。对应场景请用 `OpenWindowAsync` / `OpenPopupAsync`。

**命名约定（2026-11 命名精化后）**：

| 命名 | 含义 |
|---|---|
| `Open*Async` | 打开一个界面；`Async` 后缀只用于返回 `Task` 的方法 |
| `OpenPlainPanelAsync` | 打开平铺层界面（Main/Toast/Top 等），不参与窗口栈、不走弹窗队列 |
| `Close(panel)` | 按实例身份路由关闭（弹窗 / 窗口 / 普通面板），只发起、不等待退场动画 |
| `CloseAsync(panel)` | 与 `Close(panel)` 相同的身份路由，返回 `Task<bool>`；完成时退场已播完、实例已销毁或已入缓存 |
| `CloseOfType<T>()` / `HasOpenedPanel<T>()` | 按类型操作打开表 |
| `Try*` | 可能被拒绝并返回 `bool`（弹窗不能隐藏、非栈顶窗口不能显示等） |
| `NavigateBack()` / `CanNavigateBack` | 导航语义：优先关弹窗，否则回退窗口栈 |
| `CanClose(reason)` | 关闭拦截钩子，返回 `false` 表示"先别关"（Presenter 同名） |

### 7.1 UIFramework（实例门面）

`UIManager` 是面向默认上下文的静态薄壳，真实实现在 `Runtime/Management/UIFramework.cs`。业务可以直接用静态 API；两者操作同一套服务、状态互通。需要多套互不干扰的上下文时用 `UIFramework.Create`（见 §6.11）。

| 成员 | 说明 |
|---|---|
| `UIFramework.Default` | 默认上下文实例（懒创建，包住 `UIManager` 的三个静态服务） |
| `UIFramework.Create(config = default, loader = null)` | 创建一套独立上下文：新建三个服务 + `UIRoot.Create(config)`，loader 非空时注入；返回的实例各自一套 AssetCache |
| `IsDefault` | 是否默认上下文（`Default` 为 `true`，`Create` 出来的为 `false`） |
| `Dispose()` | 关闭所有面板并释放缓存/加载器；非默认上下文销毁自己创建的 Root，默认上下文的 Root 不销毁；重复调用幂等 |
| `Root` | 本上下文的 `UIRoot`；默认上下文首次访问才 `EnsureUIRoot` 并缓存，`Create` 时是已创建好的根节点 |
| `Panels` / `Windows` / `Popups` | 三个服务引用（内部，业务代码不要直接用） |
| `AssetLoader` / `IsLoaderReady` / `DefaultTransition` | 与 `UIManager` 同名成员，读写同一状态 |
| `InitializeAssetLoader(loader)` | 注入资源加载器 |
| 打开 / 关闭 / 显示隐藏 / 查询 / 缓存 / 调试 | 与 `UIManager` 一一对应的实例方法（同名同签名、行为一致），完整列表看 `Runtime/Management/UIFramework.cs` |

---

## 8. 新建一个界面的标准流程

### 8.1 最简界面（纯展示，无 Presenter）

```csharp
[UIPanel("CandyUI/Demo/HelloPanel", UILayer.Main)]
public sealed class HelloPanel : UIPanel
{
    private Text _title;

    protected override void OnCreate()
    {
        // 只跑一次：搭 UI / 取引用
        _title = transform.Find("Title").GetComponent<Text>();
    }

    protected override void OnShow(UIPanelData data)
    {
        // 每次显示：刷新数据
        _title.text = $"你好，第 {++_showCount} 次打开";
    }
}
```

打开：`await UIManager.OpenPlainPanelAsync<HelloPanel>();`
关闭：按钮里 `CloseSelf();`。

新增或修改面板后执行一次菜单 `CandyUI/生成/面板地址常量`，生成/刷新 `PanelKeys` 常量；需要按地址操作时用 `PanelKeys.HelloPanel`，不要写裸字符串（见 §11.6）。

### 8.2 带业务的界面（推荐 4 个文件）

```
ShopPanel.cs        // View：UI 引用 + 展示方法 + 事件
ShopPresenter.cs    // 逻辑：订阅 View、调 Service、刷新 View
ShopService.cs      // 数据/规则：金币、商品、购买
ShopPanelData.cs    // 打开参数：Greeting 等
```

```csharp
[UIPanel("CandyUI/Demo/ShopPanel", UILayer.Window)]
public sealed class ShopPanel : UIPanel
{
    public event Action<int> BuyClicked;

    protected override void OnCreate() { /* 搭 UI、绑定按钮 -> BuyClicked?.Invoke(id) */ }
    protected override PresenterBase CreatePresenter() => new ShopPresenter();
    public void ShowItems(IReadOnlyList<ShopItem> items, int gold) { /* 只负责显示 */ }
}

public sealed class ShopPresenter : PresenterBase
{
    private readonly ShopService _service = new();

    protected override void OnBind()
    {
        var view = (ShopPanel)View;
        view.BuyClicked += OnBuy;
    }

    public override void OnShow(UIPanelData data)
    {
        var view = (ShopPanel)View;
        view.ShowItems(_service.Items, _service.Gold);
    }

    public override void OnClose()
    {
        ((ShopPanel)View).BuyClicked -= OnBuy;
    }

    private void OnBuy(int id) { _service.TryBuy(id, out _); /* 刷新 */ }
}
```

打开：`await UIManager.OpenWindowAsync<ShopPanel>(new ShopPanelData());`

### 8.3 写完检查清单

- [ ] 特性地址 = `Resources` 下 Prefab 路径（去掉 `Resources/` 和 `.prefab`）或 YooAsset location
- [ ] Prefab 上挂了对应的 Panel 脚本
- [ ] `OnCreate` 只做一次性的事；数据刷新放 `OnShow`
- [ ] 有事件订阅的话，在 `OnClose`/`OnHide`/`OnSleep` 里对称退订
- [ ] View 不写业务判断；Presenter 不直接操作 UI 控件
- [ ] 打开入口走 `UIManager`；关闭走 `CloseSelf()`/`NavigateBack()`
- [ ] 关闭再开、连续点两次、加载中关闭，这三种情况都试一遍

---

## 9. 示例工程与调试

> Demo 场景、Demo 脚本与工具栏在示例工程 MakeUiFrame；本包不含 Demo。

- 入口脚本：示例工程 MakeUiFrame 的 `Samples/Demo/DemoLauncher.cs`，挂在 Demo 场景的 `DemoLauncher` 物体上。
  启动顺序：初始化资源系统 → 设全局转场 → 建工具栏 → 预加载各界面 → `OpenWindowAsync<HomeWindow>()`。
- 工具栏：`DemoToolbar.cs` 用独立 Canvas（sortingOrder 1000），盖在遮罩之上，`F1` 显隐；左上角状态文本每 0.2 秒刷新一次 `UIManager.GetDebugSnapshot()`，能看到窗口栈 / 弹窗队列 / 遮罩 / 缓存。
- 日志：框架日志统一带 `[CandyUI]` 前缀；Console 搜索 `[CandyUI]` 即可过滤。想关掉：`UIManager.DebugLogEnabled = false;`。
- 状态快照：`UIManager.GetDebugSnapshot()` 返回多行字符串（打开数、加载中、缓存实例、窗口栈、弹窗队列、遮罩）。
- Demo 行为：主城（KeepInstance）→ 设置（拦截关闭）→ 确认框；邮件演示窗口栈加深；背包/商店演示 MVP；奖励演示弹窗队列；Toast 演示不参与栈的提示层；动效演示 DOTween。
- **TMP 中文字形（重要）**：示例 Demo 用的是 TMP，但 TMP Essentials 自带的 `LiberationSans SDF` 只有拉丁字形。Demo 的界面文案是中文，所以 Console 会刷
  `The character with Unicode value \u5F53 was not found in the [LiberationSans SDF] font asset`，界面上的中文显示成 `□`。
  这不影响框架逻辑（示例工程的 PlayMode 冒烟测试跑的就是这套界面），但正式项目必须：
  1. 用 Window → TextMeshPro → Font Asset Creator 从你的中文字体生成 SDF 字体资源（建议 Dynamic 模式，按需打字形）；
  2. 设成 TMP 默认字体（Project Settings → TextMeshPro → Default Font Asset），或给 `TextMeshProUGUI` 指定 Font Asset / fallback；
  3. 生成字体资源放到项目里并纳入版本管理（大字体建议只保留实际用到的字符集，或走 Dynamic + 图集上限）。

---

## 10. YooAsset 接入（适配器在示例工程）

### 10.1 现状

- 适配器在示例工程 MakeUiFrame 的 `Assets/CandyUI.YooAsset/`，独立程序集 `CandyUIFramework.YooAsset`，**不影响** `CandyUIFramework.Runtime` 的零依赖；本包不内置适配器。
- 编辑器菜单 **CandyUI / 2. Setup YooAsset Demo Package** 会：
  - 创建/复用 `Assets/BundleCollectorSetting.asset`
  - 建包 `DefaultPackage`（可寻址）
  - 建分组 `UIDemo`，收集示例 Demo 的 `Resources` 目录
  - 用自定义地址规则把 `Resources/CandyUI/Demo/HomeWindow.prefab` 映射成 `CandyUI/Demo/HomeWindow`（和 `[UIPanel]` 字符串一致）
  - 顺手创建 `Assets/Resources/YooAssetSettings.asset`（避免 "YooAssetSettings not found" 日志）
- 示例工程的 `DemoLauncher` 启动时先尝试 YooAsset 编辑器模拟模式，失败自动回退 Resources。
- 幂等：重复执行菜单 2、重复 Play 都不会重复创建配置。
- 编辑器模拟模式已被示例工程的 PlayMode 测试覆盖：`Tests/PlayMode/YooAssetLoaderSmokeTests.cs`（4 条，见 §11.5），不需要真打 AB 包也能验证"地址 → Prefab → 面板"这条链路。

### 10.2 3.0.7 初始化代码（可复制到正式项目）

```csharp
YooAssets.Initialize();

var package = YooAssets.TryGetPackage("DefaultPackage", out var p)
    ? p
    : YooAssets.CreatePackage("DefaultPackage");

if (package.InitializeStatus == EOperationStatus.None)
{
    // 编辑器模拟模式：不真正打 AB 包；真机改用 Offline/Host 的 Options
    var buildResult = EditorSimulateBuildInvoker.Build(
        "DefaultPackage", (int)EBundleType.VirtualAssetBundle);

    await package.InitializePackageAsync(new EditorSimulateModeOptions
    {
        EditorFileSystemParameters =
            FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                buildResult.PackageRootDirectory),
    });

    // 3.0.7 的操作对象是"非泛型 awaitable"：先拿 op，再 await（await 结果是 void），
    // 版本号 await 后从 op.PackageVersion 读。
    var versionOp = package.RequestPackageVersionAsync();
    await versionOp;
    await package.LoadPackageManifestAsync(
        new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
}

UIManager.InitializeAssetLoader(new YooAssetLoader(package));
```

### 10.3 正式项目要做的额外步骤

1. 把 UI Prefab 移出 `Resources`（比如放 `Assets/UI/Prefabs/`），收集器路径改过去；示例 Demo 现在留在 Resources 是为了保留回退。
2. 在 YooAsset 窗口用 Build Pipeline 正式构建（编辑器模拟模式只用于开发期）。
3. 真机用 `OfflinePlayModeOptions` 或 `HostPlayModeOptions` + 远端服务初始化。
4. 建议把 YooAsset 初始化放到游戏启动流程（登录/热更之后），UI 框架不关心它。

---

## 11. 工程化信息

### 11.1 程序集

| asmdef | references | 说明 |
|---|---|---|
| `CandyUIFramework.Runtime` | `UnityEngine.UI`、`Unity.InputSystem` | 核心，`autoReferenced: true` |
| `CandyUIFramework.Editor` | `CandyUIFramework.Runtime` | 编辑器工具（地址常量生成 + 面板校验），Editor-only、`autoReferenced: true` |
| `CandyUIFramework.Tests` | `CandyUIFramework.Runtime`、`nunit.framework` | EditMode 测试，Editor-only、`autoReferenced: false`，需宿主工程 `testables` 才编译运行 |

`Editor/` 有独立 asmdef，只在编辑器编译；因此它访问不到 Runtime 的 `internal` 日志类 `CandyUILog`，生成器的告警统一用带 `[CandyUI] ` 前缀的 `Debug.LogWarning`。`CandyUIFramework.YooAsset` 适配器与两个 Demo Setup 脚本已随示例移到 MakeUiFrame，不在本包。

### 11.2 Editor 菜单

- `CandyUI/生成/面板地址常量`：扫描所有 `[UIPanel]` 生成包内 `Runtime/Core/Generated/PanelKeys.g.cs`；域重载后自动检查（见 §11.6）。
- `CandyUI/校验/面板引用`：校验面板继承 / 地址 / Prefab 引用 / 常量命名，结果输出到 Console（见 §11.6）。
- 命令行入口：`-executeMethod CandyUI.PanelValidator.Run`。

> 菜单 1/2（Demo Setup、YooAsset Demo Package）随示例代码移到示例工程 MakeUiFrame，不在本包。

### 11.3 编译校验（不打开 Unity 也能查错）

开发期用临时 Roslyn 工程对照 Unity 程序集编译本包（不在仓库里，位于 `%TEMP%`，丢了可以重建）：

```powershell
dotnet build "$env:TEMP\upm_check\UpmCheck.csproj"
dotnet build "$env:TEMP\upm_check_editor\UpmCheckEditor.csproj" -p:DefineConstants=UNITY_EDITOR
```

注意：
- 第二个工程要加 `UNITY_EDITOR` 宏，才能编译到 `Editor/` 下的编辑器脚本（`PanelKeyGenerator` / `PanelValidator`）。
- 工程只 Compile 本仓库 `Runtime/**/*.cs`（编辑器工程再加 `Editor/*.cs`）；引用 Unity 安装目录 `Managed\UnityEngine\*.dll` 与宿主工程 `Library\ScriptAssemblies` 下的 `UnityEngine.UI.dll` / `Unity.TextMeshPro.dll` / `Unity.InputSystem.dll`。
- 要求 0 error 0 warning；Unity 自己编译才是最终标准，Roslyn 只是"提前发现语法/API 错误"。

### 11.4 Git

- 本仓库由主工程 MakeUiFrame 的框架目录（`Assets/` 下的 `CandyUIFramework`）用 `git subtree split` 拆出，保留框架目录原有的 20 条提交，作为独立的 UPM 包仓库；当前分支 `main`。
- `.gitignore` 面向包仓库（`Library/`、`Temp/`、`Logs/`、`obj/`、`*.csproj`、`*.sln`、IDE 配置等），没有二进制资源；`.gitattributes` 只保留文本规则（`*.cs` / `*.asmdef` / `*.meta` / `*.md`），不用 LFS。
- 提交习惯：一次提交只做一件事；框架改动、编辑器工具、文档/打包配置尽量分开；Demo、YooAsset 适配器与 PlayMode 测试由示例工程 MakeUiFrame 承载，不在本仓库。
- 发布：在提交上打 tag（如 `v1.0.0`）后即可按 `https://github.com/Cherryxxh/CandyUIFramework.git#v1.0.0` 安装；版本号要同步改 `package.json` 与 `CHANGELOG.md`。
- 历史提交清单见 `git log`（subtree split 只保留框架目录相关的 20 条）。

> 早期提交信息中的 API 名以当时版本为准，现名以 §7 速查表为准。

### 11.5 测试（本包 EditMode；PlayMode 在示例工程）

本包只包含 EditMode 测试。测试程序集 `CandyUIFramework.Tests` 是 Editor-only、`autoReferenced: false`，需要在宿主工程显式开启 testable 才会编译运行：

1. 宿主工程 `Packages/manifest.json` 加 `"testables": ["com.cherryxxh.candyui"]`；
2. 用 Unity 打开宿主工程，Window → General → Test Runner → EditMode 页签跑；或命令行：

```powershell
& "D:\unity guoji\edit\2022.3.62f3\Editor\Unity.exe" -batchmode -nographics `
  -projectPath <宿主工程路径> -runTests -testPlatform EditMode `
  -testResults "$env:TEMP\candy_tests.xml"
# 看 candy_tests.xml 的 <test-run total/passed/failed>
```

- 用例：`PanelLifecycleTests`（生命周期、重入关闭、休眠）、`WindowStackTests`（入栈/返回/关非栈顶）、`PopupQueueTests`（优先级、去重、取消出队、连弹）、`AssetCacheTests`（引用计数、KeepAlive、并发取消、同步 loader）、`ConcurrencyTests`（共享加载的等待者）、`DefectRegressionTests`（6 类历史缺陷回归，见 §12.3）、`CloseRaceTests`（关闭竞态：清缓存 vs 关闭动画、Closing 重复关闭、关窗期间打开新窗口，见 §12.5）、`CanvasPanelTests`（Prefab 根自带 Canvas / 普通面板两条实例化路径与快照计数，见 §6.9）、`UIRootConfigTests`（根节点配置：相机模式/参考分辨率/排序/缺相机与非法分辨率回退/EventSystem 开关，见 §6.10）、`MultiContextTests`（多套 UI 上下文隔离：独立根节点/三个服务/AssetCache、关闭互不影响、Dispose 清理与幂等，见 §6.11）、`ContextGuardTests`（默认上下文禁止 Dispose、EventSystem 跟随根配置）、`MultiInstanceTests`（同类型多实例：不同实例名并存/同名复用/关一个留一个/不传名字仍是单实例/CloseOfType 关全部/KeepInstance 命名缓存/窗口多实例同栈，见 §6.12）。
- 用例统一用同步签名 + `GetAwaiter().GetResult()`：Unity Test Framework 的 `[Test]` 不接受 `async Task`（会报 "Method has non-void return value"），需要逐帧 yield 时用 `[UnityTest]` + `IEnumerator`。
- 测试用 `TestSupport/FakeAssetLoader` 按 `[UIPanel]` 地址现造 Prefab，不依赖任何资源；EditMode 下先建 EventSystem 再建 UIRoot（`DontDestroyOnLoad` 在编辑器里会抛异常）。
- 仍通过反射访问 `UIManager.ResetStatics`、`UIRoot.Instance/InitUIRoot`、`UIManager.s_panels`、`PanelService._cache`；改这些私有名要同步改 `Tests/TestSupport/UITestBase.cs`。

**PlayMode 冒烟（12 条，已随示例移到 MakeUiFrame）**

这些用例依赖真实的 Demo Prefab 与 Demo 场景（真实 `Instantiate/Destroy`、`DontDestroyOnLoad`、协程驱动的转场、TMP 控件创建），所以随示例代码维护，不在本包内。

- 跑法：在示例工程里用 `-runTests -testPlatform PlayMode`（projectPath 指向 MakeUiFrame）。
- 用例：真实 Prefab 打开到 Visible、`CloseAsync` 播完退场后销毁、窗口栈 `NavigateBack` 复用原实例、弹窗同类型复用 / 不同类型排队并自动推进、同类型多实例并存、根节点在 DontDestroyOnLoad、`CloseAll` 一次清空。
- 其中 4 条覆盖 **YooAsset 适配器**（`YooAssetLoaderSmokeTests`）：`YooAssetTestBootstrap` 用 `BundleCollectorSetting` 里的 `DefaultPackage` 做编辑器模拟构建（和 DemoLauncher 同一套流程），然后断言
  ①框架装的是 `YooAssetLoader` 而不是 Resources 回退；②地址能命中真实 Prefab、关闭后实例销毁；③`PreloadAsync` 走 YooAsset 后再打开仍在；④不存在的地址抛带 `YooAsset` + 地址上下文的异常。
  收集器没配好时这 4 条会 `Assert.Ignore`，日志里会有 `[CandyUI.Test] YooAsset 初始化失败：…` 说明原因。

### 11.6 面板地址常量与编辑器校验

两个菜单：

- `CandyUI/生成/面板地址常量`：扫描带 `[UIPanel]` 的面板类型（用 `UnityEditor.TypeCache` 扫描 Assembly-CSharp 等运行时代码；测试程序集与编辑器程序集跳过），生成 `Runtime/Core/Generated/PanelKeys.g.cs`。生成目录按 `PackageInfo.FindForAssembly(typeof(UIRoot).Assembly)` 定位到包根（形如 `Packages/com.cherryxxh.candyui/Runtime/Core/Generated`），找不到包时兜底到老用法路径 `Assets/` 下的 `CandyUIFramework/Runtime/Core/Generated`；从 `Library/PackageCache` 只读安装时跳过写入并打 `[CandyUI]` 告警。生成后自动跑一遍面板校验，只打 Console。
- `CandyUI/校验/面板引用`：完整校验，逐条打到 Console；编辑器内弹窗汇总，batchmode 只打日志。

自动化行为：

- `PanelKeyGenerator` 带 `[InitializeOnLoad]`，每次域重载后 `delayCall` 检查一次；**内容没变就不写文件**（逐字节比较，含编码/换行），不会因为每次编译都触发重新导入。
- 生成文件是普通资源并提交进仓库：程序集首次编译就有 `PanelKeys`，新克隆的工程不会因为缺这个类而编译失败。
- 生成文件**不要手改**：改面板地址后执行菜单即可；同样的面板集合重复生成字节完全一致，不会产生无意义 diff。
- 命名规则：成员名 = 类型名，按成员名 ordinal 排序；不同命名空间存在同名类型时改用「命名空间最后一段_类型名」，并在校验里报 Warning；生成文件固定 UTF-8 无 BOM、4 空格缩进、LF 换行。

校验项（Error/Warning 都带可点击定位的上下文：.cs 资产或 Prefab）：

| # | 检查 | 级别 |
|---|---|---|
| 1 | 类型带 `[UIPanel]` 但没继承 `UIPanel` | Error |
| 2 | 地址为空或只有空白 | Error |
| 3 | 多个类型共用同一地址（列出所有类型全名） | Error |
| 4 | `Resources` 下找不到对应 Prefab | Warning（用 YooAsset/Addressables 时可忽略） |
| 5 | Prefab 根节点没挂该面板类型的组件 | Error |
| 6 | `PanelKeys` 成员名冲突/被改名 | Warning |
| 7 | `Popup` 层沿用默认 `popupPriority`/`maskAlpha` | Info（聚合成一条，不刷屏） |

注意：

- 第 4 条只能静态检查 `Resources`。YooAsset/Addressables 的 location 是收集器按规则算出来的，编辑器期无法可靠确认存在性，这部分仍需手动或构建时校验。
- 生成与校验都会跳过测试程序集：`Tests/` 里的假面板（`FakeAssetLoader` 现造 Prefab、没有 Resources 资源）不会进 `PanelKeys`，也不会产生 `Resources` 警告。
- 命令行（CI / batchmode）：

```powershell
& "<Unity编辑器路径>\Unity.exe" -batchmode -nographics -projectPath <工程路径> `
  -executeMethod CandyUI.PanelValidator.Run -quit -logFile "$env:TEMP\validate.log"
# 结果抓这一行：
# [CandyUI] 面板校验完成：错误 0 条，警告 N 条
```

---

## 12. 已知限制与后续可做（接手时先看这里）

### 12.1 当前限制（都是"知道就行"，不是 bug）

> 阶段3（2026-11）已补上关闭退场动画、遮罩淡入淡出、窗口切换/弹窗队列等 Out（见 6.4 / 6.5 / 6.8、12.4）；关闭流程的三个竞态（清缓存 vs 关闭动画、Closing 重复关闭、关窗期间开新窗口）修复见 12.5；阶段2 的 5 个时序/状态缺陷修复见 12.3。

1. **弹窗排队返回 null**：`OpenPopupAsync` 被排队时返回 `null`，调用方无法 `await` 到"真正显示"；可用 token 取消排队。
2. **同类型弹窗默认刷新/去重**：需要连弹时加 `allowDuplicatePopup: true`。
3. **弹窗禁止隐藏**：`TryHidePanel` 对弹窗会 Warn 拒绝（会破坏遮罩/队列一致性）；弹窗请用 Close。
4. **没有控件库**：格子（ItemSlot）、循环列表（LoopList）、通用 Tip 都还没做，示例 Demo 里的格子是手写的；这些属于业务零件，计划放在宿主工程自己的业务目录（不放进框架包）。
5. **没有完整的代码生成/绑定工具**：已有 `PanelKeys` 地址常量生成与编辑器期面板校验（见 §11.6），但没有 QFramework 那种 Inspector 一键生成 Panel/Designer 代码；Resources 之外（YooAsset/Addressables）的资源存在性校验仍需手动确认。
6. **测试覆盖**：本包 EditMode 61 条（状态流转、窗口栈、弹窗队列/优先级、AssetCache 引用计数、并发取消、缺陷回归、关闭竞态、面板自带 Canvas、根节点配置、实例门面、多套上下文隔离、默认上下文守卫、同类型多实例）；示例工程另有 PlayMode 冒烟 12 条（真实 Resources 加载、真实 Destroy、DDOL、转场协程、TMP 控件创建，其中 4 条走 YooAsset 编辑器模拟包验证适配器链路）。跑法见第 11.5 节。仍未覆盖：真实 AssetBundle（打出来的包 + 联机模式）、真机/多分辨率、长时间运行的内存与句柄回收。
7. **示例 Demo 资源仍在 Resources**（示例工程 MakeUiFrame 内）：为了 Resources 回退；正式项目应移出。
8. **只支持新 Input System**：老输入项目要切换设置。
9. **没有虚拟列表/合批优化**：大量格子/长列表会有性能问题，需要后续补。
10. **关闭动画期不可强制打断（但缓存语义已确定）**：已经受理的关闭会播完退场再生效，不会被中途打断；但若这段动画期间调用了 `ClearPanelCache` / `ClearPanelCache<T>` / `CloseAll` / `Reset`，正在关闭的 KeepInstance 实例会在动画结束后直接销毁、不再入缓存，保证"缓存已清空"之后不会复活（见 §12.5）。
11. **面板独立 Canvas 需手动配置**：框架不主动加 Canvas、不校验、不排序；Prefab 根带 Canvas 即视为独立 Canvas（见 §6.9），漏配 `GraphicRaycaster`、乱改 `overrideSorting` / `sortingOrder` 等配置问题由业务自己负责。
12. **多套 UI 上下文已支持，但有边界**：`UIFramework.Create(config, loader)` 能创建完全独立的上下文（各自根节点 / 三个服务 / AssetCache，打开表、窗口栈、弹窗队列、缓存引用计数互不共享，见 §6.11）。已知限制：`EventSystem` 仍全局唯一（是否 `DontDestroyOnLoad` 跟随该上下文的配置）；**默认上下文禁止 `Dispose()`**（抛 `InvalidOperationException`，静态 API 依赖它且无法重建）；`Create` 出来的上下文必须显式 `Dispose`，没有自动回收。
13. **同类型多实例的限制**：弹窗不支持多实例（仍按队列一次显示一个，需要连弹用 `allowDuplicatePopup`）；窗口栈的调试行仍只显示类型名，同类型多个窗口会显示成 `X -> X` 不便于区分；`OpenPlainPanelAsync<T>(null)` 这种单参 `null` 字面量写法会与实例名重载产生 CS0121 歧义（显式写 `data: null` 或 `(UIPanelData)null` 即可）。

### 12.2 建议的后续优先级

| 优先级 | 事项 |
|---|---|
| P2 | 业务零件：ItemSlot / LoopList（简单版 + 循环版）/ UITip / 红点树 |
| P3 | 编辑器绑定代码生成（地址常量与校验已做，见 §11.6；缺 Inspector 一键生成 Panel/Designer 代码） |
| P3 | 3D UI（ScreenSpaceCamera）支持、每层独立 Canvas |
| 已完成 | M5 交付化：UPM `package.json` + 独立 Editor asmdef + 包 README/CHANGELOG（2026-11） |
| 随示例维护 | PlayMode 测试（真实销毁链路、`DontDestroyOnLoad`、转场协程）— 已随示例移到 MakeUiFrame |

### 12.3 阶段2 已修复（原时序/状态缺陷，保留记录）

| 缺陷 | 触发条件 | 修法 |
|---|---|---|
| 弹窗双路推进 | 弹窗加载期间调 `CloseAllPopups()` / `Reset()` 使其失效，且期间又有新弹窗排队 | `ShowCoreAsync` 失效分支只抛异常、不再自己 `ShowNext()`；推进由外层各执行一次：队列路径由 `ShowNext` 的 catch，首弹直连路径由 `OpenPopupAsync` 的 catch |
| 关非栈顶窗口重跑 OnShow | `UIManager.Close(panel)` / `CloseWindow` 关掉被压在栈里的窗口 | `CloseWindow` 在 `ClosePanel` 前记录是否为栈顶；只有栈顶才恢复下一个窗口，非栈顶只出栈 |
| 并发等待者被连带取消 | 两个调用方同时打开同一面板，第一个的 `CancellationToken` 取消 | 共享加载 Task 用 `CancellationToken.None` 创建；各等待方用 `TaskWait.WithCancellation` 只取消自己的等待 |
| 显示异常后状态恢复不精确 | `OnShow` / `Presenter.OnShow` 抛异常 | `InternalShow` 恢复到进入前的稳定状态（Created / Hidden / Visible），不再一律回 `Created`；非 Visible 时 `SetActive(false)` |
| KeepInstance 面板没有休眠钩子 | 没有 Presenter 的 KeepInstance 面板关闭进缓存后，全局事件订阅没有通知点 | `UIPanel` 新增 `protected virtual OnSleep()/OnResume()`；`InternalSleep/InternalResume` 先调面板钩子再转发 Presenter |

以上 5 条已不在"当前限制"里；12.1 / 12.2 的其余条目仍然有效。

### 12.4 阶段3 已交付（关闭生命周期闭环）

- `UIPanelState` 末尾新增 `Closing`：关闭时先进入"正在关闭"播退场，播完落到 `Closed`（销毁）或 `Hidden`（KeepInstance 入缓存）。
- `UIPanel` 新增 `InternalCloseAsync(reason, animateOut, destroyInstance)`、`WaitForCloseAsync()`、`HideAsync()` 与 `OnClosed()` 钩子；重复 `Close` 幂等，不会重播动画。关闭的退场动画使用独立 CTS，不受"重新 Show 时取消隐藏动画"影响。
- `PanelService` 新增 `ClosePanelAsync` / `WaitCloseAsync` / `ClosePanelImmediate`；关闭编排抽到新文件 `PanelCloser`：受理后立即从打开表移除，后台播退场，再 `OnClose` + 销毁 + Release，或 `OnSleep` + 入缓存；若期间同类型已被重新打开，则丢弃旧实例并 Warn（防止缓存被旧实例覆盖）。
- `UIManager` 新增 `CloseAsync(panel, reason)` 与 `WaitForCloseAsync(panel)`；`Close` 保持 void（发起关闭）。
- 窗口切换/关窗恢复上一个、弹窗关闭后推进队列，都等退场动画播完；遮罩改为 0.12s 淡入淡出（淡出开始即视为隐藏）。
- 强制关闭路径（`CloseAll` / `ClearPanelCache` / `Reset` / 打开失败回滚）不播动画，同步完成，保证切场景不等动画。

### 12.5 阶段3 补充修复：关闭流程三个竞态（2026-11）

| 缺陷 | 触发条件 | 修法 |
|---|---|---|
| 清缓存后 KeepInstance 实例"复活" | KeepInstance 面板正在播关闭动画（`Closing`）时调 `ClearPanelCache()` / `ClearPanelCache<T>()` / `CloseAll()`，动画结束后实例又被写回缓存 | `PanelService` 新增缓存代号 `_cacheGeneration`（与管打开流程的 `_generation` 分开），清缓存时 +1；关闭受理时把当时代号传给 `PanelCloser`，动画结束写回缓存前比对，代号变了就改走销毁路径（`Destroy` + 句柄 Release）并打 Info，不会重复销毁 |
| 对"正在关闭"的面板 await 关闭返回 false | 面板已进入 `Closing`、从打开表移出后再调 `Close` / `await CloseAsync`，查不到注册项返回 false + Warn | `ClosePanel` 对 `Closing` 直接返回 true（已受理，不重复发起关闭、不打 Warn）；`ClosePanelAsync` 对 `Closing` 改走 `WaitCloseAsync` 等本次关闭结束再返回 true |
| 关窗动画期间开新窗口，栈层叠错乱 | 关 C 的退场动画未播完时打开 D，随后 C 的恢复流程把 B 重新 show，出现两个窗口同时可见 | `WindowService` 记录 `_pendingClose`，`OpenWindowAsync` 压栈前先等它结束；恢复以**实时** `_stack.Peek()` 为准，且栈顶已 `Visible` 时不重复 show；关非栈顶窗口仍不恢复任何窗口 |

对应用例：`Tests/CloseRaceTests.cs` 的 3 条（清缓存销毁、Closing 等待返回 true、关窗期间打开新窗口的栈序），连同既有 33 条共 36 条 EditMode 用例。

---

## 13. 目录名与命名空间为什么不一样

- 包名 / 目录 / 程序集用 **CandyUIFramework**：包名 `com.cherryxxh.candyui`、包内目录 `Runtime/`/`Editor/`/`Tests/`、程序集 `CandyUIFramework.Runtime`/`CandyUIFramework.Editor`/`CandyUIFramework.Tests`；示例里的资源地址前缀仍是 `CandyUI/Demo/...`。
- C# 命名空间用 **CandyUI**（`namespace CandyUI;`）：它是"产品名"，日常写代码 `using CandyUI;` 更短；目录名带 `Framework` 是为了在 Assets 里和业务目录区分。
- 日志统一前缀 `[CandyUI]`，日志类 `CandyUILog`；Editor 菜单统一前缀 `CandyUI / ...`。

历史说明：工程里曾有一份逐字复制的 `Assets/CandyUI`（命名空间也是 `CandyUI`，程序集 `CandyUI.Runtime`），两份代码长期双份维护、每次改都要镜像同步。已删除并合并为一份（提交 `0638346`），此后的唯一实现就是从主工程拆出的这个包仓库。

---

## 14. 接手检查清单

1. [ ] 在示例工程 MakeUiFrame（或你的宿主工程）里等编译完成，Console 无红字；跑一次示例 Demo（菜单 1 → 菜单 2 → Play）。
2. [ ] 打开关系图 `CandyUIFramework框架关系图.prg`，对照第 5 节架构。
3. [ ] 按顺序读代码：`UIManager.cs`（静态薄转发）→ `UIFramework.cs`（实例门面/真正实现）→ `PanelService.cs` → `PanelCloser.cs` → `UIPanel.cs` → `IAssetLoader.cs` → `WindowService/PopupService.cs`。每个文件问三个问题：负责什么？依赖谁？不知道什么？
4. [ ] 跟一遍示例 Demo：主城 → 设置 → 确认框 → 返回；背包切页签/使用；商店购买；奖励连弹；Toast。
5. [ ] 跑一次 Roslyn 校验（第 11.3 节），确认"不打开 Unity 也能提前查错"这条链路可用。
6. [ ] 跑一次 EditMode 测试（第 11.5 节），本包 61 条全绿。
7. [ ] 确认宿主工程导入了 TMP Essentials（示例 Demo 的文字是 TMP，缺 TMP Essentials 会白字）。
8. [ ] 按第 8.2 节自己新建一个界面（不要抄示例，自己写一遍）。
9. [ ] 从第 12.2 节挑一个方向做；改动前先 `git commit` 快照，改完再提交。

---

## 15. 常见问题（FAQ）

**Q：为什么 `UIManager` 是 static？**
A：全局唯一、不用挂载、业务随手可调。代价是静态状态要处理 Domain Reload——已用 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 的 `ResetStatics()` 清理。

**Q：为什么不用 DOTween 做默认转场？**
A：核心保持零第三方依赖，新项目不装 DOTween 也能用；DOTween 版本随示例放在 MakeUiFrame，想用就 `UIManager.DefaultTransition = new DOTweenTransition();`。

**Q：为什么 YooAsset 不直接进 Runtime？**
A：`CandyUIFramework.Runtime` 只依赖 `IAssetLoader` 接口；YooAsset 适配器在示例工程 MakeUiFrame 的独立 asmdef 里，不装 YooAsset 的项目照样能编译。

**Q：Prefab 地址到底怎么写？**
A：`[UIPanel("CandyUI/Demo/HomeWindow")]` 里的字符串就是资源系统 location。Resources 模式 = 去掉 `Resources/` 前缀和 `.prefab` 后缀；YooAsset 模式 = 收集器里地址规则算出来的字符串（Demo 里两者一致）。

**Q：KeepInstance 下为什么 `OnClose` 不触发？**
A：因为实例没销毁，只休眠。清理逻辑放 `OnHide`/`OnSleep`；真正销毁走 `ClearPanelCache`/`CloseAll`/换 loader。如果想感知"这次关闭流程结束"（含入缓存），重写 `OnClosed`。

**Q：关闭后想等退场动画播完再做下一步怎么办？**
A：`await UIManager.CloseAsync(panel)`（返回 `Task<bool>`，被 `CanClose` 拦截时返回 false 且不关），或者在别处 `await UIManager.WaitForCloseAsync(panel)`。`Close()` 仍是发完就返回；`CloseAll()` / 切场景不等动画。

**Q：`TryHidePanel` 返回 bool，什么时候用？**
A：HUD 类常驻界面想临时隐藏（比如打开背包时藏起主城 HUD）。窗口用 `NavigateBack` / `CloseCurrentWindow`，弹窗用 `CloseTopPopup`，不要在弹窗上 `TryHidePanel`。

**Q：弹窗想"连续弹三次奖励"怎么办？**
A：在弹窗类特性上加 `allowDuplicatePopup: true`；否则同类型只刷新第一个。

**Q：加载失败会怎样？**
A：`IAssetLoader` 抛异常 → `OpenPlainPanelAsync` 把异常抛给调用方（`await` 处可 catch），框架会清掉"加载中"记录并释放句柄，可以直接重试。

---

## 16. 附录：文件职责速查（Runtime / Editor）

本表路径基准：`Runtime/`、`Editor/`、`Tests/` 下的文件都相对包根（本仓库根目录）。

| 文件 | 一句话职责 |
|---|---|
| `Core/UILayer.cs` | 层枚举与排序 |
| `Core/UIPanelState.cs` | 面板状态机 |
| `Core/UIPanelData.cs` | 打开参数基类 |
| `Core/UIPanelAttribute.cs` | 类上声明地址/层/缓存策略/遮罩/关闭行为 |
| `Core/Generated/PanelKeys.g.cs` | 自动生成的面板地址常量（勿手改，菜单 `CandyUI/生成/面板地址常量` 重新生成） |
| `Core/UIPanelInfo.cs` | 已打开面板的内部档案（类型/地址/层/实例/句柄/实例键/打开序号/是否自带 Canvas） |
| `Core/PanelKey.cs` | 面板实例键 `(类型, 实例名)`：打开表/缓存表/在途表的唯一索引，null 实例名 = 默认实例 |
| `Core/PanelCloseBehavior.cs` | Destroy / KeepInstance |
| `Core/PanelCloseReason.cs` | UserClose / ForceClose |
| `Panel/UIPanel.cs` | 界面基类：生命周期、状态、转场、Presenter、睡眠/唤醒钩子、关闭拦截 |
| `Panel/PresenterBase.cs` | MVP Presenter 基类（OnBind/OnShow/OnHide/OnSleep/OnResume/OnClose） |
| `Management/UIManager.cs` | 面向默认上下文的静态薄壳：三个静态服务 + 全部 public API 一行转发到 `UIFramework.Default` |
| `Management/UIFramework.cs` | 实例门面 + 上下文创建：真正持有三个服务，打开/关闭/路由/查询/缓存/调试的实现；`Create` 建独立上下文（`UIManager` 转发到它的 `Default`） |
| `Management/Internal/PanelService.cs` | 面板注册表 + 对外编排：打开/关闭/缓存/查询/预加载（约 580 行，纯代码 393） |
| `Management/Internal/PanelCloser.cs` | 关闭编排：播退场 → OnClose + 销毁 + Release，或 OnSleep + KeepInstance 入缓存（约 160 行） |
| `Management/Internal/PanelOpener.cs` | 在途打开：去重、等待者计数、最后一个等待者取消后的废弃收尾（约 230 行） |
| `Management/Internal/PanelFactory.cs` | 地址 → 场景实例：加载/校验/根 Canvas 检测/实例化/转场/预加载/销毁（约 220 行） |
| `Management/Internal/PanelOpenOperation.cs` | 一次在途打开的共享状态：实例键、Task、等待者计数、废弃标记（约 60 行） |
| `Management/Internal/PanelRegistry.cs` | 打开表/缓存表的按类型遍历辅助：按打开顺序取同类型全部实例、按类型摘缓存（约 60 行） |
| `Management/Internal/WindowService.cs` | 窗口栈、NavigateBack 恢复 |
| `Management/Internal/PopupService.cs` | 弹窗队列、优先级、遮罩、点击关闭 |
| `Layers/WindowStack.cs` | 窗口栈（List 实现，支持任意移除） |
| `Layers/PopupQueue.cs` | 弹窗队列（优先级插入、去重、token 取消） |
| `Layers/PopupMask.cs` | 半透明遮罩（0.12s 淡入淡出、挡住点击、可点击关闭） |
| `Transition/IPanelTransition.cs` | 转场接口 |
| `Transition/FadeScaleTransition.cs` | 默认淡入+缩放（纯协程） |
| `Loading/IAssetLoader.cs` | 资源加载接口 |
| `Loading/IAssetHandle.cs` | 资源句柄接口 |
| `Loading/AssetCache.cs` | 地址→句柄缓存、引用计数、KeepAlive |
| `Loading/AssetCachePolicy.cs` | ReleaseOnClose / KeepAlive |
| `Loading/ResourcesAssetLoader.cs` | 默认 Resources 实现 |
| `Infrastructure/UIRoot.cs` | 自动建 Canvas/CanvasScaler/EventSystem/层容器（参数来自 `UIRootConfig`） |
| `Infrastructure/UIRootConfig.cs` | 根节点配置结构体：渲染模式/相机/参考分辨率/匹配权重/排序/常驻 + 默认值与校验回退 |
| `Infrastructure/CandyUILog.cs` | 统一日志（`[CandyUI]` 前缀 + 开关） |
| `Infrastructure/TaskWait.cs` | 共享加载 Task 的"可取消等待"（只取消等待，不取消共享加载） |
| `Editor/CandyUIFramework.Editor.asmdef` | 编辑器程序集定义（Editor-only，引用 `CandyUIFramework.Runtime`） |
| `Tests/CandyUIFramework.Tests.asmdef` | EditMode 测试程序集（Editor-only、nunit；需宿主工程 `testables` 才编译运行） |
| `Editor/PanelKeyGenerator.cs` | 扫描 `[UIPanel]` 生成 `PanelKeys.g.cs`；域重载自动检查、内容不变不写文件 |
| `Editor/PanelValidator.cs` | 面板引用校验（继承/地址/重名/Resources Prefab/根组件/命名冲突）；菜单与命令行入口 |

> 示例工程 MakeUiFrame 额外包含 `Assets/CandyUI.YooAsset`（YooAsset 适配器）、Demo 脚本/场景与 PlayMode 测试，均不在本包；`Editor/PanelKeyGenerator.cs` 现在还会按包路径解析生成目录、只读 PackageCache 时跳过写入。

---

*本文档随代码维护；接口/行为改动时同步更新对应章节。*
