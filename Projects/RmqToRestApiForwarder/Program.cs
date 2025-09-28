using NLog;
using NLog.Extensions.Logging;
using RabbitMQ.Client;
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
    .UseWindowsService(options => { options.ServiceName = "RabbitMQ To REST API Forwarder"; })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<RabbitMqServerSettings>(config.GetSection("RabbitMQ:Service"));
        services.Configure<RabbitMqMessageDeliverySettings>(config.GetSection("RabbitMQ:MessageDelivery"));
        services.Configure<ApiBaseUrlSettings>(config.GetSection("ApiBaseUrl"));
        services.Configure<GitHubCodespaceSettings>(config.GetSection("GitHubCodespace"));

        services.AddSingleton<CryptService>();

        services.AddSingleton<GitHubCodespaceAwaker>();
        services.AddHostedService<RabbitMqConsumerService>();
    });

await builder.Build().RunAsync();
