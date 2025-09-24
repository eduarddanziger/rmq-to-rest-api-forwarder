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
        Started,
        InProgress
    }

    private RequestState _state = RequestState.Idle;

    public async Task Awake(CancellationToken cancellationToken)
    {
        const string shortestPassword = "my.shortest.password";

        var codespaceName
            = cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.CodespaceName, shortestPassword);

        var urlExpanded = codespaceSettings.Value.StartUrl.Replace("{codespace}", codespaceName, StringComparison.OrdinalIgnoreCase);

        var authorizationValue = "Bearer " + cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.Token, shortestPassword);

        try
        {
            using var httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Add("Authorization", authorizationValue);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            var payload = JsonSerializer.Serialize(new { codespace_name = codespaceName });
            using var jsonContent = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(urlExpanded, jsonContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                throw new Exception(reason);
            }

            _state = RequestState.Started;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Caught {ExceptionType} exception: {Message}.",
                ex.GetType().Name, ex.Message);
        }
    }
}