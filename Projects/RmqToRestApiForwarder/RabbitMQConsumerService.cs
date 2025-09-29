using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using static RmqToRestApiForwarder.Contracts.MessageFields;

namespace RmqToRestApiForwarder;

public partial class RabbitMqConsumerService : BackgroundService
{
    // Durable retry settings
    private const string AttemptHeader = "x-attempt";

    private static readonly string[] _validTargets =
    [
        nameof(ApiBaseUrlSettings.Azure), nameof(ApiBaseUrlSettings.Local), nameof(ApiBaseUrlSettings.Codespace)
    ];

    private static readonly string _validTargetsAsString = string.Join(
        ", ",
        Array.ConvertAll(_validTargets, v => $"\"{v}\""));

    private readonly string _apiEndpoint;
    private readonly string _apiTarget;
    private readonly GitHubCodespaceAwaker _codespaceAwaker;
    private readonly IConnectionFactory _connectionFactory;
    private readonly string _failedQueueName;
    private readonly ILogger<RabbitMqConsumerService> _logger;

    private readonly int _maxRetryAttempts;
    private readonly string _queueName;
    private readonly TimeSpan _retryDelay;
    private readonly string _retryQueueName;
    private readonly TimeSpan _volumeDebounceWindow;
    private DebounceWorker? _captureDebouncer;
    private IChannel? _channel;

    private IConnection? _connection;

    // Debounce workers for sound-volume events
    private DebounceWorker? _renderDebouncer;

    public RabbitMqConsumerService(IOptions<RabbitMqServerSettings> rmqServerSettings,
        IOptions<RabbitMqMessageDeliverySettings> rmqMessageDeliverySettings,
        IOptions<ApiBaseUrlSettings> apiSettings,
        GitHubCodespaceAwaker codespaceAwaker,
        ILogger<RabbitMqConsumerService> logger)
    {
        var recoverySeconds = Math.Max(0, rmqServerSettings.Value.NetworkRecoveryIntervalInSeconds);
        _connectionFactory = new ConnectionFactory
        {
            HostName = rmqServerSettings.Value.HostName,
            UserName = rmqServerSettings.Value.UserName,
            Password = rmqServerSettings.Value.Password,
            Port = rmqServerSettings.Value.Port,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(recoverySeconds)
        };
        _codespaceAwaker = codespaceAwaker;
        _logger = logger;
        _queueName = rmqServerSettings.Value.QueueName;
        _retryQueueName = _queueName + ".retry";
        _failedQueueName = _queueName + ".failed";

        _maxRetryAttempts = rmqMessageDeliverySettings.Value.MaxRetryAttempts;
        _retryDelay = TimeSpan.FromSeconds(rmqMessageDeliverySettings.Value.RetryDelayInSeconds);
        _volumeDebounceWindow = TimeSpan.FromMilliseconds(
            Math.Max(0, rmqMessageDeliverySettings.Value.VolumeChangeEventDebouncingWindowInMilliseconds));

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
            "Consumer service parameters initialized: Queue \"{Queue}\" RetryQueue \"{RetryQueue}\" FailedQueue \"{FailedQueue}\" Target REST API \"{ApiTarget}\" MaxRetryAttempts {MaxAttempts} RetryDelaySeconds {RetryDelay} VolumeDebounceWindowMs {Debounce} RecoveryIntervalSeconds {Recovery}",
            _queueName, _retryQueueName, _failedQueueName, _apiTarget, _maxRetryAttempts,
            _retryDelay.TotalSeconds, _volumeDebounceWindow.TotalMilliseconds, recoverySeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consumer service initialization started...");

        await EnsureConnectionAndTopologyReadyAsync(cancellationToken);

        await StartConsumerAsync(cancellationToken);

        // 3) Keep the service alive until shutdown (auto-recovery handles transient drops)
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    // Ensures connection + channel + required queues exist. Retries until success or cancellation
    private async Task EnsureConnectionAndTopologyReadyAsync(CancellationToken cancellationToken)
    {
        var connectRetryDelays = new[]
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5)
        };
        var maxConnectRetryDelay = TimeSpan.FromSeconds(10);
        var connectRetryIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(null, cancellationToken);

                await _channel.QueueDeclareAsync(
                    _queueName,
                    true,
                    false,
                    false,
                    cancellationToken: cancellationToken);

                var retryArgs = new Dictionary<string, object>
                {
                    ["x-dead-letter-exchange"] = string.Empty,
                    ["x-dead-letter-routing-key"] = _queueName,
                    ["x-message-ttl"] = (int)_retryDelay.TotalMilliseconds
                };
                await _channel.QueueDeclareAsync(
                    _retryQueueName,
                    true,
                    false,
                    false,
                    retryArgs!,
                    cancellationToken: cancellationToken);

                var failedArgs = new Dictionary<string, object>
                {
                    ["x-message-ttl"] = (int)TimeSpan.FromHours(24).TotalMilliseconds
                };
                await _channel.QueueDeclareAsync(
                    _failedQueueName,
                    true,
                    false,
                    false,
                    failedArgs!,
                    cancellationToken: cancellationToken);

                await VerifyIfQueuesCreatedOtherwiseThrowExceptionAsync([
                    new QueueInfo(_queueName),
                    new QueueInfo(_retryQueueName),
                    new QueueInfo(_failedQueueName)
                ], cancellationToken);

                _logger.LogInformation("Connection and topology are ready.");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    _channel?.Dispose();
                }
                catch
                {
                    /* ignored */
                }

                try
                {
                    _connection?.Dispose();
                }
                catch
                {
                    /* ignored */
                }

                _channel = null;
                _connection = null;

                var nextDelay = connectRetryIndex < connectRetryDelays.Length
                    ? connectRetryDelays[connectRetryIndex]
                    : maxConnectRetryDelay;
                connectRetryIndex = Math.Min(connectRetryIndex + 1, connectRetryDelays.Length);

                _logger.LogWarning(ex, "Connection/topology setup failed. Retrying in {DelaySeconds}s",
                    nextDelay.TotalSeconds);
                await Task.Delay(nextDelay, cancellationToken);
            }
    }

    private async Task VerifyIfQueuesCreatedOtherwiseThrowExceptionAsync(IEnumerable<QueueInfo> queues, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var q in queues)
            {
                await _channel!.QueueDeclarePassiveAsync(q.Name, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            throw new Exception("Topology verification failed: one or more queues are missing.", ex);
        }
    }

    // Start the consumer on the current channel
    private async Task StartConsumerAsync(CancellationToken cancellationToken)
    {
        _renderDebouncer = new DebounceWorker(
            "VolumeRenderChanged",
            _volumeDebounceWindow,
            async (msg, ct) =>
            {
                _logger.LogInformation(
                    "Debouncing chosen VolumeRenderChanged message at {UpdateDate:o} to be PROCESSED",
                    msg.UpdateDate);
                await ProcessMessageAsync(msg, ct);
            },
            (msg, ct) =>
            {
                _logger.LogInformation("Debouncing chosen VolumeRenderChanged message at {UpdateDate:o} to be IGNORED",
                    msg.UpdateDate);
                return _channel!.BasicAckAsync(msg.DeliveryTag, false, ct);
            },
            _logger,
            cancellationToken);

        _captureDebouncer = new DebounceWorker(
            "VolumeCaptureChanged",
            _volumeDebounceWindow,
            async (msg, ct) =>
            {
                _logger.LogInformation(
                    "Debouncing chosen VolumeCaptureChanged message at {UpdateDate:o} to be PROCESSED.",
                    msg.UpdateDate);
                await ProcessMessageAsync(msg, ct);
            },
            (msg, ct) =>
            {
                _logger.LogInformation("Debouncing chosen VolumeCaptureChanged message at {UpdateDate:o} to be IGNORED",
                    msg.UpdateDate);
                return _channel!.BasicAckAsync(msg.DeliveryTag, false, ct);
            },
            _logger,
            cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (_, deliveryEvent) =>
        {
            try
            {
                var eventBody = deliveryEvent.Body.ToArray();
                var eventHeaderAttempt = GetAttempt(deliveryEvent.BasicProperties.Headers);
                var eventMessage = JsonNode.Parse(eventBody)!.AsObject();

                // Parse event info (optional fields)
                var deviceMessageTypeAsInt = eventMessage[DeviceMessageType]?.GetValue<int?>();
                var updateDateAsString = eventMessage[UpdateDate]?.GetValue<string>();
                var updateDate = ParseToUtc(updateDateAsString);
                var deviceEventType = deviceMessageTypeAsInt.HasValue
                    ? (DeviceEventType)deviceMessageTypeAsInt.Value
                    : DeviceEventType.Confirmed;

                // Http-specific fields
                var httpRequest = eventMessage[HttpRequest]?.GetValue<string>();
                var urlSuffix = eventMessage[UrlSuffix]?.GetValue<string>();
                eventMessage.Remove(HttpRequest);
                eventMessage.Remove(UrlSuffix);

                _logger.LogInformation(
                    "Received a message with HTTP request (Attempt {Attempt}/{MaxAttempts}): \"{Method}\", suffix \"{Suffix}\", payload:\n{Payload}",
                    eventHeaderAttempt, _maxRetryAttempts, httpRequest, urlSuffix,
                    eventMessage.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                var pending = new PendingMessage(
                    deliveryEvent.DeliveryTag,
                    eventBody,
                    eventHeaderAttempt,
                    httpRequest,
                    urlSuffix,
                    updateDate);

                switch (deviceEventType)
                {
                    case DeviceEventType.VolumeRenderChanged when _renderDebouncer != null:
                        await _renderDebouncer!.EnqueueAsync(pending);
                        _logger.LogInformation("Enqueued {Type} message at {UpdateDate:o} for debounce.",
                            deviceEventType,
                            updateDate);
                        break;
                    case DeviceEventType.VolumeCaptureChanged when _captureDebouncer != null:
                        await _captureDebouncer!.EnqueueAsync(pending);
                        _logger.LogInformation("Enqueued {Type} message at {UpdateDate:o} for debounce.",
                            deviceEventType,
                            updateDate);
                        break;
                    case DeviceEventType.Confirmed:
                    case DeviceEventType.Discovered:
                    case DeviceEventType.Detached:
                    case DeviceEventType.DefaultRenderChanged:
                    case DeviceEventType.DefaultCaptureChanged:
                    default:
                        await ProcessMessageAsync(pending, cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected exception during message processing. Moving message to failed queue.");
                try
                {
                    if (_channel != null)
                    {
                        var body = deliveryEvent.Body.ToArray();
                        var attempt2 = GetAttempt(deliveryEvent.BasicProperties.Headers);

                        await PublishWithAttemptAsync(_failedQueueName, body, attempt2, cancellationToken);
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

        await _channel!.BasicConsumeAsync(
            _queueName,
            false,
            consumer,
            cancellationToken);

        _logger.LogInformation("Consumer started on channel.");
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
            if (_apiTarget == nameof(ApiBaseUrlSettings.Codespace)) await _codespaceAwaker.Awake(cancellationToken);

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

    private async Task ProcessMessageAsync(PendingMessage msg, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing message (Attempt {Attempt}/{MaxAttempts}): \"{Method}\", suffix \"{Suffix}\". UpdateDate: {UpdateDate:o}",
            msg.Attempt, _maxRetryAttempts, msg.HttpMethod, msg.UrlSuffix, msg.UpdateDate);

        var eventMessage = JsonNode.Parse(msg.Body)!.AsObject();

        var result = await SendToApiAsync(msg.HttpMethod, msg.UrlSuffix, eventMessage, ct);

        if (result.Success)
        {
            await _channel!.BasicAckAsync(msg.DeliveryTag, false, ct);
            _logger.LogInformation("Message processed successfully on attempt {Attempt}", msg.Attempt);
        }
        else
        {
            if (msg.Attempt < _maxRetryAttempts)
            {
                await PublishWithAttemptAsync(_retryQueueName, msg.Body, msg.Attempt + 1, ct);
                await _channel!.BasicAckAsync(msg.DeliveryTag, false, ct);
                _logger.LogWarning(
                    "Attempt {Attempt} failed. Scheduled retry attempt {NextAttempt} after {Delay}s. Reason: {Reason}",
                    msg.Attempt, msg.Attempt + 1, _retryDelay.TotalSeconds, result.ErrorReason);
            }
            else
            {
                await PublishWithAttemptAsync(_failedQueueName, msg.Body, msg.Attempt, ct);
                await _channel!.BasicAckAsync(msg.DeliveryTag, false, ct);
                _logger.LogError("Attempt {Attempt} (max) failed. Routed to failed queue. Reason: {Reason}",
                    msg.Attempt, result.ErrorReason);
            }
        }
    }

    private static DateTime ParseToUtc(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return DateTime.MinValue;
        if (DateTimeOffset.TryParse(input, out var dto)) return dto.UtcDateTime;
        if (DateTime.TryParse(input, out var dt)) return dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;

        return DateTime.MinValue;
    }

    private readonly record struct ProcessingResult(bool Success, string? ErrorReason);
    private readonly record struct QueueInfo(string Name);
}