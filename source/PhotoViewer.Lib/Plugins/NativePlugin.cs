using PhotoViewer.Common.Types;
using ImageGlass.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PhotoViewer.Plugins;

internal sealed unsafe class NativePlugin : PhDisposable
{

    public string PluginId { get; }

    public string LibraryPath { get; }

    public nint LibraryHandle { get; }

    public IGPluginApi* PluginApi { get; }

    public List<NativeCodecEntry> Codecs { get; } = [];

    public PluginLiveToken LiveToken { get; } = new();

    public NativePlugin(string pluginId, string libraryPath, nint libraryHandle, IGPluginApi* pluginApi)
    {
        PluginId = pluginId;
        LibraryPath = libraryPath;
        LibraryHandle = libraryHandle;
        PluginApi = pluginApi;
    }

    protected override void OnDisposing()
    {
        LiveToken.MarkDead();

        try
        {
            if (PluginApi != null && PluginApi->Shutdown != null)
            {
                PluginApi->Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NativePlugin] Shutdown threw for '{PluginId}': {ex.Message}");
        }

        try
        {
            if (LibraryHandle != 0) NativeLibrary.Free(LibraryHandle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NativePlugin] NativeLibrary.Free threw for '{PluginId}': {ex.Message}");
        }

        base.OnDisposing();
    }
}

internal readonly struct NativeCodecEntry
{

    public readonly nint CodecApiPtr;

    public readonly CodecPluginCapability Capability;

    public NativeCodecEntry(nint codecApiPtr, CodecPluginCapability capability)
    {
        CodecApiPtr = codecApiPtr;
        Capability = capability;
    }
}

internal sealed class PluginLiveToken
{
    private readonly Lock _gate = new();
    private bool _isAlive = true;
    private int _entered;

    public void RunIfAlive(Action release)
    {
        lock (_gate)
        {
            if (!_isAlive) return;
            release();
        }
    }

    public bool TryEnter()
    {
        lock (_gate)
        {
            if (!_isAlive) return false;
            _entered++;
            return true;
        }
    }

    public void Exit()
    {
        lock (_gate)
        {
            if (_entered > 0) _entered--;
        }
    }

    public void MarkDead()
    {
        lock (_gate) { _isAlive = false; }

        var spin = new SpinWait();
        while (true)
        {
            lock (_gate)
            {
                if (_entered == 0) return;
            }
            spin.SpinOnce();
        }
    }
}
