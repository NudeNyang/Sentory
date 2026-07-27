using System.Runtime.InteropServices;

namespace Sentory.Platform.Windows.Interop;

public interface IExplorerSelectionReader
{
    IReadOnlyList<string> ReadSelectedFiles(nint explorerWindow);
}

public sealed class ExplorerSelectionReader : IExplorerSelectionReader
{
    public IReadOnlyList<string> ReadSelectedFiles(nint explorerWindow)
    {
        if (explorerWindow == nint.Zero)
        {
            return [];
        }

        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return [];
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return [];
            }

            dynamic shellDispatch = shell;
            windows = shellDispatch.Windows();
            dynamic windowCollection = windows;
            var count = Convert.ToInt32(windowCollection.Count);
            for (var index = 0; index < count; index++)
            {
                object? window = null;
                try
                {
                    window = windowCollection.Item(index);
                    if (window is null ||
                        Convert.ToInt64(((dynamic)window).HWND) !=
                        explorerWindow.ToInt64())
                    {
                        continue;
                    }

                    return ReadSelectedFromWindow(window!);
                }
                finally
                {
                    Release(window);
                }
            }

            object? location = null;
            object? root = null;
            var desktopHwnd = 0;
            object? desktop = null;
            try
            {
                desktop = windowCollection.FindWindowSW(
                    ref location,
                    ref root,
                    8,
                    ref desktopHwnd,
                    1);
                if (desktop is not null &&
                    NativeMethods.GetAncestor(
                        new nint(desktopHwnd),
                        NativeMethods.GetAncestorRoot) == explorerWindow)
                {
                    return ReadSelectedFromWindow(desktop);
                }
            }
            finally
            {
                Release(desktop);
                Release(root);
                Release(location);
            }
        }
        catch (Exception)
        {
            // Explorer의 COM 자동화가 잠시 사용할 수 없으면 감지를 생략한다.
        }
        finally
        {
            Release(windows);
            Release(shell);
        }

        return [];
    }

    private static IReadOnlyList<string> ReadSelectedFromWindow(
        object window)
    {
        object? document = null;
        object? selectedItems = null;
        try
        {
            dynamic dispatch = window;
            document = dispatch.Document;
            if (document is null)
            {
                return [];
            }

            selectedItems = ((dynamic)document).SelectedItems();
            if (selectedItems is null)
            {
                return [];
            }

            dynamic selected = selectedItems;
            var selectedCount = Convert.ToInt32(selected.Count);
            var paths = new List<string>(selectedCount);
            for (var itemIndex = 0;
                 itemIndex < selectedCount;
                 itemIndex++)
            {
                object? item = null;
                try
                {
                    item = selected.Item(itemIndex);
                    if (item is null)
                    {
                        continue;
                    }

                    var path = Convert.ToString(((dynamic)item).Path);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    Release(item);
                }
            }

            return paths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            Release(selectedItems);
            Release(document);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
