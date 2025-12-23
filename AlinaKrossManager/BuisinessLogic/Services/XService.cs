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

		public async Task<bool> CreatePostPost(string caption, List<string> base64Files)
		{
			// Твиттер разрешает максимум 4 картинки на один твит
			var filesToUpload = base64Files?.Take(4).ToList();

			try
			{
				// 1. Проверка авторизации (чисто для логов)
				//var user = await _twitterClient.Users.GetAuthenticatedUserAsync();
				//Console.WriteLine($"Публикация от имени: {user.Name} (@{user.ScreenName})");

				var uploadedMediaIds = new List<string>();

				// 2. Загрузка картинок (если есть)
				if (filesToUpload != null && filesToUpload.Any())
				{
					foreach (var base64String in filesToUpload)
					{
						try
						{
							// А. Очистка Base64 от префикса "data:image/..."
							string cleanBase64 = base64String;
							if (cleanBase64.Contains(","))
							{
								cleanBase64 = cleanBase64.Split(',')[1];
							}

							// Б. Конвертация в байты
							byte[] imageBytes = Convert.FromBase64String(cleanBase64);

							Console.WriteLine("Загрузка изображения...");

							// В. Загрузка
							// В твоей версии просто передаем байты. 
							// Тайм-аут мы настроили глобально в Config, этого достаточно.
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
							// Продолжаем, чтобы попытаться выложить хотя бы текст или другие фото
						}
					}
				}

				// 3. Формирование запроса V2
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

				// 4. Публикация твита (вручную через V2, так как в либе нет метода PostTweetV2)
				Console.WriteLine("Публикуем пост...");

				var result = await _twitterClient.Execute.AdvanceRequestAsync(request =>
				{
					request.Query.Url = "https://api.twitter.com/2/tweets";
					request.Query.HttpMethod = Tweetinvi.Models.HttpMethod.POST;

					var jsonBody = JsonConvert.SerializeObject(tweetRequest);
					request.Query.HttpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
				});

				// 5. Проверка результата
				if (result.Response.IsSuccessStatusCode)
				{
					Console.WriteLine("Успех! Твит опубликован.");
					return true;
				}
				else
				{
					Console.WriteLine($"Ошибка публикации: {result.Response.StatusCode}");
					Console.WriteLine($"Детали ошибки: {result.Content}");
					return false;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Общая ошибка метода: {ex.Message}");
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
