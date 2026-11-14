# Photo Viewer

适用于 Windows 11 x64 的本地图片查看器。支持图片和文件夹浏览、缩略图、幻灯片、裁剪、缩放、取色、HDR 色调映射、打印和 PDF 导出。内置简体中文和英文语言包，可加载本地主题、编解码插件和外部工具。

## 运行

- ZIP 默认是便携包。解压到可写目录后运行 `PhotoViewer.exe`，设置保存在程序目录。
- MSI 支持按用户或按计算机安装。非便携模式的用户设置保存在 `%LOCALAPPDATA%\PhotoViewer`。

## 架构

| 目录 | 职责 |
| --- | --- |
| `source/PhotoViewer.Lib` | Avalonia 界面、图片浏览与缓存、SkiaSharp / Magick.NET / Svg.Skia 编解码、设置和扩展管理 |
| `source/PhotoViewer.Win32` | Windows 启动入口、资源管理器集成、文件关联、系统缩略图和打印 |
| `source/assets/app` | 随程序分发的语言包、主题、图标和过渡效果 |
| `source/assets/win/msi` | WiX 安装器定义和资源 |
| `scripts` | 语言包校验和 Windows 打包脚本 |

编解码插件使用 ImageGlass SDK 的原生 ABI。集成外部工具通过本地命名管道与程序通信。两份语言包走同一套加载流程，构建时检查翻译键和格式占位符。运行时缺少当前语言包或文案键时显示 `undefined`。

## 构建

准备 .NET 11 SDK 和 PowerShell 7。Native AOT 发布还需要 Visual Studio 或 Build Tools 的「使用 C++ 的桌面开发」工作负载及 Windows SDK。生成 MSI 需要 WiX 5.0.2 及同版本的 `WixToolset.UI.wixext`。

```powershell
dotnet build source/PhotoViewer.slnx -p:Platform=x64
pwsh -File scripts/script-pack-win-zip.ps1
pwsh -File scripts/script-pack-win-msi.ps1 -SkipPublish
```

打包脚本生成自包含的 `win-x64` Native AOT 产物，无需单独安装 .NET 运行时。ZIP 和 MSI 输出到 `.artifacts/bundle/`。

## 许可

Photo Viewer 的源码许可见 [LICENSE](LICENSE)。
