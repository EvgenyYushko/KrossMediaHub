using System.Text;
using System.Text.Json;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Models;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace AlinaKrossManager.BackgroundServices
{
	// Сервис для работы с Telegram ботом
	public class TelegramBotService : BackgroundService
	{
		private readonly ITelegramBotClient _botClient;
		private readonly ILogger<TelegramBotService> _logger;
		private readonly TelegramService _telegramService;

		public TelegramBotService(ITelegramBotClient botClient, ILogger<TelegramBotService> logger, TelegramService telegramService)
		{
			_botClient = botClient;
			_logger = logger;
			_telegramService = telegramService;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			try
			{
				// Получаем информацию о боте
				var me = await _botClient.SendRequest<User>(new GetMeRequest(), stoppingToken);
				_logger.LogInformation($"Бот @{me.Username} запущен!");

				// Минимальная настройка ReceiverOptions
				var receiverOptions = new ReceiverOptions
				{
					AllowedUpdates = []
				};

				// Базовая версия StartReceiving
				_botClient.StartReceiving(
					HandleUpdateAsync,
					HandleErrorAsync,
					receiverOptions,
					stoppingToken
				);

				_logger.LogInformation("Бот начал работу");

				// Бесконечное ожидание
				while (!stoppingToken.IsCancellationRequested)
				{
					await Task.Delay(1000, stoppingToken);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка в боте");
			}
		}

		private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
		{
			await _telegramService.HandleUpdateAsync(botClient, update, ct);
		}

		private Task HandleErrorAsync(ITelegramBotClient botClient, Exception error, CancellationToken ct)
		{
			_logger.LogError(error, "Ошибка Telegram бота");
			return Task.CompletedTask;
		}
	}
}
