namespace Sentory.Diagnostics.Uia;

public enum UiaTreeView
{
    Raw,
    Control,
    Content
}

public sealed record WindowInfo(
    string ProcessName,
    int ProcessId,
    string ProcessVersion,
    string WindowHandle,
    string NativeClassName,
    string TitleLengthBucket);
