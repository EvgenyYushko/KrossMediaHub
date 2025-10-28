using AlinaKrossManager.BuisinessLogic.Services;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static AlinaKrossManager.Helpers.Logger;
using System.Text;
using System.Text.Json;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Models;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace AlinaKrossManager.Controllers
{
	[ApiExplorerSettings(IgnoreApi = true)]
	[ApiController]
	[Route("api/update")]
	public class TelegramBotController : ControllerBase
	{
		private readonly ITelegramBotClient _telegramBotClient;
		private readonly TelegramService _telegramService;

		public TelegramBotController(ITelegramBotClient telegramBotClient, TelegramService telegramService)
		{
			_telegramBotClient = telegramBotClient;
			_telegramService = telegramService;
		}

		[HttpPost]
		public async Task<IActionResult> Post([FromBody] Update update)
		{
			try
			{
				Log($"{update.Message?.Text}");
				await _telegramService.HandleUpdateAsync(_telegramBotClient, update, CancellationToken.None); // Передайте update вашему обработчику
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
			await _telegramService.HandleUpdateAsync(botClient, update, ct);
		}

		private Task HandleErrorAsync(ITelegramBotClient botClient, Exception error, CancellationToken ct)
		{
			Log(error, "Ошибка Telegram бота");
			return Task.CompletedTask;
		}
	}
}
