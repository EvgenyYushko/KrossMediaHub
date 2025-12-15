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
		private readonly IServiceScopeFactory _scopeFactory;

		public TelegramBotController(ITelegramBotClient telegramBotClient
			, IServiceScopeFactory scopeFactory
			)
		{
			_telegramBotClient = telegramBotClient;
			_scopeFactory = scopeFactory;
		}

		[HttpPost]
		public async Task<IActionResult> Post([FromBody] Update update)
		{
			try
			{
				Log($"{update.Message?.Text}");

				// Запускаем задачу, и уже ВНУТРИ неё создаем Scope
				_ = Task.Run(async () =>
				{
					try
					{
						// Создаем Scope здесь, чтобы он жил пока работает этот поток
						using (var scope = _scopeFactory.CreateScope())
						{
							var telegramManager = scope.ServiceProvider.GetRequiredService<TelegramManager>();
							await telegramManager.HandleUpdateAsync(update, CancellationToken.None);
						}
					}
					catch (Exception ex)
					{
						Log($"Ошибка в фоновой обработке: {ex}");
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString());
			}

			Log($"Метод Post запущен в фоне");

			// Возвращаем ответ Телеграму мгновенно, не дожидаясь окончания обработки
			return Ok();
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
			// Создаем Scope (область видимости) для КАЖДОГО сообщения
			using (var scope = _scopeFactory.CreateScope())
			{
				// Теперь мы можем безопасно получить TelegramManager
				// Он будет создан заново для этого сообщения и уничтожен в конце блока using
				var telegramManager = scope.ServiceProvider.GetRequiredService<TelegramManager>();

				// Вызываем ваш метод обработки
				// (Вам нужно будет немного адаптировать TelegramManager, чтобы метод был public)
				await telegramManager.HandleUpdateAsync(update, ct);
			}
		}

		private Task HandleErrorAsync(ITelegramBotClient botClient, Exception error, CancellationToken ct)
		{
			Log(error, "Ошибка Telegram бота");
			return Task.CompletedTask;
		}
	}
}
