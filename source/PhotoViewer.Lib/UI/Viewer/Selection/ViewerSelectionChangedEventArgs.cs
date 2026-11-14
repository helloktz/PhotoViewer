
using Avalonia;
using System;

namespace PhotoViewer.UI.Viewer;

public class ViewerSelectionChangedEventArgs(Rect clientSelection, Rect sourceSelection) : EventArgs
{

    public Rect ClientSelection => clientSelection;

    public Rect SourceSelection => sourceSelection;

}
