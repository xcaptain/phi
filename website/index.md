---
_layout: landing
title: Phi
---

# Phi

一个用 C# 实现的、可学习的 coding agent。参考 [pi agent](https://github.com/badlogic/pi-mono) 的设计思路，目标是从头复现一个最小可用、UI 可插拔的 agent runtime。

## 特性

- **三层架构** — `Phi.Agent` / `Phi.Provider` / `Phi`，依赖最少、便于分发。
- **多 UI** — Avalonia 桌面端（`Phi.Avalonia.Desktop`）与 XenoAtom.Terminal.UI 终端端（`Phi.Tui`）共用同一份 runtime。
- **可扩展** — 通过 `Phi.Extensions.Host` 加载独立编写的扩展，事件钩子、自定义工具、Slash 命令等。
- **多模型** — `Phi.Provider` 内置 Anthropic 与 OpenAI 兼容协议（OpenAI / DeepSeek / 本地 llama.cpp 等）。

## 快速跳转

- [架构概览](articles/architecture.md)
- [扩展系统](articles/extensions.md)
- [与 pi extensions 对齐](articles/pi-extensions-align.md)

## 依赖关系

```
        Phi.Tui ──┐
                  ├─► Phi ─► Phi.Provider ─► Phi.Agent
Phi.Avalonia.Desktop ──┘
```