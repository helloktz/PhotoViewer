using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.ServiceProviders.FileSearchService;
using PhotoViewer.Common.Types;
using ImageMagick;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public partial class Photo : PhDisposable
{
    private uint _width = 0;
    private uint _height = 0;
    private int _frameIndex = -1;

    private Task<PhotoMetadata>? _taskMetadata;
    private CancellationTokenSource? _cancelPhotoLoading;
    private readonly Lock _lock = new();
    private int _loadGeneration;

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private int _cancelEpoch;

    private ConcurrentDictionary<Guid, bool> _taskRefs = new();

    private static int _pendingSaveCount;

    private CancellationTokenSource? _cancelThumbnailLoading;
    private double _galleryThumbnailRequestSize;

    private int _releasedBytesPerPixel;

    private SKImage? _displayFrame;
    private SKImage? _displayFrameSource;
    private DisplayColorPass? _displayFramePass;

    private bool _isDisplayClaimed;

    private readonly SemaphoreSlim _displayFrameGate = new(1, 1);

    private readonly SemaphoreSlim _thumbnailLock = new(1, 1);

    private const uint MB_PER_THUMBNAIL_SLOT = 128;

    private static readonly Lazy<SemaphoreSlim> _thumbnailThrottleLock = new(() =>
    {
        var byCpu = Math.Clamp(Environment.ProcessorCount - 1, 4, 16);
        var byBudget = (int)(Core.Config.CacheMaxMemoryInMb / MB_PER_THUMBNAIL_SLOT);
        var slots = Math.Clamp(byBudget, 4, byCpu);

        return new SemaphoreSlim(slots, slots);
    });

    public IDisposable? Bitmap { get; private set; } = null;

    public Size Size => new Size(_width, _height);

    public uint Width => (uint)Size.Width;

    public uint Height => (uint)Size.Height;

    public int BytesPerPixel => Bitmap is SKImage img && !img.IsDisposed()
        ? Math.Max(1, img.ColorType.GetBytesPerPixel())
        : _releasedBytesPerPixel > 0 ? _releasedBytesPerPixel : 4;

    public double DecodeScale { get; private set; } = 1;

    public bool HasBakedDisplayProfile { get; private set; }

    public bool WillDecodeEmbeddedPreview => ReadOptions.OnlyLoadRawPreview
        && Metadata.IsEmbeddedPreviewLargeEnough(ReadOptions.PreviewMinWidth, ReadOptions.PreviewMinHeight);

    public (uint Width, uint Height) EstimatedDecodeSize => WillDecodeEmbeddedPreview
        ? (Metadata.PreviewWidth, Metadata.PreviewHeight)
        : (Metadata.Width, Metadata.Height);

    public int FrameIndex => _frameIndex;

    public PhotoState State { get; set; } = PhotoState.None;

    public string CodecId
    {
        get; private set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = string.Empty;

    public bool IsCurrent
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    }

    public bool IsClipboard => string.IsNullOrEmpty(FilePath);

    public string FilePath
    {
        get => Metadata.FilePath; set
        {
            if (!Metadata.FilePath.Equals(value, StringComparison.Ordinal))
            {
                Metadata.FilePath = value;
                (Bitmap as AnimatorImpl)?.UpdateFilePath(value);

                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(DirPath));
                OnPropertyChanged(nameof(Metadata));

                OnPropertyChanged(nameof(IsClipboard));
                OnPropertyChanged(nameof(Extension));
                OnPropertyChanged(nameof(FileTitle));
                OnPropertyChanged(nameof(GalleryFileTitle));
                OnPropertyChanged(nameof(GalleryFileExtension));
            }
        }
    }

    public string DirPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public string Extension => Path.GetExtension(FilePath);

    public string FileTitle => Path.GetFileNameWithoutExtension(FilePath);

    public string GalleryFileTitle => $"{FileTitle}.";

    public string GalleryFileExtension => Extension.Length > 1 ? Extension.Substring(1) : string.Empty;

    public Exception? Error { get; set; } = null;

    public PhotoReadOptions ReadOptions { get; set; } = new();

    public MagickReadSettings? ReadSettings { get; set; } = null;

    public PhotoMetadata Metadata
    {
        get; private set
        {
            if (field == value) return;
            try
            {
                field.Dispose();
                field = value;
                _ = OnPropertyChanged();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌: Metadata setter: {ex.Message}");
            }
        }
    } = new();

    public CancellationToken? CancelToken => _cancelPhotoLoading?.Token;

    public Bitmap? GalleryThumbnail
    {
        get; set
        {
            if (field == value) return;

            try
            {
                var old = field;
                field = value;
                _ = OnPropertyChanged();

                if (old is not null) Dispatcher.UIThread.Post(old.Dispose, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌: GalleryThumbnail setter: {ex.Message}");
            }
        }
    }

    public Photo() { }

    public Photo(string filePath = "", PhotoReadOptions? options = null)
    {
        FilePath = filePath;
        ReadOptions = options ?? new();
    }

    public Photo(FileSearchEntry entry, PhotoReadOptions? options = null)
    {
        Metadata = new PhotoMetadata(entry);
        ReadOptions = options ?? new();
    }

    public Photo(SKBitmap? bmp, PhotoState state = PhotoState.Loaded)
    {
        InitializePhoto(bmp, bmp?.Width ?? 0, bmp?.Height ?? 0, null, state);
    }

    public Photo(SKImage? img, PhotoState state = PhotoState.Loaded)
    {
        InitializePhoto(img, img?.Width ?? 0, img?.Height ?? 0, null, state);
    }

    public Photo(SKImage? img, PhotoMetadata? meta, PhotoState state = PhotoState.Loaded)
    {
        InitializePhoto(img, 0, 0, meta, state);
    }

    protected override async void OnDisposing()
    {
        base.OnDisposing();

        try
        {
            await OnDisposing(true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(OnDisposing)}: {ex.Message}");
        }
    }

    private async Task OnDisposing(bool disposeEverything)
    {
        CancelLoading();

        while (!_taskRefs.IsEmpty)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        await _displayFrameGate.WaitAsync().ConfigureAwait(false);
        try
        {
            UnloadBitmap();
        }
        finally
        {
            _ = _displayFrameGate.Release();
        }

        if (disposeEverything)
        {
            await UnloadThumbnailAsync().ConfigureAwait(false);

            if (_taskMetadata is not null)
            {
                try
                {
                    await _taskMetadata.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌❌❌ {nameof(OnDisposing)}/metadata: {ex.Message}");
                }
            }

            Metadata.Dispose();
            Metadata = new();

            lock (_lock)
            {
                _cancelPhotoLoading?.Dispose();
                _cancelPhotoLoading = null;
            }
        }
    }

    private void InitializePhoto(IDisposable? src, int width, int height, PhotoMetadata? meta, PhotoState state = PhotoState.Loaded)
    {
        if (src is null) Bitmap = null;
        else if (src is SKImage img) Bitmap = img;
        else if (src is SKBitmap bmp) Bitmap = SkiaCodec.ToSKImage(bmp);
        else if (src is AnimatorImpl animator) Bitmap = animator;
        else throw new ArgumentException("IGE: Unsupported bitmap source", nameof(src));

        Metadata.Dispose();
        Metadata = meta ?? new()
        {
            Width = (uint)width,
            Height = (uint)height,
            FrameCount = 1,
        };

        _width = (uint)Metadata.Width;
        _height = (uint)Metadata.Height;
        DecodeScale = 1;
        HasBakedDisplayProfile = false;

        State = state;
    }

    private CodecSelectionContext CreateCodecSelectionContext(PhotoMetadata meta)
    {
        return new CodecSelectionContext
        {
            EnableVectorRenderer = Core.Config.EnableVectorRenderer,
            IsDestColorProfileSupported = Core.IsDestColorProfileSupported,
            LoadRawThumbnailOnly = ReadOptions.OnlyLoadRawPreview && meta.RawThumbnail is not null,
            LoadOtherThumbnailOnly = ReadOptions.OnlyLoadNonRawPreview && (meta.ExifProfile?.ThumbnailLength ?? 0) > 0,
            PreviewMinWidth = ReadOptions.PreviewMinWidth,
            PreviewMinHeight = ReadOptions.PreviewMinHeight,
        };
    }

    private void ApplyDecodeResult(CodecDecodeResult result, int frameIndex)
    {
        CodecId = result.CodecId;

        _width = (uint)result.Size.Width;
        _height = (uint)result.Size.Height;
        DecodeScale = result.DecodeScale;
        HasBakedDisplayProfile = result.HasBakedDisplayProfile;

        if (result.VectorSource is not null)
        {
            Bitmap = result.VectorSource;
            result.VectorSource = null;
            return;
        }

        if (result.Animator is not null)
        {
            result.Animator.FrameChanged -= OnAnimatorFrameChanged;
            result.Animator.FrameChanged += OnAnimatorFrameChanged;
            Bitmap = result.Animator;
            result.Animator = null;
            return;
        }

        if (result.SingleFrame is not null)
        {
            Bitmap = result.SingleFrame;
            result.SingleFrame = null;

            _frameIndex = Math.Max(0, frameIndex);
            return;
        }

        Bitmap = null;
    }

    private MagickReadSettings GetOrCreateMagickReadSettings()
    {
        ReadSettings ??= MagickCodec.ParseSettings(ReadOptions, false, FilePath);
        return ReadSettings;
    }

    private async Task OnDecodingAsync(PhotoMetadata meta, CancellationToken token)
    {
        var options = ReadOptions;
        var context = CreateCodecSelectionContext(meta);
        var selectedCodec = Core.CodecRegistry.SelectDecodeCodec(meta, context);

        var isFallback = selectedCodec is null;
        var codec = selectedCodec ?? Core.CodecRegistry.FallbackDecodeCodec;

        var isPlugin = codec is not (SvgCodecAdapter or SkiaCodecAdapter or MagickCodecAdapter);
        PhotoTrace.Mark("decode:codec", FilePath,
            $"{codec.CodecId} (decodePriority={codec.DecodePriority}, plugin={isPlugin}, "
            + $"fallback={isFallback}, frameIndex={options.FrameIndex})");

        using var result = await codec.DecodeAsync(meta, options, context, token).ConfigureAwait(false);

        if (PhotoTrace.Enabled) PhotoTrace.Mark("decode:done", FilePath, $"kind={DescribeDecodeResult(result)}");

        var fallbackCodec = Core.CodecRegistry.FallbackDecodeCodec;
        if (IsEmptyDecodeResult(result) && !isFallback && !ReferenceEquals(codec, fallbackCodec))
        {
            token.ThrowIfCancellationRequested();
            PhotoTrace.Mark("decode:retry-fallback", FilePath, $"{codec.CodecId} decoded no frame");

            using var fallbackResult = await fallbackCodec
                .DecodeAsync(meta, options, context, token).ConfigureAwait(false);

            if (PhotoTrace.Enabled) PhotoTrace.Mark("decode:done", FilePath,
                $"kind={DescribeDecodeResult(fallbackResult)} (fallback)");

            ApplyDecodeResult(fallbackResult, options.FrameIndex);
            return;
        }

        ApplyDecodeResult(result, options.FrameIndex);
    }

    private static bool IsEmptyDecodeResult(CodecDecodeResult result) =>
        result.VectorSource is null && result.Animator is null && result.SingleFrame is null;

    private static string DescribeDecodeResult(CodecDecodeResult result)
    {
        if (result.VectorSource is not null) return "vector";
        if (result.Animator is not null) return $"animator frames={result.Animator.Frames.Length}";
        if (result.SingleFrame is SKImage img)
            return $"single-frame {img.Width}x{img.Height} colorType={img.ColorType}";
        return "none";
    }

    private static string DescribeMetadata(PhotoMetadata meta)
    {
        var hdr = meta.IsHdr ? $"HDR({meta.HdrTransferFn})" : "SDR";
        var profile = string.IsNullOrEmpty(meta.ColorProfileName) ? "none" : meta.ColorProfileName;
        return $"{meta.Width}x{meta.Height} frames={meta.FrameCount} ext={meta.FileExtension} "
            + $"vector={meta.IsVector} {hdr} wideGamut={meta.IsWideGamut} bpc={meta.BitsPerChannel} profile={profile}";
    }

    private void OnAnimatorFrameChanged(AnimatorImpl sender, AnimatorFrameChangedEventArgs e)
    {
        _frameIndex = (int)e.CurrentFrame;
    }

    public async void Unload()
    {
        try
        {
            while (!_taskRefs.IsEmpty)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            State = PhotoState.None;
            Error = null;

            await OnDisposing(false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(Unload)}: {ex.Message}");
        }
    }

    public IDisposable PinBitmap()
    {
        var taskId = Guid.NewGuid();
        _ = _taskRefs.TryAdd(taskId, true);

        return new BitmapPin(this, taskId);
    }

    private sealed class BitmapPin(Photo photo, Guid taskId) : IDisposable
    {
        public void Dispose() => photo._taskRefs.TryRemove(taskId, out _);
    }

    public void UnloadBitmap()
    {
        IDisposable? bitmap;
        SKImage? displayFrame;
        lock (_lock)
        {
            bitmap = Bitmap;
            Bitmap = null;
            _frameIndex = -1;
            _releasedBytesPerPixel = 0;

            displayFrame = _displayFrame;
            _displayFrame = null;
            _displayFrameSource = null;
            _displayFramePass = null;
            _isDisplayClaimed = false;
        }

        if (bitmap is AnimatorImpl animator)
        {
            animator.FrameChanged -= OnAnimatorFrameChanged;
        }

        bitmap?.Dispose();
        displayFrame?.Dispose();
    }

    public bool ReleaseFrame(SKImage frame)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(Bitmap, frame)) return false;

            _releasedBytesPerPixel = frame.IsDisposed() ? 0 : Math.Max(1, frame.ColorType.GetBytesPerPixel());
            Bitmap = null;
            return true;
        }
    }

    public long DisplayFrameBytes
    {
        get
        {
            lock (_lock)
            {
                var frame = _displayFrame;
                if (frame.IsDisposed()) return 0;

                return (long)frame.Width * frame.Height * frame.ColorType.GetBytesPerPixel();
            }
        }
    }

    public async Task PrepareDisplayFrameAsync(DisplayColorPass pass, CancellationToken token)
    {
        if (pass.IsNoOp) return;

        await _displayFrameGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            SKImage? source;
            lock (_lock)
            {
                if (_isDisplayClaimed) return;
                if (State != PhotoState.Loaded) return;
                if (Error is not null) return;
                if (Metadata.FrameCount > 1) return;

                source = Bitmap as SKImage;
                if (source.IsDisposed()) return;

                var isSameSource = ReferenceEquals(_displayFrameSource, source);
                var isSamePass = Equals(_displayFramePass, pass);
                if (isSameSource && isSamePass) return;
            }

            var sw = Stopwatch.StartNew();
            var displayFrame = pass.Apply(source);
            if (PhotoTrace.Enabled) PhotoTrace.Mark("cache:display-frame", FilePath,
                $"{sw.ElapsedMilliseconds} ms, changed={displayFrame is not null}");

            SKImage? stale;
            lock (_lock)
            {
                stale = _displayFrame;
                _displayFrame = displayFrame;
                _displayFrameSource = source;
                _displayFramePass = pass;
            }

            stale?.Dispose();
        }
        finally
        {
            _ = _displayFrameGate.Release();
        }
    }

    public async Task<SKImage?> TakeDisplayFrameAsync(SKImage frame, DisplayColorPass pass)
    {
        await _displayFrameGate.WaitAsync().ConfigureAwait(false);

        SKImage? displayFrame;
        bool isSameSource;
        bool isSamePass;
        try
        {
            lock (_lock)
            {
                _isDisplayClaimed = true;

                displayFrame = _displayFrame;
                isSameSource = ReferenceEquals(_displayFrameSource, frame);
                isSamePass = Equals(_displayFramePass, pass);

                _displayFrame = null;
                _displayFrameSource = null;
                _displayFramePass = null;
            }
        }
        finally
        {
            _ = _displayFrameGate.Release();
        }

        if (displayFrame is null) return null;

        var isUsable = isSameSource && isSamePass;
        if (isUsable) return displayFrame;

        displayFrame.Dispose();
        return null;
    }

    [MemberNotNull(nameof(_cancelPhotoLoading))]
    public virtual CancellationToken CancelLoading()
    {
        _ = Interlocked.Increment(ref _cancelEpoch);

        return ResetCancelToken();
    }

    [MemberNotNull(nameof(_cancelPhotoLoading))]
    private CancellationToken ResetCancelToken()
    {
        lock (_lock)
        {
            _cancelPhotoLoading?.Cancel();
            _cancelPhotoLoading?.Dispose();
            _cancelPhotoLoading = new();

            return _cancelPhotoLoading.Token;
        }
    }

    public virtual async Task LoadAsync(bool useCache,
        Func<PhotoLoadingEventArgs, Task>? handleProgressFn = null,
        bool skipLoadingEvent = false)
    {
        if (useCache && State != PhotoState.None)
        {
            PhotoTrace.Mark("load:cache-hit", FilePath, $"state={State}");
            await DispatchLoadedAsync(handleProgressFn);
            return;
        }

        var epoch = Volatile.Read(ref _cancelEpoch);
        await _loadGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _cancelEpoch) != epoch)
            {
                PhotoTrace.Mark("load:queued-cancel", FilePath, $"state={State}");
                return;
            }

            if (useCache && State != PhotoState.None)
            {
                PhotoTrace.Mark("load:cache-hit", FilePath, $"state={State}, afterGate=True");
                await DispatchLoadedAsync(handleProgressFn);
                return;
            }

            await LoadAsync__(useCache, handleProgressFn, skipLoadingEvent);
        }
        finally
        {
            _ = _loadGate.Release();
        }
    }

    private async Task DispatchLoadedAsync(Func<PhotoLoadingEventArgs, Task>? handleProgressFn)
    {
        if (handleProgressFn is null || State != PhotoState.Loaded) return;

        await handleProgressFn(new(PhotoState.Loaded, this, CancellationToken.None));
    }

    private async Task LoadAsync__(bool useCache,
        Func<PhotoLoadingEventArgs, Task>? handleProgressFn,
        bool skipLoadingEvent)
    {
        var token = ResetCancelToken();
        var myGeneration = Interlocked.Increment(ref _loadGeneration);

        PhotoTrace.Begin(FilePath, $"useCache={useCache}, skipLoadingEvent={skipLoadingEvent}");
        try
        {
            _isDisposed.SetFalse();
            State = PhotoState.None;
            Error = null;

            if (token.IsCancellationRequested) return;

            await LoadMetadataAsync(useCache, token: token);
            PhotoTrace.Mark("metadata:loaded", FilePath, DescribeMetadata(Metadata));

            var defaultFrameIndex = GetDefaultFrameIndex();
            if (ReadOptions.FrameIndex != defaultFrameIndex)
            {
                ReadOptions = ReadOptions with { FrameIndex = defaultFrameIndex };
            }

            if (!skipLoadingEvent)
            {
                if (handleProgressFn is not null)
                {
                    PhotoTrace.Mark("preview:dispatch", FilePath);
                    await handleProgressFn(new(PhotoState.Preview, this, token));
                }
            }

            if (token.IsCancellationRequested) return;

            Error = await Task.Factory.StartNew(async () =>
            {
                try
                {
                    await OnDecodingAsync(Metadata, token);
                    return null;
                }
                catch (TaskCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

            if (Error is not null) PhotoTrace.Mark("decode:error", FilePath, Error.Message);

            if (token.IsCancellationRequested) return;

            State = PhotoState.Loaded;
            PhotoTrace.Mark("loaded:dispatch", FilePath, $"hasError={Error is not null}");
            if (handleProgressFn is not null)
            {
                await handleProgressFn(new(PhotoState.Loaded, this, token));
            }
        }
        catch (TaskCanceledException)
        {
            State = PhotoState.None;
        }
        catch (Exception ex)
        {
            Error = ex;
            State = PhotoState.Loaded;
            PhotoTrace.Mark("load:exception", FilePath, ex.Message);
            if (handleProgressFn is not null)
            {
                await handleProgressFn(new(PhotoState.Loaded, this, token));
            }
        }
        finally
        {
            PhotoTrace.End(FilePath, $"state={State}, cancelled={token.IsCancellationRequested}");

            if (token.IsCancellationRequested
                && Volatile.Read(ref _loadGeneration) == myGeneration)
            {
                Unload();
            }
        }
    }

    public async Task LoadMetadataAsync(bool useCache, PhotoReadOptions? newOptions = null, CancellationToken token = default)
    {
        var task = Task.Run(() => LoadMetadataAsync__(useCache, newOptions));
        var meta = await task.WaitAsync(token).ConfigureAwait(false);

        if (meta is not null)
        {
            Metadata = meta;
        }
    }
    private async Task<PhotoMetadata?> LoadMetadataAsync__(bool useCache, PhotoReadOptions? newOptions = null)
    {
        try
        {
            ReadOptions = newOptions ?? ReadOptions;

            if (useCache)
            {
                if (_taskMetadata is not null
                    && _taskMetadata.Status != TaskStatus.Canceled
                    && _taskMetadata.Status != TaskStatus.Faulted)
                {
                    return await _taskMetadata;
                }
            }
            else
            {
                _taskMetadata = null;
            }

            var hasOutdatedCache = !useCache || Metadata.IsOutdated();

            if (hasOutdatedCache)
            {
                var metadataCodec = Core.CodecRegistry.SelectMetadataCodec(FilePath);
                PhotoTrace.Mark("metadata:codec", FilePath,
                    metadataCodec is null ? "none (fallback)" : $"{metadataCodec.CodecId} (metaPriority={metadataCodec.MetadataPriority})");

                if (metadataCodec is not null)
                {
                    _taskMetadata = Task.Run(() => metadataCodec.LoadMetadataAsync(
                        FilePath,
                        ReadOptions,
                        CancellationToken.None));
                }
                else
                {
                    _taskMetadata = Task.Run(() => new PhotoMetadata(FilePath));
                }

                return await _taskMetadata;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(LoadMetadataAsync__)}: {ex.Message}");
        }

        return null;
    }

    public int GetDefaultFrameIndex()
    {
        var meta = Metadata;
        var frameIndex = meta.DefaultFrameIndex;
        if (frameIndex <= 0) return 0;
        if (frameIndex >= meta.FrameCount) return 0;

        var context = CreateCodecSelectionContext(meta);
        var codec = Core.CodecRegistry.SelectDecodeCodec(meta, context) ?? Core.CodecRegistry.FallbackDecodeCodec;
        var decodesWithMagick = ReferenceEquals(codec, Core.CodecRegistry.FallbackDecodeCodec);
        if (!decodesWithMagick) return 0;

        return frameIndex;
    }

    public async Task<SKImage?> GetFrameAsync(uint frameIndex)
    {
        var newFrameIndex = (int)frameIndex;

        if (Bitmap is AnimatorImpl animator)
        {
            _frameIndex = newFrameIndex;
            return animator.GetRenderedFrameBitmap(frameIndex);
        }

        if (frameIndex == _frameIndex && Bitmap is SKImage cachedImg)
        {
            return cachedImg;
        }

        if (Metadata.FrameCount <= 1)
        {
            _frameIndex = newFrameIndex;
            return Bitmap as SKImage;
        }

        var newFrame = await Task.Factory.StartNew(async () =>
        {
            var options = ReadOptions with { FrameIndex = newFrameIndex };
            var context = CreateCodecSelectionContext(Metadata);
            var codec = Core.CodecRegistry.SelectDecodeCodec(Metadata, context);
            if (codec is not null)
            {
                using var result = await codec.DecodeAsync(Metadata, options, context, CancellationToken.None).ConfigureAwait(false);
                if (result.SingleFrame is SKImage sf)
                {
                    CodecId = result.CodecId;
                    HasBakedDisplayProfile = result.HasBakedDisplayProfile;

                    var detached = sf;
                    result.SingleFrame = null;
                    return detached;
                }
            }

            using var data = await MagickCodec.DecodeImageAsync(Metadata,
                options, GetOrCreateMagickReadSettings(), null, CancellationToken.None);
            HasBakedDisplayProfile = MagickCodec.HasBakedDisplayProfile(data, Metadata);
            return SkiaCodec.FromMagick(data.SingleFrame, MagickCodec.GetDecodedColorSpace(data, Metadata), Metadata.IsHdr);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

        lock (_lock)
        {
            if (IsDisposed)
            {
                newFrame?.Dispose();
                return null;
            }

            Bitmap?.Dispose();
            Bitmap = newFrame;
            _frameIndex = newFrameIndex;
            _width = (uint)(newFrame?.Width ?? 0);
            _height = (uint)(newFrame?.Height ?? 0);
        }

        return newFrame;
    }

    public Task<SKImage?> DecodeStaticFrameAsync(uint frameIndex, CancellationToken token = default)
    {
        return DecodeStaticFrameAsync(frameIndex, null, token);
    }

    public async Task<SKImage?> DecodeStaticFrameAsync(uint frameIndex, PhotoReadOptions? readOptions, CancellationToken token = default)
    {
        var newFrameIndex = (int)frameIndex;

        return await Task.Factory.StartNew(async () =>
        {
            var options = (readOptions ?? ReadOptions) with { FrameIndex = newFrameIndex };
            var context = CreateCodecSelectionContext(Metadata);
            var codec = Core.CodecRegistry.SelectDecodeCodec(Metadata, context);
            if (codec is not null)
            {
                using var result = await codec.DecodeAsync(Metadata, options, context, token).ConfigureAwait(false);
                if (result.SingleFrame is SKImage sf)
                {
                    var detached = sf;
                    result.SingleFrame = null;
                    return detached;
                }
            }

            using var data = await MagickCodec.DecodeImageAsync(Metadata, options,
                GetOrCreateMagickReadSettings(), null, token);
            return SkiaCodec.FromMagick(data.SingleFrame, MagickCodec.GetDecodedColorSpace(data, Metadata), Metadata.IsHdr);
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    public async Task SaveAsAsync(string destFilePath,
        PhotoTransform transforms, uint quality,
        bool preserveModifiedDate = false, CancellationToken token = default)
    {
        var taskId = Guid.NewGuid();
        _ = _taskRefs.TryAdd(taskId, true);
        _ = Interlocked.Increment(ref _pendingSaveCount);

        try
        {
            var lastWriteTime = File.GetLastWriteTime(destFilePath);

            var handled = await CodecEncodePipeline.TryEncodeAsync(this, destFilePath, transforms, quality, token);

            if (!handled && IsClipboard && Bitmap is SKImage img)
            {
                await SaveStaging.WriteThenPromoteAsync(destFilePath, stagePath =>
                    Task.Factory.StartNew(async () =>
                    {
                        await SkiaCodec.SaveAsync(img, stagePath, transforms, quality, token);
                    }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap(), token);
            }

            else if (!handled)
            {
                var saveAllFrames = Metadata.FrameCount > 1
                    && MagickFormatInfo.Create(destFilePath)?.SupportsMultipleFrames == true;

                var readOptions = ReadOptions with
                {
                    FrameIndex = saveAllFrames
                        ? -1
                        : ReadOptions.FrameIndex,
                };

                await SaveStaging.WriteThenPromoteAsync(destFilePath, stagePath =>
                    Task.Factory.StartNew(async () =>
                    {
                        await MagickCodec.SaveAsync(Metadata, stagePath, readOptions, transforms, quality, token);
                    }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap(), token);
            }

            if (preserveModifiedDate)
            {
                File.SetLastWriteTime(destFilePath, lastWriteTime);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _ = Interlocked.Decrement(ref _pendingSaveCount);
            _ = _taskRefs.TryRemove(taskId, out _);
        }
    }

    public static int PendingSaveCount => Volatile.Read(ref _pendingSaveCount);

    public static async Task WaitForPendingSavesAsync(TimeSpan timeout)
    {
        var startedAt = Stopwatch.StartNew();

        while (Volatile.Read(ref _pendingSaveCount) > 0 && startedAt.Elapsed < timeout)
        {
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    public void CancelThumbnailLoading()
    {
        try
        {
            _cancelThumbnailLoading?.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    public async Task LoadThumbnailAsync(double dpi)
    {
        var thumbSize = Core.Config.ThumbnailSize * dpi * 2;
        await LoadThumbnailAsync(thumbSize, useCache: true);
    }

    public async Task LoadThumbnailAsync(double thumbSize, bool useCache, CancellationToken cancellationToken = default)
    {
        if (useCache && GalleryThumbnail is not null && _galleryThumbnailRequestSize == thumbSize) return;
        if (IsDisposed || string.IsNullOrEmpty(FilePath)) return;

        await _thumbnailThrottleLock.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _thumbnailLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (useCache && GalleryThumbnail is not null && _galleryThumbnailRequestSize == thumbSize) return;
                if (IsDisposed) return;

                _cancelThumbnailLoading?.Dispose();
                _cancelThumbnailLoading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var token = _cancelThumbnailLoading.Token;

                using var diskThumb = useCache
                    ? await ThumbnailDiskCache.TryGetAsync(
                        FilePath, (int)thumbSize, Metadata.FileLastWriteTimeUtc, token)
                        .ConfigureAwait(false)
                    : null;
                var didProbePlatformCache = useCache && diskThumb.IsDisposed();
                using var platformThumb = didProbePlatformCache
                    ? await Core.PreviewProvider.TryGetCachedThumbnailAsync(
                        FilePath, (int)thumbSize, token).ConfigureAwait(false)
                    : null;
                var cachedThumb = platformThumb ?? diskThumb;

                if (!cachedThumb.IsDisposed())
                {
                    using var scaledThumb = thumbSize > 0
                        && (cachedThumb.Width > thumbSize || cachedThumb.Height > thumbSize)
                        ? await Task.Run(() => SkiaCodec.ScaleDown(cachedThumb, thumbSize), token)
                            .ConfigureAwait(false)
                        : null;

                    var avBitmapCached = await Task.Run(
                        () => SkiaCodec.ToWritableBitmap(scaledThumb ?? cachedThumb), token)
                        .ConfigureAwait(false);

                    if (avBitmapCached is null) return;
                    if (!await ShowGalleryThumbnailAsync(avBitmapCached, token)) return;

                    var cachedThumbMayBeTooSmall = thumbSize > 0
                        && Math.Max(cachedThumb.Width, cachedThumb.Height)
                            < thumbSize * PhotoPreviewProvider.MIN_PREVIEW_SIZE_RATIO;
                    if (!cachedThumbMayBeTooSmall)
                    {
                        _galleryThumbnailRequestSize = thumbSize;
                        return;
                    }
                }

                await LoadMetadataAsync(true, token: token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                if (PhotoPreviewProvider.IsPreviewLargeEnough(cachedThumb, Metadata, thumbSize))
                {
                    _galleryThumbnailRequestSize = thumbSize;
                    return;
                }

                PhotoTrace.Mark("thumb:provider", null, $"{FilePath} @ {(int)thumbSize}px");
                var swThumb = PhotoTrace.Enabled ? Stopwatch.StartNew() : null;
                using var skThumb = await Task.Run(
                    () => Core.PreviewProvider.GetThumbnailAsync(
                        Metadata, thumbSize, token, didProbePlatformCache), token)
                    .ConfigureAwait(false);
                if (token.IsCancellationRequested || skThumb.IsDisposed()) return;

                if (swThumb is not null)
                {
                    PhotoTrace.Mark("thumb:provider-done", null,
                        $"{FilePath} -> {skThumb.Width}x{skThumb.Height} in {swThumb.ElapsedMilliseconds}ms");
                }

                var avBitmap = await Task.Run(
                    () => SkiaCodec.ToWritableBitmap(skThumb), token)
                    .ConfigureAwait(false);

                if (avBitmap is null) return;
                if (!await ShowGalleryThumbnailAsync(avBitmap, token)) return;
                _galleryThumbnailRequestSize = thumbSize;

                await ThumbnailDiskCache.PutAsync(FilePath, (int)thumbSize, skThumb, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ {nameof(LoadThumbnailAsync)}: {ex.Message}");
            }
            finally
            {
                _thumbnailLock.Release();
            }
        }
        finally
        {
            _thumbnailThrottleLock.Value.Release();
        }
    }

    public async Task UnloadThumbnailAsync()
    {
        CancelThumbnailLoading();

        await _thumbnailLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _cancelThumbnailLoading?.Dispose();
            _cancelThumbnailLoading = null;

            await ClearGalleryThumbnailAsync();
        }
        finally
        {
            _thumbnailLock.Release();
        }
    }

    private async Task<bool> ShowGalleryThumbnailAsync(Bitmap thumbnail, CancellationToken token)
    {
        if (token.IsCancellationRequested || IsDisposed)
        {
            thumbnail.Dispose();
            return false;
        }

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested || IsDisposed)
            {
                thumbnail.Dispose();
                return false;
            }

            GalleryThumbnail = thumbnail;
            return true;
        });
    }

    private async Task ClearGalleryThumbnailAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            GalleryThumbnail = null;
            _galleryThumbnailRequestSize = 0;
        });
    }

}
