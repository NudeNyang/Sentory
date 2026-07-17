using System.Runtime.InteropServices;
using System.Windows.Automation;
using Sentory.Core;

namespace Sentory.Platform.Windows.Interop;

public sealed class KakaoInputValueVerifier
{
    public async Task<bool> ContainsClipboardUrlsAsync(
        nint inputWindow,
        string clipboardText,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var clipboardUrls = UrlExtractor.Extract(clipboardText);
        if (inputWindow == nint.Zero || clipboardUrls.Count == 0)
        {
            return false;
        }

        var verifyTask = Task.Run(
            () => Verify(inputWindow, clipboardUrls),
            CancellationToken.None);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(verifyTask, timeoutTask);
        if (completed != verifyTask)
        {
            return false;
        }

        return await verifyTask;
    }

    private static bool Verify(
        nint inputWindow,
        IReadOnlyList<NormalizedUrl> clipboardUrls)
    {
        try
        {
            var element = AutomationElement.FromHandle(inputWindow);
            var inputText = ReadText(element);
            if (string.IsNullOrEmpty(inputText))
            {
                return false;
            }

            var inputUrls = UrlExtractor.Extract(inputText)
                .Select(url => url.Value)
                .ToHashSet(StringComparer.Ordinal);
            return clipboardUrls.All(url => inputUrls.Contains(url.Value));
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static string? ReadText(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(
                ValuePattern.Pattern,
                out var valuePattern))
        {
            return ((ValuePattern)valuePattern).Current.Value;
        }

        if (element.TryGetCurrentPattern(
                TextPattern.Pattern,
                out var textPattern))
        {
            return ((TextPattern)textPattern)
                .DocumentRange
                .GetText(-1);
        }

        return null;
    }
}
