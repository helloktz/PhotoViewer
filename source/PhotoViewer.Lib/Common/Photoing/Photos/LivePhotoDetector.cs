using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public record LivePhotoInfo(bool IsLivePhoto, long EmbeddedVideoOffsetFromEnd);

public static class LivePhotoDetector
{

    private const int HEAD_READ_SIZE_JPEG = 65536;

    private const int HEAD_READ_SIZE_HEIF = 2 * 1024 * 1024;

    private const int TAIL_READ_SIZE = 65536;

    private const int SCAN_CHUNK_SIZE = 256 * 1024;

    private static readonly string[] _heifExtensions =
        [".heif", ".heic", ".hif", ".avif"];

    private static ReadOnlySpan<byte> FtypSignature => "ftyp"u8;

    private static ReadOnlySpan<byte> SamsungMotionPhotoDataMarker => "MotionPhoto_Data"u8;

    private static ReadOnlySpan<byte> SamsungMpv2Tag => "mpv2"u8;

    private static readonly List<string> _tempFiles = [];
    private static readonly Lock _tempFilesLock = new();
    private static bool _exitHandlerRegistered;

    public static LivePhotoInfo Detect(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return new(false, 0);

        try
        {
            var fileLength = new FileInfo(filePath).Length;
            if (fileLength < 100) return new(false, 0);

            var xmpResult = DetectFromXmp(filePath, fileLength);
            if (xmpResult.IsLivePhoto)
            {
                if (ValidateFtypAtOffset(filePath, fileLength, xmpResult.EmbeddedVideoOffsetFromEnd))
                {
                    return xmpResult;
                }
            }

            var tailResult = DetectFromTail(filePath, fileLength);
            if (tailResult.IsLivePhoto) return tailResult;

            var samsungResult = DetectFromSamsungMarkerScan(filePath, fileLength);
            return samsungResult;
        }
        catch { }

        return new(false, 0);
    }

    public static async Task<string?> ExtractEmbeddedVideoAsync(
        string imagePath, long offsetFromEnd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || offsetFromEnd <= 0)
            return null;

        try
        {
            var fileLength = new FileInfo(imagePath).Length;
            var videoStart = fileLength - offsetFromEnd;
            if (videoStart < 0) return null;

            using var fs = new FileStream(imagePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            fs.Seek(videoStart, SeekOrigin.Begin);

            var header = new byte[8];
            var bytesRead = await fs.ReadAsync(header.AsMemory(0, 8), ct).ConfigureAwait(false);
            if (bytesRead < 8) return null;

            if (!header.AsSpan(4, 4).SequenceEqual(FtypSignature))
                return null;

            fs.Seek(videoStart, SeekOrigin.Begin);
            var videoLength = (int)offsetFromEnd;
            var tempPath = BHelper.ConfigDir(Dir.Temporary, $"ig_livephoto_{Guid.NewGuid():N}.mp4");

            using (var outFs = new FileStream(tempPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 8192,
                FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                var remaining = videoLength;

                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var toRead = Math.Min(buffer.Length, remaining);
                    bytesRead = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
                    if (bytesRead == 0) break;

                    await outFs.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    remaining -= bytesRead;
                }
            }

            RegisterTempFile(tempPath);
            return BHelper.GetRealPlatformPath(tempPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return null;
    }

    public static void CleanupTempFiles()
    {
        string[] files;
        lock (_tempFilesLock)
        {
            files = [.. _tempFiles];
            _tempFiles.Clear();
        }

        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch { }
        }
    }

    private static LivePhotoInfo DetectFromXmp(string filePath, long fileLength)
    {
        var ext = Path.GetExtension(filePath);
        var maxHead = _heifExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)
            ? HEAD_READ_SIZE_HEIF
            : HEAD_READ_SIZE_JPEG;
        var readSize = (int)Math.Min(maxHead, fileLength);
        var headBuffer = new byte[readSize];

        using (var fs = new FileStream(filePath, FileMode.Open,
            FileAccess.Read, FileShare.Read,
            Math.Min(readSize, 81920), FileOptions.SequentialScan))
        {
            _ = fs.Read(headBuffer);
        }

        var headText = Encoding.UTF8.GetString(headBuffer);

        var hasMotionPhotoMarker =
            headText.Contains("MotionPhoto=\"1\"", StringComparison.Ordinal)
            || headText.Contains("MotionPhoto='1'", StringComparison.Ordinal)
            || headText.Contains("MicroVideo=\"1\"", StringComparison.Ordinal)
            || headText.Contains("MicroVideo='1'", StringComparison.Ordinal);

        if (hasMotionPhotoMarker)
        {
            var offset = ParseVideoOffset(headText, fileLength);
            if (offset > 0)
            {
                return new LivePhotoInfo(true, offset);
            }

            var tailResult = DetectFromTail(filePath, fileLength);
            if (tailResult.IsLivePhoto) return tailResult;

            return new(false, 0);
        }

        return new(false, 0);
    }

    private static bool ValidateFtypAtOffset(string filePath, long fileLength, long offsetFromEnd)
    {
        var videoStart = fileLength - offsetFromEnd;
        if (videoStart < 0 || videoStart + 8 > fileLength) return false;

        var header = new byte[8];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192);
        fs.Seek(videoStart, SeekOrigin.Begin);

        if (fs.Read(header) < 8) return false;

        return header.AsSpan(4, 4).SequenceEqual(FtypSignature);
    }

    private static long ParseVideoOffset(string xmpText, long fileLength)
    {
        var offset = ExtractNumericAttribute(xmpText, "MotionPhotoVideoOffset=\"");
        if (offset <= 0)
        {
            offset = ExtractNumericAttribute(xmpText, "MicroVideoOffset=\"");
        }

        if (offset > 0 && offset < fileLength)
        {
            return offset;
        }

        offset = ParseVideoOffsetFromContainerDirectory(xmpText);
        if (offset > 0 && offset < fileLength)
        {
            return offset;
        }

        return 0;
    }

    private static long ParseVideoOffsetFromContainerDirectory(string xmpText)
    {
        const string semanticMarker = "Item:Semantic=\"MotionPhoto\"";
        var semanticIdx = xmpText.IndexOf(semanticMarker, StringComparison.Ordinal);
        if (semanticIdx < 0) return 0;

        var elemStart = xmpText.LastIndexOf('<', semanticIdx);
        if (elemStart < 0) elemStart = Math.Max(0, semanticIdx - 500);

        var elemEnd = xmpText.IndexOf("/>", semanticIdx, StringComparison.Ordinal);
        if (elemEnd < 0) elemEnd = xmpText.IndexOf('>', semanticIdx);
        if (elemEnd < 0) elemEnd = Math.Min(xmpText.Length, semanticIdx + 500);
        else elemEnd += 2;

        var element = xmpText.AsSpan(elemStart, elemEnd - elemStart);

        const string lengthPrefix = "Item:Length=\"";
        var lengthIdx = element.IndexOf(lengthPrefix.AsSpan(), StringComparison.Ordinal);
        if (lengthIdx < 0) return 0;

        var valueStart = lengthIdx + lengthPrefix.Length;
        var remaining = element[valueStart..];
        var quoteIdx = remaining.IndexOf('"');
        if (quoteIdx <= 0) return 0;

        var valueSpan = remaining[..quoteIdx];
        if (long.TryParse(valueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            && length > 0)
        {
            return length;
        }

        return 0;
    }

    private static long ExtractNumericAttribute(string text, string prefix)
    {
        var idx = text.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return 0;

        idx += prefix.Length;
        var endIdx = text.IndexOf('"', idx);
        if (endIdx < 0 || endIdx == idx) return 0;

        var valueSpan = text.AsSpan(idx, endIdx - idx);
        if (long.TryParse(valueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return 0;
    }

    private static LivePhotoInfo DetectFromTail(string filePath, long fileLength)
    {
        var readSize = (int)Math.Min(TAIL_READ_SIZE, fileLength);
        var tailBuffer = new byte[readSize];
        var tailStart = fileLength - readSize;

        using (var fs = new FileStream(filePath, FileMode.Open,
            FileAccess.Read, FileShare.Read, readSize, FileOptions.SequentialScan))
        {
            fs.Seek(tailStart, SeekOrigin.Begin);
            _ = fs.Read(tailBuffer.AsSpan());
        }

        var samsungIdx = tailBuffer.AsSpan().IndexOf(SamsungMotionPhotoDataMarker);
        if (samsungIdx >= 0)
        {
            var result = TryParseSamsungMotionPhotoData(tailBuffer, samsungIdx, tailStart, fileLength);
            if (result.IsLivePhoto) return result;
        }

        var ftypIdx = FindFtypBox(tailBuffer);
        if (ftypIdx >= 0)
        {
            var boxStart = ftypIdx - 4;
            if (boxStart >= 0)
            {
                var offsetFromEnd = readSize - boxStart;
                if (offsetFromEnd > 0 && offsetFromEnd < fileLength)
                {
                    return new LivePhotoInfo(true, offsetFromEnd);
                }
            }
        }

        return new(false, 0);
    }

    private static int FindFtypBox(ReadOnlySpan<byte> buffer)
    {
        var idx = 0;
        while (idx + 4 <= buffer.Length)
        {
            var pos = buffer[idx..].IndexOf(FtypSignature);
            if (pos < 0) return -1;

            var absPos = idx + pos;

            if (absPos >= 4)
            {
                var boxSize = (buffer[absPos - 4] << 24)
                    | (buffer[absPos - 3] << 16)
                    | (buffer[absPos - 2] << 8)
                    | buffer[absPos - 1];

                if (boxSize >= 8 && boxSize < 1024)
                {
                    return absPos;
                }
            }

            idx = absPos + 1;
        }

        return -1;
    }

    private static LivePhotoInfo DetectFromSamsungMarkerScan(string filePath, long fileLength)
    {
        if (fileLength > 50 * 1024 * 1024) return new(false, 0);

        const int overlap = 32;
        var buffer = new byte[SCAN_CHUNK_SIZE + overlap];

        using var fs = new FileStream(filePath, FileMode.Open,
            FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);

        var scanEnd = fileLength - TAIL_READ_SIZE;
        if (scanEnd < 0) return new(false, 0);

        var pos = scanEnd;

        while (pos > 0)
        {
            var chunkStart = Math.Max(0, pos - SCAN_CHUNK_SIZE);
            var chunkSize = (int)(pos + overlap - chunkStart);
            chunkSize = (int)Math.Min(chunkSize, fileLength - chunkStart);

            fs.Seek(chunkStart, SeekOrigin.Begin);
            var bytesRead = fs.Read(buffer, 0, chunkSize);
            if (bytesRead == 0) break;

            var span = buffer.AsSpan(0, bytesRead);
            var markerIdx = span.IndexOf(SamsungMotionPhotoDataMarker);

            if (markerIdx >= 0)
            {
                var result = TryParseSamsungMotionPhotoData(
                    buffer.AsSpan(0, bytesRead), markerIdx, chunkStart, fileLength);
                if (result.IsLivePhoto) return result;
            }

            pos = chunkStart;
            if (chunkStart == 0) break;
        }

        return new(false, 0);
    }

    private static LivePhotoInfo TryParseSamsungMotionPhotoData(
        ReadOnlySpan<byte> buffer, int markerIdx, long bufferStartInFile, long fileLength)
    {
        var afterMarker = markerIdx + SamsungMotionPhotoDataMarker.Length;
        if (afterMarker >= buffer.Length) return new(false, 0);

        if (afterMarker + 32 <= buffer.Length)
        {
            var searchWindow = buffer.Slice(afterMarker, Math.Min(32, buffer.Length - afterMarker));
            var ftypPos = searchWindow.IndexOf(FtypSignature);

            if (ftypPos >= 0 && ftypPos >= 4)
            {
                var videoStartInFile = bufferStartInFile + afterMarker + ftypPos - 4;
                var offsetFromEnd = fileLength - videoStartInFile;

                if (offsetFromEnd > 0)
                {
                    return new LivePhotoInfo(true, offsetFromEnd);
                }
            }
        }

        if (afterMarker + 12 <= buffer.Length
            && buffer.Slice(afterMarker, 4).SequenceEqual(SamsungMpv2Tag))
        {
            var videoStartFromBof =
                ((long)buffer[afterMarker + 4] << 24)
                | ((long)buffer[afterMarker + 5] << 16)
                | ((long)buffer[afterMarker + 6] << 8)
                | buffer[afterMarker + 7];

            var videoLength =
                ((long)buffer[afterMarker + 8] << 24)
                | ((long)buffer[afterMarker + 9] << 16)
                | ((long)buffer[afterMarker + 10] << 8)
                | buffer[afterMarker + 11];

            if (videoStartFromBof > 0 && videoLength > 0
                && videoStartFromBof + videoLength <= fileLength)
            {
                var offsetFromEnd = fileLength - videoStartFromBof;
                return new LivePhotoInfo(true, offsetFromEnd);
            }
        }

        return new(false, 0);
    }

    private static void RegisterTempFile(string path)
    {
        lock (_tempFilesLock)
        {
            _tempFiles.Add(path);

            if (!_exitHandlerRegistered)
            {
                _exitHandlerRegistered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupTempFiles();
            }
        }
    }

}
