
using Avalonia.Controls;
using PhotoViewer.Common.Types;
using System;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public interface IWindowColorProfileProvider : IDisposable
{

    event TEventHandler<IWindowColorProfileProvider, ColorProfileChangedEventArgs>? Changed;

    string ProfilePath { get; }

    bool IsHdr { get; }

    bool IsInitialized { get; }

    void Initialize(Window window);

    Task<byte[]?> ReadColorProfileDataAsync();

}

public class ColorProfileChangedEventArgs(string profilePath, bool isHdr) : EventArgs
{

    public string ProfilePath { get; set; } = profilePath;

    public bool IsHdr { get; set; } = isHdr;
}
