# Phi Agent Instructions

Phi 是一个 C# 版本的 基于 pi agent 架构的最小 coding agent，目标是从头到尾自己实现一个 coding agent，方便我理解开发中的每个细节

## 架构

### 整体架构和依赖关系

- PhiProvider: 手写的 llm provider，对应 tau_ai 目录，我不喜欢 tau_ai 这个名字，因为看不出来是负责跟llm provider通信的
- PhiAgent: 对应 tau_agent 目录，自己实现的一个 harness, 状态管理，循环
- PhiCoding: Library，提供 UI-agnostic runtime（sessions / providers / tools / prompts / status router / slash commands / tool descriptors / chat projector）。无 UI 框架依赖。
- PhiCoding.Tui: 基于 XenoAtom.Terminal.UI 的终端界面 exe，引用 PhiCoding。
- PhiCoding.Avalonia: 基于 Avalonia 跨平台框架的桌面 UI 库，引用 PhiCoding。所有 UI（桌面 / 移动 / browser）共享同一份控件代码。
- PhiCoding.Avalonia.Desktop: Avalonia 的桌面平台入口 exe（classic desktop lifetime），引用 PhiCoding.Avalonia。

UI 框架选择：**Avalonia**（跨平台，支持 Windows / macOS / Linux / 移动 / browser），后续的桌面 UI
开发统一在 `PhiCoding.Avalonia/` 推进。`PhiCoding.Avalonia/` 输出的控件树通过不同的 platform
host 复用（`PhiCoding.Avalonia.Desktop` 是 Windows / macOS / Linux 桌面入口）。

依赖关系：

```
                PhiCoding.Tui ───┐
                                  ├─► PhiCoding ─► PhiProvider ─► PhiAgent
PhiCoding.Avalonia ──────────────┘
        ▲
PhiCoding.Avalonia.Desktop (exe)
```

`PhiAgent` 是最底层的 package，依赖最少，可以注入不同的 provider 使用，可以随意分发。

### 应用层依赖关系

```
PhiTuiApp ─→ PromptInput ─┐
   (TUI 壳：     (输入组件：    │
    nav 触发     editor +      │
    整体换页)    slash 分发/   ├─→ ISession ─→ CodingSession ─→ Harness ─→ Provider
                对话框/        │    (接口，    (session +   (dispatch)  (LLM)
                skill 补全)    │    已水合的    harness +
              ↓                │    model)     provider + queue)
            ChatTranscript     │
            (对话 + 输入反馈) │
              ↓                │
            ChatHeader
            (chrome)
            PhiStatusBar
            (错误/上下文/模型)
              ↑
            SessionStatusRouter + ISessionStatusSink
            (UI-agnostic 错误分类 + 路由；TUI 在 PhiCoding.Tui.Components.StatusBarBinder 适配)

└─→ SessionNavigator ─→ CodingSessionFactory ─→ CodingSession
    (拥有当前 session     (组装 runtime:
     生命周期：cancel +    资源/tools/prompt/
     await + dispose；      harness)
     /new 走 NavigateToNewAsync、
     /sessions/:id 走 ResumeAsync(id))
```

Avalonia 端结构镜像：

```
PhiCoding.Avalonia.Desktop.Program
   └─► PhiAvaloniaApp (Avalonia Application)
          └─► MainWindow ─→ ShellView (两栏 shell)
                              ├─ 左栏：NewChat + 会话列表（按 workspace/date 分组）+ Providers
                              └─► ContentControl (ViewHost)
                                    ├─ ChatPageView ─→ (header + transcript + prompt + status)
                                    └─ ProvidersPage
```

`TranscriptView` 订阅共享 `ChatTranscriptProjector`，按稳定 `ChatLine.Id` DIFF 渲染。
Avalonia 端走 `ISessionNavigator` → `SessionStatusRouter` + `ISessionStatusSink` 与 TUI 完全一致。

### TUI 渲染

PhiTuiApp 持有一个 `State<ISession>`，由 SessionNavigator 的 SessionChanged
事件在 UI 线程翻转。一个 ComputedVisual 读这个 State，navigate 时自动重建整页（header + transcript
+ editor + strip + status bar）。空 session 的内容槽显示一行 slogan，提交首条 prompt 后被 user bubble
自动替换——session 立刻有自己的 id，详情路径不再需要。

### 目录约定

PhiCoding 库下面分：

- `PhiCoding/Sessions/`：ISession、ISessionNavigator、SessionNavigator、CodingSessionFactory — 拥有 session 生命周期
- `PhiCoding/Providers/`：ProviderManager、ProviderCatalog、ICredentialStore、PhiSettings
- `PhiCoding/Tools/`、`PhiCoding/Prompts/`、`PhiCoding/Resources/`：runtime + skill/prompt 加载
- `PhiCoding/Slash/`：UI-agnostic slash 命令（SlashCommands、SlashCommandCatalog）
- `PhiCoding/Status/`：session → 状态条目的 routing（ISessionStatusSink、SessionStatusRouter、ErrorClassifier）
- `PhiCoding/Prompt/`：UI-agnostic 输入建议提供器（ISuggestionProvider、SuggestionItem、SlashCommandProvider、SkillSuggestionProvider）
- `PhiCoding/ToolCards/`：跨 UI 的 tool 元数据（ToolDescriptor、ToolDescriptors）
- `PhiCoding/Chat/`：UI-agnostic chat 投影（ChatLine DU、ChatTranscriptProjector）——两个 UI 都订阅 projector 的 `Changed`，按稳定 `ChatLine.Id` DIFF 渲染
- `PhiCoding/` 根：`ISession`、`SessionState`、`CodingSession`、`WorkspaceSessionStore`（扫 `{PHI_HOME}/sessions/*/index.jsonl` 合并所有工作区的会话）、compaction 等

PhiCoding.Tui exe 下分：

- `PhiCoding.Tui/`：应用壳 `PhiTuiApp`、基础设施（`SelectionCopyHost`、`SystemClipboard`、`ToastHostSentinel`）+ 入口 `Program.cs`
- `PhiCoding.Tui/Components/`：可复用积木——`PromptInput`（输入壳：editor + slash 分发 + 对话框 + skill 补全）、`ChatHeader`、`ChatTranscript`（订阅 projector 并按 `ChatLine.Id` DIFF 到 `DocumentFlow`）、`PhiStatusBar`、`SuggestionStrip`、`StatusBarBinder`（薄壳，调 `SessionStatusRouter` + 实现 `ISessionStatusSink`）、`SideBySideDiff`、`ToolCards/`（XenoAtom 实现）
- 命名空间：`PhiCoding.Tui.*`

PhiCoding.Avalonia 库下分：

- `PhiCoding.Avalonia/`：应用壳 `PhiAvaloniaApp`（Avalonia `Application`）+ `MainWindow` + `ShellView`（两栏 shell）+ `ChatPageView` + `NavModel`（纯导航模型）+ `AvaloniaTheme`（语义色 / FluentTheme 集成）+ 入口组件 + `DeskLog`
- `PhiCoding.Avalonia/Components/`：与 TUI 镜像的积木——`TranscriptView`（订阅 projector，按 `ChatLine.Id` DIFF 渲染到 `StackPanel`）、`PromptInputView`（editor + slash 分发 + 工作区选择器 + 模型 picker）、`ProvidersPage`（provider 连接 + API key 弹窗）、`ToolCards/`（Avalonia 实现）
- `PhiCoding.Avalonia/Controls/`：`EllipsisMenu` 等跨组件复用的小控件
- 命名空间：`PhiCoding.Avalonia.*`（含子命名空间 `PhiCoding.Avalonia.Components.*` / `PhiCoding.Avalonia.Controls.*`）

PhiCoding.Avalonia.Desktop exe 下分：

- `PhiCoding.Avalonia.Desktop/`：`Program.cs`（组合 provider manager / session factory / navigator，挂到 `PhiAvaloniaApp`，启动 classic desktop lifetime）
- 命名空间：`PhiCoding.Avalonia.Desktop.*`

### Avalonia Shell 布局

Avalonia shell 不是 TUI 的单页全屏聊天，而是两栏布局：

`PhiCoding.Avalonia.Desktop.Program` → `PhiAvaloniaApp` → `MainWindow` → `ShellView`
（左栏：New Chat + 会话列表 + footer 的 Providers 入口；右栏：
`ViewHost`（`ContentControl`）切换聊天页 / ProvidersPage）。

左栏构造：

- 顶部 New Chat 按钮 → `NavigateToNewAsync()`
- "By date" / "By workspace" 分组模式切换（icon-only，`AvaloniaTheme.Accent` 高亮）
- 会话 `ListBox`，按 `NavModel.GroupMode` 分组；每行 session 显示标题 + ⋯ 菜单（Rename / Delete），workspace 行显示工作区名 + ⋯ 菜单（New session / Delete workspace）
- 底部 Models / Providers footer 按钮

选中 session 触发 `ResumeAsync`，`SessionChanged` 时重建聊天页。Models / Providers 通过
`ViewHost.Content` 切换。

**重要**：导航（`NavigateToNewAsync` / `ResumeAsync`）不能在 `ListBox.SelectionChanged` 的
dispatch 内同步执行——它会触发 `SessionChanged` → `RebuildNavigation`，在 dispatch 中重入修改
ListBox 的 ItemsSource / SelectedIndex，导致右侧界面（编辑器）消失。
`ShellView.OnSessionSelection` 用 `postToUi`（`Dispatcher.UIThread.Post`）把选中处理推迟到当前事件之后。
`SessionChanged` 的重建走 `dispatchToUi`（`Dispatcher.UIThread.Post`）。

**跨工作区会话**：session 按 cwd 分目录存储（`{PHI_HOME}/sessions/{projectKey}/`）。
TUI 绑定进程 cwd，`/sessions` 只看当前目录；Avalonia 桌面不绑定进程 cwd，用
`WorkspaceSessionStore` 合并所有工作区的会话，左侧导航按工作区分组展示。
`SessionNavigator.ResumeAsync` 会解析会话记录自己的 `Cwd`（跨工作区 resume），
`NavigateToNewAsync(cwd)` 可指定新会话的工作目录。`PromptInputView`
在新建（未持久化）会话的编辑器上方显示工作区选择器（来自记录的 distinct cwd +
"Choose folder…"），第一条消息到达后隐藏。

## 开发工作流

- 在加新功能之前要添加测试用例
- 每次改完代码都要执行 `dotnet test` 确保所有测试通过

## C# 代码规范

- 使用 .NET 10 lts sdk 以及最新的开发规范
- 使用 CPM (Centered Package Management) 来管理依赖，版本定义在 `Directory.Packages.props`

## 参考代码

我将一些可能会用到的开源代码下载到本地了，如果需要了解设计和api用法可以去读对应代码

- XenoAtom.Terminal.UI: ~/github/XenoAtom.Terminal.UI
- tau: ~/github/tau
- Avalonia: ~/github/Avalonia（samples/ 有完整示例）

## 桌面 UI 差异（Avalonia vs TUI）

- Avalonia 有成熟的控件体系：Markdown 通过 `Markdown.Avalonia` 渲染，图标通过 `Material.Icons.Avalonia` 渲染，主题走 `FluentTheme`（light / dark 自动跟随系统）。
- 语义色统一在 `PhiCoding.Avalonia.AvaloniaTheme`（`TextSecondary` / `Danger` / `Success` / `Accent` / `ControlBorder` / `ContainerBackground` 等），没有 TUI ANSI 命名的色板。
- 两个 UI 共用 `ChatTranscriptProjector` 投影：各自按 `ChatLine.Id` DIFF 渲染（TUI→DocumentFlow，Avalonia→StackPanel）。