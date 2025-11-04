using System.Text.Json;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class FaceBook
	{
		private readonly string _longLivedUserToken;
		private string _userId = "122108650443054121";
		string _pageIdToPublish = "872506142593246";

		public FaceBook(string longLivedUserToken)
		{
			_longLivedUserToken = longLivedUserToken;
		}

		public async Task<bool> PublishToPageAsync(string message, List<string> base64Images = null)
		{
			// 2. ЗАПРОС ТОКЕНА СТРАНИЦЫ
			string accountsUrl = $"https://graph.facebook.com/v24.0/{_userId}/accounts?access_token={_longLivedUserToken}";
			string pageAccessToken = null;

			try
			{
				using (var httpClient = new HttpClient())
				{
					// 2a. Получаем список страниц
					var response = await httpClient.GetAsync(accountsUrl);
					response.EnsureSuccessStatusCode();
					string result = await response.Content.ReadAsStringAsync();

					// 2b. Парсим ответ и ищем нужный токен
					var accountsData = JsonSerializer.Deserialize<AccountsResponse>(result);

					var targetPage = accountsData?.data.FirstOrDefault(p => p.id == _pageIdToPublish);

					if (targetPage != null)
					{
						pageAccessToken = targetPage.access_token;
						Console.WriteLine($"Успешно получен новый токен для страницы {targetPage.name}.");
					}
					else
					{
						throw new Exception($"Страница с ID {_pageIdToPublish} не найдена или нет прав.");
					}

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

		public async Task<bool> PublishImageAsync(string pageAccessToken, string pageId, string base64Image, string caption)
		{
			// 1. Преобразование Base64 в байты
			byte[] imageBytes;
			try
			{
				imageBytes = Convert.FromBase64String(base64Image);
			}
			catch (FormatException)
			{
				Console.WriteLine("Ошибка: Неверный формат Base64.");
				return false;
			}

			// 2. Настройка запроса
			string url = $"https://graph.facebook.com/v24.0/{pageId}/photos"; // КОНЕЧНАЯ ТОЧКА ИЗМЕНЕНА НА /photos

			using (var httpClient = new HttpClient())
			using (var content = new MultipartFormDataContent())
			{
				// 3. Добавление файла (Image)
				var imageContent = new ByteArrayContent(imageBytes);
				// Важно: нужно указать Content-Type изображения (например, image/jpeg или image/png)
				imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

				// Добавляем файл: "source" - это обязательный параметр для API
				content.Add(imageContent, "source", "image.jpg");

				// 4. Добавление других полей (Caption и Token)
				content.Add(new StringContent(caption), "caption");
				content.Add(new StringContent(pageAccessToken), "access_token");

				// 5. Отправка запроса
				var response = await httpClient.PostAsync(url, content);

				// 6. Обработка ответа
				return await ProcessPublishResponseAsync(response); // Используем ваш готовый обработчик
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

				// 3. Проверка наличия ID
				if (!string.IsNullOrEmpty(data?.id))
				{
					// УСПЕХ: Пост опубликован, и его ID получен.
					Console.WriteLine($"Публикация успешна. ID поста: {data.id}");
					return true;
				}
				else
				{
					// Тело не содержит ожидаемого ID, что указывает на неожиданный формат ответа.
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
	}

	public class UploadResponse
	{
		// ID загруженной фотографии (media_fbid)
		public string id { get; set; }
	}
}
