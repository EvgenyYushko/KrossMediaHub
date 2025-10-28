using AlinaKrossManager.BuisinessLogic.Services;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
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
	}
}
