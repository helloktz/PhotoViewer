using System.Threading.Tasks;
using System.Windows.Input;

namespace PhotoViewer.Common.Commands;

public interface IPhCommand : ICommand
{

    bool IsAsync { get; }

    Task ExecuteAsync(object? parameter);

}
