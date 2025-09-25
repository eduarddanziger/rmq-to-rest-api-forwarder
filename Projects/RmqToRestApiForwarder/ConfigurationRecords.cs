namespace RmqToRestApiForwarder;

public record RabbitMqServerSettings
{
    public string HostName { get; init; } = string.Empty;
    public string QueueName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public record RabbitMqMessageDeliverySettings
{
    public int MaxRetryAttempts { get; init; } = 5;
    public int RetryDelayInSeconds { get; init; } = 10;
}

public record ApiBaseUrlSettings
{
    public string Target { get; init; } = "Azure";
    public string Codespace { get; init; } = string.Empty;
    public string Azure { get; init; } = string.Empty;
    public string Local { get; init; } = string.Empty;
}

public record GitHubCodespaceSettings
{
    public string StartUrl { get; init; } = string.Empty;
    public string CodespaceName { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
}