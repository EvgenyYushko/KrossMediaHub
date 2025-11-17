using System.Text.Json;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class FaceBookService : SocialBaseService
	{
		private readonly string _longLivedUserToken;
		private string _userId = "122108650443054121";
		string _pageIdToPublish = "872506142593246";

		protected override string ServiceName => "FaceBook";

		public FaceBookService(string longLivedUserToken, IGenerativeLanguageModel generativeLanguageModel)
			: base(generativeLanguageModel)
		{
			_longLivedUserToken = longLivedUserToken;
		}

		private async Task<string> GetPageAccessTokenAsync()
		{
			string accountsUrl = $"https://graph.facebook.com/v24.0/{_userId}/accounts?access_token={_longLivedUserToken}";

			using (var httpClient = new HttpClient())
			{
				var response = await httpClient.GetAsync(accountsUrl);
				response.EnsureSuccessStatusCode();
				string result = await response.Content.ReadAsStringAsync();

				var accountsData = JsonSerializer.Deserialize<AccountsResponse>(result);
				var targetPage = accountsData?.data.FirstOrDefault(p => p.id == _pageIdToPublish);

				if (targetPage != null)
				{
					return targetPage.access_token;
				}
				else
				{
					throw new Exception($"Страница с ID {_pageIdToPublish} не найдена или нет прав.");
				}
			}
		}

		public async Task<bool> PublishToPageAsync(string message, List<string> base64Images = null)
		{
			// 2. ЗАПРОС ТОКЕНА СТРАНИЦЫ
			string pageAccessToken = await GetPageAccessTokenAsync();

			try
			{
				using (var httpClient = new HttpClient())
				{
					if (base64Images?.Any() == true)
					{
						return await PublishAlbumAsync(pageAccessToken, _pageIdToPublish, message, base64Images);
					}
					else
					{
						// 3. ПУБЛИКАЦИЯ С НОВЫМ ТОКЕНОМ СТРАНИЦЫ
						string publishUrl = $"https://graph.facebook.com/v24.0/{_pageIdToPublish}/feed";

						var postData = new Dictionary<string, string>
						{
							{ "message", message },
							// Передаем токен как параметр, используя FormUrlEncodedContent
							{ "access_token", pageAccessToken }
						};

						using (var content = new FormUrlEncodedContent(postData))
						{
							// 4. Отправляем POST-запрос
							var publishResponse = await httpClient.PostAsync(publishUrl, content);
							bool success = await ProcessPublishResponseAsync(publishResponse);

							return success;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}

			return false;
		}

		private async Task<bool> PublishAlbumAsync(string pageAccessToken, string pageId, string message, List<string> base64Images)
		{
			var mediaFbidList = new List<string>();

			using (var httpClient = new HttpClient())
			{
				// 1. ЗАГРУЗКА ВСЕХ ИЗОБРАЖЕНИЙ
				Console.WriteLine($"Начинается загрузка {base64Images.Count} изображений...");

				foreach (var base64Image in base64Images)
				{
					// Используем новый метод для загрузки
					string photoId = await UploadImageAsync(pageAccessToken, pageId, base64Image, httpClient);

					if (!string.IsNullOrEmpty(photoId))
					{
						mediaFbidList.Add(photoId);
					}
					else
					{
						// Если хоть одно изображение не загрузилось, прекращаем операцию.
						Console.WriteLine("Не удалось загрузить одно из изображений. Публикация отменена.");
						return false;
					}
				}

				// 2. ФОРМИРОВАНИЕ ФИНАЛЬНОГО ПОСТА (КАРУСЕЛИ)

				// Конечная точка для публикации альбома - это /feed
				string publishUrl = $"https://graph.facebook.com/v24.0/{pageId}/feed";

				var postData = new Dictionary<string, string>
				{
					{ "message", message },
					{ "access_token", pageAccessToken }
				};

				// Добавление каждого загруженного ID в формате attached_media[i]
				for (int i = 0; i < mediaFbidList.Count; i++)
				{
					// Формат значения: {"media_fbid": "ID"}
					var mediaObject = new { media_fbid = mediaFbidList[i] };
					string jsonMedia = JsonSerializer.Serialize(mediaObject);

					// Ключ: attached_media[0], attached_media[1], и т.д.
					postData.Add($"attached_media[{i}]", jsonMedia);
				}

				// 3. ОТПРАВКА ПОСТА С attached_media
				using (var content = new FormUrlEncodedContent(postData))
				{
					var publishResponse = await httpClient.PostAsync(publishUrl, content);
					return await ProcessPublishResponseAsync(publishResponse);
				}
			}
		}

		// Возвращает ID загруженной фотографии (media_fbid)
		private async Task<string> UploadImageAsync(string pageAccessToken, string pageId, string base64Image, HttpClient httpClient)
		{
			byte[] imageBytes;
			try
			{
				imageBytes = Convert.FromBase64String(base64Image);
			}
			catch (FormatException)
			{
				Console.WriteLine("Ошибка: Неверный формат Base64.");
				return null;
			}

			// Используем конечную точку /photos, чтобы загрузить файл
			// published=false гарантирует, что изображение не публикуется сразу
			string url = $"https://graph.facebook.com/v24.0/{pageId}/photos?published=false";

			using (var content = new MultipartFormDataContent())
			{
				var imageContent = new ByteArrayContent(imageBytes);
				imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

				// "source" - это имя поля для бинарного содержимого
				content.Add(imageContent, "source", "image.jpg");
				content.Add(new StringContent(pageAccessToken), "access_token");

				var response = await httpClient.PostAsync(url, content);

				if (response.IsSuccessStatusCode)
				{
					string result = await response.Content.ReadAsStringAsync();
					try
					{
						var data = JsonSerializer.Deserialize<UploadResponse>(result);
						// Возвращаем ID загруженной фотографии
						return data?.id;
					}
					catch (JsonException)
					{
						Console.WriteLine($"Ошибка парсинга ID при загрузке. Ответ: {result}");
						return null;
					}
				}
				else
				{
					string errorResult = await response.Content.ReadAsStringAsync();
					Console.WriteLine($"Ошибка при загрузке изображения: {errorResult}");
					return null;
				}
			}
		}

		public async Task<bool> ProcessPublishResponseAsync(HttpResponseMessage publishResponse)
		{
			// 1. Проверка статуса HTTP
			// Успешная публикация всегда вернет код 200 OK.
			if (!publishResponse.IsSuccessStatusCode)
			{
				// Если статус не 200 (например, 400 Bad Request, 403 Forbidden), 
				// это ошибка. Читаем тело для деталей (сообщение об ошибке Facebook)
				string errorResult = await publishResponse.Content.ReadAsStringAsync();
				Console.WriteLine($"Ошибка публикации (HTTP {publishResponse.StatusCode}): {errorResult}");
				return false;
			}

			// 2. Парсинг тела ответа
			try
			{
				string publishResult = await publishResponse.Content.ReadAsStringAsync();

				// Проверяем, что тело не пустое
				if (string.IsNullOrWhiteSpace(publishResult))
				{
					Console.WriteLine("Ошибка: Успешный HTTP-статус, но пустое тело ответа.");
					return false;
				}

				// Десериализуем JSON. Если Facebook вернул {"id":"..."} - это успех.
				var data = JsonSerializer.Deserialize<PublishResponse>(publishResult);

				// ПЕРВЫМ ДЕЛОМ ПРОВЕРЯЕМ post_id (для Reels), ЗАТЕМ id (для фото/текста)
				string finalId = data?.post_id ?? data?.id; // Используем post_id или id

				// 3. Проверка наличия ID
				if (!string.IsNullOrEmpty(finalId))
				{
					// УСПЕХ: Пост опубликован, и его ID получен.
					Console.WriteLine($"Публикация успешна. ID поста: {finalId}");
					return true;
				}
				else
				{
					// УСПЕХ, НО БЕЗ ID: Если пришла {"success": true} без post_id/id (например, на шаге 2 загрузки)
					if (publishResult.Contains("\"success\":true"))
					{
						Console.WriteLine($"Успешная операция, но без ID поста в ответе (возможно, это промежуточный шаг загрузки).");
						return true;
					}

					// Тело не содержит ожидаемого ID
					Console.WriteLine($"Ошибка парсинга: Успешный HTTP-статус, но отсутствует ID в ответе. Ответ: {publishResult}");
					return false;
				}
			}
			catch (JsonException ex)
			{
				// Ошибка, если тело ответа не является валидным JSON
				Console.WriteLine($"Ошибка десериализации JSON: {ex.Message}");
				return false;
			}
			catch (Exception ex)
			{
				// Прочие ошибки
				Console.WriteLine($"Неизвестная ошибка: {ex.Message}");
				return false;
			}
		}

		public async Task<bool> PublishReelAsync(string message, string base64Video)
		{
			// Шаг 1: Получение токена страницы (логика из PublishToPageAsync)
			string pageAccessToken;
			try
			{
				pageAccessToken = await GetPageAccessTokenAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при получении токена страницы: {ex.Message}");
				return false;
			}

			if (string.IsNullOrEmpty(pageAccessToken)) return false;

			// Шаг 2: Конвертация Base64 в байты
			byte[] videoBytes;
			try
			{
				videoBytes = Convert.FromBase64String(base64Video);
			}
			catch (FormatException)
			{
				Console.WriteLine("Ошибка: Неверный формат Base64 для видео.");
				return false;
			}

			// Шаг 3: Выполнение 3-х шагов загрузки Reels
			using (var httpClient = new HttpClient())
			{
				// 1. Инициировать сессию
				var (videoId, uploadUrl) = await StartReelUploadSessionAsync(pageAccessToken, _pageIdToPublish, httpClient);

				if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(uploadUrl)) return false;

				// 2. Загрузить видео (в вашем случае, все сразу, так как Base64 уже в памяти)
				bool uploadSuccess = await TransferReelDataAsync(uploadUrl, videoBytes, pageAccessToken, httpClient);

				if (!uploadSuccess) return false;

				// 3. Завершить и опубликовать
				bool publishSuccess = await FinishReelUploadSessionAsync(pageAccessToken, _pageIdToPublish, videoId, message, httpClient);

				return publishSuccess;
			}
		}

		private async Task<(string videoId, string uploadUrl)> StartReelUploadSessionAsync(string pageAccessToken, string pageId, HttpClient httpClient)
		{
			// Используем конечную точку /{page-id}/video_reels
			string url = $"https://graph.facebook.com/v24.0/{pageId}/video_reels";

			var postData = new Dictionary<string, string>
			{
				// Обязательный параметр для начала сессии
				{ "upload_phase", "start" },
				{ "access_token", pageAccessToken }
			};

			using (var content = new FormUrlEncodedContent(postData))
			{
				var response = await httpClient.PostAsync(url, content);

				if (response.IsSuccessStatusCode)
				{
					string result = await response.Content.ReadAsStringAsync();
					try
					{
						// Ожидаемый ответ: {"video_id": "...", "upload_url": "..."}
						var data = JsonSerializer.Deserialize<ReelStartResponse>(result);
						Console.WriteLine($"Сессия Reel инициирована. Video ID: {data.video_id}");
						return (data.video_id, data.upload_url);
					}
					catch (JsonException ex)
					{
						Console.WriteLine($"Ошибка парсинга ответа начала сессии Reels: {ex.Message}. Ответ: {result}");
						return (null, null);
					}
				}
				else
				{
					string errorResult = await response.Content.ReadAsStringAsync();
					Console.WriteLine($"Ошибка при начале сессии Reels: {errorResult}");
					return (null, null);
				}
			}
		}

		private async Task<bool> TransferReelDataAsync(string uploadUrl, byte[] videoBytes, string pageAccessToken, HttpClient httpClient)
		{
			// URL получен на этапе Start: https://rupload.facebook.com/video-upload/v24.0/{video-id}

			var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);

			// 1. Установка токена в заголовок Authorization, как в curl-примере.
			// Если это не сработает, вернемся к передаче токена в URL.
			request.Headers.Add("Authorization", $"OAuth {pageAccessToken}");

			// 2. Содержимое файла (Content)
			var videoContent = new ByteArrayContent(videoBytes);

			// Устанавливаем Content-Type, как требует документация: application/octet-stream
			videoContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

			// 3. ПЕРЕДАЧА НЕСТАНДАРТНЫХ ЗАГОЛОВКОВ В CONTENT.HEADERS
			// Это обходной путь для .NET, позволяющий отправить 'offset' и 'file_size' 
			// без префикса 'X-Entity-', что вызывает ошибку 'Header Offset not convertable'.
			// Мы передаем их как заголовки, связанные с содержимым.

			// Заголовок 'offset'
			videoContent.Headers.Add("offset", "0");

			// Заголовок 'file_size'
			videoContent.Headers.Add("file_size", videoBytes.Length.ToString());

			request.Content = videoContent;

			Console.WriteLine($"Загрузка Reel: URL={request.RequestUri}, Размер={videoBytes.Length} байт, Offset=0");

			var response = await httpClient.SendAsync(request);

			if (response.IsSuccessStatusCode)
			{
				string result = await response.Content.ReadAsStringAsync();
				// Ожидаемый ответ: {"success": true}
				Console.WriteLine($"Данные Reels успешно загружены. Ответ: {result}");
				return true;
			}
			else
			{
				string errorResult = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"Ошибка при загрузке данных Reels: {errorResult}");
				return false;
			}
		}

		private async Task<bool> FinishReelUploadSessionAsync(string pageAccessToken, string pageId, string videoId, string description, HttpClient httpClient)
		{
			// Конечная точка та же, что и на старте
			string url = $"https://graph.facebook.com/v24.0/{pageId}/video_reels";

			var postData = new Dictionary<string, string>
			{
				// Обязательный параметр для завершения
				{ "upload_phase", "finish" },
				{ "video_id", videoId },
				{ "description", description },
				// Обязательные параметры для публикации
				{ "video_state", "PUBLISHED" }, // Указывает, что нужно сразу опубликовать
				{ "access_token", pageAccessToken }
			};

			using (var content = new FormUrlEncodedContent(postData))
			{
				var response = await httpClient.PostAsync(url, content);

				// Используем существующий метод для проверки ответа публикации
				return await ProcessPublishResponseAsync(response);
			}
		}

		protected override string GetBaseDescriptionPrompt(string base64Img)
		{
			return "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в FaceBook под постом с фотографией. " +
				$"А так же придумай не более 15 хештогов, они должны соответствовать " +
				$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
				$"Вот само изображение: {base64Img}" +
				$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
				$"без всякого рода ковычек и экранирования. " +
				$"Пример ответа: ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence #neuralnetwork #digitalart " +
				$"#generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";
		}
	}

	public class ReelStartResponse
	{
		// ID контейнера видео (Reel)
		public string video_id { get; set; }
		// Специальный URL для загрузки файла
		public string upload_url { get; set; }
	}

	public class PageToken
	{
		// Токен, который нужен для публикации
		public string access_token { get; set; }
		// ID Страницы
		public string id { get; set; }
		// Имя Страницы
		public string name { get; set; }
	}

	public class AccountsResponse
	{
		// Список страниц находится в поле "data"
		public List<PageToken> data { get; set; }
	}

	public class PublishResponse
	{
		// Ожидаемый формат ID: "pageId_postId"
		public string id { get; set; }

		// ID поста, возвращаемый API Reels после шага "finish"
		public string post_id { get; set; }
	}

	public class UploadResponse
	{
		// ID загруженной фотографии (media_fbid)
		public string id { get; set; }
	}
}
