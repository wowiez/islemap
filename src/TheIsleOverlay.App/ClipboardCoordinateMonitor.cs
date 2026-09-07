using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public sealed class ClipboardCoordinateMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private uint _lastSequence;
    private bool _disposed;

    public ClipboardCoordinateMonitor(Dispatcher dispatcher, Func<WorldLocation, bool> onCoordinate)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(onCoordinate);

        _lastSequence = GetClipboardSequenceNumber();
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            (_, _) => CheckClipboard(onCoordinate),
            dispatcher);
    }

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (enabled)
        {
            _lastSequence = 0;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
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
    }

    private void CheckClipboard(Func<WorldLocation, bool> onCoordinate)
    {
        var sequence = GetClipboardSequenceNumber();
        if (sequence == _lastSequence)
        {
            return;
        }

        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                _lastSequence = sequence;
                return;
            }

            var text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (ClipboardCoordinateParser.TryParse(text, out var location))
            {
                if (!onCoordinate(location))
                {
                    return;
                }
            }

            // Consume the sequence only after the receiver accepts the value.
            // This lets a valid Asset Location copied while the map is open be
            // retried after the map closes.
            _lastSequence = sequence;
        }
        catch (ExternalException)
        {
            // Another process owns the clipboard. Keep the old sequence so the
            // next 100 ms tick retries without losing the coordinate update.
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
