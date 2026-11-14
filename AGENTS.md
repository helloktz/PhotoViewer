# Photo Viewer

- Work on `main`. Keep `AGENTS.md` at the repository root. Do not add `CLAUDE.md`.
- Target Windows 11 x64. Keep shared UI, image processing, and settings in `source/PhotoViewer.Lib`. Put Windows-specific services in `source/PhotoViewer.Win32`.
- Keep the app usable offline with no license file. Preserve local tools, the ImageGlass SDK plugin ABI, and the local external-tool protocol. Add UI text to both the built-in English strings and `source/assets/app/languages/zh-CN.iglang.json`.
- Preserve Native AOT compatibility. Use the existing source-generated JSON contexts. Preserve image leases and cache limits when changing decoding or rendering.
- Set the version in `source/Directory.Build.props` as `major.minor.patch`, using digits other than `4`. Keep build output, intermediate files, and packaging staging under the root `.artifacts/` directory. Use directory names without a leading underscore unless an existing runtime path requires it.
- For C# changes, run `dotnet build source/PhotoViewer.slnx -p:Platform=x64`. For Native AOT, runtime dependency, or packaging changes, run `pwsh -File scripts/script-pack-win-zip.ps1` and `pwsh -File scripts/script-pack-win-msi.ps1 -SkipPublish`.
- Keep developer entry-point scripts in root `scripts/`. Keep WiX definitions and installer artwork in `source/assets/win/msi/`.
- Keep source, scripts, and build configuration free of comments and author fields. Preserve the embedded copyright, license, and warranty notices.
