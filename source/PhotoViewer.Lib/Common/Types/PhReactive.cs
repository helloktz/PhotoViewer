using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;

namespace PhotoViewer.Common.Types;

public partial class PhReactive : INotifyPropertyChanged
{
    private readonly Lock _eventLock = new();
    private List<PropertyChangedEventHandler> _propertyChangedEvents = [];
    private event PropertyChangedEventHandler? _propertyChanged;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add
        {
            if (value is not null)
            {
                lock (_eventLock)
                {
                    _propertyChanged += value;
                    _propertyChangedEvents.Add(value);
                }
            }
        }
        remove
        {
            if (value is not null)
            {
                lock (_eventLock)
                {
                    _propertyChanged -= value;
                    _propertyChangedEvents.Remove(value);
                }
            }
        }
    }

    [JsonIgnore]
    public bool SuspendReactivity { get; set; } = false;

    public bool OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        return OnPropertyChanged(null, null, propertyName);
    }

    public bool OnPropertyChanged(object? value, object? oldValue, [CallerMemberName] string? propertyName = null)
    {
        if (SuspendReactivity) return false;

        _propertyChanged?.Invoke(this, new ReactiveEventArgs(propertyName, value, oldValue));
        return true;
    }

    public void CleanUpPropertyChangedEvents()
    {
        lock (_eventLock)
        {
            foreach (var eventHandler in _propertyChangedEvents)
            {
                _propertyChanged -= eventHandler;
            }
            _propertyChangedEvents.Clear();
        }
    }

    public void WithNoReactive(Action fn)
    {
        SuspendReactivity = true;
        fn();
        SuspendReactivity = false;
    }

}

public class ReactiveEventArgs : PropertyChangedEventArgs
{

    public bool HasValues => Value is not null && OldValue is not null;

    public object? Value { get; set; }
    public object? OldValue { get; set; }

    public ReactiveEventArgs(
        string? propertyName,
        object? value = null,
        object? oldValue = null) : base(propertyName)
    {
        Value = value;
        OldValue = oldValue;
    }
}
