param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [ValidateRange(1, 60)]
    [int]$Seconds = 10,

    [ValidateRange(1, 50000)]
    [int]$MaxEvents = 10000
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

if (-not ('Sentory.Diagnostics.WinEventMonitor' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sentory.Diagnostics
{
    public sealed class WinEventRecord
    {
        public long ElapsedMilliseconds { get; set; }
        public uint EventId { get; set; }
        public long WindowHandle { get; set; }
        public int ObjectId { get; set; }
        public int ChildId { get; set; }
        public uint EventThreadId { get; set; }
    }

    public sealed class WinEventResult
    {
        public WinEventRecord[] Events { get; set; }
        public bool Truncated { get; set; }
        public string ErrorType { get; set; }
    }

    public static class WinEventMonitor
    {
        private const uint EventMinimum = 0x00000001;
        private const uint EventMaximum = 0x7FFFFFFF;
        private const uint WinEventOutOfContext = 0x0000;
        private const uint PeekMessageRemove = 0x0001;

        private delegate void WinEventDelegate(
            IntPtr hook,
            uint eventId,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThreadId,
            uint eventTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr WindowHandle;
            public uint Id;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Cursor;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMinimum,
            uint eventMaximum,
            IntPtr eventHookModule,
            WinEventDelegate callback,
            uint processId,
            uint threadId,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(
            out Message message,
            IntPtr windowHandle,
            uint filterMinimum,
            uint filterMaximum,
            uint removeMessage);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Message message);

        public static WinEventResult Monitor(
            uint processId,
            int seconds,
            int maxEvents)
        {
            var result = new WinEventResult
            {
                Events = new WinEventRecord[0],
                ErrorType = string.Empty
            };
            var records = new List<WinEventRecord>();
            var stopwatch = Stopwatch.StartNew();
            var gate = new object();

            WinEventDelegate callback = (
                hook,
                eventId,
                windowHandle,
                objectId,
                childId,
                eventThreadId,
                eventTime) =>
            {
                lock (gate)
                {
                    if (records.Count >= maxEvents)
                    {
                        result.Truncated = true;
                        return;
                    }

                    records.Add(new WinEventRecord
                    {
                        ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                        EventId = eventId,
                        WindowHandle = windowHandle.ToInt64(),
                        ObjectId = objectId,
                        ChildId = childId,
                        EventThreadId = eventThreadId
                    });
                }
            };

            IntPtr eventHook = IntPtr.Zero;
            try
            {
                eventHook = SetWinEventHook(
                    EventMinimum,
                    EventMaximum,
                    IntPtr.Zero,
                    callback,
                    processId,
                    0,
                    WinEventOutOfContext);

                if (eventHook == IntPtr.Zero)
                {
                    result.ErrorType = "SetWinEventHookFailed";
                    return result;
                }

                var duration = TimeSpan.FromSeconds(seconds);
                while (stopwatch.Elapsed < duration)
                {
                    Message message;
                    while (PeekMessage(
                        out message,
                        IntPtr.Zero,
                        0,
                        0,
                        PeekMessageRemove))
                    {
                        TranslateMessage(ref message);
                        DispatchMessage(ref message);
                    }

                    Thread.Sleep(10);
                }
            }
            catch (Exception exception)
            {
                result.ErrorType = exception.GetType().Name;
            }
            finally
            {
                if (eventHook != IntPtr.Zero)
                {
                    UnhookWinEvent(eventHook);
                }

                GC.KeepAlive(callback);
            }

            lock (gate)
            {
                result.Events = records.ToArray();
            }

            return result;
        }
    }
}
'@
}

$result = [Sentory.Diagnostics.WinEventMonitor]::Monitor(
    [uint32]$ProcessId,
    $Seconds,
    $MaxEvents
)

$events = @(
    foreach ($eventRecord in $result.Events) {
        [pscustomobject]@{
            elapsedMilliseconds = $eventRecord.ElapsedMilliseconds
            eventId = ('0x{0:X8}' -f $eventRecord.EventId)
            windowHandle = if ($eventRecord.WindowHandle -eq 0) {
                ''
            }
            else {
                '0x{0:X}' -f $eventRecord.WindowHandle
            }
            objectId = $eventRecord.ObjectId
            childId = $eventRecord.ChildId
            eventThreadId = $eventRecord.EventThreadId
        }
    }
)

[pscustomobject]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    processId = $ProcessId
    requestedSeconds = $Seconds
    eventCount = $events.Count
    truncated = $result.Truncated
    errorType = $result.ErrorType
    privacyMode = 'No accessible names, values, text, or coordinates are collected.'
    events = $events
} | ConvertTo-Json -Depth 5 -Compress
