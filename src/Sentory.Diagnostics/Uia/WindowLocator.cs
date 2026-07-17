using System.Diagnostics;
using System.IO;
using System.Text;
using Sentory.Diagnostics.Interop;

namespace Sentory.Diagnostics.Uia;

public static class WindowLocator
{
    public static IReadOnlyList<WindowInfo> FindVisibleTopLevelWindows(
        IReadOnlyList<string> processNames)
    {
        var requested = processNames
            .Select(NormalizeProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((windowHandle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(windowHandle))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(windowHandle, out var rawProcessId);
            if (rawProcessId == 0)
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById(checked((int)rawProcessId));
                if (!requested.Contains(process.ProcessName))
                {
                    return true;
                }

                var className = new StringBuilder(256);
                NativeMethods.GetClassName(windowHandle, className, className.Capacity);

                string version;
                try
                {
                    version = process.MainModule?.FileVersionInfo.FileVersion ?? "unknown";
                }
                catch
                {
                    version = "unknown";
                }

                windows.Add(new WindowInfo(
                    process.ProcessName,
                    process.Id,
                    version,
                    $"0x{windowHandle.ToInt64():X}",
                    Privacy.SafeIdentifier(className.ToString()),
                    Privacy.LengthBucket(NativeMethods.GetWindowTextLength(windowHandle))));
            }
            catch (ArgumentException)
            {
                // The process exited while windows were being enumerated.
            }
            catch (InvalidOperationException)
            {
                // The process exited while windows were being enumerated.
            }

            return true;
        }, nint.Zero);

        return windows
            .OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.ProcessId)
            .ThenBy(window => window.WindowHandle, StringComparer.Ordinal)
            .ToArray();
    }

    internal static nint ParseHandle(WindowInfo window) =>
        new(Convert.ToInt64(window.WindowHandle[2..], 16));

    private static string NormalizeProcessName(string value) =>
        Path.GetFileNameWithoutExtension(value.Trim());
}
