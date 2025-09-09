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

        services.Configure<RabbitMqSettings>(config.GetSection("RabbitMQ"));
        services.Configure<ApiBaseUrlSettings>(config.GetSection("ApiBaseUrl"));

        services.AddSingleton<IConnectionFactory>(_ =>
            new ConnectionFactory
            {
                HostName = config["RabbitMQ:HostName"] ?? string.Empty,
                UserName = config["RabbitMQ:UserName"] ?? string.Empty,
                Password = config["RabbitMQ:Password"] ?? string.Empty
            });

        services.AddHostedService<RabbitMqConsumerService>();
    });

await builder.RunConsoleAsync();
