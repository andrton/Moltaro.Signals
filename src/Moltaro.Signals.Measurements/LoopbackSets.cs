using System.Buffers.Text;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Moltaro.Signals.Measurements;

// SET lines over loopback, reported only (concept section 11): the rate at which a host applies the SET lines of one
// connection that sends them without waiting for their answers, and the bytes allocated in the process per SET. The
// client is a raw socket in the same process that writes the lines and counts the answers; it allocates nothing per
// line, so the bytes are the host's. The one member of the key never waits, so every set after the first finds a
// signal pending, as most sets do.
internal static class LoopbackSets
{
    private const int WarmUpSets = 100_000;

    private const int MeasuredSets = 1_000_000;

    // "SET ", a <rid> of at most 19 digits and " jobs\r\n".
    private const int LongestLine = 30;

    public static async Task RunAsync()
    {
        await using SignalHost host = Loopback.StartHost();
        using ISignalSubscription busy = host.Subscribe("jobs", "workers");

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host.LocalEndPoint!);
        await HelloAsync(socket);

        // A rid is never used twice on a connection, so the measured sets go on from the last one of the warm-up.
        await SetManyAsync(socket, firstRid: 1, WarmUpSets);

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        await SetManyAsync(socket, firstRid: 1 + WarmUpSets, MeasuredSets);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        Console.WriteLine(
            $"One connection, pipelined: {MeasuredSets / elapsed.TotalSeconds:N0} SET lines applied per second (reported, not judged)");
        Console.WriteLine($"Allocated per SET over loopback: {allocated / (double)MeasuredSets:F1} bytes");
    }

    private static async Task HelloAsync(Socket socket)
    {
        await socket.SendAsync(Encoding.ASCII.GetBytes($"HELLO 1 {Loopback.Token}\r\n").AsMemory(), SocketFlags.None);

        byte[] answer = new byte[64];
        int length = 0;

        while (length == 0 || answer[length - 1] != '\n')
        {
            int received = await socket.ReceiveAsync(answer.AsMemory(length), SocketFlags.None);

            if (received == 0)
            {
                throw new InvalidOperationException("The host closed the connection instead of answering its HELLO.");
            }

            length += received;
        }

        string line = Encoding.ASCII.GetString(answer, 0, length);

        if (line != "OK 0\r\n")
        {
            throw new InvalidOperationException($"The host answered the HELLO with {line.TrimEnd()}.");
        }
    }

    // Sends count SET lines with the rids from firstRid on, without waiting for their answers, and counts the answers
    // as they come, one line each.
    private static async Task SetManyAsync(Socket socket, long firstRid, int count)
    {
        Task writing = Task.Run(() => WriteSetsAsync(socket, firstRid, count));

        await CountAnswersAsync(socket, count);
        await writing;
    }

    // Fills a buffer with as many whole lines as fit and sends it, until every line is sent.
    private static async Task WriteSetsAsync(Socket socket, long firstRid, int count)
    {
        byte[] buffer = new byte[4096];
        long rid = firstRid;
        long end = firstRid + count;

        while (rid < end)
        {
            int length = 0;

            while (rid < end && buffer.Length - length >= LongestLine)
            {
                length += FormatSet(buffer.AsSpan(length), rid);
                rid++;
            }

            await socket.SendAsync(buffer.AsMemory(0, length), SocketFlags.None);
        }
    }

    private static int FormatSet(Span<byte> destination, long rid)
    {
        "SET "u8.CopyTo(destination);
        Utf8Formatter.TryFormat(rid, destination[4..], out int digits);
        " jobs\r\n"u8.CopyTo(destination[(4 + digits)..]);

        return 4 + digits + 7;
    }

    private static async Task CountAnswersAsync(Socket socket, int count)
    {
        byte[] buffer = new byte[65536];
        int answers = 0;

        while (answers < count)
        {
            int received = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);

            if (received == 0)
            {
                throw new InvalidOperationException($"The host closed the connection after {answers:N0} of {count:N0} answers.");
            }

            answers += buffer.AsSpan(0, received).Count((byte)'\n');
        }
    }
}
