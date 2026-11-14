using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer.ZoomAndPan;
using SkiaSharp;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl : PhControl
{
    private CancellationTokenSource? _cancelPreview;
    internal InterlockedBool _isPreviewing = new(false);
    internal InterlockedBool _isFirstDraw = new(false);
    private InterlockedBool _isRecoveringPhoto = new(false);
    internal PhotoLoadingOptions _loadingOptions = new();

    internal TaskCompletionSource? _firstDrawTcs;

    private Point? _lastMousePanPoint = null;
    private Point? _lockZoomSavedSrcPoint;
    private Point? _mouseClickDownPoint = null;

    public event TEventHandler<ViewerControl, PhotoLoadingEventArgs>? PhotoLoading;
    public event TEventHandler<ViewerControl, PhotoFrameChangedEventArgs>? PhotoFrameChanged;
    public event TEventHandler<ViewerControl, ViewerPointerEventArgs>? ViewerPointerMoved;
    public event TEventHandler<ViewerControl, ViewerPointerEventArgs>? ViewerPointerPressed;
    public event TEventHandler<ViewerControl, ViewerPointerClickEventArgs>? ViewerPointerClicked;
    public event TEventHandler<ViewerControl, ViewerMouseWheelEventArgs>? ViewerMouseWheel;

    public ViewerControl()
    {
        InitScrollbars();
    }

    public Rect DrawingArea { get; private set; }

    public Photo? Photo { get; private set; }

    public Size BitmapSize { get; private set; }

    public PhotoSource SourceKind { get; private set; } = PhotoSource.None;

    public InterlockedBool ShouldLoadFullResolution = new(true);

    public bool IsImageAnimating => _animator?.IsPlaying ?? false;

    public bool IsColorInverted { get; protected set; } = false;

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<ViewerControl, bool>(nameof(IsInteractive), true);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        Core.ColorProfileChanged += Core_ColorProfileChanged;

        AddHandler(ContextRequestedEvent, OnContextMenuRequested, RoutingStrategies.Tunnel);
        RegisterTouchGestures();

        AttachMirrorSource(MirrorSource);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Core.ColorProfileChanged -= Core_ColorProfileChanged;

        UnregisterTouchGestures();

        RemoveHandler(ContextRequestedEvent, OnContextMenuRequested);

        DetachMirrorSource(MirrorSource);
        DisposeCheckerboard();
        DisposeNativePhotoResources();
    }

    private void Core_ColorProfileChanged(object? sender, DestColorProfileChangedEventArgs e)
    {
        if (!e.RequiresPhotoReload) return;

        Photo? photo;
        lock (_lock)
        {
            if (_animator is not null || IsVectorSource()) return;
            if (Photo is not { State: PhotoState.Loaded } || !HasColorManagement()) return;
            photo = Photo;
        }

        _ = SetPhotoAsync(photo, new PhotoLoadingOptions
        {
            ResetZoom = false,
            UseCache = false,
            Channels = Core.ColorChannels,
            Orientation = PhotoOrientation,
            IsColorInverted = IsColorInverted,
        });
    }

    private bool HasColorManagement()
    {
        var meta = Photo?.Metadata;
        if (meta is null) return false;

        return meta.IsHdr
            || Core.Config.EnableAlwaysApplyColorProfile
            || meta.SkiaColorSpace is not null
            || meta.MagickColorProfile is not null;
    }

    protected override void OnIgDpiChanged()
    {
        base.OnIgDpiChanged();

        DisposeCheckerboard();
        InvalidateVisual();

        OnRenderStateChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == PaddingProperty || e.Property == BoundsProperty)
        {
            if (MirrorSource is not null)
            {
                InvalidateVisual();
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                DrawingArea = Bounds.Deflate(Padding);
                CalculateDrawingRegion();

                var shouldResetZoom = Photo is not null && !_zooming.IsManual;
                Refresh(shouldResetZoom, false, true);
            });
        }
        else if (e.Property == EnableNavButtonsProperty)
        {
            _navButtons.IsEnabled = (bool)e.NewValue! && IsInteractive;
        }
        else if (e.Property == ScrollbarModeProperty)
        {
            ApplyScrollbarMode();
        }
        else if (e.Property == IsInteractiveProperty)
        {
            ApplyInteractivity();
        }
        else if (e.Property == MirrorSourceProperty)
        {
            OnMirrorSourceChanged(e.OldValue as ViewerControl, e.NewValue as ViewerControl);
        }
        else if (e.Property == EnableMirrorSyncProperty)
        {
            InvalidateVisual();
        }
    }

    private void ApplyInteractivity()
    {
        var isInteractive = IsInteractive;

        IsHitTestVisible = isInteractive;
        _navButtons.IsEnabled = isInteractive && EnableNavButtons;
        ApplyScrollbarMode();
    }

    private void OnContextMenuRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var p = e.GetCurrentPoint(this);
        var requestRerender = false;

        if (p.Pointer.Type == PointerType.Mouse && !IsThumbButtonPressed(p))
        {
            var canPanByMouse = !EnableSelection || e.Properties.IsMiddleButtonPressed;

            if (canPanByMouse)
            {
                _lastMousePanPoint = p.Position;
            }
            else if (EnableSelection)
            {
                requestRerender = OnSelectionBegin(p);
            }

            _mouseClickDownPoint = p.Position;
        }

        if (requestRerender) InvalidateVisual();

        OnViewerPointerPressed(e, p);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (CurrentSelectionAction != SelectionAction.None)
        {
            _ = OnSelectionEnd(true);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var isThumbButton = IsThumbButton(e.InitialPressMouseButton);

        if (!isThumbButton)
        {
            _lastMousePanPoint = null;

            var requestRerender = OnSelectionEnd(false);
            if (requestRerender) InvalidateVisual();
        }

        DispatchMouseClickAction(e);
        if (!isThumbButton) _mouseClickDownPoint = null;

        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _lastMousePanPoint = null;

        var requestRerender = OnSelectionEnd(false);
        if (requestRerender) InvalidateVisual();

        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetCurrentPoint(this);
        var requestRerender = false;

        _zooming.ZoomedPoint = p.Position;

        if (_lastMousePanPoint is not null)
        {
            var hDistance = _lastMousePanPoint.Value.X - p.Position.X;
            var vDistance = _lastMousePanPoint.Value.Y - p.Position.Y;
            _lastMousePanPoint = p.Position;

            if (!FeatureManager.IsPanLocked())
                requestRerender = PanTo(hDistance, vDistance, p.Position);
        }
        else
        {
            requestRerender = OnSelectionUpdating(p.Position);
        }

        if (requestRerender) InvalidateVisual();

        OnViewerPointerMoved(e, p);
    }

    protected override void OnRightTapped(TappedEventArgs e)
    {
        base.OnRightTapped(e);

        ViewerPointerClicked?.Invoke(this, new ViewerPointerClickEventArgs(e, MouseClickEvent.RightClick));
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        if (EnableSelection) return;

        var pos = e.GetPosition(this);
        if (IsInNavButtonHitArea(pos)) return;

        ViewerPointerClicked?.Invoke(this, new ViewerPointerClickEventArgs(e, MouseClickEvent.LeftDoubleClick));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y;
        var deltaAbs = Math.Abs(delta);
        var position = e.GetPosition(this);
        var isPreciseScrolling = Core.ShellProvider?.HasPreciseScrollingDeltas() ?? false;
        var isUsingTouchpad = isPreciseScrolling || deltaAbs != 1;

        if (isUsingTouchpad)
        {
            var isScrollingHorz = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) * 1.5;

            if (isScrollingHorz)
            {
                if (!FeatureManager.IsPanLocked()) PanTo(e.Delta.X * -50, e.Delta.Y * -50, position);
                return;
            }

            delta *= 70;
            if (!FeatureManager.IsZoomLocked()) _ = ZoomByDeltaToPoint(delta, position);
            return;
        }

        delta *= Const.MOUSE_WHEEL_SCROLL_DELTA;
        var wheelEvent = GetMouseWheelEvent(e.KeyModifiers);

        ViewerMouseWheel?.Invoke(this, new ViewerMouseWheelEventArgs(e, wheelEvent, delta, position));
    }

    protected virtual void OnPhotoLoading(PhotoLoadingEventArgs e)
    {
        PhotoLoading?.Invoke(this, e);
    }

    protected virtual void OnPhotoFrameChanged(AnimatorFrameChangedEventArgs? e = null)
    {
        if (SourceKind == PhotoSource.None) return;

        var canAnimate = _animator != null;
        var isLivePhoto = Photo?.Metadata?.IsLivePhoto ?? false;
        var args = new PhotoFrameChangedEventArgs(canAnimate, IsImageAnimating, isLivePhoto)
        {
            CurrentFrame = e?.CurrentFrame ?? (uint)Math.Max(0, Photo?.FrameIndex ?? 0),
            FrameCount = e?.FrameCount ?? Photo?.Metadata.FrameCount ?? 0,
            CurrentLoop = e?.CurrentLoop ?? _animator?.CurrentLoop ?? 0,
            LoopCount = e?.LoopCount ?? _animator?.LoopCount ?? 0,
        };

        PhotoFrameChanged?.Invoke(this, args);
    }

    protected virtual void OnViewerPointerPressed(PointerEventArgs e, PointerPoint p)
    {
        if (ViewerPointerPressed is not null)
        {
            var srcPoint = PointClientToSource(p.Position).ToPixelPoint();
            ViewerPointerPressed.Invoke(this, new ViewerPointerEventArgs(e, p, srcPoint));
        }
    }

    protected virtual void OnViewerPointerMoved(PointerEventArgs e, PointerPoint p)
    {
        if (ViewerPointerMoved is not null)
        {
            var srcPoint = PointClientToSource(p.Position).ToPixelPoint();
            ViewerPointerMoved.Invoke(this, new ViewerPointerEventArgs(e, p, srcPoint));
        }
    }

    private static MouseWheelEvent GetMouseWheelEvent(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control)) return MouseWheelEvent.CtrlAndScroll;
        if (modifiers.HasFlag(KeyModifiers.Shift)) return MouseWheelEvent.ShiftAndScroll;
        if (modifiers.HasFlag(KeyModifiers.Alt)) return MouseWheelEvent.AltAndScroll;
        return MouseWheelEvent.Scroll;
    }

    private bool IsInNavButtonHitArea(Point pos)
    {
        if (!EnableNavButtons || !_navButtons.IsEnabled) return false;

        var hitWidth = NavButtonsInfo.NAV_BTN_SIZE.Width + NavButtonsInfo.NAV_BTN_MARGIN;
        var leftHitArea = new Rect(0, 0, hitWidth, Bounds.Height);
        var rightHitArea = new Rect(Bounds.Width - hitWidth, 0, hitWidth, Bounds.Height);

        return leftHitArea.Contains(pos) || rightHitArea.Contains(pos);
    }

    private void DispatchMouseClickAction(PointerReleasedEventArgs e)
    {
        var clickEvent = e.InitialPressMouseButton switch
        {
            MouseButton.Left => MouseClickEvent.LeftClick,
            MouseButton.Middle => MouseClickEvent.WheelClick,
            MouseButton.XButton1 => MouseClickEvent.XButton1Click,
            MouseButton.XButton2 => MouseClickEvent.XButton2Click,
            _ => (MouseClickEvent?)null,
        };
        if (clickEvent is null) return;

        if (!IsThumbButton(e.InitialPressMouseButton))
        {
            if (_mouseClickDownPoint is null) return;
            if (EnableSelection) return;

            var pos = e.GetPosition(this);

            var dragDistance = Math.Sqrt(
                Math.Pow(pos.X - _mouseClickDownPoint.Value.X, 2)
                + Math.Pow(pos.Y - _mouseClickDownPoint.Value.Y, 2));
            if (dragDistance > 5) return;

            if (IsInNavButtonHitArea(pos)) return;
        }

        ViewerPointerClicked?.Invoke(this, new ViewerPointerClickEventArgs(e, clickEvent.Value));
    }

    private static bool IsThumbButtonPressed(PointerPoint p)
    {
        return p.Properties.PointerUpdateKind
            is PointerUpdateKind.XButton1Pressed
            or PointerUpdateKind.XButton2Pressed;
    }

    private static bool IsThumbButton(MouseButton button)
    {
        return button is MouseButton.XButton1 or MouseButton.XButton2;
    }

    public void Refresh()
    {
        Refresh(true);
    }

    public void Refresh(bool resetZoom = true, bool isManualZoom = false, bool zoomedByResizing = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (resetZoom)
            {
                SetZoomMode(null, isManualZoom, zoomedByResizing);
            }
            else if (DestRect.IsEmpty && !BitmapSize.IsEmpty)
            {
                if (_zooming.IsManual)
                {
                    _zooming.ZoomedPoint = new();
                    _zooming.OldFactor = _zooming.Factor;
                    CalculateDrawingRegion();
                }
                else
                {
                    SetZoomMode(null, false, zoomedByResizing);
                }
            }

            UpdateScrollbars();
            InvalidateVisual();
            OnRenderStateChanged();
        });
    }

    [MemberNotNull(nameof(_cancelPreview))]
    private void CancelPreview()
    {
        lock (_lock)
        {
            _cancelPreview?.Cancel();
            _cancelPreview?.Dispose();
            _cancelPreview = new();
        }
    }

    private void DisposeNativePhotoResources()
    {
        lock (_lock)
        {
            _mipmapCache?.Dispose();
            _mipmapCache = null;

            _animator?.Dispose();
            _animator = null;

            DisposeVectorResources();

            SKImageRef.Set(ref _imgSource, null);
            SKImageRef.Set(ref _imgRender, null);
            SKImageRef.Set(ref _imgHdrSource, null);
        }

        OnRenderStateChanged();
    }

    [MemberNotNull(nameof(_cancelPreview))]
    public void UnloadPhoto()
    {
        lock (_lock)
        {
            CancelPreview();
            StopAnimator();
            SourceKind = PhotoSource.None;
            Photo?.CancelLoading();
            Photo?.Unload();

            _firstDrawTcs?.TrySetResult();
            _firstDrawTcs = null;

            AnimationSource = AnimationSources.None;
            ClearPhotoTransforms();
            _loadingOptions = new();
            _enablePanningVelocity = false;
            SetSourceSelection(new Rect(), false);
            BitmapSize = new();

            DisposeNativePhotoResources();
        }
    }

    public void ClearPhotoTransforms()
    {
        IsColorInverted = false;
        PhotoOrientation = ImageOrientation.Identity;
    }

    public async Task SetPhotoAsync(Photo? inputPhoto, PhotoLoadingOptions? options = null)
    {
        PhotoTrace.Mark("viewer:set-photo", inputPhoto?.FilePath,
            $"cache={options?.UseCache ?? true}, state={inputPhoto?.State}, shouldLoadFull={ShouldLoadFullResolution}");

        lock (_lock)
        {
            _lockZoomSavedSrcPoint = ZoomMode == ZoomMode.LockZoom ? _logicalSrcPoint : null;

            BeginTransition(options?.Transition);

            UnloadPhoto();

            if (inputPhoto is null)
            {
                Photo = null;
                Refresh(true);
                return;
            }

            SourceKind = PhotoSource.Native;
            _loadingOptions = options ?? new();
            _enablePanningVelocity = true;
            Photo = inputPhoto;

            IsColorInverted = _loadingOptions.IsColorInverted;
            PhotoOrientation = _loadingOptions.Orientation;
        }

        if (_loadingOptions.UseCache && inputPhoto.State == PhotoState.Loaded && ShouldLoadFullResolution)
        {
            var token = inputPhoto.CancelToken ?? default;
            await HandlePhotoLoadedAsync(new(PhotoState.Loaded, inputPhoto, token));
        }
        else
        {
            var useCache = _loadingOptions.UseCache && ShouldLoadFullResolution;
            await LoadPhotoAsync(
                useCache: useCache,
                skipLoadingEvent: !_loadingOptions.ResetZoom);
        }

        await RecoverIfNothingRenderedAsync(inputPhoto);
    }

    private async Task RecoverIfNothingRenderedAsync(Photo photo)
    {
        if (_isRecoveringPhoto) return;
        if (!NeedsRecovery(photo)) return;

        PhotoTrace.Mark("viewer:recover", photo.FilePath, $"state={photo.State}");

        if (photo.State == PhotoState.Loaded)
        {
            await HandlePhotoLoadedAsync(new(PhotoState.Loaded, photo, CancellationToken.None));
            if (!NeedsRecovery(photo)) return;
        }

        PhotoTrace.Mark("viewer:recover-reload", photo.FilePath);
        _isRecoveringPhoto.SetTrue();
        try
        {
            await LoadPhotoAsync(useCache: false, skipLoadingEvent: true);
        }
        finally
        {
            _isRecoveringPhoto.SetFalse();
        }
    }

    private bool NeedsRecovery(Photo photo)
    {
        if (!ReferenceEquals(photo, Photo)) return false;

        if (!ShouldLoadFullResolution || photo.Error is not null) return false;

        lock (_lock)
        {
            if (IsVectorSource() || _animator is not null) return false;

            return _imgSource is null || _imgSource.Image.IsDisposed();
        }
    }

    public async Task LoadPhotoAsync(bool useCache, bool skipLoadingEvent)
    {
        if (Photo is null) return;
        var photo = Photo;

        await photo.LoadAsync(useCache, OnPhotoLoadingProgressAsync, skipLoadingEvent);

        await RecoverIfNothingRenderedAsync(photo);
    }

    public async Task WaitForPhotoRenderedAsync(TimeSpan timeout)
    {
        Task? drawTask;
        lock (_lock)
        {
            drawTask = _firstDrawTcs?.Task;
        }

        if (drawTask is null || drawTask.IsCompleted) return;

        _ = await Task.WhenAny(drawTask, Task.Delay(timeout));
    }

    private async Task OnPhotoLoadingProgressAsync(PhotoLoadingEventArgs e)
    {
        if (e.State == PhotoState.Preview)
        {
            await HandlePhotoPreviewAsync(e);
        }
        else
        {
            await HandlePhotoLoadedAsync(e);
        }
    }

    private async Task HandlePhotoPreviewAsync(PhotoLoadingEventArgs e)
    {
        if (!ShouldLoadFullResolution) e.Photo.CancelLoading();

        if (!EnableImagePreview || ZoomMode == ZoomMode.LockZoom)
        {
            PhotoTrace.Mark("viewer:preview-skip", e.Photo.FilePath,
                $"enablePreview={EnableImagePreview}, zoomMode={ZoomMode}");

            _isPreviewing.SetFalse();
            OnPhotoLoading(e);
            return;
        }

        SKImage? imgPreview = null;
        var token = _cancelPreview?.Token ?? default;
        var hasPreview = false;

        try
        {
            if (e.Photo.GalleryThumbnail is not null)
            {
                PhotoTrace.Mark("viewer:preview-source", e.Photo.FilePath, "gallery-thumbnail");
                using var skBmp = SkiaCodec.FromBitmap(e.Photo.GalleryThumbnail);
                imgPreview = SkiaCodec.ToSKImage(skBmp);
            }
            else
            {
                PhotoTrace.Mark("viewer:preview-source", e.Photo.FilePath, "preview-provider");
                var previewHeight = Math.Min(Math.Min(DrawingArea.Height, e.Metadata.Height), 700);
                imgPreview = await Core.PreviewProvider.GetPreviewAsync(e.Metadata, previewHeight, token);
            }
            hasPreview = imgPreview is not null;

            if (token.IsCancellationRequested)
            {
                HandleCancelLoading();
                return;
            }

            if (hasPreview)
            {
                var prevWidth = imgPreview?.Width ?? (int)e.Metadata.Width;
                var prevHeight = imgPreview?.Height ?? (int)e.Metadata.Height;
                BitmapSize = new(prevWidth, prevHeight);

                if (token.IsCancellationRequested)
                {
                    HandleCancelLoading();
                    return;
                }
            }
        }
        catch
        {
            HandleCancelLoading();
        }

        if (hasPreview)
        {
            lock (_lock)
            {
                SKImageRef.Set(ref _imgSource, imgPreview);

                var desiredSrcZoomFactor = CalculateZoomFactor(ZoomMode, e.Metadata.Width, e.Metadata.Height);

                var previewToSrcScale = GetPreviewToSourceScale(BitmapSize, e.Metadata);
                var previewZoomFactor = desiredSrcZoomFactor * previewToSrcScale;

                SetZoomFactor(previewZoomFactor, false);
            }
        }

        PhotoTrace.Mark("viewer:preview-done", e.Photo.FilePath,
            hasPreview ? $"{BitmapSize.Width}x{BitmapSize.Height}" : "no-preview");

        _isPreviewing.Set(hasPreview);
        OnPhotoLoading(e);

        void HandleCancelLoading()
        {
            hasPreview = false;
            imgPreview?.Dispose();
            imgPreview = null;
        }
    }

    private static double GetPreviewToSourceScale(Size previewSize, PhotoMetadata meta)
    {
        if (previewSize.Width <= 0 || previewSize.Height <= 0) return 1;
        if (meta.Width == 0 || meta.Height == 0) return 1;

        var widthScale = meta.Width / previewSize.Width;
        var heightScale = meta.Height / previewSize.Height;

        return Math.Max(1, Math.Min(widthScale, heightScale));
    }

    private async Task HandlePhotoLoadedAsync(PhotoLoadingEventArgs e)
    {
        if (!ShouldLoadFullResolution) return;

        var prevSize = BitmapSize;

        SKImage? imgFrame = null;
        AnimatorImpl? animator = null;
        var hasSource = false;

        SKImage? hdrRawToRetain = null;

        SKImage? editedFrame = null;

        try
        {
            if (e.Photo.Error is not null)
            {
                HandleCancelLoaded(false);
                OnPhotoLoading(e);
                return;
            }

            if (e.CancelToken.IsCancellationRequested)
            {
                HandleCancelLoaded(true);
                return;
            }

            if (e.Photo.Bitmap is not null)
            {
                if (e.Photo.Bitmap is SkiaVectorSource)
                {
                    PhotoTrace.Mark("viewer:loaded-source", e.Photo.FilePath, "vector");
                    hasSource = true;
                }
                else if (e.Photo.Bitmap is AnimatorImpl skAnimator)
                {
                    PhotoTrace.Mark("viewer:loaded-source", e.Photo.FilePath,
                        $"animator ({skAnimator.GetType().Name})");

                    BitmapSize = e.Photo.Size;

                    animator = skAnimator;
                    hasSource = true;
                }
                else
                {
                    PhotoTrace.Mark("viewer:loaded-source", e.Photo.FilePath, "single-frame");

                    BitmapSize = e.Photo.Size;

                    var frameToLoad = (uint)Math.Max(0, e.Photo.FrameIndex);
                    imgFrame = await e.Photo.GetFrameAsync(frameToLoad);

                    SKImage? imgFrameColored;
                    using (e.Photo.PinBitmap())
                    {
                        imgFrameColored = await ApplySkiaColorSpaceAsync(imgFrame, e.Photo);
                    }

                    var ownsFrame = imgFrame is not null && !e.Photo.IsClipboard
                        && e.Photo.ReleaseFrame(imgFrame);

                    if (imgFrameColored is not null)
                    {
                        PhotoTrace.Mark("viewer:color-managed", e.Photo.FilePath,
                            $"applied (hdrToneMap={Core.Config.EnableHdrToneMapping && e.Photo.Metadata.IsHdr}, srcProfile={(string.IsNullOrEmpty(e.Photo.Metadata.ColorProfileName) ? "none" : e.Photo.Metadata.ColorProfileName)})");

                        if (ownsFrame && _liveHdrToneMapping && e.Photo.Metadata.IsHdr)
                        {
                            hdrRawToRetain = imgFrame;
                        }
                        else if (ownsFrame)
                        {
                            imgFrame?.Dispose();
                        }

                        imgFrame = imgFrameColored;
                    }
                    else
                    {
                        PhotoTrace.Mark("viewer:color-managed", e.Photo.FilePath, "skipped");

                        if (!ownsFrame && !e.Photo.IsClipboard) imgFrame = null;
                    }

                    hasSource = !imgFrame.IsDisposed();
                    if (hasSource) editedFrame = await BuildEditedFrameAsync(imgFrame);
                }
            }

            if (e.CancelToken.IsCancellationRequested)
            {
                HandleCancelLoaded(true);
                return;
            }

            CancelPreview();

            lock (_lock)
            {
                BitmapSize = editedFrame is not null ? new Size(editedFrame.Width, editedFrame.Height) : e.Photo.Size;

                if (editedFrame is not null && PhotoOrientation.SwapsAxes) prevSize = new Size(prevSize.Height, prevSize.Width);

                if (hasSource)
                {
                    _firstDrawTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                    if (e.Photo.Bitmap is SkiaVectorSource vectorSource)
                    {
                        HandleVectorPhotoLoaded(vectorSource);
                    }
                    else
                    {
                        _isFirstDraw.SetTrue();
                        SKImageRef.Set(ref _imgSource, imgFrame);
                        SKImageRef.Set(ref _imgRender, editedFrame);
                        editedFrame = null;

                        SKImageRef.Set(ref _imgHdrSource, hdrRawToRetain);
                    }

                    if (animator is not null)
                    {
                        _animator?.FrameChanged -= Animator_FrameChanged;
                        _animator = animator;
                        _animator.FrameChanged += Animator_FrameChanged;
                        StartAnimator();
                    }

                    if (_isPreviewing
                        && _zooming.IsManual
                        && ZoomMode != ZoomMode.LockZoom)
                    {
                        _isPreviewing.SetFalse();

                        var diffRatio = new Size(
                            prevSize.Width / BitmapSize.Width,
                            prevSize.Height / BitmapSize.Height);

                        var newSrcRect = SrcRect;
                        newSrcRect = newSrcRect.WithX(newSrcRect.X / diffRatio.Width);
                        newSrcRect = newSrcRect.WithY(newSrcRect.Y / diffRatio.Height);
                        newSrcRect = newSrcRect.WithWidth(newSrcRect.Width / diffRatio.Width);
                        newSrcRect = newSrcRect.WithHeight(newSrcRect.Height / diffRatio.Height);

                        SrcRect = newSrcRect.Normalize();
                        _logicalSrcPoint = SrcRect.Position;
                        _zooming.Factor *= diffRatio.Width;
                        _zooming.ZoomedPoint = new();

                        ZoomChanged?.Invoke(this, new ViewerZoomEventArgs()
                        {
                            ZoomFactor = _zooming.Factor,
                            IsManualZoom = false,
                            IsZoomModeChange = false,
                            IsPreviewingImage = _isPreviewing,
                            ChangeSource = ZoomChangeSource.Unknown,
                        });

                        Refresh(false);
                    }
                    else
                    {
                        _isPreviewing.SetFalse();

                        if (ZoomMode == ZoomMode.LockZoom && _lockZoomSavedSrcPoint is not null)
                        {
                            _logicalSrcPoint = _lockZoomSavedSrcPoint.Value;
                            _lockZoomSavedSrcPoint = null;
                        }

                        if (_loadingOptions.ResetZoom)
                        {
                            SetZoomMode();
                            InvalidateVisual();
                        }
                        else
                        {
                            Refresh(false);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            e.Photo.Error = ex;
            HandleCancelLoaded(false);
        }
        finally
        {
            if (SourceKind != PhotoSource.VectorRenderer)
            {
                SourceKind = hasSource ? PhotoSource.Native : PhotoSource.None;
            }

            OnPhotoLoading(e);
            OnPhotoFrameChanged();
        }

        void HandleCancelLoaded(bool userCancelled)
        {
            lock (_lock)
            {
                _firstDrawTcs?.TrySetResult();
            }

            if (userCancelled) e.Photo.Unload();

            imgFrame?.Dispose();
            imgFrame = null;

            hdrRawToRetain?.Dispose();
            hdrRawToRetain = null;
            editedFrame?.Dispose();
            editedFrame = null;

            animator?.Dispose();
            animator = null;

            Refresh(userCancelled);
        }
    }

    private void Animator_FrameChanged(AnimatorImpl sender, AnimatorFrameChangedEventArgs e)
    {
        if (Core.IsBusy) return;

        if (sender is SvgAnimator)
        {
            OnPhotoFrameChanged(e);
            return;
        }

        var renderedFrame = sender.GetRenderedFrameBitmap(e.CurrentFrame);

        lock (_lock)
        {
            SourceKind = PhotoSource.Native;
            SKImageRef.Set(ref _imgSource, renderedFrame);
            SKImageRef.Set(ref _imgRender, renderedFrame, _imgSource);

            _firstDrawTcs?.TrySetResult();
        }

        if (!renderedFrame.IsDisposed()
            && (renderedFrame.Width != BitmapSize.Width || renderedFrame.Height != BitmapSize.Height))
        {
            RefitForFrameSize(new Size(renderedFrame.Width, renderedFrame.Height));
        }

        InvalidateVisual();
        OnRenderStateChanged();
        OnPhotoFrameChanged(e);
    }

    public void StartAnimator()
    {
        if (IsImageAnimating || _animator is null || SourceKind == PhotoSource.None)
            return;

        lock (_lock)
        {
            _animator.Play();

            OnPhotoFrameChanged();
        }
    }

    public void StopAnimator()
    {
        lock (_lock)
        {
            if (_animator is null) return;
            _animator.Pause();

            OnPhotoFrameChanged();
        }
    }

    public async Task ViewFrameAsync(uint frameIndex)
    {
        var photo = Photo;
        if (photo is null) return;

        if (IsImageAnimating) StopAnimator();

        if (photo.Bitmap is not AnimatorImpl && photo.FrameIndex == (int)frameIndex)
        {
            lock (_lock)
            {
                if (_imgSource?.Image.IsDisposed() == false) return;
            }
        }

        var imgFrame = await photo.GetFrameAsync(frameIndex);
        if (imgFrame is null) return;

        var ownsFrame = photo.Bitmap is not AnimatorImpl && !photo.IsClipboard;
        if (ownsFrame && !photo.ReleaseFrame(imgFrame)) return;

        SKImage? colored;
        using (photo.PinBitmap())
        {
            colored = await ApplySkiaColorSpaceAsync(imgFrame, photo);
        }

        if (colored is not null && ownsFrame) imgFrame.Dispose();
        var sourceImg = colored ?? imgFrame;

        var editedImg = await BuildEditedFrameAsync(sourceImg);

        lock (_lock)
        {
            if (!ReferenceEquals(Photo, photo))
            {
                if (ownsFrame || colored is not null) sourceImg.Dispose();
                editedImg?.Dispose();
                return;
            }

            _mipmapCache?.Dispose();
            _mipmapCache = null;

            SKImageRef.Set(ref _imgRender, editedImg);
            SKImageRef.Set(ref _imgSource, sourceImg);
            _isFirstDraw.SetTrue();
        }

        var shownImg = editedImg ?? sourceImg;
        var frameSize = shownImg.IsDisposed()
            ? photo.Size
            : new Size(shownImg.Width, shownImg.Height);

        if (frameSize != BitmapSize)
        {
            RefitForFrameSize(frameSize);
            InvalidateVisual();
        }
        else
        {
            Refresh(resetZoom: _loadingOptions.ResetZoom);
        }

        OnPhotoFrameChanged();
    }

}
