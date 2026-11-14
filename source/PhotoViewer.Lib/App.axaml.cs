using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Windows;
using PhotoViewer.UI.Windowing;
using PhotoViewer.ViewModels;
using PhotoViewer.Windows;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public partial class App : Application
{
    private static MainWindow? _mainWindow = null;
    private TaskCompletionSource _taskUi = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private const int LINUX_SYSTEM_THEME_GRACE_MS = 100;

    public static MainWindow MainWindow => _mainWindow!;

    public static SettingsWindow? SettingsWindow { get; set; }

    public Func<MainWindow>? CreateMainWindowFn = null;

    public override void Initialize()
    {
        if (!Debugger.IsAttached)
        {
            Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        AvaloniaXamlLoader.Load(this);
        LocalizeApplication();
        Core.LanguageChanged += (_, _) => LocalizeApplication();
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        StartupTrace.Mark("OnFwInit:enter");
        _ = ApplyUIConfigsAsync();
        PlatformSettings?.ColorValuesChanged += PlatformSettings_ColorValuesChanged;

        if (OperatingSystem.IsMacOS())
        {
            LocalizeAppMenu();
            Core.LanguageChanged += (_, _) => LocalizeAppMenu();
        }

        var activable = this.TryGetFeature<IActivatableLifetime>();
        activable?.Activated += App_Activated;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (ConfigMode.PortableError is not null)
            {
                await _taskUi.Task;
                await ShowPortableModeErrorAsync();
                return;
            }

            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            Core.ShellProvider?.ForegroundShell = Core.ShellProvider.GetForegroundWindowView();

            Core.UpdateInitImagePath();

            StartupTrace.Mark("MainWindow:ctor:begin");
            CreateMainWindowIfNotExist();
            StartupTrace.Mark("MainWindow:ctor:end");

            Core.DiscoverPlugins();

            Core.RegisterExternalTools();
            StartupTrace.Mark("RegisterTools:done");

            await _taskUi.Task;
            StartupTrace.Mark("Theme:ready");

            desktop.MainWindow = MainWindow;

            if (Config.LoadingException is not null)
            {
                var isContinue = await ModalWindow.ShowUnhandledErrorAsync(
                    Config.LoadingException, null,
                    Core.Lang[LangId.Settings_LoadError]);
                if (!isContinue) return;
            }

            if (!await ConfirmIncompatibleConfigResetAsync()) return;

            if (!await RunStartupQuickSetupAsync()) return;

            MainWindow.Show();
            StartupTrace.Mark("MainWindow:show");

        }

        base.OnFrameworkInitializationCompleted();
    }

    private void App_Activated(object? sender, ActivatedEventArgs e)
    {
        if (e is FileActivatedEventArgs fileArgs && fileArgs.Files.Count > 0)
        {
            var filePath = fileArgs.Files[0].TryGetLocalPath();
            var modulePath = Core.Args.FirstOrDefault();
            var isModulePath = filePath?.Equals(modulePath, StringComparison.OrdinalIgnoreCase) ?? false;

            if (!string.IsNullOrEmpty(filePath) && !isModulePath)
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    Core.UpdateInitImagePath(filePath);

                    if (Core.API is not null)
                        _ = Core.API.RunApiAsync(API.IG_OpenPath, filePath);

                    MainWindow?.Activate();
                });
            }
        }

        else if (e.Kind == ActivationKind.Reopen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                MainWindow?.Activate();
            });
        }
    }

    private void AppMenuAbout_Click(object? sender, EventArgs e) => RunAppMenuAction(LangId.Menu_MnuAbout);

    private void AppMenuSettings_Click(object? sender, EventArgs e) => RunAppMenuAction(LangId.Menu_MnuSettings);

    private static void RunAppMenuAction(LangId menuKey)
    {
        var action = AppAPIProvider.GetMenuAction(menuKey);
        if (Core.API is not null) _ = Core.API.RunActionAsync(action);
    }

    private void LocalizeAppMenu()
    {
        if (NativeMenu.GetMenu(this) is not { } menu) return;

        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            if (item.CommandParameter is not string tag || !Enum.TryParse<LangId>(tag, out var langId)) continue;

            item.Header = Core.Lang[langId];

            if (langId == LangId.Menu_MnuSettings)
            {
                item.Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta);
            }

        }
    }

    private void LocalizeApplication()
    {
        Name = BHelper.AppDisplayName;
        LangId[] scrollbarLabels =
        [
            LangId._ScrollLineUp, LangId._ScrollLineDown, LangId._ScrollColumnLeft, LangId._ScrollColumnRight,
            LangId._ScrollPageUp, LangId._ScrollPageDown, LangId._ScrollPageLeft, LangId._ScrollPageRight,
            LangId._ScrollPosition,
        ];

        foreach (var key in scrollbarLabels)
        {
            Resources[key] = Core.Lang[key];
        }
    }

    private void PlatformSettings_ColorValuesChanged(object? sender, PlatformColorValues e)
    {
        Core.IsSystemDarkMode = e.ThemeVariant == PlatformThemeVariant.Dark;

        Dispatcher.UIThread.Post(async () =>
        {
            await ApplyThemePackAsync(Core.IsSystemDarkMode, e.AccentColor1);
        }, DispatcherPriority.Send);
    }

    private static void UIThread_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var ex = e.Exception;

        Dispatcher.UIThread.Post(async () =>
        {
            _ = await ModalWindow.ShowUnhandledErrorAsync(ex);

#if DEBUG
            throw ex;
#endif
        });
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        var ex = e.Exception;

        Dispatcher.UIThread.Post(async () =>
        {
            await ModalWindow.ShowUnhandledErrorAsync(ex);

#if DEBUG
            throw ex;
#endif
        });
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = (Exception)e.ExceptionObject;

        if (e.IsTerminating)
        {
            Console.Error.WriteLine($"Unhandled exception: {BHelper.GetExceptionDetails(ex)}");
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            _ = await ModalWindow.ShowUnhandledErrorAsync(ex);

#if DEBUG
            throw ex;
#endif
        });
    }

    public static bool InitializeAppInstance(string[] args, Action installServicesFn)
    {
        CultureInfo.DefaultThreadCurrentCulture =
            CultureInfo.DefaultThreadCurrentUICulture =
            Thread.CurrentThread.CurrentCulture =
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        Core.Args = Environment.GetCommandLineArgs();

        if (ConfigMode.IsPortable) StartupTrace.Mark("InitInstance:portableMode");

        Core.Config = Config.Load(Config.CONFIG_USER, Core.Args);
        StartupTrace.Mark("InitInstance:configLoaded");

        ServiceProviders.FeatureManager.Refresh();

        installServicesFn();
        StartupTrace.Mark("InitInstance:servicesReady");

        if (App.HandleCommandLineAsync(args).GetAwaiter().GetResult())
        {
            return true;
        }

        if (!Core.Config.EnableMultiInstances && !Core.AppInstance.IsFirstInstance)
        {
            if (Core.AppInstance.SendArgsToExistingInstances(AppCmds.SINGLE_INSTANCE, args))
            {
                return true;
            }
        }

        Core.Config.LoadCurrentLanguageAsync().GetAwaiter().GetResult();
        return false;
    }

    private static async Task<bool> HandleCommandLineAsync(string[] args)
    {
        StartupTrace.EnableFromArgs(args);

        PhotoTrace.EnableFromArgs(args);

        if (args.Length < 1) return false;

        var topCmd = args[0];

        if (topCmd == AppCmds.SET_DEFAULT_PHOTO_VIEWER
            || topCmd == AppCmds.REMOVE_DEFAULT_PHOTO_VIEWER)
        {
            var enable = topCmd == AppCmds.SET_DEFAULT_PHOTO_VIEWER;

            var extensions = args.Length >= 2
                ? args[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Core.GetSupportedFileExtensions().ToArray();

            if (Core.ShellProvider is not null)
            {
                await Core.Config.LoadCurrentLanguageAsync();
                await Core.ShellProvider.SetDefaultPhotoViewerAsync(extensions, enable);
            }

            return true;
        }

        return false;
    }

    private static async Task ShowPortableModeErrorAsync()
    {
        var error = ConfigMode.PortableError!;

        _ = await ModalWindow.ShowErrorAsync(null, new ModalWindowOptions
        {
            Title = BHelper.AppDisplayName,
            Heading = error.Message,
            Details = BHelper.GetExceptionDetails(error),
            ShowInTaskbar = true,
        }, ModalWindowButton.Close);

        BHelper.ExitApp(true);
    }

    private static async Task<bool> ConfirmIncompatibleConfigResetAsync()
    {
        var configPath = Config.IncompatibleUserConfigPath;
        if (string.IsNullOrEmpty(configPath)) return true;

        var result = await ModalWindow.ShowWarningAsync(null, new ModalWindowOptions
        {
            Title = BHelper.AppDisplayName,
            Heading = Core.Lang[LangId._IncompatibleConfig],
            Description = Core.Lang[LangId._IncompatibleConfig_Description],
            Details = configPath,
            Note = Core.Lang[LangId._IncompatibleConfig_BackupNote],
            NoteStyle = InfoBarSeverity.Warning,
            ShowInTaskbar = true,
        }, ModalWindowButton.Yes_No);

        if (result.ExitCode != DialogExitCode.OK)
        {
            BHelper.ExitApp(true);
            return false;
        }

        return true;
    }

    private static async Task<bool> RunStartupQuickSetupAsync()
    {
        if (!QuickSetupWindow.ShouldShowAtStartup || Core.Args.Contains(AppCmds.NO_QUICK_SETUP))
            return true;

        var wizard = new QuickSetupWindow();
        await wizard.ShowAsync(null);

        if (wizard.IsRestarting) return false;

        if (wizard.DialogResult == DialogExitCode.Cancel)
        {
            Core.Config.QuickSetupVersion = Const.QUICK_SETUP_VERSION;
            if (!await Core.Config.SaveAsync())
            {
                await ModalWindow.ShowSettingsSaveErrorAsync(null, Core.Lang[LangId.QuickSetup_Title]);
            }
            BHelper.RestartApp(suppressQuickSetup: true);
            return false;
        }

        BHelper.ExitApp(true);
        return false;
    }

    private void CreateMainWindowIfNotExist()
    {
        if (_mainWindow is not null) return;

        if (CreateMainWindowFn is not null)
        {
            _mainWindow = CreateMainWindowFn();
        }
        else
        {
            var mainWin = new MainWindow();
            mainWin.DataContext = new MainWindowModel(mainWin);

            _mainWindow = mainWin;
        }

        Core.API = new AppAPIProvider();

        if (Core.ShellProvider is { } shell) _ = Task.Run(shell.RepairDefaultViewerRegistration);
    }

    private async Task ApplyUIConfigsAsync()
    {
        try
        {
            Core.UpdateBaseResources();

            var themeApplied = false;
            if (BHelper.OS == OSType.Linux)
            {
                await Task.WhenAny(_taskUi.Task, Task.Delay(LINUX_SYSTEM_THEME_GRACE_MS));

                themeApplied = _taskUi.Task.IsCompleted;
            }

            if (!themeApplied)
            {
                var info = PlatformSettings!.GetColorValues();
                var isSystemDarkMode = info.ThemeVariant == PlatformThemeVariant.Dark;

                Core.IsSystemDarkMode = isSystemDarkMode;

                try
                {
                    await ApplyThemePackAsync(isSystemDarkMode, info.AccentColor1);
                }
                catch (Exception ex)
                {
                    var isContinue = await ModalWindow.ShowUnhandledErrorAsync(ex);
                    if (!isContinue) return;
                }
            }

            _ = Task.Run(MagickCodec.Initialize);
        }
        finally
        {
            _ = _taskUi.TrySetResult();
        }
    }

    public async Task ApplyThemePackAsync(bool isSystemDarkMode, Color? systemAccentColor = null)
    {
        var hasThemeChanged = await Core.Config.LoadCurrentThemeAsync(isSystemDarkMode,
                useFallBackTheme: true,
                throwIfThemeInvalid: true,
                forceUpdateBackground: false);

        var systemAccent = systemAccentColor
            ?? PlatformSettings?.GetColorValues().AccentColor1
            ?? Core.AccentColor;
        var accent = Core.Theme.UseSystemAccent
            ? systemAccent
            : Core.Theme.AccentColor;
        var hasAccentChanged = Core.SetAccentColor(accent);

        Core.SetAppDarkThemeVariant(Core.Theme.Settings.IsDarkMode);

        if (hasAccentChanged || hasThemeChanged)
        {
            Core.UpdateAccentColorResources();
            AppThemeColors.Load(Core.Theme.Colors, accent);
            Core.UpdateAppThemedColorResources();
        }

        if (hasThemeChanged)
        {
            Core.OnThemeChanged();
        }

        StartupTrace.Mark("ApplyThemePack:done");
        _ = _taskUi.TrySetResult();
    }

}
