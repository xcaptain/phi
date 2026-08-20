# Phi 扩展系统设计

> 状态：设计阶段（已对齐，未开工）。配套讨论：跨平台 + v1 安全模型 + 重命名（均已决策）。
> 对标实现：tau（~/github/tau）的 pi-extensions 形态，已生产验证。

## 设计目标

让 Phi 在保持现有 TUI / Avalonia 双端结构不变的前提下，变成一个**可扩展的 host**：用户写一个 C# 类库，引用 `Phi.Extensions` 包，实现 `IPhiExtension.Setup(IPhiApi)`，编译产出 dll（managed-only）或 bundle（带 `runtimes/`），放到 `~/.phi/extensions/` 或 `<project>/.phi/extensions/` 下，重新启动（或 `/reload`）后立即生效——**复用所有现有 UI**（transcript / prompt / status bar / tool card / slash 分发）。

扩展能做什么：

| 能力 | API 入口 | 用户最终体验 |
|---|---|---|
| 注册自定义工具 | `api.RegisterTool(tool, contribution?)` | 模型能调，转录区有 card |
| 注册 slash 命令 | `api.RegisterCommand("/foo", handler, ...)` | TUI 输入 / Avalonia 侧栏识别 |
| 监听事件 | `api.On<TEvent>(handler)` | 14+ 个 agent / lifecycle / hook 事件 |
| 提交 prompt | `api.SubmitUserMessage(...)` | 后台任务往对话塞消息 |
| 写转录行 | `api.SubmitTranscriptLine(...)` | 自定义 UI 行进转录 |
| 持久化 | `api.AppendEntryAsync("ns", data)` | 跟随 session 写盘，resume 自动重放 |
| 系统提示注入 | `api.AddPromptGuideline(text)` | 注入到 system prompt |
| 拦截 tool 调用 | `On<ToolCallHookEvent>` → `ToolCallHookResult` | 改参数 / 拦截 / 加守卫 |
| 拦截 tool 结果 | `On<ToolResultHookEvent>` → `ToolResultHookResult` | 改返回内容 / 改 details |
| 拦截用户输入 | `On<InputEvent>` → `InputHookResult` | 改写 prompt / 消费 |
| 通知用户 | `api.Notify(message, level)` | TUI toast / Avalonia 通知 |
| 询问用户 | `await api.Context.Ui.SelectAsync(...)` | 复用现有 select / confirm / input |
| 自定义 Tool Card | `api.RegisterToolCard(name, descriptor, renderer?)` | 双端按 descriptor 渲染 |
| 自定义 Transcript Line | `api.RegisterTranscriptLineRenderer(type, fn)` | 自定义行类型 + 渲染 |

---

## 1. 架构总览

完全照搬 tau 的"runtime 是 session 的伴生对象"形态。`Session` 在 `ApplyRuntime` 时持有一个 `ExtensionRuntime`，runtime 负责发现 → 加载 → 安装 hooks → 与 session 事件总线双向通信。

```
                    ┌──────────────────────────┐
                    │ Phi (runtime)            │
                    │                          │
   user prompt ─►   │ Session                  │ ◄── SteeringQueue / FollowUpQueue
                    │   Harness                │
                    │   AgentLoop              │ ─► HarnessEvent (TurnStart, ToolExec*, TurnEnd…)
                    │                          │
                    │   ┌──────────────────┐   │
                    │   │ ExtensionRuntime │   │ ◄── /reload (teardown + re-import)
                    │   │                  │   │
                    │   │  ┌─ LoadedExt   │   │
                    │   │  │   tools      │   │ ─► wrapped into IReadOnlyList<Tool>
                    │   │  │   commands   │   │ ─► appended to SlashCommandCatalog
                    │   │  │   guidelines │   │ ─► fed into SystemPromptBuilder
                    │   │  │   line render│  │ ─► installed into ChatTranscriptProjector
                    │   │  │   tool cards │   │ ─► installed into AvaloniaToolCardRegistry / TUI registry
                    │   │  │   handlers   │   │
                    │   │  └──────────────┘   │
                    │   │                  │   │
                    │   │  Tool wrappers   │   │ ─► hook tool_call / tool_result around every Tool
                    │   │  Hook dispatch   │   │ ─► turn_start / turn_end / tool_execution_* / session_*
                    │   │  GenerationGuard │   │ ─► stale-after-/reload → ExtensionError
                    │   └──────────────────┘   │
                    └──────────┬───────────────┘
                               │ IPhiUiBridge
                ┌──────────────┴──────────────┐
                ▼                              ▼
        Phi.Tui                         Phi.Avalonia
        TuiPhiUiBridge                  AvaloniaPhiUiBridge
```

**关键不变量**：

- `ExtensionRuntime` 是 session 的内部对象，**生命周期 = session 生命周期**，由 `SessionRuntime` 在 `SessionFactory.BuildRuntime` 里构造，跟随 `ApplyRuntime` 注入到 `Session`。
- `IPhiApi` 是**唯一**扩展可见的入口。session / Harness / AgentLoop 的内部状态不暴露。
- 所有 UI 都是**已存在的 UI**。扩展不构造 Visual / Control，只调接口；接口由 host 的 bridge 实现。
- TUI / Avalonia 共用同一份 `IPhiUiBridge` 协议——两边各实现一个，扩展代码完全不知道宿主是哪个。

---

## 2. 目录与发现

跟 tau 严格对齐：

| 路径 | 加载时机 |
|---|---|
| `~/.phi/extensions/*.dll` | 默认 |
| `~/.phi/extensions/<dir>/<dir>.dll` | bundle 默认（带 `runtimes/`，Sprint 3+） |
| `<project>/.phi/extensions/*.dll` | 项目信任通过后 + `--project-extensions` |
| `--extension PATH`（CLI / Settings） | 显式，无论 `--no-extensions` 与否 |

**入口点契约**：

```csharp
[PhiExtension("my-cool-ext", Version = "1.0.0",
             Capabilities = ExtensionCapability.Network | ExtensionCapability.FileSystemRead)]
public sealed class MyCoolExt : IPhiExtension
{
    public void Setup(IPhiApi api) { ... }
}
```

用 `[PhiExtension]` attribute 标注名字，避免反射遍历所有 `IPhiExtension` 实现。`IPhiExtension.Setup` 必须是同步方法——async setup 太容易写出 race condition，且 action 方法要求 session 已绑定，setup 时 session 还没绑。

**Manifest（v1 跳过）**：v1 不引入 manifest 配 `*.phi.json`，因为 .NET 项目本身就是 `csproj`，用户装 dll 就行。后续要做"扩展自带 sub-tool"时再加 manifest，声明 entry assembly / dependencies / project-trust category。

**v1 一个 dll 限一个 `[PhiExtension]` class**，避免多 class 增加 manifest 复杂度。v2 再放宽。

---

## 3. 加载机制：AssemblyLoadContext

跟 tau 的 synthetic module 不同，C# 的标准做法是 **`AssemblyLoadContext`**（ALC）：每个扩展一个 ALC，可独立卸载（`/reload` 用），扩展依赖的程序集冲突不会污染 host。

### 3.1 两个包切分

**`Phi.Extensions`**（公开包，扩展直接引用）：
- `IPhiExtension` / `IPhiApi` / `IPhiContext` / `IPhiUiBridge`
- 所有事件 payload record
- `[PhiExtension]` attribute
- `ExtensionError` / `NotifyLevel` / `MessageDelivery`
- `Capability` `[Flags]` 枚举（v1 不强制，attribute 留位，Sprint 3+ 启用）

**`Phi.Extensions.Host`**（私有包，Phi 自己引用，扩展**拿不到**）：
- `ExtensionLoader` / `ExtensionLoadContext` / `ExtensionRuntime`
- `LoadedExtension` / `DiscoveredExtension`
- 工具包装、hook dispatch、generation guard、ALC 解析

```
Phi.Extensions            ←── 扩展引用（netstandard2.0）
        ▲
Phi.Extensions.Host       ←── Phi 自己用，不发给扩展
        ▲
   Extensions/*.dll       (loaded into isolated ALC per extension)
```

**为什么分**：
- 隔离 API 边界：扩展看不到 Phi 内部类型，无法反向调用未公开 API。
- 版本独立：`Phi.Extensions` 可以 `netstandard2.0` 发布，不跟随 Phi 主版本。扩展升级 Phi 后扩展代码不动。
- 测试独立：`Phi.Extensions.Tests` 不依赖 Phi 主项目，跑得更快。

### 3.2 ALC 加载流程

```csharp
public static class ExtensionLoader
{
    public static LoadedExtension Load(string dllPath, ExtensionLoadContext alc)
    {
        // 1. loadFromAssemblyPath（不解析依赖，依赖解析到 alc.Resolving）
        var asm = alc.LoadFromAssemblyPath(dllPath);

        // 2. 找 [PhiExtension("name", ...)] attribute
        var entry = asm.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<PhiExtensionAttribute>()
                              .Select(a => (Type: t, Attr: a)))
            .FirstOrDefault()
            ?? throw ExtensionLoadDiagnostic.MissingAttribute(dllPath);

        // 3. 实例化 + 调 Setup（try/catch 记录诊断，永不让扩展崩 host）
        IPhiExtension instance;
        try
        {
            instance = (IPhiExtension)Activator.CreateInstance(entry.Type)!;
        }
        catch (Exception ex)
        {
            throw ExtensionLoadDiagnostic.ActivationFailed(dllPath, ex);
        }
        return new LoadedExtension(
            entry.Attr.Name, entry.Attr.Version, entry.Attr.Description,
            dllPath, entry.Type, instance, alc);
    }
}
```

### 3.3 ALC 的 Resolving 事件

扩展引用的第三方 dll（Newtonsoft.Json、SkiaSharp 之类）按"扩展目录优先"解析，**不**冒泡到 host ALC。一个扩展用 Newtonsoft.Json 12，另一个用 13，互不打架。

```csharp
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly string _extensionDir;

    public ExtensionLoadContext(string extensionDir)
        : base(isCollectible: true)  // 关键：可卸载
    {
        _extensionDir = extensionDir;
        Resolving += OnResolving;   // 解析失败时给本扩展一次机会
    }

    private Assembly? OnResolving(AssemblyLoadContext ctx, AssemblyName name)
    {
        // 查 runtimes/{rid}/native/ 加载 native deps（Sprint 3+）
        // 当前 v1：返回 null，冒泡到默认 ALC（依赖 host 的 runtimeconfig）
        return null;
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // v1 留空：纯托管扩展直接走默认解析
        // Sprint 3+：在这里拦截 runtimes/{rid}/native/** native deps
        return null;
    }
}
```

**关键点 `isCollectible: true`**——ALC 可卸载是 `/reload` 的前提。

### 3.4 ALC 卸载（`/reload`）

`alc.Unload()` 异步，但 .NET 不会真正释放内存，除非：

```csharp
public static void UnloadSafely(AssemblyLoadContext alc)
{
    alc.Unload();
    for (int i = 0; i < 3; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
    // 验证：WeakReference 监控，确保 dll 真正卸载
}
```

tau 的 Python `sys.modules` 卸载简单粗暴，C# 必须做这个 GC dance，否则 reload 后旧 dll 还在内存里。**这是 v1 实现细节里最容易踩坑的地方之一**——Sprint 2 必须写专门的 reload 泄漏测试。

---

## 4. `IPhiApi` 形态

完全镜像 tau 的 `ExtensionAPI`，但用 C# 习惯的命名和类型。

```csharp
public interface IPhiApi
{
    string Name { get; }
    string Version { get; }
    IPhiContext Context { get; }

    // ──────── 注册（同步，setup 内调用） ────────

    void RegisterTool(Tool tool, ToolContribution? contribution = null);
    void RegisterCommand(string name, PhiCommandHandler handler,
                         string description = "",
                         string usage = "",
                         IReadOnlyList<string>? aliases = null);
    void AddPromptGuideline(string guideline);
    void RegisterToolCard(string toolName,
                          ToolDescriptor descriptor,
                          IToolCardRenderer? renderer = null);
    void RegisterTranscriptLineRenderer(string lineType,
                                         TranscriptLineRenderer renderer);
    void RegisterMessageRenderer(string customType, MessageRenderer renderer);

    IDisposable On(string eventName, Func<PhiEvent, IPhiContext, ValueTask> handler);
    IDisposable On(string eventName, Action<PhiEvent, IPhiContext> handler);

    // ──────── 行动（session 已绑定后调用，绑定前调用 → ExtensionError） ────────

    void SubmitUserMessage(string text,
                           MessageDelivery delivery = MessageDelivery.FollowUp);
    void SubmitCustomMessage(string text,
                             string customType,
                             IReadOnlyDictionary<string, object?>? details = null,
                             MessageDelivery delivery = MessageDelivery.FollowUp,
                             bool triggerTurn = true);
    void SubmitTranscriptLine(TranscriptLine line);

    Task AppendEntryAsync(string ns, IReadOnlyDictionary<string, object?> data);

    void Notify(string message, NotifyLevel level = NotifyLevel.Info);

    void SwitchModel(string model);
    void SwitchProvider(IPhiProvider provider, string providerName, string model);
}

public interface IPhiContext
{
    string Cwd { get; }
    string Model { get; }
    string ProviderName { get; }
    string? SessionId { get; }
    string SystemPrompt { get; }
    bool IsRunning { get; }
    bool HasUi { get; }
    IReadOnlyList<IAgentMessage> Transcript { get; }
    IPhiUiBridge Ui { get; }
}
```

### 4.1 关键设计点

- **`RegisterTool(Tool, ToolContribution?)`**——Phi 已有 `ToolContribution`（prompt snippet / guidelines / capabilities），扩展可传 `null`（自动从 `tool.Description` 推断）或完整指定，**复用 system prompt 渲染**。不要让扩展直接拼 system prompt 字符串。
- **`RegisterCommand` 同步 handler**（跟 tau 一致）——命令处理器跑在 TUI / Avalonia 的 submit 路径上。要异步的扩展就 `Task.Run` 包裹。
- **`SubmitUserMessage` vs `SubmitCustomMessage`**——后者带 `customType` / `details`，前端按 `RegisterMessageRenderer` 注册的 renderer 渲染。
- **`IDisposable On(...)` 返回 token**——扩展拿 token 自己 dispose，比让扩展去记"handler 列表"更稳。
- **所有 action 方法在 generation stale / session unbound 时抛 `ExtensionError`**，**绝不静默吞错**。

---

## 5. 事件与钩子系统

照搬 tau 的 4 个分类，每类用 C# 的 `sealed record`。

### 5.1 Agent 事件

| 事件名 | Payload | 来源 |
|---|---|---|
| `agent_start` | `AgentStartEvent` | `SubmitPrompt` 起点 |
| `agent_end` | `AgentEndEvent { Messages, WillRetry }` | session run 结束 |
| `agent_settled` | `AgentSettledEvent` | 无 retry / compaction / queued continuation |
| `turn_start` | `TurnStartEvent { TurnIndex, TimestampMs }` | `TurnStartEvent(Turn)` 加 timestamp / index |
| `turn_end` | `TurnEndEvent { TurnIndex, Message, ToolResults }` | `TurnEndEvent(FinalMessage)` |
| `message_start` | `MessageStartEvent { Message }` | 已有 |
| `message_update` | `MessageUpdateEvent { Message, AssistantMessageEvent }` | 已有 |
| `message_end` | `MessageEndEvent { Message }` | 已有 |
| `tool_execution_start` | `ToolExecutionStartEvent { ToolCallId, ToolName, Arguments }` | 已有 |
| `tool_execution_update` | `ToolExecutionUpdateEvent { ... PartialResult }` | 已有（Sprint 1+ 实现） |
| `tool_execution_end` | `ToolExecutionEndEvent { ... Result, IsError }` | 已有 |
| `queue_update` | `QueueUpdateEvent { SteeringCount, FollowUpCount }` | `EnqueueSteering` / `EnqueueFollowUp` |
| `compaction_start` | `CompactionStartEvent { Reason }` | 已有 |
| `compaction_end` | `CompactionEndEvent { Reason, Result, Aborted, WillRetry, ErrorMessage }` | 已有 |
| `entry_appended` | `EntryAppendedEvent { Entry }` | `AppendMessage` 后 |
| `session_info_changed` | `SessionInfoChangedEvent { SessionId?, Title, Model, ProviderName }` | `SwitchModel` / `Rename` |
| `thinking_level_changed` | `ThinkingLevelChangedEvent { Level }` | Phi 暂无，留位 |
| `auto_retry_start` / `auto_retry_end` | 暂无，Phi 还没有 retry，留位 | — |
| `agent_event` | wildcard 透传 | — |

### 5.2 Lifecycle 事件

| 事件名 | Payload |
|---|---|
| `session_start` | `SessionStartEvent { Reason: SessionLifecycleReason }`（startup / reload / new / resume / quit） |
| `session_shutdown` | `SessionShutdownEvent { Reason }` |
| `input` | `InputEvent { Text, Source, StreamingBehavior }`；返回 `InputHookResult { Action, Text, Message }` |
| `tool_call` | `ToolCallHookEvent { ToolName, Arguments }`；返回 `ToolCallHookResult { Block, Reason, Arguments }` |
| `tool_result` | `ToolResultHookEvent { ToolName, Arguments, Result }`；返回 `ToolResultHookResult { Content, Details }` |
| `project_trust` | `ProjectTrustEvent { Cwd, HasUi, Counts }`；返回 `ExtensionTrustResult { Decision, Remember }` |

### 5.3 Hook 链语义（照搬 tau）

- **`input`**：`transform` 链式改写；`handled` 短路（消费掉，不进 agent run）。
- **`tool_call`**：`block=true` 优先；`arguments` 链式改写；handler 异常视为 block（fail-safe）。
- **`tool_result`**：链式改写 `content` / `details`。

### 5.4 Hook 挂载位置

| Hook | 挂载位置 |
|---|---|
| `input` | `Session.SubmitPrompt` 最前面（before `_runCts` 构造 / `RunAgentCoreAsync`） |
| `tool_call` / `tool_result` | `Phi` 包在 runtime 阶段包装 `Tool`：`baseTools.Concat(ext.RegisteredTools).Select(WrapTool)`；`AgentLoop.ExecuteToolSafelyAsync` 前后插入 hook |
| `session_start` / `session_shutdown` | `Session.ApplyRuntime` 末尾 / `Dispose` 里 |

**关键**：hook 是 `Phi` 内部代码的扩展点，不是 `Phi.Agent` 的。`Phi.Agent.Harness` / `Loop` 完全不知道扩展的存在——`Phi.SessionFactory.BuildRuntime` 负责把扩展注册的工具 + 包装后的工具喂给 harness。

```csharp
// SessionFactory.BuildRuntime
var baseTools = new BuiltInToolProvider(config.Cwd).GetTools();
var extTools = _extensionRuntime.RegisteredTools;
var allTools = baseTools.Concat(extTools).ToList();
var wrapped = allTools.Select(t => _extensionRuntime.WrapWithHooks(t)).ToArray();
var harness = new Harness(provider, wrapped, model: model, system: systemPrompt);
```

---

## 6. UI 接入（双端）

最关键的一段：扩展必须用**同一份 UI**，且**不能 import UI framework**（否则 TUI 扩展在 Avalonia 跑不了，反之亦然）。

### 6.1 `IPhiUiBridge` 协议

跟 tau 的 `UiBridge` 严格对应：

```csharp
public interface IPhiUiBridge
{
    bool HasUi { get; }

    // 通知（fire-and-forget）
    void Notify(string message, NotifyLevel level = NotifyLevel.Info);

    // 对话框（async；UI 不可用时返回 Pi-style no-op default）
    Task<string?> SelectAsync(string title,
                              IReadOnlyList<string> options,
                              TimeSpan? timeout = null);
    Task<bool> ConfirmAsync(string title,
                            string message,
                            TimeSpan? timeout = null);
    Task<string?> InputAsync(string title,
                             string placeholder = "",
                             TimeSpan? timeout = null);

    // Transcript 自定义行
    void SubmitTranscriptLine(TranscriptLine line);

    // 状态条 / 错误（Phi-specific）
    void NotifyStatus(string message);
    void FlashError(string message, bool persistent);
}
```

**实现**：
- `Phi.Tui.TuiPhiUiBridge` → 复用 `ChatTranscript`（已有 SubmitTransient / SubmitPersistent 路径）+ `PhiStatusBar`
- `Phi.Avalonia.AvaloniaPhiUiBridge` → 复用 `ChatTranscriptProjector` + `DeskLog` + `PhiStatusBar`
- `NullPhiUiBridge`（在 `Phi.Extensions` 里）→ 无 UI 时所有 dialog 返回 no-op default

### 6.2 Tool Card 双端复用

**这是 Phi 现有架构里最容易扩展的部分**——`ToolDescriptor` 已经是 UI-agnostic 的，`AvaloniaToolCardRegistry` 和 TUI 的 `ToolCardRegistry` 都是按 `name` 分派。

扩展注册：

```csharp
api.RegisterToolCard(
    "deploy",
    new ToolDescriptor(ToolKind.Generic, "deploy", "🚀"),
    renderer: args => $"deploy to {args["env"]}");
```

- `ToolDescriptor` 让双端按自己的图标集渲染（TUI emoji / Avalonia MaterialIcon，跟内置工具一致）
- `renderer` 可选；不注册就用默认 `GenericToolCardView`（Avalonia）/ TUI 默认 card——扩展工具调用行立即有可读输出
- `tool_execution_start` / `tool_execution_end` 事件把 `ToolCallLine` 完整字段暴露给扩展，扩展可订阅做"deploy 完成后在 transcript 画 ✓"等效果

### 6.3 Transcript 自定义行

扩展想塞一个"进度条"或"折叠的 build log"到 transcript，不走 tool call 路径：

```csharp
api.RegisterTranscriptLineRenderer("my-ext:progress", (line, expanded) =>
{
    var pct = line.Details?.TryGetValue("percent", out var v) == true ? (int)v : 0;
    return new ProgressLine(line.Id, pct, line.Content);
});
api.SubmitTranscriptLine(new TranscriptLine(
    "my-ext:progress",
    "Building…",
    new Dictionary<string, object?> { ["percent"] = 42 }));
```

`ChatLine` DU 加一个：

```csharp
public sealed record CustomLine(string Id, string LineType, string Content,
                                IReadOnlyDictionary<string, object?>? Details)
    : ChatLine(Id);
```

`TranscriptView`（Avalonia）/ `ChatTranscript`（TUI）按 `LineType` 调注册的 renderer 渲染。扩展不需要知道宿主是 TUI 还是 Avalonia——它提交一行，host 渲染。

### 6.4 自定义 Dialog 复用

`api.Context.Ui.SelectAsync(...)` 直接复用 `Phi.Tui.Components.PromptInput.Dialogs.cs` 和 `Phi.Avalonia.Components.ProvidersPage` 里已有的 picker 控件。**不**为扩展写新 dialog——现有 picker 视觉/交互已经够用。

---

## 7. `/reload` 与 Generation Guard

### 7.1 GenerationGuard

照搬 tau 的 `ExtensionGeneration` 思想，但用 C# 更直接：

```csharp
public sealed class ExtensionGeneration
{
    private volatile bool _alive = true;
    private string? _staleMessage;

    public bool IsAlive => _alive;
    public void Invalidate(string? reason = null)
    {
        _alive = false;
        _staleMessage ??= reason;  // 第一次赢，跟 tau 对齐
    }
    public void AssertAlive()
    {
        if (!_alive)
            throw new ExtensionError(
                _staleMessage ?? "extension generation stale after /reload");
    }
}

internal sealed class PhiApi : IPhiApi
{
    private readonly ExtensionGeneration _gen;
    public PhiApi(ExtensionRuntime runtime, string extName, ExtensionGeneration gen)
    {
        _gen = gen;
        _runtime = runtime;
        ...
    }
    public void RegisterTool(Tool tool)
    {
        _gen.AssertAlive();
        _runtime.RegisterTool(this, tool);
    }
    // 每个 public 方法第一行都是 _gen.AssertAlive()
}
```

捕获旧 `IPhiApi` 的扩展代码在 `/reload` 后调用任何方法都抛 `ExtensionError`，**绝不静默执行**。

### 7.2 `/reload` 流程

`/reload` 是 session 内的一个 action，**不是**前端的事。前端只需要：
1. 把 `/reload` 注册到 `SlashCommandCatalog`
2. 注册到 Avalonia 侧栏
3. 调 `session.ReloadAsync()`，等结果，弹 toast

```csharp
// Session
public async Task<ReloadSummary> ReloadAsync()
{
    // 1. 等当前 run 结束
    _runCts?.Cancel();
    if (_currentRunTask is not null) await _currentRunTask;

    var oldRuntime = _extensionRuntime;

    // 2. 发 session_shutdown(reason="reload")
    await oldRuntime.EmitSessionShutdownAsync(SessionLifecycleReason.Reload);

    // 3. 让所有 PhiApi 立即失效
    oldRuntime.InvalidateAllGenerations();

    // 4. 卸载所有 ALC（GC dance）
    oldRuntime.UnloadAssembliesSafely();

    // 5. 构造新 runtime
    var newRuntime = new ExtensionRuntime();
    newRuntime.DiscoverAndLoad(
        extensionPaths: _extensionPaths,
        extensionDirs: _extensionDirs,
        includeUserExtensions: _userExtensionsEnabled,
        includeProjectExtensions: _projectExtensionsEnabled);

    // 6. 把新 runtime 挂到 session
    newRuntime.SetUiBridge(_uiBridge);
    newRuntime.Bind(this);
    newRuntime.AttachHarnessListener(_harness.Subscribe);
    _extensionRuntime = newRuntime;

    // 7. 重建 harness（wrapped tools + 新 system prompt）
    var wrapped = newRuntime.WrapTools(_tools.ToArray());
    _harness.ReplaceTools(wrapped);
    RebuildSystemPrompt();

    // 8. 发 session_start(reason="reload")
    await newRuntime.EmitSessionStartAsync(SessionLifecycleReason.Reload);

    return new ReloadSummary(newRuntime.Diagnostics);
}
```

---

## 8. 跨平台

### 8.1 托管 DLL 本质跨平台

同一份 `extension.dll` 在 Windows / Linux / macOS 加载完全一样，CoreCLR 不在乎后缀名（`.dll` 在 Linux 上对 managed assembly 合法，只是约定俗成用 `.so`）。所以**托管扩展天生跨平台**。

### 8.2 Native deps 是真问题

扩展只要 `PackageReference` 带 native 二进制的包，立刻按 RID 分裂：

| 包 | Windows | Linux | macOS |
|---|---|---|---|
| `SkiaSharp` 2.x | `runtimes/win-x64/native/libSkiaSharp.dll` | `runtimes/linux-x64/native/libSkiaSharp.so` | `runtimes/osx-arm64/native/libSkiaSharp.dylib` |
| `SQLitePCLRaw.bundle_e_sqlite3` | `.dll` | `.so` | `.dylib` |
| `Magick.NET` | 同上 | 同上 | 同上 |

### 8.3 v1 决策：单 dll + bundle 二者皆可

**形态 1（v1 主推）**：纯托管单 dll

```
~/.phi/extensions/hello-tool/
└── HelloTool.dll
```

扩展作者只引用 `Phi.Extensions`（纯托管），不引任何带 native 的 NuGet 包。一套 dll 通吃三平台。

**形态 2（bundle，Sprint 3+）**：

```
~/.phi/extensions/chart-tool/
├── ChartTool.dll
├── ChartTool.deps.json
├── runtimes/
│   ├── win-x64/native/skiasharp.dll
│   ├── linux-x64/native/libskiasharp.so
│   └── osx-arm64/native/libskiasharp.dylib
└── ChartTool.runtimeconfig.json
```

`ExtensionLoadContext.Load` 拦截 native deps 解析，按 `RuntimeInformation.RuntimeIdentifier` 选对应目录。**一份 bundle 跨三平台零修改**。

文件命名约定：Phi 统一用 `.dll` 后缀（CoreCLR 不在乎，扩展作者不用为不同平台维护多个文件名）。

### 8.4 Native deps 解析

```csharp
// ExtensionLoadContext.Resolving
private Assembly? OnResolving(AssemblyLoadContext ctx, AssemblyName name)
{
    var rid = RuntimeInformation.RuntimeIdentifier; // win-x64 / linux-x64 / osx-arm64
    var nativePath = Path.Combine(_extensionDir, "runtimes", rid, "native");
    if (Directory.Exists(nativePath))
    {
        NativeLibrary.SetDllImportResolver(GetAssembly(), (libName, assembly, path) =>
        {
            var libPath = Path.Combine(nativePath,
                OperatingSystem.IsWindows() ? $"{libName}.dll" :
                OperatingSystem.IsMacOS()     ? $"lib{libName}.dylib" :
                                               $"lib{libName}.so");
            return NativeLibrary.Load(libPath);
        });
    }
    return null;  // 冒泡到默认 ALC
}
```

### 8.5 v2 才考虑

NuGet feed 分发（`dotnet phi install foo`）、自动更新、签名 + trusted publishers。

---

## 9. 安全模型

### 9.1 诚实声明

**C# 没有跟 Python / JavaScript 同等级别的"扩展沙箱"**。Java SecurityManager 但 .NET Core 故意没做，OS 级（App Sandbox / Landlock / Job Object）跨平台又难统一。Phi 的安全模型不可能像 Chrome WebExtensions 那样"扩展触不到文件系统"——更接近 npm 包 + 浏览器扩展的中间地带。

**`README` 第一句写明："Phi extensions 是任意代码，跟 npm / pip packages 一样。请只装你信任作者发布的扩展。"**

### 9.2 威胁模型

| 威胁 | 攻击向量 | 影响 |
|---|---|---|
| **T1** 恶意 user-level 扩展 | 用户被骗装了恶意 dll | 任意代码执行，无限制 |
| **T2** 恶意 project 扩展 | 仓库提交 `.phi/extensions/` 被启用 | 同 T1，攻击面更广 |
| **T3** 升级劫持 | 扩展 author 账号被盗，新版本投毒 | 同 T1 |
| **T4** 依赖混淆 | 扩展引用被劫持的 NuGet 包 | 同 T1 |
| **T5** Side effect 滥用 | 良性扩展不打招呼 `System.IO.File.WriteAllText("~/.bashrc", ...)` | 用户数据被改 |
| **T6** 偷凭证 | 扩展读 `~/.phi/credentials.json` / env vars / `~/.aws/credentials` | 凭证泄漏 |
| **T7** 网络外泄 | 扩展把 transcript / 用户文件 POST 到攻击者 | 隐私泄漏 |
| **T8** UI 替换 | 扩展挂 slot widget 冒充工具 card | 钓鱼 |

### 9.3 v1：透明 + Project Trust + 用户控制

防御 **T1 / T2 / T5 / T7 大部分**。

**a. 文档透明**：扩展 = 任意代码。`README` 写清楚，不假装是沙箱。

**b. Project Trust 流程**（已设计）：
- 默认 `<cwd>/.phi/extensions/` **不加载**
- 用户必须显式 `--project-extensions` 或 `/project-extensions on` 才启用
- 启用前 `project_trust` 事件触发；built-in / user / explicit 扩展可投 `approve` / `decline` / `defer`
- 第一次启用 status bar 弹一条 persistent 提示"已加载 N 个项目扩展，`/project-extensions off` 关闭"

**c. 扩展白名单 / 黑名单**：

```jsonc
// ~/.phi/config.json
{
  "extensions": {
    "disabled": ["malicious-ext"],
    "userExtensionsEnabled": true,
    "projectExtensionsEnabled": false
  }
}
```

**d. `/extensions` 命令**：列出已加载扩展 + 来源（user / project / explicit）+ 一键 disable。

**e. 审计日志**：

```
~/.phi/logs/extensions-{date}.log
[2026-08-20 14:32:01] [hello-tool/1.0.0] setup() called
[2026-08-20 14:32:05] [hello-tool/1.0.0] tool_call: hello({"who":"world"})
[2026-08-20 14:32:05] [hello-tool/1.0.0] notify("info", "done")
```

按扩展名分文件，明文（用户可 grep），事后追责。

**f. Phi 进程权限**：
- 文档写明"请用普通用户跑，不要 sudo / 管理员"
- 不主动请求 OS 提权
- 不主动打开网络端口（vs daemon-style 编辑器）

### 9.4 v1.5：Capability 显式声明

防御 **T5 / T6 / T7**。

```csharp
[PhiExtension(
    Name = "chart-tool",
    Version = "1.2.0",
    Capabilities = ExtensionCapability.Network | ExtensionCapability.FileSystemRead)]
public sealed class ChartTool : IPhiExtension { ... }
```

```csharp
[Flags]
public enum ExtensionCapability
{
    None               = 0,
    Network            = 1 << 0,
    FileSystemRead     = 1 << 1,
    FileSystemWrite    = 1 << 2,
    ProcessSpawn       = 1 << 3,
    SecretsRead        = 1 << 4,
    EnvironmentRead    = 1 << 5,
    ClipboardRead      = 1 << 6,
    ClipboardWrite     = 1 << 7,
    UiInteract         = 1 << 8,
    TranscriptWrite    = 1 << 9,
}
```

**落地**：
1. **API 层**：`IPhiApi` 不暴露任何危险方法。扩展想 `File.ReadAllText`，只能走 `api.ReadFile(path)`；`api.ReadFile` 检查声明 capability，未声明抛 `ExtensionError` + 写审计日志。强制走 host API。
2. **ALC 黑名单**（防御性深度）：`ExtensionLoadContext.Load` override 拦截危险 assembly，但 BCL 的 `System.IO.File` 在 `System.Runtime` 里拦不住——价值有限，不依赖。

**Manifest 签名**（v1.5 后期）：用 `System.Reflection.PortableExecutable.PEBuilder` 验证 Authenticode（Windows）/ codesign（macOS）/ GPG（Linux）。`--require-signed-extensions` 开关，开了之后未签名扩展直接拒。

### 9.5 v2：进程级隔离（真沙箱）

只在用户主动启用时生效，默认关闭——因为有性能开销和复杂度：

```
phi.exe (host, 普通用户权限)
   │
   │ NamedPipe (Windows) / Unix Socket (Linux/macOS)
   ▼
phi-ext-host.exe (扩展进程, OS sandbox)
   │
   │ 反序列化 IPhiApi 调用 → 真的执行 → 序列化结果回传
   ▼ 扩展 dll
```

**OS sandbox**：

| 平台 | 机制 | 默认 deny |
|---|---|---|
| macOS | `sandbox-exec` + entitlement profile | 网络出站、文件系统写（cwd 外）、`posix_spawn` |
| Linux | Landlock (5.13+) + seccomp + user namespaces | 同上 + 限制 syscall |
| Windows | AppContainer + Job Object + Capability SIDs | 同上 + 限制 Win32 API |

**IPC 成本**：每次 `api.ReadFile` 走 IPC 不能接受。v2 走批量 + async streaming——扩展一次性申请一组权限，host 一次性预加载好，IPC 只用在 tool call / event 边界。

**不做**（明确放弃）：
- 应用层水印 / DRM：扩展作者能写 `procdump`、读自己进程内存，所有"防泄漏"机制都会被绕过
- 自动审计扩展代码：静态分析 C# 复杂到 100x 投入产出比。靠生态（评论、举报、签名 publisher）解决
- 强制 sandbox：v2 是 opt-in，因为强制会让大量合法扩展（要访问 git、调用 gh CLI、读 K8s config）跑不起来

### 9.6 安全路线图

| 阶段 | 安全能力 | 实施成本 |
|---|---|---|
| **Sprint 0-2** | 文档透明 + project trust + `/extensions` + 审计日志 + 配置文件 disable | 低，复用现有架构 |
| **Sprint 3** | Capability 声明 + API 层强制 + `[PhiExtension]` 加 `Capabilities` 参数 + 审计拒绝的越权调用 | 中，走遍所有 API 边界 |
| **Sprint 4** | Manifest 签名验证（Authenticode / codesign / GPG）+ trusted-publishers.json + `--require-signed-extensions` | 中，签名工具链跨平台 |
| **v1.5 后期** | ALC 黑名单（防御性深度）+ 扩展自动更新 with signature | 中 |
| **v2** | 进程级隔离 + OS sandbox + IPC | 高，独立项目（4+ sprint） |

**v1 时刻意保守**：只做"跟 tau 等价"的安全 + 一两个关键增量（审计日志、disable 配置）。v1 不承诺做不到的事。

---

## 10. 目录结构

```
Phi.slnx
├── Phi.Agent/                         # agent core (无 Phi 依赖)
│   ├── Harness.cs
│   ├── AgentLoop.cs
│   ├── Messages.cs
│   ├── Tool.cs / ToolResult.cs / TypedTool.cs
│   ├── SessionEntry.cs / SessionEntryCodec.cs / SessionStorage.cs
│   └── Phi.Agent.csproj
├── Phi.Agent.Tests/
│
├── Phi.Provider/                      # provider 抽象
│   ├── IPhiProvider.cs
│   └── Phi.Provider.csproj
├── Phi.Provider.Tests/
│
├── Phi.SchemaGen/                     # source generator
│
├── Phi/ # runtime（was PhiCoding）
│   ├── Session.cs                          # was CodingSession
│   ├── SessionFactory.cs                   # was CodingSessionFactory
│   ├── ISession.cs
│   ├── SessionState.cs
│   ├── Sessions/
│   │   ├── SessionRuntime.cs               # 加 ExtensionRuntime 字段
│   │   ├── SessionFactory.cs (top-level, re-exports)
│   │   ├── ISessionNavigator.cs
│   │   ├── SessionNavigator.cs
│   │   └── WorkspaceSessionStore.cs
│   ├── Chat/
│   │   ├── ChatLine.cs                     # 加 CustomLine record
│   │   ├── ChatTranscriptProjector.cs
│   │   └── …
│   ├── Prompts/
│   │   ├── SystemPromptBuilder.cs         # Sprint 2.5+ 抽 coding 模板
│   │   ├── ToolContribution.cs
│   │   └── …
│   ├── Providers/
│   │   ├── ProviderManager.cs
│   │   ├── ProviderCatalog.cs
│   │   └── …
│   ├── Resources/
│   │   ├── SkillLoader.cs
│   │   ├── SkillValidator.cs
│   │   └── ProjectContextLoader.cs        # Sprint 2.5+ 评估移 CodingPack
│   ├── Tools/ # Sprint 2.5 前暂留，之后搬入 CodingPack
│   │   ├── BashTool.cs
│   │   ├── ReadTool.cs
│   │   ├── WriteTool.cs
│   │   ├── EditTool.cs
│   │   ├── BuiltInTools.cs
│   │   └── BuiltInToolProvider.cs
│   ├── Compaction/
│   │   ├── CompactionPlanner.cs            # 算法通用，留 Phi
│   │   ├── CompactionSummarizer.cs         # Sprint 2.5+ 抽 coding prompt 到 CodingPack
│   │   ├── FileOpsExtractor.cs             # coding-specific，Sprint 2.5 移走
│   │   └── CompactionStorage.cs
│   ├── Slash/
│   │   ├── SlashCommands.cs
│   │   └── SlashCommandCatalog.cs
│   ├── Status/
│   │   ├── SessionStatusRouter.cs
│   │   └── ErrorClassifier.cs
│   ├── ToolCards/
│   │   ├── ToolDescriptor.cs
│   │   └── ToolDescriptors.cs
│   ├── Prompt/
│   │   ├── ISuggestionProvider.cs
│   │   ├── SlashCommandProvider.cs
│   │   └── SkillSuggestionProvider.cs
│   └── Extensions/                         # Sprint 0+ 创建
│       ├── Phi.Extensions/                  # public package
│       │   ├── Phi.Extensions.csproj
│       │   ├── PhiExtensionAttribute.cs
│       │   ├── IPhiExtension.cs
│       │   ├── IPhiApi.cs
│       │   ├── IPhiContext.cs
│       │   ├── IPhiUiBridge.cs
│       │   ├── NullPhiUiBridge.cs
│       │   ├── ExtensionError.cs
│       │   ├── NotifyLevel.cs
│       │   ├── MessageDelivery.cs
│       │   ├── ExtensionCapability.cs
│       │   ├── TranscriptLine.cs
│       │   ├── Events/
│       │   │   ├── PhiEvent.cs
│       │   │   ├── AgentEvents.cs
│       │   │   ├── MessageEvents.cs
│       │   │   ├── ToolExecutionEvents.cs
│       │   │   ├── CompactionEvents.cs
│       │   │   ├── SessionEvents.cs
│       │   │   ├── LifecycleEvents.cs
│       │   │   ├── HookEvents.cs
│       │   │   ├── ToolHookEvents.cs
│       │   │   └── ProjectTrustEvents.cs
│       │   └── Rendering/
│       │       ├── IToolCardRenderer.cs
│       │       ├── TranscriptLineRenderer.cs
│       │       └── MessageRenderer.cs
│       └── Phi.Extensions.Host/             # private wiring package
│           ├── Phi.Extensions.Host.csproj
│           ├── ExtensionRuntime.cs
│           ├── ExtensionLoader.cs
│           ├── ExtensionLoadContext.cs
│           ├── LoadedExtension.cs
│           ├── DiscoveredExtension.cs
│           ├── ExtensionDiagnostics.cs
│           ├── ExtensionPaths.cs
│           ├── ExtensionGeneration.cs
│           ├── PhiApi.cs
│           ├── HookDispatch.cs
│           ├── EventDispatch.cs
│           └── ReloadSummary.cs
│
├── Phi.Tests/                              # was PhiCoding.Tests
│
├── Phi.Tui/                                # was PhiCoding.Tui
│   ├── PhiTuiApp.cs
│   ├── TuiPhiUiBridge.cs                   # Sprint 3+ implements IPhiUiBridge
│   └── Components/...
│
├── Phi.Avalonia/                           # was PhiCoding.Avalonia
│   ├── PhiAvaloniaApp.axaml(.cs)
│   ├── AvaloniaPhiUiBridge.cs              # Sprint 3+ implements IPhiUiBridge
│   └── Components/...
│
├── Phi.Avalonia.Desktop/                   # was PhiCoding.Avalonia.Desktop
│
└── Phi.Avalonia.Tests/                     # was PhiCoding.Avalonia.Tests

examples/
└── extensions/
    ├── CodingPack/                         # Sprint 2.5+：第一个"真"扩展
    ├── HelloTool/
    └── PermissionGate/
```

### CPM 一行加依赖

```xml
<!-- Directory.Packages.props 新增 -->
<PackageVersion Include="Phi.Extensions" Version="0.1.0" />
```

扩展自己的 csproj：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Phi.Extensions\Phi.Extensions.csproj" />
  </ItemGroup>
</Project>
```

---

## 11. 关键决策

| 决策 | 选择 | 理由 |
|---|---|---|
| 扩展物理形态 | 单 dll + bundle 二者皆可（v1 主推单 dll，bundle Sprint 3+） | 单 dll 跨平台零成本，bundle 留口子 |
| v1 安全深度 | 透明 + project trust + `/extensions` + 审计日志 | 跟 tau 对齐，不画饼 |
| Native deps 策略 | 每扩展独立 ALC + `runtimes/{rid}/` 优先解析 | 跟主流 .NET 插件系统对齐 |
| `IPhiApi` 接口 vs sealed class | Interface（内部 sealed `PhiApi` 实现） | 演化友好：v1 后想加方法不影响扩展 |
| Hook handlers sync / async | 两者都支持（`Func<...>` 和 `Func<..., ValueTask>`） | 跟 tau 的"sync 或 async handler"对齐 |
| 持久化 entry | `AppendEntryAsync("my-ext:state", data)` → 独立 SessionEntry，namespace 区分 | 不污染现有 transcript / tool result 流 |
| Project Trust | v1 默认 off；built-in / user / explicit 扩展能投 `approve`；extension 不能给自己投 | 跟 tau 完全对齐 |
| 一个 dll 多 `[PhiExtension]` | v1 限一个 | 简化 loader；v2 放宽 |
| **顶层命名空间** | **Phi.***（Phi / Phi.Agent / Phi.Provider / Phi.Extensions） | Phi.Agent 与 Phi 独立项目可单独分发 |
| **`CodingSession` 命名** | **完全去掉 `Coding` 前缀 → `Session`** | 跟 `ISession` 接口名字对齐 |
| **TUI/Avalonia 项目拆分** | **保持三个独立 csproj**（Phi / Phi.Tui / Phi.Avalonia） | 最小改动，最大可移植 |

---

## 12. 测试策略

### 12.1 复用现有基础设施

- `Helpers/StubProvider.cs` 不变
- 新增 `Helpers/StubPhiExtension.cs`（实现 `IPhiExtension`、记所有调用）
- 新增 `Helpers/StubPhiUiBridge.cs`（实现 `IPhiUiBridge`、记所有调用、给对话框预设返回值）

### 12.2 Sprint 0 测试（先测试后实现）

`Phi.Extensions.Tests/`：

```
ExtensionErrorTests                抛出 / 继承链
NotifyLevelEnumTests                flags 组合 / ToString
MessageDeliveryEnumTests            steer / follow_up 命名稳定
CapabilityFlagTests                 [Flags] 位运算正确，unknown 不报错
TranscriptLineTests                 record equality + 必需字段
EventRecordTests                    每个 PhiEvent 子 record 可构造 + 字段名稳定
ApiShapeTests                       反射检查 IPhiApi 公开方法集 = 固定列表
ContextShapeTests                   IPhiContext 公开属性集 = 固定列表
UiBridgeShapeTests                  IPhiUiBridge 公开方法集 = 固定列表
```

测试一上来就卡死接口形状——之后改 `IPhiApi` 都要先改测试。

### 12.3 Sprint 1+ 测试（host 包）

```
ExtensionLoaderTests                ALC 加载 + 卸载 + 依赖隔离
ExtensionRuntimeTests               注册 / 并发 dispatch / 错误隔离
HookDispatchTests                   tool_call block / 改写 / 链式 / 异常 = block
ReloadTests                         reload 后旧 PhiApi 抛 ExtensionError，新 PhiApi 可用
                                    ALC 真正卸载（WeakReference 监控）
GenerationGuardTests                reload 后旧 api / context.ui / cmd token 调用全部失败
ToolCardRegistryTests               扩展注册 descriptor 双端可见
TranscriptLineRendererTests         扩展提交自定义行按 type 路由
DiagnosticTests                     加载失败 / setup 抛错 / 类型不对 都不崩 host
AuditLogTests                       每条调用都进日志，含扩展名 + 版本
```

### 12.4 关键测试模式

**Reload 泄漏测试**（ALC 真正卸载验证）：

```csharp
[Fact]
public void Reload_UnloadsOldAlc_TrueWeakReference()
{
    var alc = new ExtensionLoadContext(extDir);
    var asm = alc.LoadFromAssemblyPath(dllPath);
    var weakAsm = new WeakReference(asm);

    alc.Unload();
    for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }

    Assert.False(weakAsm.IsAlive, "old extension assembly must be unloaded");
}
```

**GenerationGuard 测试**（reload 后旧 api 立即失效）：

```csharp
[Fact]
public async Task Reload_OldPhiApi_ThrowsExtensionError()
{
    var oldApi = runtime.GetApiForExtension("hello");
    var extension = (StubPhiExtension)oldApi;  // 捕获旧 api
    await session.ReloadAsync();
    Assert.Throws<ExtensionError>(() => extension.RegisterTool(...));
}
```

---

## 13. 重命名路线图

**核心原则：先把命名做对，再做架构重构。**

| 阶段 | 范围 | 风险 |
|---|---|---|
| **Phase 0（现在 → Sprint 0 前）** | `PhiCoding` → `Phi` 全量改名；`CodingSession` → `Session`；`PhiAgent` → `Phi.Agent`；`PhiProvider` → `Phi.Provider`；同步 `AGENTS.md` / `README.md` / `phi.slnx` / props | 极低（纯命名，行为零变化） |
| **Sprint 0** | 新建 `Phi.Extensions` / `Phi.Extensions.Host`（用新命名空间） | 低 |
| **Sprint 1-2** | loader / hooks / reload，运行时无 naming 影响 | — |
| **Sprint 2.5（关键节点）** | 抽出 `examples/extensions/CodingPack/`：把 `Phi/Tools/*.cs`（BashTool 等）+ `FileOpsExtractor` + coding system prompt 模板搬入；`Phi.Tui` / `Phi.Avalonia` 默认引用 `CodingPack`。这同时是**扩展系统第一次端到端验证**——最强的"第一个扩展" | 中（行为不变需要回归测试） |
| **Sprint 3-4** | UI bridges、TranscriptLineRenderer、Capability 启用强制 | — |

### Phase 0 重命名 checklist

```
□ PhiCoding → Phi（namespace、csproj）
□ CodingSession → Session（含 ISession 实现者）
□ CodingSessionFactory → SessionFactory
□ PhiAgent → Phi.Agent
□ PhiProvider → Phi.Provider
□ PhiSchemaGen → Phi.SchemaGen
□ PhiCoding.Tui → Phi.Tui
□ PhiCoding.Avalonia → Phi.Avalonia
□ PhiCoding.Avalonia.Desktop → Phi.Avalonia.Desktop
□ PhiCoding.*.Tests → Phi.*.Tests
□ phi.slnx：所有 project 路径更新
□ Directory.Build.props / Directory.Packages.props：包名、namespace 约束
□ AGENTS.md：架构图 + 目录约定 + 所有 PhiCoding 引用
□ README.md
□ 所有 using PhiCoding.* → using Phi.*
□ dotnet build 三平台（Win/Linux/macOS）验证
□ dotnet test 全绿
```

### Sprint 2.5 CodingPack 抽出 checklist

```
□ 新建 examples/extensions/CodingPack/ 项目（独立 csproj）
□ CodingPack 引用 Phi.Extensions，声明 [PhiExtension("coding-pack")]
□ Setup 里：
  □ RegisterTool(BashTool / ReadTool / WriteTool / EditTool)
  □ AddPromptGuideline(coding system prompt)
  □ Subscribe tool_call hook（FileOpsExtractor 改写 args，记录 read/modified 文件）
□ Phi/Tools/ 下的 BashTool.cs 等代码 → 物理移动到 CodingPack/
□ Phi 主体移除 BuiltInTools / BuiltInToolProvider
□ Phi.Tui / Phi.Avalonia：ProjectReference 增加 CodingPack
□ CodingPack 在编译期被引用（不走 file-based discovery）
□ 端到端测试：开 TUI，跑 "list files in cwd"，tool call 走 BashTool，行为完全一致
□ CodingPack 自身被 reload 时，FileOpsExtractor 状态正确清理
```

### 为什么不把 CodingPack 抽出提前到 Phase 0

技术上可以。但 CodingPack 抽出**需要 extension runtime 已经能跑通**——CodingPack 是第一个真扩展。如果 Sprint 0/1/2 还没把 extension runtime 跑通，强行抽 CodingPack 等于在没地基时盖房。**等 extension runtime 验证了，再抽 CodingPack 是最稳的**——同时验证了"最重要的扩展（默认 coding pack）能跑通"。

---

## 14. 里程碑

每个 sprint 结束都有可跑 demo + 测试覆盖。

| Sprint | 目标 | 关键交付 |
|---|---|---|
| **0** | 包骨架 + `IPhiApi` 接口定义 | `Phi.Extensions` 公开包；`IPhiExtension` / `IPhiApi` / `IPhiUiBridge` + 所有事件 record；attribute；`ExtensionError`；`ApiShapeTests` 锁死接口 |
| **1** | Loader + 第一个 `HelloTool` 端到端 | `ExtensionLoader` + ALC；`ExtensionRuntime.DiscoverAndLoad`；`SessionFactory` 注入 runtime；`HelloTool` demo（加载 + 转录可见 + tool call 可用） |
| **2** | Events + Hooks + `/reload` | `HookDispatch`（tool_call / tool_result / input）；所有 agent event 透传；`/reload` 流程 + ALC GC dance；`PermissionGate` demo；`ReloadTests` 真卸载验证 |
| **2.5** | **CodingPack 抽出（架构重构）** | `examples/extensions/CodingPack/` 第一个真扩展；搬出 BashTool/ReadTool/WriteTool/EditTool + FileOpsExtractor + coding prompt；端到端回归测试 |
| **3** | UI Bridge 双端实现 + Capability 落地 | `TuiPhiUiBridge` + `AvaloniaPhiUiBridge`；`select` / `confirm` / `input` 复用现有 picker；`Notify` 双端；`PhiStatusBar` 错误分类扩展接入；Capability attribute 启用强制；`Project Trust` v1 上线 |
| **4** | Tool Card + Transcript Line 扩展点 + Bundle 加载 | `RegisterToolCard` / `RegisterToolCardRenderer`；`RegisterTranscriptLineRenderer`；`CustomLine` 加入 `ChatLine` DU；`AvaloniaToolCardRegistry` 和 TUI registry 走 `PhiApi` 而不是静态表；ALC 解析 `runtimes/{rid}/` |

每个 sprint：
1. 先写测试，跑通再写实现
2. `dotnet test` 全绿（含现有所有测试，无回归）
3. `examples/extensions/` 加一个最小可跑 demo
4. 跑 `dotnet build` + `dotnet test` 跨 Windows / Linux / macOS 三个平台 CI

---

## 15. 收益与风险

### 收益

- **跟 tau 严格对齐**——tau 已经验证过这套架构（`tau-subagents` 是真实的 production extension），用户的概念模型零迁移。
- **复用现有 UI**——TUI / Avalonia 的所有 chrome（transcript / status bar / prompt / tool cards / slash picker）自动对扩展生效，**不需要给扩展写 UI**。
- **隔离边界干净**——ALC + `Phi.Extensions` 公开包只暴露协议，扩展不能反向引用 Phi 内部类型。
- **可卸载**——ALC + generation guard 让 `/reload` 真卸载，不留内存泄漏。
- **C# 习惯**——`IDisposable On(...)` / `record sealed` / `Task<T?>` / `IReadOnlyList<T>`，比直接翻译 Python 类型更地道。
- **跨平台零妥协**——纯托管扩展一份 dll 通吃三平台；bundle 按 RID 自动选 native deps。
- **架构命名干净**——`Phi` 是 agent host，`Phi.CodingPack` 是默认扩展，第三方扩展自然处于同一层。命名不再"coding"。

### 风险

- **ALC 卸载的 GC dance 不写好会有内存泄漏**——Sprint 2 必须重点测试（WeakReference 验证）。
- **`IPhiUiBridge` 加新方法会破坏旧扩展**——v0.x 阶段允许，v1.0 后冻结接口。
- **`Session` 要转发 14+ 个事件到 runtime**——`HookDispatch` / 转发层是事件循环里最热的路径，要 benchmark。
- **项目扩展的 trust 模型 v1 用"默认全开 + warning"**——用户安装恶意扩展的风险跟 Python 一样，要文档里写清楚。
- **扩展依赖第三方 dll 版本冲突**——ALC 的 Resolving 策略要仔细设计，避免扩展 A 用的 Newtonsoft.Json 12 干扰 host 自己的 Newtonsoft.Json 13。
- **没有真正的应用层沙箱**——v1 必须文档明确"扩展 = 任意代码"，不画饼。
- **重命名 Sprint 0 + Phase 0 工作量叠加**——先把重命名做掉再开 Sprint 0，否则 Sprint 0 写出来又要批量改 namespace。

---

## 附录 A：示例扩展（v1 `HelloTool`）

```csharp
using Phi.Extensions;
using Phi.Agent;

[PhiExtension(
    Name = "hello-tool",
    Version = "1.0.0",
    Description = "Greet someone by name.",
    Capabilities = ExtensionCapability.None)]
public sealed class HelloTool : IPhiExtension
{
    public void Setup(IPhiApi api)
    {
        api.RegisterTool(
            new HelloToolImpl(),
            new ToolContribution
            {
                PromptSnippet = "hello: Greet someone by name.",
                PromptGuidelines = ["Use hello when asked to greet someone."],
            });

        api.RegisterCommand("/hello", (args, ctx) =>
        {
            api.SubmitUserMessage($"Say hello to {args}");
            return null;
        }, description: "Say hello to someone.");
    }
}

internal sealed class HelloToolImpl : Tool
{
    public override string Name => "hello";
    public override string Description => "Greet someone by name.";
    public override JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["who"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray { "who" }
    };

    public override async Task<ToolResult> ExecuteAsync(
        string toolName, string toolCallId,
        JsonObject arguments, CancellationToken cancellationToken)
    {
        var who = arguments["who"]?.GetValue<string>() ?? "world";
        return new ToolResult(
            [new TextBlock($"Hello, {who}!")],
            IsError: false);
    }
}
```

## 附录 B：示例扩展（v1 `PermissionGate`）

```csharp
using System.Text.RegularExpressions;
using Phi.Extensions;

[PhiExtension(
    Name = "permission-gate",
    Version = "1.0.0",
    Description = "Block dangerous bash commands before they run.")]
public sealed class PermissionGate : IPhiExtension
{
    static readonly Regex[] Dangerous =
    {
        new(@"\brm\s+-(?=[a-zA-Z]*r)(?=[a-zA-Z]*f)[a-zA-Z]+", RegexOptions.Compiled),
        new(@"\bgit\s+push\s+--force", RegexOptions.Compiled),
        new(@"\bgit\s+reset\s+--hard", RegexOptions.Compiled),
        new(@"\bchmod\s+-R\s+777\b", RegexOptions.Compiled),
        new(@"\bmkfs\b", RegexOptions.Compiled),
    };

    public void Setup(IPhiApi api)
    {
        api.On("tool_call", (ev, ctx) =>
        {
            if (ev is not ToolCallHookEvent tce || tce.ToolName != "bash")
                return ValueTask.CompletedTask;

            var cmd = tce.Arguments.TryGetValue("command", out var c) ? c?.ToString() : "";
            foreach (var pattern in Dangerous)
                if (pattern.IsMatch(cmd ?? ""))
                    return ValueTask.FromResult<object?>(new ToolCallHookResult(
                        Block: true,
                        Reason: $"command matches guarded pattern `{pattern}`; ask the user to run manually"));

            return ValueTask.CompletedTask;
        });
    }
}
```