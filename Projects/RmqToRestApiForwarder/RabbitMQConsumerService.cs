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

                using var httpClient = new HttpClient();
                var jsonContent = new StringContent(
                    message.ToJsonString(),
                    Encoding.UTF8,
                    "application/json"
                );


                var response = httpRequest?.ToUpper() == "PUT"
                    ? await httpClient.PutAsync(_apiEndpoint + urlSuffix, jsonContent, cancellationToken)
                    : await httpClient.PostAsync(_apiEndpoint + urlSuffix, jsonContent, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    await _channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                    _logger.LogInformation("Processed message with {Method}", httpRequest);
                }
                else
                {
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
                    _logger.LogWarning("API rejected message. Status: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {   
                _logger.LogError(ex, "Message processing failed");
                if (_channel != null)
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Dispose();
        _connection?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}

