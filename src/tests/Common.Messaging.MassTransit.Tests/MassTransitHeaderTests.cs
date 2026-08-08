using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Messaging.MassTransit.Tests;

// Pins the producer serializer intent that keeps MassTransit's transport headers off the wire. The actual
// per-header outcome (MT-Host-Info / MT-MessageType / MT-Source-Address / ConversationId gone) is only
// observable on a real broker — the in-memory harness does not surface transport headers on consume — so it
// is proven by local integration against RabbitMQ, not here. This guard fails if a future edit re-introduces
// AddTransportHeaders (which would leak host/version info again).
public sealed class MassTransitHeaderTests
{
    [Fact]
    public void PublishSerializerOptions_OmitAddTransportHeaders_SoNoMassTransitHeadersAreStamped()
    {
        Assert.False(
            MessagingServiceCollectionExtensions.PublishSerializerOptions.HasFlag(RawSerializerOptions.AddTransportHeaders),
            "AddTransportHeaders must be omitted so MT-* / host-info headers are not stamped.");
    }

    [Fact]
    public void PublishSerializerOptions_KeepAnyMessageType()
    {
        Assert.True(MessagingServiceCollectionExtensions.PublishSerializerOptions.HasFlag(RawSerializerOptions.AnyMessageType));
    }
}
