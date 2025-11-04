using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class ThreadsGraphApiClient
	{
		private readonly HttpClient _httpClient;
		private readonly string _accessToken;
		private readonly string _userId;

		public ThreadsGraphApiClient(string accessToken, string userId)
		{
			_httpClient = new HttpClient();
			_accessToken = accessToken;
			_userId = userId;

			// Удаляем лишние заголовки, которые могут мешать
			// The graph API typically handles the User-Agent without issue.
			_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			// Установка Bearer токена в заголовок по умолчанию
		}

		public async Task<ThreadsPostResult> CreateThreadAsync(string text)
		{

			// === Исходные данные ===
			string pageId = "872506142593246"; // например, "123456789012345"
			string accessToken = "EAAY5A6MrJHgBPw7WaTySXHmZC4yZAyoS5d3S2GEAvYadZBQ55LNHCkZAAZCNZB5ZCQvUIiPZBGN96yZBB0ZA3ZC5g8KUjNASjVLieZCRf6KPVB7HPNiDVcoZAAIVYLapFu4YyAxZBSFltsl3O7ZCEPQdJiZAU26jX78xDthfecSxkUgIUpYBU0wzPgOUwsjp74CiA1ZC8igxPWrHRGSwXRF0rqVZBwjJaoezdfTGZAvYgNTZAxQp1SZBrzbwZD";
			string message = "Тестовый пост из C# через Graph API 🚀";

			// === Настройка запроса ===
			string url = $"https://graph.facebook.com/v24.0/{pageId}/feed";

			// 1. Создаем коллекцию данных формы
			var postData = new Dictionary<string, string>
			{
				{ "message", message },
				{ "access_token", accessToken }
			};

			// 2. Используем FormUrlEncodedContent для правильного форматирования (ЭТО КЛЮЧЕВОЙ ШАГ!)
			using (var content = new FormUrlEncodedContent(postData))
			using (var httpClient = new HttpClient())
			{
				try
				{
					// 3. Отправляем POST-запрос
					HttpResponseMessage response = await httpClient.PostAsync(url, content);
					string result = await response.Content.ReadAsStringAsync();

					Console.WriteLine("=== Ответ от Facebook ===");
					// Ожидаемый успешный ответ: {"id":"[post_id]"}
					Console.WriteLine(result);
				}
				catch (Exception ex)
				{
					Console.WriteLine("Ошибка при выполнении запроса:");
					Console.WriteLine(ex.Message);
				}
			}

			// Убираем установку токена из метода, переносим ее в конструктор для HttpClient,
			// но оставляем ее здесь в качестве запасного варианта или если ваш конструктор не меняется.
			// request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
			//return Task.CompletedTask;

			try
			{
				var checkUrl = $"https://graph.threads.net/v1.0/{_userId}?fields=id,username&access_token={_accessToken}";
				var checkResponse = await _httpClient.GetAsync(checkUrl);

				string mediaContainerId;

				// ----------------------------------------------------------------------
				// ИСПРАВЛЕНИЕ 1: ШАГ 1: Создание медиа-контейнера (POST /threads)
				// ----------------------------------------------------------------------
				Console.WriteLine("Step 1: Creating Threads media container...");

				// ИСПРАВЛЕНИЕ 1.1: Используем FormUrlEncodedContent, а не JSON
				var containerPayload = new FormUrlEncodedContent(new[]
				{
					// ИСПРАВЛЕНИЕ 1.2: media_type для текста должен быть "TEXT"
					new KeyValuePair<string, string>("media_type", "TEXT"),
					new KeyValuePair<string, string>("text", text),
					new KeyValuePair<string, string>("access_token", _accessToken)
				});

				var containerUrl = $"https://graph.threads.net/v1.0/me/threads";
				var containerResponse = await _httpClient.PostAsync(containerUrl, containerPayload);
				var containerResponseContent = await containerResponse.Content.ReadAsStringAsync();

				if (!containerResponse.IsSuccessStatusCode)
				{
					// ИСПРАВЛЕНИЕ 1.3: Обработка ошибок
					throw HandleThreadsError(containerResponse.StatusCode, containerResponseContent, "Container Creation");
				}

				// ИСПРАВЛЕНИЕ 1.4: Десериализация ответа контейнера
				var containerResult = JsonSerializer.Deserialize<ThreadsMediaContainerResponse>(containerResponseContent);
				mediaContainerId = containerResult?.Id;

				if (string.IsNullOrEmpty(mediaContainerId))
				{
					throw new Exception("Step 1 failed to return a Threads media container ID (id is null or empty).");
				}

				Console.WriteLine($"Step 1 Success. Media Container ID: {mediaContainerId}");


				// ----------------------------------------------------------------------
				// ИСПРАВЛЕНИЕ 2: Ожидание перед публикацией
				// Документация рекомендует подождать в среднем 30 секунд.
				// ----------------------------------------------------------------------
				Console.WriteLine("Waiting 30 seconds before publishing (as recommended by API documentation)...");
				await Task.Delay(TimeSpan.FromSeconds(30));


				// ----------------------------------------------------------------------
				// ИСПРАВЛЕНИЕ 3: ШАГ 2: Публикация контейнера (POST /threads_publish)
				// ----------------------------------------------------------------------
				Console.WriteLine("Step 2: Publishing Threads media container...");

				// ИСПРАВЛЕНИЕ 3.1: Используем creation_id из шага 1
				var publishPayload = new FormUrlEncodedContent(new[]
				{
			new KeyValuePair<string, string>("creation_id", mediaContainerId)
		});

				// ИСПРАВЛЕНИЕ 3.2: Правильный endpoint для публикации
				var publishUrl = $"https://graph.threads.net/v1.0/{_userId}/threads_publish";
				var publishResponse = await _httpClient.PostAsync(publishUrl, publishPayload);
				var publishResponseContent = await publishResponse.Content.ReadAsStringAsync();

				if (!publishResponse.IsSuccessStatusCode)
				{
					// ИСПРАВЛЕНИЕ 3.3: Обработка ошибок
					throw HandleThreadsError(publishResponse.StatusCode, publishResponseContent, "Post Publishing");
				}

				// ИСПРАВЛЕНИЕ 3.4: Десериализация ответа публикации
				var postResult = JsonSerializer.Deserialize<ThreadsPostResponse>(publishResponseContent);

				Console.WriteLine($"Step 2 Success. Published Post ID: {postResult?.Id}");

				return new ThreadsPostResult
				{
					Id = postResult?.Id,
					Success = !string.IsNullOrEmpty(postResult?.Id),
					Platform = "Threads"
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error creating Threads post: {ex.Message}");
				// Не нужно дублировать throw, если вы уже бросаете исключение в HandleThreadsError
				throw;
			}
		}

		// Вспомогательный метод для обработки ошибок
		private HttpRequestException HandleThreadsError(System.Net.HttpStatusCode statusCode, string responseContent, string stepName)
		{
			try
			{
				var error = JsonSerializer.Deserialize<ThreadsErrorResponse>(responseContent);
				return new HttpRequestException($"Threads API Error (Step {stepName}): {error?.Error?.Message} (Code: {error?.Error?.Code}, Subcode: {error?.Error?.ErrorSubcode})", null, statusCode);
			}
			catch (JsonException)
			{
				return new HttpRequestException($"Threads API Error (Step {stepName}): {statusCode} - {responseContent}", null, statusCode);
			}
		}

		// Дополнительный метод для создания поста с медиа
		public async Task<ThreadsPostResult> CreateThreadWithMediaAsync(string text, string mediaId)
		{
			try
			{
				Console.WriteLine("Creating Threads post with media...");

				var payload = new
				{
					media_type = "IMAGE_POST", // или "VIDEO_POST" для видео
					text = text,
					media_id = mediaId
				};

				var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
				{
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

				var content = new StringContent(json, Encoding.UTF8, "application/json");
				content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

				var url = $"https://graph.threads.net/v1.0/{_userId}/threads";
				var request = new HttpRequestMessage(HttpMethod.Post, url)
				{
					Content = content
				};
				request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

				var response = await _httpClient.SendAsync(request);
				var responseContent = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"Threads Media API Response: {responseContent}");

				if (!response.IsSuccessStatusCode)
				{
					var error = JsonSerializer.Deserialize<ThreadsErrorResponse>(responseContent);
					throw new HttpRequestException($"Threads Media API Error: {error?.Error?.Message}");
				}

				var result = JsonSerializer.Deserialize<ThreadsPostResponse>(responseContent);

				return new ThreadsPostResult
				{
					Id = result?.Id,
					Success = result?.Success ?? false,
					Platform = "Threads"
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error creating Threads media post: {ex.Message}");
				throw;
			}
		}

		// Метод для загрузки медиа (если нужно)
		public async Task<string> UploadMediaAsync(string imageUrl)
		{
			try
			{
				var payload = new
				{
					image_url = imageUrl
				};

				var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
				{
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

				var content = new StringContent(json, Encoding.UTF8, "application/json");
				content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

				var url = $"https://graph.threads.net/v1.0/{_userId}/media";
				var request = new HttpRequestMessage(HttpMethod.Post, url)
				{
					Content = content
				};
				request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

				var response = await _httpClient.SendAsync(request);
				var responseContent = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"Media Upload Response: {responseContent}");

				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Media upload failed: {responseContent}");
				}

				var result = JsonSerializer.Deserialize<ThreadsMediaResponse>(responseContent);
				return result?.Id;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error uploading media: {ex.Message}");
				throw;
			}
		}
	}

	// Обновленные модели данных
	public class ThreadsPostResponse
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("success")]
		public bool Success { get; set; }
	}

	public class ThreadsMediaResponse
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("media_type")]
		public string MediaType { get; set; }
	}

	public class ThreadsPostResult
	{
		public string Id { get; set; }
		public bool Success { get; set; }
		public string Platform { get; set; }
		public string ErrorMessage { get; set; }
	}

	public class ThreadsErrorResponse
	{
		[JsonPropertyName("error")]
		public ThreadsError Error { get; set; }
	}

	public class ThreadsError
	{
		[JsonPropertyName("message")]
		public string Message { get; set; }

		[JsonPropertyName("type")]
		public string Type { get; set; }

		[JsonPropertyName("code")]
		public int Code { get; set; }

		[JsonPropertyName("error_subcode")]
		public int ErrorSubcode { get; set; }

		[JsonPropertyName("fbtrace_id")]
		public string FbTraceId { get; set; }
	}

	public class ThreadsMediaContainerResponse
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } // Это ID контейнера (creation_id)
	}
}
