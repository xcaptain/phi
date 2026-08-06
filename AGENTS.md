# Phi Agent Instructions

Phi 是一个 C# 版本的 基于 pi agent 架构的最小 coding agent，目标是从头到尾自己实现一个 coding agent，方便我理解开发中的每个细节

## 架构

### 整体架构和依赖关系

- PhiProvider: 手写的 llm provider，对应 tau_ai 目录，我不喜欢 tau_ai 这个名字，因为看不出来是负责跟llm provider通信的
- PhiAgent: 对应 tau_agent 目录，自己实现的一个 harness, 状态管理，循环
- PhiCoding: 一个终端界面的 coding agent，前端用户使用的就是这个项目，基于 XenoAtom.Terminal.UI 这个库开发的界面

依赖关系：

PhiCoding 依赖 PhiProvider + PhiAgent
PhiProvider 依赖 PhiAgent
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

└─→ SessionNavigator ─→ CodingSessionFactory ─→ CodingSession
    (拥有当前 session     (组装 runtime:
     生命周期：cancel +    资源/tools/prompt/
     await + dispose；      harness)
     /new 走 NavigateToNewAsync、
     /sessions/:id 走 ResumeAsync(id))

### TUI 渲染

PhiTuiApp 持有一个 `State<ISession>`，由 SessionNavigator 的 SessionChanged
事件在 UI 线程翻转。一个 ComputedVisual 读这个 State，navigate 时自动重建整页（header + transcript
+ editor + strip + status bar）。空 session 的内容槽显示一行 slogan，提交首条 prompt 后被 user bubble
自动替换——session 立刻有自己的 id，详情路径不再需要。

### 目录约定

UI 代码统一放在 `PhiCoding/Tui/` 下，非 UI 代码放在 `PhiCoding/` 根、`Sessions/`、`Providers/`：
- `Tui/Components/`：可复用积木——PromptInput（输入壳：editor + slash 分发 + 对话框 + skill 补全）、ChatHeader、ChatTranscript、PhiStatusBar、StatusBarBinder（session state → status bar 接线，错误去重 + 分类）、SuggestionStrip、suggestion providers、ToolCards/
- `Tui/` 根：应用壳 PhiTuiApp + 基础设施（SelectionCopyHost、ToastHostSentinel、SystemClipboard、ErrorClassifier、SlashCommands、SlashCommandCatalog）
- `Sessions/`：ISession、ISessionNavigator、SessionNavigator、CodingSessionFactory — 拥有 session 生命周期
- `Providers/`：ProviderManager、ProviderCatalog

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