# Phi 扩展系统设计

> 状态：**Phase 0（重命名）+ 架构重构已完成**。`SessionFactory` / `SessionNavigator` /
> `SessionConfig` / `ISessionNavigator` 全部删除，扩展系统的核心点（`ISession`、
> `Session.LoadAsync`、composition root、`Phi.Avalonia.ActiveSession`）已经稳定。
> Sprint 0-2（`Phi.Extensions` / `Phi.Extensions.Host` 包骨架、loader、hooks、`/reload`）**代码已实现**
> （`ExtensionLoader` / `ExtensionRuntime` / `HookRegistry` / `EventDispatch` / `ExtensionReloader` 均已落地，
> `HelloTool` / `PermissionGate` 两个 demo 扩展齐全），但 §14 里程碑表尚未逐项勾掉——追踪状态以代码 + 测试为准，
> 表格本身有滞后，读表时留意。
> 对标实现：tau（~/github/tau）的 pi-extensions 形态，已生产验证。
>
> **本次修订对照当前代码（重构后）逐章节更新**。修改点散落在第 1 / 4 / 5 / 7 / 10 / 13 节，
> 其余章节保持不变。
>
> §17（`McpPack`）新加：MCP 作为官方扩展 `McpPack` 的设计（**不进入 Phi 主体代码**）。

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
                     ┌─────────────────────────────────────────────┐
                     │ Phi (runtime)                               │
                     │                                             │
    user prompt ─►   │ Session  ─── LoadAsync(cwd, env, …)         │ ◄── SteeringQueue / FollowUpQueue
                     │   │      (composition root, was              │
                     │   │       SessionFactory)                    │
                     │   Harness                                    │
                     │   AgentLoop                                  │ ─► HarnessEvent (TurnStart, ToolExec*, TurnEnd…)
                     │   SessionEnvironment  ◄───────────────────────│─── resolver / prompt opts / compaction
                     │   SessionRuntime        (internal, holds env) │─── injected via ApplyRuntime
                     │   │                                          │
                     │   ┌──────────────────────┐                   │
                     │   │ ExtensionRuntime      │ ◄── /reload       │
                     │   │                      │     (teardown +    │
                     │   │  ┌─ LoadedExt   │     │      re-import)   │
                     │   │  │   tools      │     │                   │
                     │   │  │   commands   │     │ ─► wrapped into IReadOnlyList<Tool>
                     │   │  │   guidelines │     │ ─► appended to SlashCommandRegistry
                     │   │  │   line render│     │ ─► fed into SystemPromptBuilder
                     │   │  │   tool cards │     │ ─► installed into AvaloniaToolCardRegistry / TUI registry
                     │   │  │   handlers   │     │                   │
                     │   │  └──────────────┘     │                   │
                     │   │                      │                   │
                     │   │  Tool wrappers       │ ─► hook tool_call / tool_result around every Tool
                     │   │  Hook dispatch       │ ─► turn_start / turn_end / tool_execution_* / session_*
                     │   │  GenerationGuard     │ ─► stale-after-/reload → ExtensionError
                     │   └──────────────────────┘                   │
                     └────────────┬────────────────────────────────┘
                                  │ IPhiUiBridge
                  ┌───────────────┴───────────────┐
                  ▼                               ▼
          Phi.Tui                          Phi.Avalonia
          TuiPhiUiBridge                   AvaloniaPhiUiBridge
                                          (uses ActiveSession for reactive binding)
```

**关键不变量**：

- `ExtensionRuntime` 是 session 的内部对象，**生命周期 = session 生命周期**，由 `SessionRuntime` 在 `Session.LoadAsync`（原 `SessionFactory.BuildRuntime`）里构造，跟随 `ApplyRuntime` 注入到 `Session`。
- `IPhiApi` 是**唯一**扩展可见的入口。session / Harness / AgentLoop 的内部状态不暴露。
- 所有 UI 都是**已存在的 UI**。扩展不构造 Visual / Control，只调接口；接口由 host 的 bridge 实现。
- TUI / Avalonia 共用同一份 `IPhiUiBridge` 协议——两边各实现一个，扩展代码完全不知道宿主是哪个。
- **`SessionEnvironment` 替代了旧的 `SessionConfig`**：它是 composition root 构造一次的**跨 session 上下文**（provider resolver / system prompt options / compaction knobs），`Session.LoadAsync` 接收它并注入到 `SessionRuntime.Environment`。Extension runtime 在 reload 时复用同一 env，避免扩展改了 env 就被覆盖。
- **Avalonia 端的 `ActiveSession`**（`src/Phi.Avalonia/ActiveSession.cs`）是 XenoAtom `State<T>` 在 Avalonia 侧的等价物——一个 current-session 容器 + `Changed` 事件。Sprint 0+ 不需要为扩展重写它，但 hooks 内部用到的 `IPhiContext.Ui` 要从 session 的 bridge 转发到 `ActiveSession.Current` 上拿到的 session（详见 §6）。

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

### 3.2 ALC 加载流程（两阶段）

> **Sprint 1 实现细节**：原设计是单步"Load 时实例化 + 调 Setup"，但 Setup 需要
> 一个 session-bound 的 `IPhiApi`（action 方法要求 session 已绑定），所以实际拆成两步：
> **Load**（无 session）只实例化并返回 `LoadedExtension`；**Initialize**（session 已 bound
> 之后）才调 `Setup`。这跟 tau 的 "setup() 在 session 创建后调" 一致。

**第一阶段：`ExtensionLoader.Load`**

```csharp
public static class ExtensionLoader
{
    public static LoadedExtension Load(string dllPath, ExtensionLoadContext alc)
    {
        // 1. loadFromAssemblyPath（不解析依赖，依赖解析到 alc.Resolving）
        var asm = alc.LoadFromAssemblyPath(dllPath);

        // 2. 找 [PhiExtension("name", ...)] attribute
        //    找不到 → ExtensionLoadDiagnostic("missing attribute")
        //    找到多个 → ExtensionLoadDiagnostic("v1 allows one per assembly")
        //    反射异常（缺 transitive deps） → 把 LoaderException 信息透传
        var (entryType, attribute) = FindEntryType(asm, dllPath);

        // 3. 实例化（try/catch 记录诊断，永不让扩展崩 host）
        IPhiExtension instance;
        try
        {
            instance = (IPhiExtension)Activator.CreateInstance(entryType)!;
        }
        catch (Exception ex)
        {
            // ActivationFailed; ALC 已 try-unload 避免泄漏
            throw new ExtensionLoadDiagnostic(
                $"failed to instantiate '{entryType.FullName}': {ex.Message}", ex);
        }

        // 注意：Load 不调 Setup。Setup 需要 IPhiApi，而 IPhiApi 需要 Session。
        return new LoadedExtension(
            attribute.Name, attribute.Version, attribute.Description,
            entryType, instance, Path.GetFullPath(dllPath), asm, alc);
    }
}
```

**第二阶段：`ExtensionRuntime.Initialize`**（session 已 bound 后调）

```csharp
public void Initialize()
{
    // 一个 session 一个 PhiContext（共享给所有 extension）
    var context = new PhiContext(_session, _uiBridge);
    foreach (var ext in _extensions)
    {
        try
        {
            // 每 extension 一个 PhiApi 实例——同样的 context，不同的
            // GenerationGuard（见 §7.1）。Sprint 2 真正实现，目前 stub。
            var api = new PhiApi(this, ext, context);
            ext.Instance.Setup(api);
        }
        catch (Exception ex)
        {
            // Setup 抛异常不杀其它 extension；写进 SetupResults audit log
            _setupResults.Add(new ExtensionSetupFailure(ext, ex));
        }
    }
}
```

Composition root 完整流程：

```csharp
// Phi.Tui/Program.cs 或 Phi.Avalonia.Desktop/Program.cs
var session = await Phi.Session.LoadAsync(cwd, env, ...);   // 阶段零：composition root
session.HasUi = true;                                          // 标志 UI 已 attached

using var runtime = new ExtensionRuntime(session, uiBridge);    // 持有 lifetime
runtime.DiscoverAndLoad(extensionPaths);                          // 阶段一：Load
runtime.Initialize();                                             // 阶段二：Setup
// runtime.Dispose() 时所有 ALC unload
```

**为什么不合并成一步**：Setup 的 IPhiContext 投影 `Session.SystemPrompt` 等字段，
而这些字段是 `Session.LoadAsync`（即 `ApplyRuntime`）之后才填充的。Load 阶段
session 还没 ready，Setup 会抛"session not bound"——所以必须分两阶段。

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

### 4.0 `IPhiContext` 与现有 `ISession` 的映射

`IPhiContext` 的字段**全部**可以从现有 `ISession` + 即将新增的 bridge 字段派生。Sprint 0+ 实现时，`PhiApi` 内部持有一个 `Session` 引用 + 一个 `IPhiUiBridge`：

| `IPhiContext` 字段 | 来源（现有 `ISession` / 新增） |
|---|---|
| `Cwd` | `ISession.Cwd`（直接有） |
| `Model` | `ISession.State.Model`（已有） |
| `ProviderName` | `ISession.State.ProviderName`（已有） |
| `SessionId` | `ISession.Id`（已有） |
| `SystemPrompt` | **需要**：`Session._systemPrompt` 当前是 `private`，新增 `ISession.SystemPrompt { get; }` |
| `IsRunning` | `ISession.State.IsRunning`（已有） |
| `HasUi` | **新增字段**（composition root 在 UI 模式下设 true，未来 headless 模式设 false） |
| `Transcript` | `ISession.State.Messages`（已有） |
| `Ui` | **新增**：`Session` 持有一个 `IPhiUiBridge _uiBridge`（UI 层在 init 时注入；详见 §6） |

**关键点**：`IPhiContext` 不是新的"session 状态字段"，而是 `ISession` 的**只读投影**。`PhiApi` 构造时拿一个 `Session` + 一个 `IPhiUiBridge`，所有 getter 转发过去。`PhiApi` 自己**不持有任何状态**——reload 时旧 `PhiApi` 立即失效（详见 §7），新 `PhiApi` 用新的 Session 重新构造。
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
| `agent_start` | `AgentStartEvent` | `Session.SubmitPrompt` 起点（`Session.RunAgentCoreAsync` 进入 try 块时） |
| `agent_end` | `AgentEndEvent { Messages, WillRetry }` | `RunAgentCoreAsync` finally 块 |
| `agent_settled` | `AgentSettledEvent` | 无 retry / compaction / queued continuation |
| `turn_start` | `TurnStartEvent { TurnIndex, TimestampMs }` | `AgentLoop.RunAgentAsync` 已发 `TurnStartEvent(Turn)`；Sprint 1+ 加 timestamp / index |
| `turn_end` | `TurnEndEvent { TurnIndex, Message, ToolResults }` | `AgentLoop` 已发 `TurnEndEvent(FinalMessage)`；扩展 payload 在 `Phi.Agent` 基础上扩 |
| `message_start` | `MessageStartEvent { Message }` | 已有（合并自 `Phi.Agent.AssistantMessageEvent` 流） |
| `message_update` | `MessageUpdateEvent { Message, AssistantMessageEvent }` | 已有 |
| `message_end` | `MessageEndEvent { Message }` | 已有 |
| `tool_execution_start` | `ToolExecutionStartEvent { ToolCallId, ToolName, Arguments }` | 已有（来自 `HarnessEvent` 流） |
| `tool_execution_update` | `ToolExecutionUpdateEvent { ... PartialResult }` | 已有（Sprint 1+ 实现） |
| `tool_execution_end` | `ToolExecutionEndEvent { ... Result, IsError }` | 已有 |
| `queue_update` | `QueueUpdateEvent { SteeringCount, FollowUpCount }` | `Session.EnqueueSteering` / `EnqueueFollowUp` 调 `UpdateQueueCount` 时 |
| `compaction_start` | `CompactionStartEvent { Reason }` | 已有 |
| `compaction_end` | `CompactionEndEvent { Reason, Result, Aborted, WillRetry, ErrorMessage }` | 已有 |
| `entry_appended` | `EntryAppendedEvent { Entry }` | `Session.AppendMessage` 后 |
| `session_info_changed` | `SessionInfoChangedEvent { SessionId?, Title, Model, ProviderName }` | `Session.SwitchModel` / `Rename` / `SubmitCustomMessage` 等改动 `State.SessionTitle/Model/ProviderName` 时 |
| `thinking_level_changed` | `ThinkingLevelChangedEvent { Level }` | Phi 暂无，留位 |
| `auto_retry_start` / `auto_retry_end` | 暂无，Phi 还没有 retry，留位 | — |
| `agent_event` | wildcard 透传 | — |

**重要：`ISession` 当前已有的事件总线**（重构后稳定）：
- `ISession.StateChanged` — 每次 `SessionState` snapshot 变化时触发（包含上面大部分"状态类"事件）。`SessionState` record 已含 `Messages / Model / ProviderName / SessionTitle / IsRunning / Stats / ContextUsedTokens / LastError / SteeringCount / FollowUpCount / SessionId / IsPersisted / AutoCompactThreshold`。
- `ISession.HarnessEvent` — 每次 harness emit 触发（覆盖 `TurnStart` / `TurnEnd` / `ToolExecution*` / `AssistantTextDelta` / `AssistantThinking*` / `AssistantToolCall` / `HarnessError` / `MessageStart/Update/End` / `CompactionStart/End` 等所有 Phi.Agent 已有的 `HarnessEvent`）。

**扩展层要做的**：在 `ExtensionRuntime` 里订阅这两个 event，把它们转成 `PhiEvent` 子 record（payload 适配），再分发给扩展的 `On(...)` handler。**不要**绕过这两个 event 自己再去监听 `Phi.Agent.Harness` 内部状态——单一源真相是 `ISession`，扩展和 host UI 都从这同一个源拿数据。

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
1. 把 `/reload` 注册到 `SlashCommandCatalog`（**当前状态**：现有 `SlashCommandCatalog.All` 是 `static readonly` list，需要在 Sprint 2 改成可变 registry —— 见 §10 / §11 关于"扩展注册命令"的接口设计）
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

> **本节为本次修订重写**——结构跟原文档有较大差异（见各模块注释）。

```
Phi.slnx
├── Phi.Agent/                              # agent core（已存在，Phase 0 完成）
│   ├── Phi.Agent.csproj                    # netstandard 风格，零 Phi 依赖
│   ├── AgentLoop.cs                        # was Loop.cs（重命名跟类名一致）
│   ├── Harness.cs
│   ├── HarnessEvent.cs
│   ├── IAgentMessage.cs                    # marker interface
│   ├── IPhiProvider.cs                     # provider 抽象，host 端实现
│   ├── Messages.cs                          # UserMessage / AssistantMessage / ToolResultMessage / ContentBlock / Usage
│   ├── ProviderEvent.cs
│   ├── PhiAgentJsonContext.cs
│   ├── SessionEntry.cs
│   ├── SessionEntryCodec.cs
│   ├── SessionStorage.cs
│   ├── Tool.cs                              # abstract
│   ├── ToolResult.cs
│   └── TypedTool.cs                         # 强类型 helper
│
├── Phi.Agent.Tests/
│
├── Phi.Provider/                           # LLM provider 实现（已存在）
│   ├── Phi.Provider.csproj
│   ├── Anthropic.cs / AnthropicConfig.cs
│   ├── Config.cs
│   ├── NullProvider.cs                     # TUI 启动时无 key 的 fallback
│   ├── OpenAICompatibleProvider.cs
│   └── ToolCallBuilder.cs
│
├── Phi.Provider.Tests/
│
├── Phi.SchemaGen/                          # 已存在：TypedTool<T> 的 source generator
│
├── Phi/                                    # runtime（已重命名为 Phi，去掉 Coding 前缀）
│   ├── Phi.Runtime.csproj                  # net10.0，引用 Phi.Agent / Phi.Provider / Phi.SchemaGen
│   │                                     # ⚠️ 不引用 ModelContextProtocol —— MCP 是 §17 McpPack 扩展的依赖，不是核心
│   ├── ISession.cs                          # ★ 重构后加了 NewSessionAsync / ResumeAsync / ListRecent / AvailableProviders / Id
│   ├── Session.cs                           # ★ 重构后 ISession 是导航 API 的承载者；生命周期 = conversation
│   ├── SessionEnvironment.cs               # ★ 新增（替换 SessionConfig）：composition root 构造一次，跨 session 共享
│   ├── SessionRuntime.cs                   # ★ 改为 internal，原来 public record + Config 字段改成 Environment
│   ├── SessionState.cs                      # immutable snapshot，StateChanged 携带
│   ├── SessionEntryConverter.cs            # agent message <-> session entry
│   ├── SessionIndex.cs                      # index.jsonl 的 reader/writer
│   ├── SessionManager.cs                    # cwd → index 的 facade
│   ├── SessionRecord.cs                     # 索引 record 类型
│   ├── SessionStorage.cs                    # JSONL transcript 读写
│   ├── SessionStats.cs / SessionStatsCalculator.cs
│   ├── WorkspaceSessionStore.cs             # 跨 cwd 扫描所有 session（用于 Avalonia 侧栏 + ResumeAsync 的 cwd 解析）
│   ├── BuiltInTools.cs                      # Sprint 2.5 拆到 CodingPack 后会消失
│   ├── PhiJsonContext.cs                    # AOT 用的 source-gen JSON context
│   │
│   ├── Compaction* (root-level files — 不是子目录)
│   │   ├── CompactionPlanner.cs
│   │   ├── CompactionPlan.cs
│   │   ├── CompactionStorage.cs
│   │   ├── CompactionSummarizer.cs          # Sprint 2.5+ prompt 部分搬到 CodingPack
│   │   ├── ContextWindow.cs
│   │   ├── FileOpsExtractor.cs              # coding-specific，Sprint 2.5 移 CodingPack
│   │   └── OverflowDetector.cs
│   │
│   ├── Chat/
│   │   ├── ChatLine.cs                      # ★ Sprint 4+ 加 CustomLine record（extensions 提交自定义行）
│   │   └── ChatTranscriptProjector.cs        # TUI / Avalonia 都订阅它的 Changed
│   │
│   ├── Prompt/
│   │   ├── ISuggestionProvider.cs
│   │   ├── PromptPickers.cs                 # Phi.Avalonia 的 workspace / model picker 复用这里
│   │   ├── SkillSuggestionProvider.cs
│   │   ├── SlashCommandProvider.cs          # ★ Sprint 2 改成从 SlashCommandRegistry 读，扩展注册的命令自然出现
│   │   └── SuggestionItem.cs
│   │
│   ├── Prompts/
│   │   ├── BuiltInToolProvider.cs           # 把 BashTool / ReadTool / WriteTool / EditTool 包成 ToolContribution
│   │   ├── ISystemPromptBuilder.cs
│   │   ├── ProjectContextFile.cs
│   │   ├── ShellKind.cs
│   │   ├── SkillDescriptor.cs
│   │   ├── SystemPromptBuildContext.cs
│   │   ├── SystemPromptBuilder.cs           # Sprint 2.5+ 抽 coding 模板到 CodingPack
│   │   ├── SystemPromptOptions.cs
│   │   ├── ToolCapabilities.cs
│   │   └── ToolContribution.cs              # ★ 已有，扩展注册 tool 时直接复用
│   │
│   ├── Providers/
│   │   ├── ProviderManager.cs               # ★ 重构后：catalog 转发拆走、CreateProvider 改 static、SuppressMessage 删掉
│   │   ├── ProviderCatalog.cs               # static list，扩展在这里追加要重 build，不走 extension discovery
│   │   ├── ProviderCatalogEntry.cs / ProviderKind.cs
│   │   ├── IProviderResolver.cs            # SessionEnvironment.ProviderResolver 走这个
│   │   ├── ICredentialStore.cs / FileCredentialStore.cs / PhiSettings.cs
│   │
│   ├── Resources/
│   │   ├── SkillLoader.cs / SkillValidator.cs
│   │   └── ProjectContextLoader.cs          # Sprint 2.5+ 评估移 CodingPack
│   │
│   ├── Slash/
│   │   ├── SlashCommands.cs
│   │   └── SlashCommandCatalog.cs          # ★ Sprint 2 改成可变 registry（当前 static readonly list，扩展无法注册）
│   │
│   ├── Status/
│   │   ├── ErrorClassifier.cs
│   │   ├── ISessionStatusSink.cs
│   │   └── SessionStatusRouter.cs           # 现状 → 扩展的 error 转译入口
│   │
│   ├── ToolCards/
│   │   ├── ToolDescriptor.cs                # ★ 已有：Kind + Title + IconKey，跟 tau 一致
│   │   └── ToolDescriptors.cs               # 内置工具的 descriptor 表
│   │
│   ├── Tools/                              # Sprint 2.5 前暂留，之后搬入 CodingPack
│   │   ├── BashTool.cs / ReadTool.cs / WriteTool.cs / EditTool.cs
│   │   ├── ToolComposer.cs
│   │   ├── IWorkspacePathResolver.cs / WorkspacePathResolver.cs
│   │   └── Details/                         # typed tool args（ReadDetails / EditDetails 等）
│   │
│   └── Extensions/                         # ★ Sprint 0+ 新建（本节剩余部分都未开工）
│       ├── Phi.Extensions/                  # public package（net10.0）
│       │   ├── Phi.Extensions.csproj
│       │   ├── PhiExtensionAttribute.cs
│       │   ├── IPhiExtension.cs
│       │   ├── IPhiApi.cs / IPhiContext.cs / IPhiUiBridge.cs
│       │   ├── NullPhiUiBridge.cs            # headless 模式（CI / 自动化）默认实现
│       │   ├── ExtensionError.cs
│       │   ├── NotifyLevel.cs / MessageDelivery.cs
│       │   ├── ExtensionCapability.cs        # v1.5 启用强制；v1 attribute 留位
│       │   ├── TranscriptLine.cs             # ★ 新增：扩展提交自定义 transcript 行的载体
│       │   ├── Events/
│       │   │   ├── PhiEvent.cs
│       │   │   ├── AgentEvents.cs            # 5.1 表里的 agent_start / turn_* 等
│       │   │   ├── MessageEvents.cs / ToolExecutionEvents.cs
│       │   │   ├── CompactionEvents.cs
│       │   │   ├── SessionEvents.cs          # session_info_changed 等
│       │   │   ├── LifecycleEvents.cs        # session_start / session_shutdown / project_trust
│       │   │   ├── HookEvents.cs             # input hook
│       │   │   └── ToolHookEvents.cs         # tool_call / tool_result hook
│       │   └── Rendering/
│       │       ├── IToolCardRenderer.cs
│       │       ├── TranscriptLineRenderer.cs
│       │       └── MessageRenderer.cs
│       │
│       └── Phi.Extensions.Host/             # private wiring package（host 自己用，不发给扩展）
│           ├── Phi.Extensions.Host.csproj
│           ├── ExtensionRuntime.cs          # session 内部对象，生命周期 = session 生命周期
│           ├── ExtensionLoader.cs / ExtensionLoadContext.cs
│           ├── LoadedExtension.cs / DiscoveredExtension.cs
│           ├── ExtensionDiagnostics.cs / ExtensionPaths.cs
│           ├── ExtensionGeneration.cs        # GenerationGuard 实现
│           ├── PhiApi.cs                     # internal sealed：host-side IPhiApi impl（不发给扩展）
│           ├── PhiContext.cs                 # internal sealed：host-side IPhiContext impl（不发给扩展）
│           ├── HookDispatch.cs / EventDispatch.cs
│           ├── ExtensionEntryStore.cs        # AppendEntryAsync 持久化 pipeline
│           ├── ReloadSummary.cs
│           └── UI/
│               ├── TuiPhiUiBridge.cs         # 实现 IPhiUiBridge，包装 PhiStatusBar / ChatTranscript
│               └── AvaloniaPhiUiBridge.cs    # 实现 IPhiUiBridge，包装 DeskLog / ChatTranscriptProjector / PhiStatusBar / ActiveSession
│
├── Phi.Tests/
│   ├── Helpers/
│   │   ├── MockSession.cs                   # ISession 的 mock（重构后加了 OnNewSession / OnResume / NewSessionCalls）
│   │   ├── StubProvider.cs / AllKeysCredentialStore.cs
│   │   ├── TestSessionFactory.cs            # ★ 重构后新增：测试用 SessionEnvironment + Session.LoadAsync
│   │   ├── StubPhiExtension.cs              # Sprint 1+ 新增
│   │   └── StubPhiUiBridge.cs               # Sprint 1+ 新增
│   ├── SessionSwitchTests.cs                # ★ 重构后新增（合并了原 SessionFactoryTests + SessionNavigatorTests）
│   └── SessionTests.cs / SessionRuntimeTests.cs / SessionModelSwitchTests.cs
│       / SessionCompactionTests.cs / ...
│
├── Phi.Tui/
│   ├── PhiTuiApp.cs                         # ★ 重构后只剩 (ISession, ProviderManager) 两个 ctor 参数
│   ├── Program.cs                           # ★ 重构后构造 SessionEnvironment + Session.LoadAsync
│   ├── Components/
│   │   ├── PromptInput.cs                   # ★ 重构后用 SessionReplaced event 通知 shell
│   │   ├── PromptInput.Dialogs.cs            # /connect / /models / /sessions dialogs（PhiStatusBar 也在这里）
│   │   ├── ChatTranscript.cs / ChatHeader.cs / PhiStatusBar.cs
│   │   ├── ToolCards/                       # XenoAtom 实现，含 ToolCardRegistry（static）
│   │   └── …
│   ├── SelectionCopyHost.cs / ToastHostSentinel.cs / SystemClipboard.cs
│   └── TuiPhiUiBridge.cs                    # Sprint 3+ 新增
│
├── Phi.Avalonia/                           # ★ 新增：ActiveSession.cs（XenoAtom State<T> 等价物）
│   ├── ActiveSession.cs                    # ★ 已有：Avalonia 端 session 容器 + Changed 事件
│   ├── PhiAvaloniaApp.axaml(.cs)           # ★ 重构后 ctor 接 (ActiveSession, ProviderManager)
│   ├── MainWindow.cs                        # ★ 重构后接 ActiveSession
│   ├── ShellView.cs                         # ★ 重构后监听 ActiveSession.Changed 而不是 navigator.SessionChanged
│   ├── ShellLayout.axaml / ChatPageView.cs / ChatPageLayout.axaml
│   ├── NavModel.cs / AvaloniaTheme.cs / DeskLog.cs
│   ├── Components/
│   │   ├── PromptInputView.cs               # ★ 重构后接 ISession + ActiveSession（不再有 ISessionNavigator）
│   │   ├── TranscriptView.cs / ChatTranscriptProjector.cs
│   │   ├── ProvidersPage.cs / ProvidersPageLayout.axaml / ProviderRowView.axaml
│   │   ├── ToolCards/                       # 含 AvaloniaToolCardRegistry（static，目前跟 ToolCardRegistry 一样是 static）
│   │   └── …
│   └── AvaloniaPhiUiBridge.cs               # Sprint 3+ 新增
│
├── Phi.Avalonia.Desktop/
│   └── Program.cs                           # ★ 重构后：构造 SessionEnvironment + Session.LoadAsync + new ActiveSession(session)
│
└── Phi.Avalonia.Tests/
    ├── Helpers/
    │   ├── MockSession.cs / StubProvider.cs / AllKeysCredentialStore.cs
    │   └── AvaloniaTestHost.cs
    ├── ShellViewTests.cs                    # ★ 重构后用 ActiveSession 而不是 navigator
    ├── ChatPageViewTests.cs / PromptInputViewTests.cs / ProvidersPageTests.cs / ...
    └── ...
```

extensions/                                # 顶层目录，跟 src/ 平级——不是 examples，是随 Phi 一起编译/分发的扩展
├── CodingPack/                          # Sprint 2.5+：第一个"真"扩展，Phi.Tui / Phi.Avalonia 编译期默认引用
├── HelloTool/                           # 附录 A，纯教学 demo（不被默认引用）
├── PermissionGate/                      # 附录 B，纯教学 demo（不被默认引用）
├── MultiAgentPack/                      # Sprint 5+：官方参考扩展，演示 multi-agent 模式（§16）
└── McpPack/                             # Sprint 6+：官方 MCP 客户端扩展（§17）
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
| **官方 Multi-Agent 参考扩展** | **在 `extensions/MultiAgentPack/` 单独维护**（不是 Phi 主体代码）；采用 "subagent as tool" 模式（§16）；证明 `IPhiApi` 原语足够，**不需要新增任何 `IPhiApi` 方法** | 锁住"subagent 是扩展能力不是 session 抽象"——避免以后有人污染 `ISession` 加 `SubAgents` |
| **最小核心理念** | **核心 = "能跟 LLM 聊天 + 能写代码"**（Session + Harness + 4 个 builtin tools）。其它一切（MCP、multi-agent、permission gate、domain 集成、自定义 UI 卡片）**都通过扩展启用**，用户不装就不付代价 | 保持 `Phi.Runtime.csproj` 简洁；让"Phi 是什么"对所有用户一致；避免"我装的 Phi 跟你的不一样"这种 CI/可复现性问题 |
| **MCP 通过官方 McpPack 扩展提供（不进入 Phi 主体代码）** | `extensions/McpPack/` 装上就用；用户配 `~/.phi/mcp-servers.json` 接入任何 MCP server；不想要 MCP 的用户完全不付这个代价。**专用服务扩展（figma / aws / notion / ...）由社区或 Phi 团队后续按需写**，不阻塞扩展平台本身的发布 | 跟 CodingPack / MultiAgentPack 平级；锁定"MCP 是生态集成能力不是核心能力"——以后有人说"我们 Phi 应该支持 MCP server"时直接指 §17 |

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

| 阶段 | 范围 | 风险 | 状态 |
|---|---|---|---|
| **Phase 0** | `PhiCoding` → `Phi` 全量改名；`CodingSession` → `Session`；`PhiAgent` → `Phi.Agent`；`PhiProvider` → `Phi.Provider`；同步 `AGENTS.md` / `README.md` / `phi.slnx` / props | 极低（纯命名，行为零变化） | **✅ 已完成** |
| **Phase 0.5** | 架构清理（最近一次大重构）：删除 `SessionNavigator` / `SessionFactory` / `SessionConfig` / `ISessionNavigator`；新增 `ISession.NewSessionAsync` / `ResumeAsync` / `ListRecent` / `AvailableProviders`；新增 `SessionEnvironment` 替代 `SessionConfig`；新增 `Session.LoadAsync(cwd, env, ...)` 替代 `SessionFactory.Create/Resume`；Avalonia 端新增 `ActiveSession` 作为 XenoAtom `State<T>` 等价物 | 中（33+ 文件改动，838 测试全绿） | **✅ 已完成** |
| **Sprint 0** | 新建 `Phi.Extensions` / `Phi.Extensions.Host`（用新命名空间） | 低 | ⏳ 未开工 |
| **Sprint 1-2** | loader / hooks / reload，运行时无 naming 影响 | — | ⏳ 未开工 |
| **Sprint 2.5（关键节点）** | 抽出 `extensions/CodingPack/`：把 `Phi/Tools/*.cs`（BashTool / ReadTool / WriteTool / EditTool）+ coding system prompt 搬入；`Phi.Tui` / `Phi.Avalonia` 默认引用 `CodingPack`。这同时是**扩展系统第一次端到端验证**——最强的"第一个扩展"。**实际偏差**：FileOpsExtractor 留 Phi 主体（它是压缩离线分析，非 hook），详见 §13 checklist 的"实际偏差" | 中（行为不变需要回归测试） | ✅ 已完成 |
| **Sprint 3-4** | UI bridges、TranscriptLineRenderer、Capability 启用强制 | — | ⏳ 未开工 |

### Phase 0 重命名 checklist（已完成 ✅）

```
✅ PhiCoding → Phi（namespace、csproj）
✅ CodingSession → Session（含 ISession 实现者）
✅ CodingSessionFactory → SessionFactory（已删除）
✅ PhiAgent → Phi.Agent
✅ PhiProvider → Phi.Provider
✅ PhiSchemaGen → Phi.SchemaGen
✅ PhiCoding.Tui → Phi.Tui
✅ PhiCoding.Avalonia → Phi.Avalonia
✅ PhiCoding.Avalonia.Desktop → Phi.Avalonia.Desktop
✅ PhiCoding.*.Tests → Phi.*.Tests
✅ phi.slnx：所有 project 路径更新
✅ Directory.Build.props / Directory.Packages.props：包名、namespace 约束
✅ AGENTS.md：架构图 + 目录约定 + 所有 PhiCoding 引用
✅ README.md
✅ 所有 using PhiCoding.* → using Phi.*
✅ dotnet build 三平台（Win/Linux/macOS）验证
✅ dotnet test 全绿（838/838 通过）
```

### Phase 0.5 checklist（架构清理，已完成 ✅）

```
✅ 删除 src/Phi/Sessions/ISessionNavigator.cs
✅ 删除 src/Phi/Sessions/SessionNavigator.cs
✅ 删除 src/Phi/Sessions/SessionFactory.cs
✅ 删除 src/Phi/SessionConfig.cs
✅ 删除 src/Phi/Sessions/SessionRuntime.cs（重建到 Phi/ 根并改为 internal）
✅ 删除空目录 src/Phi/Sessions/
✅ 删除 tests/Phi.Tests/Helpers/FakeSessionNavigator.cs
✅ 删除 tests/Phi.Avalonia.Tests/Helpers/FakeSessionNavigator.cs
✅ ISession 加成员：Id / NewSessionAsync / ResumeAsync / ListRecent / AvailableProviders
✅ Session 加 SessionEnvironment? 字段（持久化测试会话为 null，full-composition 会话非 null）
✅ Session.Create / Session.Resume 增 SessionEnvironment? 可选参数
✅ 新增 Session.LoadAsync(cwd, env, providerName, model, resumeId?) 作为 composition root 唯一入口
✅ Session.BuildRuntime 私有 static：从 env + provider 装出 SessionRuntime
✅ Session.NewSessionAsync / ResumeAsync 走 WaitUntilIdleAsync → LoadAsync → Dispose
✅ SessionEnvironment 公开 record + Default(providerResolver) 工厂
✅ 删除 SessionFactory.Create 中冗余的 cancellationToken 参数（dead parameter）
✅ Phi.Tui：PhiTuiApp ctor 改 (ISession, ProviderManager)；PromptInput 用 SessionReplaced event
✅ Phi.Tui：Program.cs 构造 SessionEnvironment.Default + Session.LoadAsync
✅ Phi.Avalonia：新增 ActiveSession（XenoAtom State<T> 等价物）
✅ Phi.Avalonia：ShellView / ChatPageView / PromptInputView 改吃 ActiveSession
✅ Phi.Avalonia.Desktop：Program.cs 构造 Session + new ActiveSession(session)
✅ Phi.Avalonia：PromptInputView.SwitchWorkspace 调 session.NewSessionAsync + active.Replace
✅ 测试：SessionSwitchTests.cs 合并了 SessionFactoryTests + SessionNavigatorTests 的覆盖
✅ 测试：TestSessionFactory.cs 提供测试用 SessionEnvironment + LoadAsync
✅ 测试：MockSession 加 OnNewSession / OnResume / NewSessionCalls
✅ 测试：PhiTuiAppTests / PromptInputProviderTests / ShellViewTests / ChatPageViewTests / PromptInputViewTests 全部迁完
✅ AGENTS.md：架构图 + 目录约定更新（移除 Sessions/ 子目录引用）
✅ dotnet test 838/838 通过
```

### Sprint 2.5 CodingPack 抽出 checklist（已完成 ✅）

```
✅ 新建 extensions/CodingPack/ 项目（独立 csproj，引用 Phi.Extensions + Phi.Agent + Phi.SchemaGen analyzer + DiffPlex）
✅ CodingPack 声明 [PhiExtension("coding-pack")]，设 Capabilities = FileSystemRead | FileSystemWrite | ProcessSpawn
✅ Setup 里：
  ✅ RegisterTool(BashTool / ReadTool / WriteTool / EditTool) ← Tool 类型已搬到 CodingPack/Tools/
  ✅ AddPromptGuideline(coding system prompt) ← CodingPackExt.Setup 注入行为规则
  ⚠️ tool_call hook 的 FileOpsExtractor 没搬 —— 见下方"实际偏差"
✅ Phi/Tools/ 下的 BashTool.cs / ReadTool.cs / WriteTool.cs / EditTool.cs / Details/ → 物理移动到 CodingPack/Tools/
✅ Phi 主体移除 BuiltInTools.cs（不再内置工具列表）
✅ Phi 主体移除 BuiltInToolProvider.cs
✅ Phi.Tui / Phi.Avalonia / Phi.Avalonia.Desktop：ProjectReference 增加 CodingPack
✅ CodingPack 在编译期被引用（不走 file-based discovery，用 ExtensionRuntime.RegisterCompiledExtension）
✅ 端到端测试：CodingPackIntegrationTests 验证 4 个 tool 进 harness + write/read 可调用
✅ dotnet test 903/903 通过
✅ 补充调整：`extensions/` 提到仓库顶层（跟 `src/` 平级，不再挂在 `examples/` 下）——CodingPack 是编译期默认引用的组件，
  跟 HelloTool/PermissionGate 这类纯教学 demo 性质不同；`examples/` 目录随之删除
✅ 补充调整：CodingPack 的 `RootNamespace` / `AssemblyName` 统一为 `Phi.Extensions.CodingPack`，
  跟 HelloTool（`Phi.Extensions.HelloTool`）/ PermissionGate（`Phi.Extensions.PermissionGate`）的命名约定对齐
✅ 修复：`RegisterCompiledExtension` 最初只在 `Program.cs` 里围绕**启动时的第一个** `Session` 调用一次——
  `/new` / `/sessions`（`ISession.NewSessionAsync` / `ResumeAsync`）会经 `LoadAsync` 造出全新的 `Session`，
  但没有任何东西对新 session 重新调用 `RegisterCompiledExtension`，于是切换/恢复会话后 CodingPack 的
  四个工具全部消失（`CodingPackIntegrationTests` 当时只测了单个 fresh session，没覆盖这条路径，因而放过了这个回归）。
  修复方案：`SessionEnvironment` 新增 `ExtensionRuntimeFactory`（`Func<Session, IDisposable>?`）——组合根把
  "造 ExtensionRuntime + RegisterCompiledExtension(CodingPackExt) + Initialize" 包成一个闭包放进 env，
  `Session.LoadAsync` 在 `ApplyRuntime` 之后自动调用它并把返回的句柄存起来、随 `Session.Dispose()` 一起释放。
  因为 `NewSessionAsync` / `ResumeAsync` 都是复用同一个 `env` 重新走 `LoadAsync`，CodingPack 从此在
  每一个会话（不只是第一个）上都会自动注册。见 `CodingPack_Survives_NewSessionAsync` 回归测试。
  这也是 Sprint 1 里"`Session.LoadAsync` 注入 runtime"设计提前在 Sprint 2.5 落地的部分——之所以没有让
  `Session`/`Phi` 核心直接引用 `Phi.Extensions.Host.ExtensionRuntime`，是因为 `Phi.Extensions.Host` 反过来
  引用 `Phi`（核心），直接引用会成环；`Func<Session, IDisposable>` 这层不透明委托刻意避开了这个环。
```

**实际偏差**：

- **FileOpsExtractor 留在 Phi 主体**（`src/Phi/FileOpsExtractor.cs`，未搬）。它其实不是运行时 tool_call hook —— 是**压缩 pipeline 的离线分析**（读历史 `AssistantMessage.ToolCalls` 提取 read/modified 文件路径，供 compaction 摘要用）。`Session.cs` 在压缩时直接调用它，而 Phi 主体不能反向引用 CodingPack（循环依赖）。文档 checklist 误把它当 hook；实际它是压缩内部实现。它编码的 `read/write/edit` tool 名耦合留待 Sprint 4（扩展 tool 元数据）解耦。
- **CodingPack 用 `RegisterTool` 注册工具**，但 harness 在 `LoadAsync` 时已构建（无 tool）。工具通过 `Session.RegisterExtensionTool` 在 `ApplyRuntime` 之后加进 harness。system prompt 的 available-tools 段因此为空 —— 但**不影响工具可用性**：provider 通过独立的 `tools` 参数把 tool schema 发给模型（`provider.StreamResponseAsync(model, system, messages, tools, ...)`），模型能看到 schema 并调用。available-tools 段只是描述性文字，缺失不影响功能（这是扩展化的固有结果）。
- **CodingPack 需要自己的 AOT context**：`ToolDetails`（Details 序列化）从 Phi 的 `PhiJsonContext` 改用 CodingPack 的 `CodingPackJsonContext`；SchemaGen 硬编码的 `Phi.ToolArgsJsonContext` 由 CodingPack 在自己 assembly 里定义同名 internal context（不同 assembly 不冲突）。
- **`ToolDescriptors` 留在 Phi.Agent**（前端 `ChatTranscriptProjector` 在 Phi 主体调用它渲染 ToolCallLine，Phi 不能引用 CodingPack）。Sprint 4 改成扩展注册。

**为什么 RegisterTool 后 system prompt 的 available-tools 段空了还是能跑**：模型对工具的感知来自两处 —— (1) provider 的 `tools` 参数（工具 schema，必传，模型靠它知道签名），(2) system prompt 的描述段（可选增强）。CodingPack 注册后 harness 的 `_tools` 有 4 个工具，provider 把 schema 发给模型，所以工具可用。AddPromptGuideline 补了行为规则。

### 为什么不把 CodingPack 抽出提前到 Phase 0 或 0.5

技术上可以。但 CodingPack 抽出**需要 extension runtime 已经能跑通**——CodingPack 是第一个真扩展。如果 Sprint 0/1/2 还没把 extension runtime 跑通，强行抽 CodingPack 等于在没地基时盖房。**等 extension runtime 验证了，再抽 CodingPack 是最稳的**——同时验证了"最重要的扩展（默认 coding pack）能跑通"。

### 为什么 Phase 0.5 必须先于 Sprint 0 完成

`Phi.Extensions.Host` 里要写 `ExtensionRuntime.SetHarnessListener(harness.Subscribe)` 之类的桥接代码。如果 `Session` 还是"composition root 构造 SessionFactory → SessionFactory.BuildRuntime → ApplyRuntime"的多步模型，扩展 runtime 接入点会是 4 处。重构后的"composition root 一句话 `Session.LoadAsync(cwd, env, ...)`"**只**需要 1 处注入点（`SessionRuntime.Environment` 已经有 env）。**先把 composition 路径收敛，再写扩展**，代码量少一半、bug surface 少一半。

---

## 14. 里程碑

每个 sprint 结束都有可跑 demo + 测试覆盖。

| Sprint | 目标 | 关键交付 | 状态 |
|---|---|---|---|
| **Phase 0** | 命名重命名 | 所有 `PhiCoding` / `CodingSession` 等命名清理；AGENTS.md / README.md / phi.slnx / props 同步 | ✅ |
| **Phase 0.5** | 架构清理（删除 navigator/factory/config，重写 composition 路径） | `ISession` 加导航 API；`SessionEnvironment` + `Session.LoadAsync`；Avalonia `ActiveSession`；33+ 文件改动，838 测试 | ✅ |
| **0** | 包骨架 + `IPhiApi` 接口定义 | `Phi.Extensions` 公开包；`IPhiExtension` / `IPhiApi` / `IPhiUiBridge` + 所有事件 record；attribute；`ExtensionError`；`ApiShapeTests` 锁死接口 | ✅ |
| **1** | Loader + 第一个 `HelloTool` 端到端 | `ExtensionLoader` + ALC；`ExtensionRuntime.DiscoverAndLoad`；`Session.LoadAsync` 通过 `SessionEnvironment.ExtensionRuntimeFactory` 注入 runtime（见 Sprint 2.5 checklist 的"修复"条目）；`HelloTool` demo（加载 + 转录可见 + tool call 可用） | ✅ |
| **2** | Events + Hooks + `/reload` | `HookDispatch`（tool_call / tool_result / input）；所有 agent event 透传；`ExtensionReloader` + ALC GC dance；`PermissionGate` demo；`ReloadTests` 真卸载验证。`/reload` 已注册为 TUI slash 命令（`PromptInput.HandleInput`），调 `ISession.ReloadExtensions()`：内部 `RemoveExtensionTools`（释放 harness 对旧 assembly 的强引用） + dispose 旧 runtime + 重跑 `env.ExtensionRuntimeFactory` 重建 CodingPack 等 compiled extensions。回归测试 `CodingPack_Survives_ReloadExtensions` + `ReloadExtensions_WithoutEnv_Throws_LeavesSessionUsable` 锁死语义。**Avalonia 端 `/reload` 未接线**：`PromptInputView.HandleInput` 当前所有输入都走 `SubmitPrompt`，slash 调度器还是空骨架（`/new`/`/sessions` 等也没接）—— Avalonia 端 slash dispatcher 统一留到 Sprint 3 的 `AvaloniaPhiUiBridge` 一起做 | ✅ |
| **2.5** | **CodingPack 抽出（架构重构）** | `extensions/CodingPack/` 第一个真扩展；搬出 BashTool/ReadTool/WriteTool/EditTool + coding prompt；端到端回归测试（`CodingPackIntegrationTests`，含 `CodingPack_Survives_NewSessionAsync` + `CodingPack_Survives_ReloadExtensions`）；906/906 测试 | ✅ |
| **3** | UI Bridge 双端实现 + Capability 落地 | `TuiUiSink` + `AvaloniaUiSink` 实现 `IUiSink`；`PhiUiBridge` 用 lazy `Func<IUiSink>` accessor 转发，session 切换后自动重指新 sink；`select` / `confirm` / `input` 走 `TuiDialogShower` / `AvaloniaUiSink.ShowDialogAsync`；**TuiDialogShower** 通过 `Dispatcher.InvokeAsync` 跨线程 marshal，避免 hook 跨 async 边界调 dialog 时的 `Invalid thread access`；`Notify` / `NotifyStatus` / `FlashError` 双端接线；`HookRegistry.ContextProvider` 让 hook handler 拿到真 context（PermissionGate 接 ConfirmAsync 演示）；`/reload` TUI 是 slash 命令、Avalonia 是 session row 的 EllipsisMenu 项（跟 `Rename` / `Delete` 同位，遵循"Avalonia 不用 slash"约定）；`PermissionGate` demo 升级成「问用户」语义，UI 缺失时 fall back 到自动 block（与 Sprint 2 兼容） | ✅（UI Bridge 完成 + Capability Enforce + Project Trust v1） |
| **4** | Tool Card + Transcript Line 扩展点 + Bundle 加载 | `RegisterToolCard` / `RegisterToolCardRenderer`；`RegisterTranscriptLineRenderer`；`CustomLine` 加入 `ChatLine` DU；`AvaloniaToolCardRegistry` 和 TUI registry 走 `PhiApi` 而不是静态表；ALC 解析 `runtimes/{rid}/` | ⏳ |
| **5** | **官方 Multi-Agent 参考扩展 `MultiAgentPack`** | `extensions/MultiAgentPack/` 第一个完整 demo 扩展，演示 "subagent as tool" 模式（§16）；含端到端测试；作为其它扩展的参考样板 | ⏳ |
| **6** | **官方 MCP 客户端扩展 `McpPack`**（§17） | `extensions/McpPack/`；读 `~/.phi/mcp-servers.json`；stdio + HTTP/SSE transport；`tools/list` → `api.RegisterTool`；`On("session_start"/"reload")` 管理 server 生命周期；端到端测试（mock JSON-RPC server）；**证明 MCP 完全不需要进入 Phi 主体代码** | ⏳ |

每个 sprint：
1. 先写测试，跑通再写实现
2. `dotnet test` 全绿（含现有所有测试，无回归）
3. `extensions/` 加一个最小可跑 demo
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
- **架构命名干净**——`Phi` 是 agent host，`Phi.Extensions.CodingPack` 是默认扩展，第三方扩展自然处于同一层。命名不再"coding"。

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

---

## §16 官方 Multi-Agent 参考扩展 (`MultiAgentPack`)

> **定位**：本节展示如何用现有 `IPhiApi` 原语（**零新增**）构建一个能用的 multi-agent
> 系统。代码就是 Phi 仓库 `extensions/MultiAgentPack/` 的最终形态。
> 其它扩展作者可以直接复制本节作为起点。

### 16.1 为什么需要"官方"扩展

Sprint 4 之后，扩展系统的每个原语（`RegisterTool` / `AddPromptGuideline` / `SubmitTranscriptLine` /
`On<>`）都已经在 HelloTool 和 PermissionGate 里用过。**但都没有展示子会话编排**。

`MultiAgentPack` 是 Phi 项目官方维护的"参考实现"，目的是：

1. **验证 API 完备性**——multi-agent 是公认的复杂用例；如果现有 API 写不动，
   就暴露了需要补的接口。结论：**全部能写，不需要补**。
2. **为社区树立样板**——其它人写自己的 multi-agent 扩展时直接抄。
3. **可装可用**——用户装上就把自己的 Phi 变成 multi-agent。

### 16.2 模式：`Subagent as Tool`

```
用户输入: "调研 X 技术选型，然后给 Phi 提一个集成方案"
   │
   ▼
Main Agent (Session A, 全套 builtin tools + delegate tool)
   │
   │ think → 并行调 3 次 delegate
   │
   ├──► tool_call: delegate(agent="explorer", prompt="查 Phi 当前 provider 管理...")
   │      │
   │      │ DelegateTool 内部:
   │      │   1. Session.LoadAsync(cwd, envExplorer) → childExplorer
   │      │   2. childExplorer.SubmitPrompt(prompt)
   │      │   3. 等到 IsRunning=false
   │      │   4. 取 State.Messages 最后一个 AssistantMessage.Text
   │      │   5. childExplorer.Dispose()
   │      │   6. return ToolResult(textBlock: resultText)
   │      │
   │      ▼
   │   返回 "Phi.ProviderManager 实现了 catalog + credential + factory，..."
   │
   ├──► tool_call: delegate(agent="researcher", prompt="查 .NET 10 AssemblyLoadContext...")
   │      │
   │      ▼
   │   返回 "ALC 用 isCollectible=true 支持 /reload..."
   │
   └──► tool_call: delegate(agent="explorer", prompt="查 Phi 已有的跨进程状态...")
          │
          ▼
      返回 "Phi.Avalonia.ActiveSession 是 Avalonia 端的 session holder..."

       ← (3 个 delegate 并发跑; main 端在等)
   │
   ▼
Main Agent 拿到 3 个结果 → 写集成方案
```

**关键约束**：
- 子 session **不能**回到 `ISession` 加 `SubAgents` 属性 —— multi-agent 是**扩展能力**，
  不是 `Session` 抽象的一部分。
- 子 session **完全**活在 `DelegateTool.ExecuteAsync` 的栈上；`using` 结束就 Dispose，
  provider 的 HTTP transport 释放。
- 主 agent 看子 session 的方式只有一条：**通过子 session 的 tool result 拿到最终文本**。

### 16.3 用到的 `IPhiApi` / `ISession` 能力（全部已有，零新增）

| 需求 | 用什么 |
|---|---|
| 起子 session | `Session.LoadAsync(cwd, env, providerName, model)`（`public static`，扩展直接调） |
| 子 session 用独立 system prompt | `SessionEnvironment.Default(...).WithSystemPrompt(spec.SystemPrompt)` |
| 子 session 用独立工具白名单 | `SessionEnvironment.WithTools(...)`（v1 用 system prompt 里的"only use X"约束；v2 接 catalog 过滤） |
| 等子 session 完成 | 轮询 `session.State.IsRunning`（v2 改 `await session.StateChanged`） |
| 拿结果 | `session.State.Messages.OfType<AssistantMessage>().LastOrDefault()?.Text` |
| 清理 | `using var child = ...` |
| 显示进度 | `api.SubmitTranscriptLine(...)` + `api.RegisterTranscriptLineRenderer(...)` |
| 取消传播 | `CancellationTokenSource.CreateLinkedTokenSource(ct).Token.Register(child.Cancel)` |
| 持久化（可选） | `api.AppendEntryAsync("multi-agent:log", dict)` 把每次 spawn 写盘 |

**没有一行需要改 `IPhiApi`**。

### 16.4 项目结构

```
extensions/MultiAgentPack/
├── MultiAgentPack.csproj
├── MultiAgentPack.cs              # 主入口（IPhiExtension 实现）
├── SubAgentSpec.cs                # 子 agent 配置 record
├── DelegateTool.cs                 # 主 agent 调用的 tool，内部跑子 session
├── SubAgentProgressLine.cs        # 自定义 transcript 行 record
├── SubAgentProgressRenderer.cs     # SubmitTranscriptLineRenderer 的实现（如果想要独立可换实现）
├── Configuration/
│   ├── DefaultSubAgents.cs         # 内置 explorer / researcher 的 spec
│   └── SubAgentConfigLoader.cs     # 可选：~/.phi/multi-agent.json 加载
└── MultiAgentPack.Tests/
    ├── DelegateToolTests.cs
    ├── MultiAgentPackTests.cs
    └── Fixtures/
        ├── StubSubAgentRunner.cs
        └── FakeProviderResolver.cs
```

`csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\Phi.Extensions\Phi.Extensions.csproj" />
    <ProjectReference Include="..\..\..\src\Phi\Phi.Runtime.csproj" />
    <!-- Phi.Extensions 还不存在 (Sprint 0)，暂时直接引用 Phi.Runtime 拿 ISession / Session -->
  </ItemGroup>
</Project>
```

> **注**：Sprint 0 完成后 Phi.Extensions 拆出来，csproj 切换成只依赖 `Phi.Extensions`，
> `Phi.Runtime` 不再是 public 包的一部分。

### 16.5 完整代码

#### `MultiAgentPack.cs` —— 入口

```csharp
using Phi.Extensions;

namespace MultiAgentPack;

[PhiExtension(
    Name = "multi-agent-pack",
    Version = "1.0.0",
    Description = "Spawn parallel subagents via the 'delegate' tool. " +
                  "Built-in roles: 'explorer' (codebase) and 'researcher' (web).")]
public sealed class MultiAgentPack : IPhiExtension
{
    public void Setup(IPhiApi api)
    {
        // 1) 让 main agent 知道有 delegate tool，以及什么时候用
        api.AddPromptGuideline("""
            For complex tasks spanning multiple domains, use the 'delegate' tool
            to spawn subagents. Issue multiple delegate calls across DIFFERENT
            roles in one turn to run them in parallel. Each call blocks until
            the subagent finishes; its final answer is returned as the tool result.
            """);

        // 2) 把 subagent 目录暴露给 DelegateTool
        var specs = DefaultSubAgents.All;   // 静态字典，name → SubAgentSpec

        // 3) 注册 delegate tool
        api.RegisterTool(
            new DelegateTool(api, specs),
            new ToolContribution
            {
                PromptSnippet =
                    "delegate: spawn a subagent with a specific role (e.g. 'explorer', 'researcher'), " +
                    "block until it finishes, and return its final answer.",
                PromptGuidelines =
                {
                    "Issue parallel delegate calls across different roles when the task spans multiple domains.",
                    "Keep each subagent's prompt focused — they're single-purpose.",
                },
                Capabilities = ToolCapabilities.ReadLocalFiles | ToolCapabilities.Network,
            });

        // 4) 注册 subagent 进度的 transcript renderer（host 的 ChatTranscript 会按 type 路由）
        api.RegisterTranscriptLineRenderer("multi-agent:subagent-progress", (line, expanded) =>
        {
            var role = line.Details?.TryGetValue("role", out var r) == true
                ? r?.ToString() ?? "?" : "?";
            var status = line.Details?.TryGetValue("status", out var s) == true
                ? s?.ToString() ?? "running" : "running";
            var preview = Truncate(line.Content, expanded ? 400 : 80);
            return new SubAgentProgressLine(line.Id, role, status, preview);
        });
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
```

#### `SubAgentSpec.cs` —— 配置 record

```csharp
namespace MultiAgentPack;

/// <summary>
/// Configuration for one subagent role. The extension ships two built-ins
/// (explorer / researcher) and users can add their own via
/// <see cref="DefaultSubAgents"/> override or, in v2, a JSON config file.
/// </summary>
public sealed record SubAgentSpec(
    string Role,
    string Description,
    string SystemPrompt,
    string? ModelOverride,
    IReadOnlyList<string> ToolAllowList);

public static class DefaultSubAgents
{
    public static IReadOnlyDictionary<string, SubAgentSpec> All { get; } =
        new Dictionary<string, SubAgentSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["explorer"] = new(
                Role: "explorer",
                Description: "Read-only codebase explorer. Returns file paths + concise summaries.",
                SystemPrompt: """
                    You are a read-only codebase explorer.
                    Use read/grep tools only. Do not modify any files.
                    Return a concise summary with file paths and line numbers.
                    """,
                ModelOverride: null,
                ToolAllowList: ["read", "grep"]),

            ["researcher"] = new(
                Role: "researcher",
                Description: "Web researcher. Returns findings with source URLs.",
                SystemPrompt: """
                    You are a web researcher.
                    Use search/fetch tools only.
                    Always cite your sources with URLs.
                    Return findings as a structured summary.
                    """,
                ModelOverride: null,
                ToolAllowList: ["search", "fetch"]),
        };
}
```

#### `DelegateTool.cs` —— 子 session 编排核心

```csharp
using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Extensions;

namespace MultiAgentPack;

/// <summary>
/// The 'delegate' tool. Forks a child <see cref="Phi.Session"/> with a focused
/// system prompt and (optionally) tool whitelist, runs the agent loop to
/// completion, returns the final assistant message as a string.
/// </summary>
public sealed class DelegateTool : Tool
{
    private readonly IPhiApi _api;
    private readonly IReadOnlyDictionary<string, SubAgentSpec> _specs;

    public DelegateTool(IPhiApi api, IReadOnlyDictionary<string, SubAgentSpec> specs)
    {
        _api = api;
        _specs = specs;
    }

    public override string Name => "delegate";
    public override string Description =>
        "Spawn a subagent (role: e.g. 'explorer' or 'researcher'), block until it finishes, " +
        "return its final answer. Issue multiple calls in one turn for parallel work.";

    public override JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["agent"]  = new() { ["type"] = "string",
                                 ["description"] = "Role name (must match a registered subagent spec)." },
            ["prompt"] = new() { ["type"] = "string",
                                 ["description"] = "Task for the subagent. Be specific and self-contained." },
        },
        ["required"] = new JsonArray("agent", "prompt"),
    };

    public override async Task<ToolResult> ExecuteAsync(
        string toolName, string toolCallId, JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var agent = arguments["agent"]?.GetValue<string>();
        var prompt = arguments["prompt"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(agent) || string.IsNullOrWhiteSpace(prompt))
            return new ToolResult(
                [new TextBlock("delegate: 'agent' and 'prompt' are both required.")],
                IsError: true);

        if (!_specs.TryGetValue(agent!, out var spec))
            return new ToolResult(
                [new TextBlock($"delegate: unknown agent '{agent}'. Available: " +
                                string.Join(", ", _specs.Keys))],
                IsError: true);

        var progressId = $"subagent:{Guid.NewGuid():N}";
        try
        {
            Announce(progressId, spec.Role, "running",
                     $"🤖 [{spec.Role}] starting: {Truncate(prompt!, 60)}",
                     new() { ["prompt_preview"] = Truncate(prompt!, 200) });

            var child = await SpawnAsync(spec, prompt!, cancellationToken);
            try
            {
                var result = await WaitForResultAsync(child, spec.Role, progressId, cancellationToken);
                Announce(progressId, spec.Role, "done",
                         $"✅ [{spec.Role}] done: {Truncate(result, 60)}",
                         new() { ["result_preview"] = Truncate(result, 200) });
                return new ToolResult([new TextBlock(result)]);
            }
            finally
            {
                // 子 session 必须显式 Dispose，否则 provider 的 HttpClient 不释放
                child.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            Announce(progressId, spec.Role, "cancelled",
                     $"🚫 [{spec.Role}] cancelled", null);
            throw;
        }
        catch (Exception ex)
        {
            Announce(progressId, spec.Role, "error",
                     $"❌ [{spec.Role}] error: {ex.Message}",
                     new() { ["error"] = ex.Message });
            return new ToolResult(
                [new TextBlock($"delegate: subagent '{spec.Role}' failed: {ex.Message}")],
                IsError: true);
        }
    }

    /// <summary>
    /// Loads a child session with the subagent's focused environment.
    /// Cancellation token is linked to the tool's token, so cancelling the
    /// parent cancels the child.
    /// </summary>
    private async Task<Phi.Session> SpawnAsync(
        SubAgentSpec spec, string prompt, CancellationToken toolCt)
    {
        // 子 session 跟主 session 共用 provider resolver + cwd；env 不同
        var childEnv = SessionEnvironment.Default(_api.Context.ProviderResolver)
            .WithSystemPrompt(spec.SystemPrompt);

        // 当前实现: 工具白名单靠 system prompt 约束（v1 没有 Tool 过滤 API）；
        // v2 走 ToolContribution 过滤。ToolAllowList 字段保留给将来。
        _ = spec.ToolAllowList;

        return await Phi.Session.LoadAsync(
            cwd: _api.Context.Cwd,
            env: childEnv,
            providerName: _api.Context.ProviderName,
            model: spec.ModelOverride ?? _api.Context.Model);
    }

    /// <summary>
    /// Polls the child session until it settles. v2: subscribe to
    /// StateChanged for event-driven completion instead of polling.
    /// </summary>
    private static async Task<string> WaitForResultAsync(
        Phi.Session child, string role, string progressId, CancellationToken ct)
    {
        var pollInterval = TimeSpan.FromMilliseconds(50);
        while (child.State.IsRunning)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(pollInterval, ct);
        }

        var last = child.State.Messages.OfType<AssistantMessage>().LastOrDefault();
        if (last?.Text is { Length: > 0 } text) return text;

        throw new InvalidOperationException(
            $"subagent '{role}' produced no assistant text (stopReason={last?.StopReason})");
    }

    private void Announce(string id, string role, string status, string content,
                          Dictionary<string, object?>? extraDetails)
    {
        var details = extraDetails is null
            ? new Dictionary<string, object?> { ["role"] = role, ["status"] = status }
            : new Dictionary<string, object?>(extraDetails) { ["role"] = role, ["status"] = status };

        _api.SubmitTranscriptLine(new TranscriptLine(
            Type: "multi-agent:subagent-progress",
            Id: id,
            Content: content,
            Details: details));
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
```

#### `SubAgentProgressLine.cs` —— 自定义 transcript 行

```csharp
namespace MultiAgentPack;

/// <summary>
/// One row in the transcript for a running / finished / failed subagent.
/// TUI / Avalonia render this however their registered
/// <see cref="Phi.Extensions.TranscriptLineRenderer"/> chooses — by
/// default a compact "🤖 [role] ..." line.
/// </summary>
public sealed record SubAgentProgressLine(
    string Id,
    string Role,
    string Status,    // "running" | "done" | "cancelled" | "error"
    string Preview) : Phi.Extensions.ChatLine(Id);
```

> **注**：`TranscriptLine` 类型（被 `SubmitTranscriptLine` 接受）和 `ChatLine` DU
> （被 host 渲染）是两个不同的概念。`TranscriptLine` 是**扩展用的 DTO**（带 type
> string + JSON details）；`ChatLine` 是**host 渲染用的 DU**（带具体子类型）。`TranscriptLineRenderer`
> 负责从 DTO 到 DU 的转换（返回 `ChatLine`）。**这里直接返回 `SubAgentProgressLine` 必须是
> host 的 DU 子类型**——所以 `SubAgentProgressLine` 在 host 的 DU 里也得定义，扩展
> 自己不能造 host 不知道的类型。**v2 会改成扩展返回 `CustomLine` 让 host 走 registered
> renderer**——本参考实现假定 host 是 Avalonia/TUI 主仓，DU 跟参考扩展一起 evolve。

### 16.6 测试

#### `DelegateToolTests.cs` —— 子 session 编排的关键路径

```csharp
using Phi.Agent;
using Phi.Provider;
using Phi.Tests.Helpers;  // 假设 host 提供 StubProvider / FakeProviderResolver

namespace MultiAgentPack.Tests;

[NotInParallel("multi-agent")]
public class DelegateToolTests : IDisposable
{
    private readonly FakeProviderResolver _resolver = new();
    private readonly InMemoryPhiApi _api;

    public DelegateToolTests()
    {
        _api = new InMemoryPhiApi(
            resolver: _resolver,
            cwd: "/test",
            providerName: "stub",
            model: "stub-model");
    }

    [Test]
    public async Task Unknown_Agent_Returns_ErrorResult()
    {
        var tool = new DelegateTool(_api, DefaultSubAgents.All);
        var result = await tool.ExecuteAsync(
            "delegate", "call-1",
            JsonNode.Parse("""{"agent":"nope","prompt":"x"}""").AsObject(),
            CancellationToken.None);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Content[0].As<TextBlock>().Text)
            .Contains("unknown agent");
    }

    [Test]
    public async Task Spawns_Subagent_With_Focused_SystemPrompt()
    {
        // 验证：env 传进去的 system prompt 真的被子 session 用上
        _resolver.Providers["stub"] = StubProvider.Echo(StubProvider.TextTurn("ok"));
        var tool = new DelegateTool(_api, DefaultSubAgents.All);

        var result = await tool.ExecuteAsync(
            "delegate", "call-1",
            JsonNode.Parse("""{"agent":"explorer","prompt":"find Foo"}""").AsObject(),
            CancellationToken.None);

        await Assert.That(result.IsError).IsFalse();
        // StubProvider.Echo 返回 "ok"，所以结果包含 "ok"
        await Assert.That(result.Content[0].As<TextBlock>().Text).Contains("ok");

        // 关键：捕获子 session 创建时的 env，看 system prompt 是否被 override
        await Assert.That(_api.LastSpawnedEnv?.SystemPrompt)
            .Contains("read-only codebase explorer");
    }

    [Test]
    public async Task Cancellation_Propagates_To_Child()
    {
        // 用一个 blocks 的 provider —— 子 session 永远不结束，直到被 cancel
        _resolver.Providers["stub"] = StubProvider.FirstCallBlocks(new TaskCompletionSource());
        var tool = new DelegateTool(_api, DefaultSubAgents.All);

        using var cts = new CancellationTokenSource();
        var task = tool.ExecuteAsync(
            "delegate", "call-1",
            JsonNode.Parse("""{"agent":"explorer","prompt":"x"}""").AsObject(),
            cts.Token);

        // 等子 session 进入 IsRunning=true
        await SpinWait.SpinUntil(() => _api.LastSpawnedSession?.State.IsRunning == true,
            TimeSpan.FromSeconds(2));

        cts.Cancel();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
        // 子 session 必须被 cancel
        await Assert.That(_api.LastSpawnedSession!.State.IsRunning).IsFalse();
    }

    public void Dispose() { /* cleanup */ }
}
```

> **InMemoryPhiApi** 是一个测试用的 `IPhiApi` mock，记所有调用。完整实现约 100 行，
> 放在 `MultiAgentPack.Tests/Fixtures/`。**这是社区扩展作者的模板** —— 自己写
> in-memory `IPhiApi` mock 来 unit-test 工具的编排逻辑，不依赖真实 host。

### 16.7 给其它扩展作者的建议

#### 抄什么

| 模式 | 在 `MultiAgentPack` 哪里 |
|---|---|
| **在 `Setup` 里 `AddPromptGuideline` 教 main agent 怎么用你的扩展** | `MultiAgentPack.cs:18-22` |
| **用 `ToolContribution`（不是裸 `RegisterTool`）让 tool 自动出现在 system prompt 的可用工具段** | `MultiAgentPack.cs:33-43` |
| **`SubmitTranscriptLine` + `RegisterTranscriptLineRenderer` 给你的扩展提供 transcript 可见性** | `MultiAgentPack.cs:45-52` + `Announce` helper |
| **`try/finally Dispose` 你的子资源**（provider / child session / disposable handles） | `DelegateTool.cs:70-79` |
| **取消 token 用 `CreateLinkedTokenSource` 而不是裸 token** | `SpawnAsync` 内部（v2） |
| **结构化错误信息用 `ToolResult.IsError: true` 而不是抛异常** | `unknown agent` 分支 |

#### 不要抄什么

| 反模式 | 原因 |
|---|---|
| ❌ 不要在 `Setup` 里启动后台线程 | 同步 `Setup` 是契约；后台 work 应该挂在 `On("session_start")` 里 |
| ❌ 不要直接调 `File.ReadAllText` 等 BCL | 走 `IPhiContext.Ui`（读文件等应在 v1.5 capability 后走声明 API） |
| ❌ 不要在 extension 里 catch + 吞掉异常 | 让 `ExtensionError` 抛上去，由 host 的 audit log 记录 |
| ❌ 不要持有 `ISession` 引用跨 turn | 扩展可能被 reload；用 `IDisposable On(...)` 而不是直接存 `session` 引用 |
| ❌ 不要把 child session 的 transcript 全文塞回 main agent | 只回最终 assistant message 的文本；其余让 main agent 自己 `delegate` 进一步问 |

### 16.8 已知限制 / 未来工作

| 当前限制 | 后续何时 |
|---|---|
| 工具白名单靠 system prompt 约束，没真过滤 | Sprint 2：扩展拿到的 `IPhiContext.Tools` 变成可枚举的；`ToolContribution` 过滤 API 公开 |
| 用 50ms 轮询 `IsRunning` | Sprint 2：扩展能 `await session.StateChanged`（`Session` 已经 raise `StateChanged`） |
| 单 `delegate` tool 内部串行等；多个 `delegate` 在 main agent 一 turn 内并发由 LLM 驱动 | 已支持（model 自己 fire 多个 tool_call，host 并发执行）；不需要改 |
| 没有 subagent transcript 自动 forward 回 main | Sprint 4：`CustomLine` 进 `ChatLine` DU + host 渲染支持折叠的 subagent 视图 |
| 不能 resume subagent（必须每次从头跑） | Sprint 5+：`ISession.ResumeAsync` 已经支持，扩展只需要存 sessionId |
| 大型结果（>10K tokens）走 model context | v2：`ToolResult` 改支持 file reference，主 agent 用 `read` 看完整结果 |

### 16.9 为什么这一节放在最后

`MultiAgentPack` 是 Sprint 5 的交付物。它依赖 Sprint 1-4 的所有 API：

- Sprint 1：`IPhiApi.RegisterTool` + `AddPromptGuideline` + `SubmitTranscriptLine`
- Sprint 2：`Phi.Extensions.Host` 加载 + lifecycle + generation guard（保证 `/reload` 后旧 PhiApi 失效）
- Sprint 3：`IPhiUiBridge` + `TuiPhiUiBridge` / `AvaloniaPhiUiBridge`
- Sprint 4：`RegisterTranscriptLineRenderer` + `ChatLine` DU 的 `CustomLine`

**没有这些前置 sprint 就写不出能跑的 `MultiAgentPack`**——但反过来，**有了这些 sprint 后写 `MultiAgentPack` 不需要任何新的 API**。这就是 §16 的核心结论：**当前 `IPhiApi` 设计已经在 multi-agent 这个公认复杂用例上自洽**。

---

---

## §17 官方 MCP 客户端扩展 (`McpPack`)

> **定位**：MCP（Model Context Protocol）通过官方扩展 `McpPack` 提供，**不进入 Phi 主体代码**。
> 本节是 §16 MultiAgentPack 之后的第二个"参考扩展"——展示"外部协议适配器"在 Extensions 平台上的形态。
> 用户不装 McpPack 就完全不付 MCP 相关的任何代价（运行时、内存、API surface、UI）。
>
> **依赖官方 SDK**：McpPack 用 NuGet `ModelContextProtocol`（微软官方 C# MCP SDK，仓库
> [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)），
> **不自己手写 transport / JSON-RPC 序列化 / 协议握手**。SDK 跟随 spec 自动升级；
> McpPack 只负责"把 SDK 的 client 接到 Phi 生命周期上 + 把 MCP tool 包成 Phi tool"。
> 当前 SDK 跟踪到 MCP 协议 spec `2025-06-18`（SDK 2.x 线的稳定分支），McpPack 的 `ModelContextProtocol` 引用跟着升级即可。
>
> **本次修订**：原 §17 是手写 stdio transport + JSON-RPC 的方案；
> 改成"用 SDK"后代码量 -25%、跨平台 QA 消失、协议升级 0 工作量。详见 §17.7 / §17.11 / §17.16 / 附录 C。

### 17.1 最小核心理念

```
Phi 核心（始终装、最小）                Phi 扩展生态（可选装）
─────────────────────────                ──────────────────────────
Session / Harness / AgentLoop             CodingPack (bash/read/write/edit)
ISession 导航 API                         MultiAgentPack (delegate tool)
4 个 builtin tools                         McpPack (MCP 客户端)   ← 本节
tool / hook / event 原语                    PermissionGate (tool_call 拦截)
tool card / transcript line 渲染           Phi.Figma (figma 集成, 假设)
TUI / Avalonia 双端 shell                  Phi.Aws (AWS 集成, 假设)
                                          …… 社区自己写的几百个 dll
```

**判定标准**：

- **核心**：任何用户在"第一次装 Phi"时就期待能用的能力 —— 跟 LLM 聊天、写代码、读文件
- **扩展**：特定 workflow / 外部服务 / 领域深度 / UX 定制 —— 需要的人装

`McpPack` 是典型扩展：它让 Phi **能跟 MCP server 生态对话**，但 80% 的"基本使用"（只用 builtin tools）完全不依赖它。

### 17.2 McpPack 做与不做的明确边界

#### 做

- 读 `~/.phi/mcp-servers.json` 拿 server 配置
- 为每个 server spawn 一个 stdio 子进程（或开 HTTP/SSE 连接，Sprint 6.5+）
- 跑 JSON-RPC `initialize` + `tools/list`
- **每个 MCP tool → 一个 `Phi.Tool`**，通过 `api.RegisterTool` 注册
- 模型调 MCP tool → `Phi.Tool.ExecuteAsync` → JSON-RPC `tools/call` → 结果
- 子进程 lifecycle：`session_start` 时 connect，`session_shutdown` 时 dispose，`reload` 时 reconnect
- Tool 名加前缀避免冲突：`mcp__<server-key>__<tool-name>`

#### 不做

- **不做** server picker UI（用 `api.Context.Ui.SelectAsync` 让用户挑 server —— 这部分 v2）
- **不做** OAuth / token 管理（用户自己填 `env` 字段，sprint 6.5+ 加 host API）
- **不做** MCP-specific tool cards（用通用 `ToolDescriptor(ToolKind.Generic, ...)`，TUI/Avalonia 自渲染）
- **不做** MCP resource / prompt 模板支持（v2，先聚焦 tools）
- **不做** HTTP/SSE transport（Sprint 6 v1 只做 stdio；HTTP/SSE 在 v2 + Cross-platform 一起做）
- **不做** Phi 主体代码任何改动

### 17.3 配置文件格式

`~/.phi/mcp-servers.json`：

```jsonc
{
  "$schema": "https://phi.dev/schemas/mcp-servers-v1.json",
  "servers": {
    "figma": {
      "transport": "stdio",                       // v1 只支持 stdio
      "command": "npx",
      "args": ["-y", "@figma/mcp-server"],
      "env": {
        "FIGMA_TOKEN": "${env:FIGMA_TOKEN}"        // 引用环境变量
      },
      "disabled": false                           // false 时 McpPack 不加载（用户手动关）
    },
    "github": {
      "transport": "stdio",
      "command": "mcp-server-github",
      "args": [],
      "env": { "GITHUB_TOKEN": "${env:GITHUB_TOKEN}" }
    },
    "internal-db": {
      "transport": "stdio",
      "command": "/opt/internal/mcp-server",
      "args": ["--config", "/etc/mcp/db.json"]
      // 不填 disabled → 默认 false
    }
  }
}
```

| 字段 | 必需 | 说明 |
|---|---|---|
| `transport` | ✅ | `stdio` 是 v1 唯一选项；`http`/`sse` 是 v2 |
| `command` | ✅ (stdio) | 可执行文件 |
| `args` | ❌ | 启动参数 |
| `env` | ❌ | 环境变量；`${env:NAME}` 引用 host 环境 |
| `disabled` | ❌ | `true` 时 McpPack 跳过；其它字段照填 |
| `cwd` | ❌ | 工作目录；v2 |
| `timeout` | ❌ | 启动超时（秒）；v2 |

**加密**：`env` 里如果放 token 会明文写盘。v1.5+ 借鉴 `PhiSettings` 的做法（操作系统 keyring 优先，文件 fallback 配权限位 0600）。

### 17.4 Tool 命名与冲突解决

MCP tool 名是 `server` 维度的（每个 server 可能都有 `get_file`）。直接用会冲突。McpPack 强制前缀：

```
MCP server 内部 tool:   get_file
Phi 注册名:             mcp__figma__get_file
                       ^^^^^^^  ^^^^^^^^
                       server   tool
                       key      name
```

实现：

```csharp
string PhiToolName(string serverKey, string mcpToolName) =>
    $"mcp__{serverKey}__{mcpToolName}".Replace('-', '_');
```

下划线分隔，跟现有 Phi 工具名（`read` / `write` / `bash` / `edit`）兼容。模型在 tool 选择时看到 `mcp__figma__get_file` 自带 server 来源信息。

### 17.5 项目结构

```
extensions/McpPack/
├── McpPack.csproj                # + PackageReference ModelContextProtocol
├── McpPack.cs                     # IPhiExtension 入口（注册 lifecycle hooks）
├── Configuration/
│   ├── McpServersConfig.cs        # record + System.Text.Json source-gen
│   └── McpServersLoader.cs        # 读 ~/.phi/mcp-servers.json + 解析 ${env:...}
├── McpTransportFactory.cs         # SDK transport 跟我们 config 的桥（stdio 转换）
├── McpToolAdapter.cs              # MCP tool → Phi.Tool（包一层 + 调 IMcpClient）
├── McpErrorMapper.cs              # SDK 的 CallToolResult → ToolResult
├── ProtocolVersionTracker.cs     # 记录每次 connect 协商到的版本 + capabilities（audit）
├── McpPack.Tests/
│   ├── McpToolAdapterTests.cs     # 验 tool 名映射 / 参数透传 / 结果解析（用 mock IMcpClient）
│   ├── McpPackTests.cs             # 验 lifecycle + config 解析
│   └── VersionTrackingTests.cs    # 验 SDK 版本协商事件正确写入 audit log
└── Fixtures/
    └── EchoMcpServer/             # 测试用最小 stdio MCP server（fixture，可选）
        └── Program.cs
```

`McpPack.csproj` 关键引用：

```xml
<ItemGroup>
  <PackageReference Include="ModelContextProtocol" Version="2.2.0" />
  <!-- 当前 SDK 跟踪到协议 spec 2025-06-18；McpPack 不写死版本号，靠 SDK 自动跟 spec -->
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="..\..\..\src\Phi.Extensions\Phi.Extensions.csproj" />
  <!-- Sprint 0 后 Phi.Extensions 拆出；这里只引用 Phi.Extensions，不引 Phi 主体 -->
</ItemGroup>
```

**总规模估算**：~280 行（含 fixture），比手写 transport 路径（~410 行）**少 ~30%** —— SDK 把 transport / JSON-RPC / 握手 / 错误码处理 / 进程管理全包了。

### 17.6 入口：`McpPack.cs`

```csharp
using ModelContextProtocol.Client;
using Phi.Extensions;

namespace McpPack;

[PhiExtension(
    Name = "mcp-pack",
    Version = "1.0.0",
    Description = "Generic MCP client: connect to MCP servers via stdio, expose their tools as Phi tools. " +
                  "Backed by the official ModelContextProtocol SDK.",
    Capabilities = ExtensionCapability.Network | ExtensionCapability.ProcessSpawn)]
public sealed class McpPack : IPhiExtension
{
    private readonly List<IMcpClient> _clients = new();
    private readonly List<McpToolAdapter> _registeredTools = new();
    private readonly ProtocolVersionTracker _versionTracker = new();

    public void Setup(IPhiApi api)
    {
        api.AddPromptGuideline("""
            MCP tools (prefix `mcp__<server>__<tool>`) are provided by McpPack.
            Use them when the user asks for capabilities that require external services
            (figma files, github issues, database queries, etc.) that aren't covered by
            the built-in tools.
            """);

        // Lifecycle: connect at session_start
        api.On("session_start", async (ev, ctx) =>
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".phi", "mcp-servers.json");

            if (!File.Exists(configPath)) return ValueTask.CompletedTask;

            var servers = McpServersLoader.Load(configPath);
            foreach (var (key, serverConfig) in servers)
            {
                if (serverConfig.Disabled) continue;

                // SDK factory: 协议握手 + capability 协商 + lifecycle 全部交给 SDK
                var transport = McpTransportFactory.Build(key, serverConfig);
                var client = await McpClientFactory.CreateAsync(transport);
                _clients.Add(client);

                // 记录协商到的协议版本（audit / 调试用）
                _versionTracker.RecordConnect(api, key, client);

                // 每个 MCP tool → 一个 Phi.Tool
                foreach (var mcpTool in await client.ListToolsAsync())
                {
                    var adapter = new McpToolAdapter(client, key, mcpTool);
                    _registeredTools.Add(adapter);
                    api.RegisterTool(adapter, new ToolContribution
                    {
                        PromptSnippet = mcpTool.Description,
                        Capabilities = ToolCapabilities.Network,
                    });
                }
            }
            return ValueTask.CompletedTask;
        });

        // Lifecycle: dispose at session_shutdown
        api.On("session_shutdown", async (ev, ctx) =>
        {
            foreach (var c in _clients) await c.DisposeAsync();
            _clients.Clear();
            _registeredTools.Clear();
            return ValueTask.CompletedTask;
        });
    }
}
```

> **关键**：`McpClientFactory.CreateAsync(transport)` 这一行替换了原来的"自己写 transport + 自己写
> JSON-RPC client + 自己写 handshake"——SDK 包了所有。McpPack **只**关心两件事：
> （1）从 config 文件构造 SDK 接受的 transport；
> （2）把 SDK 暴露的 tools 列表翻译成 Phi tools。

### 17.7 Transport：用官方 SDK，不自己写

**v1 不实现 transport**。`ModelContextProtocol` NuGet（微软官方，跟 MCP spec 同步升级，
版本 2.2.0 对应当前协议规范 `2025-06-18`）已经提供了：

- `StdioClientTransport`：stdio 模式（spawn 子进程 + 读写 stdin/stdout，跨 Win/Linux/macOS 处理 named pipe vs pipe）
- `HttpClientTransport`：HTTP/SSE 模式（Streamable HTTP，server-sent events 跟 progress notification 自动处理）
- `McpClientFactory.CreateAsync`：协议握手（initialize / initialized notification）+ capability 协商 + 子进程 lifecycle 管理

我们只做"把 SDK 的 transport 跟 config 文件接起来"的薄薄一层：

```csharp
// McpTransportFactory.cs
using ModelContextProtocol.Client;

namespace McpPack;

internal static class McpTransportFactory
{
    public static IClientTransport Build(string serverKey, McpServerConfig config)
    {
        return config.Transport?.ToLowerInvariant() switch
        {
            "stdio" => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = serverKey,
                Command = config.Command
                    ?? throw new InvalidOperationException(
                        $"McpPack: server '{serverKey}' has no 'command'"),
                Arguments = (IList<string>)(config.Args ?? new List<string>()),
                EnvironmentVariables = config.Env
                    ?? new Dictionary<string, string?>(),
            }),
            // v2:
            // "http" => new HttpClientTransport(new HttpClientTransportOptions
            // {
            //     Name = serverKey,
            //     Endpoint = new Uri(config.Url!),
            //     TransportMode = HttpTransportMode.StreamableHttp,
            // }),
            _ => throw new NotSupportedException(
                $"McpPack v1 only supports 'stdio' transport, got '{config.Transport}' " +
                $"for server '{serverKey}'. Use 'stdio' or wait for v2 (HTTP/SSE)."),
        };
    }
}
```

**为什么不用自己的 transport**：

- **MCP 协议细节很微妙**——JSON-RPC over stdio、notification 过滤、id 匹配、错误码分类、`ContentBlock` 类型多态，
  每一个都是踩坑点
  （原版 §17.7 自己写的 `Read until we get a response with matching id` 就是最常见的错误源——遗漏 notification 会让协议死锁）
- **SDK 跟踪 spec 自动升级**——我们写代码时是 spec v1，今天 spec `2025-06-18`，SDK 已经跟上了；
  spec 再升级，**我们什么都不用改**，升 NuGet 包版本即可
- **跨平台细节 SDK 都处理了**：Windows 上 stdin/stdout 是 named pipe，Unix 上是 fd；process group cleanup；
  Ctrl-C 信号传递；环境变量编码。**自己写至少 200 行 + 3 个平台的 QA**
- **错误处理 SDK 也包了**：transport 中断、JSON 解析失败、协议版本不匹配——SDK 都抛成 typed exception，
  我们 catch 后转成 `ToolResult.IsError`

**v1 净省 ~150 行 transport 代码 + 跨平台 QA 时间**，把精力放在 Phi 集成（lifecycle / error mapping / tool wrapping）上。

#### MCP 协议版本协商（v2 spec 关键变化）

SDK 在 `IMcpClient.NegotiatedProtocolVersion` 暴露 connect 时**协商到的协议版本**。
新版 spec（2025-06-18+）引入**结构化 content**（typed `StructuredContent`），跟 v1（2024-11-05）的
"内容只是 `TextContent` + `ImageContent` 字符串"不同。McpPack 通过 SDK 自动跟 spec 兼容，
不需要 McpPack 自己做版本分支判断。

**记录协商到的版本**（写到 audit + 暴露给 `/extensions` 命令）：

```csharp
// ProtocolVersionTracker.cs
internal sealed class ProtocolVersionTracker
{
    public void RecordConnect(IPhiApi api, string serverKey, IMcpClient client)
    {
        var version = client.NegotiatedProtocolVersion?.ToString() ?? "unknown";

        // 写到 extension 自己的 audit 段（per-extension，跨 session 累积）
        api.AppendEntryAsync("mcp:connect", new Dictionary<string, object?>
        {
            ["server"] = serverKey,
            ["protocol_version"] = version,
            ["capabilities"] = client.ServerCapabilities?.ToString() ?? "none",
        });
    }
}
```

用户运行 `/extensions` 能看到每个 MCP server 协商到的版本；不匹配的版本（比如 McpPack 期望 v2 但 server 只支持 v1）会被 SDK 协商降级并记录在 audit log。

### 17.8 Tool 适配器

```csharp
// McpToolAdapter.cs
using ModelContextProtocol.Client;
using Phi.Agent;

namespace McpPack;

/// <summary>
/// Wraps one MCP tool (from an <see cref="IMcpClient"/>) as a Phi <see cref="Tool"/>.
/// SDK owns the JSON-RPC / transport; we own naming + result mapping.
/// </summary>
public sealed class McpToolAdapter : Tool
{
    private readonly IMcpClient _client;
    private readonly string _serverKey;
    private readonly McpClientTool _mcpTool;

    public McpToolAdapter(IMcpClient client, string serverKey, McpClientTool mcpTool)
    {
        _client = client;
        _serverKey = serverKey;
        _mcpTool = mcpTool;
    }

    public override string Name => $"mcp__{_serverKey}__{_mcpTool.Name}".Replace('-', '_');
    public override string Description => _mcpTool.Description ?? "";
    public override JsonObject Parameters => _mcpTool.JsonSchema;   // SDK 直接给 JsonObject

    public override async Task<ToolResult> ExecuteAsync(
        string toolName, string toolCallId,
        JsonObject arguments, CancellationToken cancellationToken)
    {
        try
        {
            // SDK 把所有 MCP / transport / protocol 错误抛成 typed exception，
            // McpErrorMapper 处理 happy path；except 分支处理 transport 中断 / server crash。
            var result = await _client.CallToolAsync(
                _mcpTool.Name,
                arguments,
                cancellationToken: cancellationToken);

            return McpErrorMapper.ToToolResult(result);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"MCP {_serverKey}: {ex.Message}")],
                IsError: true);
        }
    }
}
```

> **关于 `JsonSchema`**：SDK 的 `McpClientTool.JsonSchema` 已经是 `JsonNode`/`JsonObject` 形式，
> 直接给 Phi.Tool 用，不需要额外序列化。v2 spec 的 `StructuredContent` 走 `result.StructuredContent`
> 字段（typed C# object），McpPack v1 只读 `result.Content`（content blocks），够用。

### 17.9 错误映射

```csharp
// McpErrorMapper.cs
using ModelContextProtocol.Protocol;
using Phi.Agent;

namespace McpPack;

/// <summary>
/// Maps SDK's <see cref="CallToolResult"/> (MCP protocol spec types) to Phi's
/// <see cref="ToolResult"/>. SDK 已把 JSON-RPC / transport / protocol errors
/// 抛成 typed exception（见 §17.8 的 catch 分支），这里只处理 happy path 的
/// content blocks 翻译。
/// </summary>
internal static class McpErrorMapper
{
    public static ToolResult ToToolResult(CallToolResult result)
    {
        var blocks = new List<ContentBlock>();
        foreach (var content in result.Content)
        {
            switch (content)
            {
                case TextContentBlock t:
                    blocks.Add(new TextBlock(t.Text));
                    break;
                case ImageContentBlock i:
                    blocks.Add(new ImageBlock(i.Data, i.MimeType ?? "image/png"));
                    break;
                case AudioContentBlock a:
                    blocks.Add(new AudioBlock(a.Data, a.MimeType ?? "audio/mpeg"));
                    break;
                case ResourceLinkBlock r:
                    blocks.Add(new ResourceLinkBlock(r.Uri));
                    break;
                case EmbeddedResourceBlock er:
                    blocks.Add(new EmbeddedResourceBlock(er.Resource));
                    break;
                // 未知 content type → 文本化降级（保留原始 JSON 供调试）
                default:
                    blocks.Add(new TextBlock(content.ToString() ?? "(unknown MCP content)"));
                    break;
            }
        }
        return new ToolResult(blocks, IsError: result.IsError ?? false);
    }
}
```

> **v2 spec 新增**：`StructuredContent` 字段携带 typed C# object（强 schema）。
> McpPack v1 **不**读这个字段（用 reflection deserialize 风险大，等 Phi 域消息有
> `StructuredContent` 支持再加）。如果 server 返回 structured content + text content，
> McpPack 只读 text —— 这种 server 应该升级 McpPack 到 v2 + Phi 域加 `StructuredMessage`。

### 17.10 Lifecycle 与 reload

```
T0  ── phi.exe 启动
        │
        ▼ McpPack loaded (Sprint 0+)
T1  ── session_start 事件
        │   ├─ 读 ~/.phi/mcp-servers.json
        │   ├─ 对每个 enabled server: spawn stdio + initialize + tools/list
        │   └─ 每个 MCP tool → api.RegisterTool(...)
        │
        ▼ 用户对话 (MCP tools 已可用)
T2  ── model fire tool_call mcp__figma__get_file(...)
        │   └─ McpToolAdapter → JSON-RPC tools/call → TextBlock result
        │
        ▼ 用户按 /reload
T3  ── Phi: old McpPack disposed (generation guard 失效)
        │      所有 old McpClient dispose (stdio 子进程 terminate)
        │
        ▼ new McpPack loaded
T4  ── session_start(reason="reload")
        │   └─ 重新 spawn 子进程 + 重新 register tools
        │
        ▼ 用户对话继续
T5  ── session_shutdown (TUI exit / /exit / navigation to new session)
        │   └─ 所有 McpClient dispose (stdio terminate)
```

**关键不变量**：
- 子进程**强绑定到 session**：session 结束 → 子进程终止。**绝不泄漏** zombie。
- **不强绑定到进程**：reload 时旧 client 全部 dispose，新 client 全部 spawn。
- `GenerationGuard` 保证 reload 期间 in-flight 的 `McpClient.CallToolAsync` 不会写到已 dispose 的 `_stdin`。

### 17.11 测试策略

#### 单元测试（mock `IMcpClient`）

**不需要**自己造 JSON-RPC mock —— SDK 的 `IMcpClient` 是接口，直接 fake：

```csharp
// McpToolAdapterTests.cs
using ModelContextProtocol.Client;
using Phi.Tests.Helpers;  // StubProvider / in-memory IPhiApi

[NotInParallel("mcp")]
public class McpToolAdapterTests
{
    [Fact]
    public async Task Name_Follows_ServerKey_Underscore_ToolName_Pattern()
    {
        var client = new FakeMcpClient(tools: new[]
        {
            new McpClientTool { Name = "get_file", Description = "Get a file", JsonSchema = /* ... */ },
        });
        var adapter = new McpToolAdapter(client, "figma", client.Tools[0]);
        Assert.Equal("mcp__figma__get_file", adapter.Name);
    }

    [Fact]
    public async Task ExecuteAsync_CallsTool_AndMapsResult()
    {
        var client = new FakeMcpClient(tools: new[]
        {
            new McpClientTool { Name = "echo", Description = "echo", JsonSchema = /* ... */ },
        }) { CallToolFn = (name, args) => new CallToolResult {
            Content = new[] { new TextContentBlock { Text = "hello" } },
            IsError = false,
        }};
        var adapter = new McpToolAdapter(client, "test", client.Tools[0]);
        var result = await adapter.ExecuteAsync("mcp__test__echo", "call-1",
            JsonNode.Parse("""{"text":"hello"}""").AsObject(), default);

        Assert.False(result.IsError);
        Assert.Equal("hello", result.Content[0].As<TextBlock>().Text);
    }

    [Fact]
    public async Task ExecuteAsync_OnTransportError_ReturnsErrorResult()
    {
        var client = new FakeMcpClient(tools: new[] { /* ... */ }) {
            CallToolFn = (name, args) => throw new InvalidOperationException("transport died"),
        };
        var adapter = new McpToolAdapter(client, "test", client.Tools[0]);
        var result = await adapter.ExecuteAsync(/* ... */, default);
        Assert.True(result.IsError);
        Assert.Contains("transport died", result.Content[0].As<TextBlock>().Text);
    }
}
```

`FakeMcpClient` 是测试辅助类（~30 行），实现 `IMcpClient` 把工具列表 + `CallToolFn` 委托
注入。**这是 SDK 测试惯用法**——`ModelContextProtocol.Client.IMcpClient` 本身是
public interface，专为 mock 设计。

#### 集成测试（真 stdio via SDK）

```csharp
[Fact]
public async Task StdioServer_ListTools_RealSubprocess()
{
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "echo",
        Command = "dotnet",
        Arguments = ["run", "--project", "Fixtures/EchoMcpServer", "--no-build"],
    });

    await using var client = await McpClientFactory.CreateAsync(transport);
    var tools = await client.ListToolsAsync();

    Assert.NotEmpty(tools);
    Assert.Equal("echo", tools[0].Name);   // 来自 Fixtures/EchoMcpServer
}
```

> **重要**：`await using` 保证 transport dispose 时 SDK 自动 terminate 子进程。
> 不需要 `Process.Kill` 兜底——SDK 走 `StdioClientTransport.DisposeAsync` 路径，
> 内部 `try { Close(); } catch {}` + `process.WaitForExit(2s)` + `Dispose()`。
> 我们 **不**自己管 process lifecycle。

### 17.12 McpPack 跟其它官方扩展的关系

| 能力 | 由谁提供 | 备注 |
|---|---|---|
| `mcp__figma__get_file` 这种工具 | **McpPack**（薄包装） | 任何有 MCP server 的服务都自动有 |
| figma 特定的 tool card（带缩略图） | **(假设的) Phi.Figma 扩展** | McpPack 只做通用 card |
| `/figma export` slash 命令 | **(假设的) Phi.Figma** | McpPack 不注册 slash |
| 设计审查 sub-agent | **MultiAgentPack**（复用） | 通过 `delegate` tool 调 McpPack 的 figma tools |
| figma MCP 连接的 lifecycle | **McpPack** | `On("session_start")` 自动接，`On("session_shutdown")` 自动断 |
| 阻拦"在 production 调 figma delete" | **PermissionGate** | 跟 MCP 无关，所有 tool 都拦 |

**McpPack 是基础设施层**，MultiAgentPack / PermissionGate 是横切关注点，专用服务扩展是 UX 层。**三层不重叠**。

### 17.13 哪些场景 McpPack 够用，哪些需要专用扩展

| 场景 | McpPack | 专用扩展？ |
|---|---|---|
| "把这个 GitHub issue 转成 PR description"（通用 tool 调用） | ✅ 够用 | ❌ 不需要 |
| "figma 这张图存到我的设计规范库"（需要 figma 缩略图 UI） | ❌ tool card 是通用文本 | ✅ 写 `Phi.Figma` |
| "查询 prod DB 然后写 incident report"（通用 tool 调用 + report 模板） | ✅ 够用 | ❌ |
| "auto-deploy to staging"（多步 + 审批） | ✅ 够用 | ❌（或写 deploy orchestrator 通用扩展） |
| "在这个 AWS 账户创建 EKS cluster"（需要 AWS-特定错误处理） | ⚠️ 可以跑，但错误信息不友好 | ✅ 写 `Phi.Aws` |
| "实时跟踪 design token 变更"（需要 stream MCP resource 变化） | ❌ v1 不支持 resource | ✅ 写专用扩展 |

**判定原则**：如果"拿到 tool 结果就能直接用模型解释"，McpPack 够；如果"需要专门的渲染 / 专门的 workflow / 专门的错误处理"，写专用扩展。

### 17.14 安全考量

`~/.phi/mcp-servers.json` 里的 `command` / `args` 是**任意可执行**。这跟 Phi.Extensions 的整体安全模型一致：

- **凭据来源信任**：用户自己编辑的 config 视为可信。**不**解析来自不可信来源（web / fetch）的 mcpServers 配置。
- **`${env:NAME}` 引用 host 环境**：如果 host 环境被注入，token 泄漏到 MCP server。McpPack **不**做变量展开的注入防御 —— host 环境是 trusted computing base 的一部分。
- **`ProcessSpawn` capability 必填**：`[PhiExtension(..., Capabilities = Network | ProcessSpawn)]` —— McpPack 显式声明。Sprint 3 的 capability 强制启用后，用户在 status bar 能看到 "McpPack: Network + ProcessSpawn"。
- **MCP server 本身的代码 = 任意可执行**（同 §9 风险 T1）—— 跟 `dotnet add package` 装个会 `rm -rf` 的 postinstall 是一类风险。`README` 写明"只配你信任的 MCP server"。

### 17.15 跟 §9 安全模型的关系

MCP 走 `ProcessSpawn` capability（第 9 节 v1.5 启用）。McpPack 显式声明它是 **唯一一个需要 ProcessSpawn 的官方扩展**。Sprint 3 上线后：

```
~/.phi/extensions/
├── McpPack.dll            Capabilities = Network | ProcessSpawn ✓
├── CodingPack.dll         Capabilities = FileSystem* ✓
├── MultiAgentPack.dll     Capabilities = FileSystemRead ✓
└── PermissionGate.dll     Capabilities = None ✓
```

用户在 status bar / `/extensions` 命令里能看到每个扩展的 capability 声明。McpPack 是唯一有 `ProcessSpawn` 的，**用户想禁用 MCP 直接 disable McpPack 即可**，不影响其它扩展。

### 17.16 为什么这一节放在 MultiAgentPack 之后 + 跟 SDK 对齐

跟 §16 同样的逻辑：

- **依赖 Sprint 1-4 的所有 API**：`RegisterTool` + `On(event)` + `AddPromptGuideline` + `Capability`
- **不依赖 §16 MultiAgentPack**：可以独立并行开发，但 Sprint 5 排在 Sprint 6 是因为：
  - MultiAgentPack 验证了"用 IPhiApi 组合外部能力"的模式
  - McpPack 是"用 IPhiApi 接入外部协议"的另一种形态，先让社区看到 MultiAgentPack 再放 McpPack，扩散风险更小
- **跟官方 SDK 对齐**：McpPack 走 NuGet `ModelContextProtocol`，跟着 spec 自动升级（spec `2025-06-18`
  → SDK 2.2.0 → McpPack 自动获取 typed `StructuredContent`、Resource / Prompt 模板等 v2 新能力），
  **不需要**每次 spec 更新重写 McpPack
- **Sprint 6 之前**就能给已经有 MCP server 的用户写一份 README "如何用 McpPack" —— 即使代码在 Sprint 6 才合入，文档先稳定

**为什么不自己写 transport**（重申 §17.7 的核心论点）：

| 自己写 | 用 `ModelContextProtocol` SDK |
|---|---|
| ~150 行 JSON-RPC + stdio + 协议握手 | 0 行（SDK 包了） |
| 每次 spec 升级重写（v1 → v2.2.0） | 升 NuGet 版本即可 |
| 跨平台 QA：Win named pipe / Unix pipe / macOS 异常路径 | SDK 测过的 |
| notification 跟 response 串流时按 id 过滤（容易写错） | SDK 内部处理 |
| `Process.Kill(entireProcessTree: true)` 兜底 | SDK 自动清理 |
| Tool discovery schema 解析自己写 | SDK 直接给 `McpClientTool.JsonSchema` |

**结论**：McpPack v1 的代码量比"自己写"路径**少 ~30%**，且**没有协议维护负担**。这就是
"用 SDK 不自己造"在工程上的具体收益。

---

## 附录 C：MCP 客户端扩展的最小骨架（v1 stdio + SDK）

把上面 §17.5 + §17.6 + §17.7 + §17.8 + §17.9 拼起来，**v1 的最小可用 McpPack 是 ~280 行**（不含测试）：

| 文件 | 行数估算 | 备注 |
|---|---|---|
| `McpPack.csproj` | n/a | + `PackageReference ModelContextProtocol` |
| `McpPack.cs`（入口 + lifecycle） | 80 | lifecycle hooks + client 列表 + 注册 loop |
| `Configuration/McpServersConfig.cs`（record + JSON 绑定） | 60 | `System.Text.Json` source-gen |
| `Configuration/McpServersLoader.cs`（读文件 + `${env:}` 展开） | 50 | |
| `McpTransportFactory.cs`（config → SDK transport） | 30 | v1 只支持 stdio；v2 加 http/sse |
| `McpToolAdapter.cs`（tool name 映射 + 参数透传） | 40 | |
| `McpErrorMapper.cs`（SDK result → Phi ToolResult） | 30 | content blocks 类型 switch |
| `ProtocolVersionTracker.cs`（audit + 协商记录） | 20 | |
| **总计** | **~310 行**（不含测试） | 比"自己写"路径（~410 行）**少 ~25%** |

测试 ~250 行（fake `IMcpClient` + echo MCP server + 端到端）。**不用**自己 mock JSON-RPC —— SDK 的 `IMcpClient` interface 专为此设计。

**整个 Sprint 6 是一个 sprint**。如果只想 ship stdio transport + 最简 error handling，**~2 周 1 人**。
```
