using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Threading;
using Accessibility;
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
        _eventHook = new FileDialogWinEventHook(
            native,
            _decisions,
            diagnostic);
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

                _decisions.Track(dialog);
                _sessions.Add(dialog, session);
                _diagnostic?.Invoke(
                    "manual-file-dialog-opened",
                    $"source={session.SourceApp} handle={dialog.ToInt64():X}");
            }

            session.MissingSince = null;
            var observation = await Task.Run(() =>
                _selectionReader.Read(dialog));
            if (observation.AddressValues.Count > 0)
            {
                session.AddressValues = observation.AddressValues;
            }

            if (observation.SelectedPaths.Count > 0)
            {
                session.SelectedPaths = observation.SelectedPaths;
            }
        }

        foreach (var session in _sessions.Values.ToArray())
        {
            if (visibleDialogs.Contains(session.DialogWindow))
            {
                continue;
            }

            session.MissingSince ??= now;
            if (now - session.MissingSince < ClosedDialogGracePeriod)
            {
                continue;
            }

            var snapshot = _decisions.TakeSnapshot(
                session.DialogWindow);
            var eventPaths = FileDialogPathResolver.Resolve(
                snapshot.RawSelections,
                session.AddressValues);
            if (eventPaths.Count > 0)
            {
                session.SelectedPaths = eventPaths;
            }

            var decision = FileDialogCompletionPolicy.Resolve(
                snapshot.Decision,
                snapshot.Decision == FileDialogDecision.Unknown
                    ? eventPaths.Count
                    : session.SelectedPaths.Count,
                snapshot.SelectedAt,
                now);
            if (decision != FileDialogDecision.Unknown)
            {
                _sessions.Remove(session.DialogWindow);
                _decisions.Untrack(session.DialogWindow);
                if (decision == FileDialogDecision.Accepted &&
                    session.SelectedPaths.Count > 0)
                {
                    _diagnostic?.Invoke(
                        "manual-file-dialog-accepted",
                        $"source={session.SourceApp} selected={session.SelectedPaths.Count} eventValues={snapshot.RawSelections.Count} addresses={session.AddressValues.Count}");
                    await DispatchAcceptedSelectionAsync(session);
                }
                else
                {
                    _diagnostic?.Invoke(
                        "manual-file-dialog-ignored",
                        $"source={session.SourceApp} decision={decision} selected={session.SelectedPaths.Count} eventValues={snapshot.RawSelections.Count} addresses={session.AddressValues.Count}");
                }

                continue;
            }

            _sessions.Remove(session.DialogWindow);
            _decisions.Untrack(session.DialogWindow);
            _diagnostic?.Invoke(
                "manual-file-dialog-ignored",
                $"source={session.SourceApp} decision={FileDialogDecision.Unknown} selected={session.SelectedPaths.Count} eventValues={snapshot.RawSelections.Count} addresses={session.AddressValues.Count}");
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
        foreach (var dialog in _sessions.Keys)
        {
            _decisions.Untrack(dialog);
        }

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
        public IReadOnlyList<string> AddressValues { get; set; } = [];
        public DateTimeOffset? MissingSince { get; set; }
    }
}

internal enum FileDialogDecision
{
    Unknown,
    Accepted,
    Cancelled
}

internal enum FileDialogSelectionChange
{
    Replace,
    Add,
    Remove
}

internal sealed record FileDialogDecisionSnapshot(
    FileDialogDecision Decision,
    IReadOnlyList<string> RawSelections,
    DateTimeOffset? SelectedAt);

internal sealed class FileDialogDecisionTracker
{
    private readonly object _gate = new();
    private readonly HashSet<nint> _trackedDialogs = [];
    private readonly Dictionary<nint, FileDialogDecision> _decisions = [];
    private readonly Dictionary<nint, HashSet<string>> _selections = [];
    private readonly Dictionary<nint, DateTimeOffset> _selectionTimes = [];

    public void Track(nint dialog)
    {
        lock (_gate)
        {
            _trackedDialogs.Add(dialog);
        }
    }

    public void Untrack(nint dialog)
    {
        lock (_gate)
        {
            _trackedDialogs.Remove(dialog);
            _decisions.Remove(dialog);
            _selections.Remove(dialog);
            _selectionTimes.Remove(dialog);
        }
    }

    public void Record(nint dialog, FileDialogDecision decision)
    {
        lock (_gate)
        {
            if (!_trackedDialogs.Contains(dialog))
            {
                return;
            }

            _decisions[dialog] = decision;
        }
    }

    public FileDialogDecision Take(nint dialog)
        => TakeSnapshot(dialog).Decision;

    public bool RecordSelection(
        nint dialog,
        FileDialogSelectionChange change,
        IEnumerable<string> values,
        DateTimeOffset observedAt)
    {
        lock (_gate)
        {
            if (!_trackedDialogs.Contains(dialog))
            {
                return false;
            }

            if (!_selections.TryGetValue(dialog, out var selections) ||
                change == FileDialogSelectionChange.Replace)
            {
                selections = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                _selections[dialog] = selections;
            }

            foreach (var value in values.Where(value =>
                         !string.IsNullOrWhiteSpace(value)))
            {
                if (change == FileDialogSelectionChange.Remove)
                {
                    selections.Remove(value.Trim());
                }
                else
                {
                    selections.Add(value.Trim());
                }
            }

            if (selections.Count > 0)
            {
                _selectionTimes[dialog] = observedAt;
            }
            else
            {
                _selectionTimes.Remove(dialog);
            }

            return true;
        }
    }

    public FileDialogDecisionSnapshot TakeSnapshot(nint dialog)
    {
        lock (_gate)
        {
            var decision = _decisions.Remove(dialog, out var recorded)
                ? recorded
                : FileDialogDecision.Unknown;
            var selections = _selections.Remove(dialog, out var values)
                ? values.ToArray()
                : [];
            var selectedAt = _selectionTimes.Remove(
                dialog,
                out var observedAt)
                ? observedAt
                : (DateTimeOffset?)null;
            return new FileDialogDecisionSnapshot(
                decision,
                selections,
                selectedAt);
        }
    }
}

internal static class FileDialogCompletionPolicy
{
    private static readonly TimeSpan ImplicitSelectionWindow =
        TimeSpan.FromSeconds(1);

    public static FileDialogDecision Resolve(
        FileDialogDecision explicitDecision,
        int selectedPathCount,
        DateTimeOffset? selectedAt,
        DateTimeOffset closedAt)
    {
        if (explicitDecision != FileDialogDecision.Unknown)
        {
            return explicitDecision;
        }

        return selectedPathCount > 0 &&
               selectedAt is not null &&
               closedAt >= selectedAt &&
               closedAt - selectedAt <= ImplicitSelectionWindow
            ? FileDialogDecision.Accepted
            : FileDialogDecision.Unknown;
    }
}

internal sealed class FileDialogWinEventHook : IDisposable
{
    internal const string DialogClassName = "#32770";
    private const uint EventObjectSelection = 0x8006;
    private const uint EventObjectValueChange = 0x800E;
    private const uint EventObjectInvoked = 0x8013;
    private const uint WineventOutOfContext = 0x0000;
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmLButtonUp = 0x0202;
    private const int OpenButtonControlId = 1;
    private const int CancelButtonControlId = 2;
    private const int FileNameControlId = 1148;

    private readonly INativeWindowApi _native;
    private readonly FileDialogDecisionTracker _decisions;
    private readonly Action<string, string>? _diagnostic;
    private readonly WinEventDelegate _callback;
    private readonly LowLevelHookDelegate _keyboardCallback;
    private readonly LowLevelHookDelegate _mouseCallback;
    private nint _invokedHook;
    private nint _selectionHook;
    private nint _keyboardHook;
    private nint _mouseHook;

    public FileDialogWinEventHook(
        INativeWindowApi native,
        FileDialogDecisionTracker decisions,
        Action<string, string>? diagnostic = null)
    {
        _native = native;
        _decisions = decisions;
        _diagnostic = diagnostic;
        _callback = OnWinEvent;
        _keyboardCallback = OnKeyboardInput;
        _mouseCallback = OnMouseInput;
    }

    public void Start()
    {
        if (_invokedHook != nint.Zero)
        {
            return;
        }

        _invokedHook = SetWinEventHook(
            EventObjectInvoked,
            EventObjectInvoked,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        _selectionHook = SetWinEventHook(
            EventObjectSelection,
            EventObjectValueChange,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        _keyboardHook = SetWindowsHookEx(
            WhKeyboardLl,
            _keyboardCallback,
            GetModuleHandle(null),
            0);
        _mouseHook = SetWindowsHookEx(
            WhMouseLl,
            _mouseCallback,
            GetModuleHandle(null),
            0);
        if (_invokedHook == nint.Zero ||
            _selectionHook == nint.Zero ||
            _keyboardHook == nint.Zero ||
            _mouseHook == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(
                error,
                "파일 선택 확인 훅을 설치하지 못했습니다.");
        }
    }

    private nint OnKeyboardInput(
        int code,
        nint message,
        nint keyboardData)
    {
        try
        {
            if (code >= 0 &&
                (message == WmKeyDown || message == WmSysKeyDown))
            {
                var dialog = _native.GetForegroundWindow();
                if (IsFileDialog(dialog))
                {
                    var data = Marshal.PtrToStructure<KeyboardHookData>(
                        keyboardData);
                    var focused = _native.GetFocusedWindow(dialog);
                    var decision = FileDialogInputPolicy.ClassifyKeyboard(
                        checked((int)data.VirtualKeyCode),
                        _native.GetControlId(focused));
                    if (decision != FileDialogDecision.Unknown)
                    {
                        _decisions.Record(dialog, decision);
                    }
                }
            }
        }
        catch
        {
            // 예외가 네이티브 훅 경계를 넘어가면 프로세스가 종료될 수 있다.
        }

        return CallNextHookEx(
            _keyboardHook,
            code,
            message,
            keyboardData);
    }

    private nint OnMouseInput(
        int code,
        nint message,
        nint mouseData)
    {
        try
        {
            if (code >= 0 && message == WmLButtonUp)
            {
                var data = Marshal.PtrToStructure<MouseHookData>(mouseData);
                var dialog = _native.GetForegroundWindow();
                if (IsFileDialog(dialog))
                {
                    var decision = FileDialogDecision.Unknown;
                    foreach (var controlId in new[]
                             {
                                 OpenButtonControlId,
                                 CancelButtonControlId
                             })
                    {
                        var control = GetDlgItem(dialog, controlId);
                        if (control == nint.Zero ||
                            !Contains(
                                _native.GetWindowBounds(control),
                                data.Point.X,
                                data.Point.Y))
                        {
                            continue;
                        }

                        decision = FileDialogInputPolicy.ClassifyControl(
                            controlId);
                        break;
                    }

                    if (decision != FileDialogDecision.Unknown)
                    {
                        _decisions.Record(dialog, decision);
                    }
                }
            }
        }
        catch
        {
            // 예외가 네이티브 훅 경계를 넘어가면 프로세스가 종료될 수 있다.
        }

        return CallNextHookEx(_mouseHook, code, message, mouseData);
    }

    private bool IsFileDialog(nint window) =>
        window != nint.Zero &&
        _native.GetRootWindow(window) == window &&
        string.Equals(
            _native.GetClassName(window),
            DialogClassName,
            StringComparison.Ordinal);

    private static bool Contains(
        WindowBounds bounds,
        int x,
        int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;

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

        var selectionChange =
            FileDialogAccessibilityEventPolicy.MapSelectionChange(eventType);
        if (selectionChange is not null)
        {
            if (eventType == EventObjectValueChange &&
                _native.GetControlId(window) != FileNameControlId)
            {
                return;
            }

            var values = ReadAccessibleValues(window, objectId, childId);
            if (values.Count > 0)
            {
                var tracked = _decisions.RecordSelection(
                    dialog,
                    selectionChange.Value,
                    values,
                    DateTimeOffset.UtcNow);
                if (tracked)
                {
                    _diagnostic?.Invoke(
                        "manual-file-dialog-selection-event",
                        $"event=0x{eventType:X4} values={values.Count}");
                }
            }

            return;
        }

        if (eventType != EventObjectInvoked)
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

    private static IReadOnlyList<string> ReadAccessibleValues(
        nint window,
        int objectId,
        int childId)
    {
        IAccessible? accessible = null;
        try
        {
            if (AccessibleObjectFromEvent(
                    window,
                    objectId,
                    childId,
                    out var eventAccessible,
                    out var childVariant) < 0)
            {
                return [];
            }

            accessible = eventAccessible;
            var values = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            AddAccessibleValue(
                values,
                SafeAccessibleName(eventAccessible, childVariant));
            AddAccessibleValue(
                values,
                SafeAccessibleValue(eventAccessible, childVariant));
            AddAccessibleValue(
                values,
                SafeAccessibleDescription(eventAccessible, childVariant));
            AddAccessibleSelectionValues(eventAccessible, values);
            return values.ToArray();
        }
        catch (COMException)
        {
            return [];
        }
        catch (InvalidCastException)
        {
            return [];
        }
        finally
        {
            if (accessible is not null && Marshal.IsComObject(accessible))
            {
                try
                {
                    Marshal.ReleaseComObject(accessible);
                }
                catch (COMException)
                {
                    // 이미 해제된 접근성 개체는 무시한다.
                }
            }
        }
    }

    private static void AddAccessibleSelectionValues(
        IAccessible accessible,
        ISet<string> values)
    {
        object? selection;
        try
        {
            selection = accessible.accSelection;
        }
        catch (COMException)
        {
            return;
        }
        catch (InvalidCastException)
        {
            return;
        }

        switch (selection)
        {
            case System.Runtime.InteropServices.ComTypes.IEnumVARIANT
                selectedItems:
                AddAccessibleEnumerationValues(
                    accessible,
                    selectedItems,
                    values);
                break;
            case System.Collections.IEnumerable enumerable:
                foreach (var selected in enumerable)
                {
                    AddAccessibleSelectionValue(
                        accessible,
                        selected,
                        values);
                }

                break;
            default:
                AddAccessibleSelectionValue(accessible, selection, values);
                break;
        }
    }

    private static void AddAccessibleEnumerationValues(
        IAccessible parent,
        System.Runtime.InteropServices.ComTypes.IEnumVARIANT items,
        ISet<string> values)
    {
        var item = new object[1];
        var fetched = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            while (items.Next(1, item, fetched) == 0 &&
                   Marshal.ReadInt32(fetched) == 1)
            {
                AddAccessibleSelectionValue(parent, item[0], values);
            }
        }
        catch (COMException)
        {
            // 선택이 바뀌어 열거가 무효가 되면 현재까지 읽은 값만 사용한다.
        }
        finally
        {
            Marshal.FreeCoTaskMem(fetched);
        }
    }

    private static void AddAccessibleSelectionValue(
        IAccessible parent,
        object? selected,
        ISet<string> values)
    {
        switch (selected)
        {
            case int child:
                AddAccessibleValue(
                    values,
                    SafeAccessibleName(parent, child));
                AddAccessibleValue(
                    values,
                    SafeAccessibleValue(parent, child));
                break;
            case IAccessible item:
                AddAccessibleValue(
                    values,
                    SafeAccessibleName(item, 0));
                AddAccessibleValue(
                    values,
                    SafeAccessibleValue(item, 0));
                break;
        }
    }

    private static string? SafeAccessibleName(
        IAccessible accessible,
        object child)
    {
        try
        {
            return accessible.get_accName(child);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static string? SafeAccessibleValue(
        IAccessible accessible,
        object child)
    {
        try
        {
            return accessible.get_accValue(child);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static string? SafeAccessibleDescription(
        IAccessible accessible,
        object child)
    {
        try
        {
            return accessible.get_accDescription(child);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static void AddAccessibleValue(
        ISet<string> values,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    public void Dispose()
    {
        var invokedHook = Interlocked.Exchange(
            ref _invokedHook,
            nint.Zero);
        if (invokedHook != nint.Zero)
        {
            UnhookWinEvent(invokedHook);
        }

        var selectionHook = Interlocked.Exchange(
            ref _selectionHook,
            nint.Zero);
        if (selectionHook != nint.Zero)
        {
            UnhookWinEvent(selectionHook);
        }

        var keyboardHook = Interlocked.Exchange(
            ref _keyboardHook,
            nint.Zero);
        if (keyboardHook != nint.Zero)
        {
            UnhookWindowsHookEx(keyboardHook);
        }

        var mouseHook = Interlocked.Exchange(ref _mouseHook, nint.Zero);
        if (mouseHook != nint.Zero)
        {
            UnhookWindowsHookEx(mouseHook);
        }
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    private delegate nint LowLevelHookDelegate(
        int code,
        nint message,
        nint data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardHookData
    {
        public readonly uint VirtualKeyCode;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseHookData
    {
        public readonly Point Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelHookDelegate callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint message,
        nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern nint GetDlgItem(nint dialog, int controlId);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromEvent(
        nint window,
        int objectId,
        int childId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible,
        [MarshalAs(UnmanagedType.Struct)] out object childVariant);
}

internal static class FileDialogAccessibilityEventPolicy
{
    private const uint EventObjectSelection = 0x8006;
    private const uint EventObjectSelectionAdd = 0x8007;
    private const uint EventObjectSelectionRemove = 0x8008;
    private const uint EventObjectSelectionWithin = 0x8009;
    private const uint EventObjectValueChange = 0x800E;

    public static FileDialogSelectionChange? MapSelectionChange(
        uint eventType) =>
        eventType switch
        {
            EventObjectSelection => FileDialogSelectionChange.Replace,
            EventObjectSelectionAdd => FileDialogSelectionChange.Add,
            EventObjectSelectionRemove => FileDialogSelectionChange.Remove,
            EventObjectSelectionWithin => FileDialogSelectionChange.Replace,
            EventObjectValueChange => FileDialogSelectionChange.Replace,
            _ => null
        };
}

internal static class FileDialogInputPolicy
{
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int OpenButtonControlId = 1;
    private const int CancelButtonControlId = 2;

    public static FileDialogDecision ClassifyKeyboard(
        int virtualKey,
        int focusedControlId)
    {
        if (virtualKey == VkEscape ||
            (virtualKey == VkReturn &&
             focusedControlId == CancelButtonControlId))
        {
            return FileDialogDecision.Cancelled;
        }

        return virtualKey == VkReturn
            ? FileDialogDecision.Accepted
            : FileDialogDecision.Unknown;
    }

    public static FileDialogDecision ClassifyControl(int controlId) =>
        controlId switch
        {
            OpenButtonControlId => FileDialogDecision.Accepted,
            CancelButtonControlId => FileDialogDecision.Cancelled,
            _ => FileDialogDecision.Unknown
        };
}

internal sealed class WindowsFileDialogSelectionReader
{
    private static readonly PropertyCondition FileNameCondition = new(
        AutomationElement.AutomationIdProperty,
        "1148");
    private static readonly PropertyCondition AddressCondition = new(
        AutomationElement.AutomationIdProperty,
        "1001");

    public FileDialogSelectionObservation Read(nint dialog)
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

            return new FileDialogSelectionObservation(
                FileDialogPathResolver.Resolve(
                    rawSelections,
                    addressValues),
                addressValues);
        }
        catch (ElementNotAvailableException)
        {
            return FileDialogSelectionObservation.Empty;
        }
        catch (InvalidOperationException)
        {
            return FileDialogSelectionObservation.Empty;
        }
        catch (COMException)
        {
            return FileDialogSelectionObservation.Empty;
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

internal sealed record FileDialogSelectionObservation(
    IReadOnlyList<string> SelectedPaths,
    IReadOnlyList<string> AddressValues)
{
    public static FileDialogSelectionObservation Empty { get; } =
        new([], []);
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
                return;
            }

            if (folder is null || Path.HasExtension(trimmed))
            {
                return;
            }

            var hiddenExtensionMatches = ClipboardImageCodec
                .EnumerateSupportedExtensions()
                .Select(extension => Path.Combine(
                    folder,
                    trimmed + extension))
                .Where(fileExists)
                .Take(2)
                .ToArray();
            if (hiddenExtensionMatches.Length == 1)
            {
                result.Add(Path.GetFullPath(hiddenExtensionMatches[0]));
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
