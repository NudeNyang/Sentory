using System.Windows;
using System.Windows.Input;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Sentory.App;

public sealed class SelectableTextBox : WpfTextBox
{
    private int _selectionAnchor;
    private bool _isSelecting;

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        Focus();
        var insertionIndex = GetTextInsertionIndex(e.GetPosition(this));
        if (e.ClickCount > 1)
        {
            SelectWordAt(insertionIndex);
            e.Handled = true;
            return;
        }

        _selectionAnchor = insertionIndex;
        _isSelecting = true;
        Select(_selectionAnchor, 0);
        Mouse.Capture(this, CaptureMode.Element);
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(WpfMouseEventArgs e)
    {
        if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            base.OnPreviewMouseMove(e);
            return;
        }

        UpdateSelection(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (!_isSelecting || e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseUp(e);
            return;
        }

        UpdateSelection(e.GetPosition(this));
        _isSelecting = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(WpfMouseEventArgs e)
    {
        _isSelecting = false;
        base.OnLostMouseCapture(e);
    }

    private void UpdateSelection(WpfPoint pointer)
    {
        var insertionIndex = GetTextInsertionIndex(pointer);
        var selectionStart = Math.Min(_selectionAnchor, insertionIndex);
        Select(selectionStart, Math.Abs(insertionIndex - _selectionAnchor));
    }

    private int GetTextInsertionIndex(WpfPoint pointer)
    {
        if (string.IsNullOrEmpty(Text) ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return 0;
        }

        var clampedPoint = new WpfPoint(
            Math.Clamp(pointer.X, 0, Math.Max(0, ActualWidth - 0.5)),
            Math.Clamp(pointer.Y, 0, Math.Max(0, ActualHeight - 0.5)));
        var characterIndex = GetCharacterIndexFromPoint(
            clampedPoint,
            snapToText: true);
        if (characterIndex < 0)
        {
            return pointer.X <= ActualWidth / 2 ? 0 : Text.Length;
        }

        var characterBounds = GetRectFromCharacterIndex(
            characterIndex,
            trailingEdge: false);
        var insertionIndex = characterIndex;
        if (pointer.X >= ActualWidth ||
            clampedPoint.X > characterBounds.X + characterBounds.Width / 2)
        {
            insertionIndex++;
        }

        return Math.Clamp(insertionIndex, 0, Text.Length);
    }

    private void SelectWordAt(int insertionIndex)
    {
        if (string.IsNullOrEmpty(Text))
        {
            Select(0, 0);
            return;
        }

        var characterIndex = Math.Clamp(insertionIndex, 0, Text.Length - 1);
        if (char.IsWhiteSpace(Text[characterIndex]) && characterIndex > 0)
        {
            characterIndex--;
        }

        var start = characterIndex;
        while (start > 0 && !char.IsWhiteSpace(Text[start - 1]))
        {
            start--;
        }

        var end = characterIndex + 1;
        while (end < Text.Length && !char.IsWhiteSpace(Text[end]))
        {
            end++;
        }

        Select(start, end - start);
    }
}
