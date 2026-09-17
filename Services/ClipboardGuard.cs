using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace SchemaCompare.Services;

/// <summary>
/// Crash-safe clipboard handling shared by every window of the app.
///
/// Background: Avalonia's built-in <c>TextBox.Cut()/Copy()/Paste()</c> are <c>async void</c> and do
/// not guard the platform clipboard call. When the Windows clipboard is transiently locked by
/// another process (clipboard manager, password manager, RDP rdpclip, Office, browser) the Win32
/// backend throws <c>COMException</c> after exhausting its OLE retries; the exception escapes the
/// async-void continuation and terminates the whole process — i.e. Ctrl+X / Ctrl+C closes the app.
/// (Upstream: AvaloniaUI/Avalonia issue #21296 / PR #21665.)
///
/// <see cref="Attach"/> hooks the clipboard routed events — which fire before any clipboard access —
/// cancels Avalonia's built-in path and redoes the operation through <see cref="ClipboardSafety"/>,
/// which retries transient locks and never throws.
/// </summary>
public static class ClipboardGuard
{
    /// <summary>
    /// Wires the safe clipboard handling onto <paramref name="window"/>. Call once from the window
    /// constructor, after <c>InitializeComponent()</c>. <paramref name="reportFailure"/> receives a
    /// user-facing message whenever a clipboard operation could not be completed (optional).
    /// </summary>
    public static void Attach(Window window, Action<string>? reportFailure = null)
    {
        window.AddHandler(TextBox.CuttingToClipboardEvent, (sender, e) => OnCutting(e, reportFailure));
        window.AddHandler(TextBox.CopyingToClipboardEvent, (sender, e) => OnCopying(e, reportFailure));
        window.AddHandler(TextBox.PastingFromClipboardEvent, (sender, e) => OnPasting(e, reportFailure));

        // Password boxes (PasswordChar set) are excluded from the events above: Avalonia swallows
        // cut/copy gestures for them without ever raising a clipboard event, so Ctrl+X / Ctrl+C would
        // silently do nothing there. Intercept those gestures in the tunnel phase to give every text
        // field the same safe behavior. (Paste is left to the framework, which already allows it in
        // password boxes.)
        window.AddHandler(InputElement.KeyDownEvent, (sender, e) => OnPreviewKeyDown(e, reportFailure),
            RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Safe Cut (keyboard, context menu or programmatic). Cancels Avalonia's built-in handler — its
    /// unguarded clipboard await crashes the process when the OS clipboard is locked — and redoes the
    /// cut via <see cref="ClipboardSafety"/>, which keeps the selected text when the clipboard write
    /// fails so no data is lost. Read-only boxes keep copy-only semantics.
    /// </summary>
    private static async void OnCutting(RoutedEventArgs e, Action<string>? reportFailure)
    {
        if (e.Source is not TextBox tb)
            return;
        e.Handled = true;
        if (!await SafeCutAsync(tb))
            ReportFailure(reportFailure, "Cut");
    }

    /// <summary>Safe Copy for every TextBox in the window. Never throws on clipboard failure.</summary>
    private static async void OnCopying(RoutedEventArgs e, Action<string>? reportFailure)
    {
        if (e.Source is not TextBox tb)
            return;
        e.Handled = true;
        if (!await SafeCopyAsync(tb))
            ReportFailure(reportFailure, "Copy");
    }

    /// <summary>Safe Paste for every TextBox in the window. Never throws on clipboard failure.</summary>
    private static async void OnPasting(RoutedEventArgs e, Action<string>? reportFailure)
    {
        if (e.Source is not TextBox tb)
            return;
        e.Handled = true;
        await SafePasteAsync(tb, reportFailure);
    }

    /// <summary>
    /// Tunnel-phase cut/copy handling for password boxes only. All other TextBoxes are covered by the
    /// clipboard routed events above; password boxes never raise those.
    /// </summary>
    private static async void OnPreviewKeyDown(KeyEventArgs e, Action<string>? reportFailure)
    {
        if (e.Handled || e.Source is not TextBox tb || tb.PasswordChar == '\0')
            return;
        var hotkeys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (hotkeys is null)
            return;
        if (hotkeys.Cut.Any(g => g.Matches(e)))
        {
            e.Handled = true;
            if (!await SafeCutAsync(tb))
                ReportFailure(reportFailure, "Cut");
        }
        else if (hotkeys.Copy.Any(g => g.Matches(e)))
        {
            e.Handled = true;
            if (!await SafeCopyAsync(tb))
                ReportFailure(reportFailure, "Copy");
        }
    }

    private static async Task<bool> SafeCutAsync(TextBox tb)
    {
        var selected = tb.SelectedText;
        if (string.IsNullOrEmpty(selected))
            return true; // nothing selected: silent no-op, like the built-in
        return await ClipboardSafety.CutSelectionAsync(
            selected,
            () => tb.SelectedText = string.Empty,
            tb.IsReadOnly,
            TopLevel.GetTopLevel(tb)?.Clipboard);
    }

    private static async Task<bool> SafeCopyAsync(TextBox tb)
    {
        var selected = tb.SelectedText;
        if (string.IsNullOrEmpty(selected))
            return true; // nothing selected: silent no-op, like the built-in
        return await ClipboardSafety.CopySelectionAsync(
            selected,
            TopLevel.GetTopLevel(tb)?.Clipboard);
    }

    private static async Task SafePasteAsync(TextBox tb, Action<string>? reportFailure)
    {
        if (tb.IsReadOnly)
            return;
        var (success, text) = await ClipboardSafety.TakePasteTextAsync(TopLevel.GetTopLevel(tb)?.Clipboard);
        if (!success)
        {
            ReportFailure(reportFailure, "Paste");
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
            Debug.WriteLine($"[ClipboardGuard] Paste-apply failed ({ex.GetType().Name}): {ex.Message}");
            ReportFailure(reportFailure, "Paste");
        }
    }

    /// <summary>Reports a clipboard failure. Empty selections never get here (they are silent no-ops).</summary>
    private static void ReportFailure(Action<string>? reportFailure, string operation) =>
        reportFailure?.Invoke(
            $"{operation} failed: the system clipboard is unavailable (another app may be holding it). Nothing was changed.");
}
