namespace RmqToRestApiForwarder;

public record RabbitMqSettings
{
    public string HostName { get; init; } = string.Empty;
    public string QueueName { get; init; } = string.Empty;
}

public record ApiBaseUrlSettings
#pragma warning restore CA1050
{
    public string Target { get; init; } = "Azure";
    public string Codespace { get; init; } = string.Empty;
    public string Azure { get; init; } = string.Empty;
    public string Local { get; init; } = string.Empty;
}