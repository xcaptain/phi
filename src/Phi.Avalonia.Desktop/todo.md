# Phi.Avalonia 重构计划

> 目标：删除老的 `Phi.Avalonia` / `Phi.Avalonia.Desktop` / `Phi.Avalonia.Tests`，把 Avalonia 业务内联到 `Phi.Avalonia.Desktop`（最终改名 `Phi.Avalonia.Desktop`），按 XAML-first + CommunityToolkit.Mvvm + FluentTheme 重写，并支持 NativeAOT。

## 验收标准

- [ ] `src/Phi.Avalonia` 目录已删除
- [ ] `src/Phi.Avalonia.Desktop` 目录已删除
- [ ] `tests/Phi.Avalonia.Tests` 已并入 `tests/Phi.Avalonia.Desktop.Tests`
- [ ] `tests/Phi.Avalonia.Desktop.Tests` 通过 `dotnet test`
- [ ] `dotnet publish -c Release -r osx-arm64 -p:PublishAot=true` 成功，无 trim warning
- [ ] 运行 `phi-avalonia` 二进制能正常加载会话、发送消息、切换会话
- [ ] 不依赖 SukiUI；主题用 FluentTheme，MarkdownTheme 走 `<StyleInclude>`
- [ ] `ViewLocator` 反射已删除，VM→View 用 `DataTemplate x:DataType` 或手写映射

## 不复用的旧代码（按对话确定）

- `Phi.Avalonia.ActiveSession` — 内联重写（用 `ObservableObject`）
- `Phi.Avalonia.AvaloniaUiSink` — dialog 部分按 XAML-first 重写
- `Phi.Avalonia.AvaloniaTheme` — 保留语义色结构，XAML 化为资源字典
- `Phi.Avalonia.PhiAvaloniaApp.axaml`/`.cs` — 重写（partial + InitializeComponent）
- `Phi.Avalonia.ShellView.cs` / `ShellLayout.axaml` — 控制器 + XAML 各一半
- `Phi.Avalonia.MainWindow.cs`（SukiWindow） — 改回 `Window`
- `Phi.Avalonia.ChatPageView.cs` / `ChatPageLayout.axaml` — 重写（XAML-first）
- `Phi.Avalonia.Components/*` — 全部按 XAML-first / MVVM 重写
- `Phi.Avalonia.Controls.EllipsisMenu` — 用 FluentTheme `MenuFlyout` 替代
- `Phi.Avalonia.DeskLog` — 内联（小工具）
- `Phi.Avalonia.ViewLocator` — 删除

## 复用的 UI-agnostic 块（在 Phi 主项目，不动）

- `Phi.Chat.ChatTranscriptProjector` / `ChatLine` DU
- `Phi.Status.SessionStatusRouter` / `ErrorClassifier` / `ISessionStatusSink`
- `Phi.Slash.SlashCommands` / `SlashCommandCatalog`
- `Phi.Prompt.ISuggestionProvider` / `SlashCommandProvider` / `SkillSuggestionProvider`
- `Phi.ToolCards.ToolDescriptor` / `ToolDescriptors`
- `Phi.WorkspaceSessionStore` / `Phi.Session.LoadAsync` / `Phi.SessionEnvironment`

---

## Phase 0 — 项目脚手架（先做，后面所有阶段都依赖这里）

> 范围限制：只动 `Phi.Avalonia.Desktop.csproj` + 删 `app.manifest`。**不改**任何模板代码（`App.axaml`、`App.axaml.cs`、`ViewLocator.cs`、`ViewModels/*`、`Views/*`、`Program.cs`），由 Phase 1+ 自然覆盖。

### 0.1 ProjectReference

- [x] 加 `Phi.csproj`、`Phi.Extensions.Host.csproj`、`extensions/CodingPack/CodingPack.csproj`
- [x] `Phi.Agent` / `Phi.Provider` 经 `Phi.csproj` transitive 引入，不必显式列

### 0.2 AOT 标记

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <RootNamespace>Phi.Avalonia.Desktop</RootNamespace>
  <AssemblyName>phi-avalonia</AssemblyName>
</PropertyGroup>
```

- [x] `<PublishAot>` **只**挂在这一个 csproj，不能漏到 Phi 核心（`Phi.SchemaGen` 是 netstandard2.0，会触发 NETSDK1207）
- [x] `<RuntimeIdentifier>` **不**写在 csproj —— `dotnet publish -r <rid> -p:PublishAot=true` 时按平台传（macOS `-r osx-arm64`、Windows `-r win-x64`、Linux `-r linux-x64`）。csproj 写死 RID 会让跨平台 dev 构建下载错平台运行时包

### 0.3 清理 csproj

- [x] **保留** `app.manifest`（macOS + Windows 双平台；Windows 端 transparency / embedded controls 行为依赖它，csproj 里 `<ApplicationManifest>` 保留指向）。Phase 9 改名时同步把 manifest 里的 `assemblyIdentity` 名字从 `Phi.Avalonia.Desktop.Desktop` 改成 `Phi.Avalonia.Desktop.Desktop`
- [x] 删 `<Folder Include="Models\" />`
- [x] 保留原始 6 项 PackageReference：`Avalonia`、`Avalonia.Desktop`、`Avalonia.Themes.Fluent`、`Avalonia.Fonts.Inter`、`AvaloniaUI.DiagnosticsSupport`（条件 <IncludeAssets> 不变）、`CommunityToolkit.Mvvm`
- [x] 加 PackageReference：`MarkView.Avalonia`、`MarkView.Avalonia.SyntaxHighlighting`、`Material.Icons.Avalonia`、`DiffPlex`
- [x] `Avalonia.Headless` 留到测试项目，不在这层引用

### 0.4 保留模板脚手架

- [x] **不动** `App.axaml` / `App.axaml.cs`（Phase 1 重写）
- [x] **不动** `ViewLocator.cs`（Phase 1 删）
- [x] **不动** `ViewModels/MainViewModel.cs`、`ViewModels/ViewModelBase.cs`（Phase 1 移到 Phase 9 一起处理）
- [x] **不动** `Views/MainWindow.axaml(.cs)`（Phase 2 重写）
- [x] **不动** `Program.cs`（Phase 1 重写整个 composition root）

### 0.5 加入 solution

- [x] `Phi.Avalonia.Desktop.csproj` 已在 `phi.slnx`（实施前就已就绪）
- [ ] 删 `App.axaml` / `App.axaml.cs`（Phase 1 重写）
- [ ] 删 `Program.cs` 里的 `WithDeveloperTools()` 之外的 `LogToTrace()` 等默认 trace

### 0.5 把项目加入 solution

- [ ] `phi.slnx` 加 `Phi.Avalonia.Desktop.csproj`
- [ ] 暂时保留 `Phi.Avalonia` / `Phi.Avalonia.Desktop` / `Phi.Avalonia.Tests`，Phase 9 才删

---

## Phase 1 — App + Composition Root

### 1.1 App 主题

`App.axaml`（XAML-first，不要 ViewLocator）：

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Phi.Avalonia.Desktop.App"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://MarkView.Avalonia/Themes/MarkdownTheme.axaml" />
  </Application.Styles>
</Application>
```

- [ ] `StyleInclude` 走 XAML，不要走 `AvaloniaXamlLoader.Load(stream)`（AOT 拿不到 AvaloniaResource 流，参见老 `PhiAvaloniaApp.axaml:6-14` 注释）

### 1.2 App 代码后置（AOT-friendly）

- [ ] `public partial class App : Application` + `InitializeComponent()`，不要 `AvaloniaXamlLoader.Load(this)`
- [ ] 在 `OnFrameworkInitializationCompleted` 里接入 IServiceProvider（后面 Stage 注入）

### 1.3 Composition Root（`Program.cs`）

照搬老 `Phi.Avalonia.Desktop/Program.cs:78-156`：

- [ ] `TaskScheduler.UnobservedTaskException` 写 log
- [ ] CLI args：`--session <id>` 解析
- [ ] `new ProviderManager()` + `ResolveDefaultProvider()` + `ResolveDefaultModel(...)`
- [ ] `IUiSink currentSink = new NullUiSink()`
- [ ] `ExtensionRuntime? currentRuntime = null`
- [ ] `SessionEnvironment.Default(providerManager, extensionRuntimeFactory, extensionRuntimeFactoryAsync)`
- [ ] `WorkspaceSessionStore.ListWorkspaces()` 派生默认 cwd
- [ ] `await Phi.Session.LoadAsync(...)` 构造初始会话
- [ ] `session.HasUi = true`、`new ActiveSession(session)` 传给 `PhiAvaloniaApp`
- [ ] `using (session) { ... }` 内启动 Avalonia

### 1.4 ActiveSession（内联）

- [ ] 新建 `App/ActiveSession.cs`
- [ ] `partial class ActiveSession : ObservableObject`，`[ObservableProperty] private ISession _current`
- [ ] `public event Action? Changed`（partial 变更后 raise），ViewModel 订阅刷新
- [ ] `public ISession Replace(ISession next)` 设新值、释放旧值、raise Changed
- [ ] 整个文件 < 80 行

### 1.5 null sinks 占位

- [ ] 实现一个 `NullUiSink : IUiSink`（AOT 友好，全部返回 null/default）— 启动期在 sink 还没建出来时占位

---

## Phase 2 — Window + Shell 骨架

### 2.1 MainWindow

- [ ] `Views/MainWindow.axaml`：纯 `Window`，不引入 `SukiWindow`
- [ ] `x:Class="Phi.Avalonia.Desktop.Views.MainWindow"` + `partial class MainWindow : Window` + `InitializeComponent()`
- [ ] `Width=1024`、`Height=720`、`WindowStartupLocation=CenterScreen`
- [ ] `Icon = new WindowIcon(new Bitmap(AssetLoader.Open(...)))`（保留图标）
- [ ] `Content` 绑定到 `ShellView.Root`（用 data context 或者直接 code-behind 设）
- [ ] `Closed += DisposeShell`

### 2.2 ShellView（控制器类）

- [ ] `Views/ShellView.axaml`：两栏 `<Grid>` — 左 `Sidebar`（sessions + 新建/Providers 按钮），右 `ViewHost`（`ContentControl`，聊天页 / Providers 页切换）
- [ ] 后置 `ShellView.cs` 是 orchestrator：监听 `ActiveSession.Changed`、`WorkspaceSessionStore` 变化，调用 `RebuildNavigation`、`ShowChat`、`ShowProviders`
- [ ] 老 `ShellView.cs:211-313` 里 imperative 拼 `Grid`/`TextBox`/`EllipsisMenu` 全部用 `DataTemplate x:DataType="vm:SessionEntryViewModel"` + `x:DataType="vm:WorkspaceEntryViewModel"` 替换
- [ ] 行 ⋯ 菜单改用 `MenuFlyout` (`<Button.Flyout>`) — Fluent 原生，AOT 友好
- [ ] 重命名就地编辑用 `TextBox.IsVisible` toggle + top-level `PointerPressedEvent` bubble 提交，参考 `ShellView.cs:367-402` 的 `AttachRenameDismiss` 模式
- [ ] `OnSessionSelection` 用 `Dispatcher.UIThread.Post` 推迟 navigation（不能同步 dispatch，否则 ListBox 重入会丢焦点，见 `ShellView.cs:482-488` 注释）

### 2.3 主题与语义色

- [ ] `Resources/Theme.axaml`：把老 `AvaloniaTheme.cs` 的 `Accent`、`AccentText`、`ControlBorder`、`ContainerBackground`、`Danger`、`TextPrimary`、`TextSecondary`、`DangerBackground`、`MonoFontFamily` 全部改为 `<SolidColorBrush x:Key="...">` 资源字典
- [ ] `App.axaml` `<Application.MergedDictionaries>` 引 `Resources/Theme.axaml`
- [ ] 控件按 key 引用，不写死的 `Brushes.Transparent` 等（除 ViewModel-driven 临时态）

---

## Phase 3 — Sidebar

### 3.1 NavModel

- [ ] `Models/NavModel.cs`（从 `Phi.Avalonia/NavModel.cs` 搬）：纯 group/entry 模型，UI-agnostic
- [ ] `public record struct Entry(Kind Kind, string? SessionId, string Title, string? Cwd)`
- [ ] `BuildMainEntries(...)`、`ListAllSessions(int limit)`、`IndexForActive(...)` 直接搬

### 3.2 Sidebar XAML

- [ ] `Views/SidebarView.axaml`：New Chat 按钮 / 工作区分组 Header / `ItemsRepeater` / Providers 按钮（pin 在底）
- [ ] 行模板 `DataTemplate x:DataType="models:SessionEntryVm"`：标题 + `MenuFlyout`（Rename / Reload Extensions / Delete）
- [ ] 工作区行 `DataTemplate x:DataType="models:WorkspaceEntryVm"`：标题 + `MenuFlyout`（New Session / Delete）
- [ ] 行控件全部 XAML，控件逻辑通过 `Command` binding 到 ViewModel

### 3.3 SidebarViewModel

- [ ] `[ObservableProperty] ObservableCollection<Entry> Entries`
- [ ] `ICommand NewChatCommand`、`ResumeCommand`、`RenameCommand`、`DeleteCommand`、`ReloadExtensionsCommand`
- [ ] `ICommand SetGroupModeCommand(ByWorkspace|ByDate)`
- [ ] 监听 `ActiveSession.Changed` 调 `RebuildEntries()`
- [ ] 监听 `WorkspaceSessionStore` 持久化事件

---

## Phase 4 — ChatPage

### 4.1 ChatPageView（XAML-first）

- [ ] `Views/ChatPageView.axaml`：上下分栏，上 transcript 填充，下 `PromptInputView` dock
- [ ] 上方 `Header`（标题、模型切换、cwd 切换）+ `ScrollViewer` 包 `ItemsRepeater`
- [ ] `x:DataType="vm:ChatPageViewModel"` — ViewModel 持有 projector、subscribes 监听
- [ ] 后置 `ChatPageView.cs` 只做 composition（创建 VM、绑 projector、dispose）

### 4.2 ChatHeader

- [ ] `Components/ChatHeader.axaml`：标题、cwd chip、模型切换下拉（用 `ComboBox`/Fluent `ComboBox` 而非自定义 picker）
- [ ] ViewModel 通过 `ActiveSession.Current.State` 反映

### 4.3 TranscriptView

- [ ] `Components/TranscriptView.axaml`：`ScrollViewer` 包 `ItemsControl` 或 `ItemsRepeater`，`DataTemplate` 按 `ChatLine` DU dispatch 到对应 user-bubble / assistant-markdown / thinking-collapsible / tool-card / custom-line / persistent-error
- [ ] ViewModel 订阅 `ChatTranscriptProjector.Changed`，按 `ChatLine.Id` DIFF：
  - 新增 → 加到集合
  - 同 id → patch in place（assistant markdown 续写、tool card complete）
- [ ] `Dispatcher.UIThread.Post` 跑 DIFF
- [ ] 初始渲染按 `InitialRenderChunkSize=12` 分块（同老 `TranscriptView.cs:95-113`），避免长 transcript 卡顿
- [ ] `StickToBottom`：用户已接近底部时黏住，否则不动（老 `TranscriptView.cs:155-168` 逻辑照搬）

### 4.4 行模板（DataTemplate）

- [ ] `UserTextLine` → 右对齐 accent-color bubble
- [ ] `AssistantTextLine` → `<MarkdownViewer Markdown="{Binding Text}" />`（streaming 时 patch `Markdown` 续写）
- [ ] `ThinkingLine` → Fluent `Expander` 或自写 `CollapsibleSection`，title + body 各自可 patch
- [ ] `ToolCallLine` → `ToolCards/{Read,Bash,Edit,CustomCard}View`（详见 Phase 4.5）
- [ ] `PersistentErrorLine` → 红色 left bubble
- [ ] `CompactionDividerLine` → 灰字一行
- [ ] `CustomLine` / `CustomMessageLine` → dispatch 到 `IExtensionRenderers` 注册的渲染器，没有 fallback 到 plain text bubble
- [ ] `SkillInvocationLine` → Expander + body

### 4.5 Tool Cards（从 `Phi.Avalonia.Components.ToolCards` 迁）

- [ ] `ToolCards/ReadToolCardView.axaml` / `ReadToolCardViewModel.cs`
- [ ] `ToolCards/BashToolCardView.axaml` / `BashToolCardViewModel.cs`
- [ ] `ToolCards/EditToolCardView.axaml` / `EditToolCardViewModel.cs`
- [ ] `ToolCards/CustomCardDemoView.axaml` / VM
- [ ] `ToolCards/SideBySideDiff.cs`：DiffPlex 引用照搬，AOT 安全
- [ ] 全部 `partial class XxxToolCardViewModel : ObservableObject`，`[ObservableProperty]` 标 args / result / pending

### 4.6 PromptInput

- [ ] `Components/PromptInputView.axaml`：`<TextBox>` 占满 editor + 下方工具条（model picker、send button）
- [ ] 新会话未持久化时上方显示 workspace picker（原生 `ComboBox`，从 `WorkspaceSessionStore.ListWorkspaces()` 加载）
- [ ] 占位 `ComboBox` 用 Fluent 原生，**不**自己定义 dropdown dismissal 逻辑（修老 `PromptInputView.cs` 的 dropdown 关不掉 bug）
- [ ] `PromptInputViewModel`：
  - 订阅 `ISuggestionProvider`（slash / skill）
  - `SubmitCommand` → `session.SubmitAsync(text)`
  - `Text` property 双向绑定 TextBox
  - workspace picker `SelectedWorkspace` → 第一条消息到达后 ViewModel 隐藏 picker
- [ ] Slash 命令分发走 `Phi.Slash.SlashCommands.HandleAsync(...)` — UI-agnostic 块直接调，不重新实现

### 4.7 StatusBar

- [ ] `Components/PhiStatusBar.axaml`：错误 / 上下文 / 模型信息
- [ ] 绑定 `ISessionStatusSink`（由 `Phi.Status.SessionStatusRouter` 产出）
- [ ] ChatPageViewModel 注入 `ISessionStatusSink`，订阅 → 把状态展示到底部条

---

## Phase 5 — Providers

### 5.1 ProvidersPage

- [ ] `Views/ProvidersPage.axaml`：每个 provider 一行 `ProviderRowView`，列 provider 名、状态、连接按钮
- [ ] `ProviderRowViewModel`：绑 provider name、`HasKey`（来自 `ICredentialStore`）、`ConnectCommand`（打开 API key dialog）
- [ ] API key 弹窗用 Fluent `DialogHost` 或自写 `Window`，**走 XAML** 而不是 imperative（避免老 `AvaloniaUiSink` 里 `new Button {...}` 手写 dialog 风格）

---

## Phase 6 — UI Bridge（接 `IPhiUiBridge`）

### 6.1 AvaloniaUiSink 重写

- [ ] `Bridge/AvaloniaUiSink.cs`：实现 `IUiSink`
- [ ] `Notify(message, level)`：`level == Info` 走 DeskLog；其他（Warning/Error）走 `projector.SubmitPersistentError(...)`
- [ ] `FlashError(message, persistent)`：persistent 也走 projector，不污染 transcript（Sprint 4 切 `CustomLine`）
- [ ] `SubmitTranscriptLine(line)`：`projector.SubmitCustomLine(line.Type, line.Id, ...)`
- [ ] `SubmitCustomMessageLine(...)`: `projector.SubmitCustomMessageLine(...)`
- [ ] `ShowSelectAsync/ShowConfirmAsync/ShowInputAsync`：弹窗用 XAML 优先 — 找现有 Fluent theme 下能用的 modal 模式；如必须新建，统一抽 `Bridge/Dialogs/SelectDialog.axaml` 等
- [ ] timeout 用 `Task.Delay + dialog.Close()` 老办法保留

### 6.2 PhiUiBridge 接线

- [ ] 照搬老 `Program.cs:91-101` 的 `currentSink` + `currentRuntime` accessor 模式
- [ ] 每次 `ShowChat` 重建 sink（`page.Projector` + main window accessor）
- [ ] `PhiUiBridge(() => currentSink)` 喂给 `ExtensionRuntimeFactory`

### 6.3 File picker

- [ ] `Bridge/FolderPicker.cs`：`Task<string?> PickAsync(Window owner)` 用 `IStorageProvider.OpenFolderPickerAsync(...)`
- [ ] 失败返回 null，主 ViewModel 处理 fallback（用 default cwd）

---

## Phase 7 — 测试（Avalonia.Headless，独立项目）

### 7.1 测试项目脚手架

新建 `tests/Phi.Avalonia.Desktop.Tests/Phi.Avalonia.Desktop.Tests.csproj`：

- [ ] `<OutputType>Exe</OutputType>` + `<IsTestProject>true</IsTestProject>` + `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`
- [ ] **不要**加 `<PublishAot>`（TUnit managed CLR 跑）
- [ ] `PackageReference Include="TUnit"`, `Avalonia.Headless`, `Avalonia.Themes.Fluent`
- [ ] `ProjectReference`：`Phi.Avalonia.Desktop.csproj` + 测试需要的 `Phi.csproj` / `Phi.Extensions.Host.csproj` / `extensions/CodingPack/CodingPack.csproj`

### 7.2 AvaloniaTestHost

- [ ] `tests/Phi.Avalonia.Desktop.Tests/AvaloniaTestHost.cs` 照搬老 `AvaloniaTestHost.cs`，只是 `AppBuilder.Configure<App>()` 指到新的 `Phi.Avalonia.Desktop.App`

### 7.3 测试覆盖

- [ ] ViewModel：
  - `SidebarViewModelTests`：分组、selected index、NewChatCommand 触发 `ActiveSession.Replace(...)`
  - `ChatPageViewModelTests`：订阅 projector、line DIFF、新增 vs patch
  - `PromptInputViewModelTests`：slash 触发 `SlashCommands.HandleAsync`、`SubmitCommand`
  - `ProviderRowViewModelTests`：HasKey、ConnectCommand
- [ ] View（Avalonia.Headless）：
  - `ShellLayoutTests`：左栏宽度、右栏 ViewHost 切换
  - `TranscriptLayoutTests`：行模板能渲染
  - `ProvidersPageLayoutTests`
- [ ] 集成：
  - `ShellViewTests`：NewChat、selection（用 postToUi defer）、delete session
  - `TranscriptViewTests`：transcript DIFF 行为
  - `PromptInputViewTests`：dropdown 行为
- [ ] 桥接：
  - `AvaloniaUiSinkTests`：sink → projector 写入

---

## Phase 8 — AOT 验证

### 8.1 静态扫描

- [ ] `dotnet build -c Release -p:PublishAot=true -p:RuntimeIdentifier=osx-arm64`：所有 trim warning 必须处理，不允许 `IL2026`/`IL3050`
- [ ] 检查整个 `Phi.Avalonia.Desktop/` 源码树没有 `Activator.CreateInstance`、`Type.GetType`（除注释或 AOT-safe 路径）
- [ ] 检查没有 `XamlReader.Parse(...)` 字符串 XAML

### 8.2 NativeAOT 发布

- [ ] `dotnet publish src/Phi.Avalonia.Desktop -c Release -r osx-arm64 --self-contained`：成功输出 `phi-avalonia` 单文件
- [ ] `file ./phi-avalonia` → Mach-O 64-bit
- [ ] `nm ./phi-avalonia | grep -c 'Unknown'` → 0 或极少

### 8.3 运行时验证

- [ ] 启动 `phi-avalonia`：窗口能开
- [ ] 新建会话：第一条消息发出去，user bubble + assistant markdown + tool card render 正常
- [ ] 切换会话：`/sessions` 或侧边栏 resume 正常，transcript 显示出来
- [ ] Providers 页能打开、API key 连接流程走得通
- [ ] 退到后台 / focus / cmd+Q 几次无 crash

---

## Phase 9 — 删旧 UI

### 9.1 重命名

- [ ] 项目目录 `src/Phi.Avalonia.Desktop/` → `src/Phi.Avalonia.Desktop/`
- [ ] csproj `<RootNamespace>Phi.Avalonia.Desktop</RootNamespace>` 已设，namespace 改 `Phi.Avalonia.Desktop.X` → `Phi.Avalonia.X`
- [ ] 全文 `Phi.Avalonia.Desktop` 替换为 `Phi.Avalonia.Desktop`
- [ ] `phi.slnx` 同步改

### 9.2 删旧项目

- [ ] `src/Phi.Avalonia/` 删除（连同 `Components/`、`Controls/`、`Assets/`）
- [ ] `src/Phi.Avalonia.Tests/` 已并入新 `Phi.Avalonia.Desktop.Tests`，删除老目录
- [ ] `phi.slnx` 移除三个老项目 csproj 引用
- [ ] `tests/Phi.Avalonia.Desktop.Tests` 目录重命名为 `tests/Phi.Avalonia.Tests`

---

## 全局风险 / 待确认

1. **FluentTheme 的 Popup/Flyout 自动 dismiss** — 老项目手动处理 `PointerPressedEvent` 是因为旧的 popup 不自动 dismiss，FluentTheme 的 `Popup` 默认 toggle via `IsOpen` + `LostFocus` 应足够。如果用户报告焦点丢失问题，回退方案是把外部 dismiss handler 抽成一个 `Behavior`（XAML 可复用，partial class）。
2. **`ActiveSession` 的 event 触发** — `ObservableObject.PropertyChanged` 配合一个 `PropertyChanged += ... → Changed?.Invoke()` 的 partial setter 模式就能工作；不需要单独的 event 总线。
3. **Tool card 的长生命周期** — 老 `ToolCallHandle` 把 result 状态存在 handle 里；新版用 ViewModel `[ObservableProperty]`，通过 `ID` 配对。每条 ToolCallLine 有唯一 id，view 层按 id 派发到对应 VM，AOT-clean。
4. **`AvaloniaToolCardRegistry`** — 老 `Phi.Avalonia.Components.ToolCards` 有一个 registry 分发到 Bash/Read/Edit/CustomCard 四个视图，迁过来做 `ToolCardRegistry.For(toolName, renderers)`，纯 `Dictionary<string, Func<...>>`，AOT-clean（无反射）。
5. **`AvaloniaUI.DiagnosticsSupport` 在 AOT 下** — Release 配置靠条件 trim，OK；Debug 跑 Avalonia.Headless 测试时**不要**带这个包，按 `csproj:19-22` 条件已隔离。
