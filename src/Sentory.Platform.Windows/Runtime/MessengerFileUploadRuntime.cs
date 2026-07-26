using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Threading;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class MessengerFileUploadRuntime : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ClosedDialogGracePeriod =
        TimeSpan.FromMilliseconds(400);

    private readonly INativeWindowApi _native;
    private readonly IDiscordWindowApi _discordWindows;
    private readonly IKakaoDropWindowApi _windows;
    private readonly DiscordCaptureRuntime _discordRuntime;
    private readonly KakaoCaptureRuntime _kakaoRuntime;
    private readonly Action<string, string>? _diagnostic;
    private readonly WindowsFileDialogSelectionReader _selectionReader = new();
    private readonly FileDialogDecisionTracker _decisions = new();
    private readonly FileDialogWinEventHook _eventHook;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<nint, FileDialogSession> _sessions = [];
    private bool _started;
    private bool _tickRunning;
    private bool _disposed;

    public MessengerFileUploadRuntime(
        DiscordCaptureRuntime discordRuntime,
        KakaoCaptureRuntime kakaoRuntime,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        _native = native;
        _discordWindows = native;
        _windows = native;
        _discordRuntime = discordRuntime;
        _kakaoRuntime = kakaoRuntime;
        _diagnostic = diagnostic;
        _eventHook = new FileDialogWinEventHook(native, _decisions);
        _timer = new DispatcherTimer(
            PollInterval,
            DispatcherPriority.Background,
            OnTick,
            Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _eventHook.Start();
        _timer.Start();
        _started = true;
    }

    private async void OnTick(object? sender, EventArgs eventArgs)
    {
        if (_tickRunning || _disposed)
        {
            return;
        }

        _tickRunning = true;
        try
        {
            await InspectDialogsAsync();
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke(
                "manual-file-upload-observer-failed",
                $"type={exception.GetType().Name}");
        }
        finally
        {
            _tickRunning = false;
        }
    }

    private async Task InspectDialogsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var visibleDialogs = _windows.EnumerateTopLevelWindows()
            .Where(window =>
                _windows.IsWindowVisible(window) &&
                string.Equals(
                    _native.GetClassName(window),
                    FileDialogWinEventHook.DialogClassName,
                    StringComparison.Ordinal))
            .ToHashSet();

        foreach (var dialog in visibleDialogs)
        {
            if (!_sessions.TryGetValue(dialog, out var session))
            {
                session = TryCreateSession(dialog);
                if (session is null)
                {
                    continue;
                }

                _sessions.Add(dialog, session);
                _diagnostic?.Invoke(
                    "manual-file-dialog-opened",
                    $"source={session.SourceApp} handle={dialog.ToInt64():X}");
            }

            session.MissingSince = null;
            var selectedPaths = await Task.Run(() =>
                _selectionReader.ReadSelectedImagePaths(dialog));
            if (selectedPaths.Count > 0)
            {
                session.SelectedPaths = selectedPaths;
            }
        }

        foreach (var session in _sessions.Values.ToArray())
        {
            if (visibleDialogs.Contains(session.DialogWindow))
            {
                continue;
            }

            var decision = _decisions.Take(session.DialogWindow);
            if (decision != FileDialogDecision.Unknown)
            {
                _sessions.Remove(session.DialogWindow);
                if (decision == FileDialogDecision.Accepted &&
                    session.SelectedPaths.Count > 0)
                {
                    await DispatchAcceptedSelectionAsync(session);
                }
                else
                {
                    _diagnostic?.Invoke(
                        "manual-file-dialog-ignored",
                        $"source={session.SourceApp} decision={decision} selected={session.SelectedPaths.Count}");
                }

                continue;
            }

            session.MissingSince ??= now;
            if (now - session.MissingSince < ClosedDialogGracePeriod)
            {
                continue;
            }

            _sessions.Remove(session.DialogWindow);
            _diagnostic?.Invoke(
                "manual-file-dialog-ignored",
                $"source={session.SourceApp} decision={FileDialogDecision.Unknown} selected={session.SelectedPaths.Count}");
        }
    }

    private FileDialogSession? TryCreateSession(nint dialog)
    {
        var owner = _native.GetOwnerWindow(dialog);
        var ownerRoot = _native.GetRootWindow(owner);
        if (ownerRoot == nint.Zero)
        {
            return null;
        }

        var processId = _native.GetProcessId(ownerRoot);
        var processName = _native.GetProcessName(processId);
        if (string.Equals(
                processName,
                DiscordContextValidator.DiscordProcessName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                _native.GetClassName(ownerRoot),
                DiscordContextValidator.MainWindowClassName,
                StringComparison.Ordinal))
        {
            var renderer = _discordWindows.FindDescendant(
                ownerRoot,
                DiscordContextValidator.RendererClassName);
            if (renderer == nint.Zero ||
                _native.GetProcessId(renderer) != processId)
            {
                return null;
            }

            return new FileDialogSession(
                dialog,
                SourceApp.Discord,
                new DiscordDropTarget(
                    ownerRoot,
                    renderer,
                    processId,
                    _native.GetWindowBounds(ownerRoot)),
                null);
        }

        if (!string.Equals(
                processName,
                KakaoContextValidator.KakaoProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var input = _windows.FindDescendant(
            ownerRoot,
            KakaoContextValidator.InputClassName,
            KakaoContextValidator.InputControlId);
        if (input == nint.Zero ||
            !_native.HasDescendant(
                ownerRoot,
                KakaoContextValidator.MessageListClassName,
                KakaoContextValidator.MessageListControlId))
        {
            return null;
        }

        return new FileDialogSession(
            dialog,
            SourceApp.KakaoTalk,
            null,
            new KakaoDropTarget(
                ownerRoot,
                input,
                processId,
                _native.GetWindowBounds(ownerRoot),
                _native.GetWindowBounds(input)));
    }

    private async Task DispatchAcceptedSelectionAsync(FileDialogSession session)
    {
        if (session.DiscordTarget is not null)
        {
            var result = await _discordRuntime.RegisterManualFileUploadAsync(
                session.DiscordTarget,
                session.SelectedPaths);
            _diagnostic?.Invoke(
                "discord-manual-file-upload",
                $"result={result} files={session.SelectedPaths.Count}");
            return;
        }

        if (session.KakaoTarget is not null)
        {
            var result = await _kakaoRuntime.CaptureManualSelectedFilesAsync(
                session.KakaoTarget,
                session.SelectedPaths);
            _diagnostic?.Invoke(
                "kakao-manual-file-upload",
                $"result={result} files={session.SelectedPaths.Count}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _eventHook.Dispose();
        _sessions.Clear();
    }

    private sealed class FileDialogSession(
        nint dialogWindow,
        SourceApp sourceApp,
        DiscordDropTarget? discordTarget,
        KakaoDropTarget? kakaoTarget)
    {
        public nint DialogWindow { get; } = dialogWindow;
        public SourceApp SourceApp { get; } = sourceApp;
        public DiscordDropTarget? DiscordTarget { get; } = discordTarget;
        public KakaoDropTarget? KakaoTarget { get; } = kakaoTarget;
        public IReadOnlyList<string> SelectedPaths { get; set; } = [];
        public DateTimeOffset? MissingSince { get; set; }
    }
}

internal enum FileDialogDecision
{
    Unknown,
    Accepted,
    Cancelled
}

internal sealed class FileDialogDecisionTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<nint, FileDialogDecision> _decisions = [];

    public void Record(nint dialog, FileDialogDecision decision)
    {
        lock (_gate)
        {
            _decisions[dialog] = decision;
        }
    }

    public FileDialogDecision Take(nint dialog)
    {
        lock (_gate)
        {
            if (!_decisions.Remove(dialog, out var decision))
            {
                return FileDialogDecision.Unknown;
            }

            return decision;
        }
    }
}

internal sealed class FileDialogWinEventHook : IDisposable
{
    internal const string DialogClassName = "#32770";
    private const uint EventObjectInvoked = 0x8013;
    private const uint WineventOutOfContext = 0x0000;
    private const int OpenButtonControlId = 1;
    private const int CancelButtonControlId = 2;

    private readonly INativeWindowApi _native;
    private readonly FileDialogDecisionTracker _decisions;
    private readonly WinEventDelegate _callback;
    private nint _hook;

    public FileDialogWinEventHook(
        INativeWindowApi native,
        FileDialogDecisionTracker decisions)
    {
        _native = native;
        _decisions = decisions;
        _callback = OnWinEvent;
    }

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        _hook = SetWinEventHook(
            EventObjectInvoked,
            EventObjectInvoked,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (window == nint.Zero)
        {
            return;
        }

        var dialog = _native.GetRootWindow(window);
        if (dialog == nint.Zero ||
            !string.Equals(
                _native.GetClassName(dialog),
                DialogClassName,
                StringComparison.Ordinal))
        {
            return;
        }

        var controlId = _native.GetControlId(window);
        if (controlId == CancelButtonControlId)
        {
            _decisions.Record(dialog, FileDialogDecision.Cancelled);
        }
        else if (controlId == OpenButtonControlId)
        {
            _decisions.Record(dialog, FileDialogDecision.Accepted);
        }
    }

    public void Dispose()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        UnhookWinEvent(_hook);
        _hook = nint.Zero;
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventDelegate eventProc,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}

internal sealed class WindowsFileDialogSelectionReader
{
    private static readonly PropertyCondition FileNameCondition = new(
        AutomationElement.AutomationIdProperty,
        "1148");
    private static readonly PropertyCondition AddressCondition = new(
        AutomationElement.AutomationIdProperty,
        "1001");

    public IReadOnlyList<string> ReadSelectedImagePaths(nint dialog)
    {
        try
        {
            var root = AutomationElement.FromHandle(dialog);
            var rawSelections = new List<string>();
            var fileName = root.FindFirst(
                TreeScope.Descendants,
                FileNameCondition);
            AddElementValues(fileName, rawSelections);
            if (fileName is not null)
            {
                foreach (AutomationElement child in fileName.FindAll(
                             TreeScope.Descendants,
                             Condition.TrueCondition))
                {
                    AddElementValues(child, rawSelections);
                }
            }

            var selectedItems = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    SelectionItemPattern.IsSelectedProperty,
                    true));
            foreach (AutomationElement item in selectedItems)
            {
                AddElementValues(item, rawSelections);
            }

            var addressValues = new List<string>();
            var address = root.FindFirst(
                TreeScope.Descendants,
                AddressCondition);
            AddElementValues(address, addressValues);
            if (address is not null)
            {
                foreach (AutomationElement child in address.FindAll(
                             TreeScope.Descendants,
                             Condition.TrueCondition))
                {
                    AddElementValues(child, addressValues);
                }
            }

            return FileDialogPathResolver.Resolve(
                rawSelections,
                addressValues);
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (COMException)
        {
            return [];
        }
    }

    private static void AddElementValues(
        AutomationElement? element,
        ICollection<string> values)
    {
        if (element is null)
        {
            return;
        }

        Add(values, element.Current.Name);
        Add(values, element.Current.HelpText);
        Add(values, element.Current.ItemStatus);
        if (element.TryGetCurrentPattern(
                ValuePattern.Pattern,
                out var valuePattern))
        {
            Add(values, ((ValuePattern)valuePattern).Current.Value);
        }

    }

    private static void Add(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }
}

internal static partial class FileDialogPathResolver
{
    private static readonly Regex QuotedValueRegex = QuotedValue();

    public static IReadOnlyList<string> Resolve(
        IEnumerable<string> rawSelections,
        IEnumerable<string> addressValues,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var folders = addressValues
            .Select(TryResolveFolder)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawSelection in rawSelections)
        {
            foreach (var value in SplitSelection(rawSelection))
            {
                AddCandidate(value, null);
                foreach (var folder in folders)
                {
                    AddCandidate(value, folder);
                }
            }
        }

        return result.ToArray();

        void AddCandidate(string value, string? folder)
        {
            var trimmed = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            string path;
            try
            {
                path = Path.IsPathRooted(trimmed)
                    ? Path.GetFullPath(trimmed)
                    : folder is not null
                        ? Path.GetFullPath(Path.Combine(folder, trimmed))
                        : string.Empty;
            }
            catch (Exception exception)
                when (exception is ArgumentException or
                      NotSupportedException or
                      PathTooLongException)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(path) &&
                ClipboardImageCodec.HasSupportedImageExtension(path) &&
                fileExists(path))
            {
                result.Add(path);
            }
        }
    }

    private static IEnumerable<string> SplitSelection(string value)
    {
        var matches = QuotedValueRegex.Matches(value);
        if (matches.Count > 0)
        {
            return matches.Select(match => match.Groups[1].Value);
        }

        return [value];
    }

    private static string? TryResolveFolder(string value)
    {
        var normalized = value.Trim();
        foreach (var prefix in new[] { "주소:", "Address:" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..].Trim();
            }
        }

        if (Directory.Exists(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        var leaf = normalized
            .Split(['>', '\u203a'], StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return leaf?.ToLowerInvariant() switch
        {
            "문서" or "documents" => Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            "사진" or "pictures" => Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures),
            "바탕 화면" or "desktop" => Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory),
            "다운로드" or "downloads" => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"),
            _ => null
        };
    }

    [GeneratedRegex("\\\"([^\\\"]+)\\\"")]
    private static partial Regex QuotedValue();
}
