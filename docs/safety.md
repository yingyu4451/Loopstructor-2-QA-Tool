# 安全与测试隔离

本工具的写入能力只允许出现在两类明确状态之一：`ResidentPlayer`（Manager/Host 的常规会话，使用真实存档与平台行为）或 `IsolatedQa`（QA 脚本通过环境变量激活的隔离会话）。不存在“尽量隔离”的中间态；门禁不成立时插件保持只读，界面显示等待或不兼容。

## 基本原则

- Fail-closed：任何门禁标志缺失、补丁安装失败或校验不一致，都进入只读状态，不降级、不猜测修复。
- 写入可追溯：作弊写入会在 profile 留下标记，之后该 profile 的运行结果记为 `cheat-modified`。
- 本机闭环：管道只接受本机连接，Editor 桥接只监听 `127.0.0.1`，令牌不进入界面、日志或产物。
- 不改游戏二进制：通过 Harmony 在进程内挂接，不修改、不重签 `Assembly-CSharp.dll`。

## 激活模式与门禁

| 标志 | ResidentPlayer | IsolatedQa |
| --- | --- | --- |
| `SaveIsolationApplied` | false | true |
| `SaveIsolationVerified` | false | true |
| `PlatformWritesBlocked` | false | true |
| `GameArtifactsRedirected` | false | true |

- 四个标志由插件启动时计算：补丁安装结果 + 二次校验结果。
- 模式与标志必须完全匹配上表；混合状态（例如隔离已安装但未验证）一律不满足门禁。
- 门禁结果通过 `hello` 上报。Host 只接受 `ResidentPlayer` 握手（即四个标志全为假），其他模式或标志组合一律拒绝建立可信连接；隔离 QA 不经过 Host，由 QA 脚本直连插件管道。
- 不满足门禁时插件仍可上报状态，但拒绝所有写命令。

## 本机激活与 IPC

- 数据根：`%LOCALAPPDATA%\LoopstructorAutoPlayer`。
- 插件按优先级解析激活来源：
  1. 进程环境变量（`LOOPSTRUCTOR_AUTOPLAYER_ENABLED=1` 及管道名、令牌等）→ `IsolatedQa`；
  2. 本机控制注册 `control\installed-<game-id>.json` → `ResidentPlayer`（Manager/Host 启动的常规路径）；
  3. 一次性启动票据 `tickets\launch-<game-id>.json` → `IsolatedQa`（兜底路径）。
- 本机控制注册与启动票据都包含管道名（≤180 字符）、随机令牌（32–256 字符）、profile 根、产物根、期望的 `Assembly-CSharp.dll` SHA-256 与游戏根哈希；启动票据有效期 5 分钟、单次读取。
- 每个请求携带令牌；令牌错误即断开。管道只接受本机连接；令牌与票据只通过进程环境、本机控制注册文件和管道传递，不写日志、不进入界面或产物。
- 管道名、令牌、profile 根与产物根都经过校验：路径必须落在数据根对应子目录下，令牌长度与字符集受限。
- Editor 实例登记在 `editor-instances\editor-<pid>.json`。

## 隔离 QA

### 存档隔离

- 通过 Harmony 前缀重定向 `ActFramework_ByHZR.Save.SavePathUtility` 的存档根路径，使存档落到 `profiles\<game-id>\<profile>`。
- 安装后做二次校验：反射读取 `SaveManager` 的实际存档路径，必须位于激活来源指定的 profile 根内；`SaveIsolationVerified` 只在二次校验通过后为 true。
- `hello` 必须同时满足“已安装 + 已验证 + profile 与激活来源一致”，否则 Host 不建立可信连接；隔离 QA 由脚本侧自行校验 `hello`。
- 隔离模式下的存档不属于游戏账号；切换 profile 不会影响真实存档，除非手动恢复。

### 平台写入拦截

四个入口全部安装成功才算 `PlatformWritesBlocked=true`：

| 类型 | 方法 |
| --- | --- |
| `ActFramework_ByHZR.Achievements.Unit.SteamAchievementController` | `UnlockAchievement` |
| `ActFramework_ByHZR.MainLoop.Version.PlatformAchievementBridge` | `ReportIGPAchievement` |
| `MetroTD.UISystem.SettlementDataTotalManager` | `TryAutoSendResultOnce` |
| `Steamworks.SteamAPI` | `RestartAppIfNecessary` |

- 前三个拦截成就与结算上报；`RestartAppIfNecessary` 被替换为返回 `false`，防止游戏因 AppID 判断重启进程。
- 两种启动方式都通过进程环境变量注入 `SteamAppId` / `SteamGameId` = `3841840`（Steam 开发会话变量，避免 Steam 客户端替换所选构建）；不写 `steam_appid.txt`。
- `ResidentPlayer` 不安装上述任何补丁，成就、结算与平台行为按真实环境执行。

### 产物重定向

以下游戏写入被改写到产物根（`artifacts\<game-id>\<session>`）：

- 调试日志目录：`GameDebugLogs\`（通过 `DefaultDebugLoggerHandler` / 日志对象的 `LogFilePath` 改写）。
- 本地化启动日志：`LocalizationStartup.log`。
- 结算数据：`GameSettlementData\`（`SettlementDataSaveManager`）。
- 对象使用数据：`ObjectUseData\`（`ObjectPoolHandler` 的 CSV 导出）。

四类全部重定向成功才置 `GameArtifactsRedirected=true`；缺少任一类说明游戏版本与契约不匹配，插件保持只读。

## ResidentPlayer

- 不安装存档隔离、平台写入拦截和产物重定向补丁；使用真实存档、真实成就与真实结算上报。
- 同样由 Host 注入 `SteamAppId` / `SteamGameId` 环境变量后直接启动游戏（不经 Steam 客户端）。
- 作弊写入直接修改真实存档，失败不可回滚；写入前请自行确认存档已备份。
- 用于验证接近线上环境的实际行为；任何写入都会污染该 profile 的证据，运行结果记为 `cheat-modified`。

## 作弊模式

- 只对可信连接开放；命令必须命中白名单（`CheatCommands.All`），未知命令直接拒绝。
- 连接建立后 Host 会自动开启作弊模式；只要插件未报告已启用，就每 5 秒重试一次。
- 自动游玩运行期间只允许只读观察命令（查询、覆盖层开关、状态读取）；写入类命令返回拒绝。
- 连接中断、心跳丢失或令牌失效时，插件关闭作弊模式。
- 写入类命令执行前在 profile 根原子创建标记 `.loopstructor-autoplayer-cheat-tainted.json`；标记存在时，该 profile 后续运行结果统一为 `cheat-modified`。
- 高风险操作（结束波次、地图节点跳过、刷怪、清怪、基地无敌）先校验当前游戏状态（波次、目标、预制体、开关前置条件等），条件不满足时返回失败，不尽力执行。
- 控制命令按请求 ID 去重：相同 ID 携带不同命令会被拒绝，网络/超时重试不会重复执行同一次写入。

## 对游戏文件的影响

- 不修改、不重签 `Assembly-CSharp.dll`；只读取它计算 SHA-256，并读取构建 GUID 和 Unity 版本用于身份校验。
- Harmony 补丁只存在于游戏进程内存，退出即消失。
- 插件安装器只管理自己清单内的文件（`BepInEx\plugins\Loopstructor.AutoPlayer\`、BepInEx 配置项）；不删除整个 `BepInEx` 目录，不触碰其他插件。
- 安装、卸载、更新都要求相关进程已退出；运行中只允许禁用/启用这类可逆操作。

## 运行中保护

- 自动游玩通过游戏内 API 操作，不注入真实鼠标键盘事件；自动游玩本身不改变前台窗口（QA 脚本截图前会尝试前置游戏窗口，未前置时写出提示文件）。
- 连续失败、长时间无进展或状态无法确认时停止自动游玩并记录原因。
- `Faulted` 时先保留存档状态、日志与截图证据，再决定是否停止。
- 只有 `NeedsProcessRestart` 场景要求结束游戏进程；暂停只暂停决策，不改变游戏时间或进程状态。

## 游戏动作与污染处理

- 开局默认防御准备只在正常/随机模式的新运行中执行；继续已有存档时从场上现有防线继续，不重建开局防御。
- `statePolluted` / `needsReset` 只根据当前写操作的结果信封判定，忽略上一轮、历史轮和旧对象的结果。
- 前端写操作只在全局模块与场景 `Main` 稳定后发送；路由选择只考虑 `canPlayerSelect=true` 的节点；普通事件跳过只认真实的 `EventUI_Normal` 跳过按钮。
- 载具部署、轨道插入、弹射点放置、工坊升级等独立写操作按对象身份锁定，只读对账，失败不重发。

## 证据与隐私

- 产物根包含截图、日志、`game-errors.log`、结算数据和路径信息；只保存在本机，不上传。
- 令牌、票据、管道名不写入产物、日志或界面。
- Editor 会话产物单独放在 `artifacts\editor\<project-id>`，不与 Player 产物混放。

## 每次运行检查表

1. 游戏根路径为 ASCII，`Assembly-CSharp.dll` 指纹与预期一致。
2. 激活来源明确：Manager 会话为 `ResidentPlayer`（已接受真实存档风险）；隔离 QA 用独立 profile 运行 `scripts/run-autoplay-qa.ps1`。
3. 插件状态为已启用，控制器/键盘/鼠标未被本工具接管。
4. 自动游玩开始前确认作弊开关所需状态；运行中不要手动操作游戏。
5. 停止后检查时间线、`game-errors.log` 与产物目录，再决定是否恢复存档。

## 已知限制

- BepInEx 注入后无法证明游戏以“未注入原始状态”启动；需要该前提的测试不能用本工具完成。
- 通过游戏内 API 操作，不能证明真实输入路径、反作弊或覆盖层兼容性。
- `RandomMode_TurnTableManager.StopDecorateAnimation` 在部分转盘离场时会打印 `Animator.Play` 空引用；为游戏侧非致命日志，不影响自动游玩结果。
- 本机无 AppID 3841840 的授权，`SteamAPI_Init` 会失败并打印日志；隔离 QA 通过环境变量与 `RestartAppIfNecessary` 补丁规避重启，不代表 Steam 功能可用。
