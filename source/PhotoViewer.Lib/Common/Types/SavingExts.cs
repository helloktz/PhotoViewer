using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PhotoViewer.Common.Types;

public static class SavingExts
{

    private static IReadOnlyCollection<KeyValuePair<string, string>> BuiltInExtensions =>
    [
        new(".png",   "PNG"),
        new(".jpg",   "JPG"),
        new(".jxl",   "JXL"),
        new(".webp",  "WEBP"),
        new(".avif",  "AVIF"),

        new(".bmp",   "BMP"),
        new(".gif",   "GIF"),
        new(".tiff",  "TIFF"),

        new(".emf",   "EMF"),
        new(".exif",  "EXIF"),
        new(".ico",   "ICO"),
        new(".wmf",   "WMF"),
    ];

    public static ImmutableList<FilePickerFileType> FilePickerFileTypeChoices => BuildChoices();

    public static string? LastSavedExtension { get; set; }

    public static FilePickerFileType? FindChoice(IEnumerable<FilePickerFileType> choices, string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return null;

        var pattern = $"*{ext}";
        var choice = choices.FirstOrDefault(c => c.Patterns?.Contains(pattern, StringComparer.OrdinalIgnoreCase) is true);

        return choice;
    }

    private static ImmutableList<FilePickerFileType> BuildChoices()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FilePickerFileType>(BuiltInExtensions.Count + 8);

        foreach (var (ext, label) in BuiltInExtensions)
        {
            if (!seen.Add(ext)) continue;
            list.Add(NewChoice(ext, label));
        }

        foreach (var plugin in GetPluginExtensions())
        {
            if (!seen.Add(plugin.Ext)) continue;
            list.Add(NewChoice(plugin.Ext, plugin.Ext.TrimStart('.').ToUpperInvariant()));
        }

        return list.ToImmutableList();
    }

    private static FilePickerFileType NewChoice(string ext, string label)
    {
        var codecName = EncoderNameOf(ext);
        var name = codecName.Length > 0
            ? $"{label} – {codecName} (*{ext})"
            : $"{label} (*{ext})";

        return new FilePickerFileType(name) { Patterns = [$"*{ext}"] };
    }

    private static string EncoderNameOf(string ext)
    {
        try
        {
            return Core.CodecRegistry.GetEncodeCodecInfo(ext)?.CodecName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<(string Ext, int EncodePriority)> GetPluginExtensions()
    {
        try
        {
            return Core.CodecRegistry.GetEncodingExtensions()
                .Where(e => e.IsPlugin)
                .Select(e => (e.Ext, e.EncodePriority))
                .OrderByDescending(e => e.EncodePriority)
                .ThenBy(e => e.Ext, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

}
