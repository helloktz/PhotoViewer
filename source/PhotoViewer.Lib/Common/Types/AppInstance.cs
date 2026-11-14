using Avalonia.Threading;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Types;

public partial class AppInstance : PhDisposable
{
    private readonly string _id;
    private readonly string _pipeName;

    private readonly Mutex? _mutex;
    private readonly FileStream? _lockFile;

    public bool IsFirstInstance { get; }

    public event TEventHandler<AppInstance, InstanceInvokedEventArgs>? InstanceInvoked;

    public AppInstance(string id)
    {
        _id = id;

        _pipeName = OperatingSystem.IsWindows()
            ? id
            : $"{id}-{Environment.UserName}";

        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(true, id, out var isFirstInstance);
            IsFirstInstance = isFirstInstance;
        }
        else
        {
            IsFirstInstance = TryLockFile(_pipeName, out _lockFile);
        }

        if (IsFirstInstance)
        {
            StartPipeServer();
        }
    }

    private static bool TryLockFile(string name, out FileStream? lockFile)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}.lock");

        try
        {
            lockFile = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            lockFile = null;
            return false;
        }
        catch
        {
            lockFile = null;
            return true;
        }
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        if (_mutex is not null && IsFirstInstance)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        _lockFile?.Dispose();
    }

    public bool SendArgsToExistingInstances(string cmd, params string[] args)
    {
        if (IsFirstInstance) return false;

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(1000);

            using var writer = new StreamWriter(client);
            writer.WriteLine(args.Length + 1);

            writer.WriteLine(cmd);

            foreach (var arg in args)
            {
                writer.WriteLine(arg);
            }

            writer.Flush();
            return true;
        }
        catch { return false; }
    }

    private void StartPipeServer()
    {
        _ = Task.Run(() => StartPipeServerAsync__());
    }

    private async Task StartPipeServerAsync__()
    {
        while (!IsDisposed)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName,
                    PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using var reader = new StreamReader(server);

                await server.WaitForConnectionAsync();

                var countStr = await reader.ReadLineAsync();
                if (!int.TryParse(countStr, out var argsCount)) continue;

                var args = new string[argsCount - 1];
                var cmd = string.Empty;

                for (int i = 0; i < argsCount; i++)
                {
                    var text = await reader.ReadLineAsync() ?? string.Empty;

                    if (i == 0) cmd = text;
                    else args[i - 1] = text;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    InstanceInvoked?.Invoke(this, new InstanceInvokedEventArgs(_id, cmd, args));
                });
            }
            catch { }
        }
    }

}

public class InstanceInvokedEventArgs(string id, string cmd, string[] args) : EventArgs
{
    public string Id { get; set; } = id;
    public string Command { get; set; } = cmd;
    public string[] Arguments { get; set; } = args;
}
