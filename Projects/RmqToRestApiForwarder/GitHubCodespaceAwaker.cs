using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RmqToRestApiForwarder;

public class GitHubCodespaceAwaker(IOptions<GitHubCodespaceSettings> codespaceSettings, CryptService cryptService, ILogger<GitHubCodespaceAwaker> logger)
{
    private enum RequestState
    {
        Idle,
        Requested,
        InProgress
    }

    private const int TimeoutSecs = 30; // Duration to keep InProgress state before resetting to Idle

    private RequestState _state = RequestState.Idle;
    private readonly object _stateLock = new();
    private Timer? _resetTimer;

    public async Task Awake(CancellationToken cancellationToken)
    {
        // Only continue if currently Idle
        lock (_stateLock)
        {
            if (_state != RequestState.Idle)
            {
                logger.LogDebug("Awake() call ignored because current state is {State}", _state);
                return;
            }
            _state = RequestState.Requested;
            logger.LogInformation("Codespace awake sequence started. State -> Requested");
        }

        const string shortestPassword = "my.shortest.password";

        var codespaceName = cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.CodespaceName, shortestPassword);
        var urlExpanded = codespaceSettings.Value.StartUrl.Replace("{codespace}", codespaceName, StringComparison.OrdinalIgnoreCase);
        var authorizationValue = "Bearer " + cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.Token, shortestPassword);

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", authorizationValue);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            var payload = JsonSerializer.Serialize(new { codespace_name = codespaceName });
            using var jsonContent = new StringContent(payload, Encoding.UTF8, "application/json");

            logger.LogInformation("Sending GitHub Codespace start request for '{CodespaceName}'", codespaceName);
            var response = await httpClient.PostAsync(urlExpanded, jsonContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                throw new Exception(reason);
            }
            logger.LogInformation("GitHub Codespace start request accepted by server (HTTP {Status}).", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Codespace start request encountered {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
        }
        finally
        {
            // Transition to InProgress and schedule reset
            lock (_stateLock)
            {
                _state = RequestState.InProgress;
                logger.LogInformation("Codespace awaker state -> InProgress. Will reset to Idle in {Timeout}s", TimeoutSecs);
                _resetTimer?.Dispose();
                _resetTimer = new Timer(_ =>
                {
                    lock (_stateLock)
                    {
                        _state = RequestState.Idle;
                        logger.LogInformation("Codespace awaker state reset to Idle after timeout of {Timeout}s", TimeoutSecs);
                    }
                }, null, TimeSpan.FromSeconds(TimeoutSecs), Timeout.InfiniteTimeSpan);
            }
        }
    }
}