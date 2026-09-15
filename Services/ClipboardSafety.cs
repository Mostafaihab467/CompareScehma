using System.Diagnostics;
using Avalonia.Input.Platform;

namespace SchemaCompare.Services;

/// <summary>
/// Clipboard helpers that never throw.
///
/// Background: Avalonia's built-in <c>TextBox.Cut()/Copy()/Paste()</c> are <c>async void</c>
/// and do not guard the platform clipboard call. When the Windows clipboard is transiently
/// locked by another process (clipboard manager, password manager, RDP rdpclip, Office,
/// browser), the Win32 backend throws COMException after exhausting its OLE retries.
/// The exception escapes the async-void continuation, surfaces as an unhandled dispatcher
/// exception and terminates the whole process — i.e. Ctrl+X / Cut closes the app.
/// (Upstream: AvaloniaUI/Avalonia issue #21296 / PR #21665.)
///
/// <see cref="MainWindow"/> cancels the built-in handlers via the
/// CuttingToClipboard/CopyingToClipboard/PastingFromClipboard routed events and re-does
/// the operation through these guarded helpers instead.
/// </summary>
public static class ClipboardSafety
{
    /// <summary>Copies text to the clipboard. Retries transient locks, never throws.</summary>
    public static async Task<bool> CopySelectionAsync(
        string selectedText, IClipboard? clipboard, int maxAttempts = 5, int delayMs = 100)
    {
        if (string.IsNullOrEmpty(selectedText) || clipboard is null)
            return false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ClipboardExtensions.SetTextAsync(clipboard, selectedText);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClipboardSafety] Copy attempt {attempt}/{maxAttempts} failed ({ex.GetType().Name}): {ex.Message}");
                if (attempt >= maxAttempts)
                    return false;
                try { await Task.Delay(delayMs); } catch { }
            }
        }
    }

    /// <summary>
    /// Cuts text: copies to the clipboard, then invokes <paramref name="clearSelection"/>
    /// only if the clipboard write succeeded — so no data is lost when the clipboard is busy.
    /// Read-only boxes use copy-only semantics (selection is kept).
    /// </summary>
    public static async Task<bool> CutSelectionAsync(
        string selectedText, Action clearSelection, bool isReadOnly, IClipboard? clipboard,
        int maxAttempts = 5, int delayMs = 100)
    {
        if (string.IsNullOrEmpty(selectedText))
            return false;
        if (!await CopySelectionAsync(selectedText, clipboard, maxAttempts, delayMs))
            return false; // clipboard unreachable: keep the selected text
        if (isReadOnly)
            return true;
        try
        {
            clearSelection();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClipboardSafety] Clear-selection failed ({ex.GetType().Name}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads text from the clipboard. Retries transient locks, never throws.
    /// Returns <c>(false, null)</c> only when the read itself failed; an empty clipboard
    /// yields <c>(true, null)</c> so callers can stay silent in that case.
    /// </summary>
    public static async Task<(bool Success, string? Text)> TakePasteTextAsync(
        IClipboard? clipboard, int maxAttempts = 5, int delayMs = 100)
    {
        if (clipboard is null)
            return (false, null);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return (true, await ClipboardExtensions.TryGetTextAsync(clipboard));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClipboardSafety] Paste attempt {attempt}/{maxAttempts} failed ({ex.GetType().Name}): {ex.Message}");
                if (attempt >= maxAttempts)
                    return (false, null);
                try { await Task.Delay(delayMs); } catch { }
            }
        }
    }

    /// <summary>
    /// Normalizes pasted text the way single-line boxes expect (no raw line breaks).
    /// Multi-line boxes keep their line breaks (as "\n").
    /// </summary>
    public static string SanitizePasteText(string text, bool acceptsReturn)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return acceptsReturn ? text : text.Replace('\n', ' ');
    }
}
