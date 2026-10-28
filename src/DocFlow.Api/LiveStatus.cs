using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DocFlow.Contracts;
using MassTransit;

namespace DocFlow.Api;

public sealed class DocumentEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<DocumentStatusChanged>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public void Broadcast(DocumentStatusChanged change)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(change);
        }
    }

    public async IAsyncEnumerable<DocumentStatusChanged> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<DocumentStatusChanged>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[id] = channel;
        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}

public sealed class LiveStatusConsumer(DocumentEventBroadcaster broadcaster) : IConsumer<DocumentStatusChanged>
{
    public Task Consume(ConsumeContext<DocumentStatusChanged> context)
    {
        broadcaster.Broadcast(context.Message);
        return Task.CompletedTask;
    }
}
