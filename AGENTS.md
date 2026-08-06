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

PhiTuiApp ─→ PageRegistry ─→ SessionPage ─→ PromptInput ─┐
   (TUI)      (路由算法：     (页面/屏幕      (输入组件：  │
               AppRoute →     控制器：        editor +    │
               IPage 解析)    自含视图/        slash 分发/ │
                             状态/交互)      对话框/      ├─→ ISession ─→ CodingSession ─→ Harness ─→ Provider
                          ┌─ NewSessionPage  skill 补全) │    (接口，    (session +   (dispatch)  (LLM)
                          └─ SessionPage    ↓            │    已水合的    harness +
                                            ChatHeader    │    model)     provider + queue)
                                            (chrome)
    └─→ SessionNavigator ─→ CodingSessionFactory ─→ CodingSession
        (导航：构建目标      (组装 runtime:
         session、dispose    资源/tools/prompt/harness)
         旧 session、触发
         RouteChanged)

路由是强类型判别联合 `AppRoute`（`ChatRoute(NewSessionRequest | ExistingSessionRequest(id))`），
PageRegistry 把路由族解析成页面（route→page）：
- `ChatRoute(NewSessionRequest)` → NewSessionPage（/sessions/new 落地页：居中 editor，无 transcript）
- `ChatRoute(ExistingSessionRequest(id))` → SessionPage（/sessions/:id 详情页：transcript + editor + status）
页面每次导航都新建一个实例，绑定那条已水合的会话；两页都用 `PromptInput`（组合，不是继承）
复用输入壳——editor/slash 分发/对话框/skill 补全，靠三条回调把"提交/信息/排队"交给页面。

session 切换（/new、resume）就是路由跳转：SessionNavigator 用 factory 构建目标 session、
dispose 旧 session、触发 RouteChanged；TUI 据此重建绑定当前路由的 page。
新建页提交首条 prompt 后"晋升"到详情路由：导航到当前 session 自己的 id 时 navigator
直接采纳内存中的同一条（不重建、不取消、不 dispose），并把提交文本经 pending submission
带给详情页渲染用户气泡。
CodingSession 只代表"一条活着的会话"，不再自己换身份。

每层只依赖下一层，不跨层。PhiTuiApp 不知道 Harness 的存在，Session 不知道 Provider 的存在。
这样前后端分离，各司其职，未来要新增前端也好做；新增页面 = 加一个路由族 + 一个 IPage 实现 + PageRegistry 一行。

### 目录约定（仿 Next.js 的 pages / components / lib）

UI 代码统一放在 `PhiCoding/Tui/` 下，非 UI 代码放在 `PhiCoding/` 根、`Sessions/`、`Providers/`、`Routing/`：
- `Tui/Pages/`（Next.js pages/）：路由绑定的屏幕——IPage、SessionPage、NewSessionPage，以及 route→page 的 PageRegistry（页面清单放页面旁边，避免非 UI 的 Routing 引用 UI）
- `Tui/Components/`（Next.js components/）：可复用积木——PromptInput（输入壳：editor + slash 分发 + 对话框 + skill 补全）、ChatHeader、ChatTranscript、PhiStatusBar、SuggestionStrip、suggestion providers、ToolCards/
- `Tui/` 根：应用壳 PhiTuiApp + 基础设施（SelectionCopyHost、ToastHostSentinel、SystemClipboard、ErrorClassifier、SlashCommands、SlashCommandCatalog）
- `Routing/`：只剩纯路由值类型 AppRoute（无 UI 依赖）
- 页面由组件构建：SessionPage/NewSessionPage 组合 PromptInput，靠三条回调（提交/信息/排队）把差异交给页面

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