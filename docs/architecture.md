# 架构

## 设计目标

- 所有能力都在本机完成：没有远端服务，没有账号系统，不向外发送遥测。
- Player 与 Editor 共用一个 QA 命令面，但走两条独立传输，见 [ADR 0001](adr/0001-separate-player-and-editor-transport.md)。
- 默认只读：未建立可信连接前只暴露只读状态；写能力必须显式开启并留下记录。
- 进程边界负责隔离故障：Launcher、Manager、Host、游戏、更新器各自独立，单个进程崩溃不影响其余进程的既有状态。

## 组件与进程

| 进程 | 程序集 | 职责 |
| --- | --- | --- |
| Launcher | `Loopstructor.AutoPlayer.Launcher` | 包根 exe；以 `manager\` 为工作目录启动 Manager，透传参数，失败弹错误框 |
| Manager | `desktop/`（Electron + Vue） | 界面与 Electron 主进程；创建 Host、转发 RPC、渲染事件流 |
| Host | `Loopstructor.AutoPlayer.Host` | 无窗口 .NET 8 进程；按顺序处理 Manager 请求，持有游戏/Editor 会话、插件安装、日志、存档备份 |
| Verification | `Updater/Verification` | 发布校验控制台；供 CI 检查发布包结构与清单 |
| Updater | `Loopstructor.AutoPlayer.Updater` | 事务更新：check / apply / cleanup；替换前备份，失败回滚 |
| 游戏进程 | Unity Player + BepInEx 5 + 插件 | 插件在游戏内提供管道服务、补丁、自动游玩控制器 |
| Unity Editor | Editor + Editor Bridge 包 | 仅 Editor 的 UPM 包，加载 Editor Bridge Runtime，提供回环 HTTP |

Host 与 Manager 之间是 stdin/stdout 的 JSON 行协议；Host 不写 stdout 日志，日志作为事件回传。Host 与游戏之间是命名管道；Host 与 Editor 之间是本机回环 HTTP。Manager 不直接接触游戏或 Editor。

## 启动与激活

### 启动链

```text
Launcher (包根 exe)
  └─ manager\Loopstructor-2-QA-Tool.exe        cwd = manager\
       ├─ Host 子进程                          stdin/stdout JSON 行
       │    ├─ 读取 %LOCALAPPDATA%\LoopstructorAutoPlayer 下的设置与 profile
       │    ├─ 写入 control\installed-<game-id>.json（本机控制注册；票据仅作兜底）
       │    └─ 启动游戏进程：注入 SteamAppId/SteamGameId 与 -logFile，直接启动（不经 Steam 客户端）
       └─ Updater 子进程（仅在更新时）
```

Host 从 Manager 收到 `game.launch` 后：

1. 校验游戏根：唯一的 `<游戏名>_Data\Managed\Assembly-CSharp.dll`、与之同名的 exe、`UnityPlayer.dll`、`globalgamemanagers`；读取 Unity 版本、构建 GUID 和 `Assembly-CSharp.dll` 的 SHA-256。
2. 确认插件为已启用状态；未安装或已禁用时拒绝启动。
3. 若已有同目录游戏进程：只允许一个，直接接管该进程并进入握手；多于一个则报错，不启动新进程。
4. 否则写入本机控制注册（`control\installed-<game-id>.json`：管道名、随机令牌、profile 根、产物根、期望程序集 SHA-256），写入游戏 `BepInEx` 配置，然后直接创建游戏进程（不经 Steam 客户端，`-logFile` 指向产物日志）并等待插件连回。一次性启动票据（`tickets\launch-<game-id>.json`，5 分钟有效）只在本机控制注册不可用时作为插件激活的兜底路径。

### Player 连接

插件启动时按优先级解析激活来源：进程环境变量 → 本机控制注册 → 启动票据。`hello` 携带协议版本、激活模式、程序集指纹和门禁标志。Host 依次校验：

- 插件使用本机控制注册激活，且激活模式为 `ResidentPlayer`；
- 进程 PID、可执行文件路径、游戏根与注册记录一致；
- 插件自报的程序集 SHA-256 与 Host 计算的 `Assembly-CSharp.dll` 哈希一致，产品标识与指纹被接受；
- 运行时契约可用，插件协议版本 `3`、作弊协议版本 `7` 匹配；
- `ResidentPlayer` 门禁：四个隔离标志全为假（见 [safety.md](safety.md)）；
- `hello` 中的 profile 根与产物根与本机控制注册一致；作弊授权与注册一致。

全部通过后进入可信状态（`Trusted Connection`），才有资格执行 QA 命令。任一项失败时连接被拒绝或降级为等待，不重试越权命令。

### Unity Editor 连接

1. Manager 选择工程根，Host 校验 `Assets`、`Packages`、`ProjectSettings/ProjectVersion.txt`。
2. 安装 Editor Bridge：只复制 UPM 包目录到 `Packages\`，使用 staging 目录与备份目录替换；目标已存在且不属于本工具时拒绝覆盖。卸载只删该包目录。
3. Editor 内的桥接包在 Editor 启动/刷新时写入 `editor-instances\editor-<pid>.json`：实例 ID、回环端口、随机令牌、工程路径、模式。
4. Host 周期性扫描实例文件并剔除过期项；`editor.connect` 携带实例 ID，Host 使用文件中的端口与令牌访问回环 API，校验实例 ID 与工程路径后建立会话。
5. Edit Mode 下只读发现；进入 Play Mode 后重新校验实例文件与实例自述的 PID、工程路径、模式、程序集指纹一致后才转发 QA 命令。
6. 自动游玩与依赖 Harmony 的能力仅对 Player 生效；Editor 不加载 BepInEx、Harmony 或 Player 插件。

## IPC 协议

### Manager ↔ Host

- 一行一个 JSON 对象，UTF-8，stdin 请求、stdout 响应/事件，stderr 仅用于 Host 崩溃诊断。
- 协议版本 `1`（`DesktopHostProtocol.CurrentVersion`）。
- 请求：`{"id": "<string>", "method": "<name>", "params": {...}}`；响应：`{"id": "<string>", "success": <bool>, "result": ..., "error": "<message>"}`；事件：`{"event": "<name>", "payload": {...}}`。失败用 `error` 文本说明原因，不抛异常给界面。
- Host 串行处理请求（内部 operation gate），不会并发执行两个会改变状态的请求。
- 方法分组：`app.*`、`settings.*`、`game.*`、`plugin.*`、`editor.*`、`connection.*`、`cheat.*`、`automation.*`、`update.*`、`backups.*`、`diagnostics.*`、`logs.*`。

### Host ↔ Plugin

- Windows 命名管道，一行一个 JSON 消息；管道名来自本机控制注册（隔离 QA 来自环境变量或票据），不出现在界面。
- 每个请求都携带会话令牌；协议版本 `3`，作弊协议版本 `7`。
- 主要命令：`hello`、`status` / `ping`、`start` / `pause` / `resume` / `stop`、`queryautomationsetup`、`cheat.*`；状态快照随响应返回。
- 插件只在激活来源指定的管道上服务；令牌错误即断开。
- 心跳丢失时 Host 撤销可信状态，插件关闭作弊写能力与瞬态效果。

### Host ↔ Editor Bridge

- 仅 `127.0.0.1` 回环 HTTP，端口随机；请求头携带实例令牌。
- 桥接协议版本 `1`；端点固定为 `/api/status`（健康检查与自述）与 `/api/command`（工程目录读取与运行时命令）。
- 令牌只存在于 Host 与实例文件，不进入界面或日志。

## 游戏运行时契约

插件通过反射绑定游戏类型与成员，不直接引用 `Assembly-CSharp.dll`。契约分为：

- 身份：Unity 版本 `2022.3.62f3c1`、构建 GUID、`Assembly-CSharp.dll` SHA-256、产品标识与指纹；
- 启动：BepInEx 5.4.23.5、插件 GUID `com.ponegames.loopstructor2.autoplayer`、运行环境变量；
- 运行时：若干游戏类型/方法/字段的反射句柄（存档、结算、成就、奖励、载具、弹射点、波次、地图、敌人等）；
- 门禁：存档隔离（安装 + 运行时存档路径二次校验）、平台写入拦截、产物重定向的安装结果；与激活模式共同决定是否允许写操作。

身份或契约不匹配时连接不会进入可信状态：插件只上报状态，不执行写命令；界面显示“等待”或“不兼容”，而不是猜测修复。

## 决策循环

插件内的自动游玩控制器是阶段状态机，阶段枚举（按代码顺序）为：`WaitingForGame`、`FrontEnd`、`RandomSelection`、`InitializingRun`、`PreparingDefense`、`ManagingRewards`、`ManagingEvent`、`ManagingShop`、`SelectingRoute`、`StartingWave`、`Battle`、`Completed`；异常进入 `Recovery`。运行模式与当前画面决定实际经过哪些阶段。

每一轮：

1. 读取当前游戏快照（场景是否稳定、界面模块状态、可交互对象）。
2. 按当前阶段和决策优先级（载具奖励 → 弹射点 → 遗物）选出一个动作。
3. 动作 = 命令 + 参数 + 阶段 + 原因，写入时间线后执行。
4. 读取结果信封，推进或回退阶段；写操作按对象身份锁定，只读对账，失败不重发。

连续失败、卡死或状态无法确认时停止自动游玩并记录原因；只有 `NeedsProcessRestart` 场景要求重启游戏进程。

## 数据与隔离

- 数据根：`%LOCALAPPDATA%\LoopstructorAutoPlayer`。
- Player 会话记录在 `control\installed-<game-id>.json`，Editor 实例在 `editor-instances\editor-<pid>.json`。
- 隔离 QA 的存档根为 `profiles\<game-id>\<profile>`；游戏实际读取的存档路径通过 Harmony 前缀重定向到该根，并做运行时二次校验。`ResidentPlayer` 使用真实存档。
- 产物根为 `artifacts\<game-id>\<session>`（Manager 会话固定为 `player`）或 `artifacts\editor\<project-id>`（Editor）：截图、日志、`game-errors.log`、结算数据等。
- 游戏安装目录只由插件安装/卸载流程写入自身清单文件，不修改 `Assembly-CSharp.dll`。

## 发布包结构

```text
Loopstructor-2-QA-Tool\
├─ Loopstructor-2-QA-Tool.exe          根启动器（AOT、体积优化）
├─ manager\
│  ├─ Loopstructor-2-QA-Tool.exe       Electron 应用
│  ├─ resources\app.asar               界面代码构建产物（不含 node_modules）
│  ├─ resources\app.asar.unpacked\     图片/字体等静态资源（解包以保持跨版本字节稳定）
│  ├─ Loopstructor.AutoPlayer.Host.exe/.dll/.deps.json/.runtimeconfig.json
│  ├─ Loopstructor.AutoPlayer.Updater.exe/.dll/.deps.json/.runtimeconfig.json
│  ├─ coreclr.dll                      共享 .NET 运行时
│  └─ ...                              Electron/Chromium 运行时文件
├─ payload\
│  ├─ bepinex\                         BepInEx 5.4.23.5 运行时（winhttp.dll、doorstop_config.ini、core\）
│  ├─ plugin\                          插件程序集、Core 与 deps.json（不含 pdb/xml）
│  └─ editor\                          Editor Bridge 运行时程序集
├─ autoplayer-release.json             发布标记（schemaVersion 2）
├─ version.json                        与发布标记同内容
└─ checksums.sha256                    全部文件校验和
```

发布标记固定字段：`schemaVersion`、`version`、`runtimeIdentifier`、`managerPath`、`updaterPath`、`payloadPath`、`bepInExPayloadPath`、`pluginPayloadPath`、`pluginPath`、`editorRuntimePayloadPath`、`bepInExVersion`。`version.json` 与发布标记内容相同。包内不允许出现已退役的 `updater\` 顶层目录、嵌套 zip 或缺失的必需文件；由 `scripts/verify-release-assets.ps1` 在 CI 中检查。
