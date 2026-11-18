using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Moltaro.Signals.Hosting.Tests.GenericHost;

// The logger of one category of a LogRecorder.
internal sealed class RecordingLogger(string category, ChannelWriter<LogLine> lines) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // A structured message carries its named values as its state.
        var values = new Dictionary<string, object?>();

        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            foreach ((string name, object? value) in properties)
            {
                values[name] = value;
            }
        }

        lines.TryWrite(new LogLine(category, logLevel, values));
    }
}
