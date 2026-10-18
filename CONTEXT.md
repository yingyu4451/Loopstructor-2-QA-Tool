# Loopstructor 2 QA Tool

面向 Loopstructor 2 的本地 QA 控制域，同时支持已打包 Player 和正在运行的 Unity Editor。所有连接、命令、证据与日志都留在本机。

## Language

**Player Target**:
已打包的游戏进程，通过 BepInEx 插件与 Host 通信。
_Avoid_: 游戏端、正式端

**Editor Target**:
正在运行的 Unity Editor，通过 Editor Bridge UPM 包与 Host 通信。
_Avoid_: 开发版 Player、编辑器游戏

**Editor Bridge**:
仅 Editor 使用的 Unity UPM 包，在 Editor 内提供本机回环 HTTP API。
_Avoid_: Editor 插件、BepInEx 插件

**Editor Instance**:
某个具体 Unity Editor 进程及工程；由 `editor-instances/editor-<pid>.json` 登记。
_Avoid_: Unity 工程、Editor 会话

**Trusted Connection**:
Host 已校验进程路径、程序集 SHA-256 和运行时契约，因而允许发送 QA 命令的连接。
_Avoid_: 已发现、在线

**QA Runtime Control**:
对游戏运行时状态（速度、奖励、弹射点、敌人、波次等）的受控读写。
_Avoid_: 作弊开关、远程控制

**Cheat Mode**:
可信连接上的显式写能力开关；开启后写入会在 profile 留下作弊标记。
_Avoid_: 修改器、外挂

**QA Profile**:
按 profile 隔离的状态根，路径为 `profiles\<game-id>\<profile>`；隔离 QA 中同时作为存档根，作弊标记也记录在此。
_Avoid_: 存档、账号

**Activation Mode**:
插件启动时的运行模式：`ResidentPlayer` 或 `IsolatedQa`。来源按优先级解析：环境变量 → 本机控制注册 → 启动票据。
_Avoid_: 游戏模式、运行环境

**Host**:
无窗口 .NET 8 进程，负责安装、启动、握手、日志、存档备份与更新编排。
_Avoid_: 后端、服务端

**Manager**:
Electron + Vue 3 桌面界面及其主进程。
_Avoid_: 客户端、工具窗口

**Launcher**:
发布包根目录的启动器 exe，只负责以 `manager\` 为工作目录启动 Manager。
_Avoid_: 主程序、入口 exe

**Updater**:
控制台事务更新器，执行 check / apply / cleanup。
_Avoid_: 更新程序、安装器

**Editor Bridge Runtime**:
随发布包分发的 Editor 专用运行时程序集，由 UPM 包加载。
_Avoid_: Editor 插件 DLL

**Release Manifest**:
`autoplayer-update-manifest.json`，描述整包与可选增量包（schema 3）。
_Avoid_: 版本文件、更新包列表
