# Changelog

本包遵循语义化版本，版本号与 `package.json` 保持一致。

## [1.2.0] - 2026-11-16

- **改为可选依赖**：`package.json` 移除 `com.tuyoogame.yooasset`，装本包不再强制安装 YooAsset（宿主只需 ugui / TMP / InputSystem）。
- YooAsset 适配器移到 `Extras~/YooAsset/`：`~` 结尾的目录 Unity 不导入、不编译，需要时手动拷到工程 `Assets/` 下；文件与 GUID 保持不变，程序集名仍是 `CandyUIFramework.YooAsset`。
- README 安装章节改为「可选：接入 YooAsset」，新增手动拷贝步骤与两种接入方式。
## [1.1.0] - 2026-11-16

- 新增 `Extras/YooAsset`：把 YooAsset 3.0.7 适配器（`YooAssetLoader` / `YooAssetHandle`，程序集 `CandyUIFramework.YooAsset`）并入本包，安装后即可直接用 YooAsset 加载界面。
- `package.json` 增加依赖 `com.tuyoogame.yooasset: 3.0.7`；宿主工程需配置 OpenUPM scoped registry（README「前置：配置 OpenUPM 源」）。
- 示例工程 MakeUiFrame 不再各自维护一份适配器，改为直接使用包内版本。

## [1.0.0] - 2026-11-16

首次发布（从示例工程 MakeUiFrame 的框架目录独立为 UPM 包 `com.cherryxxh.candyui`）。

- 特性驱动的 uGUI 框架：`[UIPanel]` 声明界面，异步打开/关闭，7 层自动根节点（Canvas / EventSystem / 层容器）。
- 窗口栈（`NavigateBack`）、弹窗队列 + 遮罩（优先级 / 去重 / 连弹 / 点击遮罩关闭）。
- 生命周期闭环：入场/退场转场可插拔（内置零依赖 `FadeScaleTransition`）、KeepInstance 休眠/唤醒、关闭拦截、`Closing` 状态与关闭竞态处理。
- 异步加载抽象 `IAssetLoader` / `IAssetHandle`（默认 Resources 实现），框架级缓存 + 引用计数 + 取消不泄漏。
- MVP 可选基类（`UIPanel` / `PresenterBase` / Service），同类型多实例（实例名）、多套 UI 上下文（`UIFramework.Create`）。
- 编辑器工具独立程序集 `CandyUIFramework.Editor`：
  - `CandyUI/生成/面板地址常量`：扫描 `[UIPanel]` 生成 `PanelKeys.g.cs`，按包路径定位生成目录；从 `Library/PackageCache` 只读安装时跳过写入并告警。
  - `CandyUI/校验/面板引用`：继承 / 地址唯一性 / Prefab 与根组件 / 常量命名冲突校验，支持 batchmode。
- EditMode 测试程序集 `CandyUIFramework.Tests`（nunit，Editor-only，需宿主工程 `testables` 才会编译运行）。
- 移除示例与可选适配器（`Samples/`、`Extras/YooAsset/`、`Tests/PlayMode/`），改由示例工程 MakeUiFrame 承载；框架 Runtime 保持对 YooAsset / DOTween 零依赖。
