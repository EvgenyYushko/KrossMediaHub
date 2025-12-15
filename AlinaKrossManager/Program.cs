using AlinaKrossManager.BackgroundServices;
using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using AlinaKrossManager.Controllers;
using AlinaKrossManager.DataAccess;
using AlinaKrossManager.Helpers;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.EntityFrameworkCore;
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

	var token = builder.Configuration.GetValue<string>(INSTAGRAM_BOT_TOKEN) ?? Environment.GetEnvironmentVariable(INSTAGRAM_BOT_TOKEN);
	return new InstagramService(token, geminiModel, conversationService, hostedInvarment);
});

builder.Services.AddSingleton(provider =>
{
	var geminiModel = provider.GetService<IGenerativeLanguageModel>();

	var id = builder.Configuration.GetValue<string>(IDENTIFIER_BLUE_SKY) ?? Environment.GetEnvironmentVariable(IDENTIFIER_BLUE_SKY);
	var pass = builder.Configuration.GetValue<string>(APP_PASSWORD_BLUE_SKY) ?? Environment.GetEnvironmentVariable(APP_PASSWORD_BLUE_SKY);
	return new BlueSkyService(id, pass, geminiModel);
});

builder.Services.AddSingleton(provider =>
{
	var geminiModel = provider.GetService<IGenerativeLanguageModel>();
	var longLiveToken = builder.Configuration.GetValue<string>(FACE_BOOK_LONG_TOKEN) ?? Environment.GetEnvironmentVariable(FACE_BOOK_LONG_TOKEN);
	return new FaceBookService(longLiveToken, geminiModel);
});

builder.Services.AddSingleton<TelegramService>();
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddSingleton<PublicTelegramChanel>();
builder.Services.AddSingleton<PrivateTelegramChanel>();
// сервисы зависящие от БД
builder.Services.AddScoped<PostService>();
builder.Services.AddScoped<TelegramManager>();

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

var connectionString = builder.Configuration.GetValue<string>(DB_URL_POSTGRESQL) ?? Environment.GetEnvironmentVariable(DB_URL_POSTGRESQL);
if (string.IsNullOrEmpty(connectionString))
{
	throw new Exception("Отсутствует строка подключения!");
}

// Разрешает сохранять даты в любом формате без ошибок
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

builder.Services.AddDbContext<AppDbContext>(options =>
options.UseNpgsql(connectionString, npgsqlOptions =>
{
	npgsqlOptions.MigrationsAssembly("AlinaKrossManager");
	npgsqlOptions.EnableRetryOnFailure(
		maxRetryCount: 5,
		maxRetryDelay: TimeSpan.FromSeconds(30),
		errorCodesToAdd: null);
}));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}
else
{
	var dbContext = app.Services.GetRequiredService<AppDbContext>();
	dbContext.Database.Migrate();
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

	var inspector = serviceProvider.GetRequiredService<ScheduleInspectorService>();
	Task.Run(async () => await inspector.PrintScheduleInfo()).Wait();


	if (app.Environment.IsDevelopment())
	{
		//var config = serviceProvider.GetRequiredService<IConfiguration>();
		var telegramClient = serviceProvider.GetRequiredService<ITelegramBotClient>();
		var serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
		var bot = new TelegramBotController(telegramClient, serviceScopeFactory);
		await bot.RunLocalTest();
	}
}
app.UseStaticFiles();
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
//			var urlSite = APP_URL;//config?[$"AppOptions:Domain"];
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

