using System.Windows;
using System.Windows.Threading;

namespace Sentory.App;

internal static class OwnedPopupDismissBehavior
{
    public static void Enable(
        Window window,
        Func<bool>? canDismiss = null)
    {
        var closeRequested = false;
        window.Closing += (_, _) => closeRequested = true;
        window.Deactivated += (_, _) =>
        {
            if (!window.IsLoaded || closeRequested ||
                canDismiss?.Invoke() == false)
            {
                return;
            }

            closeRequested = true;
            var owner = window.Owner;
            _ = window.Dispatcher.BeginInvoke(
                () =>
                {
                    var returnFocusToOwner = owner?.IsActive == true;
                    if (window.IsLoaded)
                    {
                        window.Close();
                    }

                    if (returnFocusToOwner && owner?.IsVisible == true)
                    {
                        owner.Activate();
                    }
                },
                DispatcherPriority.Background);
        };
    }
}
