using System.Threading.Channels;
using Agent.Shared.Models;

namespace Agent.Service.Infrastructure;

public sealed class WebEventQueue
{
    public Channel<WebEvent> Channel { get; } =
        System.Threading.Channels.Channel.CreateUnbounded<WebEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
}
