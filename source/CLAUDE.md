# Photo Viewer

The Windows 11 x64 application uses C#, .NET 11, Avalonia, SkiaSharp, and ImageMagick. The solution is `PhotoViewer.slnx`. Keep shared UI, codecs, and settings in `PhotoViewer.Lib`, and Windows-specific services in `PhotoViewer.Win32`.

Preserve Native AOT compatibility. Use the existing source-generated JSON contexts and keep GPU image ownership and cache limits intact. Local plugins use the upstream ImageGlass SDK ABI.

Build and publish using the x64 commands in the repository README. The supported interface languages are built-in English and the bundled Simplified Chinese pack.
