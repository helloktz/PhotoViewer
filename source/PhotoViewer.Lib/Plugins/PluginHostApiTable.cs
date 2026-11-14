using PhotoViewer.Common;
using ImageGlass.SDK.Plugins;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace PhotoViewer.Plugins;

internal static unsafe class PluginHostApiTable
{
    private static IGHostApi* _hostApi;
    private static IGHostCoreApi* _coreApi;

    private static readonly ConcurrentDictionary<nint, CancellationToken> _cancellationTokens = new();
    private static long _nextCancellationHandle;

    public static IGHostApi* Get()
    {
        if (_hostApi != null) return _hostApi;
        Initialize();
        return _hostApi!;
    }

    public static nint RegisterCancellation(CancellationToken token)
    {
        if (token == default || !token.CanBeCanceled) return 0;
        var handle = (nint)Interlocked.Increment(ref _nextCancellationHandle);
        _cancellationTokens[handle] = token;
        return handle;
    }

    public static void ReleaseCancellation(nint handle)
    {
        if (handle == 0) return;
        _cancellationTokens.TryRemove(handle, out _);
    }

    private static void Initialize()
    {
        _coreApi = (IGHostCoreApi*)NativeMemory.AllocZeroed((nuint)sizeof(IGHostCoreApi));
        _coreApi->Log = &HostLog;
        _coreApi->Alloc = &HostAlloc;
        _coreApi->Free = &HostFree;
        _coreApi->IsCancellationRequested = &HostIsCancellationRequested;
        _coreApi->GetConfigDirectory = &HostGetConfigDirectory;

        _hostApi = (IGHostApi*)NativeMemory.AllocZeroed((nuint)sizeof(IGHostApi));
        _hostApi->StructSize = sizeof(IGHostApi);
        _hostApi->AbiVersion = IGNativeAbi.IG_PLUGIN_ABI_VERSION;
        _hostApi->Core = _coreApi;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void HostLog(int level, IGStringRef message)
    {
        try
        {
            var text = message.ToManaged();
            Debug.WriteLine($"[NativePlugin][{level}] {text}");
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void* HostAlloc(nuint size)
    {
        try
        {
            return NativeMemory.Alloc(size);
        }
        catch
        {
            return null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HostFree(void* ptr)
    {
        try
        {
            if (ptr != null) NativeMemory.Free(ptr);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int HostIsCancellationRequested(void* cancellation)
    {
        try
        {
            var handle = (nint)cancellation;
            if (handle == 0) return 0;
            if (_cancellationTokens.TryGetValue(handle, out var token))
            {
                return token.IsCancellationRequested ? 1 : 0;
            }
        }
        catch { }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int HostGetConfigDirectory(char* buffer, int bufferLength)
    {
        try
        {
            var path = BHelper.ConfigPath;
            if (buffer == null || bufferLength <= 0) return path.Length;
            var n = Math.Min(path.Length, bufferLength);
            for (var i = 0; i < n; i++) buffer[i] = path[i];
            return n;
        }
        catch
        {
            return 0;
        }
    }

}
