# Photo Viewer

A free, offline photo viewer for Windows 11 x64. It uses the C#, Avalonia, SkiaSharp and ImageMagick architecture of ImageGlass 10.

- English (en-US) and Simplified Chinese (zh-CN)
- Local image browsing, editing, printing, plugins and external tools
- HDR tone mapping, projector synchronization, free panning, folder monitoring and managed settings available without a license file

## Build on Windows

Install the .NET 11 SDK and Visual Studio Build Tools with the Desktop development with C++ workload and a Windows SDK. From the repository root, run:

```powershell
pwsh -File scripts/script-pack-win-zip.ps1
pwsh -File scripts/script-pack-win-msi.ps1
```

Both scripts package the self-contained Native AOT application and its local resources. The packages go in `.artifacts/bundle` at the repository root. Build outputs and intermediate files also live under `.artifacts/`.

The ZIP is portable by default. Extract it before running `PhotoViewer.exe`. Pass `-NoPortable` to store settings in the Windows user profile.

The MSI requires WiX 5.0.2 and its UI extension. The installer supports per-user and per-machine installation.

After changing the app logo, run `pwsh -File scripts/script-generate-msi-art.ps1` to regenerate the installer artwork.

### Upgrading an existing install

Run the new MSI in the same installation scope as the existing copy. The setup wizard selects that scope automatically.

For an unattended per-user upgrade, run:

```powershell
msiexec /i .artifacts/bundle/PhotoViewer_1.0.0_win-x64.msi /qn ALLUSERS=2 MSIINSTALLPERUSER=1
```

For an unattended per-machine upgrade, run this from an elevated terminal:

```powershell
msiexec /i .artifacts/bundle/PhotoViewer_1.0.0_win-x64.msi /qn ALLUSERS=1
```

## Source and license

Photo Viewer is derived from [ImageGlass](https://github.com/d2phap/ImageGlass) at commit `642f0d791671c9d6f1b54f15074bf5c03fae62a1`. Copyright notices are listed in [LICENSE](LICENSE).

Photo Viewer was adapted in November 2026. Changes include a separate product identity, the Windows x64 build, removal of application network requests and payment flows, and public access to existing local tools. It is distributed under the [GNU General Public License v3](LICENSE). Third-party notices are included in `credits`.
