using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Aperture.Tests.Fixtures;

internal sealed class DebugMonitorServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
    private readonly Task worker;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public DebugMonitorServer(Func<NetworkStream, CancellationToken, Task> script)
    {
        listener.Start();
        worker = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            await using var stream = client.GetStream();
            await Write(stream, "201- connected\r\n", stop.Token);
            await script(stream, stop.Token);
        });
    }
    public static async Task<string> ReadLine(NetworkStream stream, CancellationToken ct)
    {
        var result = new List<byte>(); var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, ct) == 0) throw new EndOfStreamException();
            result.Add(one[0]);
            if (one[0] == 10) return Encoding.Latin1.GetString(result.ToArray()).TrimEnd('\r', '\n');
        }
    }
    public static Task Write(NetworkStream stream, string text, CancellationToken ct) => stream.WriteAsync(Encoding.Latin1.GetBytes(text), ct).AsTask();
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        try { await worker; } catch (OperationCanceledException) { } catch (EndOfStreamException) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        catch (InvalidOperationException) when (stop.IsCancellationRequested) { }
        stop.Dispose();
    }
}
