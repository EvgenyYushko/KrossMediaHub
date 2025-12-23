using AlinaKrossManager.Services;

namespace AlinaKrossManager.BuisinessLogic.Facades
{
	public class AiFacade
	{
		private readonly IGenerativeLanguageModel _generativeLanguageModel;

		public AiFacade(IGenerativeLanguageModel generativeLanguageModel)
		{
			_generativeLanguageModel = generativeLanguageModel;
		}

		public async Task<string> TryCreateDescription(string replayText, List<string> images, string promptForeDescriptionPost)
		{
			Console.WriteLine("Вошли в метод генерации описание...");
			Console.WriteLine("Текущее описание: " + replayText);
			string description = replayText;
			if (string.IsNullOrEmpty(replayText) && images.Count > 0)
			{
				try
				{
					Console.WriteLine("Начниаем генерировать описание...");
					var res = string.IsNullOrEmpty(replayText) ? await _generativeLanguageModel.GeminiRequest(promptForeDescriptionPost) : replayText;
					Console.WriteLine("Сгенерированное описание: " + res);
					return res;
				}
				catch (Exception e)
				{
					Console.WriteLine($"Ошикбка создания описания поста: " + e.Message);
					Console.WriteLine(e.ToString());
				}
			}

			return description ?? "";
		}

		public Task<List<string>> GenerateImage(string promptImg, int countImage)
		{
			return _generativeLanguageModel.GeminiRequestGenerateImage(promptImg, countImage);
		}
	}
}
