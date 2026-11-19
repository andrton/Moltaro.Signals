using Moltaro.Signals.Measurements;

// The measurements of the performance targets of concept section 11, judged as the owner answered PQ2 of the first
// epic. Started by hand from src with "dotnet run -c Release --project Moltaro.Signals.Measurements"; never by CI or
// the test lane. Every figure is printed below the processor, the operating system and the .NET version it was
// measured on.
//
// The scenarios take no cancellation token: a cancellable token changes what a wait costs, and a run ends by itself.
Console.WriteLine(Machine.Describe());

await RunScenarioAsync("In-process set to wake", SetToWake.RunInProcessAsync);
await RunScenarioAsync("Sets per second on one thread, most of them coalesced", SetRate.RunOneThreadAsync);
await RunScenarioAsync("Memory retained per (key, group) and per subscription", RetainedMemory.RunAsync);
await RunScenarioAsync("Allocations of a wait with a deadline and a cancellable token", WaitAllocations.RunAsync);
await RunScenarioAsync("A set of a key of three segments with members on one level only", AncestorSets.RunAsync);
await RunScenarioAsync("Sets per second on 2, 4 and 8 threads, reported only", SetRate.RunSeveralThreadsAsync);
await RunScenarioAsync("SET lines over loopback from one connection, pipelined, reported only", LoopbackSets.RunAsync);
await RunScenarioAsync("Set to wake over loopback, from one client to a member on another", SetToWake.RunOverLoopbackAsync);
await RunScenarioAsync("Sets per second from one client with concurrent callers, reported only", ClientSets.RunAsync);

static async Task RunScenarioAsync(string title, Func<Task> scenario)
{
    Console.WriteLine();
    Console.WriteLine($"== {title}");

    await scenario();
}
