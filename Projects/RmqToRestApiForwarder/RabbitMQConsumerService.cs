using System.Text.Json.Nodes;
using NLog;

namespace HttpRequestProcessor;

using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

public class RabbitMqConsumerService : BackgroundService
{
    private readonly string _queueName;
    private readonly string _apiTarget;
    private readonly string _apiEndpoint;

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqConsumerService> _logger;

    private static readonly string[] ValidTargets =
    [
        nameof(ApiBaseUrlSettings.Azure), nameof(ApiBaseUrlSettings.Local), nameof(ApiBaseUrlSettings.Codespace)
    ];

    private static readonly string ValidTargetsAsString = string.Join(
        ", ",
        Array.ConvertAll(ValidTargets, v => $"\"{v}\""));

    private readonly record struct ProcessingResult(bool Success, string? ErrorReason);

    public RabbitMqConsumerService(IConnectionFactory connectionFactory,
        IOptions<RabbitMqSettings> rabbitSettings,
        IOptions<ApiBaseUrlSettings> apiSettings,
        ILogger<RabbitMqConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _queueName = rabbitSettings.Value.QueueName;
        _apiTarget = apiSettings.Value.Target;

        if (Array.IndexOf(ValidTargets, _apiTarget) < 0)
        {
            _logger.LogWarning(
                "Service initializing: Unknown Target REST API \"{ApiTarget}\". The possible values are {PossibleTargets}. Setting it to default value \"{Default}\"",
                _apiTarget, ValidTargetsAsString, nameof(ApiBaseUrlSettings.Azure));

            _apiTarget = nameof(ApiBaseUrlSettings.Azure);
        }
        _apiEndpoint = _apiTarget switch
        {
            nameof(ApiBaseUrlSettings.Azure) => apiSettings.Value.Azure,
            nameof(ApiBaseUrlSettings.Codespace) => apiSettings.Value.Codespace,
            nameof(ApiBaseUrlSettings.Local) => apiSettings.Value.Local,
            _ => apiSettings.Value.Azure
        };
        _logger.LogInformation("Service initialized: Source RabbitMQ Queue \"{QueueName}\", Target REST API \"{ApiTarget}\"", _queueName, _apiTarget);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(null, cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: _queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = JsonNode.Parse(body)!.AsObject();


                var httpRequest = message["httpRequest"]?.GetValue<string>();
                var urlSuffix = message["urlSuffix"]?.GetValue<string>();
                message.Remove("httpRequest");
                message.Remove("urlSuffix");

                var prettyPayload = message.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                _logger.LogInformation("Received {POSTOrPUT} HTTP request with suffix {Suffix} and payload:\n{Payload}", httpRequest, urlSuffix, prettyPayload);

                var result = await SendToApiAsync(httpRequest, urlSuffix, message, cancellationToken);

                if (result.Success)
                {
                    await _channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                    _logger.LogInformation("Processed message with {Method}", httpRequest);
                }
                else
                {
                    // Requeue for HTTP failure (may refine later).
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
                    _logger.LogWarning("Message processing failed (will requeue). Reason: {Reason}", result.ErrorReason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message processing failed. Message will be not re-enqueued (dropped).");
                if (_channel != null)
                {
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                }
            }
        };

        await _channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private async Task<ProcessingResult> SendToApiAsync(string? httpMethod, string? urlSuffix, JsonObject payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(urlSuffix))
            return new ProcessingResult(false, "Missing urlSuffix");

        if (string.IsNullOrWhiteSpace(httpMethod))
            return new ProcessingResult(false, "Missing httpRequest method");

        try
        {
            using var httpClient = new HttpClient();
            using var jsonContent = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var upper = httpMethod.ToUpperInvariant();
            var response = upper == "PUT"
                ? await httpClient.PutAsync(_apiEndpoint + urlSuffix, jsonContent, ct)
                : await httpClient.PostAsync(_apiEndpoint + urlSuffix, jsonContent, ct);

            if (response.IsSuccessStatusCode)
                return new ProcessingResult(true, null);

            var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            return new ProcessingResult(false, reason);
        }
        catch (Exception ex)
        {
            return new ProcessingResult(false, ex.Message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Dispose();
        _connection?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}

