﻿using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Headers;
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

    private readonly int _timeoutSeconds = codespaceSettings.Value.TimeoutSeconds;

    private RequestState _state = RequestState.Idle;
    private readonly object _stateLock = new();
    private Timer? _resetTimer;

    // Thread-safe state property
    private RequestState State
    {
        get { lock (_stateLock) return _state; }
        set { lock (_stateLock) _state = value; }
    }

    // Atomic compare-and-set helper

    public async Task Awake(CancellationToken cancellationToken)
    {
        if (State != RequestState.Idle)
        {
            logger.LogDebug("Awake() call ignored because current state is {State}", State);
            return;
        }
        State = RequestState.Requested;

        logger.LogInformation("Codespace awake sequence started. State -> Requested");

        const string shortestPassword = "my.shortest.password";

        var codespaceName = cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.CodespaceName, shortestPassword);
        var urlExpanded = codespaceSettings.Value.StartUrl.Replace("{codespace}", codespaceName, StringComparison.OrdinalIgnoreCase);
        var authorizationValue = "Bearer " + cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.Token, shortestPassword);

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", authorizationValue);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("rmq-to-rest-api-forwarder", "1.0"));

            var payload = JsonSerializer.Serialize(new { codespace_name = codespaceName });
            using var jsonContent = new StringContent(payload, Encoding.UTF8, "application/json");

            logger.LogInformation("Sending GitHub Codespace start request for '{CodespaceName}'", codespaceName);
            var response = await httpClient.PostAsync(urlExpanded, jsonContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Log full response body for diagnostics
                var body = string.Empty;
                try
                {
                    body = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception readEx)
                {
                    logger.LogDebug(readEx, "Failed reading error response body");
                }
                var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                logger.LogWarning("GitHub Codespace start request failed: {Reason}. Body: {Body}", reason, body);
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
            State = RequestState.InProgress;
            logger.LogInformation("Codespace awaker state -> InProgress. Will reset to Idle in {Timeout}s", _timeoutSeconds);

            Timer? oldTimer;
            lock (_stateLock)
            {
                oldTimer = _resetTimer;
                _resetTimer = new Timer(_ =>
                {
                    State = RequestState.Idle;
                    logger.LogInformation("Codespace awaker state reset to Idle after timeout of {Timeout}s", _timeoutSeconds);
                }, null, TimeSpan.FromSeconds(_timeoutSeconds), Timeout.InfiniteTimeSpan);
            }
            // ReSharper disable once MethodHasAsyncOverload
            oldTimer?.Dispose();
        }
    }
}