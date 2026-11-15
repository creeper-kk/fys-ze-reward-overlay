# 致谢与开源依赖

项目作者 **creeper_kk**。

## 数据与界面参考

- 程序附带风云社（fyscs）公开奖励配置快照。快照合并了地图文件并移除了注释，数据按 CC BY-NC-SA 4.0 提供。见 [上游奖励配置](https://github.com/fyscs/cs2/tree/1f27a0e2c51e9ed0d4df65436f8d19a6db987d14/2001/sharp/configs/rewards) 和 [许可文本](https://github.com/fyscs/cs2/blob/1f27a0e2c51e9ed0d4df65436f8d19a6db987d14/LICENSE.md)。
- Valve 提供 Counter-Strike 的 Game State Integration，用于读取地图、比分和本局击杀数。[GSI 文档](https://developer.valvesoftware.com/wiki/Counter-Strike:_Global_Offensive_Game_State_Integration)
- Widdit 的 Now Playing 提供无边框设置窗口和侧栏布局的设计参考。[Now Playing](https://github.com/Widdit/now-playing-service)

## 开源依赖

| 组件 | 在本项目中的用途 | 开源许可 |
| --- | --- | --- |
| [.NET 运行时与基础类库](https://github.com/dotnet/runtime) | HTTP、JSON 与文件读写 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [WPF](https://github.com/dotnet/wpf) | 悬浮窗、设置和实时预览 | [MIT](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT) |
| [Windows Forms](https://github.com/dotnet/winforms) | 系统托盘图标、右键菜单与选色器 | [MIT](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT) |

这些组件由 .NET 10 Windows Desktop Runtime 提供。项目没有额外的 NuGet 包引用。
