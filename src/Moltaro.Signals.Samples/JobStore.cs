using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Moltaro.Signals.Samples;

// Stands in for the durable storage of an application, such as a table of jobs in a database that every process
// reaches. The work lives here; a signal only says "look now", and a worker claims what it finds.
internal sealed class JobStore
{
    private readonly ConcurrentQueue<string> _jobs = new();

    public void Add(string job)
    {
        _jobs.Enqueue(job);
    }

    public bool TryClaim([NotNullWhen(true)] out string? job)
    {
        return _jobs.TryDequeue(out job);
    }
}
