using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AlinaKrossManager.Services
{
	public interface IHandleMessageService
	{
		public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, UpdateType type, CancellationToken cancellationToken);
	}
}
