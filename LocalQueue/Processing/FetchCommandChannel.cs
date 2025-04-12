using System.Threading.Channels;
using LocalQueue.Storage;

namespace LocalQueue.Processing;

internal class FetchCommandChannel
{
    private readonly Channel<CommandRecord> _channel;

    public ChannelReader<CommandRecord> Reader => _channel.Reader;
    public ChannelWriter<CommandRecord> Writer => _channel.Writer;

    public FetchCommandChannel(int prefetchCount)
    {
        _channel = Channel.CreateBounded<CommandRecord>(
            new BoundedChannelOptions(prefetchCount)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
    }
}