using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SchemaCompare.Models;
using SchemaCompare.Services;
using SchemaCompare.ViewModels;

namespace SchemaCompare;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();

        // Wire clipboard helper so the ViewModel can copy text without holding a window reference.
        // Guarded: a locked OS clipboard throws (COMException) and must never crash the app.
        vm.CopyToClipboardAsync = async text =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    await ClipboardExtensions.SetTextAsync(cb, text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
            }
        };

        // Guard Avalonia's built-in TextBox Cut/Copy/Paste: they are async void with no
        // clipboard try/catch, so a locked clipboard (COMException) kills the process.
        // These routed events fire before any clipboard access (keyboard, context menu and
        // programmatic Cut()/Copy()/Paste()); marking them handled cancels the built-in
        // path and we redo the operation safely via ClipboardSafety.
        AddHandler(TextBox.CuttingToClipboardEvent, OnTextCuttingToClipboard);
        AddHandler(TextBox.CopyingToClipboardEvent, OnTextCopyingToClipboard);
        AddHandler(TextBox.PastingFromClipboardEvent, OnTextPastingFromClipboard);

        // Password boxes (PasswordChar set) are excluded from the above: Avalonia swallows
        // cut/copy gestures for them without ever raising a clipboard event, so Ctrl+X /
        // Ctrl+C would silently do nothing there. Intercept those gestures in the tunnel
        // phase to give every text field the same safe behavior. (Paste is left to the
        // framework, which already allows it in password boxes.)
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        DataContext = vm;
    }

    private async void CopyScript_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.FullDeployScript) &&
                TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await ClipboardExtensions.SetTextAsync(cb, vm.FullDeployScript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
    }

    private async void CopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
                await vm.CopyLogsToClipboardAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Copy failed ({ex.GetType().Name}): {ex.Message}");
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = "Copy failed: the system clipboard is currently unavailable.";
        }
    }

    private void DiffItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: SchemaDiffItem diff } && DataContext is MainViewModel vm)
            vm.SelectedDiff = diff;
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.ShowApplyConfirmation = true;
    }

    /// <summary>
    /// Safe Cut for every TextBox in this window (keyboard Ctrl+X / Shift+Delete, context
    /// menu, or programmatic). Cancels Avalonia's built-in handler — its unguarded clipboard
    /// await crashes the process when the OS clipboard is locked — and redoes the cut via
    /// <see cref="ClipboardSafety"/>, which retries transient locks and keeps the selected
    /// text when the clipboard write fails so no data is lost. Read-only boxes keep
    /// copy-only semantics.
    /// </summary>
    private async void OnTextCuttingToClipboard(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox tb)
            return;
        e.Handled = true;
        await SafeCutAsync(tb);
    }

    /// <summary>Safe Copy for every TextBox in this window. Never throws on clipboard failure.</summary>
    private async void OnTextCopyingToClipboard(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox tb)
            return;
        e.Handled = true;
        await SafeCopyAsync(tb);
    }

    /// <summary>Safe Paste for every TextBox in this window. Never throws on clipboard failure.</summary>
    private async void OnTextPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox tb)
            return;
        e.Handled = true;
        await SafePasteAsync(tb);
    }

    /// <summary>
    /// Tunnel-phase cut/copy handling for password boxes only. All other TextBoxes are
    /// covered by the clipboard routed events above; password boxes never raise those.
    /// </summary>
    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Source is not TextBox tb || tb.PasswordChar == '\0')
            return;
        var hotkeys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (hotkeys is null)
            return;
        if (hotkeys.Cut.Any(g => g.Matches(e)))
        {
            e.Handled = true;
            await SafeCutAsync(tb);
        }
        else if (hotkeys.Copy.Any(g => g.Matches(e)))
        {
            e.Handled = true;
            await SafeCopyAsync(tb);
        }
    }

    private async Task<bool> SafeCutAsync(TextBox tb)
    {
        var selected = tb.SelectedText;
        if (string.IsNullOrEmpty(selected))
            return false; // nothing selected: silent no-op, like the built-in
        var ok = await ClipboardSafety.CutSelectionAsync(
            selected,
            () => tb.SelectedText = string.Empty,
            tb.IsReadOnly,
            TopLevel.GetTopLevel(tb)?.Clipboard);
        if (!ok)
            ReportClipboardFailure("Cut");
        return ok;
    }

    private async Task<bool> SafeCopyAsync(TextBox tb)
    {
        var selected = tb.SelectedText;
        if (string.IsNullOrEmpty(selected))
            return false; // nothing selected: silent no-op, like the built-in
        var ok = await ClipboardSafety.CopySelectionAsync(
            selected,
            TopLevel.GetTopLevel(tb)?.Clipboard);
        if (!ok)
            ReportClipboardFailure("Copy");
        return ok;
    }

    private async Task SafePasteAsync(TextBox tb)
    {
        if (tb.IsReadOnly)
            return;
        var (success, text) = await ClipboardSafety.TakePasteTextAsync(TopLevel.GetTopLevel(tb)?.Clipboard);
        if (!success)
        {
            ReportClipboardFailure("Paste");
            return;
        }
        if (string.IsNullOrEmpty(text))
            return; // empty clipboard: nothing to do, stay silent
        try
        {
            // Replaces the current selection (or inserts at the caret when nothing is selected).
            tb.SelectedText = ClipboardSafety.SanitizePasteText(text, tb.AcceptsReturn);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Paste-apply failed ({ex.GetType().Name}): {ex.Message}");
            ReportClipboardFailure("Paste");
        }
    }

    private void ReportClipboardFailure(string operation)
    {
        if (DataContext is MainViewModel vm)
            vm.StatusMessage = $"{operation} failed: the system clipboard is unavailable (another app may be holding it). Nothing was changed.";
    }
}
