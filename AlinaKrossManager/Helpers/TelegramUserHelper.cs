using Telegram.Bot.Types;

namespace AlinaKrossManager.Helpers
{
	public static class TelegramUserHelper
	{
		public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source,
			Func<TSource, TKey> keySelector)
		{
			var seenKeys = new HashSet<TKey>();
			foreach (var element in source)
			{
				if (seenKeys.Add(keySelector(element)))
				{
					yield return element;
				}
			}
		}

		public static string GetMsgText(this Message message)
		{
			return message?.Text ?? message?.Caption;
		}

		public static Voice GetVoice(this Message message)
		{
			return message.Voice ?? message.ReplyToMessage?.Voice;
		}


		public static bool IsCommand(this string msg, string commandName)
		{
			return msg.Equals($"/{commandName}") /*|| msg.Equals($"/{commandName}@{BotSettings.BOT_USER_NAME}")*/;
		}

		public static string ToBlockQuote(this string text)
		{
			return "<blockquote expandable>" + text + "</blockquote>";
		}
	}
}
