using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Controllers;
using AlinaKrossManager.Services;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Protos.GoogleGeminiService;
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

builder.Services.AddSingleton<IGenerativeLanguageModel>(provider =>
{
	var client = provider.GetService<GeminiService.GeminiServiceClient>();
	return new GoogleGenerativeLanguageModel(client);
});

builder.Services.AddSingleton(provider =>
{
	var geminiModel = provider.GetService<IGenerativeLanguageModel>();
	var conversationService = provider.GetService<ConversationService>();
	//var PageAccessToken = "IGAALt2MgjsilBZAFNKQ040cWM4TTl1Mkt5dFF5WXloekRJbGdwU1hqTmNBVFkzQU9mV29NWVk0X2hPdXZALZA3Q2ZA09VYXFyUnhrY3QweElHNVBnaExaS0c5TDJxS2RjN3lydlFYS0JzRUhkaGVyaTJyOTJFaHpYMVh0S2p1ZAy1xMAZDZD";
	var accessToken = "IGAAQEMxhZAfcFBZAFM0NHhuZAjRRcnpkWEZANNGtiZAkZA2ZA1NUME8yYXFHMndXU29GUEVpUDh0bmVSeV9WSEs3M3Q4Sk93TWUzb0RWcXNYOGktekhFQ2x3YVE1Y0ZAOWm9fTEpDTXRiQlBkNXpzc0Y5dndfcS0tcm1veHNNTUUzSmRydwZDZD";
	return new InstagramService(accessToken, geminiModel, conversationService);
});

builder.Services.AddSingleton(provider =>
{
	const string IDENTIFIER_CLUE_SKY = "alinakross.bsky.social";
	const string APP_PASSWORD_BLUE_SKY = "d4an-bvic-ssrd-r663";
	return new BlueSkyService(IDENTIFIER_CLUE_SKY, APP_PASSWORD_BLUE_SKY);
});

builder.Services.AddSingleton(provider =>
{
	var longLiveToken = "EAAY5A6MrJHgBPZBQrANTL62IRrEdPNAFCTMBBRg1PraciiqfarhG98YZCdGO9wxEhza3uk7BE56KEDGtWHagB8hgaUsQUFiQ3x3uhPZBbZBDZC6BtGsmoQURUAO7aVSEktmGeer6TtQZC9PWA6ZAM0EEgInZAFtWmjkz7ow4IDsCl7B55O80n2VW9wsNil3Nh8F5lkRfbIpj";
	return new FaceBookService(longLiveToken);
});

builder.Services.AddSingleton<TelegramService>();
builder.Services.AddSingleton<TelegramManager>();

var channel = GrpcChannel.ForAddress("https://google-services-kdg8.onrender.com", new GrpcChannelOptions
{
	HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler())
});

builder.Services.AddSingleton(new GeminiService.GeminiServiceClient(channel));

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
		var telegramService = serviceProvider.GetRequiredService<TelegramManager>();
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

