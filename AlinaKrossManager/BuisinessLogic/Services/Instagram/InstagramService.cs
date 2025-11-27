using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Helpers;
using AlinaKrossManager.Services;
using static AlinaKrossManager.Helpers.Logger;

namespace AlinaKrossManager.BuisinessLogic.Services.Instagram
{
	public partial class InstagramService : SocialBaseService
	{
		private readonly HttpClient _https;
		private readonly string _accessToken;
		private readonly ConversationService _conversationService;
		private readonly IWebHostEnvironment _env;
		public string _imgbbApiKey = "807392339c89019fcbe08fcdd068a19c";
		private const string _alinaKrossId = "17841477563266256";
		private const string _alinaKrossName = "alina.kross.ai";
		private const string _evgenyYushkoId = "1307933750574022";
		public override string ServiceName => "Instagram";

		public InstagramService(string accessToken
			, IGenerativeLanguageModel generativeLanguage
			, ConversationService conversationService
			, IWebHostEnvironment env
		)
			: base(generativeLanguage)
		{
			_accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
			_conversationService = conversationService;
			_env = env;
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

		public async Task<InstagramMedia> GetRandomMedia()
		{
			try
			{
				// Используем твой рабочий метод
				var mediaList = await GetUserMediaAsync();

				if (mediaList == null || !mediaList.Any())
				{
					Log("📭 No media found");
					return null;
				}

				// Фильтруем только фото и видео (сторис поддерживают IMAGE и VIDEO)
				var eligibleMedia = mediaList
					.Where(m => m.Media_Type == "IMAGE" || m.Media_Type == "VIDEO")
					.ToList();

				if (!eligibleMedia.Any())
				{
					Log("📷 No eligible media found for stories");
					return null;
				}

				// Выбираем случайную публикацию
				var random = new Random();
				var randomMedia = eligibleMedia[random.Next(eligibleMedia.Count)];

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
			// *** ОПРЕДЕЛЕНИЕ ТИПА МЕДИА ***
			string videoUrl = null;
			string imageUrl = null;

			// Если это не CAROUSEL_ALBUM, определяем URL
			if (media.Media_Type == "VIDEO")
			{
				videoUrl = media.Media_Url;
			}
			else if (media.Media_Type == "IMAGE")
			{
				imageUrl = media.Media_Url;
			}
			else
			{
				// Невозможно создать Story из CAROUSEL_ALBUM напрямую.
				Log($"❌ Cannot create story container from media type: {media.Media_Type}");
				return null;
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
				var randomMedia = await GetRandomMedia();
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
			var url = $"me/media?fields=id,caption,media_type,media_url,permalink,thumbnail_url,timestamp&access_token={_accessToken}";
			var json = await _https.GetStringAsync(url);

			using (var doc = JsonDocument.Parse(json))
			{
				var root = doc.RootElement.GetProperty("data");

				var result = new List<InstagramMedia>();
				foreach (var item in root.EnumerateArray())
				{
					var timestampString = item.GetProperty("timestamp").GetString();
					DateTime timestamp;

					try
					{
						// Пробуем разные форматы даты
						if (DateTime.TryParse(timestampString, out timestamp))
						{
							// Успешно распарсили
						}
						else if (timestampString.Contains("+0000"))
						{
							// Убираем временную зону для парсинга
							timestampString = timestampString.Replace("+0000", "").Trim();
							timestamp = DateTime.Parse(timestampString);
						}
						else
						{
							// Если все равно не парсится, используем текущее время
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
				return result;
			}
		}

		protected override string GetBaseDescriptionPrompt(string base64Img)
		{
			return "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в instagram под постом с фотографией. " +
				$"А так же придумай не более 15 хештогов, они должны соответствовать " +
				$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
				$"Вот само изображение: {base64Img}" +
				$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
				$"без всякого рода ковычек и экранирования. " +
				$"Пример ответа: ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence " +
				$"#neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";
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
