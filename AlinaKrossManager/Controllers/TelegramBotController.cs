using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.Models;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using static AlinaKrossManager.Helpers.Logger;

namespace AlinaKrossManager.Controllers
{
	[ApiExplorerSettings(IgnoreApi = true)]
	[ApiController]
	[Route("api/update")]
	public class TelegramBotController : ControllerBase
	{
		private readonly ITelegramBotClient _telegramBotClient;
		private readonly TelegramManager _telegramManager;

		public TelegramBotController(ITelegramBotClient telegramBotClient, TelegramManager telegramService)
		{
			_telegramBotClient = telegramBotClient;
			_telegramManager = telegramService;
		}

		[HttpPost]
		public async Task<IActionResult> Post([FromBody] Update update)
		{
			try
			{
				Log($"{update.Message?.Text}");
				 _ = Task.Run(() => _telegramManager.HandleUpdateAsync(update, CancellationToken.None));
			}
			catch (Exception ex)
			{
				Log(ex.ToString());
			}

			return Ok(); // Важно: всегда возвращайте 200 OK быстро
		}

		public async Task RunLocalTest()
		{
			try
			{
				// Получаем информацию о боте
				var stoppingToken = CancellationToken.None;
				var me = await _telegramBotClient.SendRequest<User>(new GetMeRequest(), stoppingToken);
				Log($"Бот @{me.Username} запущен!");

				// Минимальная настройка ReceiverOptions
				var receiverOptions = new ReceiverOptions
				{
					AllowedUpdates = []
				};

				// Базовая версия StartReceiving
				_telegramBotClient.StartReceiving(
					HandleUpdateAsync,
					HandleErrorAsync,
					receiverOptions,
					stoppingToken
				);

				Log("Бот начал работу");

				// Бесконечное ожидание
				while (!stoppingToken.IsCancellationRequested)
				{
					await Task.Delay(1000, stoppingToken);
				}
			}
			catch (Exception ex)
			{
				Log(ex, "Ошибка в боте");
			}
		}

		private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
		{
			await _telegramManager.HandleUpdateAsync(update, ct);
		}

		private Task HandleErrorAsync(ITelegramBotClient botClient, Exception error, CancellationToken ct)
		{
			Log(error, "Ошибка Telegram бота");
			return Task.CompletedTask;
		}
	}
}
