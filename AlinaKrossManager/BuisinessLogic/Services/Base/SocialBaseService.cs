using AlinaKrossManager.Services;

namespace AlinaKrossManager.BuisinessLogic.Services.Base
{
	public abstract class SocialBaseService
	{
		protected readonly IGenerativeLanguageModel _generativeLanguageModel;
		private readonly TelegramService _telegramService;

		protected SocialBaseService(IGenerativeLanguageModel generativeLanguageModel, TelegramService telegramService)
		{
			_generativeLanguageModel = generativeLanguageModel;
			_telegramService = telegramService;
		}

		protected abstract string GetBaseDescriptionPrompt(string base64Img);
		protected abstract string ServiceName { get; }

		public virtual async Task<string> TryCreateDescription(string replayText, List<string> images)
		{
			string description = replayText;
			if (string.IsNullOrEmpty(replayText) && images.Count > 0)
			{
				try
				{
					var promptForeDescriptionPost = GetBaseDescriptionPrompt(images.FirstOrDefault());
					return string.IsNullOrEmpty(replayText) ? await _generativeLanguageModel.GeminiRequest(promptForeDescriptionPost) : replayText;
				}
				catch (Exception e)
				{
					Console.WriteLine($"Ошикбка создания описания поста в {ServiceName}: " + e.Message);
				}
			}

			return description ?? "";
		}
	}
}
