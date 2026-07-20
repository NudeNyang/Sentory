using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sentory.App;

internal static class OwnedPopupDismissBehavior
{
    public static void Enable(
        Window window,
        Func<bool>? canDismiss = null)
    {
        var closeRequested = false;
        Window? owner = null;
        MouseButtonEventHandler? ownerMouseDown = null;

        void DetachOwnerHandler()
        {
            if (owner is not null && ownerMouseDown is not null)
            {
                owner.RemoveHandler(
                    Mouse.PreviewMouseDownEvent,
                    ownerMouseDown);
            }

            owner = null;
            ownerMouseDown = null;
        }

        void AttachOwnerHandler()
        {
            DetachOwnerHandler();
            owner = window.Owner;
            if (owner is null)
            {
                return;
            }

            ownerMouseDown = (_, _) =>
            {
                if (!window.IsLoaded || closeRequested ||
                    canDismiss?.Invoke() == false)
                {
                    return;
                }

                closeRequested = true;
                _ = window.Dispatcher.BeginInvoke(
                    () =>
                    {
                        if (window.IsLoaded)
                        {
                            window.Close();
                        }
                    },
                    DispatcherPriority.Background);
            };
            owner.AddHandler(
                Mouse.PreviewMouseDownEvent,
                ownerMouseDown,
                handledEventsToo: true);
        }

        window.Loaded += (_, _) => AttachOwnerHandler();
        window.Closing += (_, _) => closeRequested = true;
        window.Closed += (_, _) => DetachOwnerHandler();
        if (window.IsLoaded)
        {
            AttachOwnerHandler();
        }
    }
}
