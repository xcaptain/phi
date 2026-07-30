# Phi agent

参考 tau agent 使用 C# 实现的一个 coding agent

## 起因

- pi agent 是一个生产可用的agent，它的理念很先进，上下文管理做得好，所以token 消耗很少
- tau agent 是有人学习 pi agent 自己使用 python 从头实现的一个 agent，是以学习开发agent为目标的
- phi agent 是我学习 agent 开发做的项目，使用 C# 开发，参考 tau 的实现路径复现一遍


## 依赖关系

PhiTuiApp -> Session -> Harness -> Loop -> Provider

