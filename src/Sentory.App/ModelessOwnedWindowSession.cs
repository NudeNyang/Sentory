using System.Windows;

namespace Sentory.App;

internal static class ModelessOwnedWindowSession
{
    public static Task ShowAsync(
        Window window,
        bool blockOwnerInput = false)
    {
        ArgumentNullException.ThrowIfNull(window);

        var closed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerContent = blockOwnerInput
            ? window.Owner?.Content as UIElement
            : null;
        var ownerContentWasEnabled = ownerContent?.IsEnabled == true;
        var activationWindows = EnumerateOwnerChain(window).ToArray();

        void RedirectActivation(object? sender, EventArgs args)
        {
            if (!window.IsLoaded || window.IsActive)
            {
                return;
            }

            _ = window.Dispatcher.BeginInvoke(() =>
            {
                if (window.IsLoaded)
                {
                    window.Activate();
                }
            });
        }

        void RestoreOwnerState()
        {
            foreach (var owner in activationWindows)
            {
                owner.Activated -= RedirectActivation;
            }

            if (ownerContent is not null)
            {
                ownerContent.IsEnabled = ownerContentWasEnabled;
            }
        }

        foreach (var owner in activationWindows)
        {
            owner.Activated += RedirectActivation;
        }

        if (ownerContent is not null)
        {
            ownerContent.IsEnabled = false;
        }

        window.Closed += (_, _) =>
        {
            RestoreOwnerState();
            closed.TrySetResult(true);
        };

        try
        {
            window.Show();
            window.Activate();
        }
        catch
        {
            RestoreOwnerState();
            throw;
        }

        return closed.Task;
    }

    private static IEnumerable<Window> EnumerateOwnerChain(Window window)
    {
        var owner = window.Owner;
        while (owner is not null)
        {
            yield return owner;
            owner = owner.Owner;
        }
    }
}
