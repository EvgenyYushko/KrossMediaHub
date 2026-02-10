using AlinaKrossManager.Services;
using Grpc.Core;
using Protos.GoogleGeminiService;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class GoogleGenerativeLanguageModel : IGenerativeLanguageModel
	{
		private readonly GeminiService.GeminiServiceClient _geminiServiceClient;
		private readonly string _token;
		private string[] modelsToTry =
		{
			"imagen-4.0-ultra-generate-001",
			"imagen-4.0-generate-001",
			"imagen-4.0-fast-generate-001",
			"imagen-3.0-generate-002",
			"gemini-2.5-flash-image" // nano banana
		};

		public GoogleGenerativeLanguageModel(GeminiService.GeminiServiceClient geminiServiceClient, string token)
		{
			_geminiServiceClient = geminiServiceClient;
			_token = token;
		}

		public async Task<string> RequestWithChatAsync(List<ChatMessage> messages, string systemInstruction = null)
		{
			var rsponce = await _geminiServiceClient.RequestWithChatAsync(new()
			{
				SystemInstruction = systemInstruction,
				History =
				{
					messages.Select(m => new ChatMessage
					{
						Role = m.Role,
						Text = m.Text
					})
				}
			}, AddTokenToHeaders());

			return rsponce.GeneratedText;
		}		

		public async Task<string> GeminiAudioToText(string base64Iaudio)
		{
			var prompt = "Распознай что сказано на этом аудио файле и верни только его содержимое, на том же языке, без всяких дополнителных кооментариев.";
			var response = await _geminiServiceClient.RequestWithAudioAsync(new()
			{
				Prompt = { new Prompt { Text = prompt } },
				Base64Idata = base64Iaudio
			}, AddTokenToHeaders());
			return response.GeneratedText;
		}

		public async Task<string> GeminiTextToSpeechEn(string text)
		{
			var response = await _geminiServiceClient.SynthesizeSpeechAsync(new()
			{
				Text = text,
				LanguageName = "en-US-Studio-O",
				LanguageCode = "en-US",
				AudioEncoding = "MP3"
			}, AddTokenToHeaders());

			return response.AudioContent;
		}

		public async Task<string> GeminiTextToSpeechRu(string text)
		{
			var response = await _geminiServiceClient.SynthesizeSpeechAsync(new()
			{
				Text = text,
				LanguageName = "ru-RU-Standard-A",
				LanguageCode = "ru-RU",
				AudioEncoding = "MP3"
			}, AddTokenToHeaders());

			return response.AudioContent;
		}

		public async Task<string> GeminiRequest(string prompt)
		{
			var response = await _geminiServiceClient.RequestAsync(new Prompt { Text = prompt }, AddTokenToHeaders());
			return response.GeneratedText;
		}

		public async Task<string> GeminiRequestWithImage(string prompt, string base64Image)
		{
			var response = await _geminiServiceClient.RequestWithImageAsync(new()
			{
				Prompt = { new Prompt { Text = prompt } },
				Base64Idata = base64Image
			}, AddTokenToHeaders());
			return response.GeneratedText;
		}

		public async Task<string> GeminiRequestWithVideo(string prompt, string base64video)
		{
			var response = await _geminiServiceClient.RequestWithVideoAsync(new()
			{
				Prompt = { new Prompt { Text = prompt } },
				Base64Idata = base64video
			}, AddTokenToHeaders());
			return response.GeneratedText;
		}

		public async Task<List<string>> GeminiRequestGenerateImage(string prompt, int countImage = 1/*, int maxAttemptsPerModel = 2, int delayBetweenAttempts = 1000*/)
		{
			var maxAttemptsPerModel = 2;
			var delayBetweenAttempts = 1000;

			foreach (var model in modelsToTry)
			{
				for (int attempt = 1; attempt <= maxAttemptsPerModel; attempt++)
				{
					try
					{
						Console.WriteLine($"🔄 Попытка {attempt}/{maxAttemptsPerModel} с моделью: {model}");

						var response = await _geminiServiceClient.RequestGenerateImageAsync(new()
						{
							Prompt = { new Prompt { Text = prompt } },
							MimeType = "image/jpeg",
							AspectRatio = "3:4",
							SampleCount = countImage,
							SelectedModel = model
						}, AddTokenToHeaders());

						var images = response.GeneratedImagesBase64.ToList();

						if (images.Count > 0)
						{
							Console.WriteLine($"✅ Успех! Модель {model} сгенерировала {images.Count} изображений");
							return images;
						}

						Console.WriteLine($"⚠️ Модель {model} вернула 0 изображений");

						// Задержка перед следующей попыткой (кроме последней)
						if (attempt < maxAttemptsPerModel)
						{
							await Task.Delay(delayBetweenAttempts);
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine($"❌ Ошибка (модель: {model}, попытка: {attempt}): {ex.Message}");

						if (attempt < maxAttemptsPerModel)
						{
							await Task.Delay(delayBetweenAttempts);
						}
					}
				}

				Console.WriteLine($"🔁 Переход к следующей модели...");
			}

			Console.WriteLine($"💥 Все модели исчерпаны. Не удалось сгенерировать изображения.");
			return new List<string>();
		}

		private Metadata AddTokenToHeaders()
		{
			var headers = new Metadata();
			headers.Add("x-goog-api-key", _token);
			return headers;
		}
	}

}
