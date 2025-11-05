using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class BlueSkyService
	{
		// Объявляем HttpClient как член класса (с использованием 'readonly' для потокобезопасности)
		private readonly HttpClient _httpClient;
		private const string RefreshUrl = "https://bsky.social/xrpc/com.atproto.server.refreshSession";
		private const string LoginUrl = "https://bsky.social/xrpc/com.atproto.server.createSession"; // Добавлен для удобства
		private readonly string _identifire = "alinakross.bsky.social";
		private readonly string _appPassword = "d4an-bvic-ssrd-r663";

		// Свойства для хранения состояния сессии
		public string AccessJwt { get; private set; } = string.Empty;
		public string RefreshJwt { get; private set; } = string.Empty;
		public string Did { get; private set; } = "did:plc:oqaj3ux2ixowx36aifbqcvjz";//string.Empty;
		public string PdsUrl { get; private set; } = "https://auriporia.us-west.host.bsky.network";//string.Empty;

		public BlueSkyService(string identifire, string appPassword)
		{
			_identifire = identifire;
			_appPassword = appPassword;
			_httpClient = new HttpClient();
		}

		public bool BlueSkyLogin = false;

		// --- Шаг 1: Первичный вход (Login) ---

		// Новый метод для выполнения первого входа по логину/паролю
		public async Task<bool> LoginAsync()
		{
			var payload = new { identifier = _identifire, password = _appPassword };
			var jsonPayload = JsonSerializer.Serialize(payload);
			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			try
			{
				var response = await _httpClient.PostAsync(LoginUrl, content);

				if (response.IsSuccessStatusCode)
				{
					var jsonResponse = await response.Content.ReadAsStringAsync();
					var session = JsonSerializer.Deserialize<SessionResponse>(jsonResponse);

					if (session != null)
					{
						InitializeSession(session);
						return true;
					}
				}
				else
				{
					var errorContent = await response.Content.ReadAsStringAsync();
					Console.WriteLine($"❌ Ошибка первичного входа: {response.StatusCode} - {errorContent}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при входе: {ex.Message}");
			}
			return false;
		}


		// Метод для инициализации сессии после успешного входа или обновления
		public void InitializeSession(SessionResponse response)
		{
			AccessJwt = response.AccessJwt;
			RefreshJwt = response.RefreshJwt;
			Did = response.Did;

			// Извлечение PDS URL из didDoc
			PdsUrl = response.DidDoc?.Service?
				.FirstOrDefault(s => s.Type == "AtprotoPersonalDataServer")?
				.ServiceEndpoint ?? "https://bsky.social"; // Запасной вариант

			Console.WriteLine($"Сессия инициализирована. PDS URL: {PdsUrl}");
		}

		// --- Шаг 2: Обновление токена (Refresh) ---

		public async Task<bool> UpdateSessionAsync()
		{

			if (string.IsNullOrEmpty(RefreshJwt))
			{
				Console.WriteLine("❌ Невозможно обновить сессию: Refresh Token не задан.");
				return false;
			}

			// Устанавливаем заголовок Authorization с текущим Refresh Token
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RefreshJwt);

			// --- ИСПРАВЛЕНИЕ: Создаем пустой контент ---
			// Это гарантирует, что тело запроса будет отправлено, но будет пустым.
			var emptyContent = new StringContent(string.Empty, Encoding.UTF8, "application/json");

			try
			{
				// Используем пустой StringContent вместо HttpContent.Empty
				HttpResponseMessage response = await _httpClient.PostAsync(RefreshUrl, emptyContent);
				// ----------------------------------------

				if (response.IsSuccessStatusCode)
				{
					string jsonResponse = await response.Content.ReadAsStringAsync();
					var newSession = JsonSerializer.Deserialize<SessionResponse>(jsonResponse);

					if (newSession != null && !string.IsNullOrEmpty(newSession.AccessJwt))
					{
						// !!! ПЕРЕЗАПИСЫВАЕМ СТАРЫЕ ЗНАЧЕНИЯ НОВЫМИ !!!
						AccessJwt = newSession.AccessJwt;
						RefreshJwt = newSession.RefreshJwt;
						Console.WriteLine("✅ Токены успешно обновлены в памяти.");
						return true;
					}
				}

				// Если обновление не удалось (например, refreshJwt тоже истёк или отозван)
				var errorContent = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"❌ Обновление сессии не удалось. Статус: {response.StatusCode} - {errorContent}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при обновлении токена: {ex.Message}");
			}
			return false;
		}

		// --- Шаг 3: Создание поста ---

		// Метод теперь не принимает токены/PDS URL, а использует внутренние свойства класса
		public async Task<bool> CreatePostAsync(string postText)
		{
			if (string.IsNullOrEmpty(AccessJwt) || string.IsNullOrEmpty(PdsUrl))
			{
				Console.WriteLine("❌ Сессия не активна или PDS URL не определен. Выполните Login/UpdateSession.");
				return false;
			}

			var postEndpoint = $"{PdsUrl}/xrpc/com.atproto.repo.createRecord";

			// 1. Устанавливаем токен AccessJwt
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessJwt);

			// 2. Создаем тело запроса
			var record = new
			{
				text = postText,
				createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
			};

			var payload = new
			{
				repo = Did, // Используем внутренний Did
				collection = "app.bsky.feed.post",
				record = record
			};

			var jsonPayload = JsonSerializer.Serialize(payload);
			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			// 3. Отправляем запрос
			var response = await _httpClient.PostAsync(postEndpoint, content);

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine("✅ Пост успешно опубликован!");
				return true;
			}
			else
			{
				var errorContent = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"❌ Ошибка при создании поста: {response.StatusCode} - {errorContent}");
				return false;
			}
		}

		public async Task<bool> CreatePostWithVideoAsync(string postText, Blob videoBlob, AspectRatio aspectRatio)
		{
			if (string.IsNullOrEmpty(AccessJwt) || string.IsNullOrEmpty(PdsUrl))
			{
				Console.WriteLine("❌ Сессия не активна.");
				return false;
			}

			var postEndpoint = $"{PdsUrl}/xrpc/com.atproto.repo.createRecord";
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessJwt);

			// 1. Создаем объект EMBED для видео
			var embedPayload = new VideoEmbedPayload
			{
				Video = videoBlob, // Blob, полученный из UploadVideoFromBase64Async
				AspectRatio = aspectRatio // Соотношение сторон
			};

			// 2. Создаем тело записи (PostRecord)
			var record = new PostRecord
			{
				Text = postText,
				CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
				Embed = embedPayload
			};

			var payload = new
			{
				repo = Did,
				collection = "app.bsky.feed.post",
				record = record
			};

			// 3. Сериализация и отправка
			var jsonPayload = JsonSerializer.Serialize(payload);
			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			var response = await _httpClient.PostAsync(postEndpoint, content);

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine("✅ Пост с видео успешно опубликован!");
				return true;
			}
			else
			{
				var errorContent = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"❌ Ошибка при создании поста с видео: {response.StatusCode} - {errorContent}");
				return false;
			}
		}

		public async Task<Blob?> UploadVideoFromBase64Async(string base64Video, string mimeType)
		{
			if (string.IsNullOrEmpty(AccessJwt) || string.IsNullOrEmpty(PdsUrl))
			{
				Console.WriteLine("❌ Сессия не активна.");
				return null;
			}

			var uploadUrl = $"{PdsUrl}/xrpc/com.atproto.repo.uploadBlob";
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessJwt);

			try
			{
				// 1. Преобразование Base64 в массив байтов
				byte[] fileBytes;
				try
				{
					fileBytes = Convert.FromBase64String(base64Video);
				}
				catch (FormatException)
				{
					Console.WriteLine("❌ Ошибка: Входная строка Base64 видео имеет неверный формат.");
					return null;
				}

				var fileContent = new ByteArrayContent(fileBytes);
				// 2. Указываем MIME-тип видео
				fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

				// 3. Отправляем на uploadBlob (PDS сам решит, куда его направить)
				HttpResponseMessage response = await _httpClient.PostAsync(uploadUrl, fileContent);

				if (response.IsSuccessStatusCode)
				{
					string jsonResponse = await response.Content.ReadAsStringAsync();
					var result = JsonSerializer.Deserialize<UploadBlobResponse>(jsonResponse);

					if (result?.Blob != null)
					{
						Console.WriteLine("✅ Видео успешно загружено из Base64.");
						return result.Blob;
					}
				}

				var errorContent = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"❌ Ошибка загрузки видео: {response.StatusCode} - {errorContent}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при загрузке видео: {ex.Message}");
			}
			return null;
		}

		public async Task<Blob?> UploadImageFromBase64Async(string base64Image, string mimeType)
		{
			if (string.IsNullOrEmpty(AccessJwt) || string.IsNullOrEmpty(PdsUrl))
			{
				Console.WriteLine("❌ Сессия не активна.");
				return null;
			}

			var uploadUrl = $"{PdsUrl}/xrpc/com.atproto.repo.uploadBlob";

			// Устанавливаем AccessJwt
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessJwt);

			try
			{
				// --- 1. Преобразование Base64 в массив байтов (byte[]) ---
				byte[] fileBytes;
				try
				{
					fileBytes = Convert.FromBase64String(base64Image);
				}
				catch (FormatException)
				{
					Console.WriteLine("❌ Ошибка: Входная строка Base64 имеет неверный формат.");
					return null;
				}
				// --------------------------------------------------------

				var fileContent = new ByteArrayContent(fileBytes);

				// Установка правильного MIME-типа для контента
				fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

				// Отправка POST-запроса
				HttpResponseMessage response = await _httpClient.PostAsync(uploadUrl, fileContent);

				if (response.IsSuccessStatusCode)
				{
					string jsonResponse = await response.Content.ReadAsStringAsync();
					var result = JsonSerializer.Deserialize<UploadBlobResponse>(jsonResponse);

					if (result?.Blob != null)
					{
						Console.WriteLine("✅ Изображение успешно загружено из Base64.");
						return result.Blob; // Возвращаем Blob, готовый к постингу
					}
				}

				var errorContent = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"❌ Ошибка загрузки изображения: {response.StatusCode} - {errorContent}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при загрузке файла: {ex.Message}");
			}
			return null;
		}

		public async Task<bool> CreatePostWithImagesAsync(string postText, List<ImageAttachment> images)
		{
			if (string.IsNullOrEmpty(AccessJwt) || string.IsNullOrEmpty(PdsUrl))
			{
				Console.WriteLine("❌ Сессия не активна.");
				return false;
			}
			if (images.Count == 0)
			{
				Console.WriteLine("❌ Для данного метода требуется хотя бы одно изображение.");
				return false;
			}

			var postEndpoint = $"{PdsUrl}/xrpc/com.atproto.repo.createRecord";
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessJwt);

			// 1. Создаем объект EMBED: ВСЕГДА используем app.bsky.embed.images
			var embedPayload = new ImageEmbedPayload
			{
				Images = images // Используем список ImageAttachment (будь то 1 или 4 элемента)
			};

			// 2. Создаем тело записи
			var record = new PostRecord
			{
				Text = postText,
				CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
				Embed = embedPayload // Embed принимает наш объект ImageEmbedPayload
			};

			var payload = new
			{
				repo = Did,
				collection = "app.bsky.feed.post",
				record = record
			};

			// 3. Отправка и обработка
			var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }); // Включим WriteIndented для отладки
			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			var response = await _httpClient.PostAsync(postEndpoint, content);

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine("✅ Пост с изображениями успешно опубликован!");
				return true;
			}
			else
			{
				var errorContent = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"❌ Ошибка при создании поста с изображениями: {response.StatusCode} - {errorContent}");
				// Выведите отправленный JSON для финальной проверки структуры
				Console.WriteLine($"Отправленный JSON (для отладки): {jsonPayload}");
				return false;
			}
		}
	}

	public class PostRecord
	{
		// Обязательное поле $type для записи поста
		[JsonPropertyName("$type")]
		public string Type { get; } = "app.bsky.feed.post";

		[JsonPropertyName("text")]
		public string Text { get; set; } = string.Empty;

		[JsonPropertyName("createdAt")]
		public string CreatedAt { get; set; } = string.Empty;

		// Вложение (изображения, ссылки и т.д.)
		[JsonPropertyName("embed")]
		public object? Embed { get; set; }

		// (Необязательные поля, такие как reply, facets, langs, здесь опущены)
	}

	public class MediaImagePayload
	{
		[JsonPropertyName("$type")]
		public string Type { get; } = "app.bsky.embed.media";

		[JsonPropertyName("media")]
		public MediaContent Media { get; set; } = new MediaContent();
	}

	public class MediaContent
	{
		[JsonPropertyName("$type")]
		public string Type { get; } = "app.bsky.embed.media.image";

		[JsonPropertyName("image")]
		public Blob Image { get; set; }

		[JsonPropertyName("alt")]
		public string AltText { get; set; } = string.Empty;
	}

	public class ImageEmbedPayload
	{
		[JsonPropertyName("$type")]
		public string Type { get; } = "app.bsky.embed.images"; // Имя свойства $type

		[JsonPropertyName("images")]
		public List<ImageAttachment> Images { get; set; } = new List<ImageAttachment>();
	}

	public class SessionResponse
	{
		// --- Ключевые поля для авторизации и PDS ---

		// Токен Доступа. Используется для всех действий (постинг, лайки и т.д.)
		[JsonPropertyName("accessJwt")]
		public string AccessJwt { get; set; } = string.Empty;

		// Токен Обновления. Используется для получения нового AccessJwt.
		[JsonPropertyName("refreshJwt")]
		public string RefreshJwt { get; set; } = string.Empty;

		// Децентрализованный Идентификатор (DID) пользователя. 
		[JsonPropertyName("did")]
		public string Did { get; set; } = string.Empty;

		// Хендл пользователя (например, alinakross.bsky.social)
		[JsonPropertyName("handle")]
		public string Handle { get; set; } = string.Empty;

		// --- Поля, связанные с DID Document (для удобства) ---

		// В AT Protocol Service Endpoint содержит URL вашего PDS.
		// Если вы десериализуете весь DID Doc, используйте этот класс:
		[JsonPropertyName("didDoc")]
		public DidDocument? DidDoc { get; set; }

		// --- Дополнительные поля ---

		[JsonPropertyName("email")]
		public string Email { get; set; } = string.Empty;

		[JsonPropertyName("emailConfirmed")]
		public bool EmailConfirmed { get; set; }

		[JsonPropertyName("active")]
		public bool Active { get; set; }
	}

	public class Service
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = string.Empty;

		[JsonPropertyName("type")]
		public string Type { get; set; } = string.Empty;

		// URL вашего Персонального Сервера Данных (PDS)
		[JsonPropertyName("serviceEndpoint")]
		public string ServiceEndpoint { get; set; } = string.Empty;
	}

	public class DidDocument
	{
		// Массив, содержащий URL вашего PDS
		[JsonPropertyName("service")]
		public List<Service>? Service { get; set; }

		// (Могут быть другие поля, такие как context, id, verificationMethod, но они менее критичны для автопостинга)
	}

	public class UploadBlobResponse
	{
		[JsonPropertyName("blob")]
		public Blob? Blob { get; set; }
	}

	public class AspectRatio
	{
		[JsonPropertyName("width")]
		public int Width { get; set; }

		[JsonPropertyName("height")]
		public int Height { get; set; }
	}

	// 2. Класс для вложения видео (app.bsky.embed.video)
	public class VideoEmbedPayload
	{
		[JsonPropertyName("$type")]
		public string Type { get; } = "app.bsky.embed.video";

		[JsonPropertyName("video")]
		public Blob Video { get; set; } // Blob, полученный после загрузки

		[JsonPropertyName("aspectRatio")]
		public AspectRatio? AspectRatio { get; set; }
	}

	public class Blob
	{
		// Cлужебный дескриптор, необходимый для включения в запись поста
		[JsonPropertyName("$type")]
		public string Type { get; set; } = string.Empty;

		// MIME-тип изображения (image/jpeg, image/png)
		[JsonPropertyName("mimeType")]
		public string MimeType { get; set; } = string.Empty;

		// Криптографический хэш содержимого (CID)
		[JsonPropertyName("ref")]
		public Ref? Ref { get; set; }

		// Размер файла в байтах
		[JsonPropertyName("size")]
		public long Size { get; set; }
	}

	public class Ref
	{
		// CID в формате AT URI
		[JsonPropertyName("$link")]
		public string Link { get; set; } = string.Empty;
	}

	public class ImageAttachment
	{
		[JsonPropertyName("image")]
		public Blob Image { get; set; }

		[JsonPropertyName("alt")]
		public string AltText { get; set; } = string.Empty;
	}
}
