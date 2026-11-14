using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public sealed class CodecEncodeContext
{

    public static readonly CodecEncodeContext Default = new();
}

public sealed record CodecEncodeResult(bool Succeeded, bool Unsupported, string? Error = null);

public sealed record CodecEncodeFrame(SKImage Image, int DurationMs, bool OwnsImage);

public sealed record CodecEncodeRequest
{

    public required string DestFilePath { get; init; }

    public required SKImage Source { get; init; }

    public uint Quality { get; init; } = 100;

    public string? SourceFilePath { get; init; }

    public byte[]? SourceIccProfile { get; init; }
}

public sealed record CodecMultiFrameEncodeRequest
{

    public required string DestFilePath { get; init; }

    public required int FrameCount { get; init; }

    public required Func<int, CancellationToken, Task<CodecEncodeFrame?>> GetFrameAsync { get; init; }

    public bool IsAnimated { get; init; }

    public int LoopCount { get; init; }

    public uint Quality { get; init; } = 100;

    public string? SourceFilePath { get; init; }

    public byte[]? SourceIccProfile { get; init; }
}
