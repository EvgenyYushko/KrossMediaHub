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

		public Task<string> GenerateNanoBanana(string prompt, string aspectRatio, string imageSize, List<string> base64Images)
		{
			return _generativeLanguageModel.GenetareNanoBanana(prompt, aspectRatio, imageSize, base64Images);
		}

		public Task<MemoryStream?> TextToSpeech(string text, string voiceName, string model)
		{
			return _generativeLanguageModel.TextToSpeech(text, voiceName, model);
		}

		public Task<string> SpeechToText(string base64Audio)
		{
			return _generativeLanguageModel.GeminiAudioToText(base64Audio);
		}

		public Task<MemoryStream> GenerateVoiceAsync(string text, VoiceSettings settings)
		{
			var model = "gemini-3.1-flash-tts-preview";

			// 1. Получаем описание темпа
			string paceDescription = VoiceConstants.PaceMap.TryGetValue(settings.Pace, out var desc)
				? $"{settings.Pace}: {desc}"
				: settings.Pace;

			// 2. Собираем финальный промпт (Template)
			var prompt = $@"Read the following transcript based on the audio profile and director's note.

			# Audio Profile
			{settings.AudioProfile}

			# Director's note
			Style: {settings.Style}. 
			Pace: {paceDescription}. 
			Accent: {settings.Accent}.

			## Scene:
			{settings.Scene}

			## Sample Context:
			{settings.SampleContext}

			## Transcript:
			{text}";

			// 3. Отправляем в AI
			return TextToSpeech(prompt, settings.VoiceName, model);
		}
	}
	public static class VoiceConstants
	{
		// Описания для темпа (Pace)
		public static readonly Dictionary<string, string> PaceMap = new()
		{
			{ "Natural", "Natural conversational pace." },
			{ "Rapid Fire", "Fast, energetic, no dead air. Sentences overlap slightly." },
			{ "The Drift", "Slow, liquid, zero urgency. Long pauses for breath." },
			{ "Staccato", "Short, clipped sentences with distinct pauses between words." }
		};

		public static readonly List<string> Accents = new()
		{
			"Neutral", "American (Gen)", "American (Valley)", "American (South)",
			"British (RP)", "British (Brixton)", "Transatlantic", "Australian"
		};

		public static readonly List<string> Styles = new()
		{
			"Whisper", "Empathetic", "Professional", "Cheerful", "Sad", "Angry"
		};
	}

	public class VoiceSettings
	{
		public string AudioProfile { get; set; } = "Default Profile";
		public string VoiceName { get; set; } = "Aoede";
		public string Style { get; set; } = "Whisper";
		public string Pace { get; set; } = "Natural";
		public string Accent { get; set; } = "American (Gen)";
		public string Scene { get; set; } = "";
		public string SampleContext { get; set; } = "";
	}
}
