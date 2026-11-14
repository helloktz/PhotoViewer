using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using ImageGlass.SDK.Plugins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PhotoViewer.Plugins;

public sealed unsafe class PluginRegistry : PhDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, NativePlugin> _plugins = new(StringComparer.Ordinal);

    private static int DecodeMajor(int abiVersion) => abiVersion / 1_000_000;

    internal const string TRASH_DIR_NAME = "_trash";

    private static readonly int MIN_CAPABILITY_SIZE = MeasureCapabilityDecodeFields();
    private static readonly int CAPABILITY_ENCODE_FIELDS_END = MeasureCapabilityEncodeFields();

    private static int MeasureCapabilityDecodeFields()
    {
        IGCodecCapability probe = default;
        return (int)((byte*)&probe.DecodeExtensions - (byte*)&probe) + sizeof(nint);
    }

    private static int MeasureCapabilityEncodeFields()
    {
        IGCodecCapability probe = default;
        return (int)((byte*)&probe.EncodeExtensions - (byte*)&probe) + sizeof(nint);
    }

    internal static bool TryResolvePluginLibraryPath(string executable, string pluginDir, out string libraryPath)
    {
        libraryPath = string.Empty;

        if (Path.IsPathRooted(executable)) return false;
        if (executable.Contains('/') || executable.Contains('\\')) return false;
        if (executable.Contains("..")) return false;

        var expectedExt = OperatingSystem.IsWindows() ? ".dll"
            : OperatingSystem.IsMacOS() ? ".dylib"
            : ".so";
        if (!Path.GetExtension(executable).Equals(expectedExt, StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = Path.Combine(pluginDir, executable);

        if (!BHelper.IsPathContainedIn(candidate, pluginDir)) return false;

        libraryPath = candidate;
        return true;
    }

    public PluginFailureManager FailureManager { get; } = new();

    private readonly ConcurrentDictionary<string, string> _loadErrors = new(StringComparer.Ordinal);

    public string? GetLoadError(string pluginId) => _loadErrors.GetValueOrDefault(pluginId);

    private NativePlugin? Fail(string pluginId, string reason)
    {
        _loadErrors[pluginId] = reason;
        Debug.WriteLine($"[PluginRegistry] '{pluginId}': {reason}");
        return null;
    }

    internal NativePlugin? LoadAndProbe(PluginManifest manifest, string pluginDir)
    {
        if (manifest.Kind != IGPluginKind.Codec)
        {
            return Fail(manifest.Id, $"unsupported plugin kind '{manifest.Kind}'.");
        }
        if (string.IsNullOrEmpty(manifest.Executable))
        {
            return Fail(manifest.Id, "the manifest has no 'executable'.");
        }

        if (FailureManager.IsQuarantined(manifest.Id))
        {
            var quarantineReason = FailureManager.GetQuarantineReason(manifest.Id);
            return Fail(manifest.Id, "quarantined after an earlier failure"
                + (quarantineReason is null ? "." : $": {quarantineReason}"));
        }

        if (!TryResolvePluginLibraryPath(manifest.Executable, pluginDir, out var libraryPath))
        {
            return Fail(manifest.Id, $"unsafe or invalid executable '{manifest.Executable}': it must be a "
                + "plain file name inside the plugin folder, with the platform's native-library extension.");
        }
        if (!File.Exists(libraryPath))
        {
            return Fail(manifest.Id, $"library not found: {libraryPath}");
        }

        if (FailureManager.HasLoadingBreadcrumb(manifest.Id))
        {
            FailureManager.ClearLoadingBreadcrumb(manifest.Id);
            FailureManager.Quarantine(manifest.Id, "hard crash during a previous load");
            return Fail(manifest.Id, "it hard-crashed the app during a previous load; now quarantined.");
        }

        if (!PluginTrustPolicy.IsTrusted(manifest.Id, libraryPath))
        {
            return Fail(manifest.Id, "not enabled, or the library no longer matches the approved file.");
        }

        nint libHandle = 0;
        IGPluginApi* pluginApi = null;

        FailureManager.SetLoadingBreadcrumb(manifest.Id);
        try
        {
            libHandle = NativeLibrary.Load(libraryPath);

            if (!NativeLibrary.TryGetExport(libHandle, IGNativeAbi.ENTRY_POINT_NAME, out var entryAddr))
            {
                NativeLibrary.Free(libHandle);
                return Fail(manifest.Id, $"'{Path.GetFileName(libraryPath)}' exports no "
                    + $"'{IGNativeAbi.ENTRY_POINT_NAME}'. A managed build output is not a plugin library: "
                    + "publish the project with PublishAot + NativeLib=Shared.");
            }
            var entry = (delegate* unmanaged[Cdecl]<int, IGHostApi*, IGPluginApi*>)entryAddr;

            var hostApi = PluginHostApiTable.Get();
            try
            {
                pluginApi = entry(IGNativeAbi.IG_PLUGIN_ABI_VERSION, hostApi);
            }
            catch (Exception ex)
            {
                FailureManager.Quarantine(manifest.Id, $"entry point threw: {ex.Message}");
                NativeLibrary.Free(libHandle);
                return Fail(manifest.Id, $"'{IGNativeAbi.ENTRY_POINT_NAME}' threw "
                    + $"{ex.GetType().Name}: {ex.Message}");
            }
            if (pluginApi == null)
            {
                NativeLibrary.Free(libHandle);
                return Fail(manifest.Id, $"'{IGNativeAbi.ENTRY_POINT_NAME}' returned null: the plugin "
                    + $"refused host ABI {IGNativeAbi.IG_PLUGIN_ABI_VERSION}.");
            }

            if (pluginApi->StructSize != sizeof(IGPluginApi))
            {
                NativeLibrary.Free(libHandle);
                return Fail(manifest.Id, $"IGPluginApi.StructSize is {pluginApi->StructSize}, expected "
                    + $"{sizeof(IGPluginApi)}: rebuild the plugin against the current ImageGlass SDK.");
            }
            if (DecodeMajor(pluginApi->AbiVersion) != IGNativeAbi.IG_PLUGIN_ABI_MAJOR)
            {
                NativeLibrary.Free(libHandle);
                return Fail(manifest.Id, $"ABI major mismatch: plugin {pluginApi->AbiVersion}, "
                    + $"host {IGNativeAbi.IG_PLUGIN_ABI_VERSION}.");
            }

            if (pluginApi->Initialize != null)
            {
                IGStatus initStatus;
                try { initStatus = pluginApi->Initialize(); }
                catch (Exception ex)
                {
                    FailureManager.Quarantine(manifest.Id, $"Initialize threw: {ex.Message}");
                    NativeLibrary.Free(libHandle);
                    return Fail(manifest.Id, $"Initialize threw {ex.GetType().Name}: {ex.Message}");
                }
                if (initStatus != IGStatus.OK)
                {
                    NativeLibrary.Free(libHandle);
                    return Fail(manifest.Id, $"Initialize returned {initStatus}.");
                }
            }

            var handle = new NativePlugin(manifest.Id, libraryPath, libHandle, pluginApi);

            var codecCount = pluginApi->Info.CodecCount;
            if (codecCount < 0 || codecCount > 1024)
            {
                FailureManager.Quarantine(manifest.Id, $"invalid CodecCount={codecCount} (likely IGPluginInfo struct layout mismatch).");
                NativeLibrary.Free(libHandle);
                return Fail(manifest.Id, $"reported CodecCount={codecCount}: IGPluginInfo struct layout "
                    + "mismatch; rebuild the plugin against the current ImageGlass SDK.");
            }
            var capabilities = new List<CodecPluginCapability>(codecCount);

            var codecErrors = new List<string>();
            for (var i = 0; i < codecCount; i++)
            {
                IGCodecApi* codecApi = null;
                IGStatus status;
                try { status = pluginApi->GetCodec(i, &codecApi); }
                catch (Exception ex)
                {
                    codecErrors.Add($"codec[{i}]: GetCodec threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (status != IGStatus.OK || codecApi == null)
                {
                    codecErrors.Add($"codec[{i}]: GetCodec returned {status}"
                        + (codecApi == null ? " with a null table." : "."));
                    continue;
                }

                if (codecApi->StructSize < PluginAbi.MinCodecApiSize)
                {
                    codecErrors.Add($"codec[{i}]: IGCodecApi.StructSize is {codecApi->StructSize}, expected "
                        + $"at least {PluginAbi.MinCodecApiSize}; rebuild against the current SDK.");
                    continue;
                }
                if (!PluginAbi.HasEntryPoint(codecApi, &codecApi->GetCapability))
                {
                    codecErrors.Add($"codec[{i}]: GetCapability is null.");
                    continue;
                }

                IGCodecCapability* cap = null;
                try { status = codecApi->GetCapability(&cap); }
                catch (Exception ex)
                {
                    codecErrors.Add($"codec[{i}]: GetCapability threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (status != IGStatus.OK || cap == null)
                {
                    codecErrors.Add($"codec[{i}]: GetCapability returned {status}"
                        + (cap == null ? " with a null capability." : "."));
                    continue;
                }

                if (cap->StructSize < MIN_CAPABILITY_SIZE)
                {
                    codecErrors.Add($"codec[{i}]: IGCodecCapability.StructSize is {cap->StructSize}, expected "
                        + $"at least {MIN_CAPABILITY_SIZE}; rebuild against the current SDK.");
                    continue;
                }

                var managed = MarshalCapability(codecApi, cap);
                if (!HasAnyCapability(managed))
                {
                    codecErrors.Add($"codec[{i}]: advertises no usable capability (check the capability "
                        + "flags against their required entry points).");
                    continue;
                }

                capabilities.Add(managed);
                handle.Codecs.Add(new NativeCodecEntry((nint)codecApi, managed));
            }

            if (handle.Codecs.Count == 0)
            {
                handle.Dispose();
                return Fail(manifest.Id, "no usable codec. " + (codecErrors.Count > 0
                    ? string.Join(" ", codecErrors)
                    : $"The plugin reported CodecCount={codecCount}."));
            }

            _loadErrors.TryRemove(manifest.Id, out _);

            lock (_lock)
            {
                if (_plugins.TryGetValue(manifest.Id, out var existing))
                {
                    existing.Dispose();
                }
                _plugins[manifest.Id] = handle;
            }

            return handle;
        }
        catch (Exception ex)
        {
            FailureManager.Quarantine(manifest.Id, $"load failed: {ex.Message}");

            try
            {
                if (libHandle != 0)
                {
                    NativeLibrary.Free(libHandle);
                }
            }
            catch { }

            return Fail(manifest.Id, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            FailureManager.ClearLoadingBreadcrumb(manifest.Id);
        }
    }

    public bool IsLoaded(string pluginId)
    {
        lock (_lock)
        {
            return _plugins.ContainsKey(pluginId);
        }
    }

    internal NativePlugin? GetLoadedPlugin(string pluginId)
    {
        lock (_lock)
        {
            return _plugins.GetValueOrDefault(pluginId);
        }
    }

    public bool UnloadPlugin(string pluginId)
    {
        NativePlugin? handle;
        lock (_lock)
        {
            if (!_plugins.Remove(pluginId, out handle)) return false;
        }

        try { handle.Dispose(); } catch { }
        return true;
    }

    internal IEnumerable<NativeCodecProxy> CreateProxies(NativePlugin handle)
    {
        var list = new List<NativeCodecProxy>(handle.Codecs.Count);
        foreach (var entry in handle.Codecs)
        {
            unsafe
            {
                list.Add(new NativeCodecProxy(handle, (IGCodecApi*)entry.CodecApiPtr, entry.Capability, FailureManager));
            }
        }
        return list;
    }

    private static CodecPluginCapability MarshalCapability(IGCodecApi* codecApi, IGCodecCapability* cap)
    {
        var readable = Math.Min(cap->StructSize, sizeof(IGCodecCapability));

        var hasEncodeFields = readable >= CAPABILITY_ENCODE_FIELDS_END;

        var decodeExts = MarshalExtensions(cap->DecodeExtensionCount, cap->DecodeExtensions, "DecodeExtensions");
        var encodeExts = hasEncodeFields
            ? MarshalExtensions(cap->EncodeExtensionCount, cap->EncodeExtensions, "EncodeExtensions")
            : [];

        var animDecode = cap->SupportsAnimationDecoding != 0
            && PluginAbi.HasEntryPoint(codecApi, &codecApi->GetAnimationInfo)
            && PluginAbi.HasEntryPoint(codecApi, &codecApi->FreeAnimationInfo)
            && PluginAbi.HasEntryPoint(codecApi, &codecApi->DecodeAnimationFrame);

        var staticEncode = hasEncodeFields
            && cap->SupportsStaticRasterEncoding != 0
            && PluginAbi.HasEntryPoint(codecApi, &codecApi->EncodeStaticRaster)
            && encodeExts.Length > 0;

        var multiEncode = staticEncode
            && cap->SupportsMultiFrameEncoding != 0
            && PluginAbi.HasEntryPoint(codecApi, &codecApi->BeginEncodeMultiFrame)
            && PluginAbi.HasEntryPoint(codecApi, &codecApi->EncodeFrame)
            && PluginAbi.HasEntryPoint(codecApi, &codecApi->EndEncodeMultiFrame);

        return new CodecPluginCapability
        {
            CodecId = cap->CodecId.ToManaged(),
            CodecName = cap->CodecName.ToManaged(),
            MetadataPriority = cap->MetadataPriority,
            DecodePriority = cap->DecodePriority,
            EncodePriority = hasEncodeFields ? cap->EncodePriority : 0,
            DecodingExtensions = decodeExts,
            EncodingExtensions = encodeExts,
            SupportsMetadata = cap->SupportsMetadata != 0
                && PluginAbi.HasEntryPoint(codecApi, &codecApi->LoadMetadata),
            SupportsColorProfiles = cap->SupportsColorProfiles != 0,
            SupportsStaticRasterDecoding = cap->SupportsStaticRasterDecoding != 0
                && PluginAbi.HasEntryPoint(codecApi, &codecApi->DecodeStaticRaster)
                && PluginAbi.HasEntryPoint(codecApi, &codecApi->FreePixelBuffer),
            SupportsAnimationDecoding = animDecode,
            SupportsStaticRasterEncoding = staticEncode,
            SupportsMultiFrameEncoding = multiEncode,
        };
    }

    private static string[] MarshalExtensions(int count, IGStringRef* extensions, string what)
    {
        if (count < 0)
        {
            Debug.WriteLine($"[PluginRegistry] capability reports negative count for {what} ({count}); clamping to 0.");
            count = 0;
        }
        if (extensions == null || count == 0) return [];

        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(extensions[i].ToManaged());
        }
        return [.. list];
    }

    private static bool HasAnyCapability(CodecPluginCapability cap)
        => cap.SupportsMetadata
        || cap.SupportsStaticRasterDecoding
        || cap.SupportsAnimationDecoding
        || cap.SupportsStaticRasterEncoding;

    protected override void OnDisposing()
    {
        lock (_lock)
        {
            foreach (var handle in _plugins.Values)
            {
                try { handle.Dispose(); } catch { }
            }
            _plugins.Clear();
        }
        base.OnDisposing();
    }

    public (string[] Decode, string[] Encode) GetDeclaredCodecExtensions(string pluginId)
    {
        var handle = GetLoadedPlugin(pluginId);
        if (handle is null) return ([], []);

        var decode = new List<string>();
        var encode = new List<string>();
        var decodeSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var encodeSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in handle.Codecs)
        {
            Collect(entry.Capability.DecodingExtensions, decodeSeen, decode);
            Collect(entry.Capability.EncodingExtensions, encodeSeen, encode);
        }

        return ([.. decode], [.. encode]);

        static void Collect(string[] source, HashSet<string> seen, List<string> target)
        {
            foreach (var ext in source)
            {
                var normalized = PluginTrustPolicy.NormalizeExtension(ext);
                if (normalized.Length > 1 && seen.Add(normalized)) target.Add(normalized);
            }
        }
    }

    public static List<(PluginManifest Manifest, string PluginDir)> DiscoverManifests(string pluginsDirectory)
    {
        var results = new List<(PluginManifest, string)>();
        if (!Directory.Exists(pluginsDirectory)) return results;

        foreach (var dir in Directory.EnumerateDirectories(pluginsDirectory))
        {
            if (string.Equals(Path.GetFileName(dir), TRASH_DIR_NAME, StringComparison.Ordinal)) continue;

            var manifestPath = Path.Combine(dir, PluginManifest.FILE_NAME);
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = System.Text.Json.JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginManifest);
                if (manifest is not null && !string.IsNullOrEmpty(manifest.Id))
                {
                    results.Add((manifest, dir));
                }
            }
            catch
            {
            }
        }

        return results;
    }

    public static void CleanupTrashDirs(string pluginsDir)
    {
        var trashRoot = Path.Combine(pluginsDir, TRASH_DIR_NAME);
        if (!Directory.Exists(trashRoot)) return;

        foreach (var dir in Directory.GetDirectories(trashRoot))
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        try { Directory.Delete(trashRoot, recursive: false); } catch { }
    }

}
