using Microsoft.Extensions.Logging;

namespace Moltaro.Signals.Hosting.Tests.GenericHost;

// One line written through a LogRecorder, with the named values of its message.
internal sealed record LogLine(string Category, LogLevel Level, IReadOnlyDictionary<string, object?> Values);
