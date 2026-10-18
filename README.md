# Loopstructor 2 QA Tool

面向 Loopstructor 2（Skyspine）Windows x64 构建的本地 QA 与自动游玩工具。同时支持已打包 Player 和正在运行的 Unity Editor，所有状态、日志和证据都留在本机。

## 组成

发布包根目录的启动器拉起 Electron 界面；界面驱动一个无窗口的 .NET Host，Host 通过命名管道控制游戏内的 BepInEx 插件，或通过回环 HTTP 访问 Unity Editor 内的桥接包。

```text
Loopstructor-2-QA-Tool.exe (Launcher)
└─ manager\  Electron + Vue 3 界面 + .NET 8 Host + 更新器
   ├─ 命名管道 JSON-RPC → BepInEx 插件（已打包 Player）
   └─ 127.0.0.1 HTTP    → Editor Bridge UPM 包（Unity Editor）
```

| 组件 | 位置 | 职责 |
| --- | --- | --- |
| Launcher | 包根 `Loopstructor-2-QA-Tool.exe` | 只做一件事：以 `manager\` 为工作目录启动 `manager\Loopstructor-2-QA-Tool.exe`，原样透传命令行参数；失败时弹出错误框 |
| Manager（界面） | `manager\` | Electron 44 + Vue 3：项目/游戏选择、运行控制、作弊面板、存档备份、设置、更新页 |
| Host | `manager\Loopstructor.AutoPlayer.Host.exe` | 无窗口 .NET 8 进程，stdin/stdout JSON 行 RPC；负责安装插件、启动游戏、安全握手、日志与存档备份、更新编排 |
| Plugin | 游戏 `BepInEx\plugins\Loopstructor.AutoPlayer\` | netstandard2.1 BepInEx 5 插件，在游戏进程内执行 QA 命令与自动游玩决策 |
| Updater | `manager\Loopstructor.AutoPlayer.Updater.exe` | 控制台事务更新器：`check` / `apply` / `cleanup`，整包或增量重建、校验、备份、原子替换、失败回滚 |
| Editor Bridge | Unity 工程 `Packages\com.loopstructor.qa-editor-bridge\` | 仅 Editor 的 UPM 包，向 Host 暴露本机回环 API |

## 适用范围

- Windows x64；游戏为 Unity 2022.3.62f3c1 Mono 构建。
- BepInEx 运行时固定为 5.4.23.5，随发布包提供。
- 游戏目录全路径必须是 ASCII 字符：BepInEx/Mono 组合对非 ASCII 路径不稳定，不做兼容。
- 已核对的真实包为 Skyspine 1.385。其他构建必须同时通过产品标识、程序集 SHA-256 指纹和运行时反射契约校验；任一不通过时只保留只读信息，自动游玩保持不可用。
- 隔离 QA 模式下四个门禁标志必须全部为真：`SaveIsolationApplied`、`SaveIsolationVerified`、`PlatformWritesBlocked`、`GameArtifactsRedirected`；`ResidentPlayer` 模式则必须全部为假。不满足时不会降级为另一种模式，自动游玩保持不可用。详见 [docs/safety.md](docs/safety.md)。

## 环境要求

| 依赖 | 版本 |
| --- | --- |
| Node.js | 24 |
| pnpm | 11.19.0（`desktop/package.json` 的 `packageManager` 固定） |
| .NET SDK | 8.0.423（`global.json` 固定，由 `scripts/bootstrap.ps1` 安装到仓库 `.dotnet`） |
| 7-Zip | 打包时需要（`package.ps1` 自动探测，可用 `-SevenZipPath` 指定） |

## 从源码构建

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1 -Configuration Release
./scripts/test.ps1 -Configuration Release -NoRestore -NoBuild
./scripts/package.ps1 -Version 0.6.79
```

- `build.ps1` 构建解决方案与 `desktop`，输出在各项目 `bin`/`dist` 目录。
- `test.ps1` 依次运行：解决方案测试（TRX 输出到 `artifacts\TestResults`）、`Verification` 控制台校验、`pnpm typecheck`、`pnpm lint`、`pnpm test`。
- `desktop` 的 Playwright E2E 由 `pnpm test:e2e` 单独运行（CI 会执行；本地需要可交互的桌面会话）。
- `package.ps1` 需要 Node/pnpm 与 7-Zip，输出到 `artifacts\release\`：`Loopstructor-2-QA-Tool-0.6.79-win-x64.zip`、对应的 `.sha256` 和 `autoplayer-update-manifest.json`。`-SkipBuild` 可复用已有构建产物。
- 加 `-SkipBuild` 前必须先完成一次对应版本号的 Release 构建，否则包内二进制版本校验会失败。

## 使用发布包

1. 解压 `Loopstructor-2-QA-Tool-<版本>-win-x64.zip`，保持单一顶层目录 `Loopstructor-2-QA-Tool\`，不要只复制其中的 exe。
2. 包内结构：

```text
Loopstructor-2-QA-Tool\
├─ Loopstructor-2-QA-Tool.exe        启动器
├─ manager\                          Electron 界面、Host、更新器、共享 .NET 运行时
├─ payload\bepinex\                  固定版本 BepInEx 运行时
├─ payload\plugin\                   AutoPlayer 插件
├─ payload\editor\                   Editor Bridge 运行时程序集
├─ autoplayer-release.json           发布标记（schema 2）
├─ version.json                      与发布标记同内容
└─ checksums.sha256                  全部文件校验和
```

3. 双击根目录 `Loopstructor-2-QA-Tool.exe`，在“游戏”页选择游戏根目录并验证。
4. 安装插件：Host 把 `payload\bepinex\` 和 `payload\plugin\` 复制到游戏目录的 `BepInEx\` 下；插件目录为 `BepInEx\plugins\Loopstructor.AutoPlayer\`。支持启用、禁用（改名为 `.disabled`）和卸载，状态分为未安装、不完整、已启用、已禁用。
5. 从 Manager 启动游戏。Host 先写本机控制注册（`control\installed-<game-id>.json`：管道名、随机令牌、profile/产物根、期望程序集 SHA-256），再以 `-logFile` 参数启动游戏；插件读取注册连回 Host。Host 校验进程路径、程序集指纹、运行时契约和 `ResidentPlayer` 门禁后建立可信连接。
6. 更新：显示在更新页，由 Updater 执行；详见 [docs/release.md](docs/release.md)。

## 连接 Unity Editor

1. 在“游戏”页选择 Unity 工程根目录；目录必须包含 `Assets`、`Packages` 和 `ProjectSettings/ProjectVersion.txt`。
2. 安装 Editor Bridge。安装只复制 `com.loopstructor.qa-editor-bridge` 到工程的 `Packages\` 下，通过 staging 目录与备份目录替换；目标目录已存在且不属于本工具时拒绝覆盖。移除时只删除该包目录，不改 `Assets`、`Packages/manifest.json`，也不影响 Player 构建。
3. 用 Unity 打开工程。Editor 内的桥接包会写 `%LOCALAPPDATA%\LoopstructorAutoPlayer\editor-instances\editor-<pid>.json`，其中包含回环端口和随机令牌；Host 定期扫描实例并校验文件时效。
4. 在 Manager 中选择实例连接。Edit Mode 下只提供只读发现；进入 Play Mode 后 Host 重新校验实例 ID、进程 ID、工程路径、模式（`editor-edit` / `editor-play`）和程序集 SHA-256，通过后才转发 QA 命令。
5. 自动游玩和依赖 Harmony 的地图跳过只对 Player 生效；Editor 仅用于 QA 运行控制。

## 自动游玩与运行控制

- 运行模式：`Common`（指定角色/难度/超模块）与 `Random`；决策优先级为载具奖励、弹射点、遗物三选一。
- 运行选项：覆盖游戏速度（0/1/2 档）、最长运行分钟数（5–480）、继续现有 profile、跳过剧情。
- 开始按钮条件：可信连接已建立、状态允许开始（Standby / Completed / Faulted）、`NeedsProcessRestart` 为 false、未开启基地无敌或地图节点自由跳转。
- 运行状态控制：

| 状态 | 可用操作 |
| --- | --- |
| Standby / Completed / Faulted | 开始 |
| Running | 暂停、停止 |
| Paused | 继续、停止 |

- `NeedsProcessRestart` 只阻止新的开始；插件认为当前进程内的状态无法清理时置位，需要重启游戏进程。

## 作弊调试

- 只对可信连接会话开放；`cheat.query*` 等只读命令随时可用。
- 连接建立后 Host 默认开启作弊模式；插件未报告已启用时，Host 每 5 秒重试一次。
- 自动游玩运行期间只允许只读观察类作弊命令，写入类命令被拒绝。
- 写入类命令会在当前 QA profile 写入作弊标记；之后该 profile 的运行结果统一记为 `cheat-modified`，便于区分被改动过的证据。
- 作弊会话依赖 Host 心跳；连接中断或心跳丢失时插件关闭作弊状态。
- 能力覆盖：载具与消耗品、弹射点（背包/场地）、遗物、敌人查询与修改、覆盖层显示、结束波次、清怪、跳过奖励弹窗、基地无敌、地图节点自由跳转、刷怪、弹射点捕获。命令以 `cheat.` 为前缀，共 33 个，具体以插件返回的 `capabilities` 为准。

## 存档备份

- 自动备份可开关，保留份数 1–100。
- 进度变化并通过写入稳定检测（默认 2 秒）后生成快照；单次快照最多 5000 个文件、总计 2 GiB，超限跳过并记录原因。
- 恢复流程：正常关闭游戏 → 事务替换存档目录 → 自动重启游戏；失败时保留原存档。

## 运行时数据目录

`%LOCALAPPDATA%\LoopstructorAutoPlayer\`

| 路径 | 内容 |
| --- | --- |
| `control\installed-<game-id>.json` | 玩家模式本机控制注册：profile/产物根、管道与令牌；Manager 启动的会话由此激活 |
| `tickets\launch-<game-id>.json` | 一次性启动票据，5 分钟有效；本机控制注册不可用时的兜底激活路径 |
| `profiles\<game-id>\<profile>` | 按 profile 隔离的状态根；隔离 QA 中作为存档根，作弊标记也记录在此 |
| `artifacts\<game-id>\<session>` | Player 运行产物：证据、日志、`game-errors.log` 等；Manager 会话固定为 `player` |
| `artifacts\editor\<project-id>` | Editor 会话产物 |
| `save-backups\<game-id>\...` | 存档备份 |
| `editor-instances\editor-<pid>.json` | Editor Bridge 实例登记（端口、令牌、工程信息） |

Plugin 端日志由 Host 合并显示；游戏处于非 Development Build 时不会把报错显示在游戏里，插件把 Error/Exception/Assert 写入产物目录的 `game-errors.log`，Host 把新增行并入运行日志（error 级）。插件对 5 秒内相同类别与消息只累加计数，在下一次落盘时注明合并次数；文件达到 8 MiB 轮转。

## 安全边界

- 两种激活模式；插件按优先级解析激活来源：进程环境变量 → 本机控制注册 → 启动票据。
  - `ResidentPlayer`：Manager/Host 启动的常规会话，由本机控制注册激活；四个隔离标志全为假，使用真实存档、成就与结算上报。
  - `IsolatedQa`：隔离 QA，由 `scripts/run-autoplay-qa.ps1` 通过环境变量一次性激活、不经过 Host；四个标志全为真才可运行。
- Host 管理的已安装会话只接受 `ResidentPlayer` 握手；隔离 QA 由脚本直接连接插件管道。
- 隔离 QA 拦截四个入口：`SteamAchievementController.UnlockAchievement`、`PlatformAchievementBridge.ReportIGPAchievement`、`SettlementDataTotalManager.TryAutoSendResultOnce`、`SteamAPI.RestartAppIfNecessary`。
- 游戏产物重定向：`GameDebugLogs`、`LocalizationStartup.log`、`GameSettlementData`、`ObjectUseData` 写入产物根目录，不污染游戏安装目录。
- 两种启动方式都通过进程环境变量注入 `SteamAppId` / `SteamGameId`，不写 `steam_appid.txt`；`RestartAppIfNecessary` 补丁只在隔离 QA 安装。
- 完整门禁、隔离与污染处理见 [docs/safety.md](docs/safety.md)。

## 更新机制

- 更新源为 GitHub Releases：默认仓库 `yingyu4451/Loopstructor-2-QA-Tool`，可用 `LOOPSTRUCTOR_AUTOPLAYER_GITHUB_OWNER` / `LOOPSTRUCTOR_AUTOPLAYER_GITHUB_REPOSITORY` 覆盖。
- 无令牌时使用 `releases/latest` 的网页重定向，不消耗匿名 API 配额；配置令牌时只向 `api.github.com` 发送，不转发给下载 CDN。
- 清单 schema 3：整包资产加可选取的增量资产列表（按 `fromVersion` 匹配）。
- 增量包只在已安装版本与清单 `fromVersion` 完全一致、校验和通过、且体积小于整包时使用；否则回退整包。
- `apply` 先从临时副本重启自身，等待 Manager 退出后 stage → 备份 → 替换，失败回滚；成功后重启 Manager。`cleanup` 清理更新残留进程与临时目录。
- 发布由 `v*` 标签触发 GitHub Actions 生成整包、SHA-256、清单与上一版本增量包；见 [docs/release.md](docs/release.md)。

## 文档

| 文档 | 内容 |
| --- | --- |
| [CONTEXT.md](CONTEXT.md) | 领域术语表 |
| [docs/architecture.md](docs/architecture.md) | 组件边界、启动与握手、IPC、游戏运行时契约、决策循环、发布包结构 |
| [docs/safety.md](docs/safety.md) | 门禁、隔离、作弊、证据与隐私模型 |
| [docs/release.md](docs/release.md) | 构建、打包、更新清单、CI、发布检查表、版本记录 |
| [docs/adr/0001-separate-player-and-editor-transport.md](docs/adr/0001-separate-player-and-editor-transport.md) | 为什么 Player 与 Editor 使用不同传输 |
| [assets/branding/README.md](assets/branding/README.md) | 图标来源与生成方式 |

## 项目结构

| 路径 | 内容 |
| --- | --- |
| `src/Loopstructor.AutoPlayer.Core` | 协议、状态模型、安全门禁、决策引擎与策略 |
| `src/Loopstructor.AutoPlayer.Plugin` | BepInEx 插件（netstandard2.1）：管道服务、补丁、自动游玩控制器 |
| `src/Loopstructor.AutoPlayer.Manager` | Host 复用的服务：游戏校验、插件安装、Editor Bridge、存档备份、更新编排 |
| `src/Loopstructor.AutoPlayer.Host` | 无窗口 Host：JSON 行 RPC、会话与握手、日志、快照 |
| `src/Loopstructor.AutoPlayer.Launcher` | 包根启动器 |
| `src/Loopstructor.AutoPlayer.Updater` | 控制台更新器与事务安装 |
| `src/Loopstructor.AutoPlayer.EditorBridge.Runtime` | Editor Bridge 运行时程序集（Editor 专用） |
| `desktop/` | Electron 44 + Vue 3 + Vite + Pinia + Tailwind/daisyUI 界面 |
| `resources/unity-package/com.loopstructor.qa-editor-bridge/` | Editor Bridge UPM 包源 |
| `tests/` | .NET 测试 |
| `scripts/` | bootstrap、build、test、package、增量包与发布校验脚本 |
| `docs/` | 架构、安全、发布文档与 ADR |

## 版本

当前版本 0.6.79。版本记录见 [docs/release.md](docs/release.md#版本记录)，发布流程与检查表见同一文档。
