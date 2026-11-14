# Photo Viewer

Photo Viewer 是适用于 Windows 11 x64 的本地图片查看器，可离线运行，提供便携 ZIP 和 MSI 安装包。

## 架构

| 目录 | 职责 |
| --- | --- |
| `source/PhotoViewer.Lib` | 共享 Avalonia 界面、图片解码与渲染、缓存、设置和扩展管理 |
| `source/PhotoViewer.Win32` | Windows 启动入口与系统集成，包括文件关联、缩略图和打印 |
| `source/assets` | 随应用分发的资源及安装器定义 |
| `scripts` | 构建校验与 Windows 打包 |

编解码插件使用 ImageGlass SDK 的原生 ABI。外部工具通过本地命名管道与程序通信。

## 构建与打包

使用 PowerShell 7 和项目要求的 .NET SDK。Native AOT 发布需要 Visual Studio 或 Build Tools 的「使用 C++ 的桌面开发」工作负载及 Windows SDK。MSI 脚本可通过 `-BootstrapWix` 安装指定版本的 WiX 工具及 UI 扩展。

```powershell
dotnet build source/PhotoViewer.slnx -p:Platform=x64
pwsh -File scripts/script-pack-win-zip.ps1
pwsh -File scripts/script-pack-win-msi.ps1 -SkipPublish -BootstrapWix
```

打包脚本生成自包含的 Native AOT 产物，输出到 `.artifacts/bundle/`。

## 运行

将便携 ZIP 解压到可写目录后运行 `PhotoViewer.exe`，或使用 MSI 为当前用户或整台计算机安装。

源码许可见 [LICENSE](LICENSE)。
