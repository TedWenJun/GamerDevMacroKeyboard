using System.IO.Pipes;
using System.Text;

namespace MacroHub.Clients;

/// <summary>
/// Named-pipe transport for application clients: <c>\\.\pipe\&lt;name&gt;</c>, newline-delimited UTF-8 JSON in both
/// directions, same messages as the WebSocket. Restricted to the current user (PipeOptions.CurrentUserOnly), no port,
/// and a broken pipe is detected immediately on either side. Unlike the WebSocket it carries only messages addressed
/// to the client (no web-UI broadcast noise).
/// </summary>
public sealed class PipeServer(string name, HubEngine engine, ILogger<PipeServer> log) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    public string PipeName { get; } = name;
    public string FullName => $@"\\.\pipe\{PipeName}";
    public int Connections => _connections;
    private int _connections;

    public void Start() => _ = Task.Run(AcceptLoop);

    private async Task AcceptLoop()
    {
        log.LogInformation("app pipe listening on {Pipe}", FullName);
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token);
                var connected = server;
                server = null;
                _ = Task.Run(() => Serve(connected));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                log.LogError(e, "pipe accept failed (is another MacroHub using {Pipe}?)", FullName);
                await Task.Delay(2000, _cts.Token).ContinueWith(_ => { });
            }
            finally { server?.Dispose(); }
        }
    }

    private async Task Serve(NamedPipeServerStream stream)
    {
        var id = Guid.NewGuid();
        var sink = new ChannelSink("pipe");
        Interlocked.Increment(ref _connections);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var writer = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in sink.Reader.ReadAllAsync(linked.Token))
                {
                    var bytes = Encoding.UTF8.GetBytes(line + "\n");
                    await stream.WriteAsync(bytes, linked.Token);
                    await stream.FlushAsync(linked.Token);
                }
            }
            catch (Exception) { linked.Cancel(); }
        });
        try
        {
            sink.Send(engine.HubHello());
            using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            while (!linked.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(linked.Token);
                if (line is null) break; // client closed
                if (line.Length > 0) engine.HandleClientMessage(id, line, sink);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception e) { log.LogError(e, "pipe client failed"); }
        finally
        {
            engine.UnregisterClient(id);
            sink.Complete();
            linked.Cancel();
            try { await writer; } catch { }
            stream.Dispose();
            Interlocked.Decrement(ref _connections);
        }
    }

    public void Dispose() => _cts.Cancel();
}
