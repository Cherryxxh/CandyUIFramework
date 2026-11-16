# CandyUI Framework

面向 Unity（uGUI）的特性驱动 UI 框架：用 `[UIPanel]` 声明界面、异步打开/关闭、可插拔资源加载、窗口栈与弹窗队列，带编辑器期校验与 EditMode 自动化测试。

- UPM 包名：`com.cherryxxh.candyui`，C# 命名空间：`CandyUI`
- 包版本：1.0.0（见 [CHANGELOG.md](CHANGELOG.md)）
- 本包 = 框架代码 + YooAsset 适配器（`Extras/YooAsset`）；Demo 场景、Demo 脚本、DOTween 转场与 PlayMode 测试在示例工程 **MakeUiFrame** 中
- 交接文档：正文在示例工程仓库 [`MakeUiFrame/docs/CandyUIFramework_交接文档.md`](https://github.com/Cherryxxh/MakeUiFrame/blob/main/docs/CandyUIFramework_%E4%BA%A4%E6%8E%A5%E6%96%87%E6%A1%A3.md)（架构、核心机制、完整 API、FAQ、已知限制）；本包 `Documents/` 只留镜像说明

## 特性

| 能力 | 说明 |
| --- | --- |
| 声明式注册 | 界面类打 `[UIPanel("地址", UILayer.Window)]`，不需要注册表、不用往场景里挂空物体 |
| 分层 | 7 层（Background/Main/Window/Mask/Popup/Toast/Top），自动建根节点、Canvas、EventSystem |
| 异步生命周期 | `OpenXxxAsync` 返回 `Task<T>`；关闭可 `await`，支持转场动画播完再销毁 |
| 转场可插拔 | `IPanelTransition`；内置零依赖 `FadeScaleTransition`，可自行实现（示例工程的 DOTween 版本即一个实现） |
| 窗口栈 | 打开即入栈、`NavigateBack()` 返回上一个窗口，`CanNavigateBack` 可判断 |
| 弹窗队列 | 优先级排队、同类型去重、`allowDuplicatePopup` 连弹、遮罩点击关闭 |
| 资源缓存 | `IAssetLoader` 可插拔（Resources / YooAsset / 自定义），引用计数 + KeepAlive，取消不泄漏 |
| MVP 可选 | `PresenterBase`：View 只管控件，Presenter 管数据与事件，可完全不用 |
| 多实例 | 同一面板类型可用实例名并存：`OpenPlainPanelAsync<ShopPanel>("npc_smith")` |
| 多套上下文 | `UIFramework.Create(config, loader)` 一套服务 + 一个根节点，互不干扰 |
| 根节点可配置 | `UIRootConfig`：渲染模式、相机、参考分辨率、排序、常驻、是否自动建 EventSystem |
| 编辑器期校验 | 生成 `PanelKeys` 地址常量 + 菜单校验（继承/空地址/重复地址/Prefab 缺失/命名冲突） |
| 可观测 | 统一 `[CandyUI]` 前缀日志 + `GetDebugSnapshot()` 三行状态 |

## 环境要求

- Unity **2022.3 及以上**（开发与测试使用 2022.3.62f3）
- 依赖包（`package.json` 已声明，Package Manager 会自动安装）：
  - `com.unity.ugui` 1.0.0
  - `com.unity.textmeshpro` 3.0.7
  - `com.unity.inputsystem` 1.7.0（Runtime 程序集直接引用 `Unity.InputSystem`，必装）
- 只支持**新版 Input System**：Player Settings → Active Input Handling 选 `Input System Package (New)`；`UIRoot` 会自动创建 `InputSystemUIInputModule`
- YooAsset 3.0.7（本包依赖，装包前先按 §安装 配置 OpenUPM 源；适配器源码在 `Extras/YooAsset`）
- 不依赖 DOTween：内置转场为纯协程实现

## 安装

本仓库根目录就是 UPM 包（`package.json` 在根上），按需选一种方式：

### 前置：配置 OpenUPM 源（必须）

本包依赖 YooAsset 3.0.7（适配器 `Extras/YooAsset` 直接引用其程序集），而 YooAsset 发布在 OpenUPM 上，宿主工程必须先加 scoped registry，否则解析依赖会失败：

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.tuyoogame.yooasset"]
    }
  ]
}
```

### 方式一：Git URL（发布后推荐）

在宿主工程的 `Packages/manifest.json` 里加：

```json
{
  "dependencies": {
    "com.cherryxxh.candyui": "https://github.com/Cherryxxh/CandyUIFramework.git#v1.1.0"
  }
}
```

或用 Package Manager 窗口：`+` → **Add package from git URL…** 粘贴上面的 URL。

### 方式二：本地 `file:` 包（开发/需要改包时）

```json
{
  "dependencies": {
    "com.cherryxxh.candyui": "file:D:/unity guoji/unity project guoji/CandyUIFramework"
  }
}
```

代码改动立即生效，适合边改框架边在宿主工程里验证。**需要重新生成 `PanelKeys.g.cs` 时请用这种方式**（见“编辑器工具”）。

### 方式三：嵌入式包（把包代码提交进宿主工程）

把本仓库整个目录拷到宿主工程的 `Packages/com.cherryxxh.candyui/` 下，随宿主工程一起提交，改包不需要回到原仓库。

> 从 Git URL 安装的包会被放在 `Library/PackageCache`（只读），编辑器工具不会（也不能）修改包内文件；此时 `PanelKeys` 以包内已提交的版本为准。

## 快速开始

### 1. 初始化资源加载器

框架不绑定资源系统，先装一个 `IAssetLoader`：

```csharp
using CandyUI;
using UnityEngine;

public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        // Resources（原型/小项目）：面板 Prefab 放在 Resources/<[UIPanel] 地址> 下
        UIManager.InitializeAssetLoader(new ResourcesAssetLoader());

        // 自定义：实现 IAssetLoader / IAssetHandle 后替换（YooAsset、Addressables 等）
        // UIManager.InitializeAssetLoader(new MyAssetLoader());
    }
}
```

### 2. 配置默认转场（可选）

不配置时打开/关闭都是瞬切。

```csharp
UIManager.DefaultTransition = new FadeScaleTransition();   // 内置，零依赖
```

### 3. 写一个面板

```csharp
using CandyUI;

[UIPanel("CandyUI/HelloPanel", UILayer.Main)]
public sealed class HelloPanel : UIPanel
{
    protected override void OnCreate() { /* 建控件，只执行一次 */ }
    protected override void OnShow(UIPanelData data) { /* 每次显示都刷新 */ }
    protected override void OnClose() { /* 被销毁前 */ }
    protected override void OnClosed() { /* 关闭流程结束（退场播完） */ }
}
```

资源地址与 `[UIPanel]` 的第一个参数一致：用 `ResourcesAssetLoader` 时 Prefab 放 `Resources/CandyUI/HelloPanel.prefab`；换成 YooAsset 等加载器时地址就是对应的 location。打开面板前记得先执行一次菜单 `CandyUI/生成/面板地址常量`，业务代码即可用 `PanelKeys.HelloPanel` 代替裸字符串。

### 4. 打开与关闭

```csharp
var panel = await UIManager.OpenPlainPanelAsync<HelloPanel>();      // 普通面板
var win   = await UIManager.OpenWindowAsync<HomeWindow>();          // 窗口（入栈）
var popup = await UIManager.OpenPopupAsync<ConfirmPopup>();         // 弹窗（排队 + 遮罩）

UIManager.Close(panel);              // 发起关闭，内部异步播退场
await UIManager.CloseAsync(panel);   // 等关闭完成（含转场、销毁/入缓存）
```

## API 速查

初始化与配置

| API | 说明 |
| --- | --- |
| `UIManager.InitializeAssetLoader(IAssetLoader)` | 设置资源加载器（默认上下文） |
| `UIManager.AssetLoader` / `IsLoaderReady` | 当前加载器 / 是否已就绪 |
| `UIManager.DefaultTransition` | 默认转场，`null` 表示瞬切 |
| `UIManager.DebugLogEnabled` | 是否输出框架日志（统一 `[CandyUI]` 前缀） |
| `UIFramework.Create(config, loader)` | 独立上下文（独立服务 + 独立根节点，需显式 `Dispose`） |

打开

| API | 说明 |
| --- | --- |
| `OpenPlainPanelAsync<T>(data = null, ct = default)` | 打开普通面板（不参与窗口栈） |
| `OpenPlainPanelAsync<T>(instanceName, data = null, ct = default)` | 按实例名打开（同类型多实例） |
| `OpenWindowAsync<T>(...)` | 打开窗口：隐藏上一个、入栈 |
| `OpenPopupAsync<T>(...)` | 打开弹窗：显示遮罩、入队；有同类在显示时复用，有其它弹窗在显示时返回 `null` 入队 |
| `PreloadAsync<T>(ct = default)` | 预加载（常驻缓存，不显示） |

> 注意：`OpenPlainPanelAsync<T>(null)` 这种单个 `null` 字面量会同时匹配 `UIPanelData` 与 `string` 重载（CS0121）；请写 `data: null` 或 `(UIPanelData)null`。

关闭

| API | 说明 |
| --- | --- |
| `Close(panel, reason = UserClose)` | 发起关闭（异步播退场，立即返回） |
| `CloseAsync(panel, reason)` / `WaitForCloseAsync(panel)` | 等关闭完成 / 只等流程结束 |
| `CloseOfType<T>(reason)` | 关闭该类型全部实例 |
| `CloseCurrentWindow()` / `NavigateBack()` | 关当前窗口 / 返回上一个窗口 |
| `CloseTopPopup()` / `ClosePopup(p)` / `CloseAllPopups()` | 弹窗相关 |
| `CloseAll()` | 全关（强制路径，无转场） |

显隐与查询

| API | 说明 |
| --- | --- |
| `TryHidePanel<T>(instanceName?)` / `TryShowPanel<T>(instanceName?, data?)` | 隐藏/重新显示（返回 `bool`） |
| `GetOpenedPanel<T>(instanceName?)` / `GetOpenedPanels<T>()` | 取已打开实例 |
| `HasOpenedPanel<T>(instanceName?)` / `IsPanelVisible<T>(instanceName?)` | 状态查询 |
| `CanNavigateBack` / `GetDebugSnapshot()` | 能否返回 / 调试快照 |
| `ClearPanelCache()` / `ClearPanelCache<T>()` | 清 KeepInstance 缓存 |

`[UIPanel]` 参数

```csharp
[UIPanel(
    prefabAddress,                        // 资源地址（必填）
    layer = UILayer.Main,                 // 层
    cachePolicy = AssetCachePolicy.ReleaseOnClose,
    dismissOnMaskClick = false,           // 点遮罩关闭（弹窗）
    maskAlpha = 0.6f,                     // 遮罩透明度
    popupPriority = 0,                    // 队列优先级，越大越先
    closeBehavior = PanelCloseBehavior.Destroy, // 或 KeepInstance（隐藏并缓存）
    allowDuplicatePopup = false)]         // 同类型弹窗是否允许连弹
```

## 编辑器工具

| 菜单 | 作用 |
| --- | --- |
| `CandyUI/生成/面板地址常量` | 扫描所有 `[UIPanel]`，生成 `Runtime/Core/Generated/PanelKeys.g.cs`（地址拼错编译期报错） |
| `CandyUI/校验/面板引用` | 检查继承、空地址、重复地址、Prefab 缺失、根节点缺组件、命名冲突 |

- 生成器按**包路径**定位生成目录（`UnityEditor.PackageManager.PackageInfo.FindForAssembly`），兜底兼容“框架被拷进 `Assets/`”的旧用法；从 `Library/PackageCache` 只读安装时不会写文件，而是打一条 `[CandyUI]` 警告并跳过——需要更新常量就换 `file:` 或嵌入式安装后重新生成。
- 生成文件固定 UTF-8 无 BOM、4 空格缩进、LF 换行，同样输入重复生成字节一致；域重载后自动检查，内容没变不写文件。生成文件要随包提交。
- 校验菜单在编辑器内弹窗汇总，batchmode 下只打日志。命令行入口：`-executeMethod CandyUI.PanelValidator.Run`。

## 目录结构

```
com.cherryxxh.candyui/
├── package.json
├── README.md / CHANGELOG.md
├── Runtime/                     # CandyUIFramework.Runtime（核心，零第三方依赖）
│   ├── Core/                    # 数据/契约/特性/枚举 + Generated/PanelKeys.g.cs
│   ├── Panel/                   # UIPanel 基类、PresenterBase
│   ├── Management/              # UIManager 门面、UIFramework 实例、Internal 服务
│   ├── Layers/                  # 窗口栈、弹窗队列、遮罩
│   ├── Loading/                 # IAssetLoader / IAssetHandle / AssetCache / Resources 实现
│   ├── Transition/              # IPanelTransition + FadeScaleTransition
│   └── Infrastructure/          # UIRoot、UIRootConfig、日志
├── Editor/                      # CandyUIFramework.Editor（Editor-only）：地址生成器、引用校验
├── Extras/YooAsset/             # CandyUIFramework.YooAsset（可选程序集）：YooAssetLoader / YooAssetHandle
├── Tests/                       # CandyUIFramework.Tests（EditMode，nunit）
└── Documents/                   # 交接文档镜像说明（正文在示例工程）
```

架构依赖单向：`UIManager`（静态门面）→ `UIFramework`（实例）→ `PanelService` / `WindowService` / `PopupService` → `PanelOpener` + `PanelFactory` + `PanelCloser` → `AssetCache` → `IAssetLoader`。

## 测试

本包提供 EditMode 测试（`Tests/`，程序集 `CandyUIFramework.Tests`）。测试程序集是 Editor-only 且 `autoReferenced: false`，必须显式开启才会编译运行——在宿主工程的 `Packages/manifest.json` 里加：

```json
{
  "testables": ["com.cherryxxh.candyui"]
}
```

然后在 Unity 的 **Window → General → Test Runner** 里跑 EditMode；或命令行：

```powershell
& "<Unity编辑器路径>\Unity.exe" -batchmode -nographics -projectPath <宿主工程路径> `
  -runTests -testPlatform EditMode -testResults "$env:TEMP\candy_editmode.xml"
```

覆盖范围与用例清单见交接文档 §11.5。PlayMode 冒烟测试依赖示例工程的真实 Prefab 与 Demo 场景，因此随示例移到 MakeUiFrame，不在本包内。

## 已知限制

- **弹窗排队返回 `null`**：已有弹窗在显示时，`OpenPopupAsync` 返回 `null` 只表示“已入队”，不是失败。
- **同类型弹窗默认复用当前实例**，不会开第二个；需要连弹请加 `allowDuplicatePopup: true`。
- **`CloseAll()` 是强制路径**，不播退场动画；需要动画请逐个 `Close`。
- **弹窗不支持多实例**（普通面板/窗口支持）。
- **框架不带控件库**：格子、循环列表、红点、通用 Tip 需自行实现。
- **面板自带 Canvas** 时框架不代管排序与 GraphicRaycaster，需要自行配置。
- 默认上下文禁止 `Dispose()`（静态 API 依赖它），独立生命周期请用 `UIFramework.Create`。
- 资源存在性校验只覆盖 `Resources`；YooAsset/Addressables 的 location 需要自行确认。
- 只支持新 Input System；老项目需先在 Player Settings 切换。

## 文档与示例

- 交接文档（示例工程仓库）：[docs/CandyUIFramework_交接文档.md](https://github.com/Cherryxxh/MakeUiFrame/blob/main/docs/CandyUIFramework_%E4%BA%A4%E6%8E%A5%E6%96%87%E6%A1%A3.md) —— 架构总览、核心机制、新建界面流程、YooAsset 接入、调试方法、FAQ、文件职责表。
- 示例：Demo 场景、Demo 面板、DOTween 转场与 PlayMode 测试见示例工程 **MakeUiFrame**。

## 版本

当前版本 1.0.0，变更记录见 [CHANGELOG.md](CHANGELOG.md)。
