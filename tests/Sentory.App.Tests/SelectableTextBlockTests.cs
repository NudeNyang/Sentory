using System.Runtime.ExceptionServices;
using System.Windows;

namespace Sentory.App.Tests;

public sealed class SelectableTextBlockTests
{
    [Fact]
    public void ReusesCharacterLayoutDuringRepeatedSelectionUpdates()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new SelectableTextBlock
                {
                    Text = string.Join(' ', Enumerable.Repeat("long-link-title", 300)),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13
                };
                control.Measure(new Size(320, 2000));
                control.Arrange(new Rect(0, 0, 320, control.DesiredSize.Height));

                for (var index = 0; index < 30; index++)
                {
                    _ = control.GetTextInsertionIndex(new Point(10 + index, 12));
                }

                Assert.Equal(1, control.CharacterLayoutBuildCount);
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
}
