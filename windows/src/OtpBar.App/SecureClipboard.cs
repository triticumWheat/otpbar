using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace OtpBar.App;

/// <summary>
/// Copies a code to the clipboard and can take it back once it expires.
///
/// Unlike macOS, Windows keeps a clipboard history (Win+V) and can sync the clipboard to other devices,
/// so clearing the clipboard alone would still leave the code readable. Every copy is therefore marked
/// with the formats that keep it out of history, out of the cloud, and away from clipboard monitors.
/// </summary>
public sealed class SecureClipboard
{
    private const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";
    private const string ExcludeFromHistory = "CanIncludeInClipboardHistory";
    private const string ExcludeFromCloud = "CanUploadToCloudClipboard";

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private uint? _ownSequenceNumber;

    /// <summary>Returns false when another process is holding the clipboard open.</summary>
    public bool TryCopy(string text)
    {
        var data = new DataObject();
        data.SetText(text);
        data.SetData(ExcludeFromMonitors, new MemoryStream([0]));
        data.SetData(ExcludeFromHistory, new MemoryStream([0, 0, 0, 0]));
        data.SetData(ExcludeFromCloud, new MemoryStream([0, 0, 0, 0]));
        try
        {
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (ExternalException)
        {
            // The clipboard is a single shared resource; another process can hold it for a moment.
            _ownSequenceNumber = null;
            return false;
        }
        _ownSequenceNumber = GetClipboardSequenceNumber();
        return true;
    }

    /// <summary>Clears the clipboard only while it still holds our code, never someone else's copy.</summary>
    public void ClearIfUnchanged()
    {
        if (_ownSequenceNumber is { } sequence && GetClipboardSequenceNumber() == sequence)
        {
            try
            {
                Clipboard.Clear();
            }
            catch (ExternalException)
            {
                // Losing the race to clear is not worth reporting; the code has already expired.
            }
        }
        _ownSequenceNumber = null;
    }
}
