# 这是什么?

一个简洁的Windows 桌面工具。通过读取风云社直播助手实时文件，根据 ZE 地图名称与当前关卡显示通关奖励。

## 功能

- 显示当前关卡的通关云点、云点伤害转化比、通关龙晶和龙晶伤害转化比。
- 可自定义地图 / 关卡读取间隔（0.2～60 秒，默认 0.6 秒）和奖励联网更新间隔（1～1440 分钟，默认 10 分钟）。
- 透明置顶悬浮窗，支持拖动、位置保存和锁定后鼠标穿透。
- 可设置全局快捷键、背景不透明度、整体显示大小和本机已安装的文本字体。
- 可逐项勾选隐藏地图信息、四项奖励、来源状态、快捷键提示和错误详情，也可一键仅显示四项奖励。
- 找不到广播文件时提供手动选择入口，支持填写完整路径或环境变量；未生成的文件出现后自动开始读取。
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

| 操作 | 默认快捷键 |
| --- | --- |
| 显示 / 隐藏 | Ctrl + Shift + F8 |
| 拖动 / 锁定 | Ctrl + Shift + F9 |
| 打开设置 | Ctrl + Shift + F10 |

你可以通过任务栏找到该软件,右键进行设置。
CS2 建议使用无边框窗口或普通窗口；独占全屏可能覆盖桌面悬浮窗。

设置分为「数据与文件」「外观与字体」「隐藏信息」「快捷键」四个页签。勾选隐藏项后点击「保存设置」立即生效；旧版本的快捷键、透明度和位置会保留。

## 数据来源

- 实时输入：`%LOCALAPPDATA%\fys\fys.broadcast.assistant.txt`，需要在 `FYS助手`的直播中心开启2个必选项:
          直播助手-显示内容-`关卡`和`地图`。
- 官方奖励：[fyscs/cs2 — 2001/sharp/configs/rewards](https://github.com/fyscs/cs2/tree/master/2001/sharp/configs/rewards)。
- 本机设置和缓存：`%LOCALAPPDATA%\FysZeRewardOverlay`。

这里只显示公开配置的基础奖励，服务器同步、个人加成和活动倍率可能影响实际结算。
显示关卡奖励信息不会依据玩家的VIP加成倍率而更改,所以通关结算奖励以服务器获取为准。
