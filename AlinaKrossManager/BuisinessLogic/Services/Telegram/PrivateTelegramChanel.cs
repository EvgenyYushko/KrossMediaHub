using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;

namespace AlinaKrossManager.BuisinessLogic.Services.Telegram
{
	public class PrivateTelegramChanel : SocialBaseService
	{
		public const long CHANEL_ID = -1003388317919;
		
		public override string ServiceName => "PrivateTelegramChanel";

		public PrivateTelegramChanel(IGenerativeLanguageModel generativeLanguageModel)
				: base(generativeLanguageModel)
		{
		}

		protected override string GetBaseDescriptionPrompt(string base64Img)
		{
			return "Придумай одно красивое, флиртующее, краткое описание на английском языке, возможно добавь эмодзи, к посту в приватном эротическом Telegram канале, под постом с фотографией. " +
				"Без хештегов." +
				$"Вот фотография: {base64Img}" +
				$"\n\n Формат ответа: Верни только одно готовое описание, " +
				$"без всякого рода пояснений, комментариев и ковычек и экранирования. ";
		}
	}
}
