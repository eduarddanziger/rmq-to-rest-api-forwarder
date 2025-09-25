using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace RmqToRestApiForwarder;

public class RabbitMqConsumerService : BackgroundService
{
    // Durable retry settings
    private const string AttemptHeader = "x-attempt";

    private readonly int _maxRetryAttempts;
    private readonly TimeSpan _retryDelay;

    private static readonly string[] _validTargets =
    [
        nameof(ApiBaseUrlSettings.Azure), nameof(ApiBaseUrlSettings.Local), nameof(ApiBaseUrlSettings.Codespace)
    ];

    private static readonly string _validTargetsAsString = string.Join(
        ", ",
        Array.ConvertAll(_validTargets, v => $"\"{v}\""));

    private readonly string _apiEndpoint;
    private readonly IConnectionFactory _connectionFactory;
    private readonly string _failedQueueName;
    private readonly GitHubCodespaceAwaker _codespaceAwaker;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private readonly string _queueName;
    private readonly string _retryQueueName;
    private IChannel? _channel;

    private IConnection? _connection;
    private readonly string _apiTarget;

    public RabbitMqConsumerService(IOptions<RabbitMqServerSettings> rmqServerSettings,
        IOptions<RabbitMqMessageDeliverySettings> rmqMessageDeliverySettings,
        IOptions<ApiBaseUrlSettings> apiSettings,
        GitHubCodespaceAwaker codespaceAwaker,
        ILogger<RabbitMqConsumerService> logger)
    {
        _connectionFactory = new ConnectionFactory
        {
            HostName = rmqServerSettings.Value.HostName,
            UserName = rmqServerSettings.Value.UserName,
            Password = rmqServerSettings.Value.Password
        };
        _codespaceAwaker = codespaceAwaker;
        _logger = logger;
        _queueName = rmqServerSettings.Value.QueueName;
        _retryQueueName = _queueName + ".retry";
        _failedQueueName = _queueName + ".failed";

        _maxRetryAttempts = rmqMessageDeliverySettings.Value.MaxRetryAttempts;
        _retryDelay = TimeSpan.FromSeconds(rmqMessageDeliverySettings.Value.RetryDelayInSeconds);

        var apiTarget = apiSettings.Value.Target;

        _apiTarget = apiTarget;
        if (Array.IndexOf(_validTargets, _apiTarget) < 0)
        {
            _logger.LogWarning(
                "Service initializing: Unknown Target REST API \"{ApiTarget}\". The possible values are {PossibleTargets}. Setting it to default value \"{Default}\"",
                _apiTarget, _validTargetsAsString, nameof(ApiBaseUrlSettings.Azure));

            _apiTarget = nameof(ApiBaseUrlSettings.Azure);
        }

        _apiEndpoint = _apiTarget switch
        {
            nameof(ApiBaseUrlSettings.Azure) => apiSettings.Value.Azure,
            nameof(ApiBaseUrlSettings.Codespace) => apiSettings.Value.Codespace,
            nameof(ApiBaseUrlSettings.Local) => apiSettings.Value.Local,
            _ => apiSettings.Value.Azure
        };
        _logger.LogInformation(
            "Consumer service parameters initialized: Queue \"{Queue}\" RetryQueue \"{RetryQueue}\" FailedQueue \"{FailedQueue}\" Target REST API \"{ApiTarget}\" MaxRetryAttempts {MaxAttempts} RetryDelaySeconds {RetryDelay}",
            _queueName, _retryQueueName, _failedQueueName, _apiTarget, _maxRetryAttempts, (int)_retryDelay.TotalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consumer service initialization started...");

        try
        {
            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(null, cancellationToken);

            // Primary work queue (messages consumed here)
            await _channel.QueueDeclareAsync(
                _queueName,
                true,
                false,
                false,
                cancellationToken: cancellationToken);

            // Retry queue: messages sit here for TTL then are dead-lettered (x-dead-letter-exchange="") back to the primary queue
            var retryArgs = new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = _queueName,
                ["x-message-ttl"] = (int)_retryDelay.TotalMilliseconds,
                ["x-expires"] = (int)_retryDelay.Add(TimeSpan.FromMinutes(5)).TotalMilliseconds
            };
            await _channel.QueueDeclareAsync(
                _retryQueueName,
                true,
                false,
                false,
                retryArgs!,
                cancellationToken: cancellationToken);

            // Failed (dead) queue: final resting place after max attempts (messages expire after 24h)
            var failedArgs = new Dictionary<string, object>
            {
                ["x-message-ttl"] = (int)TimeSpan.FromHours(24).TotalMilliseconds,
                ["x-expires"] = (int)TimeSpan.FromHours(25).TotalMilliseconds
            };
            await _channel.QueueDeclareAsync(
                _failedQueueName,
                true,
                false,
                false,
                failedArgs!,
                cancellationToken: cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (_, deliveryEvent) =>
            {
                try
                {
                    var body = deliveryEvent.Body.ToArray();
                    var attempt = GetAttempt(deliveryEvent.BasicProperties.Headers);

                    var message = JsonNode.Parse(body)!.AsObject();
                    var httpRequest = message["httpRequest"]?.GetValue<string>();
                    var urlSuffix = message["urlSuffix"]?.GetValue<string>();
                    message.Remove("httpRequest");
                    message.Remove("urlSuffix");

                    _logger.LogInformation(
                        "Received a message with HTTP request (Attempt {Attempt}/{MaxAttempts}): \"{Method}\", suffix \"{Suffix}\", payload:\n{Payload}",
                        attempt, _maxRetryAttempts, httpRequest, urlSuffix,
                        message.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                    var result = await SendToApiAsync(httpRequest, urlSuffix, message, cancellationToken);

                    if (result.Success)
                    {
                        await _channel.BasicAckAsync(deliveryEvent.DeliveryTag, false, cancellationToken);
                        _logger.LogInformation("Message processed successfully on attempt {Attempt}", attempt);
                    }
                    else
                    {
                        if (attempt < _maxRetryAttempts)
                        {
                            await PublishWithAttemptAsync(_retryQueueName, body, attempt + 1, cancellationToken);
                            await _channel.BasicAckAsync(deliveryEvent.DeliveryTag, false, cancellationToken); // remove original
                            _logger.LogWarning(
                                "Attempt {Attempt} failed. Scheduled retry attempt {NextAttempt} after {Delay}s. Reason: {Reason}",
                                attempt, attempt + 1, (int)_retryDelay.TotalSeconds, result.ErrorReason);
                        }
                        else
                        {
                            await PublishWithAttemptAsync(_failedQueueName, body, attempt, cancellationToken);
                            await _channel.BasicAckAsync(deliveryEvent.DeliveryTag, false, cancellationToken); // remove original
                            _logger.LogError("Attempt {Attempt} (max) failed. Routed to failed queue. Reason: {Reason}",
                                attempt, result.ErrorReason);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected exception during message processing. Moving message to failed queue.");
                    try
                    {
                        if (_channel != null)
                        {
                            var body = deliveryEvent.Body.ToArray();
                            var attempt = GetAttempt(deliveryEvent.BasicProperties.Headers);

                            await PublishWithAttemptAsync(_failedQueueName, body, attempt, cancellationToken);
                            await _channel.BasicAckAsync(deliveryEvent.DeliveryTag, false, cancellationToken);
                        }
                    }
                    catch (Exception republishEx)
                    {
                        _logger.LogError(republishEx, "Failed to move message to failed queue.");
                        if (_channel != null)
                            await _channel.BasicNackAsync(deliveryEvent.DeliveryTag, false, false, cancellationToken);
                    }
                }
            };

            await _channel.BasicConsumeAsync(
                _queueName,
                false,
                consumer,
                cancellationToken);

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in consumer service");
        }
        finally
        {
            _logger.LogInformation("Consumer service stopping...");
        }
    }

    private static int GetAttempt(IDictionary<string, object?>? headers)
    {
        if (headers == null) return 1;
        if (!headers.TryGetValue(AttemptHeader, out var raw)) return 1;
        try
        {
            switch (raw)
            {
                case byte[] bytes:
                {
                    var asString = Encoding.UTF8.GetString(bytes);
                    if (int.TryParse(asString, out var parsed) && parsed > 0)
                        return parsed;
                    break;
                }
                case string s when int.TryParse(s, out var parsed2) && parsed2 > 0:
                    return parsed2;
            }
        }
        catch
        {
            // ignored
        }

        return 1;
    }

    private async Task PublishWithAttemptAsync(string queue, byte[] body, int attempt, CancellationToken ct)
    {
        if (_channel == null) return;
        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object>
            {
                [AttemptHeader] = Encoding.UTF8.GetBytes(attempt.ToString())
            }!
        };
        await _channel.BasicPublishAsync(
            string.Empty,
            queue,
            false,
            props,
            body,
            ct);
    }

    private async Task<ProcessingResult> SendToApiAsync(string? httpMethod, string? urlSuffix, JsonObject payload,
        CancellationToken cancellationToken)
    {
        if (urlSuffix == null)
            return new ProcessingResult(false, "urlSuffix is null");

        if (string.IsNullOrWhiteSpace(httpMethod))
            return new ProcessingResult(false, "httpMethod is null or empty");

        try
        {
            using var httpClient = new HttpClient();
            using var jsonContent = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var response = httpMethod.ToUpperInvariant() == "PUT"
                ? await httpClient.PutAsync(_apiEndpoint + urlSuffix, jsonContent, cancellationToken)
                : await httpClient.PostAsync(_apiEndpoint + urlSuffix, jsonContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                throw new Exception(reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Caught {ExceptionType} exception: {Message}.",
                ex.GetType().Name, ex.Message);
            if (_apiTarget == nameof(ApiBaseUrlSettings.Codespace))
            {
                await _codespaceAwaker.Awake(cancellationToken);
            }
            var reason = $"Exception: {ex.Message}";
            return new ProcessingResult(false, reason);
        }
        return new ProcessingResult(true, null);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disposing a connection to RabbitMQ");
        _channel?.Dispose();
        _connection?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    private readonly record struct ProcessingResult(bool Success, string? ErrorReason);
}