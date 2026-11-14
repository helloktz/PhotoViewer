# Photo Viewer

Photo Viewer is a local image viewer for Windows 11 x64. It runs offline and is available as a portable ZIP or an MSI installer.

## Architecture

| Directory | Responsibility |
| --- | --- |
| `source/PhotoViewer.Lib` | Shared Avalonia UI, image decoding and rendering, caching, settings, and extension management |
| `source/PhotoViewer.Win32` | Windows entry point and system integration, including file associations, thumbnails, and printing |
| `source/assets` | Bundled application resources and installer definitions |
| `scripts` | Build validation and Windows packaging |

Codec plugins use the ImageGlass SDK native ABI. External tools communicate with the app over local named pipes.

## Build and package

Use PowerShell 7 and the .NET SDK required by the projects. Native AOT publishing requires the desktop C++ workload from Visual Studio or Build Tools and the Windows SDK. The MSI script can install its pinned WiX tool and UI extension with `-BootstrapWix`.

```powershell
dotnet build source/PhotoViewer.slnx -p:Platform=x64
pwsh -File scripts/script-pack-win-zip.ps1
pwsh -File scripts/script-pack-win-msi.ps1 -SkipPublish -BootstrapWix
```

The packaging scripts produce self-contained Native AOT builds in `.artifacts/bundle/`.

## Run

Extract the portable ZIP to a writable folder and run `PhotoViewer.exe`, or use the MSI to install for the current user or the whole computer.

See [LICENSE](LICENSE) for the source code license.
