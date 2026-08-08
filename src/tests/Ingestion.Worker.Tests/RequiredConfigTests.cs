using Common.Messaging.MassTransit;
using Ingestion.Worker;
using Microsoft.Extensions.Configuration;

namespace Ingestion.Worker.Tests;

public sealed class RequiredConfigTests
{
    private static IConfigurationSection Section(params (string Key, string? Value)[] entries)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dict["Sec:" + key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build().GetSection("Sec");
    }

    [Fact]
    public void Text_Present_ReturnsValue() =>
        Assert.Equal("hello", RequiredConfig.Text(Section(("K", "hello")), "K"));

    [Fact]
    public void Text_Missing_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RequiredConfig.Text(Section(), "K"));

    [Fact]
    public void Text_Blank_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RequiredConfig.Text(Section(("K", "   ")), "K"));

    [Fact]
    public void Integer_Present_ReturnsValue() =>
        Assert.Equal(42, RequiredConfig.Integer(Section(("K", "42")), "K"));

    [Fact]
    public void Integer_Missing_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RequiredConfig.Integer(Section(), "K"));

    [Fact]
    public void Integer_NotAnInteger_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RequiredConfig.Integer(Section(("K", "abc")), "K"));

    [Fact]
    public void Enum_Present_CaseInsensitive_ReturnsValue() =>
        Assert.Equal(MessagingTransport.RabbitMq, RequiredConfig.Enum<MessagingTransport>(Section(("K", "rabbitmq")), "K"));

    [Fact]
    public void Enum_Missing_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RequiredConfig.Enum<MessagingTransport>(Section(), "K"));

    [Fact]
    public void Enum_UnknownName_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RequiredConfig.Enum<MessagingTransport>(Section(("K", "Kafka")), "K"));

    [Fact]
    public void Enum_UndefinedNumericValue_Throws() => // parses but is not a defined member
        Assert.Throws<InvalidOperationException>(() => RequiredConfig.Enum<MessagingTransport>(Section(("K", "99")), "K"));

    [Fact]
    public void Enum_CheckpointProvider_Parses_CaseInsensitive()
    {
        Assert.Equal(CheckpointProvider.File, RequiredConfig.Enum<CheckpointProvider>(Section(("K", "File")), "K"));
        Assert.Equal(CheckpointProvider.Redis, RequiredConfig.Enum<CheckpointProvider>(Section(("K", "redis")), "K"));
    }

    [Fact]
    public void Enum_CheckpointProvider_UnknownProvider_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => RequiredConfig.Enum<CheckpointProvider>(Section(("K", "cosmos")), "K"));

    [Fact]
    public void NullSection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RequiredConfig.Text(null!, "K"));
        Assert.Throws<ArgumentNullException>(() => RequiredConfig.Integer(null!, "K"));
        Assert.Throws<ArgumentNullException>(() => RequiredConfig.Enum<MessagingTransport>(null!, "K"));
    }
}
