using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class SlackDropPassThroughState
{
    private SlackDropTarget? _target;
    private string[] _paths = [];

    public bool IsActive => _target is not null;

    public void Observe(
        SlackDropTarget target,
        IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(paths);
        _target = target;
        _paths = paths.ToArray();
    }

    public bool TryTakeCompleted(
        bool leftMouseButtonDown,
        out SlackDropTarget target,
        out IReadOnlyList<string> paths)
    {
        target = null!;
        paths = [];
        if (leftMouseButtonDown || _target is null)
        {
            return false;
        }

        target = _target;
        paths = _paths;
        Reset();
        return true;
    }

    public void Reset()
    {
        _target = null;
        _paths = [];
    }

    public void Cancel() => Reset();
}
