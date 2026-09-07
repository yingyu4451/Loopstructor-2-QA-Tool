# 工具标识

`manager-source.png` 是用户选定的装甲车头徽章原图，保留原始透明通道与完整构图。Manager、更新窗口、确认弹窗和 Launcher/Host/Updater 的程序图标均由此图派生，不重新绘制。

安装 Pillow 后，在仓库根目录运行：

```powershell
python scripts/generate-branding-assets.py --kind manager
```

生成 `manager-logo-1024.png`、`manager-logo-256.png` 和包含 16、20、24、32、40、48、64、128、256 像素帧的 `manager.ico`。仅缩放和转换格式，不裁切、不修改颜色或透明背景。

`cheat-*` 是未用于当前统一桌面入口的历史标识，本次保留不变。
