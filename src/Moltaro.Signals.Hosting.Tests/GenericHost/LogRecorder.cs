using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Moltaro.Signals.Hosting.Tests.GenericHost;

// A logger provider that keeps every line written through it, so that a test can wait for the line it expects.
internal sealed class LogRecorder : ILoggerProvider
{
    private readonly Channel<LogLine> _lines = Channel.CreateUnbounded<LogLine>();

    public ILogger CreateLogger(string categoryName)
    {
        return new RecordingLogger(categoryName, _lines.Writer);
    }

    // Returns the next line of the category at the level, waiting for it if it has not been written yet. The lines
    // before it are passed over.
    public async Task<LogLine> WaitForLineAsync(string category, LogLevel level, CancellationToken cancellationToken)
    {
        while (true)
        {
            LogLine line = await _lines.Reader.ReadAsync(cancellationToken);

            if (line.Category == category && line.Level == level)
            {
                return line;
            }
        }
    }

    public void Dispose()
    {
    }
}
