# 这是什么?

一个 Windows 桌面工具。地图可从风云社直播助手叠层文本或 CS2 GSI 获取，悬浮窗根据地图和当前关卡显示通关奖励。

## 功能

- 显示当前关卡的通关奖励信息。
- 可自定义地图 / 关卡读取间隔（0.2～60 秒，默认 10 秒）和奖励联网更新间隔（1～1440 分钟，默认 1440 分钟）。
- 透明置顶悬浮窗，支持拖动、位置保存和锁定后鼠标穿透。
- 可设置全局快捷键、背景不透明度、整体显示大小和本机已安装的文本字体。
- 在「外观与字体」用平铺选项设置全局或单项字号、颜色和粗细，右侧实时预览悬浮窗。
- 可逐项开启地图信息、四项奖励、来源状态、快捷键提示和错误详情，也可一键仅显示四项奖励。
- 找不到直播助手叠层文本时可手动选择，路径支持环境变量。叠层文本生成后会自动读取。
- 从rewards公开配置获取奖励；网络失败时使用带时间标记的本地缓存。
- 非 ZE 地图、没有地图/关卡信息或奖励缺失时，显示「当前无法获取该地图信息」。

## 运行和构建

环境：Windows 10/11，.NET 10 SDK（构建）或 .NET 10 Windows Desktop Runtime（运行构建产物）。

在项目目录运行 PowerShell：

```powershell
dotnet run --project .\tests\Overlay.Tests.csproj -c Release
dotnet publish .\src\FysZeRewardOverlay.csproj -c Release --no-self-contained -o .\app
.\app\FysZeRewardOverlay.exe
```

构建输出在 `app/`，不提交到 Git。复制运行时请保留该目录内的配套文件。更新已有程序前，先从系统托盘退出正在运行的实例。

也可以通过Release下载已打包的文件,打开`FysZeRewardOverlay.exe`即可.

| 操作 | 默认快捷键 |
| --- | --- |
| 显示 / 隐藏 | Alt + P |
| 拖动 / 锁定 | Alt + O |
| 打开设置 | Ctrl + Shift + F10 |
| 提高 / 降低推断关卡 | Shift + = / Shift + - |

右键任务栏通知区域的托盘图标可打开菜单，双击图标可打开设置。设置窗口支持从顶部拖动、最小化和最大化。
CS2 建议使用无边框窗口或普通窗口；独占全屏可能覆盖桌面悬浮窗。

左侧导航可切换「数据与文件」「外观与字体」「显示信息」「快捷键」「配置管理」。「外观与字体」可调整行间距、左右文字间距，并进入悬浮窗拖动模式。点击「保存设置」后即时应用，设置窗口保持打开。

在「配置管理」中可将已保存的设置导出为 JSON，也可导入 JSON 并立即应用。「恢复为默认」会弹出警告，确认后覆盖当前设置。

左下角显示 GSI 监听状态。「关于」可查看当前版本、作者和 GitHub 仓库。

## 数据来源

- **直播助手叠层文本（默认）**：从 `%LOCALAPPDATA%\fys\fys.broadcast.assistant.txt` 读取地图和关卡。在 FYS 助手的直播中心 → 直播助手 → 显示内容中勾选「地图」和「关卡」。
- **CS2 GSI**：在「设置 → 数据与文件」切换地图来源，点击「保存 GSI 配置文件…」，将 `gamestate_integration_fys_reward_overlay.cfg` 保存到 CS2 的 `game\csgo\cfg`，保存悬浮窗设置后重启 CS2。优先使用匹配地图和区服的叠层文本关卡。读不到明确关卡时按 CT 比分加 1 推断，可用快捷键或设置页按钮修正。
- 官方奖励：[fyscs/cs2 — 2001/sharp/configs/rewards](https://github.com/fyscs/cs2/tree/master/2001/sharp/configs/rewards)。
- 本机设置和缓存：`%LOCALAPPDATA%\FysZeRewardOverlay`。

这里只显示公开配置的基础奖励，服务器同步、个人加成和活动倍率可能影响实际结算。
显示关卡奖励信息不会依据玩家的VIP加成倍率而更改,所以通关结算奖励以服务器获取为准。

## 致谢

感谢奖励数据维护者、界面参考项目和开源依赖的贡献者。详见[致谢与开源依赖](ACKNOWLEDGMENTS.md)。
