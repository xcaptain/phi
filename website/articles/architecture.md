# 架构概览

## 设计目标

Phi 是一个「可学习」的 coding agent — 优先考虑代码可读性、最小依赖、清晰的边界，而不是功能和规模。

参考 [pi](https://github.com/badlogic/pi-mono) 与 [tau](https://github.com/gearhead-works/tau) 的设计，把 agent runtime 拆成几块互不依赖、可以单独替换的部分。

## 包划分

| 包 | 职责 |
|----|------|
| `Phi.Agent` | 最底层：消息类型、Harness、agent loop、provider event 协议 |
| `Phi.Provider` | LLM 通信：Anthropic、OpenAI 兼容协议、retry、null provider |
| `Phi` | UI-agnostic runtime：Session / Tools / Prompts / Status router / Slash commands / Chat projector |
| `Phi.Tui` | 终端 UI，XenoAtom.Terminal.UI 实现 |
| `Phi.Avalonia.Desktop` | Avalonia 桌面 UI（classic desktop lifetime） |
| `Phi.Extensions` | 扩展 SDK（事件、命令、工具注册抽象） |
| `Phi.Extensions.Host` | 扩展加载器：`AssemblyLoadContext` + hot reload |
| `Phi.SchemaGen` | 工具参数 JSON schema 生成器（编译期） |

依赖关系：

```
        Phi.Tui ──┐
                  ├─► Phi ─► Phi.Provider ─► Phi.Agent
Phi.Avalonia.Desktop ──┘

Phi.Extensions.Host ─► Phi.Extensions ─► Phi
```

## 关键子系统

### Session / Chat Transcript
`Phi/Session.cs` 管理消息历史，`ChatTranscriptProjector` 把消息流投影成 UI 行。两个 UI 各自按 `ChatLine.Id` diff 渲染。

### 上下文压缩
`Phi/CompactionSummarizer.cs` + `CompactionPlanner`：会话过长时自动压缩，保留关键信息。

### Provider 抽象
`IPhiProvider`（在 `Phi.Agent`）暴露流式事件；`Phi.Provider` 实现具体的 LLM 协议。Provider 协议转换与 agent runtime 完全解耦。

### 扩展系统
详见 [扩展系统](extensions.md)。扩展用独立的 `AssemblyLoadContext` 加载，避免污染主程序集，支持热重载。

### Slash 命令 / Skill Suggestion
`Phi/Prompt/SlashCommandProvider.cs` 提供 `/` 触发；`SkillSuggestionProvider` 在用户输入时给出提示。

## UI 差异

| 维度 | TUI | Avalonia |
|------|-----|----------|
| 渲染 | DocumentFlow | StackPanel |
| Markdown | XenoAtom.Terminal.UI Markdown ext | `MarkView.Avalonia` |
| 图标 | n/a | `Material.Icons.Avalonia` |
| 主题 | 跟随终端 | `SukiUI`，light/dark 跟随系统 |
| 语义色 | 各自硬编码 | `Phi.Avalonia.AvaloniaTheme` 统一映射 SukiUI |

## 后续文章

- [扩展系统](extensions.md)
- [与 pi extensions 对齐](pi-extensions-align.md)