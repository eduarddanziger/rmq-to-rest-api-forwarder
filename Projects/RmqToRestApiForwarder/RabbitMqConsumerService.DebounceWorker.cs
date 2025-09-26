using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace RmqToRestApiForwarder;

public partial class RabbitMqConsumerService
{
    private sealed record PendingMessage(
        ulong DeliveryTag,
        byte[] Body,
        int Attempt,
        string? HttpMethod,
        string? UrlSuffix,
        JsonObject Payload,
        DateTime UpdateDate
    );

    private sealed class DebounceWorker
    {
        private readonly string _name;
        private readonly TimeSpan _window;
        private readonly Func<PendingMessage, CancellationToken, Task> _processMessageAsync;
        private readonly Func<PendingMessage, CancellationToken, ValueTask> _ignoreMessageAsync;
        private readonly ILogger _logger;

        private readonly Channel<PendingMessage> _queue =
            Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions
                { SingleReader = true, SingleWriter = false });

        private readonly CancellationToken _stopToken;

        public DebounceWorker(string name, TimeSpan window,
            Func<PendingMessage, CancellationToken, Task> processMessageAsync,
            Func<PendingMessage, CancellationToken, ValueTask> ignoreMessageAsync,
            ILogger logger,
            CancellationToken stopToken)
        {
            _name = name;
            _window = window;
            _processMessageAsync = processMessageAsync;
            _ignoreMessageAsync = ignoreMessageAsync;
            _logger = logger;
            _stopToken = stopToken;
            _ = Task.Run(RunAsync, stopToken);
        }

        public ValueTask EnqueueAsync(PendingMessage message)
        {
            return _queue.Writer.WriteAsync(message, _stopToken);
        }

        private async Task RunAsync()
        {
            var reader = _queue.Reader;
            PendingMessage? carry = null;

            while (!_stopToken.IsCancellationRequested)
            {
                PendingMessage head;
                if (carry != null)
                {
                    head = carry;
                    carry = default;
                }
                else
                {
                    try
                    {
                        head = await reader.ReadAsync(_stopToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                var last = head;

                // Only compare message timestamps; do not use wall clock
                while (reader.TryRead(out var next))
                {
                    if ((next.UpdateDate - last.UpdateDate) <= _window)
                    {
                        await _ignoreMessageAsync(last, _stopToken); // ignore previous last
                        last = next; // keep the most recent within the window
                        continue;
                    }

                    // Next is outside the window; keep it for next iteration
                    carry = next;
                    break;
                }

                // Process the chosen last message
                try
                {
                    await _processMessageAsync(last, _stopToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Name}] Error while processing debounced message.", _name);
                    try
                    {
                        await _ignoreMessageAsync(last, _stopToken);
                    }
                    catch
                    {
                        /* ignored */
                    }
                }
            }
        }
    }
}