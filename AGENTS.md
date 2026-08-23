# Phi Agent Instructions

Phi 是一个 C# 版本的 基于 pi agent 架构的最小 coding agent，目标是从头到尾自己实现一个 coding agent，方便我理解开发中的每个细节

## 架构

### 整体架构和依赖关系

- Phi.Provider: 手写的 llm provider，对应 tau_ai 目录，我不喜欢 tau_ai 这个名字，因为看不出来是负责跟llm provider通信的
- Phi.Agent: 对应 tau_agent 目录，自己实现的一个 harness, 状态管理，循环
- Phi: Library，提供 UI-agnostic runtime（sessions / providers / tools / prompts / status router / slash commands / tool descriptors / chat projector）。无 UI 框架依赖。
- Phi.Tui: 基于 XenoAtom.Terminal.UI 的终端界面 exe，引用 Phi。
- Phi.Avalonia: 基于 Avalonia 跨平台框架的桌面 UI 库，引用 Phi。所有 UI（桌面 / 移动 / browser）共享同一份控件代码。
- Phi.Avalonia.Desktop: Avalonia 的桌面平台入口 exe（classic desktop lifetime），引用 Phi.Avalonia。

UI 框架选择：**Avalonia**（跨平台，支持 Windows / macOS / Linux / 移动 / browser），后续的桌面 UI
开发统一在 `Phi.Avalonia/` 推进。`Phi.Avalonia/` 输出的控件树通过不同的 platform
host 复用（`Phi.Avalonia.Desktop` 是 Windows / macOS / Linux 桌面入口）。

依赖关系：

```
                Phi.Tui ───┐
                                  ├─► Phi ─► Phi.Provider ─► Phi.Agent
Phi.Avalonia ──────────────┘
        ▲
Phi.Avalonia.Desktop (exe)
```

`Phi.Agent` 是最底层的 package，依赖最少，可以注入不同的 provider 使用，可以随意分发。

### 应用层依赖关系

```
PhiTuiApp ─→ PromptInput ─┐
   (TUI 壳：     (输入组件：    │
    nav 触发     editor +      │
    整体换页)    slash 分发/   ├─→ ISession ─→ Session ─→ Harness ─→ Provider
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
            (UI-agnostic 错误分类 + 路由；TUI 在 Phi.Tui.Components.StatusBarBinder 适配)
```

会话切换在 ISession 自身：`Session.NewSessionAsync(cwd?)` 返回新 session 并 Dispose 旧的；
`Session.ResumeAsync(id)` 同理（解析记录里的 `Cwd` 实现跨工作区 resume）。
TUI 端把 `State<ISession>.Value` 重新赋值即可触发 `ComputedVisual` 重建；
Avalonia 端把新 session 塞进 `ActiveSession.Replace(...)` 让 `Changed` 事件通知 shell。

Avalonia 端结构镜像：

```
Phi.Avalonia.Desktop.Program
   └─► ActiveSession ─→ PhiAvaloniaApp (Avalonia Application)
        (current session       └─► MainWindow ─→ ShellView (两栏 shell)
         + Changed 事件)                          ├─ 左栏：NewChat + 会话列表（按 workspace/date 分组）+ Providers
                                                  └─► ContentControl (ViewHost)
                                                        ├─ ChatPageView ─→ (header + transcript + prompt + status)
                                                        └─ ProvidersPage
```

Avalonia 端没有 `State<T>`，需要一个轻量的 session holder（`ActiveSession`）来保存
current 引用并发出 `Changed` 事件供 shell 重建聊天页。这是纯 UI 绑定辅助，
不含任何 Phi 领域逻辑。TUI 用 XenoAtom.Terminal.UI 的 `State<T>` 等价
（`PhiTuiApp` 直接持有一个 `State<ISession>`，session 替换通过 `SessionReplaced`
事件触发 `State.Value = newSession`）。

`TranscriptView` 订阅共享 `ChatTranscriptProjector`，按稳定 `ChatLine.Id` DIFF 渲染。
Avalonia 端走 `ISession.StateChanged` → `SessionStatusRouter` + `ISessionStatusSink`
与 TUI 完全一致。

### TUI 渲染

PhiTuiApp 持有一个 `State<ISession>`。`PromptInput` 在 `/new` / `/sessions` 触发的
导航里调 `ISession.NewSessionAsync` / `ResumeAsync`，得到新 session 后通过
`SessionReplaced` 事件让 shell 把 `State.Value` 翻到新引用。一个
`ComputedVisual` 读这个 State，navigate 时自动重建整页（header + transcript
+ editor + strip + status bar）。空 session 的内容槽显示一行 slogan，提交首条 prompt
后被 user bubble 自动替换——session 立刻有自己的 id，详情路径不再需要。

### 目录约定

Phi 库下面分：

- `src/Phi/`：`ISession`、`SessionState`、`Session`（含导航 + NewSessionAsync/ResumeAsync/ListRecent/AvailableProviders）、`SessionEnvironment`（跨 session 共享的 resolver / 工具 / 压缩参数）、`SessionManager`、`SessionRecord`、`SessionStorage`、`SessionPaths`、`WorkspaceSessionStore`、`SessionRuntime`（composition root 用 `Session.LoadAsync` 一次性装配：resources + tools + prompt + harness）、compaction
- `src/Phi/Providers/`：`ProviderManager`、`ProviderCatalog`、`ICredentialStore`、`IProviderResolver`、`PhiSettings`
- `src/Phi/Prompts/`、`src/Phi/Resources/`：prompt 构建 + skill/prompt 加载。**Sprint 2.5：`src/Phi/Tools/` 已搬到 `extensions/CodingPack/Tools/`**（4 个默认 coding tool 现在通过 CodingPack 扩展注册进 harness，不走 Phi 主体）
- `src/Phi/Slash/`：UI-agnostic slash 命令（SlashCommands、SlashCommandCatalog）
- `src/Phi/Status/`：session → 状态条目的 routing（ISessionStatusSink、SessionStatusRouter、ErrorClassifier）
- `src/Phi/Prompt/`：UI-agnostic 输入建议提供器（ISuggestionProvider、SuggestionItem、SlashCommandProvider、SkillSuggestionProvider）
- `src/Phi/ToolCards/`：跨 UI 的 tool 元数据（ToolDescriptor、ToolDescriptors）
- `src/Phi/Chat/`：UI-agnostic chat 投影（ChatLine DU、ChatTranscriptProjector）——两个 UI 都订阅 projector 的 `Changed`，按稳定 `ChatLine.Id` DIFF 渲染
- `src/Phi/` 根：`ISession`、`SessionState`、`Session`、`WorkspaceSessionStore`（扫 `{PHI_HOME}/sessions/*/index.jsonl` 合并所有工作区的会话）、compaction 等

Phi.Tui exe 下分：

- `src/Phi.Tui/`：应用壳 `PhiTuiApp`、基础设施（`SelectionCopyHost`、`SystemClipboard`、`ToastHostSentinel`）+ 入口 `Program.cs`
- `src/Phi.Tui/Components/`：可复用积木——`PromptInput`（输入壳：editor + slash 分发 + 对话框 + skill 补全；持有 `ISession` 并在导航后通过 `SessionReplaced` 事件通知 shell）、`ChatHeader`、`ChatTranscript`（订阅 projector 并按 `ChatLine.Id` DIFF 到 `DocumentFlow`）、`PhiStatusBar`、`SuggestionStrip`、`StatusBarBinder`（薄壳，调 `SessionStatusRouter` + 实现 `ISessionStatusSink`）、`SideBySideDiff`、`ToolCards/`（XenoAtom 实现）
- 命名空间：`Phi.Tui.*`

Phi.Avalonia 库下分：

- `src/Phi.Avalonia/`：应用壳 `PhiAvaloniaApp`（Avalonia `Application`）+ `MainWindow`（基于 `SukiWindow`）+ `ShellView`（两栏 shell）+ `ChatPageView` + `NavModel`（纯导航模型）+ `ActiveSession`（current session + Changed 事件，作为 XenoAtom `State<T>` 的 Avalonia 等价物）+ `AvaloniaTheme`（语义色，映射 SukiUI 色板）+ 入口组件 + `DeskLog`
- `src/Phi.Avalonia/Components/`：与 TUI 镜像的积木——`TranscriptView`（订阅 projector，按 `ChatLine.Id` DIFF 渲染到 `StackPanel`）、`PromptInputView`（editor + slash 分发 + 工作区选择器 + 模型 picker；持有 `ISession` + `ActiveSession`，导航直接调 `session.NewSessionAsync` 并 `active.Replace(next)`）、`ProvidersPage`（provider 连接 + API key 弹窗）、`ToolCards/`（Avalonia 实现）
- `src/Phi.Avalonia/Controls/`：`EllipsisMenu` 等跨组件复用的小控件
- 命名空间：`Phi.Avalonia.*`（含子命名空间 `Phi.Avalonia.Components.*` / `Phi.Avalonia.Controls.*`）

Phi.Avalonia.Desktop exe 下分：

- `src/Phi.Avalonia.Desktop/`：`Program.cs`（组合 provider manager + `SessionEnvironment` + 初始 `Session.LoadAsync(...)` + `ActiveSession`，挂到 `PhiAvaloniaApp`，启动 classic desktop lifetime）
- 命名空间：`Phi.Avalonia.Desktop.*`

### Avalonia Shell 布局

Avalonia shell 不是 TUI 的单页全屏聊天，而是两栏布局：

`Phi.Avalonia.Desktop.Program` → `PhiAvaloniaApp` → `MainWindow` → `ShellView`
（两栏壳用 SukiUI 的 `SukiSideMenu`：玻璃 pane 承载左栏的 sessions 浏览器，
右栏走 `UseCustomContent=true` 用我们的 `ViewHost`（`ContentControl`）切换聊天页 /
ProvidersPage——不走 SukiSideMenu 的 item 导航模型）。

左栏按 SukiSideMenu 的三区槽位布局：

- `HeaderContent`（五行 Grid，`MaxHeight` 绑定 SideMenu 高度防止溢出）：New Chat 导航行（圆角，SukiSideMenuItem 观感）→ divider → sessions header → sessions `ListBox`（`*` 行填满）→ Providers 导航行（pin 在底部，小窗口也可见）
- Items 区：不用——sessions 是定制数据列表，不是菜单 item
- `FooterContent`：不用

sessions 按 `NavModel.GroupMode` 分组；每行 session 显示标题 + `EllipsisMenu`（XAML UserControl：Border 触发器 + `PointerPressed` 手动 toggle 菜单 + 顶层 `PointerPressed` 外部点击 dismiss，Rename / Delete），workspace 行显示工作区名 + ⋯ 菜单（New session / Delete workspace）

选中 session 触发 `ISession.ResumeAsync`，`ActiveSession.Changed` 时重建聊天页。Providers 通过
`ViewHost.Content` 切换。

**重要**：导航（`_active.Current.NewSessionAsync` / `ResumeAsync` → `_active.Replace`）不能在
`ListBox.SelectionChanged` 的 dispatch 内同步执行——它会触发 `ActiveSession.Changed` →
`RebuildNavigation`，在 dispatch 中重入修改 ListBox 的 ItemsSource / SelectedIndex，
导致右侧界面（编辑器）消失。`ShellView.OnSessionSelection` 用 `postToUi`
（`Dispatcher.UIThread.Post`）把选中处理推迟到当前事件之后。`ActiveSession.Changed`
的重建走 `dispatchToUi`（`Dispatcher.UIThread.Post`）。

**跨工作区会话**：session 按 cwd 分目录存储（`{PHI_HOME}/sessions/{projectKey}/`）。
TUI 绑定进程 cwd，`/sessions` 只看当前目录；Avalonia 桌面不绑定进程 cwd，用
`WorkspaceSessionStore` 合并所有工作区的会话，左侧导航按工作区分组展示。
`ISession.ResumeAsync(id)` 会通过 `WorkspaceSessionStore.FindSession` 解析会话记录自己的
`Cwd`（跨工作区 resume），`ISession.NewSessionAsync(cwd)` 可指定新会话的工作目录。
`PromptInputView` 在新建（未持久化）会话的编辑器上方显示工作区选择器（来自记录的
distinct cwd + "Choose folder…"），第一条消息到达后隐藏。

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

- Avalonia 有成熟的控件体系：Markdown 通过 `MarkView.Avalonia` 渲染，图标通过 `Material.Icons.Avalonia` 渲染，主题走 `SukiUI`（`SukiTheme` + `SukiWindow`，light / dark 自动跟随系统；`PhiAvaloniaApp.axaml` 用 `<suki:SukiTheme ThemeColor="Blue"/>`）。
- 语义色统一在 `Phi.Avalonia.AvaloniaTheme`（`TextSecondary` / `Danger` / `Success` / `Accent` / `ControlBorder` / `ContainerBackground` 等），明暗 hex 对映射 SukiUI 色板，light/dark 跟随 `Application.ActualThemeVariant`；没有 TUI ANSI 命名的色板。
- 两个 UI 共用 `ChatTranscriptProjector` 投影：各自按 `ChatLine.Id` DIFF 渲染（TUI→DocumentFlow，Avalonia→StackPanel）。