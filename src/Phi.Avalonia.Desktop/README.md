# 新的 avalonia 桌面

## 原因

老的桌面 Phi.Avalonia.Desktop 代码很混乱，原因有：

- 大部分老代码是使用 code-only UI 来开发的，新的代码有的使用 axaml，两种风格混合导致维护困难
- 老的代码使用了 SukiUI 这个第三方组件库，导致风格跟原生UI差异很大，这次我想使用官方的 Avalonia.Themes.Fluent 组件库
- 老的代码没有处理好组件之间通信，比如在 PromptInputView 里面展开了一个 workspace picker dropdown，鼠标点击其它地方很难让它收起来，这是底层架构不好导致的

## 规范

- 使用 axaml 来做UI布局，不要使用 code-only 那一套
- 使用 communitytoolkit.mvvm 来做响应式更新
- 使用 Avalonia.Headless 做UI测试，但是不要影响老的项目，为这个新项目新起一个测试项目
- 不要复用老的 Phi.Avalonia 核心组件，因为我目前没有跨平台的打算，我当前的操作系统是 MacOS，因此只要把 macos desktop 的UI跑通就行，近期不会考虑移动端，因为 coding agent 在移动端无法运行（无法访问bash）
- 使用 NativeAot 发布

## 目标

- 新的UI应该跟老的UI接近，但是代码更好维护
- 等新的UI测试通过，我会删除老的UI代码
