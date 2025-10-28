using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Controllers;
using Telegram.Bot;
using static AlinaKrossManager.Constants.AppConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ITelegramBotClient>(provider =>
{
	var token = builder.Configuration.GetValue<string>(TELEGRAM_BOT_TOKEN) ?? Environment.GetEnvironmentVariable(TELEGRAM_BOT_TOKEN);
	return new TelegramBotClient(token);
});

builder.Services.AddSingleton<TelegramService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
	var serviceProvider = scope.ServiceProvider;
	var config = serviceProvider.GetRequiredService<IConfiguration>();
	var telegramClient = serviceProvider.GetRequiredService<ITelegramBotClient>();

	if (app.Environment.IsDevelopment())
	{
		var telegramService = serviceProvider.GetRequiredService<TelegramService>();
		var bot = new TelegramBotController(telegramClient, telegramService);
		await bot.RunLocalTest();
	}
}

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

