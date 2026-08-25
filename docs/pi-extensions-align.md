# Phi 扩展系统与 pi 架构对齐差距清单

> 状态：Sprint 0-5 完成。本文档把 Phi 当前的扩展实现与
> [pi extensions](https://pi.dev/docs/latest/extensions) 逐能力对照，
> 列出已对齐 / 缺失 / 需裁剪的项目，作为后续 sprint 的 roadmap。
>
> **对齐原则**：一切以 pi 的 extensions 设计为准，不发明自己的架构。
> Phi 的目标是成为 pi 的 C# 移植，扩展能力面与 pi 对齐。

## 1. 总览

**现状**：Phi 实现了 pi 扩展框架的"最小可用核心"（工具 / 命令 / 事件 /
渲染 / 持久化 / 交互 / context 基础字段 / reload / trust）。这些是 80% 扩展
真正用到的部分，且骨架结构与 pi 一致。

**结论**：Phi 的扩展系统 = pi 扩展框架的**垂直切片**，不是"除了 MCP 之外都一致"。
pi 的完整能力面（资源发现、provider 注册、provider 层 hook、会话树导航、
自定义 UI、markdown 转换等）还有 6-7 大类未实现。

---

## 2. 已对齐（核心骨架，与 pi 一致）

| pi | Phi | 备注 |
|---|---|---|
| `pi.registerTool(definition)` | `RegisterTool(tool, contribution?)` | ✅ |
| `pi.registerCommand(name, options)` | `RegisterCommand` + slash dispatcher | ✅ |
| `pi.on(event, handler)` | `On(eventName, handler)` | ✅ |
| `pi.registerMessageRenderer(customType, renderer)` | `RegisterMessageRenderer` | ✅ |
| `pi.registerEntryRenderer(customType, renderer)` | `RegisterTranscriptLineRenderer` | ✅ |
| `pi.appendEntry(customType, data)` | `AppendEntryAsync(ns, data)` | ✅ |
| `pi.sendUserMessage(content, options)` | `SubmitUserMessage` | ✅ |
| `pi.sendMessage(message, options)` | `SubmitCustomMessage` | ✅ |
| `ctx.ui` (select/confirm/input/notify) | `IPhiUiBridge` | ✅ |
| `ctx.hasUI` | `IPhiContext.HasUi` | ✅ |
| `ctx.cwd` / `ctx.model` / `ctx.isIdle()` | `IPhiContext.Cwd/Model/IsRunning` | ✅ |
| `session_start` / `session_shutdown` | `SessionStartEvent` / `SessionShutdownEvent` | ✅ |
| `tool_call` / `tool_result` / `input` hooks | `HookRegistry` | ✅ |
| `turn_start` / `turn_end` | `TurnStartEvent` / `TurnEndEvent` | ✅ |
| `tool_execution_start/update/end` | `ToolExecutionStartEvent/UpdateEvent/EndEvent` | ✅ |
| `agent_start` / `agent_end` | `AgentStartEvent` / `AgentEndEvent` | ✅ |
| `message_start/update/end` | `MessageStartEvent/UpdateEvent/EndEvent` | ✅ |
| `compaction` 相关事件 | `CompactionStartEvent` / `CompactionEndEvent` | ✅ |
| `/reload` | `ReloadExtensions` | ✅ |
| project trust | `ProjectTrustGate` + `ProjectTrustStore` | ✅ |
| **skills 文件系统发现** | `SkillLoader`（`~/.agents/skills` + 项目 `.agents/skills`）| ✅ 已符合 pi 核心模型 |

**skills 现状说明**：pi 的 skills 是通过**位置发现**加载的独立 `SKILL.md` 文件，
不是扩展打包内容。Phi 的 `SkillLoader` 扫全局 + 项目目录，**已符合 pi 的核心模型**。
唯一缺的是"扩展贡献 skill 目录路径"（见下节）。

**MCP 现状说明**：pi **不提供** MCP（既不在核心也不在扩展框架），扩展作者自己
import MCP SDK 写 client。Phi 的 `McpPack` 作为独立扩展自引
`ModelContextProtocol` SDK —— **恰好符合 pi 的做法**。MCP 不应进 Phi 核心。

---

## 3. 缺失（pi 有，Phi 缺）——按优先级排序

### P0：扩展资源发现（影响扩展生态的基本盘）

| 能力 | pi 机制 | Phi 现状 | 建议 |
|---|---|---|---|
| **`resources_discover` 事件** | 扩展在 `session_start` 后返回 `skillPaths` / `promptPaths` / `themePaths`，让 pi 去额外目录扫 | `SkillLoader` 只扫固定目录 | 加 `resources_discover` 事件，扩展可贡献资源路径，合并进 `SkillLoader` 扫描列表 |
| **`before_agent_start`** | 注入 message + 链式改写 system prompt | 只有 `AddPromptGuideline`（追加一行，不能链式改 / 不能注入消息）| 加 `before_agent_start` 事件，支持注入消息 + 改写 systemPrompt |

### P1：provider 层扩展（扩展能管模型/provider）

| 能力 | pi 机制 | Phi 现状 | 建议 |
|---|---|---|---|
| **`pi.registerProvider()` / `unregisterProvider()`** | 扩展动态注册 LLM provider（含 models、费用、transport）| `SwitchProvider` 只能切已有 provider，不能注册新的 | 加 `RegisterProvider` / `UnregisterProvider`，扩展可注入自定义 provider |
| **`before_provider_headers`** | 改写每次 provider 请求的 HTTP headers | — | 加 provider 层 hook |
| **`before_provider_request`** | 检查 / 替换 provider payload | — | 加 provider 层 hook |
| **`after_provider_response`** | 查看响应状态 / headers | — | 加 provider 层 hook |
| **`pi.setThinkingLevel()` / `getThinkingLevel()`** | 读 / 设思考级别 | 只有 `ThinkingLevelChangedEvent`（观察，不能设）| 加 setter |

### P2：会话树导航（扩展能操作 session 生命周期）

| 能力 | pi 机制 | Phi 现状 | 建议 |
|---|---|---|---|
| **`ctx.newSession()` / `fork()` / `navigateTree()` / `switchSession()`** | 扩展能创建 / fork / 导航会话树 | `ISession` 有 `NewSessionAsync` / `ResumeAsync`，但 `IPhiContext` 未暴露给扩展 | `IPhiContext` 加导航方法 |
| **`session_before_switch`** | 可取消 /new /resume | — | 加事件 |
| **`session_before_fork` / `session_tree` / `session_before_tree`** | fork / 树导航 hook | — | 加事件 |
| **`session_before_compact`** | 可取消 / 自定义压缩摘要 | `CompactionStartEvent` 只有观察 | 加可干预版本 |

### P3：自定义 UI（扩展能深入渲染）

| 能力 | pi 机制 | Phi 现状 | 建议 |
|---|---|---|---|
| **`ctx.ui.custom()`** | 扩展渲染自定义 TUI 组件（键盘输入、复杂交互）| `IPhiUiBridge` 只有 select/confirm/input | 加 custom 渲染桥（双端） |
| **`ctx.ui.setStatus()` / `setWidget()`** | 底部状态 / 编辑器上方部件 | — | 加 status/widget 槽 |
| **`pi.registerMarkdownTransformer()`** | 转换 assistant markdown 渲染 | — | 加 transformer |

### P4：杂项（小但 pi 有）

| 能力 | pi 机制 | Phi 现状 | 建议 |
|---|---|---|---|
| **`pi.registerShortcut()`** | 注册键盘快捷键 | — | 加（TUI） |
| **`pi.registerFlag()`** | 注册 CLI flag | — | 加 |
| **`ctx.getContextUsage()`** | 查 token 用量 | `IPhiContext` 无 | 加 |
| **`ctx.compact()`** | 触发压缩 | — | 加 |
| **`ctx.signal`** | 取消信号（Esc 传播）| `CancellationToken` 未暴露给扩展 | 加 |
| **`model_select` 事件** | 模型切换 hook | `SessionInfoChangedEvent` 含 model 变化，无独立事件 | 加独立事件 |
| **`user_bash` 事件** | 拦截 `!` / `!!` 命令 | — | 加 |

---

## 4. 需裁剪 / 修正（当前实现与 pi 不一致）

| 项目 | 现状 | 问题 | 建议 |
|---|---|---|---|
| **`RegisterToolCard`** | 独立的 tool card 渲染注册 | pi 用 `registerEntryRenderer`（统一按 customType），没有专门的 tool card 概念 | 保留（Phi 已有 UI 侧 tool card 体系），但不必强行对齐 pi——pi 的 tool card 就是 entry renderer |
| **`SubmitTranscriptLine`** | 独立的 transcript 行提交 | pi 用 `appendEntry` + `registerEntryRenderer` 覆盖 | 保留（Phi 的 transcript 行是 UI 投影，pi 的 entry 是持久化——概念不同但可共存）|
| **`SwitchModel` / `SwitchProvider`** | 切模型 / 切 provider | pi 的 `setModel` 是 session 级，`registerProvider` 是全局注册——语义不同 | `SwitchModel` 保留对齐 `setModel`；`SwitchProvider` 保留但明确它不是 `registerProvider` 的替代 |
| **`IPhiContext.Ui`** | 只有 dialog | pi 的 `ctx.ui` 是完整 UI 桥（含 custom/status/widget）| 扩展 `IPhiUiBridge` 到完整面 |
| **`ThinkingLevelChangedEvent`** | 只有观察事件 | pi 是 setter + event | 加 setter |

---

## 5. 建议的推进顺序

按"扩展生态收益 / 实现成本"排序：

1. **P0a：`resources_discover` 事件**（低成本，让扩展能贡献 skill/prompt 路径）
   - 加 `resources_discover` 到事件系统，`SkillLoader` 合并扩展贡献的目录
   - 与 pi 对齐：skill 仍是文件系统资源，扩展只贡献路径

2. **P0b：`before_agent_start`**（中成本，注入消息 + 链式 system prompt）
   - 加事件，handler 可返回 `{ message, systemPrompt }`
   - `SystemPromptBuilder` 支持链式改写

3. **P1：provider 层**（中成本，`registerProvider` + provider hooks）
   - 加 `RegisterProvider` / `UnregisterProvider`
   - 加 `before_provider_headers` / `before_provider_request` / `after_provider_response`

4. **P2：会话树导航**（中成本，扩展能操作 session）
   - `IPhiContext` 加导航方法（复用现有 `ISession.NewSessionAsync` / `ResumeAsync`）
   - 加 `session_before_switch` 等事件

5. **P3：自定义 UI**（高成本，custom/status/widget）
   - 双端 UI 桥扩展
   - 依赖 TUI / Avalonia 的组件能力

6. **P4：杂项**（低-中成本，shortcut/flag/contextUsage/compact/signal/model_select/user_bash）

---

## 6. 明确不做的（pi 也没有，或 Phi 故意不跟）

| 项目 | 原因 |
|---|---|
| **MCP 进核心** | pi 不提供 MCP（扩展自引 SDK）。Phi 的 `McpPack` 独立扩展已符合 pi。**MCP 保持现状** |
| **扩展内嵌 skill 字符串** | pi 的 skill 是文件系统 `SKILL.md`，扩展只贡献路径。Phi 遵循同一模型 |
| **扩展 = systemPrompt+tools+skills+mcp 垂直打包** | pi 的扩展是水平注册制（每个能力独立 `pi.xxx()`），不是"一个扩展 = 一个完整 agent"。**放弃垂直打包设想** |
| **Capability 强制（v1.5 strict）** | pi 无此概念；Phi 保留为可选增强，不作为默认 |

---

## 7. 对齐检查清单（每项 ✅/⏳）

- [x] `registerTool` → `RegisterTool`
- [x] `registerCommand` → `RegisterCommand` + dispatcher
- [x] `on(event, handler)` → `On`
- [x] `registerMessageRenderer` / `registerEntryRenderer` → `RegisterMessageRenderer` / `RegisterTranscriptLineRenderer`
- [x] `appendEntry` → `AppendEntryAsync`
- [x] `sendUserMessage` / `sendMessage` → `SubmitUserMessage` / `SubmitCustomMessage`
- [x] `ctx.ui`（基础 dialog）→ `IPhiUiBridge`
- [x] `ctx.hasUI/cwd/model/isIdle` → `IPhiContext`
- [x] 核心事件（session/tool/turn/agent/message/compaction）→ `PhiEvent`
- [x] `/reload` → `ReloadExtensions`
- [x] project trust → `ProjectTrustGate`
- [x] skills 文件系统发现 → `SkillLoader`
- [x] MCP 保持独立扩展（不进核心）→ `McpPack`
- [ ] `resources_discover` → 缺
- [ ] `before_agent_start` → 缺
- [ ] `registerProvider` / `unregisterProvider` → 缺
- [ ] provider 层 hooks（headers/request/response）→ 缺
- [ ] `setThinkingLevel` → 缺（只有观察事件）
- [ ] `ctx.newSession/fork/navigateTree/switchSession` → 缺
- [ ] `session_before_switch/fork/compact/tree` → 缺
- [ ] `ctx.ui.custom/setStatus/setWidget` → 缺
- [ ] `registerMarkdownTransformer` → 缺
- [ ] `registerShortcut` / `registerFlag` → 缺
- [ ] `ctx.getContextUsage/compact/signal` → 缺
- [ ] `model_select` / `user_bash` 事件 → 缺
