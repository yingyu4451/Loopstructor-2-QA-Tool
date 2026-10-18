# 构建、发布与更新

## 固定依赖

| 依赖 | 版本 | 来源 |
| --- | --- | --- |
| .NET SDK | 8.0.423 | `global.json`（精确版本，禁止前滚），`scripts/bootstrap.ps1` 安装到仓库 `.dotnet` |
| BepInEx 运行时 | 5.4.23.5 win-x64 | `Directory.Build.props` 的下载地址与 SHA-256：`82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4` |
| BepInEx.Core | 5.4.21 | 插件 NuGet 引用 |
| UnityEngine.Modules | 2022.3.62 | 插件与 Editor Bridge 运行时引用 |
| 插件目标框架 | netstandard2.1 | 游戏 Mono 2.1 |
| 公共依赖（Core/Plugin） | Newtonsoft.Json 13.0.4 | NuGet 引用 |
| 测试依赖 | xunit 2.9.3、xunit.runner.visualstudio 4.0.1、Microsoft.NET.Test.Sdk 18.11.0、Mono.Cecil 0.11.6 | `tests/Loopstructor.AutoPlayer.Tests` |
| Editor Bridge | UPM 包，Unity 2022.3，依赖 `com.unity.nuget.newtonsoft-json` 3.2.1 | `resources/unity-package/com.loopstructor.qa-editor-bridge` |
| Electron | 44.9.0 | `desktop/package.json`（精确版本） |
| Node.js | 24 | 构建与 CI（Electron 44.9 内置 Node 24.22） |
| pnpm | 11.19.0 | `desktop/package.json` 的 `packageManager` |
| Vue / Vite / TypeScript | 3.5.44 / 8.3.4 / 5.9.3 | `desktop/package.json`（TypeScript 7 因 typescript-eslint 与 vue-tsc 尚不支持而未升级） |
| Tailwind / daisyUI | 4.3.3 / 5.7.50 | `desktop/package.json` |
| 界面图标 | @lucide/vue 1.59.0 | `desktop/package.json`（无 Iconify 依赖，无已废弃的 lucide-vue-next） |
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
./scripts/package.ps1 -Version 0.6.77
```

参数：`-Version`（缺省取 `Directory.Build.props` 的 `VersionPrefix`；发布时必须显式传入与源码一致的版本）、`-SkipBuild`（复用已有构建产物）、`-SevenZipPath`（7-Zip 路径，缺省自动探测）。

流程：校验版本（`PluginInfo.cs` 与参数一致）→ 构建（或跳过）→ 组装 `artifacts\package\Loopstructor-2-QA-Tool\` → 生成 `checksums.sha256` → 用 7-Zip 以最大压缩率生成 zip → 校验 zip 与目录一致且无嵌套压缩包。

输出到 `artifacts\release\`：

```text
Loopstructor-2-QA-Tool-0.6.77-win-x64.zip
Loopstructor-2-QA-Tool-0.6.77-win-x64.zip.sha256
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
  "version": "0.6.77",
  "runtimeIdentifier": "win-x64",
  "assetName": "Loopstructor-2-QA-Tool-0.6.77-win-x64.zip",
  "sha256": "<整包 SHA-256>",
  "size": 123456789,
  "deltaAssets": [
    {
      "fromVersion": "0.6.76",
      "assetName": "Loopstructor-2-QA-Tool-0.6.76-to-0.6.77-win-x64.delta.zip",
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

## GitHub Actions

### CI（`.github/workflows/ci.yml`）

- 触发：所有分支 push、pull_request、手动 dispatch。
- 运行环境：`windows-2022`，超时 45 分钟，`contents: read`。
- 步骤：checkout → 安装 pnpm 11.19.0 → Node 24（缓存 pnpm）→ `pnpm install --frozen-lockfile` → 缓存固定 .NET SDK 与 NuGet 包 → `bootstrap.ps1` → `build.ps1 -Configuration Release` → `test.ps1 -Configuration Release -NoRestore -NoBuild` → `pnpm test:e2e` → 校验全部 `scripts/*.ps1` 可解析且语义版本比较正确 → 上传 TRX 测试结果（保留 14 天）。

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
git tag v0.6.77
git push origin v0.6.77
```

## 升级 BepInEx

1. 修改 `Directory.Build.props`：`BepInExRuntimeVersion`、`BepInExRuntimeArchiveName`、`BepInExRuntimeDownloadUrl`、`BepInExRuntimeSha256`。
2. 升级插件引用的 `BepInEx.Core` 包版本；如 API 变化，同步插件补丁代码。
3. 重新构建并跑完整测试。
4. 本地打包后用 `scripts/run-autoplay-qa.ps1` 启动一次隔离 QA，确认插件加载、`hello` 通过、QA 命令可执行；再用 Manager 走一次 ResidentPlayer 启动。
5. `payload\bepinex` 的内容来自固定 zip，运行时升级必须重新打包整包并递增产品版本；已安装旧运行时的游戏目录通过整包更新或重新安装插件更新。插件不读取、也不校验 BepInEx 运行时版本，兼容性只能靠打包与冒烟验证。

## 真实包验证基线

2026-07-29，Skyspine `1.385`，隔离 QA 模式：

- 普通模式与随机模式均正常开局、自动游玩并停止。
- 运行统计：`WavesStarted=2`、`WavesCompleted=1`（验证轮次短，非失败）。
- `Assembly-CSharp.dll` SHA-256：`962b9f69774d4ea20458877363bc33bc7f85cfa207baa1d775b0ea5677140f29`。
- `RandomMode_TurnTableManager.StopDecorateAnimation` 打印 `Animator.Play` 空引用，属游戏侧非致命日志。
- 本机无 AppID 3841840 授权，`SteamAPI_Init` 失败并打印日志，不影响隔离 QA 启动与游玩。

## 版本记录

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
