# Phi Agent Instructions

Phi 是一个 C# 版本的 基于 pi agent 架构的最小 coding agent，目标是从头到尾自己实现一个 coding agent，方便我理解开发中的每个细节

## 架构

### 整体架构和依赖关系

- Phi.Provider: 手写的 llm provider，对应 tau_ai 目录，我不喜欢 tau_ai 这个名字，因为看不出来是负责跟llm provider通信的
- Phi.Agent: 对应 tau_agent 目录，自己实现的一个 harness, 状态管理，循环
- Phi: Library，提供 UI-agnostic runtime（sessions / providers / tools / prompts / status router / slash commands / tool descriptors / chat projector）。无 UI 框架依赖。
- Phi.Avalonia.Desktop: Avalonia 的桌面平台入口 exe（classic desktop lifetime），引用 Phi。所有 UI 内联在 exe 内——不依赖共享组件库，因为目前没有跨平台 UI 复用的需求。

UI 框架选择：**Avalonia**（跨平台，支持 Windows / macOS / Linux）。`Phi.Avalonia.Desktop` 是唯一的 Avalonia 桌面客户端实现；老的 `Phi.Avalonia` / `Phi.Avalonia.Tests` 已删除。

依赖关系：
```
        Phi.Tui ──┐
                  ├─► Phi ─► Phi.Provider ─► Phi.Agent
        Phi.Avalonia.Desktop ──┘
```

`Phi.Agent` 是最底层的 package，依赖最少，可以注入不同的 provider 使用，可以随意分发。


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
- pi: ~/github/pi

## 文档与博客

- 所有 markdown 设计文档 / 说明 / 博客文章一律写到 `website/` 下：
  - 设计文档 / 说明 → `website/articles/`
  - 博客 → `website/blog/`
  - 新增条目记得在对应 `toc.yml` 里登记
- 站点构建：`dotnet docfx build website/docfx.json`（产物 `website/_site/`，已 gitignore）。
- 站点预览（构建 + 起本地服务器）：`dotnet docfx build website/docfx.json --serve`。
- 站点部署：CI 在 push to `main` 后自动构建并部署到 GitHub Pages，地址 https://phi.154839.xyz。
  - 触发条件：仅 `website/**` 或本 workflow 文件变更。
  - 不发布 API 参考（项目目前公开类型未带 XML doc；如需 API 页，先给 csproj 加 `<GenerateDocumentationFile>true</GenerateDocumentationFile>` + 给公开类型补 XML doc，再恢复 docfx.json 的 metadata 段）。

## 桌面 UI 差异（Avalonia vs TUI）

- Avalonia 有成熟的控件体系：Markdown 通过 `MarkView.Avalonia` 渲染，图标通过 `Material.Icons.Avalonia` 渲染，主题走 `SukiUI`（`SukiTheme` + `SukiWindow`，light / dark 自动跟随系统；`PhiAvaloniaApp.axaml` 用 `<suki:SukiTheme ThemeColor="Blue"/>`）。
- 语义色统一在 `Phi.Avalonia.AvaloniaTheme`（`TextSecondary` / `Danger` / `Success` / `Accent` / `ControlBorder` / `ContainerBackground` 等），明暗 hex 对映射 SukiUI 色板，light/dark 跟随 `Application.ActualThemeVariant`；没有 TUI ANSI 命名的色板。
- 两个 UI 共用 `ChatTranscriptProjector` 投影：各自按 `ChatLine.Id` DIFF 渲染（TUI→DocumentFlow，Avalonia→StackPanel）。
