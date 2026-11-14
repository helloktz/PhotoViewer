using PhotoViewer.UI;
using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types;

[JsonSerializable(typeof(EditingApp))]
public partial class EditingAppJsonContext : JsonSerializerContext { }

public class EditingApp
{

    public const string ALL_EXTENSIONS = ".*";

    public string AppName { get; set; } = string.Empty;

    public string Executable { get; set; } = string.Empty;

    public string Argument { get; set; } = string.Empty;

    public EditingApp() { }

    public EditingApp(string appName = "", string executable = "", string argument = "")
    {
        AppName = appName;
        Executable = executable;
        Argument = argument;
    }

    public static EditingApp? GetFromExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return null;

        EditingApp? wildcardApp = null;

        foreach (var (key, app) in Core.Config.EditApps)
        {
            if (app is null) continue;

            var exts = key.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (exts.Contains(ext)) return app;
            if (exts.Contains(ALL_EXTENSIONS)) wildcardApp = app;
        }

        return wildcardApp;
    }

    public static void UpdateAppNameForMenuEdit(PhMenuItem mnuEdit)
    {
        var appName = string.Empty;
        if (Core.ClipboardImage is null)
        {
            var ext = Core.Photos.Current?.Extension;
            if (GetFromExtension(ext) is EditingApp app)
            {
                appName = $"({app.AppName})";
            }
            else if (BHelper.OS == OSType.Windows)
            {
                appName = "(Microsoft Paint)";
            }
        }

        mnuEdit.LangParams = appName;
    }

}

public enum AfterEditAppAction
{
    Nothing = 0,
    Minimize = 1,
    Close = 2,
}
