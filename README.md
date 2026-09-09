# Phi agent

[![Build Status](https://img.shields.io/github/actions/workflow/status/xcaptain/phi/ci.yml?branch=main&label=build)](https://github.com/xcaptain/phi/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/xcaptain/phi/graph/badge.svg)](https://codecov.io/gh/xcaptain/phi)

参考 tau agent 使用 C# 实现的一个 coding agent

## 起因

- pi agent 是一个生产可用的agent，它的理念很先进，上下文管理做得好，所以token 消耗很少
- tau agent 是有人学习 pi agent 自己使用 python 从头实现的一个 agent，是以学习开发agent为目标的
- phi agent 是我学习 agent 开发做的项目，使用 C# 开发，参考 tau 的实现路径复现一遍

## UI

桌面 UI 使用 Avalonia 跨平台框架（所有 UI 内联在 `Phi.Avalonia.Desktop/`，没有共享组件库），终端 UI 使用 XenoAtom.Terminal.UI（`Phi.Tui`）。
两个 UI 共用 `Phi` 库提供的 UI-agnostic runtime。

## 依赖关系

```
        Phi.Tui ──┐
                  ├─► Phi ─► Phi.Provider ─► Phi.Agent
Phi.Avalonia.Desktop ──┘
```

Phi.Agent 是最底层的 package，依赖最少，可以注入不同的 provider 使用，可以随意分发。


## 文档 & 博客

设计文档 / 博客都写到 `website/` 下，用 DocFX 构建。文档站部署到自定义域：<https://phi.154839.xyz>。

```
dotnet docfx build website/docfx.json           # 构建到 website/_site/
dotnet docfx build website/docfx.json --serve   # 构建 + 起本地预览服务
```

详见 [website/articles/architecture.md](website/articles/architecture.md)。

## 扩展参考

- 电子书阅读器，在AI辅助下快速理解这本书的结构，知识点
- 苏格拉底启发式教练

## 已知的问题

- [ ] avalonia virtual list 对 scroll-to-end 支持不好，要想办法优化