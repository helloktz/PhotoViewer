using PhotoViewer.Common.Types;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public interface IToolControl : ITool
{
    bool ITool.IsHosted => true;

    string ToolName { get; }

    ResxIconId? ToolIcon => null;

    bool HasSettingsUI { get; }

    Task ShowSettingsWindowAsync() => Task.CompletedTask;

}
