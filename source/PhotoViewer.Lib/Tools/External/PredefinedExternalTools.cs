using Avalonia.Input;
using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.Tools;

public static class PredefinedExternalTools
{

    public const string EXIFGLASS_ID = "Tool_ExifGlass";

    private static readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase)
    {
        EXIFGLASS_ID,
    };

    public static IEnumerable<ExternalTool> CreateAll()
    {
        var (exifGlassExe, exifGlassArgs) = BHelper.OS switch
        {
            OSType.Mac => ("/Applications/ExifGlass.app", Const.FILE_MACRO),
            OSType.Linux => ("flatpak", $"run io.github.d2phap.exifglass {Const.FILE_MACRO}"),
            _ => ("exifglass", Const.FILE_MACRO),
        };

        yield return new ExternalTool
        {
            ToolId = EXIFGLASS_ID,
            Executable = exifGlassExe,
            Arguments = exifGlassArgs,
            IsIntegrated = true,
            Hotkeys = [new Hotkey(Key.X)],
        };
    }

    public static bool IsPredefined(string? toolId)
        => !string.IsNullOrEmpty(toolId) && _ids.Contains(toolId);

    public static ExternalTool? Find(string? toolId)
    {
        if (!IsPredefined(toolId)) return null;

        return CreateAll().FirstOrDefault(t =>
            string.Equals(t.ToolId, toolId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Seed(ICollection<ExternalTool> tools)
    {
        var existingIds = new HashSet<string>(
            tools.Select(t => t.ToolId ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var tool in CreateAll())
        {
            if (existingIds.Contains(tool.ToolId)) continue;

            tools.Add(tool);
            changed = true;
        }

        return changed;
    }

}
