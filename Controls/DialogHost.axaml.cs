using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MacExplorer.Controls;

public partial class DialogHost : UserControl
{
    private TaskCompletionSource<bool>? _completion;
    private Control? _previousFocus;

    public DialogHost()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        CancelButton.Click += (_, _) => Complete(false);
        ConfirmButton.Click += (_, _) => Complete(true);
    }

    public Task<bool> ShowConfirmationAsync(string title, string message, string confirmText)
    {
        _completion?.TrySetResult(false);
        _previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        IsVisible = true;
        ConfirmButton.Focus();
        return _completion.Task;
    }

    private void Complete(bool result)
    {
        if (!IsVisible) return;
        IsVisible = false;
        var completion = _completion;
        _completion = null;
        _previousFocus?.Focus();
        _previousFocus = null;
        completion?.TrySetResult(result);
    }

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ShouldBlockOutsideDialogPointer(e.Source as Visual)) return;

        e.Handled = true;
        ConfirmButton.Focus();
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ShouldBlockOutsideDialogPointer(e.Source as Visual)) return;

        e.Handled = true;
    }

    private bool ShouldBlockOutsideDialogPointer(Visual? source)
        => IsVisible && !IsInsideVisual(source, DialogCard);

    private static bool IsInsideVisual(Visual? visual, Visual target)
    {
        for (; visual != null; visual = visual.GetVisualParent())
            if (ReferenceEquals(visual, target))
                return true;
        return false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Complete(false);
        }
    }
}
