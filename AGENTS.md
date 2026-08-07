# Phi Agent Instructions

Phi 是一个 C# 版本的 基于 pi agent 架构的最小 coding agent，目标是从头到尾自己实现一个 coding agent，方便我理解开发中的每个细节

## 架构

### 整体架构和依赖关系

- PhiProvider: 手写的 llm provider，对应 tau_ai 目录，我不喜欢 tau_ai 这个名字，因为看不出来是负责跟llm provider通信的
- PhiAgent: 对应 tau_agent 目录，自己实现的一个 harness, 状态管理，循环
- PhiCoding: Library，提供 UI-agnostic runtime（sessions / providers / tools / prompts / status router / slash commands / tool descriptors / chat projector）。无 UI 框架依赖。
- PhiCoding.Tui: 基于 XenoAtom.Terminal.UI 的终端界面 exe，引用 PhiCoding。
- PhiCoding.Desk: 基于 MewUI (Aprillz.MewUI) 的桌面界面 exe，引用 PhiCoding。

依赖关系：

PhiCoding.Tui ──► PhiCoding ◄── PhiCoding.Desk
PhiCoding ──► PhiProvider
PhiCoding ──► PhiAgent
PhiProvider ──► PhiAgent
PhiAgent 是最底层的 package，依赖最少，可以注入不同的 provider 使用，可以随意分发

### 应用层依赖关系

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

Desk 端结构镜像：PhiDeskApp ─→ DeskChatPage ─→ (ChatHeaderView + TranscriptView +
PromptInputView + StatusBarView)，同样走 ISessionNavigator → SessionStatusRouter +
ISessionStatusSink，TranscriptView 订阅共享 ChatTranscriptProjector。

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
- `PhiCoding/` 根：`ISession`、`SessionState`、`CodingSession`、`EnvLoader`、compaction 等

PhiCoding.Tui exe 下分：

- `PhiCoding.Tui/`：应用壳 `PhiTuiApp`、基础设施（`SelectionCopyHost`、`SystemClipboard`、`ToastHostSentinel`）+ 入口 `Program.cs`
- `PhiCoding.Tui/Components/`：可复用积木——`PromptInput`（输入壳：editor + slash 分发 + 对话框 + skill 补全）、`ChatHeader`、`ChatTranscript`（订阅 projector 并按 `ChatLine.Id` DIFF 到 `DocumentFlow`）、`PhiStatusBar`、`SuggestionStrip`、`StatusBarBinder`（薄壳，调 `SessionStatusRouter` + 实现 `ISessionStatusSink`）、`SideBySideDiff`、`ToolCards/`（XenoAtom 实现）
- 命名空间：`PhiCoding.Tui.*`

PhiCoding.Desk exe 下分：

- `PhiCoding.Desk/`：应用壳 `PhiDeskApp` + `DeskNavModel`（纯导航模型）+ `BackendRegistrar`（OS → MewUI platform/backend）+ 入口 `Program.cs`
- `PhiCoding.Desk/DeskChatPage.cs`：单会话聊天页（持有一个 projector，导航时 Dispose）——由 header + transcript + input + status bar 组成
- `PhiCoding.Desk/Components/`：与 TUI 镜像的积木——`ChatHeaderView`、`TranscriptView`（订阅 projector）、`PromptInputView`（editor + slash 分发）、`StatusBarView`（实现 `ISessionStatusSink`）、`ToolCards/`（`IDeskToolCard` + `DeskToolCardRegistry`）、`ModelsPage`（模型切换）、`ProvidersPage`（provider 连接 + API key 弹窗）
- `PhiCoding.Desk/DeskTheme.cs`：语义色 → MewUI `Palette` 的映射（Palette 无 named color slots）
- 命名空间：`PhiCoding.Desk.*`

### Desk 布局（与 TUI 不同）

Desk 不是 TUI 的单页全屏聊天，而是两栏 shell（参考 MewUI Gallery）：
`PhiDeskApp` → `DeskShell` → `NavigationView`（可折叠左栏 + 右侧内容区）。左栏放 New Chat +
Sessions 列表 + footer 的 Models / Providers 入口；右侧内容区通过一个共享的 `ViewHost`
（ContentControl）切换聊天页 / ModelsPage / ProvidersPage。选中 session 触发 `ResumeAsync`，
`SessionChanged` 时重建聊天页。选择 Models/Providers 通过 `ContentSelector` 返回同一个
`ViewHost` 来绕过 NavigationView 的 per-item content 缓存。

**重要**：导航（`NavigateToNewAsync`/`ResumeAsync`）不能在 NavigationView 的
`SelectionChanged` dispatch 内同步执行——它会触发 `SessionChanged` → `RebuildNavigation`，
在 dispatch 中重入修改 nav 的 items/content host，导致右侧界面（编辑器）消失。
`DeskShell.OnNavSelection` 用 `postToUi`（`dispatcher.BeginInvoke`）把选中处理推迟到
当前事件之后。`SessionChanged` 的重建走 `dispatchToUi`（`dispatcher.Invoke`）。

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
- MewUI: ~/github/MewUI（NuGet 包名 Aprillz.MewUI，samples/ 有示例）

## 桌面 UI 差异（Desk vs TUI）

- MewUI 没有内置 markdown 控件：Desk 端 assistant 文本直接以 mono `Label` 显示原文，不解析 markdown。
- `Palette` 无 named color slots：语义色通过 `PhiCoding.Desk.DeskTheme` 映射（TextSecondary/Danger/Success）。
- 两个 UI 共用 `ChatTranscriptProjector` 投影：各自按 `ChatLine.Id` DIFF 渲染（TUI→DocumentFlow，Desk→StackPanel）。