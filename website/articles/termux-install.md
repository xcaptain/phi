# Termux 试运行

把 Phi.Tui 安装到 Termux 上跑，最简路径。

## 为什么

Phi.Tui 是 portable .NET 10 console app——**理论上**能在任何 Linux 环境跑，包括 Android 上 Termux（通过 proot 提供完整 Linux 用户态）。这条路径**不需要**Avalonia Android 移植、sandbox、scoped storage——Termux 已经提供完整 Linux runtime、bash、apt。

## 安装步骤

### 1. 安装 Termux

从 [F-Droid](https://f-droid.org/en/packages/com.termux/) 装 **Termux**（不要用 Google Play 版本——那个是老的且不再维护）。

### 2. 在 Termux 里安装 .NET 10 SDK

```bash
pkg update && pkg upgrade
pkg install clang make python
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --install-dir $PREFIX/dotnet
echo 'export DOTNET_ROOT=$PREFIX/dotnet' >> ~/.bashrc
echo 'export PATH=$PREFIX/dotnet:$PATH' >> ~/.bashrc
source ~/.bashrc
dotnet --version   # 应输出 10.0.x
```

### 3. 下载 Phi TUI 二进制

两种方式：

**(a)** 从 GitHub Actions artifacts 下载：
   - 打开仓库 → Actions → "Build binary" → Run workflow
   - 完成后点 latest run → Artifacts → 下载 `phi-tui-linux-arm64`
   - 解压 zip 得到 `phi` binary

**(b)** 自己 build（需要先克隆 repo）：
   ```bash
   git clone https://github.com/xcaptain/phi.git
   cd phi
   dotnet build src/Phi.Tui -c Release -r linux-arm64 --self-contained
   ```

### 4. 放到 Termux 的 `~/` 并 chmod

把 `phi` binary 放到 Termux 的 `$HOME`：

```bash
cd ~
chmod +x phi
./phi --help   # 应该输出 CLI usage
```

### 5. 跑一次真的任务

```bash
./phi
# TUI 应该启动 — 一个 chat-like 界面，model 用你配置的 provider
# 输入 "list files in current directory" 看 bash tool 是否 work
```

## 预期会碰到的问题

| 症状 | 原因 | 修法 |
|------|------|------|
| `./phi: Permission denied` | 没 chmod | `chmod +x phi` |
| `error while loading shared libraries: libssl.so.1.1` | Termux 缺 SSL 库 | `pkg install openssl-tool` 或类似包 |
| `dotnet --version` 报错 | .NET install 失败 | 重跑 install script |
| TUI 启动后没反应 / 软键盘问题 | Termux 没物理 Esc / Tab 键 | 用 `volume up + letter` 模拟 Esc 等 Termux 快捷键 |
| `bash` 工具调用失败 | Termux shell 在 non-standard 路径 | CodingPack 默认用 `/bin/bash`；在 Termux 里是 `$PREFIX/bin/bash` |

## 现状（Phase 1 探索）

- 2025-09：第一次 build 在 macOS arm64 上 cross-build，self-contained 但含 libssl 动态依赖——Termux 上缺 libssl
- CI workflow `.github/workflows/build-binary.yml` 用 `ubuntu-22.04-arm64` + NativeAOT 应该能直接编出完全静态的 binary

## 不要做的事

- 不要用 Google Play 的 Termux——那个版本老、过时、API 不全
- 不要 install 第三方 sources——只用 F-Droid 官方版
- 不要让 Termux 后台被 Android 系统杀——必要时 `termux-wake-lock` + `termux-foreground-service`插件