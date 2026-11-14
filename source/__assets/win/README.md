# Windows x64 packages

Run these commands from the repository root. Both packages include the Native AOT application and its local resources.

```powershell
pwsh -File source/__assets/win/script-pack-win-zip.ps1
pwsh -File source/__assets/win/script-pack-win-msi.ps1
```

The ZIP is portable by default. Use `-NoPortable` to keep settings in the user profile. The MSI uses WiX 5.0.2 with its UI extension and supports a per-user or per-machine installation. Package files are written to `source/__artifacts/bundle`.
