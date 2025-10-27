using AlinaKrossManager.Models;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class TelegramService
	{
		public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
		{
			if (update.Message?.Text is not { } text) return;

			var response = text.ToLower() switch
			{
				"/start" => "Привет! Я Alina Kross Manager .",
				_ => $"Вы сказали: {text}"
			};

			await botClient.SendRequest<Message>(
				new SendMessageRequest(update.Message.Chat.Id, response),
				ct
			);
		}
	}
}
