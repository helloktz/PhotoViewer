using PhotoViewer.Common.Types;
using PhotoViewer.Plugins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PhotoViewer.Common.Photoing;

public sealed record CodecInfo(string CodecId, string CodecName, int DecodePriority,
    IReadOnlyList<string> DecodingExtensions, bool IsPlugin, string? PluginId = null)
{

    public int EncodePriority { get; init; }

    public IReadOnlyList<string> EncodingExtensions { get; init; } = [];

    public bool SupportsEncoding { get; init; }

    public bool IsFallback { get; init; }
}

public sealed class CodecRegistry : PhDisposable
{
    private readonly Lock _lock = new();
    private readonly List<ICodec> _codecs = [];
    private readonly List<ICodec> _metadataCodecs = [];
    private readonly List<ICodec> _decodeCodecs = [];

    private readonly ConcurrentDictionary<string, ICodec> _metadataCodecByExt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ICodec> _decodeCodecByExt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ICodec> _encodeCodecByExt = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ICodec> _encodeCodecs = [];

    private readonly HashSet<string> _pluginDecodingExts = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, long> _regSeqById = new(StringComparer.Ordinal);
    private long _regSeq;

    private readonly MagickCodecAdapter _fallbackDecodeCodec;

    public ICodec FallbackDecodeCodec => _fallbackDecodeCodec;

    public CodecRegistry()
    {
        _fallbackDecodeCodec = new MagickCodecAdapter();

        Register(new SvgCodecAdapter());
        Register(new SkiaCodecAdapter());
        Register(_fallbackDecodeCodec);
    }

    public void Register(ICodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        lock (_lock)
        {
            if (_codecs.Exists(c => c.CodecId.Equals(codec.CodecId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Codec '{codec.CodecId}' is already registered.");
            }

            _regSeqById[codec.CodecId] = _regSeq++;

            _codecs.Add(codec);
            _metadataCodecs.Add(codec);
            _decodeCodecs.Add(codec);
            if (codec.SupportsEncoding) _encodeCodecs.Add(codec);

            _metadataCodecs.Sort((left, right) => Compare(right.MetadataPriority, left.MetadataPriority, left, right));
            _decodeCodecs.Sort((left, right) => Compare(right.DecodePriority, left.DecodePriority, left, right));
            _encodeCodecs.Sort((left, right) => Compare(right.EncodePriority, left.EncodePriority, left, right));

            ClearSelectionCaches();
            RebuildPluginDecodingExts();
        }
    }

    private int Compare(int rightPriority, int leftPriority, ICodec left, ICodec right)
    {
        var byPriority = rightPriority.CompareTo(leftPriority);
        if (byPriority != 0) return byPriority;

        var leftSeq = _regSeqById.GetValueOrDefault(left.CodecId, long.MaxValue);
        var rightSeq = _regSeqById.GetValueOrDefault(right.CodecId, long.MaxValue);
        return leftSeq.CompareTo(rightSeq);
    }

    public bool Unregister(ICodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        lock (_lock)
        {
            var removed = _codecs.RemoveAll(c => ReferenceEquals(c, codec)) > 0;
            if (!removed) return false;

            _metadataCodecs.RemoveAll(c => ReferenceEquals(c, codec));
            _decodeCodecs.RemoveAll(c => ReferenceEquals(c, codec));
            _encodeCodecs.RemoveAll(c => ReferenceEquals(c, codec));
            _regSeqById.Remove(codec.CodecId);

            ClearSelectionCaches();
            RebuildPluginDecodingExts();
            return true;
        }
    }

    public ICodec? SelectMetadataCodec(string filePath)
    {
        var ext = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetExtension(filePath);

        lock (_lock)
        {
            return SelectWithCache(_metadataCodecByExt, _metadataCodecs, ext,
                c => c.CanLoadMetadata(filePath), nameof(SelectMetadataCodec));
        }
    }

    public ICodec? SelectDecodeCodec(PhotoMetadata metadata, CodecSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(context);

        var ext = metadata.FileExtension ?? string.Empty;

        lock (_lock)
        {
            return SelectWithCache(_decodeCodecByExt, _decodeCodecs, ext,
                c => c.CanDecode(metadata, context), nameof(SelectDecodeCodec));
        }
    }

    public bool IsDecodingExtensionOwnedByPlugin(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;

        lock (_lock)
        {
            return _pluginDecodingExts.Contains(extension);
        }
    }

    private void RebuildPluginDecodingExts()
    {
        _pluginDecodingExts.Clear();

        foreach (var codec in _decodeCodecs)
        {
            if (!IsPluginCodec(codec)) continue;

            foreach (var ext in codec.DecodingExtensions)
            {
                _pluginDecodingExts.Add(ext);
            }
        }
    }

    public void InvalidateSelectionCaches()
    {
        lock (_lock)
        {
            ClearSelectionCaches();
        }
    }

    private void ClearSelectionCaches()
    {
        _metadataCodecByExt.Clear();
        _decodeCodecByExt.Clear();
        _encodeCodecByExt.Clear();
    }

    public ICodec? SelectEncodeCodec(string destFilePath, CodecEncodeContext? context = null)
    {
        var ext = string.IsNullOrEmpty(destFilePath) ? string.Empty : Path.GetExtension(destFilePath);
        var ctx = context ?? CodecEncodeContext.Default;

        lock (_lock)
        {
            return SelectWithCache(_encodeCodecByExt, _encodeCodecs, ext,
                c => c.CanEncode(destFilePath, ctx), nameof(SelectEncodeCodec));
        }
    }

    public ICodec? SelectMultiFrameEncodeCodec(string destFilePath, CodecEncodeContext? context = null)
    {
        var ctx = context ?? CodecEncodeContext.Default;

        lock (_lock)
        {
            return SelectFirst(_encodeCodecs, c => c.CanEncodeMultiFrame(destFilePath, ctx),
                nameof(SelectMultiFrameEncodeCodec));
        }
    }

    public CodecInfo? GetEncodeCodecInfo(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;

        var probePath = "_" + (extension.StartsWith('.') ? extension : "." + extension);
        var codec = SelectEncodeCodec(probePath);

        return codec is null ? null : ToCodecInfo(codec);
    }

    public IReadOnlyList<(string Ext, string CodecName, int EncodePriority, bool IsPlugin)> GetEncodingExtensions()
    {
        lock (_lock)
        {
            var list = new List<(string, string, int, bool)>();
            foreach (var codec in _encodeCodecs)
            {
                foreach (var ext in codec.EncodingExtensions)
                {
                    list.Add((ext, codec.CodecName, codec.EncodePriority, !IsBuiltIn(codec)));
                }
            }
            return list;
        }
    }

    public IReadOnlyList<CodecInfo> GetCodecInfos()
    {
        lock (_lock)
        {
            var list = new List<CodecInfo>(_decodeCodecs.Count);
            foreach (var c in _decodeCodecs)
            {
                list.Add(ToCodecInfo(c));
            }
            return list;
        }
    }

    protected override void OnDisposing()
    {
        lock (_lock)
        {
            foreach (var codec in _codecs)
            {
                codec.Dispose();
            }

            _codecs.Clear();
            _metadataCodecs.Clear();
            _decodeCodecs.Clear();
            _encodeCodecs.Clear();
            _regSeqById.Clear();
            ClearSelectionCaches();
        }

        base.OnDisposing();
    }

    private static bool IsBuiltIn(ICodec codec)
        => codec is SvgCodecAdapter or SkiaCodecAdapter or MagickCodecAdapter;

    public static bool IsPluginCodec(ICodec? codec)
        => codec is not null && !IsBuiltIn(codec);

    private CodecInfo ToCodecInfo(ICodec codec)
    {
        return new CodecInfo(codec.CodecId, codec.CodecName, codec.DecodePriority,
            codec.DecodingExtensions, !IsBuiltIn(codec), (codec as NativeCodecProxy)?.Plugin.PluginId)
        {
            EncodePriority = codec.EncodePriority,
            EncodingExtensions = codec.EncodingExtensions,
            SupportsEncoding = codec.SupportsEncoding,
            IsFallback = ReferenceEquals(codec, _fallbackDecodeCodec),
        };
    }

    private static ICodec? SelectFirst(List<ICodec> orderedCodecs, Func<ICodec, bool> predicate, string opName)
    {
        foreach (var codec in orderedCodecs)
        {
            try { if (predicate(codec)) return codec; }
            catch (Exception ex) { Debug.WriteLine($"❌❌❌ {opName} ({codec.CodecId}): {ex.Message}"); }
        }
        return null;
    }

    private static ICodec? SelectWithCache(
        ConcurrentDictionary<string, ICodec> cache,
        List<ICodec> orderedCodecs,
        string ext,
        Func<ICodec, bool> predicate,
        string opName)
    {
        if (!string.IsNullOrEmpty(ext) && cache.TryGetValue(ext, out var cached))
        {
            try
            {
                if (predicate(cached)) return cached;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ {opName} ({cached.CodecId}) [cached]: {ex.Message}");
            }
        }

        var codec = SelectFirst(orderedCodecs, predicate, opName);
        if (codec != null && !string.IsNullOrEmpty(ext))
        {
            cache[ext] = codec;
        }

        return codec;
    }

}
