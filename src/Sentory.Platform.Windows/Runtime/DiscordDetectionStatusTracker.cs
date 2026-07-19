using Sentory.Core;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class DiscordDetectionStatusTracker
{
    private readonly object _gate = new();
    private CaptureRuntimeState _state = CaptureRuntimeState.Connecting;
    private bool _published;

    public event EventHandler<CaptureRuntimeStatus>? StatusChanged;

    public CaptureRuntimeState Current
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool Publish(CaptureRuntimeState state)
    {
        lock (_gate)
        {
            if (_published && _state == state)
            {
                return false;
            }

            _state = state;
            _published = true;
        }

        StatusChanged?.Invoke(
            this,
            new CaptureRuntimeStatus(
                SourceApp.Discord,
                state,
                DateTimeOffset.UtcNow));
        return true;
    }
}
