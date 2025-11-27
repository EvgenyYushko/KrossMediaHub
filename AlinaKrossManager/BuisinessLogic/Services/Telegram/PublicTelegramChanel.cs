using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace AlinaKrossManager.BuisinessLogic.Services.Telegram
{
	public class PublicTelegramChanel : SocialBaseService
	{
		public const long CHANEL_ID = -1003122621836;
		private const string PRIVATE_CHANEL_LINK = "https://t.me/+d2M9V8rIF-BhNWMy";
		private readonly TelegramService _telegramService;

		public PublicTelegramChanel(TelegramService telegramService , IGenerativeLanguageModel generativeLanguageModel)
			: base(generativeLanguageModel)
		{
			_telegramService = telegramService;
		}

		public override string ServiceName => "PublicTelegramChanel";

		public Task<Message> SendMainButtonMessage()
		{
			var inlineKeyboard = new InlineKeyboardMarkup(new[]
			{
				new[]{InlineKeyboardButton.WithUrl("💋 Open me 🔞", PRIVATE_CHANEL_LINK)}
			});

			return _telegramService.SendMessage(CHANEL_ID, text: "Exclusive content ❤️‍🔥", replyMarkup: inlineKeyboard);
		}

		protected override string GetBaseDescriptionPrompt(string base64Img)
		{
			return "Придумай одно самое красивое, флиртующее, краткое описание на английском языке, возможно добавь эмодзи, " +
				"к посту в публичном эротическом Telegram канале, под постом с фотографией. " +
				$"Вот фотография: {base64Img}" +
				$"\n\n Формат ответа: Верни только одно готовое описание, можешь добавить пару релевантных хештегов, " +
				$"без всякого рода пояснений, комментариев и ковычек и экранирования. ";
		}
	}
}
