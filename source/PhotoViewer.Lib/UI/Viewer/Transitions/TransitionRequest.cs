using System.Threading.Tasks;

namespace PhotoViewer.UI.Viewer.Transitions;

public sealed class TransitionRequest(TransitionEffect effect, int durationMs, int direction)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TransitionEffect Effect { get; } = effect;

    public int DurationMs { get; } = durationMs;

    public int Direction { get; } = direction;

    public Task Completion => _completion.Task;

    internal void Complete() => _completion.TrySetResult();

}
