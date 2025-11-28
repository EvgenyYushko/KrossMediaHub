using AlinaKrossManager.BackgroundServices;
using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using AlinaKrossManager.Controllers;
using AlinaKrossManager.Helpers;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Protos.GoogleGeminiService;
using Quartz;
using Telegram.Bot;
using static AlinaKrossManager.Constants.AppConstants;
using static AlinaKrossManager.Jobs.Helpers.JobHelper;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ITelegramBotClient>(provider =>
{
	var token = builder.Configuration.GetValue<string>(TELEGRAM_BOT_TOKEN) ?? Environment.GetEnvironmentVariable(TELEGRAM_BOT_TOKEN);
	return new TelegramBotClient(token);
});

builder.Services.AddSingleton<IGenerativeLanguageModel>(provider =>
{
	var client = provider.GetService<GeminiService.GeminiServiceClient>();
	return new GoogleGenerativeLanguageModel(client);
});

builder.Services.AddSingleton(provider =>
{
	var geminiModel = provider.GetService<IGenerativeLanguageModel>();
	var conversationService = provider.GetService<ConversationService>();
	var hostedInvarment = provider.GetService<IWebHostEnvironment>();
	//var telegramService = provider.GetService<TelegramService>();

	//var PageAccessToken = "IGAALt2MgjsilBZAFNKQ040cWM4TTl1Mkt5dFF5WXloekRJbGdwU1hqTmNBVFkzQU9mV29NWVk0X2hPdXZALZA3Q2ZA09VYXFyUnhrY3QweElHNVBnaExaS0c5TDJxS2RjN3lydlFYS0JzRUhkaGVyaTJyOTJFaHpYMVh0S2p1ZAy1xMAZDZD";
	var accessToken = "IGAAQEMxhZAfcFBZAFJYRUlNOVVwWTlFb3h1ZAnJ1cWZA5eFNUTlpIX1NHYkwyenp0a0NTNTN6WWlxR3BVVkV2aEJqSGpaNHlNQWg5ck5GQnlQajJwMC1VS3pEREJNb1Nxc19RclpGdURuaWl0TWVzMGtGUlVJYmNjNnR0SmxKcHZAyZAwZDZD";
	return new InstagramService(accessToken, geminiModel, conversationService, hostedInvarment);
});

builder.Services.AddSingleton(provider =>
{
	const string IDENTIFIER_CLUE_SKY = "alinakross.bsky.social";
	const string APP_PASSWORD_BLUE_SKY = "d4an-bvic-ssrd-r663";
	var geminiModel = provider.GetService<IGenerativeLanguageModel>();
	//var telegramService = provider.GetService<TelegramService>();

	return new BlueSkyService(IDENTIFIER_CLUE_SKY, APP_PASSWORD_BLUE_SKY, geminiModel);
});

builder.Services.AddSingleton(provider =>
{
	var longLiveToken = "EAAY5A6MrJHgBPZBQrANTL62IRrEdPNAFCTMBBRg1PraciiqfarhG98YZCdGO9wxEhza3uk7BE56KEDGtWHagB8hgaUsQUFiQ3x3uhPZBbZBDZC6BtGsmoQURUAO7aVSEktmGeer6TtQZC9PWA6ZAM0EEgInZAFtWmjkz7ow4IDsCl7B55O80n2VW9wsNil3Nh8F5lkRfbIpj";
	var geminiModel = provider.GetService<IGenerativeLanguageModel>();
	//var telegramService = provider.GetService<TelegramService>();

	return new FaceBookService(longLiveToken, geminiModel);
});

builder.Services.AddSingleton<TelegramService>();
builder.Services.AddSingleton<TelegramManager>();
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddSingleton<PublicTelegramChanel>();
builder.Services.AddSingleton<PrivateTelegramChanel>();

builder.Services.AddHostedService<HealthCheckBackgroundService>();

var channel = GrpcChannel.ForAddress("https://google-services-kdg8.onrender.com", new GrpcChannelOptions
{
	HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler())
});

builder.Services.AddSingleton(new GeminiService.GeminiServiceClient(channel));

builder.Services.AddQuartz(q =>
{
	q.SchedulerId = "MainScheduler";
	q.UseMicrosoftDependencyInjectionJobFactory();

	// Настройка пула потоков (ограничиваем количество одновременно выполняемых задач)
	q.UseDefaultThreadPool(tp =>
	{
		tp.MaxConcurrency = 1; // Ограничиваем до 1 одновременно выполняемой задачи
	});

	var timeZone = TimeZoneHelper.GetTimeZoneInfo();

	foreach (var job in JobSettings)
	{
		var jobKey = new JobKey($"{job.Key}Job");
		q.AddJob(job.Type, jobKey, j => j.StoreDurably());

		if (!job.Castum)
		{
			q.AddTrigger(t => t
				.WithIdentity($"{job.Key}Trigger")
				.ForJob(jobKey)
				.WithCronSchedule($"{job.Time}", x => x.InTimeZone(timeZone))
			);
		}
	}
});

// Запуск Quartz как фоновой службы
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Получаем IScheduler для проверки расписаний
builder.Services.AddTransient(provider =>
{
	var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
	return schedulerFactory.GetScheduler().GetAwaiter().GetResult();
});
builder.Services.AddTransient<ScheduleInspectorService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
	var serviceProvider = scope.ServiceProvider;
	var scheduler = serviceProvider.GetRequiredService<IScheduler>();

	if (!scheduler.IsStarted)
	{
		await scheduler.Start();
		Console.WriteLine("Критическое предупреждение: Планировщик был запущен вручную!");
	}
	else
	{
		Console.WriteLine($"Планировщик запущен: {scheduler.IsStarted}, Режим ожидания: {scheduler.InStandbyMode}");
	}

	var inspector = scope.ServiceProvider.GetRequiredService<ScheduleInspectorService>();
	Task.Run(async () => await inspector.PrintScheduleInfo()).Wait();

	var config = serviceProvider.GetRequiredService<IConfiguration>();
	var telegramClient = serviceProvider.GetRequiredService<ITelegramBotClient>();

	if (app.Environment.IsDevelopment())
	{
		var telegramService = serviceProvider.GetRequiredService<TelegramManager>();
		var bot = new TelegramBotController(telegramClient, telegramService);
		await bot.RunLocalTest();
	}
}
app.UseStaticFiles();
// ВНИМАНИЕ: Закомментировать или удалить в Cloud Run/Kubernetes
// app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

//async Task ConfigureWebhookAsync(ITelegramBotClient telegramClient, IConfiguration config, bool local)
//{
//	try
//	{
//		// 1. --- DeleteWebhookAsync заменяется на SendRequest<bool> ---
//		if (local)
//		{
//			// Вызываем SendRequest с DeleteWebhookRequest (ответ - bool)
//			await telegramClient.SendRequest(new DeleteWebhookRequest(), CancellationToken.None);

//			// Получаем информацию после удаления, чтобы убедиться
//			var whAfterDelete = await telegramClient.SendRequest<WebhookInfo>(new GetWebhookInfoRequest(), CancellationToken.None);
//			Log($"Webhook успешно удален. URL: {whAfterDelete.Url}");
//		}
//		else
//		{
//			// 2. --- GetWebhookInfoAsync заменяется на SendRequest<WebhookInfo> ---
//			// Вызываем SendRequest с GetWebhookInfoRequest (ответ - WebhookInfo)
//			var wh = await telegramClient.SendRequest<WebhookInfo>(new GetWebhookInfoRequest(), CancellationToken.None);

//			// Определяем целевой URL для Cloud Run
//			// (Предполагаем, что AppOptions:Domain установлен через переменные окружения Cloud Build)
//			var urlSite = "https://krossmediahub-783314764029.europe-west1.run.app";//config?[$"AppOptions:Domain"];
//			var webhookUrl = $"{urlSite}/api/update"; // Используйте ваш маршрут!

//			if (wh.Url != webhookUrl)
//			{
//				// 3. --- SetWebhookAsync заменяется на SendRequest<bool> ---
//				Log($"Установка Webhook: {webhookUrl}");
//				await telegramClient.SendRequest(new SetWebhookRequest { Url = webhookUrl }, CancellationToken.None);
//				Log("Webhook успешно установлен.");
//			}
//			else
//			{
//				Log($"Webhook уже установлен по адресу: {webhookUrl}");
//			}
//		}
//	}
//	catch (Exception ex)
//	{
//		Log(ex, "Критическая ошибка при настройке Telegram Webhook.");
//	}
//}

