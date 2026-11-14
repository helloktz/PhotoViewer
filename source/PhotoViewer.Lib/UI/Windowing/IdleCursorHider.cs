using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;

namespace PhotoViewer.UI.Windowing;

public sealed class IdleCursorHider : PhDisposable
{

    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(3);

    private static readonly Cursor _hiddenCursor = new(StandardCursorType.None);

    private readonly TopLevel _topLevel;
    private readonly InputElement[] _targets;
    private readonly List<(InputElement Target, Cursor? Cursor, bool WasSet)> _savedCursors = [];
    private readonly DispatcherTimer _timer;
    private bool _isRunning;
    private bool _isHidden;

    public IdleCursorHider(TopLevel topLevel, params InputElement[] cursorOwners)
        : this(topLevel, DefaultIdleTimeout, cursorOwners)
    { }

    public IdleCursorHider(TopLevel topLevel, TimeSpan idleTimeout, params InputElement[] cursorOwners)
    {
        _topLevel = topLevel;

        _targets = new InputElement[cursorOwners.Length + 1];
        _targets[0] = topLevel;
        cursorOwners.CopyTo(_targets, 1);

        _timer = new DispatcherTimer(idleTimeout, DispatcherPriority.Background, (_, _) => HideCursor());
    }

    public void Start()
    {
        if (IsDisposed || _isRunning) return;
        _isRunning = true;

        _topLevel.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
        _topLevel.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        _topLevel.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        _topLevel.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel, true);

        RestartTimer();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _timer.Stop();

        _topLevel.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _topLevel.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _topLevel.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _topLevel.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);

        ShowCursor();
    }

    public void HideCursor()
    {
        _timer.Stop();
        if (_isHidden) return;
        _isHidden = true;

        _savedCursors.Clear();
        foreach (var target in _targets)
        {
            var wasSet = target.IsSet(InputElement.CursorProperty);
            _savedCursors.Add((target, wasSet ? target.Cursor : null, wasSet));
        }

        foreach (var item in _savedCursors)
        {
            item.Target.Cursor = _hiddenCursor;
        }
    }

    public void ShowCursor()
    {
        if (!_isHidden) return;
        _isHidden = false;

        foreach (var item in _savedCursors)
        {
            if (item.WasSet)
            {
                item.Target.Cursor = item.Cursor;
            }
            else
            {
                item.Target.ClearValue(InputElement.CursorProperty);
            }
        }

        _savedCursors.Clear();
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        Stop();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e) => HandlePointerActivity(e);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) => HandlePointerActivity(e);

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => HandlePointerActivity(e);

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) => HandlePointerActivity(e);

    private void HandlePointerActivity(PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch) return;

        ShowCursor();
        RestartTimer();
    }

    private void RestartTimer()
    {
        if (!_isRunning) return;

        _timer.Stop();
        _timer.Start();
    }

}
