# Photo Viewer

A free, offline photo viewer for Windows 11 x64. It uses the C#, Avalonia, SkiaSharp and ImageMagick architecture of ImageGlass 10.

- English (en-US) and Simplified Chinese (zh-CN)
- Local image browsing, editing, printing, plugins and external tools
- HDR tone mapping, projector synchronization, free panning, folder monitoring and managed settings available without a license file

## Build on Windows

Install the .NET 11 SDK and Visual Studio Build Tools with the Desktop development with C++ workload and a Windows SDK. From the repository root, run:

```powershell
pwsh -File source/__assets/win/script-pack-win-zip.ps1
```

The script publishes the self-contained Native AOT build and places the portable ZIP in `source/__artifacts/bundle`. Extract the entire ZIP before running `PhotoViewer.exe`.

## Source and license

Photo Viewer is derived from [ImageGlass](https://github.com/d2phap/ImageGlass) at commit `642f0d791671c9d6f1b54f15074bf5c03fae62a1`. ImageGlass copyright belongs to Dương Diệu Pháp and its contributors.

Photo Viewer was adapted in November 2026. Changes include a separate product identity, the Windows x64 build, removal of application network requests and payment flows, and public access to existing local tools. It is distributed under the [GNU General Public License v3](LICENSE). Third-party notices are included in `_credits`.
