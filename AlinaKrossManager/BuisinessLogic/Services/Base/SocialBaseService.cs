using AlinaKrossManager.Services;

namespace AlinaKrossManager.BuisinessLogic.Services.Base
{
	public abstract class SocialBaseService
	{
		protected readonly IGenerativeLanguageModel _generativeLanguageModel;
		private readonly TelegramService _telegramService;

		protected SocialBaseService(IGenerativeLanguageModel generativeLanguageModel)
		{
			_generativeLanguageModel = generativeLanguageModel;
		}

		protected abstract string GetBaseDescriptionPrompt(string base64Img);
		protected abstract string ServiceName { get; }

		public virtual async Task<string> TryCreateDescription(string replayText, List<string> images)
		{
			Console.WriteLine("Вошли в метод генерации описание...");
			Console.WriteLine("Текущее описание: " + replayText);
			string description = replayText;
			if (string.IsNullOrEmpty(replayText) && images.Count > 0)
			{
				try
				{
					Console.WriteLine("Начниаем генерировать описание...");
					var promptForeDescriptionPost = GetBaseDescriptionPrompt(images.FirstOrDefault());
					var res = string.IsNullOrEmpty(replayText) ? await _generativeLanguageModel.GeminiRequest(promptForeDescriptionPost) : replayText;
					Console.WriteLine("Начинаем задержку в 20 секунд...");					
					await Task.Delay(TimeSpan.FromSeconds(20));
					Console.WriteLine("Задржка окончена...");
					Console.WriteLine("Сгенерированное описание: " + res);
					return res;
				}
				catch (Exception e)
				{
					Console.WriteLine($"Ошикбка создания описания поста в {ServiceName}: " + e.Message);
					Console.WriteLine(e.ToString());
				}
			}

			return description ?? "";
		}
	}
}
