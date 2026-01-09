using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;
using static AlinaKrossManager.Helpers.Logger;

namespace AlinaKrossManager.BuisinessLogic.Services.Instagram
{
	public partial class InstagramService : SocialBaseService
	{
		private readonly HttpClient _https;
		private readonly string _accessToken;
		private readonly string _faceBooklongLiveToken;
		private readonly ConversationService _conversationService;
		private readonly IWebHostEnvironment _env;
		private readonly ElevenLabService _elevenLabService;
		public string _imgbbApiKey = "807392339c89019fcbe08fcdd068a19c";

		public override string ServiceName => "Instagram";

		public InstagramService(string accessToken
			, string faceBooklongLiveToken
			, IGenerativeLanguageModel generativeLanguage
			, ConversationService conversationService
			, IWebHostEnvironment env
			, ElevenLabService elevenLabService
		)
			: base(generativeLanguage)
		{
			_accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
			_faceBooklongLiveToken = faceBooklongLiveToken;
			_conversationService = conversationService;
			_env = env;
			_elevenLabService = elevenLabService;
			_https = new HttpClient { BaseAddress = new Uri("https://graph.instagram.com/") };
			_https.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
		}

		public async Task<CreateMediaResult> CreateMediaAsync(List<string> base64Strings, string caption = null)
		{
			if (base64Strings == null || base64Strings.Count == 0)
				throw new ArgumentException("Список изображений не может быть пустым");

			Console.WriteLine("CreateMediaAsync - Start");

			ContainerResult containerResult;

			if (base64Strings.Count == 1)
			{
				// Одиночное изображение
				containerResult = await CreateSingleMediaContainerAsync(base64Strings[0], caption);
			}
			else if (base64Strings.Count <= 10) // Instagram позволяет до 10 фото в карусели
			{
				// Карусель из нескольких изображений
				containerResult = await CreateCarouselContainerAsync(base64Strings, caption);
			}
			else
			{
				throw new ArgumentException("Instagram позволяет не более 10 изображений в одном посте");
			}

			if (string.IsNullOrEmpty(containerResult.Id))
				throw new Exception("Не удалось создать контейнер");

			Console.WriteLine($"Контейнер создан: {containerResult}");

			// ЖДЕМ пока медиа станет готовым к публикации
			var isReady = await WaitForMediaReadyAsync(containerResult.Id);
			if (!isReady)
			{
				throw new Exception($"Медиа {containerResult} не готово к публикации после ожидания");
			}

			Console.WriteLine($"Медиа {containerResult} готово к публикации");

			// Публикуем
			var container = await PublishContainerAsync(containerResult.Id);
			container.ExternalContentUrl = containerResult.ExternalContentUrl;
			return container;
		}

		private async Task<ContainerResult> CreateSingleMediaContainerAsync(string base64String, string caption = null)
		{
			try
			{
				Console.WriteLine("CreateSingleMediaContainerAsync - Start");

				var imageUrl = await UploadToImgBBAsync(base64String);

				Console.WriteLine("CreateSingleMediaContainerAsync - 2");

				// Создаем контейнер для медиа
				var containerUrl = $"me/media?image_url={Uri.EscapeDataString(imageUrl)}" +
								  $"&caption={Uri.EscapeDataString(caption ?? "")}" +
								  $"&access_token={_accessToken}";

				var response = await _https.PostAsync(containerUrl, null);
				var json = await response.Content.ReadAsStringAsync();

				Console.WriteLine("CreateSingleMediaContainerAsync - 3");

				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Ошибка создания контейнера: {json}");
				}

				using var doc = JsonDocument.Parse(json);
				return new ContainerResult
				{
					Id = doc.RootElement.GetProperty("id").GetString(),
					ExternalContentUrl = imageUrl
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка в CreateSingleMediaContainerAsync: {ex.Message}");
				return null;
			}
		}

		private async Task<ContainerResult> CreateCarouselContainerAsync(List<string> base64Strings, string caption = null)
		{
			try
			{
				var childrenIds = new List<string>();

				// Сначала создаем все дочерние контейнеры
				foreach (var base64String in base64Strings)
				{
					var imageUrl = await UploadToImgBBAsync(base64String);

					// Создаем контейнер для этого изображения
					var childUrl = $"me/media?image_url={Uri.EscapeDataString(imageUrl)}&access_token={_accessToken}";
					var childResponse = await _https.PostAsync(childUrl, null);
					var childJson = await childResponse.Content.ReadAsStringAsync();

					if (childResponse.IsSuccessStatusCode)
					{
						using var childDoc = JsonDocument.Parse(childJson);
						var childId = childDoc.RootElement.GetProperty("id").GetString();
						childrenIds.Add(childId);

						// Ждем немного между запросами
						await Task.Delay(500);
					}
					else
					{
						Console.WriteLine($"Ошибка создания child: {childJson}");
					}
				}

				if (childrenIds.Count == 0)
					throw new Exception("Не удалось создать ни одного дочернего контейнера");

				// ВАЖНО: Используем form-data вместо JSON
				var carouselUrl = $"me/media?access_token={_accessToken}";

				var formData = new MultipartFormDataContent();
				formData.Add(new StringContent("CAROUSEL"), "media_type");
				formData.Add(new StringContent(caption ?? ""), "caption");

				// Добавляем children как отдельные поля
				for (int i = 0; i < childrenIds.Count; i++)
				{
					formData.Add(new StringContent(childrenIds[i]), $"children[{i}]");
				}

				var response = await _https.PostAsync(carouselUrl, formData);
				var json = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Ошибка создания карусели: {json}");
				}

				using var doc = JsonDocument.Parse(json);
				return new ContainerResult
				{
					Id = doc.RootElement.GetProperty("id").GetString()
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка в CreateCarouselContainerAsync: {ex.Message}");
				return null;
			}
		}

		private async Task<bool> WaitForMediaReadyAsync(string containerId, int maxWaitSeconds = 60)
		{
			Console.WriteLine($"Ожидаем готовности медиа {containerId}...");

			var startTime = DateTime.Now;

			while (DateTime.Now - startTime < TimeSpan.FromSeconds(maxWaitSeconds))
			{
				try
				{
					var statusUrl = $"{containerId}?fields=status_code,status&access_token={_accessToken}";
					var response = await _https.GetAsync(statusUrl);
					var json = await response.Content.ReadAsStringAsync();

					Console.WriteLine($"Статус ответ: {json}");

					if (response.IsSuccessStatusCode)
					{
						using var doc = JsonDocument.Parse(json);

						var statusCode = doc.RootElement.TryGetProperty("status_code", out var sc) ? sc.GetString() : null;
						var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;

						Console.WriteLine($"Статус: {status}, Status Code: {statusCode}");

						if (statusCode == "FINISHED" || status == "FINISHED")
						{
							// ДОПОЛНИТЕЛЬНАЯ ЗАДЕРЖКА после FINISHED
							Console.WriteLine($"✅ Получен статус FINISHED, ждем 15 секунд перед публикацией...");
							await Task.Delay(15000);
							Console.WriteLine($"✅ Медиа {containerId} готово к публикации!");
							return true;
						}
						else if (statusCode == "ERROR" || status == "ERROR")
						{
							Console.WriteLine($"❌ Медиа {containerId} завершилось с ошибкой");
							return false;
						}

						Console.WriteLine($"⏳ Медиа {containerId} еще обрабатывается...");
					}
					else
					{
						Console.WriteLine($"Ошибка запроса статуса: {json}");
					}

					await Task.Delay(3000);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ошибка при проверке статуса: {ex.Message}");
					await Task.Delay(3000);
				}
			}

			Console.WriteLine($"⏰ Таймаут ожидания медиа {containerId}");
			return false;
		}

		/// <summary>
		/// Загрузить base64 на ImgBB
		/// </summary>
		private async Task<string> UploadToImgBBAsync(string base64String)
		{
			if (string.IsNullOrEmpty(_imgbbApiKey))
				throw new InvalidOperationException("ImgBB API ключ не установлен");

			var cleanBase64 = base64String.Contains(",")
				? base64String.Split(',')[1]
				: base64String;

			using (var httpClient = new HttpClient())
			{
				var content = new MultipartFormDataContent
				{
					{ new StringContent(_imgbbApiKey), "key" },
					{ new StringContent(cleanBase64), "image" }
				};

				var response = await httpClient.PostAsync("https://api.imgbb.com/1/upload", content);
				var json = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
					throw new HttpRequestException($"Ошибка загрузки на ImgBB: {json}");

				using (var doc = JsonDocument.Parse(json))
				{
					return doc.RootElement.GetProperty("data")
						.GetProperty("url").GetString();
				}
			}
		}

		/// <summary>
		/// Опубликовать контейнер с медиа
		/// </summary>
		private async Task<CreateMediaResult> PublishContainerAsync(string containerId)
		{
			try
			{
				Console.WriteLine($"Публикуем контейнер: {containerId}");

				var publishUrl = $"me/media_publish?creation_id={containerId}&access_token={_accessToken}";
				var response = await _https.PostAsync(publishUrl, null);
				var json = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"Ответ публикации: {json}");

				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Ошибка публикации: {json}");
				}

				using var doc = JsonDocument.Parse(json);
				var mediaId = doc.RootElement.GetProperty("id").GetString();

				Console.WriteLine($"✅ Пост успешно опубликован! ID: {mediaId}");

				return new CreateMediaResult
				{
					Id = mediaId,
					Success = true
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Ошибка в PublishContainerAsync: {ex.Message}");
				throw;
			}
		}

		public async Task<InstagramMedia> GetRandomMediaForStory()
		{
			try
			{
				_mediaList = _mediaList ?? await GetUserMediaAsync();

				if (_mediaList == null || !_mediaList.Any())
				{
					Log("📭 No media found");
					return null;
				}			

				if (!_mediaList.Any())
				{
					Log("📷 No eligible media found for stories");
					return null;
				}

				// Выбираем случайную публикацию
				var randomMedia = GetRandomUniqeMedia(_mediaList);

				Log($"🎲 Selected random media: {randomMedia.Id} ({randomMedia.Media_Type})");

				return randomMedia;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error getting random media");
				return null;
			}
		}

		public async Task<string> PublishStoryFromMedia(InstagramMedia media)
		{
			try
			{
				if (media == null)
				{
					Log("❌ No media provided for story");
					return null;
				}

				Log($"📱 Publishing regular story: {media.Id}");

				// Создаем контейнер
				var containerId = await CreateStoryContainer(media);
				if (string.IsNullOrEmpty(containerId))
				{
					return null;
				}

				// Ждем и публикуем БЕЗ ССЫЛКИ
				var storyId = await WaitAndPublishContainer(containerId);

				if (!string.IsNullOrEmpty(storyId))
				{
					Log($"✅ Regular story published successfully: {storyId}");
					return storyId;
				}

				return null;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error publishing regular story");
				return null;
			}
		}

		public async Task<string> PublishStoryFromBase64(string base64Img)
		{
			try
			{
				if (base64Img == null)
				{
					Log("❌ No media provided for story");
					return null;
				}

				var imageUrl = await UploadToImgBBAsync(base64Img);
				if (imageUrl is null)
				{
					Log($"Не получили ссылку на изображение");
					return null;
				}

				var media = new InstagramMedia
				{
					Media_Type = "IMAGE",
					Media_Url = imageUrl,
				};

				var containerId = await CreateStoryContainer(media);
				if (string.IsNullOrEmpty(containerId))
				{
					return null;
				}

				// Ждем и публикуем БЕЗ ССЫЛКИ
				var storyId = await WaitAndPublishContainer(containerId);

				if (!string.IsNullOrEmpty(storyId))
				{
					Log($"✅ Regular story published successfully: {storyId}");
					return storyId;
				}

				return null;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error publishing regular story");
				return null;
			}
		}

		private async Task<string> CreateStoryContainer(InstagramMedia media)
		{
			string videoUrl = null;
			string imageUrl = null;

			if (media.Media_Type == "VIDEO")
			{
				videoUrl = media.Media_Url;
			}
			else
			{
				imageUrl = media.Media_Url;
			}

			var containerPayload = new
			{
				media_type = "STORIES",
				video_url = videoUrl, // Будет null, если это IMAGE
				image_url = imageUrl, // Будет null, если это VIDEO
				access_token = _accessToken
			};

			var options = new JsonSerializerOptions
			{
				// КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Не включать свойства со значением null
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
				PropertyNameCaseInsensitive = true
				// Примечание: Если вы используете Newtonsoft.Json, это JsonProperty.NullValueHandling = NullValueHandling.Ignore
			};

			var containerUrl = "https://graph.instagram.com/v19.0/me/media";

			var containerJson = JsonSerializer.Serialize(containerPayload, options);
			var containerContent = new StringContent(containerJson, Encoding.UTF8, "application/json");

			using var httpClient = new HttpClient();

			var containerResponse = await httpClient.PostAsync(containerUrl, containerContent);
			var containerResponseContent = await containerResponse.Content.ReadAsStringAsync();

			if (!containerResponse.IsSuccessStatusCode)
			{
				Log($"❌ Failed to create story container: {containerResponseContent}");
				return null;
			}

			var containerData = JsonSerializer.Deserialize<Dictionary<string, string>>(containerResponseContent);
			return containerData?["id"];
		}

		private async Task<string> WaitAndPublishContainer(string containerId)
		{
			var maxAttempts = 30;
			var attempt = 0;

			while (attempt < maxAttempts)
			{
				await Task.Delay(3000);

				var statusUrl = $"https://graph.instagram.com/v19.0/{containerId}?fields=status,error_message&access_token={_accessToken}";
				using var httpClient = new HttpClient();
				var statusResponse = await httpClient.GetAsync(statusUrl);
				var statusContent = await statusResponse.Content.ReadAsStringAsync();

				if (statusResponse.IsSuccessStatusCode)
				{
					var statusData = JsonSerializer.Deserialize<Dictionary<string, string>>(statusContent);
					var status = statusData?["status"] ?? "";

					Log($"🔄 Container status: {status}");

					if (status == "FINISHED")
					{
						// Публикуем сторис
						var publishUrl = $"https://graph.instagram.com/v19.0/me/media_publish?creation_id={containerId}&access_token={_accessToken}";

						Log($"📤 Publishing story to: {publishUrl}");

						var publishResponse = await httpClient.PostAsync(publishUrl, null);
						var publishResponseContent = await publishResponse.Content.ReadAsStringAsync();

						if (publishResponse.IsSuccessStatusCode)
						{
							var publishData = JsonSerializer.Deserialize<StoryPublishResponse>(publishResponseContent);
							Log($"✅ Story published successfully with ID: {publishData?.Id}");
							return publishData?.Id;
						}
						else
						{
							Log($"❌ Failed to publish story: {publishResponseContent}");
							return null;
						}
					}
					else if (status == "ERROR" || status == "EXPIRED")
					{
						var errMsg = statusData?["error_message"] ?? "";
						Log($"❌ Container failed with status: {status}, erroreMsg: {errMsg}");
						return null;
					}
				}

				attempt++;
				Log($"⏳ Attempt {attempt}/{maxAttempts} - Container not ready yet");
			}

			Log($"❌ Container not ready after {maxAttempts} attempts");
			return null;
		}

		public async Task<bool> PublishRandomStory()
		{
			try
			{
				var randomMedia = await GetRandomMediaForStory();
				if (randomMedia == null)
				{
					Log("📭 No media available for story");
					return false;
				}

				string storyId;

				storyId = await PublishStoryFromMedia(randomMedia);
				Log($"📸 Publishing regular story");

				if (!string.IsNullOrEmpty(storyId))
				{
					Log($"🌟 Successfully published story {storyId} from media {randomMedia.Id}");
					return true;
				}

				return false;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error in publish random story");
				return false;
			}
		}

		/// <summary>
		/// Получить список медиа (посты, фото, видео)
		/// </summary>
		public async Task<List<InstagramMedia>> GetUserMediaAsync()
		{
			var result = new List<InstagramMedia>();

			// Добавим &limit=100, чтобы забирать по 100 постов за раз (максимум), 
			// это уменьшит количество запросов к API.
			string currentUrl = $"me/media?fields=id,caption,media_type,media_url,permalink,thumbnail_url,timestamp&access_token={_accessToken}&limit=100";

			while (!string.IsNullOrEmpty(currentUrl))
			{
				try
				{
					var json = await _https.GetStringAsync(currentUrl);

					using (var doc = JsonDocument.Parse(json))
					{
						if (doc.RootElement.TryGetProperty("data", out var dataElement))
						{
							foreach (var item in dataElement.EnumerateArray())
							{
								var timestampString = item.GetProperty("timestamp").GetString();
								DateTime timestamp;

								try
								{
									if (DateTime.TryParse(timestampString, out timestamp))
									{
										// Успешно распарсили
									}
									else if (timestampString.Contains("+0000"))
									{
										timestampString = timestampString.Replace("+0000", "").Trim();
										timestamp = DateTime.Parse(timestampString);
									}
									else
									{
										timestamp = DateTime.UtcNow;
									}
								}
								catch
								{
									timestamp = DateTime.UtcNow;
								}

								result.Add(new InstagramMedia
								{
									Id = item.GetProperty("id").GetString(),
									Caption = item.TryGetProperty("caption", out var caption) ? caption.GetString() : null,
									Media_Type = item.GetProperty("media_type").GetString(),
									Media_Url = item.GetProperty("media_url").GetString(),
									Permalink = item.GetProperty("permalink").GetString(),
									//Thumbnail_Url = item.TryGetProperty("thumbnail_url", out var thumb) ? thumb.GetString() : null,
									Timestamp = timestamp
								});
							}
						}

						// 2. Обработка пагинации (paging.next)
						if (doc.RootElement.TryGetProperty("paging", out var pagingElement) &&
							pagingElement.TryGetProperty("next", out var nextElement))
						{
							// Instagram возвращает полный абсолютный URL для следующей страницы
							currentUrl = nextElement.GetString();
						}
						else
						{
							// Если поля next нет, значит это последняя страница
							currentUrl = null;
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ошибка при получении медиа: {ex.Message}");
					break;
				}
			}

			return result;
		}

		public static string GetBaseDescriptionPrompt(string base64Img)
		{
			return $@"Создай одно текстовое, красивое, интригующее и флиртующее сопровождение для поста в Instagram на основе предоставленного изображения, где изображена красивая девушка модель.

				Следуй этим шагам:
				1.  Описание: Придумай одно красивое и краткое описание на английском языке для поста. Можно добавить 1-3 релевантных эмодзи. Ориентируйся на содержание изображения.
				2.  Хештеги: Придумай список из не более 15 хештегов.
					* Хештеги должны отражать содержание изображения.
					* В их число *бязательно включи 2-3 хештега, указывающих на AI-контент (например, #aiart, #aigenerated, #digitalart, #aiartist).

				КРИТИЧЕСКИ ВАЖНО — ФОРМАТ ОТВЕТА:
				- Твой ответ должен содержать ТОЛЬКО готовое описание и хештеги.
				- АБСОЛЮТНО НИКАКИХ вступительных фраз, пояснений, уточнений, комментариев, приветствий (типа ""Okay, here's..."", ""Sure!"", ""Here is the post:"").
				- Никаких кавычек вокруг текста.
				- Структура ответа:
				  1. Сначала — описание.
				  2. Через одну пустую строку — хештеги.

				Пример того, как должен выглядеть весь твой ответ:
				✨ A moment of pure serenity in the digital dreamscape.

				#aiart #digitaldream #futureaesthetic #aigenerated #cyberzen

				Вот изображение для анализа: {base64Img}";
		}

		private static readonly ConcurrentDictionary<string, string> _hashtagIdCache = new();
		private readonly HttpClient _httpClientFaceBook = new HttpClient();
		public async Task<List<InstaMedia>> GetTopViralPostsAsync(string hashtagId, string userId = _alinaKrossId)
		{
			// 1. Формируем URL
			// Обратите внимание: я увеличил limit до 25, чтобы выборка для сортировки была лучше. 
			// Если оставить 10, мы найдем "лучшее из 10", а не "лучшее из 25".
			string url = $"https://graph.facebook.com/v18.0/{hashtagId}/top_media" +
						 $"?user_id={userId}" +
						 $"&fields=id,caption,media_type,media_url,permalink,like_count,comments_count,timestamp,children{{id,media_type,media_url}}" +
						 $"&limit=25" +
						 $"&access_token={_faceBooklongLiveToken}";

			try
			{
				// 2. Делаем запрос
				var response = await _httpClientFaceBook.GetAsync(url);

				if (!response.IsSuccessStatusCode)
				{
					var errorBody = await response.Content.ReadAsStringAsync();
					Console.WriteLine($"Ошибка API Instagram: {response.StatusCode} - {errorBody}");
					return new List<InstaMedia>();
				}

				// 3. Читаем JSON
				var jsonString = await response.Content.ReadAsStringAsync();
				var instaData = JsonSerializer.Deserialize<InstaResponse>(jsonString);

				if (instaData?.Data == null || !instaData.Data.Any())
				{
					Console.WriteLine("Посты не найдены.");
					return new List<InstaMedia>();
				}

				var bestPosts = instaData.Data
					// А. Убираем посты без медиа (на всякий случай)
					.Where(p => !string.IsNullOrEmpty(p.MediaUrl) || (p.Children?.Data != null && p.Children.Data.Any()))
					// Б. Сортируем по убыванию лайков (самые популярные сверху)
					.OrderByDescending(p => p.LikeCount)
					.Take(5)
					.ToList();

				return bestPosts;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Критическая ошибка: {ex.Message}");
				return new List<InstaMedia>();
			}
		}
		public async Task<string?> GetHashtagIdAsync(string hashtagName, string userId = _alinaKrossId)
		{
			// ВАЖНО: У Instagram есть лимит — поиск только 30 уникальных хештегов за 7 дней.
			// Используйте этот метод экономно! Сохраняйте полученные ID в базу данных.

			// Нормализуем ввод: убираем решетку, пробелы и приводим к нижнему регистру
			var cleanTag = hashtagName.Replace("#", "").Trim().ToLowerInvariant();

			// А. ПРОВЕРКА В КЭШЕ
			if (_hashtagIdCache.TryGetValue(cleanTag, out string cachedId))
			{
				Console.WriteLine($"✅ ID для #{cleanTag} взят из кэша: {cachedId}");
				return cachedId;
			}

			// Б. ЕСЛИ НЕТ В КЭШЕ — ИДЕМ В API
			Console.WriteLine($"🔍 Ищу ID для #{cleanTag} через API (тратится лимит)...");

			string url = $"https://graph.facebook.com/v18.0/ig_hashtag_search" +
						 $"?user_id={userId}" +
						 $"&q={cleanTag}" +
						 $"&access_token={_faceBooklongLiveToken}";

			try
			{
				var response = await _httpClientFaceBook.GetAsync(url);
				if (!response.IsSuccessStatusCode) return null;

				var json = await response.Content.ReadAsStringAsync();
				var searchResult = JsonSerializer.Deserialize<HashtagSearchResponse>(json);
				var foundId = searchResult?.Data?.FirstOrDefault()?.Id;

				if (!string.IsNullOrEmpty(foundId))
				{
					// В. СОХРАНЯЕМ В КЭШ
					_hashtagIdCache.TryAdd(cleanTag, foundId);
					Console.WriteLine($"💾 ID сохранен в память: {foundId}");
					return foundId;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка поиска хештега: {ex.Message}");
			}

			return null;
		}

		#region OldMethods
		/// <summary>
		/// FreeImage.Host (бесплатный, без API ключа)
		/// </summary>
		private async Task<string> UploadToFreeImageHostAsync(string base64String)
		{
			Console.WriteLine("UploadToFreeImageHostAsync - Start");

			try
			{
				var cleanBase64 = base64String.Contains(",")
					? base64String.Split(',')[1]
					: base64String;

				using var httpClient = new HttpClient();
				var content = new MultipartFormDataContent
				{
					{ new StringContent(cleanBase64), "source" }
				};

				await Task.Delay(2000);
				var response = await httpClient.PostAsync("https://freeimage.host/api/1/upload?key=6d207e02198a847aa98d0a2a901485a5", content);
				await Task.Delay(2000);
				var json = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"API Response: {json}");

				using var doc = JsonDocument.Parse(json);

				// Проверяем наличие свойств в ответе
				if (doc.RootElement.TryGetProperty("image", out var imageElement) &&
					imageElement.TryGetProperty("url", out var urlElement))
				{
					var url = urlElement.GetString();
					if (!string.IsNullOrEmpty(url))
					{
						Console.WriteLine("UploadToFreeImageHostAsync - End Success");
						return url;
					}
				}

				// ИСПРАВЛЕННЫЙ ПАРСИНГ ОШИБКИ
				if (doc.RootElement.TryGetProperty("error", out var errorElement))
				{
					string errorMessage;

					if (errorElement.ValueKind == JsonValueKind.Object)
					{
						// error - объект, извлекаем message
						if (errorElement.TryGetProperty("message", out var messageElement))
						{
							errorMessage = messageElement.GetString();
						}
						else
						{
							errorMessage = "Unknown error format";
						}
					}
					else
					{
						// error - строка
						errorMessage = errorElement.GetString();
					}

					throw new Exception($"FreeImage.host error: {errorMessage}");
				}

				throw new Exception("Unexpected response format from FreeImage.host");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"UploadToFreeImageHostAsync - Error: {ex.Message}");
				throw;
			}
		}

		#endregion
		#region Models

		// Корневой ответ от поиска хештега
		public class HashtagSearchResponse
	{
		[JsonPropertyName("data")]
		public List<HashtagData> Data { get; set; }
	}

	// Объект с ID хештега
	public class HashtagData
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }
	}

	public class InstaResponse
	{
		[JsonPropertyName("data")]
		public List<InstaMedia> Data { get; set; }
	}

	// Данные одного поста
	public class InstaMedia
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("caption")]
		public string Caption { get; set; }

		[JsonPropertyName("media_type")]
		public string MediaType { get; set; } // IMAGE, VIDEO, CAROUSEL_ALBUM

		[JsonPropertyName("media_url")]
		public string MediaUrl { get; set; } // Ссылка на фото/видео

		[JsonPropertyName("permalink")]
		public string Permalink { get; set; } // Ссылка на пост в Instagram

		[JsonPropertyName("like_count")]
		public int LikeCount { get; set; }

		[JsonPropertyName("comments_count")]
		public int CommentsCount { get; set; }

		[JsonPropertyName("timestamp")]
		public string Timestamp { get; set; }

		// Для каруселей (альбомов)
		[JsonPropertyName("children")]
		public InstaChildren Children { get; set; }
	}

	// Обертка для вложений карусели
	public class InstaChildren
	{
		[JsonPropertyName("data")]
		public List<InstaChildMedia> Data { get; set; }
	}

	// Данные вложения (слайда)
	public class InstaChildMedia
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("media_type")]
		public string MediaType { get; set; }

		[JsonPropertyName("media_url")]
		public string MediaUrl { get; set; }
	}

	public class ContainerResult
	{
		public string Id { get; set; }
		public string ExternalContentUrl { get; set; }
	}

	public class CreateMediaResult
	{
		public string Id { get; set; }
		public bool Success { get; set; }
		public string ErrorMessage { get; set; }
		public string ExternalContentUrl { get; set; }
	}

	public class InstagramMedia
	{
		public string Id { get; set; }
		public string Caption { get; set; }
		public string Media_Type { get; set; }
		public string Media_Url { get; set; }
		public string Permalink { get; set; }
		public string Thumbnail_Url { get; set; }
		public DateTime Timestamp { get; set; }
	}

	public class MediaResponse
	{
		[JsonPropertyName("data")]
		public List<InstagramMedia> Data { get; set; }

		[JsonPropertyName("paging")]
		public Paging Paging { get; set; }
	}

	public class Paging
	{
		[JsonPropertyName("cursors")]
		public Cursors Cursors { get; set; }
	}

	public class Cursors
	{
		[JsonPropertyName("before")]
		public string Before { get; set; }

		[JsonPropertyName("after")]
		public string After { get; set; }
	}

	public class StoryPublishResponse
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }
	}

	////
	public class InstagramWebhookPayload
	{
		[JsonPropertyName("object")]
		public string Object { get; set; }

		[JsonPropertyName("entry")]
		public List<InstagramEntry> Entry { get; set; }
	}

	public class InstagramEntry
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("time")]
		public long Time { get; set; }

		[JsonPropertyName("messaging")]
		public List<InstagramMessaging> Messaging { get; set; }

		[JsonPropertyName("changes")]
		public List<InstagramChange> Changes { get; set; }
	}

	public class InstagramMessaging
	{
		[JsonPropertyName("sender")]
		public InstagramUser Sender { get; set; }

		[JsonPropertyName("recipient")]
		public InstagramUser Recipient { get; set; }

		[JsonPropertyName("timestamp")]
		public long Timestamp { get; set; }

		[JsonPropertyName("message")]
		public InstagramMessage Message { get; set; }

		[JsonPropertyName("read")]
		public InstagramRead Read { get; set; }
	}

	public class InstagramRead
	{
		[JsonPropertyName("mid")]
		public string MessageId { get; set; }
	}

	public class InstagramMessage
	{
		[JsonPropertyName("mid")]
		public string MessageId { get; set; }

		[JsonPropertyName("text")]
		public string Text { get; set; }

		[JsonPropertyName("is_echo")]
		public bool IsEcho { get; set; }

		[JsonPropertyName("attachments")]
		public List<InstagramAttachment> Attachments { get; set; }
	}

	public class InstagramAttachment
	{
		[JsonPropertyName("type")]
		public string Type { get; set; } // "image", "video", etc.

		[JsonPropertyName("payload")]
		public InstagramAttachmentPayload Payload { get; set; }
	}

	public class InstagramAttachmentPayload
	{
		[JsonPropertyName("url")]
		public string Url { get; set; }
	}

	public class InstagramUser
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("username")]
		public string Username { get; set; }

		[JsonPropertyName("self_ig_scoped_id")]
		public string SelfIgScopedId { get; set; } // Добавь это поле
	}

	public class InstagramChange
	{
		[JsonPropertyName("field")]
		public string Field { get; set; }

		[JsonPropertyName("value")]
		public JsonElement Value { get; set; } // Изменено на JsonElement для гибкости
	}

	// Модель для комментариев
	public class CommentValue
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("text")]
		public string Text { get; set; }

		[JsonPropertyName("from")]
		public InstagramUser From { get; set; }

		[JsonPropertyName("media")]
		public InstagramMedia Media { get; set; }

		[JsonPropertyName("parent_id")]
		public string ParentId { get; set; }
	}
	#endregion
}
}
