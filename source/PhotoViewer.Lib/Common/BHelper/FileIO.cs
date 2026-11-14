
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public partial class BHelper
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sharedFileGates = new(StringComparer.OrdinalIgnoreCase);

    private const int SHARED_FILE_MAX_ATTEMPTS = 12;
    private const int SHARED_FILE_RETRY_DELAY_MS = 60;

    public static FileStream OpenReadShared(string filePath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
            }
            catch (Exception ex) when (IsTransientFileError__(ex) && attempt < SHARED_FILE_MAX_ATTEMPTS)
            {
                Debug.WriteLine($"[BHelper] Read attempt {attempt} on '{filePath}': {ex.Message}");
                Thread.Sleep(SHARED_FILE_RETRY_DELAY_MS);
            }
        }
    }

    public static async Task WriteAllTextSharedAsync(string filePath, string content,
        CancellationToken token = default)
    {
        var gate = _sharedFileGates.GetOrAdd(filePath, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() => WriteAllTextShared__(filePath, content, token), token)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static void WriteAllTextShared__(string filePath, string content, CancellationToken token)
    {
        if (TryWriteViaTempFile__(filePath, content, token)) return;

        WriteAllTextDirect__(filePath, content, token);
    }

    private static bool TryWriteViaTempFile__(string filePath, string content, CancellationToken token)
    {
        var tempPath = $"{filePath}.{Environment.ProcessId}.tmp";

        try
        {
            File.WriteAllText(tempPath, content, Encoding.UTF8);

            for (var attempt = 1; attempt <= SHARED_FILE_MAX_ATTEMPTS; attempt++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    File.Move(tempPath, filePath, true);
                    return true;
                }
                catch (Exception ex) when (IsTransientFileError__(ex))
                {
                    Debug.WriteLine($"[BHelper] Replace attempt {attempt} on '{filePath}': {ex.Message}");
                    Thread.Sleep(SHARED_FILE_RETRY_DELAY_MS);
                }
            }
        }
        catch (Exception ex) when (IsTransientFileError__(ex))
        {
            Debug.WriteLine($"[BHelper] Could not stage '{tempPath}': {ex.Message}");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }

        return false;
    }

    private static void WriteAllTextDirect__(string filePath, string content, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                File.WriteAllText(filePath, content, Encoding.UTF8);
                return;
            }
            catch (Exception ex) when (IsTransientFileError__(ex) && attempt < SHARED_FILE_MAX_ATTEMPTS)
            {
                Debug.WriteLine($"[BHelper] Write attempt {attempt} on '{filePath}': {ex.Message}");
                Thread.Sleep(SHARED_FILE_RETRY_DELAY_MS);
            }
        }
    }

    private static bool IsTransientFileError__(Exception ex) => ex switch
    {
        DirectoryNotFoundException or FileNotFoundException or PathTooLongException => false,
        IOException or UnauthorizedAccessException => true,
        _ => false,
    };
}
