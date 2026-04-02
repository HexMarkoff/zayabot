using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Types;
using ZayaDesertRequestCreatorBot.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                    ?? context.Configuration["TelegramBotToken"];

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Telegram bot token is missing. Set TELEGRAM_BOT_TOKEN env var or TelegramBotToken in appsettings.json.");
        }

        var storagePath = Path.Combine(context.HostingEnvironment.ContentRootPath, "data", "desserts.json");
        var logsPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "logs.json");

        services.AddSingleton(new TelegramBotClient(token));
        services.AddSingleton(new StorageService(storagePath));
        services.AddSingleton(new LogService(logsPath));
        services.AddSingleton<DessertService>();
        services.AddSingleton<ParserService>();
        services.AddSingleton<UserStateService>();
        services.AddSingleton<StepHandler>();
        services.AddSingleton<BotService>();
    })
    .Build();

var botService = host.Services.GetRequiredService<BotService>();
await botService.StartAsync(CancellationToken.None);
await host.RunAsync();
