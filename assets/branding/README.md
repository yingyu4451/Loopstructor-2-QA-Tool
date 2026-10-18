# 工具标识

`manager-source.png` 是用户选定的装甲车头徽章原图，保留原始透明通道与完整构图。Manager、更新窗口、确认弹窗和 Launcher/Host/Updater 的程序图标都由它派生，不重新绘制。

| 文件 | 用途 |
| --- | --- |
| `manager-source.png` | 源图，不参与构建 |
| `manager-logo-1024.png` | 主图（1024×1024） |
| `manager-logo-256.png` | 界面用图，Electron 打包为 `branding/manager-logo-256.png` |
| `manager.ico` | Windows 程序图标，含 16/20/24/32/40/48/64/128/256 帧 |
| `cheat-logo-*.png`、`cheat.ico` | 历史标识，当前统一桌面入口不使用，保留不动 |

重新生成（需要 Pillow）：

```powershell
python scripts/generate-branding-assets.py --kind manager
```

脚本输出到 `assets\branding\`，只做缩放与格式转换，不裁切、不修改颜色或透明背景。`--kind cheat` 可单独重新生成历史标识，`--kind all`（默认）同时生成两类文件。

`desktop/package.json` 的 electron-builder 配置以 `../assets/branding` 为 `buildResources`，并引用 `manager-logo-256.png` 与 `manager.ico`。
