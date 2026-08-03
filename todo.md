# Phi 扩展性改造计划

## 背景

Phi 当前已经基本可用，但很多内容是写死的：

- 系统提示词硬编码在 `PhiCoding/Program.cs:66`
- 工具列表是静态枚举，新增工具需要同时改 `BuiltInTools.cs`、`Program.cs` 提示词和 `ToolCardRegistry`
- AGENTS.md、skills、prompt templates、MCP 都没有注入
- `PhiAgent` 协议层足够干净，可以直接复用

参考 `tau/src/tau_coding/session.py` 和 `tau/src/tau_coding/system_prompt.py` 的设计思路，制定如下分阶段改造计划。

## 设计原则

1. 每层只依赖下一层，遵循 `AGENTS.md:19` 的分层约束
2. `PhiAgent` 保持最小，不增加 prompt 元数据
3. `SystemPromptBuilder` 是纯构建器，不直接读取磁盘
4. 工具组合必须在 system prompt 构建之前完成
5. 资源发现结果形成一次性 `SessionResources` 快照
6. C# 是静态语言，不做运行时动态扩展 / 动态加载 DLL，扩展通过编译期接口和进程外集成（MCP）
7. 恢复 session 时使用当前资源重新构建 system prompt

## 总体目录结构

```text
PhiCoding/
  Sessions/
    CodingSessionFactory.cs
    SessionRuntime.cs

  Prompts/
    SystemPromptBuilder.cs
    SystemPromptBuildContext.cs
    SystemPromptOptions.cs
    ISystemPromptContributor.cs

  Resources/
    SessionResources.cs
    SessionResourceLoader.cs
    ProjectContextLoader.cs
    ResourceDiagnostic.cs

  Skills/
    SkillDescriptor.cs
    SkillLoader.cs
    SkillExpander.cs

  PromptTemplates/
    PromptTemplate.cs
    PromptTemplateLoader.cs
    PromptTemplateExpander.cs

  Tools/
    ToolContribution.cs
    ToolCapabilities.cs
    IToolProvider.cs
    ToolComposer.cs
    BuiltInToolProvider.cs

  Integrations/
    IIntegrationRuntime.cs
    Mcp/
      McpRuntime.cs
      McpToolProvider.cs
      McpTool.cs
```

## 推荐的实施阶段

| 阶段 | 内容 | 关键产出 |
| --- | --- | --- |
| 1 | SystemPromptBuilder | 可测试的 builder；替换硬编码字符串 |
| 2 | ToolContribution 与 cwd-bound tools | 工具元数据 + 统一 cwd |
| 3 | AGENTS.md 与 SessionResources | 项目上下文发现 + 诊断 |
| 4 | CodingSessionFactory | 将构建管线从 session 中抽离 |
| 5 | Skills | 渐进披露 + `/skill:name` |
| 6 | Prompt templates + `/reload` | 用户输入宏 |
| 7 | MCP tools | 进程外扩展 |
| 8 | 扩展 API | 仅做进程内编译期注册；不做动态加载 |

---

## Phase 1：SystemPromptBuilder

### 目标

把 `PhiCoding/Program.cs:66` 的硬编码提示词替换成可测试的 builder，使提示词能跟随实际工具集合自动生成。

### 关键设计

- 三层提示语义：
  - `ResolvedSystemPrompt`：最终覆盖，完全跳过 builder
  - `CustomBasePrompt`：替换默认基础提示，但保留 cwd / 项目上下文 / skills
  - `AppendSystemPrompt`：在默认或自定义基础提示之后追加
- 区分 `null` 和空字符串
- 日期通过参数传入，测试可确定
- builder 不读磁盘，只消费 `SystemPromptBuildContext`

### 类型

```csharp
public sealed record SystemPromptOptions
{
    public string? ResolvedSystemPrompt { get; init; }
    public string? CustomBasePrompt { get; init; }
    public string? AppendSystemPrompt { get; init; }
}

public sealed record SystemPromptBuildContext
{
    public required string Cwd { get; init; }
    public required DateOnly CurrentDate { get; init; }
    public required IReadOnlyList<ToolContribution> Tools { get; init; }
    public required IReadOnlyList<SkillDescriptor> Skills { get; init; }
    public required IReadOnlyList<ProjectContextFile> ContextFiles { get; init; }
    public required SystemPromptOptions Options { get; init; }
}

public interface ISystemPromptBuilder
{
    string Build(SystemPromptBuildContext context);
}
```

### 默认提示顺序

1. 默认身份与能力 / `CustomBasePrompt`
2. `AppendSystemPrompt`
3. `<project_context>`
4. `<available_skills>`（仅当存在具有 `ReadLocalFiles` 能力的工具时）
5. 当前日期
6. 当前 cwd

### 工具列表生成规则

- 每个工具生成一行：`name: description`
- 来源：`ToolContribution.PromptSnippet ?? Tool.Description`
- 按 `Source` + `Name` 稳定排序

### guidelines 生成规则

- 收集每个工具的 `PromptGuidelines`
- 按出现顺序去重
- 工具名推断能力改为使用 `ToolCapabilities`，避免依赖工具命名约定

### 测试用例

- 默认提示包含身份、工具列表、guidelines
- `ResolvedSystemPrompt` 不为空时 builder 不被调用
- `CustomBasePrompt = ""` 仍然替换默认基础提示
- `CustomBasePrompt = null` 走默认基础提示
- `AppendSystemPrompt` 顺序正确
- 项目上下文出现在基础提示之后
- skills 索引只在具有 `ReadLocalFiles` 能力时出现
- 日期和 cwd 固定在末尾
- 工具 guideline 按出现顺序去重

### 替换点

- `PhiCoding/Program.cs:66` 删除硬编码字符串，传入 `SystemPromptOptions` 默认值
- `PhiCoding/SessionConfig.cs:27` 的 `SystemPrompt` 字段类型改为 `SystemPromptOptions`

---

## Phase 2：ToolContribution 与 cwd-bound tools

### 目标

- 工具同时携带 provider schema 和 prompt metadata
- 所有工具在创建时绑定 session cwd
- 修复 prompt 中的 cwd 与工具实际 cwd 不一致问题

### 关键设计

```csharp
[Flags]
public enum ToolCapabilities
{
    None = 0,
    ReadLocalFiles = 1 << 0,
    WriteLocalFiles = 1 << 1,
    ExecuteCommands = 1 << 2,
}

public sealed record ToolContribution
{
    public required Tool Tool { get; init; }
    public string? PromptSnippet { get; init; }
    public IReadOnlyList<string> PromptGuidelines { get; init; } = [];
    public ToolCapabilities Capabilities { get; init; }
    public string Source { get; init; } = "builtin";
}

public interface IToolProvider
{
    ValueTask<IReadOnlyList<ToolContribution>> GetToolsAsync(
        ToolProviderContext context,
        CancellationToken cancellationToken);
}
```

### cwd 处理

- `BuiltInTools.CreateDefault(string cwd)` 替代 `CreateDefault()`
- `BashTool` 设置 `ProcessStartInfo.WorkingDirectory`
- read / write / edit 通过统一 `IWorkspacePathResolver` 解析路径
- 相对路径全部基于 session cwd

### duplicate policy

- 第一版：重复 tool name 直接抛异常
- 命名空间保留：MCP 工具使用 `mcp__<server>__<tool>`

### 测试用例

- `BuiltInTools.CreateDefault(cwd)` 中 bash working directory 等于 cwd
- read 接受 session-root 相对路径
- write / edit 拒绝逃逸 session cwd 的路径（`..`）
- 重复 tool name 抛异常
- capability 在 builder 中影响 skills 索引

### 替换点

- `PhiCoding/BuiltInTools.cs:6`
- `PhiCoding/Tools/BashTool.cs`
- `PhiCoding/Tools/ReadTool.cs`、`WriteTool.cs`、`EditTool.cs`
- `PhiCoding/Tui/ToolCards/ToolCardRegistry.cs:10` 后续可改造为 registry（Phase 4 或之后）

---

## Phase 3：AGENTS.md 与 SessionResources

### 目标

发现并注入项目层级的 AGENTS.md，支持 global → project root → nested cwd 顺序。

### 关键设计

```csharp
public sealed record ProjectContextFile(string AbsolutePath, string Content);

public sealed record ResourceDiagnostic(
    string Source,
    string Message,
    DiagnosticSeverity Severity);

public sealed record SessionResources
{
    public IReadOnlyList<ProjectContextFile> ContextFiles { get; init; } = [];
    public IReadOnlyList<SkillDescriptor> Skills { get; init; } = [];
    public IReadOnlyList<PromptTemplate> PromptTemplates { get; init; } = [];
    public IReadOnlyList<ResourceDiagnostic> Diagnostics { get; init; } = [];
}

public interface ISessionResourceLoader
{
    ValueTask<SessionResources> LoadAsync(
        SessionResourceOptions options,
        CancellationToken cancellationToken);
}
```

### AGENTS.md 发现顺序

1. `~/.phi/AGENTS.md`
2. `~/.agents/AGENTS.md`
3. 项目根 `AGENTS.md`
4. 项目根到 cwd 之间每层目录的 `AGENTS.md`
5. `<cwd>/.phi/AGENTS.md`
6. `<cwd>/.agents/AGENTS.md`

### 项目根 marker

至少支持：

- `.git`
- `*.sln` / `*.slnx`
- `*.csproj`
- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `package.json`
- `pyproject.toml`
- `Cargo.toml`
- `go.mod`

### monorepo 注意

- 优先 `.git` 作为 root marker
- 不要在最近的 csproj 处截断

### 去重

- 规范化为绝对路径
- 使用跨平台大小写不敏感的 comparer
- 单个文件读失败时产生 diagnostic，不阻塞整个 session

### 注入格式

```xml
<project_context>
  <project_instructions path="/absolute/path/AGENTS.md">
    ...
  </project_instructions>
</project_context>
```

### 测试用例

- global 在前，project 在后
- 多层 nested AGENTS.md 全部包含
- canonical path 去重
- 单个文件损坏产生 diagnostic 但不抛异常
- monorepo 中 `.git` 优先
- cwd 在 home 下时使用绝对路径

### 替换点

- 新增 `PhiCoding/Resources/` 目录
- `SystemPromptBuildContext` 增加 `ContextFiles` 字段

---

## Phase 4：CodingSessionFactory

### 目标

把 resources → tools → prompt → harness 构建流程从 `Program.cs` 和 `CodingSession` 构造逻辑中抽离，使 CodingSession 只接收已构建好的 runtime。

### 关键设计

```csharp
public interface ICodingSessionFactory
{
    ValueTask<CodingSession> CreateAsync(
        CodingSessionCreateOptions options,
        CancellationToken cancellationToken);

    ValueTask<CodingSession> ResumeAsync(
        CodingSessionResumeOptions options,
        CancellationToken cancellationToken);
}
```

### 内部构建顺序

```text
1. resourceLoader.LoadAsync(...)        // AGENTS.md + 未来 skills/templates
2. integrationRuntime.StartAsync(...)   // 未来 MCP
3. toolComposer.ComposeAsync(...)       // built-ins + integrations
4. systemPromptBuilder.Build(...)       // 使用上面所有结果
5. new Harness(provider, tools, model, prompt, maxTurns)
6. new CodingSession(harness, storage, record, ...)
```

### CodingSession 职责收缩

`CodingSession` 不再负责：

- 扫描 skills
- 解析 MCP 配置
- 发现 AGENTS.md
- 拼接 system prompt
- 创建工具

仍负责：

- state machine
- 持久化
- 队列
- resume / switch model / switch provider
- 自动命名
- 自动 compact
- dispose

### resume 时重建 prompt

- 不持久化 resolved system prompt
- resume 时使用当前资源 + 当前工具重新构建
- 可选持久化 `PromptRevision`（builder version + prompt hash）以做审计

### resume 时 provider/model 修正

- 当前 `CodingSession.Resume(config, id)` 直接使用传入 config 的 provider/model
- 应改为：先读 `SessionRecord`，再用 record 的 provider/model 解析 runtime
- 显式 CLI override 才覆盖 record

### 测试用例

- factory 创建的 session 与旧 `CodingSession.Create` 行为一致
- resume 时使用当前 AGENTS.md
- resume 时 record provider/model 生效
- 显式 `--provider` 覆盖 record

### 替换点

- 新增 `PhiCoding/Sessions/CodingSessionFactory.cs`
- `PhiCoding/Program.cs` 改用 factory
- `PhiCoding/CodingSession.cs` 构造逻辑收敛

---

## Phase 5：Skills

### 目标

兼容 Agent Skills 目录结构，仅在 system prompt 注入 name / description / location，正文按需通过 read 工具读取。

### 关键设计

```csharp
public sealed record SkillDescriptor(
    string Name,
    string Description,
    string AbsolutePath,
    SkillSource Source);
```

### 目录形式

```text
<skill-root>/<skill-name>/SKILL.md
```

### 搜索位置

1. `~/.phi/skills`
2. `~/.agents/skills`
3. `<project>/.phi/skills`
4. `<project>/.agents/skills`

### precedence

- 项目级 > 用户级
- `.phi` > `.agents`
- 同名覆盖产生 diagnostic

### 渐进披露

system prompt 只包含：

```xml
<available_skills>
  <skill>
    <name>...</name>
    <description>...</description>
    <location>/absolute/path/SKILL.md</location>
  </skill>
</available_skills>
```

### capability gate

仅当存在 `ReadLocalFiles` 能力的工具时注入 skills 索引。

### `/skill:name`

展开后的内容作为 user message 进入 transcript 并持久化。

### 测试用例

- 同名 skill 按 precedence 覆盖
- 损坏 SKILL.md 不阻塞
- 仅有 read 工具时 skills 索引才出现
- `/skill:foo` 展开后正文进入 transcript

---

## Phase 6：Prompt templates + `/reload`

### 目标

- 用户输入宏与 system prompt 解耦
- `/reload` 重新加载 resources、tools、prompt

### 关键设计

- 模板仅作为 user message 展开，不进入 system prompt
- `{{ arguments }}` / `{{ args }}` 占位符
- 没有占位符时，参数附加到模板正文后
- 与内置 slash commands 做保留名检查

### `/reload`

- 每次完整重建 resources、tools 和 system prompt
- 不做复杂 diff，避免 tau reload signature 的脆弱性
- diagnostics 重新汇总

### 测试用例

- 模板展开后正文不包含 `{{ }}`
- 模板错误产生 diagnostic
- `/reload` 后 prompt 反映新的 AGENTS.md

---

## Phase 7：MCP tools

### 目标

通过进程外 server 接入第三方工具，不污染 `PhiAgent`。

### 边界

```text
MCP SDK / transport
→ McpRuntime
→ McpToolProvider
→ ToolContribution[]
→ Harness
```

### 设计要点

- stdio transport（第一版）
- `tools/list` + schema 转换
- 调用 MCP tool 时 timeout / cancellation
- `IAsyncDisposable` 释放 server 进程
- 工具命名：`mcp__<server>__<tool>`
- 项目级 MCP 配置默认需要用户确认
- 用户级 MCP 配置可标记 trusted

### 不做的事

- 不自动把 MCP resources 注入 system prompt
- 不支持同名覆盖内置工具

### 测试用例

- fake MCP server 起停
- schema 转换正确
- cancellation 传递
- 重名时抛异常

---

## Phase 8：扩展 API

### 目标

仅支持进程内编译期注册，不做动态 DLL 加载 / `AssemblyLoadContext`。

### 关键设计

```csharp
public interface IPhiExtension
{
    void Configure(PhiExtensionBuilder builder);
}

public sealed class PhiExtensionBuilder
{
    public void AddToolProvider(IToolProvider provider);
    public void AddResourceProvider(ISessionResourceProvider provider);
    public void AddPromptContributor(ISystemPromptContributor provider);
}
```

### 不做的事

- 不支持动态加载 DLL
- 不支持 NativeAOT 之外的程序集隔离
- 不复制 tau Python 扩展运行时

---

## Phase 1 + 2 + 3 的具体下一步

按你的要求，第一阶段先做这三件事：

1. **Phase 1：SystemPromptBuilder**
   - 新建 `PhiCoding/Prompts/SystemPromptOptions.cs`
   - 新建 `PhiCoding/Prompts/SystemPromptBuildContext.cs`
   - 新建 `PhiCoding/Prompts/ISystemPromptBuilder.cs`
   - 新建 `PhiCoding/Prompts/SystemPromptBuilder.cs`
   - 在 `PhiCoding.Tests/Prompts/` 下补 unit tests
   - 改 `PhiCoding/Program.cs:66`，使用 builder
   - 改 `PhiCoding/SessionConfig.cs`，类型从 `string` 改为 `SystemPromptOptions`

2. **Phase 2：ToolContribution 与 cwd-bound tools**
   - 新建 `PhiCoding/Tools/ToolCapabilities.cs`
   - 新建 `PhiCoding/Tools/ToolContribution.cs`
   - 新建 `PhiCoding/Tools/IToolProvider.cs`
   - 新建 `PhiCoding/Tools/BuiltInToolProvider.cs`
   - 改 `PhiCoding/BuiltInTools.cs`，接受 cwd 参数
   - 改 `PhiCoding/Tools/BashTool.cs`，设置 working directory
   - 在 `PhiCoding.Tests/Tools/` 下补 cwd 测试

3. **Phase 3：AGENTS.md 与 SessionResources**
   - 新建 `PhiCoding/Resources/ProjectContextFile.cs`
   - 新建 `PhiCoding/Resources/ResourceDiagnostic.cs`
   - 新建 `PhiCoding/Resources/SessionResources.cs`
   - 新建 `PhiCoding/Resources/SessionResourceLoader.cs`
   - 新建 `PhiCoding/Resources/ProjectContextLoader.cs`
   - `SystemPromptBuildContext` 增加 `ContextFiles` 字段
   - 在 `PhiCoding.Tests/Resources/` 下补 discovery 测试

## 开发约定

- 每个 phase 之前先写测试用例，遵循 `AGENTS.md:38` 的开发工作流
- 每个 phase 完成后执行 `dotnet test` 确认所有测试通过
- 不增加 prompt 元数据到 `PhiAgent.Tool`
- 不做动态扩展 / 动态 DLL 加载
- 不复制 tau 的巨型 `CodingSession`
- 复用现有 `PhiSchemaGen`，不引入新的 schema 生成机制