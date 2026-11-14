using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

internal static class SaveStaging
{
    private const string TEMP_PREFIX = ".ig-save-";

    private static readonly TimeSpan STALE_AGE = TimeSpan.FromMinutes(10);

    private const int PROMOTE_RETRIES = 4;
    private const int PROMOTE_RETRY_DELAY_MS = 60;

    public static bool IsStagingFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        return Path.GetFileName(filePath.AsSpan()).StartsWith(TEMP_PREFIX, StringComparison.Ordinal);
    }

    public static string BuildTempPath(string destFilePath)
    {
        var dir = Path.GetDirectoryName(destFilePath);
        var ext = Path.GetExtension(destFilePath);
        var name = TEMP_PREFIX + Guid.NewGuid().ToString("N") + ext;

        return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
    }

    public static async Task WriteThenPromoteAsync(string destFilePath,
        Func<string, Task> writeAsync, CancellationToken token = default)
    {
        var tempPath = BuildTempPath(destFilePath);
        SweepStaleTempFiles(destFilePath);

        try
        {
            await writeAsync(tempPath).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            if (!HasContent(tempPath))
            {
                throw new IOException($"IGE: nothing was written for '{destFilePath}'.");
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        var (promoted, promoteError) = await PromoteAsync(tempPath, destFilePath, token).ConfigureAwait(false);
        if (!promoted)
        {
            throw new IOException($"IGE: could not finalize '{destFilePath}'. {promoteError}".TrimEnd());
        }
    }

    public static async Task<(bool Ok, string Error)> PromoteAsync(string tempPath, string destFilePath,
        CancellationToken token = default)
    {
        var errors = new List<string>(3);

        for (var attempt = 0; attempt <= PROMOTE_RETRIES; attempt++)
        {
            try
            {
                await Task.Run(() => File.Move(tempPath, destFilePath, overwrite: true), token).ConfigureAwait(false);
                return (true, string.Empty);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (attempt == PROMOTE_RETRIES) errors.Add($"replace: {Describe(ex)}");
                else await Task.Delay(PROMOTE_RETRY_DELAY_MS, token).ConfigureAwait(false);
            }
        }

        try
        {
            await Task.Run(() =>
            {
                File.Delete(destFilePath);
                File.Move(tempPath, destFilePath);
            }, token).ConfigureAwait(false);

            return (true, string.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errors.Add($"unlink+rename: {Describe(ex)}");
        }

        try
        {
            await Task.Run(() => File.Copy(tempPath, destFilePath, overwrite: true), token).ConfigureAwait(false);
            TryDelete(tempPath);

            return (true, string.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errors.Add($"overwrite: {Describe(ex)}");
        }

        errors.Add($"the finished image is at '{tempPath}'");

        return (false, string.Join("; ", errors));
    }

    private static string Describe(Exception ex)
    {
        var code = Marshal.GetHRForException(ex) & 0xFFFF;

        return $"{ex.GetType().Name} (Win32 {code}): {ex.Message}";
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Debug.WriteLine($"[SaveStaging] temp delete failed: {ex.Message}"); }
    }

    public static void SweepStaleTempFiles(string destFilePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(destFilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var cutoff = DateTime.UtcNow - STALE_AGE;
            foreach (var stale in Directory.EnumerateFiles(dir, TEMP_PREFIX + "*"))
            {
                try { if (File.GetLastWriteTimeUtc(stale) < cutoff) File.Delete(stale); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveStaging] temp sweep failed: {ex.Message}");
        }
    }

    private static bool HasContent(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists && fi.Length > 0;
        }
        catch { return false; }
    }
}
