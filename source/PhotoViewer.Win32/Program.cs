using Avalonia;
using Avalonia.Input;
using PhotoViewer.Common;
using PhotoViewer.Common.Loggers;
using PhotoViewer.ViewModels;
using PhotoViewer.Win32.Common.ServiceProviders;
using PhotoViewer.Win32.Windows;
using System;
using System.Runtime.InteropServices;

namespace PhotoViewer.Win32;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            return 1;
        }

        Svg.SvgDocument.ResolveExternalImages = Svg.ExternalType.Local;
        Svg.SvgDocument.ResolveExternalElements = Svg.ExternalType.Local;

        StartupTrace.Mark("Main:start");
        Core.BuildInfo = new AppBuildInfo();

        var isHandled = App.InitializeAppInstance(args, () =>
        {
            Core.FileSearchProvider = new Win32FileSearchProvider();
            Core.PreviewProvider = new Win32PhotoPreviewProvider();
            Core.ShellProvider = new Win32ShellProvider();
            Core.PrintProvider = new Win32PrintProvider();
            Core.PipeSecurityProvider = new Win32PipeSecurityProvider();
        });

        if (isHandled) return 0;

        StartupTrace.Mark("Avalonia:start");
        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
#if DEBUG
        .LogToTrace()
        .WithDeveloperTools(o =>
        {
            o.ApplicationName = BHelper.AppDisplayName;
            o.Gesture = new KeyGesture(Key.I, KeyModifiers.Control | KeyModifiers.Shift);
        })
#endif
        .UseWin32()
        .UseSkia()
        .UseHarfBuzz()
        .With(new SkiaOptions
        {
            MaxGpuResourceSizeBytes = long.MaxValue,
        })
        .AfterSetup(builder =>
        {
            var app = (App?)builder.Instance;
            app?.CreateMainWindowFn = () =>
            {
                var mainWindow = new MainWindow32();
                mainWindow.DataContext = new MainWindowModel(mainWindow);

                return mainWindow;
            };
        });
}
