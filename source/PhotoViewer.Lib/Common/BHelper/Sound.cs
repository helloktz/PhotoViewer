using Avalonia.Platform;
using PhotoViewer.Common.Types;
using NetCoreAudio;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public partial class BHelper
{
    private const string NOTIFICATION_SOUND_ASSET = "avares://PhotoViewer.Lib/Assets/Sounds/notification.wav";
    private const int LINUX_PLAYBACK_TIMEOUT_MS = 10_000;
    private static readonly Lock _notificationSoundLock = new();
    private static string? _notificationSoundPath;
    private static Player? _notificationPlayer;

    private sealed record LinuxSoundPlayer(string Exe, string[] Args);

    private static readonly LinuxSoundPlayer[] LINUX_SOUND_PLAYERS =
    [
        new("paplay", []),
        new("pw-play", []),
        new("aplay", ["-q"]),
        new("ffplay", ["-nodisp", "-autoexit", "-loglevel", "quiet"]),
    ];
    private static volatile LinuxSoundPlayer? _linuxSoundPlayer;

    public static async Task PlayNotificationSoundAsync()
    {
        try
        {
            var filePath = GetNotificationSoundPath();
            if (filePath is null) return;

            if (OS == OSType.Linux)
            {
                await PlayLinuxSoundAsync(filePath).ConfigureAwait(false);
                return;
            }

            var player = GetNotificationPlayer();
            if (player is null) return;

            await player.Play(filePath).ConfigureAwait(false);
        }
        catch { }
    }

    private static async Task PlayLinuxSoundAsync(string filePath)
    {
        if (_linuxSoundPlayer is { } cached
            && await TryPlayLinuxSoundAsync(cached, filePath).ConfigureAwait(false))
        {
            return;
        }
        _linuxSoundPlayer = null;

        foreach (var player in LINUX_SOUND_PLAYERS)
        {
            if (await TryPlayLinuxSoundAsync(player, filePath).ConfigureAwait(false))
            {
                _linuxSoundPlayer = player;
                return;
            }
        }
    }

    private static async Task<bool> TryPlayLinuxSoundAsync(LinuxSoundPlayer player, string filePath)
    {
        var psi = new ProcessStartInfo(player.Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in player.Args) psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add(filePath);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch
        {
            return false;
        }
        if (proc is null) return false;

        using (proc)
        {
            using var timeoutCts = new CancellationTokenSource(LINUX_PLAYBACK_TIMEOUT_MS);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                return true;
            }

            return proc.ExitCode == 0;
        }
    }

    private static Player? GetNotificationPlayer()
    {
        lock (_notificationSoundLock)
        {
            try
            {
                _notificationPlayer ??= new Player();
            }
            catch { }

            return _notificationPlayer;
        }
    }

    private static string? GetNotificationSoundPath()
    {
        lock (_notificationSoundLock)
        {
            if (_notificationSoundPath is not null && File.Exists(_notificationSoundPath))
            {
                return _notificationSoundPath;
            }

            try
            {
                var filePath = ConfigDir(Dir.Temporary, "ig_notification.wav");

                using (var assetStream = AssetLoader.Open(new Uri(NOTIFICATION_SOUND_ASSET)))
                using (var fileStream = File.Create(filePath))
                {
                    assetStream.CopyTo(fileStream);
                }

                _notificationSoundPath = filePath;
            }
            catch
            {
                _notificationSoundPath = null;
            }

            return _notificationSoundPath;
        }
    }

}
