using HttpRequestProcessor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using RmqToRestApiForwarder;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Trace);

        LogManager.Setup()
            .SetupExtensions(ext => ext.RegisterLayoutRenderer<NativeThreadIdLayoutRenderer>("native-thread-id"))
            .LoadConfigurationFromSection(context.Configuration.GetSection("NLog"));

        logging.AddNLog();
    })
    .UseWindowsService(options =>
    {
        options.ServiceName = "RabbitMQ To REST API Forwarder";
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<RabbitMqSettings>(config.GetSection("RabbitMQ"));
        services.Configure<ApiBaseUrlSettings>(config.GetSection("ApiBaseUrl"));

        services.AddSingleton<IConnectionFactory>(sp =>
            new ConnectionFactory()
            {
                HostName = config["RabbitMQ:HostName"] ?? string.Empty,
                UserName = config["RabbitMQ:UserName"] ?? string.Empty,
                Password = config["RabbitMQ:Password"] ?? string.Empty
            });

        // Add hosted service
        services.AddHostedService<RabbitMqConsumerService>();
    });

await builder.RunConsoleAsync();



// Configuration classes
#pragma warning disable CA1050
public record RabbitMqSettings
#pragma warning restore CA1050
{
    public string HostName { get; init; } = string.Empty;
    public string QueueName { get; init; } = string.Empty;
}
#pragma warning disable CA1050
public record ApiBaseUrlSettings
#pragma warning restore CA1050
{
    public string GitHubCodespaceUrl { get; init; } = string.Empty;
    public string AzureUrl { get; init; } = string.Empty;
    public string LocalVmUrl { get; init; } = string.Empty;

}
