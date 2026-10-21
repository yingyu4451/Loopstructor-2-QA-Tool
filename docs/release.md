# 构建、发布与更新

## 固定依赖

| 依赖 | 版本 | 来源 |
| --- | --- | --- |
| .NET SDK | 8.0.426 | `global.json`（精确版本，禁止前滚），`scripts/bootstrap.ps1` 安装到仓库 `.dotnet` |
| BepInEx 运行时 | 5.4.23.5 win-x64 | `Directory.Build.props` 的下载地址与 SHA-256：`82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4` |
| BepInEx.Core | 5.4.21 | 插件 NuGet 引用 |
| UnityEngine.Modules | 2022.3.62 | 插件与 Editor Bridge 运行时引用 |
| 插件目标框架 | netstandard2.1 | 游戏 Mono 2.1 |
| 公共依赖（Core/Plugin/EditorBridge.Runtime） | Newtonsoft.Json 13.0.2 | NuGet 引用；与游戏及 Unity 自带的 Json.NET 13.0.2 API 对齐。13.0.3+ 新增的 `JToken.ToString(Formatting)` 重载在游戏内不存在，会导致运行时 `MissingMethodException`。Host 与测试项目仍使用 13.0.4（自带运行时，不进入游戏进程） |
| 测试依赖 | xunit 2.9.3、xunit.runner.visualstudio 4.0.1、Microsoft.NET.Test.Sdk 18.11.0、Mono.Cecil 0.11.6 | `tests/Loopstructor.AutoPlayer.Tests` |
| Editor Bridge | UPM 包，Unity 2022.3，依赖 `com.unity.nuget.newtonsoft-json` 3.2.1 | `resources/unity-package/com.loopstructor.qa-editor-bridge` |
| Electron | 44.9.0 | `desktop/package.json`（精确版本） |
| Node.js | 24 | 构建与 CI（Electron 44.9 内置 Node 24.22） |
| pnpm | 11.28.5 | `desktop/package.json` 的 `packageManager` |
| Vue / Vite / TypeScript / typescript-eslint | 3.5.44 / 8.3.4 / 5.9.3 / 8.72.0 | `desktop/package.json`（TypeScript 7 因 typescript-eslint 与 vue-tsc 尚不支持而未升级） |
| Tailwind / daisyUI | 4.3.3 / 5.7.50 | `desktop/package.json` |
| 界面图标 | @iconify/vue 5.0.4 + @iconify-icons/mdi 2.0.0 | `desktop/package.json` 与 `desktop/src/icons.ts`（离线图标数据静态导入，无 Iconify API 请求；不使用 lucide） |
| 发布 RID | win-x64 | Host、Updater、Launcher 发布 |

## 本地构建和测试

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1 -Configuration Release
./scripts/test.ps1 -Configuration Release -NoRestore -NoBuild
```

- `bootstrap.ps1` 只安装固定 SDK 到 `.dotnet`，若已存在同版本则跳过。
- `build.ps1` 参数：`-Configuration`（Debug/Release，默认 Release）、`-Version`（覆盖编译版本，发布时使用）、`-NoRestore`。它还原并构建解决方案，然后构建 `desktop`（renderer + main）。
- `test.ps1` 参数：`-Configuration`、`-NoRestore`、`-NoBuild`。依次执行：解决方案测试（TRX 输出到 `artifacts\TestResults`）、`Verification` 控制台校验、`pnpm typecheck`、`pnpm lint`、`pnpm test`。
- `desktop` 单独命令：`pnpm build`、`pnpm typecheck`、`pnpm lint`、`pnpm test`、`pnpm test:e2e`（Playwright，会先构建）。
- CI 额外运行 `pnpm test:e2e`；本地运行该命令需要可交互的桌面会话。

## 本地打包

```powershell
./scripts/package.ps1 -Version 0.6.89
```

参数：`-Version`（缺省取 `Directory.Build.props` 的 `VersionPrefix`；发布时必须显式传入与源码一致的版本）、`-SkipBuild`（复用已有构建产物）、`-SevenZipPath`（7-Zip 路径，缺省自动探测）。

流程：校验版本（`PluginInfo.cs` 与参数一致）→ 构建（或跳过）→ 组装 `artifacts\package\Loopstructor-2-QA-Tool\` → 生成 `checksums.sha256` → 用 7-Zip 以最大压缩率生成 zip → 校验 zip 与目录一致且无嵌套压缩包。

输出到 `artifacts\release\`：

```text
Loopstructor-2-QA-Tool-0.6.89-win-x64.zip
Loopstructor-2-QA-Tool-0.6.89-win-x64.zip.sha256
autoplayer-update-manifest.json
```

- zip 内只有一个顶层目录 `Loopstructor-2-QA-Tool\`；目录结构见 [architecture.md](architecture.md#发布包结构)。
- `checksums.sha256` 是 `"<sha256>  <相对路径>"` 行列表，覆盖 zip 内除自身外的全部文件。
- 不能直接复制 `artifacts\package\...` 目录发布；发布产物是 zip、sha256 与清单三件套。

## 更新清单

清单文件为 `autoplayer-update-manifest.json`，schema 3：

```json
{
  "schemaVersion": 3,
  "version": "0.6.89",
  "runtimeIdentifier": "win-x64",
  "assetName": "Loopstructor-2-QA-Tool-0.6.89-win-x64.zip",
  "sha256": "<整包 SHA-256>",
  "size": 123456789,
  "deltaAssets": [
    {
      "fromVersion": "0.6.85",
      "assetName": "Loopstructor-2-QA-Tool-0.6.88-to-0.6.89-win-x64.delta.zip",
      "sha256": "<增量包 SHA-256>",
      "size": 1234567
    }
  ]
}
```

- 客户端（`GitHubReleaseClient`）要求 `schemaVersion == 3`；未知 schema 直接拒绝。
- `deltaAssets` 最多 16 项；`fromVersion` 必须与已安装版本完全相等（Ordinal），不做“近似版本”匹配。
- 增量包由 `scripts/package-delta.ps1` 生成，参数：`-BaseArchive`、`-BaseManifest`、`-TargetPackageDirectory`、`-TargetVersion`、`-ReleaseDirectory`。输出 `Loopstructor-2-QA-Tool-<from>-to-<to>-win-x64.delta.zip` 与 `.sha256`，并把增量项写回目标清单。
- 增量包根目录固定为 `Loopstructor-2-QA-Tool.delta\`，内含 `files\`（相对目标包根的文件）和 `checksums.sha256`（目标整包完整校验和）；重建时先校验当前安装版本与 `fromVersion` 一致，再校验重建结果的校验和与目标清单。
- 任一校验失败时客户端回退整包；不使用增量包作为唯一更新路径。
- 增量包只包含相对上一发布版本内容变化的文件，不做二进制差分（`scripts/package-delta.ps1` 按 SHA-256 对比目标包与基线包）。0.6.81 起 Electron 主程序与静态资源在 patch 版本之间保持字节不变，常规 patch 发布的增量包因此只有 MB 量级。

## GitHub Actions

### CI（`.github/workflows/ci.yml`）

- 触发：所有分支 push、pull_request、手动 dispatch。
- 运行环境：`windows-2022`，超时 45 分钟，`contents: read`。
- 步骤：checkout → 安装 pnpm 11.28.5 → Node 24（缓存 pnpm）→ `pnpm install --frozen-lockfile` → 缓存固定 .NET SDK 与 NuGet 包 → `bootstrap.ps1` → `build.ps1 -Configuration Release` → `test.ps1 -Configuration Release -NoRestore -NoBuild` → `pnpm test:e2e` → 校验全部 `scripts/*.ps1` 可解析且语义版本比较正确 → 上传 TRX 测试结果（保留 14 天）。

### Release（`.github/workflows/release.yml`）

- 触发：推送 `v*` 标签；手动 dispatch 需输入不带 `v` 的版本号（仅产出 artifact，不发 Release）。
- 权限：`contents: write`；并发组 `release-<ref>`，不取消进行中的发布。
- 步骤：
  1. checkout（`fetch-depth: 0`）→ 预检全部 `scripts/*.ps1` 语法 → 依赖安装、缓存、按标签/输入解析版本 → 用 `scripts/common.ps1` 校验版本为规范语义版本（不符即失败）→ bootstrap、构建、测试、E2E。版本号只通过 `env` 传给 PowerShell 步骤，不直接拼进 `run:` 脚本。
  2. `package.ps1 -Version $env:PACKAGE_VERSION -SkipBuild` 生成整包、SHA-256 与 schema 3 清单。
  3. 仅在标签触发时：按 `v*` 版本倒序查找上一个已发布（非 draft、非 prerelease）版本，下载其整包与清单，校验 schema/版本/RID/文件名/大小/SHA-256，再用 `Verification --verify-release-package` 校验包结构；全部通过才作为增量基线。
  4. 有基线时运行 `package-delta.ps1` 生成增量包，并运行 `verify-release-assets.ps1` 校验整包与增量包布局。
  5. 上传可运行目录为 workflow artifact（保留 30 天），重新下载并校验必需文件、根目录、发布标记、无退役 `updater\` 目录、无嵌套 zip。必需文件包含 `payload/bepinex/winhttp.dll`、`payload/bepinex/doorstop_config.ini`、`payload/bepinex/BepInEx/core/BepInEx.dll`、`payload/plugin/Loopstructor.AutoPlayer.Plugin.dll`、`payload/plugin/Loopstructor.AutoPlayer.Core.dll`、`payload/plugin/Loopstructor.AutoPlayer.Plugin.deps.json`；`payload/plugin` 下出现 `*.pdb` 或 `*.xml` 直接失败。
  6. 发布：已存在同名 Release 且不是 draft 时直接失败，不覆盖；已存在 draft 时删除重建；先上传除清单外的全部资产，最后上传清单，最后把 draft 发布为正式 Release。
- 版本号解析使用仓库 `scripts/common.ps1` 的规范化语义版本比较（忽略构建元数据，预发布序号按数值比较）。

## 发布检查表

发布前：

- [ ] `Directory.Build.props`、`PluginInfo.cs`、`desktop/package.json`、`resources/unity-package/com.loopstructor.qa-editor-bridge/package.json` 版本一致。
- [ ] `docs/release.md` 的版本记录包含本次改动。
- [ ] 本地 `bootstrap.ps1`、`build.ps1`、`test.ps1` 全绿；必要时运行 `pnpm test:e2e`。
- [ ] 用 `scripts/run-autoplay-qa.ps1` 做一次隔离 QA 冒烟；在 Manager 中做一次 ResidentPlayer 启动、握手、开始/暂停/停止、存档备份。
- [ ] 检查 `game-errors.log` 没有新增非预期错误。

发布：

- [ ] `package.ps1 -Version <version>` 产物齐全，zip 内顶层目录与必需文件正确。
- [ ] 手动解压 zip 到空目录，双击根 exe 可用；再用旧版本验证一次更新。
- [ ] 提交版本改动并推送当前分支。
- [ ] 创建并推送 `v<version>` 标签触发 Release workflow。
- [ ] 在 Actions 中确认整包、增量包、清单上传成功，Release 为正式状态。
- [ ] 从上一版本实测一次增量更新与一次整包更新。

## 仓库与首次发布

- 远端：`https://github.com/yingyu4451/Loopstructor-2-QA-Tool`，主分支 `main`。
- 更新源默认读取 `yingyu4451/Loopstructor-2-QA-Tool`；可用环境变量 `LOOPSTRUCTOR_AUTOPLAYER_GITHUB_OWNER`、`LOOPSTRUCTOR_AUTOPLAYER_GITHUB_REPOSITORY` 覆盖。
- 无令牌时通过 `releases/latest` 网页重定向解析下载地址，不消耗匿名 API 配额；配置 `LOOPSTRUCTOR_AUTOPLAYER_GITHUB_TOKEN` 时只向 `api.github.com` 发送，不转发给 CDN。
- 仓库需要 Actions 写权限（`contents: write` 已写在 workflow 中）。
- 创建标签：

```powershell
git tag v0.6.89
git push origin v0.6.89
```

## 升级 BepInEx

1. 修改 `Directory.Build.props`：`BepInExRuntimeVersion`、`BepInExRuntimeArchiveName`、`BepInExRuntimeDownloadUrl`、`BepInExRuntimeSha256`。
2. 升级插件引用的 `BepInEx.Core` 包版本；如 API 变化，同步插件补丁代码。
3. 重新构建并跑完整测试。
4. 本地打包后用 `scripts/run-autoplay-qa.ps1` 启动一次隔离 QA，确认插件加载、`hello` 通过、QA 命令可执行；再用 Manager 走一次 ResidentPlayer 启动。
5. `payload\bepinex` 的内容来自固定 zip，运行时升级必须重新打包整包并递增产品版本；已安装旧运行时的游戏目录通过整包更新或重新安装插件更新。插件不读取、也不校验 BepInEx 运行时版本，兼容性只能靠打包与冒烟验证。

## 真实包验证基线

2026-10-18，Skyspine `1.419`，隔离 QA 模式：

- 插件 0.6.80 在 1.419 实机验证新增的 `cheat.clearFieldVehicles` / `cheat.clearBackpackVehicles`：开局防御部署后场上战车被正确识别为场上并整批删除；未激活战车归入背包删除；删除后 `queryVehicles` 返回 0，重复执行返回准确的空集合提示。
- 插件 0.6.78 加载、`hello` 握手与自动游玩启动正常；开局防御闭环、地图节点选择、轨神事件、波次战斗、关卡奖励领取与失败结算检测均在实机走通，无卡死、无未处理异常。
- 单局运行统计：`WavesStarted=3`、`WavesCompleted=2`；本局第 3 波被击破，由失败结算界面确认结束（`RunState=3`、`Outcome=3`），属玩法结果而非故障。
- 该版本地图节点会新增弹射点：无可用 `FreePoint_Attribute` 扩建道具时，轨道闭环门禁在连续 2 次对账失败后解除并告警，未接入站点保持原状。
- `Assembly-CSharp.dll` SHA-256：`cdf019572c315fbdd7343694aec7b8fcdbea1bbd3d5a3a1ff4289b0a6f54e150`。
- 本机无 AppID 3841840 授权，`SteamAPI_Init` 失败并打印日志，不影响隔离 QA 启动与游玩。

2026-07-29，Skyspine `1.385`，隔离 QA 模式：

- 普通模式与随机模式均正常开局、自动游玩并停止。
- 运行统计：`WavesStarted=2`、`WavesCompleted=1`（验证轮次短，非失败）。
- `Assembly-CSharp.dll` SHA-256：`962b9f69774d4ea20458877363bc33bc7f85cfa207baa1d775b0ea5677140f29`。
- `RandomMode_TurnTableManager.StopDecorateAnimation` 打印 `Animator.Play` 空引用，属游戏侧非致命日志。
- 本机无 AppID 3841840 授权，`SteamAPI_Init` 失败并打印日志，不影响隔离 QA 启动与游玩。

## 版本记录

### 0.6.89

- 修复切换获取池、游戏内快捷键或常驻作弊开关时可能弹出 `An object could not be cloned.` 的问题：这些操作会把界面状态直接交给 Electron 主进程，而当 `persistentCheats` 等数组已被 Vue 响应式代理包裹时，序列化会失败。现在所有设置写入都先转成普通对象再发送（`saveSettings` 与页面切换的 `saveSettings` 两条路径），设置可以正常保存到磁盘并回读。
- 行为不变，仅修复设置持久化路径；.NET 测试 665 项、桌面单元测试 31 项、Playwright E2E 4 项，Release 构建 0 警告。

### 0.6.88

- 修复 0.6.87 的存档内容指纹判定：改为对存档内容做 SHA-256（0.6.87 用的是最后写入时间），同一章节步骤里内容改变但长度不变的写法也能可靠触发备份；纯元数据变化（例如只更新时间戳）仍不会产生重复快照。0.6.87 的 CI 曾在同长度用例上偶发失败，此版一并修正。
- 无界面、协议与命令集变化；.NET 测试 665 项、桌面单元测试 30 项、Playwright E2E 4 项，Release 构建 0 警告。

### 0.6.87

- 问题修复（全项目审计，均为低风险局部改动）：存档页路由 `saves` 被 `NormalizeRoute` 丢弃，重启后回不到存档页；日志尾读会跳过超长文件的较早行、把 EOF 半行当成整行（改为字节级逐行解析，只前进到完整换行）；存档指纹只含路径与长度，同长度内容变化会漏备份（加入最后写入时间）；损坏的 `settings.json` 会被默认值静默覆盖（现隔离为 `settings.json.bad-<时间戳>` 并告警）；Host 进程启动失败时界面无兜底（现弹出错误并退出）、子进程错误事件不再悬挂请求；选择合法游戏目录时因 Host 的 `{ validation, plugin }` 返回结构未解包而抛 `TypeError`；Host 退出后界面仍显示已连接（现重置连接与目录、作弊状态）；非法路由名补默认页兜底；复选框命令失败时回滚选中态；批量删除/清空前增加二次确认；读档按钮忙碌时禁用；备份上限收敛到 1–100；模态框支持 Esc 取消、初始焦点、Tab 焦点陷阱与关闭后焦点恢复；虚拟目录列表裁剪越界起始行、切换数据重置滚动并显示空状态；诊断日志只在接近底部时自动跟随；自动游玩状态、阶段与结果中文化；更新器收到错误事件时进入失败态。
- 打包与 CI：7-Zip 压缩 `-mpass` 由 15 降为 3（本机 449 MB staging 实测 402 s → 68.5 s，包体 +0.25%）；Release workflow 在解析版本时校验四份版本号一致（`Directory.Build.props`、`desktop/package.json`、Unity bridge `package.json`、`PluginInfo.cs`）；CI 把 PowerShell 脚本语法校验前移到安装依赖之前，构建复用已安装依赖（`-SkipPnpmInstall`），E2E 不再重复执行 `pnpm build`；BepInEx 下载增加超时与三次重试；Electron 仅打包 `zh-CN` / `en-US` 语言包；发行校验改用本地打包 staging，去掉 189 MB 产物上传后再下载的重复步骤；新增 `.gitattributes` 与 `.gitignore` 规则，抑制 CRLF 转换噪音并忽略编辑器临时文件。
- 文档：真实包验证基线更新为 Skyspine `1.419`（实机冒烟）与 `1.423_Beta`（运行时反射契约核对）。
- 协议与命令集无变化；.NET 测试 665 项、桌面单元测试 30 项、Playwright E2E 4 项，Release 构建 0 警告。

### 0.6.86

- 依赖升级（安全与兼容性范围内）：`@iconify/vue` 5.0.3 → 5.0.4、`typescript-eslint` 8.71.2 → 8.72.0、pnpm `packageManager` 11.19.0 → 11.28.5（`desktop/pnpm-lock.yaml` 同步，CI 与 Release workflow 的 `pnpm/action-setup` 版本一并更新）。
- .NET SDK 由 8.0.423 升到 8.0.426（.NET 8 维护线最新版本，2026-10-13 发布，含缺陷与安全修复）：`global.json`、`scripts/bootstrap.ps1` 的固定版本与安装包 SHA-512 同步更新，README 与本文档的固定依赖表同步。
- 暂不升级项：TypeScript 7.0.2（`typescript-eslint` 8.72 的 peer 仍要求 `typescript <6.1.0`，`vue-tsc` 也尚未支持）、`@types/node` 26.x（本机与 CI 的 Node 运行时为 24，类型包主版本应保持一致）；游戏侧 `UnityEngine.Modules` 2022.3.62 与 `Newtonsoft.Json` 13.0.2 属游戏运行时对齐项，保持不动。
- 无功能、协议与界面变化；.NET 测试 658 项、桌面单元测试 29 项、Playwright E2E 4 项，Release 构建 0 警告。

### 0.6.85

- 新增「获取池」双池切换：战车、消耗品、遗物目录可在「全部池」（枚举全集）与「游戏配置池」之间切换。游戏配置池与游戏开发者作弊面板同源——战车取 `CheatVehiclePanelCfg.vehicleTypes`，消耗品取 `RewardManager.AllDisposableRewards`，遗物取 `RewardManager.AllSuperModuleRewards`（列表为空时先反射调用 `UpdateAllRewards()`）；某类别读取失败时回退全部池，并在插件返回的 `poolWarnings` 中说明。
- Core `CheatCommands` 新增 `cheat.setCatalogPoolMode`（`All` 37 项 / `Mutations` 27 项），插件 `QueryCatalog` 的 `catalogVersion` 升到 6 并返回 `poolMode` / `poolWarnings`；`StartGrantAllRelics` 在游戏配置池下只启用配置中的遗物；Editor Bridge 对该命令返回 `EDITOR_POOL_MODE_UNAVAILABLE`，Editor 运行控制沿用全部池。
- Manager 设置新增 `catalogPoolMode`（默认 `all`）与 `persistentCheats`；「战斗」页新增「常驻启动」卡片，可保存基地无敌、地图自由跳转、显示敌人 ID、显示 Buff 四项，连接游戏并启用作弊后由 Host 自动下发未恢复项，失败按项节流重试。
- 战车、道具、遗物页加入「全部池 / 游戏配置池」两段开关与生效状态徽标；切换会随 Manager 设置持久化，并在连接时下发给插件。
- .NET 测试 658 项、桌面单元测试 29 项，Release 构建 0 警告；目标游戏 Skyspine `1.423_Beta`（`CheatVehiclePanelCfg`、`RewardManager` 字段已核对）。
### 0.6.84

- 新增游戏内快捷键：在「战斗」页的「游戏内快捷键」卡片开启后，不用切回工具，在游戏窗口内按 `F` 清除场上所有怪物、`F1` 结束当前波次，行为与游戏开发者模式的同名按键一致（分别对应 `AgentCreator.ClearAllEnemy` / `WaveDurationController.WaveOver`）。
- 插件 `CheatController` 通过反射轮询 Unity Input System 的 `Keyboard.fKey` / `f1Key`（不新增程序集引用，找不到输入系统时按 2 秒节流重试）；快捷键只在作弊模式已启用、工具与游戏保持连接、且自动游玩未运行时响应，触发的动作复用现有作弊命令执行管线，因此同样标记作弊存档、写入 `cheat-actions.jsonl` 审计并遵守自动游玩写锁。
- 新增设置项 `gameHotkeysEnabled`（默认关闭，随 Manager 设置持久化）：Host 在自动开启作弊模式后、会话重连后以及保存设置后把开关下发给插件；插件状态新增 `hotkeysEnabled` / `hotkeysAvailable`，战斗页徽标显示「游戏中已生效 / 正在下发到游戏 / 等待作弊连接 / 未启用」。
- Core `CheatCommands` 新增 `cheat.setHotkeysEnabled`（`All` 36 项 / `Mutations` 26 项），Plugin 与 Editor Bridge 分发分支同步；插件反射读取失败只记录告警，不影响其他作弊功能。
- 目标游戏版本 Skyspine `1.423_Beta`（`Assembly-CSharp.dll` 4113 类，存在 `Unity.InputSystem.dll`）；.NET 测试保持 657 项、桌面单元测试 27 项，Release 构建 0 警告。

### 0.6.83

- 重做 Manager 界面：删除右侧机械背景（`station.png`）、铜管边框（`console-frame.png`）、齿轮传动轴与全部 `clip-path` 切角，改为扁平化仪表盘布局；重写页面边距与间距（标题栏 52px、侧栏 232px、内容区内边距 20px 24px、卡片内边距 16px、圆角 10–12px）。
- 配色沿用 0.6.82 的煤矿／铜／黄铜体系：交互强调色使用黄铜，在线与成功信号保持绿色，危险操作保持暗红；不使用冷蓝灰主题。
- 删除 5 张机械贴图与 `Skyspine Data` 字体（约 2.4 MB 源资源），移除唯一皮肤的皮肤选择入口与 `theme/skins.ts`，避免「选了也没用」的设置项。
- 桌面单元测试保持 26 项、.NET 测试保持 657 项，Release 构建 0 警告；本版只改 Manager 界面视觉与静态资源，不改变游戏内插件运行时契约与作弊命令。

### 0.6.82

- 消除 Manager 注入 Unity Editor 连接组件后 Console 反复出现的两条 `.meta` 报错（`The .meta file ... does not have a valid GUID and its corresponding Asset file will be ignored.`）：这两条错误由工程内 GUID 非法的资源 `.meta` 引起，每次 AssetDatabase 全量刷新（切出/切回 Unity）都会重新打印，与连接组件本身无关。
- 新增 `UnityAssetMetaGuidRepairer`（`src/Loopstructor.AutoPlayer.Manager/Services/UnityAssetMetaGuidRepairer.cs`）：安装/更新连接组件成功后扫描工程 `Assets`、`Packages` 下的 `*.meta`，把非 32 位十六进制的 `guid:`（含缺失 guid 行）替换为新的合法 GUID，保留原编码（UTF-8 BOM）、其余内容与文件原子写；逐文件失败计入 `FailedCount` 而不阻塞安装，跳过 reparse point 与 UTF-16 文件。
- 安装/更新结果消息追加修复数量，Manager 直接以 toast 显示，例如「已修复 N 个 Unity 会报错的无效 .meta GUID。」；工程本身无坏 `.meta` 时消息与之前完全一致。
- 新增测试：`UnityAssetMetaGuidRepairerTests`（只改非法 GUID、保留 BOM 与内容、缺失 guid 行时插入、幂等）与 `UnityProjectBridgeInstallerTests` 的安装自愈用例；解决方案测试 653 → 657，桌面单元测试保持 26，Release 构建 0 警告。
- 本版不改变游戏内插件运行时契约与作弊命令。

### 0.6.81

- 修复增量更新包体积过大的问题。此前每次 patch 发布都会重新分发约 247 MB 的 Electron 主程序与约 35 MB 的 `app.asar`：`0.6.79 → 0.6.80` 的增量包为 115.8 MB，其中压缩后的主程序 104.6 MB、`app.asar` 9.3 MB，合计约占增量包的 98%。放大体积的原因与对应处理：
  - `build.disableAsarIntegrity: true`：electron-builder 原先把 `app.asar` 的 SHA-256 完整性清单写进主程序资源，`app.asar` 每版变化都会改写整个主程序；Windows 默认不启用该完整性校验，关闭嵌入后不影响启动（已用打包产物实测）。
  - `build.extraMetadata.shortVersion` / `shortVersionWindows` 固定主程序文件版本元数据为 `0.6.0` / `0.6.0.0`，patch 版本升级不再改写主程序字节（连续两次打包的主程序 SHA-256 完全一致）。代价是 Windows 文件属性里的“文件版本”固定为 0.6.0，应用界面显示的版本仍取自 `package.json`；升级到 0.7 时需同步调整这两个字段。
  - `build.files` 排除 `node_modules/**`：界面代码由 Vite 打包、主进程只依赖 Electron 与 Node 内置模块，产物运行时不需要 `node_modules`；`app.asar` 从 35.3 MB 降到约 0.5 MB，整包也随之变小，并避免把开发依赖打进发布包。
  - `build.asarUnpack` 将图片/字体等静态资源解包到 `resources\app.asar.unpacked\`：静态资源跨版本不变时不再进入增量包。
- 效果（本地实测）：整包 zip 从 197.8 MB 降到 191.3 MB（314 个文件）；用 `0.6.81 → 0.6.82` 的模拟发布实测增量包，只有 17/313 个文件变化、增量包 2.67 MB（主程序不再进入增量包）。`0.6.80 → 0.6.81` 这一次的增量包仍会偏大，因为主程序字节首次切换为稳定值。
- `desktop/tests/dependency-contract.spec.ts` 新增打包契约守护：固定版本元数据必须跟随 `major.minor`、必须排除 `node_modules`、静态资源必须解包，防止后续改动把增量包重新撑大。
- `scripts/package.ps1` 对 Electron 主程序的版本校验改为读取并校验打包配置里固定的 `shortVersionWindows`（其余发布二进制仍要求等于产品版本）。
- 桌面单元测试 25 → 26；.NET 测试数保持 653；Release 构建 0 警告，全部测试与 e2e 通过。本版不改变游戏内插件运行时契约与作弊命令。

### 0.6.80

- 图标体系改为离线 Iconify（Material Design Icons）：移除 `@lucide/vue`，恢复 `@iconify/vue` 5.0.3 + `@iconify-icons/mdi` 2.0.0 依赖与 `desktop/src/icons.ts` 门面（按需静态导入，无 Iconify API/网络请求）；17 个组件与页面统一从 `../icons` 导入，`desktop/tests/dependency-contract.spec.ts` 守护该约定。
- 新增一键删除全部战车，分「删除场上全部战车」（`cheat.clearFieldVehicles`）与「删除背包全部战车」（`cheat.clearBackpackVehicles`）：战车页“本次装配”卡片新增批量删除行，一次点击生效（自动游玩运行或暂停期间受写锁保护）。
- 插件按游戏 `GuiGameMcpVehicleRuntime.ResolveVehicleRole` 的规则分类——激活中的战车属于场上，未激活的（含旧存档残留）属于背包；逐辆调用 `VehicleManager.DeleteVehicle`，删除前先快照车辆状态，单辆失败不中断整批并返回部分失败明细与剩余场上/背包数量。
- 命令注册在 Core `CheatCommands`（`All` 35 项 / `Mutations` 25 项），Plugin 与 Editor Bridge 分发分支同步。
- 在 Skyspine `1.419` 打包版上实机验证两条新命令：开局防御部署后 `cheat.clearFieldVehicles` 删除场上 1 辆战车、`cheat.clearBackpackVehicles` 删除背包 1 辆战车；删除后 `queryVehicles` 为 0，重复执行返回“场上/背包中没有可删除的战车”。
- 测试数 652 → 653；Release 构建 0 警告、全部测试通过，桌面 typecheck/lint/单元测试与 e2e 通过。
- 本版插件只新增作弊命令，游戏运行时契约与 0.6.79 相同。

### 0.6.79

- 修复 Manager 在更新交接阶段偶发不退出、更新窗口长时间停在“等待进程退出”的问题：
  - Manager 收到 `updateStarted` 后隐藏窗口并退出；若 8 秒内仍未退出，强制 `app.exit(0)`。Host 停止后的最终退出再加 2 秒兜底，交接各阶段写入 `%APPDATA%\loopstructor-autoplayer-desktop\update-handoff.log` 便于诊断。
  - 更新器等待逻辑改为轮询：每 5 秒报告仍在运行的 PID 与已等待/剩余时间；默认等待上限从 600 秒降为 180 秒；超时信息直接列出仍存活的 PID 与处理建议。
  - Host 通过 `--desktop-pid` 显式传入 Manager PID；更新器在 Manager 20 秒仍未退出时主动结束该进程并继续安装，避免无响应的 Manager 永久阻塞更新。
- 新增 `ProcessWaiterTests`、`UpdaterCommandOptionsTests` 覆盖已退出进程跳过、轮询进度、超时信息与强制结束无响应 Manager 等场景；测试数 645 → 652。
- 本版只调整 Manager/Updater 的更新交接逻辑，游戏内插件运行时契约与 0.6.78 相同。
- 已知限制：`0.6.78 → 0.6.79` 这次更新仍由 0.6.78 自带的更新器执行。若更新窗口停在“等待进程退出”，手动结束 `Loopstructor-2-QA-Tool` 进程即可让更新继续完成；0.6.79 起该场景会自动处理。

### 0.6.78

- 以 Skyspine `1.419` 打包版做隔离 QA 实机验证，修复该版本暴露的四个阻断问题：
  - Unity 侧工程（Core、Plugin、EditorBridge.Runtime）的 Newtonsoft.Json 编译引用回退到 `13.0.2`，与游戏/Unity 自带 Json.NET 的 API 面一致；`13.0.3+` 新增的单参数 `JToken.ToString(Formatting)` 在游戏运行时不存在，已实机复现 `MissingMethodException`。Host 与测试工程继续使用 `13.0.4`。
  - 关卡奖励收集界面：奖励轻量反射契约支持 `RewardUIPanel.DisplayedRewards` / `RewardCollectionItemUI.Data` 与卡片 `Claim()`，按卡片逐项领取（批量领取在游戏侧被禁止），旧版队列面板路径保留。
  - 地图界面：1.419 已无独立地图面板（旧地图按钮实际会打开设置面板），暂时禁用地图界面交互、只保留节点选择，避免等待不存在的“地图关闭”。
  - 开局防御闭环：站点布局无法组成严格均衡闭环时不再直接失败，依次降级为“排除非必需站点后保持均衡”→“接受可用的非均衡闭环”，降级原因写入运行警告与时间线；绘制出的开局闭环按“合法简单闭环”校验，只有选择器仍拿到严格均衡计划时才要求均衡圆环，未纳入站点仍交由开波前轨道门禁处理。
- 轨道闭环门禁：扩建材料（`FreePoint_Attribute` 弹射点道具）确认耗尽且连续 2 次无法完成闭环对账时，解除门禁继续流程并告警；非法闭环、站点读取超时等安全门禁保持失效即关闭。`docs/safety.md` 补充该例外。
- 测试数 630 → 645（新增关卡奖励契约 8 项、开局闭环降级选择 7 项，并更新性能契约断言），全部通过；Release 构建 0 警告。
- 发布包内容与发布流程不变（整包 + 相对 0.6.77 的增量包）。

### 0.6.77

- .NET 依赖升级：Newtonsoft.Json 13.0.3 → 13.0.4、Mono.Cecil 0.11.4 → 0.11.6、xunit 2.9.2 → 2.9.3、xunit.runner.visualstudio 2.8.2 → 4.0.1、Microsoft.NET.Test.Sdk 17.11.1 → 18.11.0。BepInEx.Core 5.4.21、UnityEngine.Modules 2022.3.62、BepInEx.Analyzers 1.0.8 保持不变（Unity 2022.3 兼容上限）。
- 桌面端依赖整体升级（Electron、Vue、Vite、Pinia、Tailwind、daisyUI、Playwright 等），`pnpm audit` 高危清零；仅剩 1 个 electron-builder 传递依赖的中危项（sprintf-js，advisory 要求的修复版本 1.1.4 尚未发布），该链路只参与本地打包工具，不进入发布产物。锁文件同步。
- 图标体系移除 Iconify：删除 `@iconify/vue`、`@iconify-icons/mdi` 依赖与 `desktop/src/icons.ts`，全部改用 `@lucide/vue` 1.59.0 按需导入（不走已废弃的 `lucide-vue-next`），无运行时图标告警。
- Updater 可靠性修复（均有回归测试）：清理目录的最终重试不再逃逸异常，使“新版已安装但临时回滚目录未清理”的提示可达；安装应用失败后不再遗留 `.LoopstructorAutoPlayer-staging-*` 临时目录（最大约 2 GiB）；进程在事务中途被终止后，崩溃恢复会清理遗留 staging 目录。测试数 627 → 630，全部通过。
- 游戏运行时契约修复（对照 Skyspine 1.385 游戏源码审计）：奖励弹窗跳过改为委托原生 `skipReward` 命令并把游戏侧“奖励跳过流程已移除”的提示如实返回，不再静默返回 `REWARD_POPUP_NOT_OPEN`；奖励轻量契约移除游戏中已删除的队列成员后，查询/选择/领取重新可用，跳过保持明确不可用；`independentVehicleMode` 不再返回误导性的 `false`，改为 `null` + 不可用原因；平台写入隔离补丁改为按参数类型解析目标方法，未来出现重载不再抛 `AmbiguousMatchException` 而跳过补丁。
- 发布包：`payload/plugin` 不再携带 `.pdb` / `.xml`；`verify-release-assets.ps1` 增加 `payload/plugin` 与 `payload/bepinex` 必需文件校验，并拒绝插件符号与文档文件。
- QA 脚本：`run-autoplay-qa.ps1` 异常中止或超时后结束游戏进程、不再泄漏；截图前尝试前置游戏窗口，窗口未被前置时写出 `qa-screenshot-occluded.txt`；.NET SDK 版本探测失败不再被空值异常掩盖。
- CI / 发布：`release.yml` 的标签与输入版本号改为经 `env` 传值并用 `scripts/common.ps1` 校验，消除直接拼接 PowerShell 的注入风险；CI 与 Release 都预检全部 `scripts/*.ps1` 语法；`actions/cache` v5 → v6（其余 action 已是最新主版本）。
- 文档：重写 README 与 `docs/architecture.md`、`docs/safety.md`、`docs/release.md`；删除过期的 `docs/codex-handoff.md` 与 AGENTS 模板；校验链接、命令与路径。

### 0.6.76

- 游戏包报错日志：插件订阅 Unity 线程日志，把 Error/Exception/Assert 写入 `game-errors.log`；Host 按 ERROR 级别并入运行日志，5 秒内相同错误合并计数，文件 8 MiB 轮转。

### 0.6.75

- 战车目录获取重写，改为从运行时目录数据构建，不再依赖旧反射路径。

### 0.6.74

- 修复游戏重载后插件启动失败；重载后重新建立管道会话并重新激活校验。

### 0.6.73

- 统一桌面入口标识为装甲车头徽章（Manager、更新窗口、确认弹窗、Launcher/Host/Updater 程序图标）。

### 0.6.72

- 修复 Unity Editor 目录图标获取。

### 0.6.71

- Unity Editor 连接：Editor-only UPM 桥接包、实例登记、Host 回环访问、Edit/Play 两阶段校验。

### 0.6.70

- 更新关闭确认与 Host 完整退出：更新前等待 Manager 与游戏进程退出。

### 0.6.69

- 迁移到 Tailwind + daisyUI，修复工作台布局。

### 0.6.68

- 发布命名、界面精简与更新窗口握手；引入根目录启动器与 `manager\` 布局，作为当前发布布局基线。

更早版本见 git 历史与对应 `v*` 标签。
