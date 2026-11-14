using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace PhotoViewer.Plugins;

public sealed class PluginFailureManager
{
    private const string QUARANTINE_DIR = "_quarantine";
    private const string LOADING_MARKER_EXT = ".loading";
    private const int SOFT_FAILURE_THRESHOLD = 3;

    private readonly string _quarantineDir;
    private readonly ConcurrentDictionary<string, int> _softFailureCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _sessionDisabled = new(StringComparer.Ordinal);

    public PluginFailureManager()
    {
        _quarantineDir = BHelper.ConfigDir(Dir.Plugins, QUARANTINE_DIR);
    }

    public bool IsQuarantined(string pluginId)
    {
        if (_sessionDisabled.ContainsKey(pluginId)) return true;
        return File.Exists(GetMarkerPath(pluginId));
    }

    public string? GetQuarantineReason(string pluginId)
    {
        if (_sessionDisabled.TryGetValue(pluginId, out var sessionReason)) return sessionReason;

        try
        {
            var markerPath = GetMarkerPath(pluginId);
            if (!File.Exists(markerPath)) return null;

            var lines = File.ReadAllLines(markerPath);
            return lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1] : null;
        }
        catch
        {
            return null;
        }
    }

    public void DisableForSession(string pluginId, string reason)
    {
        _sessionDisabled[pluginId] = reason;
        Debug.WriteLine($"[PluginFailureManager] Session-disabled '{pluginId}': {reason}");
    }

    public void RecordSoftFailure(string pluginId, string reason)
    {
        var count = _softFailureCounts.AddOrUpdate(pluginId, 1, static (_, v) => v + 1);
        Debug.WriteLine($"[PluginFailureManager] Soft failure #{count} for '{pluginId}': {reason}");
        if (count >= SOFT_FAILURE_THRESHOLD)
        {
            DisableForSession(pluginId, $"soft-failure threshold ({SOFT_FAILURE_THRESHOLD}) reached");
        }
    }

    public void Quarantine(string pluginId, string reason)
    {
        _sessionDisabled[pluginId] = reason;
        try
        {
            Directory.CreateDirectory(_quarantineDir);
            File.WriteAllText(GetMarkerPath(pluginId),
                $"{DateTimeOffset.UtcNow:O}\n{reason}\n");
            Debug.WriteLine($"[PluginFailureManager] Quarantined '{pluginId}': {reason}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginFailureManager] Failed to write marker for '{pluginId}': {ex.Message}");
        }
    }

    public bool Clear(string pluginId)
    {
        _sessionDisabled.TryRemove(pluginId, out _);
        _softFailureCounts.TryRemove(pluginId, out _);
        ClearLoadingBreadcrumb(pluginId);
        try
        {
            var path = GetMarkerPath(pluginId);
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginFailureManager] Failed to clear marker for '{pluginId}': {ex.Message}");
        }
        return false;
    }

    public bool HasLoadingBreadcrumb(string pluginId)
    {
        return File.Exists(GetLoadingMarkerPath(pluginId));
    }

    public void SetLoadingBreadcrumb(string pluginId)
    {
        try
        {
            Directory.CreateDirectory(_quarantineDir);
            File.WriteAllText(GetLoadingMarkerPath(pluginId), DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginFailureManager] Failed to write loading breadcrumb for '{pluginId}': {ex.Message}");
        }
    }

    public void ClearLoadingBreadcrumb(string pluginId)
    {
        try
        {
            var path = GetLoadingMarkerPath(pluginId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginFailureManager] Failed to clear loading breadcrumb for '{pluginId}': {ex.Message}");
        }
    }

    private string GetLoadingMarkerPath(string pluginId)
    {
        return Path.Combine(_quarantineDir, MakeFilenameSafe(pluginId) + LOADING_MARKER_EXT);
    }

    private string GetMarkerPath(string pluginId)
    {
        var safe = MakeFilenameSafe(pluginId);
        return Path.Combine(_quarantineDir, safe + ".marker");
    }

    private static string MakeFilenameSafe(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        }
        return new string(chars);
    }
}
