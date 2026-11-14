using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using ImageGlass.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhotoViewer.Plugins;

public static class PluginTrustPolicy
{

    public enum TrustState
    {

        Missing,

        Untrusted,

        Disabled,

        Trusted,

        Changed,
    }

    public static string? ResolveLibraryPath(PluginManifest manifest, string pluginDir)
    {
        return PluginRegistry.TryResolvePluginLibraryPath(manifest.Executable, pluginDir, out var path)
            ? path
            : null;
    }

    public static string? ComputeSha256(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public static bool IsTrusted(string pluginId, string libraryPath)
    {
        if (!Core.Config.PluginTrust.TryGetValue(pluginId, out var info) || info is null || !info.Enabled)
            return false;

        var hash = ComputeSha256(libraryPath);
        return hash is not null && string.Equals(hash, info.Hash, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;

        var trimmed = extension.Trim().ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    public static (IReadOnlySet<string> Decode, IReadOnlySet<string> Encode) GetExtensionExclusions(string pluginId)
    {
        Core.Config.PluginTrust.TryGetValue(pluginId, out var info);

        return (
            info?.DisabledDecodeExtensions ?? EMPTY_EXCLUSIONS,
            info?.DisabledEncodeExtensions ?? EMPTY_EXCLUSIONS);
    }

    public static bool IsExtensionEnabledForDecode(string pluginId, string extension)
        => !GetExtensionExclusions(pluginId).Decode.Contains(NormalizeExtension(extension));

    public static bool IsExtensionEnabledForEncode(string pluginId, string extension)
        => !GetExtensionExclusions(pluginId).Encode.Contains(NormalizeExtension(extension));

    public static async Task<bool> SetExtensionExclusionsAsync(string pluginId,
        IEnumerable<string>? disabledDecode, IEnumerable<string>? disabledEncode)
    {
        if (Config.IsConfigLocked(ConfigId.PluginTrust)) return false;
        if (!Core.Config.PluginTrust.ContainsKey(pluginId)) return false;

        await UpsertAsync(pluginId, old => new PluginTrustInfo
        {
            Enabled = old?.Enabled ?? false,
            Hash = old?.Hash ?? string.Empty,
            DisabledDecodeExtensions = Normalize(disabledDecode),
            DisabledEncodeExtensions = Normalize(disabledEncode),
        });
        return true;
    }

    public static TrustState GetState(PluginManifest manifest, string pluginDir)
    {
        var libraryPath = ResolveLibraryPath(manifest, pluginDir);
        if (libraryPath is null || !File.Exists(libraryPath)) return TrustState.Missing;

        if (!Core.Config.PluginTrust.TryGetValue(manifest.Id, out var info) || info is null)
            return TrustState.Untrusted;

        if (!info.Enabled) return TrustState.Disabled;

        var hash = ComputeSha256(libraryPath);
        return hash is not null && string.Equals(hash, info.Hash, StringComparison.OrdinalIgnoreCase)
            ? TrustState.Trusted
            : TrustState.Changed;
    }

    public static async Task<bool> TrustAsync(PluginManifest manifest, string pluginDir,
        IEnumerable<string>? disabledDecode = null, IEnumerable<string>? disabledEncode = null)
    {
        if (Config.IsConfigLocked(ConfigId.PluginTrust)) return false;

        var libraryPath = ResolveLibraryPath(manifest, pluginDir);
        if (libraryPath is null) return false;

        var hash = ComputeSha256(libraryPath);
        if (hash is null) return false;

        await UpsertAsync(manifest.Id, old => new PluginTrustInfo
        {
            Enabled = true,
            Hash = hash,
            DisabledDecodeExtensions = disabledDecode is null
                ? Clone(old?.DisabledDecodeExtensions) : Normalize(disabledDecode),
            DisabledEncodeExtensions = disabledEncode is null
                ? Clone(old?.DisabledEncodeExtensions) : Normalize(disabledEncode),
        });
        return true;
    }

    public static async Task<bool> DisableAsync(string pluginId)
    {
        if (Config.IsConfigLocked(ConfigId.PluginTrust)) return false;

        await UpsertAsync(pluginId, old => new PluginTrustInfo
        {
            Enabled = false,
            Hash = old?.Hash ?? string.Empty,
            DisabledDecodeExtensions = Clone(old?.DisabledDecodeExtensions),
            DisabledEncodeExtensions = Clone(old?.DisabledEncodeExtensions),
        });
        return true;
    }

    public static async Task<bool> RemoveAsync(string pluginId)
    {
        if (Config.IsConfigLocked(ConfigId.PluginTrust)) return false;
        if (!Core.Config.PluginTrust.ContainsKey(pluginId)) return true;

        var trust = new Dictionary<string, PluginTrustInfo>(Core.Config.PluginTrust, StringComparer.Ordinal);
        trust.Remove(pluginId);
        Core.Config.PluginTrust = trust;

        await Core.Config.SaveAsync();
        return true;
    }

    private static readonly IReadOnlySet<string> EMPTY_EXCLUSIONS =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static async Task UpsertAsync(string pluginId, Func<PluginTrustInfo?, PluginTrustInfo> mutate)
    {
        Core.Config.PluginTrust.TryGetValue(pluginId, out var existing);

        var trust = new Dictionary<string, PluginTrustInfo>(Core.Config.PluginTrust, StringComparer.Ordinal)
        {
            [pluginId] = mutate(existing),
        };
        Core.Config.PluginTrust = trust;

        await Core.Config.SaveAsync();
    }

    private static HashSet<string>? Normalize(IEnumerable<string>? extensions)
    {
        if (extensions is null) return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in extensions)
        {
            var normalized = NormalizeExtension(ext);
            if (normalized.Length > 1) set.Add(normalized);
        }
        return set.Count == 0 ? null : set;
    }

    private static HashSet<string>? Clone(HashSet<string>? set)
        => set is null || set.Count == 0 ? null : new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
}
