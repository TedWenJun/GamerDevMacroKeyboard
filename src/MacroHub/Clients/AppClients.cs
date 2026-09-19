using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MacroHub.Core;
using MacroHub.Input;

namespace MacroHub.Clients;

/// <summary>Where messages for one connection go (WebSocket subscription or named-pipe writer).</summary>
public interface IClientSink
{
    string Transport { get; }
    bool Send(object message);
}

/// <summary>Sink backed by a bounded channel; the transport drains it. Oldest messages are dropped if the peer stalls.</summary>
public sealed class ChannelSink(string transport, int capacity = 512) : IClientSink
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public string Transport { get; } = transport;
    public ChannelReader<string> Reader => _channel.Reader;
    public bool Send(object message) => _channel.Writer.TryWrite(message as string ?? JsonSerializer.Serialize(message, HubConfig.CompactJsonOptions));
    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>Sink that targets one WebSocket subscriber of the <see cref="EventBus"/>.</summary>
public sealed class BusSink(EventBus bus, Guid id) : IClientSink
{
    public string Transport => "websocket";
    public bool Send(object message) => bus.SendTo(id, message);
}

/// <summary>An application registered through the hello handshake (e.g. the UE plugin).</summary>
public sealed class AppClient
{
    public required Guid Id { get; init; }
    public required string App { get; init; }
    public required string Process { get; init; }
    public required uint Pid { get; init; }
    /// <summary>Client-declared run mode, e.g. "editor" or "game" (informational; routing is by pid).</summary>
    public string Mode { get; init; } = "";
    public int Protocol { get; init; } = 1;
    public required IClientSink Sink { get; init; }
    public DateTime Since { get; } = DateTime.Now;
    /// <summary>Last context the client reported, e.g. "Sequencer" — shown in the web UI.</summary>
    public string? Context { get; set; }
    public string? ContextDetail { get; set; }
    private long _seq;
    public long LastSeq => Interlocked.Read(ref _seq);
    public long NextSeq() => Interlocked.Increment(ref _seq);

    /// <summary>A client with a pid only matches that exact process (two editors, or editor vs. "-game" standalone, stay apart).</summary>
    public bool Matches(ForegroundInfo fg) =>
        Pid != 0 ? Pid == fg.Pid : Process.Length > 0 && string.Equals(Process, fg.Process, StringComparison.OrdinalIgnoreCase);

    public object Describe() => new { Id, App, Process, Pid, Mode, Protocol, Transport = Sink.Transport, Since, Context, ContextDetail, LastSeq };
}

public sealed class ClientRegistry
{
    private readonly ConcurrentDictionary<Guid, AppClient> _clients = new();

    public IReadOnlyList<AppClient> All => _clients.Values.OrderBy(c => c.Since).ToList();
    public void Add(AppClient c) => _clients[c.Id] = c;
    public bool Remove(Guid id, out AppClient? c) => _clients.TryRemove(id, out c);
    public AppClient? Get(Guid id) => _clients.TryGetValue(id, out var c) ? c : null;
    public List<AppClient> Matching(ForegroundInfo fg) => _clients.Values.Where(c => c.Matches(fg)).ToList();
}
