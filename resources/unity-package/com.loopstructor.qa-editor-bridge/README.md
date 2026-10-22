# Loopstructor 2 QA Editor Bridge

本包仅在 Unity Editor 中加载，不进入 Player 构建。Loopstructor 2 QA Tool 负责安装、更新和卸载本包；不要手工修改 `Editor/Managed` 中的工具程序集。

Edit Mode 提供工程、编译、场景和基础目录状态。进入 Play Mode 后，本包启动 Editor 运行层，现有 QA 调试命令通过带随机 Bearer token 的回环 HTTP 通道执行；地图自由跳转、游戏内 `F`/`F1` 快捷键与获取池切换在 Editor Play Mode 与 Player 插件中都可以使用（地图自由跳转在 Editor 内使用工程自带的 `0Harmony` 安装补丁）。自动游玩仅在 Player 插件模式提供。
