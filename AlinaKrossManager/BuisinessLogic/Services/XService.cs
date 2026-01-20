using System.Text;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;
using Newtonsoft.Json;
using Tweetinvi;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class XService : SocialBaseService
	{
		private readonly TwitterClient _twitterClient;

		public XService(IGenerativeLanguageModel generativeLanguageModel, TwitterClient twitterClient) : base(generativeLanguageModel)
		{
			_twitterClient = twitterClient;
		}

		public override string ServiceName => "XService";

		/// <summary>
		/// Метод для публикации ТОЛЬКО текста
		/// </summary>
		public async Task<bool> CreateTextPost(string text)
		{
			try
			{
				var tweetRequest = new TweetV2Request
				{
					Text = text
				};

				return await SendTweetV2Async(tweetRequest);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при отправке текстового твита: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Метод для публикации текста с картинками
		/// </summary>
		public async Task<bool> CreateImagePost(string caption, List<string> base64Files)
		{
			// Твиттер разрешает максимум 4 картинки на один твит
			var filesToUpload = base64Files?.Take(4).ToList();

			try
			{
				var uploadedMediaIds = new List<string>();

				// 1. Загрузка картинок (V1.1 API)
				if (filesToUpload != null && filesToUpload.Any())
				{
					foreach (var base64String in filesToUpload)
					{
						try
						{
							// А. Очистка Base64
							string cleanBase64 = base64String;
							if (cleanBase64.Contains(","))
							{
								cleanBase64 = cleanBase64.Split(',')[1];
							}

							// Б. Конвертация
							byte[] imageBytes = Convert.FromBase64String(cleanBase64);

							Console.WriteLine("Загрузка изображения в X...");

							// В. Загрузка
							var uploadedMedia = await _twitterClient.Upload.UploadTweetImageAsync(imageBytes);

							if (uploadedMedia != null)
							{
								Console.WriteLine($"Фото загружено. ID: {uploadedMedia.Id}");
								uploadedMediaIds.Add(uploadedMedia.Id.ToString());
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine($"Не удалось загрузить одно из фото: {ex.Message}");
						}
					}
				}

				// 2. Формирование запроса V2
				var tweetRequest = new TweetV2Request
				{
					Text = caption
				};

				// Если удалось загрузить картинки, прикрепляем их
				if (uploadedMediaIds.Count > 0)
				{
					tweetRequest.Media = new TweetV2Media
					{
						MediaIds = uploadedMediaIds
					};
				}

				// 3. Отправка через общий метод
				return await SendTweetV2Async(tweetRequest);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Общая ошибка метода публикации с фото: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Приватный метод для непосредственной отправки JSON в Twitter V2 API
		/// </summary>
		private async Task<bool> SendTweetV2Async(TweetV2Request tweetRequest)
		{
			try
			{
				Console.WriteLine("Публикуем пост в X (V2 API)...");

				var result = await _twitterClient.Execute.AdvanceRequestAsync(request =>
				{
					request.Query.Url = "https://api.twitter.com/2/tweets";
					request.Query.HttpMethod = Tweetinvi.Models.HttpMethod.POST;

					var jsonBody = JsonConvert.SerializeObject(tweetRequest);
					request.Query.HttpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
				});

				if (result.Response.IsSuccessStatusCode)
				{
					Console.WriteLine("Успех! Твит опубликован.");
					return true;
				}

				// Этот код редко выполняется для 403, так как выбрасывается Exception, 
				// но оставим для мягких ошибок
				Console.WriteLine($"Ошибка (без исключения): {result.Response.StatusCode}");
				return false;
			}
			catch (Tweetinvi.Exceptions.TwitterException twEx)
			{
				// ВОТ ЗДЕСЬ ПРАВИЛЬНАЯ ОБРАБОТКА
				Console.WriteLine($"!!! ОШИБКА X API (Exception) !!!");
				Console.WriteLine($"Status Code: {twEx.StatusCode}");

				// Пытаемся достать JSON с текстом ошибки (например, "Duplicate content")
				// В разных версиях Tweetinvi это может быть в Content или в Response.Content
				var errorJson = twEx.Content;
				Console.WriteLine($"JSON Body: {errorJson}");

				// Дополнительная проверка на дубликат
				if (errorJson != null && errorJson.Contains("duplicate"))
				{
					Console.WriteLine("⚠️ Причина: ДУБЛИКАТ КОНТЕНТА. Твит с таким текстом уже был.");
					// Можно вернуть true, чтобы джоб не считал это провалом
					return true;
				}

				return false;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Критическая ошибка при запросе: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Метод для публикации видео (MP4) из Base64
		/// </summary>
		public async Task<bool> CreateVideoPost(string caption, string base64Video)
		{
			if (string.IsNullOrEmpty(base64Video))
			{
				Console.WriteLine("Ошибка: передана пустая строка Base64 для видео.");
				return false;
			}

			try
			{
				// 1. Подготовка байтов
				string cleanBase64 = base64Video;
				if (cleanBase64.Contains(","))
				{
					cleanBase64 = cleanBase64.Split(',')[1];
				}

				byte[] videoBytes = Convert.FromBase64String(cleanBase64);
				Console.WriteLine($"Размер видео: {videoBytes.Length / 1024 / 1024} MB. Начинаем загрузку...");

				// 2. Загрузка видео (Upload)
				var uploadedMedia = await _twitterClient.Upload.UploadTweetVideoAsync(videoBytes);

				if (uploadedMedia == null || uploadedMedia.Id == 0)
				{
					Console.WriteLine("Ошибка: Не удалось загрузить видео файл.");
					return false;
				}

				Console.WriteLine($"Видео загружено (ID: {uploadedMedia.Id}). Ожидаем процессинг...");

				// 3. ВАЖНО: Ручное ожидание обработки (вместо WaitForMediaProcessingAsync)
				var isProcessed = false;
				var attempts = 0;

				while (!isProcessed && attempts < 20) // Максимум 20 попыток (около 1-2 минут)
				{
					// Получаем актуальный статус медиа
					var mediaStatus = await _twitterClient.Upload.GetVideoProcessingStatusAsync(uploadedMedia);

					// Если ProcessingInfo == null, значит видео уже готово (или это картинка/гифка, которая не требует процессинга)
					if (mediaStatus.ProcessingInfo == null)
					{
						isProcessed = true;
						break;
					}

					var state = mediaStatus.ProcessingInfo.State;

					if (state == "succeeded")
					{
						isProcessed = true;
						Console.WriteLine("Процессинг видео успешно завершен.");
					}
					else if (state == "failed")
					{
						var error = mediaStatus.ProcessingInfo.Error;
						Console.WriteLine($"Ошибка процессинга Twitter: {error?.Code} - {error?.Message}");
						return false;
					}
					else
					{
						// Если "pending" или "in_progress"
						attempts++;
						// Twitter сам говорит, сколько миллисекунд подождать до следующей проверки
						var waitTime = mediaStatus.ProcessingInfo.CheckAfterInMilliseconds;
						// Если вдруг вернул 0 или null, ждем 2 секунды по умолчанию
						await Task.Delay(waitTime > 0 ? waitTime : 2000);
					}
				}

				if (!isProcessed)
				{
					Console.WriteLine("Превышено время ожидания обработки видео.");
					return false;
				}

				// 4. Формирование запроса V2
				var tweetRequest = new TweetV2Request
				{
					Text = caption,
					Media = new TweetV2Media
					{
						MediaIds = new List<string> { uploadedMedia.Id.ToString() }
					}
				};

				// 5. Отправка твита
				return await SendTweetV2Async(tweetRequest);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Общая ошибка метода публикации видео: {ex.Message}");
				return false;
			}
		}

		public static string GetBaseDescriptionPrompt(string base64Img)
		{
			return "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в X(Twitter) под постом с фотографией. " +
				$"А так же придумай не более 15 хештогов, они должны соответствовать " +
				$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
				$"Вот само изображение: {base64Img}" +
				$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
				$"без всякого рода ковычек и экранирования. " +
				$"Пример ответа: ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence " +
				$"#neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";
		}
	}

	public class TweetV2Request
	{
		[JsonProperty("text")]
		public string Text { get; set; }

		[JsonProperty("media", NullValueHandling = NullValueHandling.Ignore)]
		public TweetV2Media Media { get; set; }
	}

	public class TweetV2Media
	{
		[JsonProperty("media_ids")]
		public List<string> MediaIds { get; set; }
	}
}