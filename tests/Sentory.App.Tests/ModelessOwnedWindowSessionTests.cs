using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

namespace Sentory.App.Tests;

public sealed class ModelessOwnedWindowSessionTests
{
    [Fact]
    public void KeepsOwnerWindowEnabledWhileBlockingItsContent()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var ownerContent = new Grid();
                var owner = CreateOffscreenWindow(showInTaskbar: true);
                owner.Content = ownerContent;
                owner.Show();
                var popup = CreateOffscreenWindow(showInTaskbar: false);
                popup.Owner = owner;

                var closed = ModelessOwnedWindowSession.ShowAsync(
                    popup,
                    blockOwnerInput: true);

                Assert.True(owner.IsEnabled);
                Assert.True(owner.ShowInTaskbar);
                Assert.False(ownerContent.IsEnabled);
                Assert.False(popup.ShowInTaskbar);

                popup.Close();
                closed.GetAwaiter().GetResult();

                Assert.True(owner.IsEnabled);
                Assert.True(ownerContent.IsEnabled);
                owner.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Window CreateOffscreenWindow(bool showInTaskbar) => new()
    {
        ShowInTaskbar = showInTaskbar,
        ShowActivated = false,
        WindowStyle = WindowStyle.None,
        Width = 1,
        Height = 1,
        Left = -10000,
        Top = -10000
    };
}
