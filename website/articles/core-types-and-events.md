# 核心类型与事件

本页将 Phi 与两个参照项目进行对照：

- **pi** — Phi 所模仿的 TypeScript coding agent（`~/github/pi`）
- **tau** — pi 的 Python 再实现（`~/github/tau`）

目标不是逐行移植，而是保持**边界与命名**对齐——这样读过 pi 或 tau 的人能在几分钟内看懂 Phi，未来 Phi 的代码也能引用先例，而不是凭空发明新形状。

## 为什么要有这一页

三层之间必须就数据的形状达成一致：

1. **Provider 层** — `Phi.Provider` 将 Anthropic / OpenAI 兼容的 wire 事件翻译成 Phi 的 provider-neutral 信封
2. **Agent loop / harness** — `Phi.Agent` 消费该信封，发出自己的 agent 级事件，驱动消息循环
3. **UI / 扩展** — `Phi.Avalonia.Desktop`、`Phi.Tui` 以及任何扩展都消费 agent 级事件

如果这三层不一致，每个消费者都要写一个自定义适配器。pi 的 [harness.md](https://github.com/badlogic/pi-mono/blob/main/packages/agent/docs/harness.md) 与 tau 的 [Phase 1: Core Types and Events](https://twotimespi.dev/internals/architecture/) 都预先把这件事固定下来——本页是 Phi 的对应物。

## 分层对照

| 层 | pi | tau | Phi |
|-----|-----|------|-----|
| Provider 翻译 | `pi-ai` | `tau_ai` | `Phi.Provider` |
| 可移植的 agent 大脑 | `@earendil-works/pi-coding-agent`（harness 部分） | `tau_agent` | `Phi.Agent` |
| UI-agnostic runtime（sessions、tools、slash commands、chat projection） | `@earendil-works/pi-coding-agent`（应用部分） | `tau_agent`（其余） | `Phi` |
| 桌面应用 | （pi 只有 TUI） | `tau_coding` | `Phi.Avalonia.Desktop` |
| TUI 应用 | （同上） | `tau_coding` + Textual | `Phi.Tui` |
| 扩展 | （TS 模块） | `tau_coding.extensions` | `Phi.Extensions.Host` + `Phi.Extensions` |

依赖方向在三个项目中完全相同：

```
Phi.Tui ──┐
Phi.Avalonia.Desktop ──┴─► Phi ─► Phi.Provider ─► Phi.Agent
                              └─► Phi.Extensions.Host ─► Phi.Extensions
```

`Phi.Agent` 依赖最少、可单独分发；其余一切都在它之上构建。

## 消息（Messages）

Phi 的 `IAgentMessage` 是持久化对话记录（transcript）的并集。三个项目使用相同的形状（核心是 `UserMessage | AssistantMessage | ToolResultMessage`，其余是 Phi 的扩展）：

| pi / tau | Phi | 说明 |
|----------|-----|------|
| `UserMessage` | `UserMessage`（`src/Phi.Agent/Messages.cs`） | 用户键入的输入。Phi 增加 `UserContent`（text 与 blocks）使同一条记录能承载多模态输入 |
| `AssistantMessage` | `AssistantMessage`（`Messages.cs`） | assistant 文本 + 工具调用。形状相同 |
| `ToolResultMessage` | `ToolResultMessage`（`Messages.cs`） | 工具输出。同样的 `toolCallId` / `name` / `content` |
| `BashExecutionMessage`（仅 tau） | `BashExecutionMessage`（`Messages.cs`） | Phi 将命令 + 输出作为独立的消息类型，以便 chat projector 渲染为 tool card。这是 Phi 自己的扩展，不是 pi/tau 的概念 |
| — | `CustomMessage`、`BranchSummaryMessage`（`Messages.cs`） | Phi 的扩展。`BranchSummaryMessage` 是将来 session-tree 工作的占位；目前已接收但未使用 |

pi 和 tau 都刻意让 `AssistantMessage` 保持精简：只含文本与工具调用，没有别的。Phi 沿用这一规则。

### 内容块（Content blocks）

`AssistantMessage` 携带一个 `ContentBlock` 列表（`Messages.cs`）：

| Block | Phi 类型 | tau 等价物 |
|-------|----------|-----------|
| Text | `TextBlock(string Text, string? TextSignature)` | `TextContent` |
| Image | `ImageBlock(string Data, string MimeType)` | `ImageContent` |
| Thinking | `ThinkingBlock(string Thinking)` | `ThinkingContent` |
| Tool call | `ToolCall(string Id, string Name)`（承载完整 `ToolCall` 数据） | 内联 `ToolCall` |

Phi 使用**带 `[JsonPolymorphic]` 的 record** 实现可传输序列化——与 pi/tau 的 JSON wire 形状一致，只是用 C# record 体系来类型化，而非外部 schema。

## 工具（Tools）

| pi / tau | Phi | 说明 |
|----------|-----|------|
| `AgentTool`（`name`、`description`、`input_schema`、async `execute`） | `Tool`（`Tool.cs`） | 形状相同 |
| `ToolCall`（id、name、arguments） | `ToolCall`（`Messages.cs`） | Phi 同时把 `ToolCall` 作为内容块（用于消息内的工具请求）和独立记录（用于结果回传） |
| `AgentToolResult`（`ok`、`content`、可选 `data`、`error`） | `ToolResult`（`ToolResult.cs`） | Phi 的 `ToolResult` 是一个带 `Result` / `IsError` 标记的 struct。可观测形状相同 |
| — | `TypedTool<TArgs>`（`TypedTool.cs`）+ `Phi.SchemaGen` | Phi 独有：一个类型化工具基类，其 JSON schema **由 Roslyn source generator 在编译期生成**。兼容 `AgentTool` 的 `input_schema` 契约，但运行时使用强类型的 args。这样工具作者不必手写 schema，并天然兼容 nativeAOT/trimming |

Phi 在核心包中不实现四个 coding tools（`read` / `write` / `edit` / `bash`）；它们位于 `extensions/CodingPack/`（`CodingPack.csproj`）。工具基类本身可移植，只有具体实现是应用特定的——与 `tau_coding.create_coding_tools()` 的分层一致。

## Provider 事件（`Phi.Provider`）

这些是**wire-level** 事件，由 provider 适配器发出，在 agent loop 看到它们之前。Tau 没有对应分层（两者都折叠进 `tau_agent.events`）；pi 也保持二者分离。

| pi 等价概念 | Phi 类型 | 说明 |
|---|---|---|
| stream start | `AssistantStartEvent` | 每个 assistant turn 一次 |
| text delta | `TextDeltaEvent(string Delta)` | |
| thinking delta | `ThinkingDeltaEvent(string Delta)` | Phi 的放置是**刻意的**：thinking 放在 provider 层而非 harness 层，使 chat projector 能直接展示，无需每个消费者显式订阅 |
| thinking end | `ThinkingEndEvent(ThinkingBlock Block)` | |
| tool call（模型发出） | `ToolCallEvent(ToolCall ToolCall)` | |
| completion | `AssistantDoneEvent(AssistantMessage Message, Usage Usage, StopReason StopReason)` | |
| error | `AssistantErrorEvent(string Message, JsonNode? Data)` | 仅 provider 层级 |

`Phi.Provider` 的 `OpenAICompatibleProvider.StreamOnceAsync` 与 `Anthropic.StreamOnceAsync` 将 wire 字节翻译为这些记录。

## Harness 事件（`Phi.Agent`）

这些是 harness 发出的**domain-level** 事件。Provider 层发出 wire-level 事件；harness 将其翻译为本信封，并转发给 UI projector、扩展 hook 与 status router。

对照表：

| tau 事件 | pi（harness） | Phi `HarnessEvent` | 状态 |
|-----------|--------------|-------------------|------|
| `agent_start` | `agent_start` | `AgentStartEvent` | ✅ 完全相同 |
| `agent_end` | `agent_end` | `AgentEndEvent(IReadOnlyList<IAgentMessage> Messages)` | ✅ 完全相同；携带本次调用累计的消息 |
| `turn_start` | `turn_start` | `TurnStartEvent(int Turn)` | ✅ 完全相同 |
| `turn_end` | `turn_end` | `TurnEndEvent(AssistantMessage Message, IReadOnlyList<ToolResultMessage>? ToolResults)` | ✅ 完全相同 |
| `queue_update` | `queue_update` | — | ⚠️ **事件层缺失，队列本身已存在** —— `Session._steeringQueue` / `Session._followUpQueue` 与 `AgentLoop.RunAgentAsync` 的 `getSteeringMessages` / `getFollowUpMessages` 参数都已实现；`EnqueueSteering` / `EnqueueFollowUp` 是沉默入队，**不发 `queue_update` 通知**，UI 看不到队列状态变化 |
| `retry` | `retry` | — | ❌ **缺失** —— provider 重试目前不作为 harness 事件上报 |
| `message_start` | `message_start` | `MessageStartEvent(IAgentMessage Message)` | ✅ 完全相同 |
| `message_delta` | `message_update` | `MessageUpdateEvent(AssistantMessage Message, ProviderEvent ProviderEvent)` | ✅ 意图一致；**Phi 命名为 `MessageUpdateEvent` 是为了对齐 pi 的 `message_update`**。Tau 的 `message_delta` 形状更窄；Phi 选择 pi 的命名以与上游 TS agent 保持一致 |
| `thinking_delta` | （折叠进 message 事件） | （折叠到 provider 层） | ⚠️ **故意差异** — Phi 把 thinking 放在 provider 层（`ThinkingDeltaEvent`）而非 harness 层。UI projector 可直接显示，无需每个消费者各自订阅 |
| `message_end` | `message_end` | `MessageEndEvent(IAgentMessage Message)` | ✅ 完全相同；pi 把它作为持久化消息边界，Phi 同样 |
| `tool_execution_start` | `tool_execution_start` | `ToolExecutionStartEvent(string ToolCallId, string ToolName, JsonObject? Args)` | ✅ 完全相同 |
| `tool_execution_update` | `tool_execution_update` | — | ❌ **缺失** — 长时间运行的工具（如 bash 输出进度）目前没有 progress 事件 |
| `tool_execution_end` | `tool_execution_end` | `ToolExecutionEndEvent(string ToolCallId, string ToolName, ToolResult Result, bool IsError)` | ✅ 完全相同 |
| `error`（顶层） | （折叠进 message 事件） | （折叠到 provider 事件） | ⚠️ **故意差异** — Phi 把错误保留在 provider 层，不单独提升一个 `ErrorEvent`。Chat projector 把错误渲染为合成的 assistant 消息；若再另设一个 error 事件，会迫使每个消费者处理两次 |

### 显著的命名差异

- **`MessageUpdateEvent` vs tau 的 `message_delta`**：pi 与 Phi 使用 `update` 动词（与 `MessageStart` / `MessageEnd` 一致）；tau 使用 `delta`。Phi 选择 pi 的约定。
- **`ToolExecutionStartEvent` vs tau 同名**：两边都有；形状一致。
- **`BashExecutionMessage`**：tau 作为 transcript 消息；Phi 也有。两边都是应用特定的扩展（四个 coding tools 不在核心里）。

### Phi 当前相对于 tau 缺失的部分

有两个事件尚未在 Phi 实现。每个都是未来可选的补充，按价值大致排序如下：

1. **`QueueUpdateEvent`** —— inbox 队列变更通知。`Session._steeringQueue` / `Session._followUpQueue` 已存在且 `AgentLoop.RunAgentAsync` 在 turn 边界消费，但 `EnqueueSteering` / `EnqueueFollowUp` 是沉默入队，没有 `HarnessEvent` 上报。补这个事件让 UI 能看见队列状态。
2. **`ToolExecutionUpdateEvent`** — 长时间运行工具的进度（bash 输出增量、文件 diff 流）。便于 UI projector 边展示流式输出，而不是等到最终结果。Phi 的 bash 工具目前只发 start / end。

另有一个辅助事件值得补：

1. **`RetryEvent`** — provider 重试进度。便于状态栏显示；目前对用户不可见。pi 和 tau 都会发。

这些 gap 记为设计债，不是 bug。

## Phi 与 pi/tau 的故意差异

凡是与两个参照项目偏离之处，原因属以下之一：

1. **栈适配的拆分** — 例如 `ThinkingDeltaEvent` 放在 provider 层而非 harness 层，使 C# `async stream` 的消费者不必单独过滤通道。
2. **暂无会话持久化** — Phi 会话不跨进程重启存活。pi/tau 有 JSONL 会话存储；Phi 只有内存。
3. **通过 assembly load context 做扩展** — Phi 的 `Phi.Extensions.Host` 通过 `AssemblyLoadContext` 加载已编译的程序集，代替 pi 的 TS 模块或 tau 的 Python 入口。事件 hook（`on_tool_call`、`on_session_start` 等）刻意镜像 pi 的 API。
4. **steer / follow-up queue 不发事件** —— 队列本身和 `AgentLoop` drain 都已实现，缺的是 `QueueUpdateEvent`。见上节「Phi 当前缺失的部分」。
5. **没有项目信任 gate** — `Phi.Extensions.Host` 让用户在不弹确认框的情况下加载项目级扩展。该 gate 设计保存在 tau 的 `project-trust.md` 中作为未来工作。

不过消息与事件的形状保持一致，这样未来的 session-tree / project-trust 实现可以在不破坏 wire 兼容性的前提下落地（inbox 已存在，缺的是 `QueueUpdateEvent` 通知，见上节）。

## 参见

- [架构概览](architecture.md) — 包布局、依赖方向、运行时边界
- [扩展系统](extensions.md) — 扩展 hook（镜像 pi 的 `ExtensionAPI` 事件表面）
- pi：[`packages/agent/docs/harness.md`](https://github.com/badlogic/pi-mono/blob/main/packages/agent/docs/harness.md)
- tau：[`dev-notes/design/05-core-types-and-events.md`](https://github.com/huggingface/tau/blob/main/dev-notes/design/05-core-types-and-events.md)
- tau：[`dev-notes/design/01-architecture.md`](https://github.com/huggingface/tau/blob/main/dev-notes/design/01-architecture.md)