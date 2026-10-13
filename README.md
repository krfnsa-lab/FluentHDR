<div align="center">

# FluentHDR

**写代码用 SDR，影音和游戏用 HDR。**

一个采用 Fluent 风格的 Windows 托盘工具，按前台应用自动切换显示模式。

**简体中文** · [English](README.en.md)

![Windows 11 x64](https://img.shields.io/badge/Windows-11%20x64-0078D4)
![.NET Framework 4.8+](https://img.shields.io/badge/.NET%20Framework-4.8%2B-512BD4)
[![MIT License](https://img.shields.io/badge/License-MIT-2EA44F)](LICENSE)

[下载最新版](https://github.com/krfnsa-lab/FluentHDR/releases/latest) · [快速开始](#快速开始) · [使用方式](#使用方式) · [开发与构建](#开发与构建)

</div>

---

开启 HDR 看电影或玩游戏后，回到编辑器却觉得画面不舒服？FluentHDR 把切换收进系统托盘：工作时使用 SDR，进入播放器或游戏时启用 HDR，也可以随时固定一种模式。

## 界面预览

<table>
  <tr>
    <th align="center">托盘面板</th>
    <th align="center">主题与延迟设置</th>
  </tr>
  <tr>
    <td align="center" valign="top"><img src="assets/tray-dark.png" alt="FluentHDR 深色托盘面板，显示 SDR 和自动、SDR、HDR 三种控制方式" width="350"></td>
    <td align="center" valign="top"><img src="assets/settings-dark.png" alt="FluentHDR 设置页，包含登录启动、外观和两组自动切换延迟" width="350"></td>
  </tr>
</table>

文档支持中英文切换；软件界面目前为简体中文。

## 功能

- **按场景自动切换**：编辑器和终端使用 SDR，播放器与游戏使用 HDR。
- **一键固定模式**：托盘面板提供自动、SDR、HDR 三种选择，右键菜单也可操作。
- **自己控制等待时间**：分别设置应用切换和返回 SDR 的延迟，减少短暂切换窗口带来的反复切换。
- **可编辑应用规则**：添加程序或游戏目录，保存后自动生效。
- **与桌面融为一体**：圆角面板、浅色与深色主题、跟随系统外观、可选登录启动。
- **在本机完成识别**：运行工具不联网，日志不保存浏览器或视频标题。

## 快速开始

### 运行要求

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 11 x64 |
| 显示器 | 支持 HDR，并能在 Windows 显示设置中启用 HDR |
| 运行环境 | Windows PowerShell 5.1、.NET Framework 4.8 或更新版本，通常已由 Windows 提供 |

### 安装

1. 在 [Releases](https://github.com/krfnsa-lab/FluentHDR/releases/latest) 下载 `FluentHDR-…-win-x64.zip`。
2. **完整解压**安装包，双击 `Install.cmd`。
3. 点击右下角托盘中的显示器图标，打开切换面板。图标被收起时，先展开托盘隐藏图标区。

安装无需管理员权限，默认进入**自动模式**并启用**登录启动**。开始菜单搜索 **“HDR 显示模式”** 可以重新打开工具；安装完成后，程序使用独立安装目录，解压的安装包无需继续保留。

## 使用方式

### 选择模式

| 模式 | 作用 |
| --- | --- |
| **自动** | 根据前台应用和规则切换 SDR / HDR |
| **SDR** | 固定 SDR，直到选择其它模式 |
| **HDR** | 固定 HDR，直到选择其它模式 |

面板中的大字表示显示器的**实际状态**，选中的按钮表示当前的**控制方式**。蓝点表示 SDR，紫点表示 HDR。

### 自动规则

| 前台场景 | 默认行为 |
| --- | --- |
| 代码编辑器、IDE、终端 | SDR |
| 视频播放器 | HDR |
| 已知游戏，或配置的游戏目录中的程序 | HDR |
| 浏览器中可识别的视频播放或已知视频网站全屏页面 | HDR |
| 其它应用 | 等待后返回 SDR |

识别以**前台应用**为准：即使后台视频仍在播放，切回代码编辑器后也会恢复 SDR。游戏启动器、壁纸工具等有排除规则，避免被当作游戏。

点击 **“应用规则”** 可增删编辑器、播放器、游戏进程和游戏安装目录。没有识别到的软件，可以输入程序名、选择 `.exe` 文件或添加游戏目录；保存后无需重启。

### 调整切换延迟

点击齿轮进入设置，分别调整：

| 设置 | 作用 | 默认值 |
| --- | --- | --- |
| 自动切换延迟 | 进入代码、影音或游戏场景后等待多久再切换 | 2 秒 |
| 其它应用返回 SDR 的延迟 | 回到其它应用后等待多久再恢复 SDR | 8 秒 |

两项均支持 **1、2、3、5、8、10、15、30、60 秒**，选择后自动保存。前台状态需持续达到设定时间，实际切换时间会受轮询与显示器响应影响。手动固定 SDR 或 HDR 时，不使用自动延迟。

设置中也可以选择 **跟随系统 / 浅色 / 深色**，以及是否登录 Windows 后运行。

## 常见问题

### 浏览器视频没有自动开启 HDR？

浏览器识别依赖 Windows 媒体会话和当前页面标题，部分网站或播放方式可能不提供足够信息。可以手动选择 **HDR**，观看结束后再切回 **自动**。

### 能让同一显示器上的代码窗口用 SDR、视频窗口用 HDR 吗？

Windows HDR 是**显示器级别**的设置，会作用于该显示器上的所有窗口。FluentHDR 切换整块显示器的模式，默认控制当前连接且支持 HDR 的显示器。

### 开启 HDR 会让普通视频变成 HDR 吗？

不会。工具切换 Windows 的 HDR 输出状态，画面效果仍取决于显示器、片源、游戏及其自身设置。

### 退出工具后会发生什么？

右键托盘选择 **“退出”**，会停止托盘和自动识别，并保留当前显示模式。需要取消下次登录启动时，先在设置中关闭 **“登录 Windows 后运行”**。

## 开发与构建

在仓库根目录运行：

```powershell
# 构建程序与图标
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1

# 运行规则与界面回归检查，不切换显示模式
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Run.ps1

# 安装并启动
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup.ps1
```

构建使用 Windows 自带的 .NET Framework 编译器，无需额外安装 .NET SDK。这些命令只设置当前 PowerShell 进程的执行策略，不修改全局执行策略。

生成下载包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1
```

输出位于 `dist/`，包含 ZIP 安装包和 `SHA256SUMS.txt` 校验文件。

<details>
<summary><strong>配置、重装与卸载</strong></summary>

### 本地文件

| 路径 | 内容 |
| --- | --- |
| `%LOCALAPPDATA%\CodexHdrSwitch\App` | 安装程序、脚本与运行中的 `config.json` |
| `%LOCALAPPDATA%\CodexHdrSwitch` | 外观设置、手动模式、状态与日志 |

`CodexHdrSwitch` 是保留的内部目录名。仓库根目录中的 `config.json` 提供默认规则；安装后的当前规则和延迟保存在安装目录中。

如需只控制指定显示器，可在安装目录的 `config.json` 中设置 `TargetDisplays`，填入显示器的完整名称、Windows 设备名、显示器设备路径或显示器键。空数组表示控制所有当前连接且支持 HDR 的显示器。可用以下命令查看显示信息：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\CodexHdrSwitch\App\HdrSwitch.ps1" -Mode Status
```

### 重装选项

`Setup.ps1` 默认保留现有配置，恢复自动模式并启用登录启动：

- `-KeepMode`：保留当前手动模式。
- `-ResetRules`：恢复安装包中的默认规则与延迟。

### 隐私与卸载

识别在本机完成，日志只记录进程名与切换原因，不记录浏览器或视频标题。

卸载前，关闭登录启动并退出工具，然后删除 `%LOCALAPPDATA%\CodexHdrSwitch` 目录和开始菜单中的 **“HDR 显示模式”** 快捷方式。

</details>

## 许可证

本项目采用 [MIT 许可证](LICENSE)。图标使用 Windows 自带的 Segoe Fluent Icons 字体，项目和安装包不分发字体文件。
