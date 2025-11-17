using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;


namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class BlueSkyService : SocialBaseService
	{
		// Объявляем HttpClient как член класса (с использованием 'readonly' для потокобезопасности)
		private const int MAX_GRAPHEME_LENGTH = 300;
		private readonly HttpClient _httpClient;
		private const string RefreshUrl = "https://bsky.social/xrpc/com.atproto.server.refreshSession";
		private const string LoginUrl = "https://bsky.social/xrpc/com.atproto.server.createSession"; // Добавлен для удобства
		private readonly string _identifire = "alinakross.bsky.social";
		private readonly string _appPassword = "d4an-bvic-ssrd-r663";

		// Свойства для хранения состояния сессии
		public string AccessJwt { get; private set; } = "eyJ0eXAiOiJhdCtqd3QiLCJhbGciOiJFUzI1NksifQ.eyJzY29wZSI6ImNvbS5hdHByb3RvLmFwcFBhc3NQcml2aWxlZ2VkIiwic3ViIjoiZGlkOnBsYzpvcWFqM3V4Mml4b3d4MzZhaWZicWN2anoiLCJpYXQiOjE3NjMzNjkyMjMsImV4cCI6MTc2MzM3NjQyMywiYXVkIjoiZGlkOndlYjphdXJpcG9yaWEudXMtd2VzdC5ob3N0LmJza3kubmV0d29yayJ9.SoV2pX6i86ZvYOatcGFeDdMIGGugIL3G3O5yPT0A8B4d2aNfdZwfpKBoPEMJLI_ofoqGuED-EtV28Qati-6sfA";
		public string RefreshJwt { get; private set; } = string.Empty;
		public string Did { get; private set; } = "did:plc:oqaj3ux2ixowx36aifbqcvjz";//string.Empty;
		public string PdsUrl { get; private set; } = "https://auriporia.us-west.host.bsky.network";//string.Empty;
		protected override string ServiceName => "BlueSky";

		public BlueSkyService(string identifire, string appPassword, IGenerativeLanguageModel generativeLanguageModel, TelegramService telegramService)
			: base(generativeLanguageModel, telegramService)
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

			List<Facet> facets = TryGetFacets(postText);

			// 1. Устанавливаем токен AccessJwt
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessJwt);

			// 2. Создаем тело запроса
			var record = new PostRecord
			{
				Text = postText,
				Facets = facets.Any() ? facets : null,
				CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
			};

			var payload = new
			{
				repo = Did, // Используем внутренний Did
				collection = "app.bsky.feed.post",
				record = record
			};

			var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
			{ 
				WriteIndented = true, 
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull 
			});
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

			List<Facet> facets = TryGetFacets(postText);

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
				Facets = facets.Any() ? facets : null,
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
			var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
			{ 
				WriteIndented = true, 
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull 
			});
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

			List<Facet> facets = TryGetFacets(postText);

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
				Facets = facets.Any() ? facets : null,
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
			var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
			{ 
				WriteIndented = true, 
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull 
			});
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

		private static List<Facet> TryGetFacets(string postText)
		{
			var facets = new List<Facet>();
			// Паттерн для поиска хештегов: #слово (должно быть пробел или конец строки после слова)
			var hashtagRegex = new Regex(@"#(\w+)");

			foreach (Match match in hashtagRegex.Matches(postText))
			{
				var hashtagText = match.Groups[1].Value; // Слово без #
				var matchIndex = match.Index;           // Индекс начала совпадения (включая #)

				// Вычисление смещений в БАЙТАХ
				// Bluesky требует байтовые смещения.
				var byteStart = Encoding.UTF8.GetByteCount(postText.Substring(0, matchIndex));
				var byteEnd = Encoding.UTF8.GetByteCount(postText.Substring(0, matchIndex + match.Length));

				var facet = new Facet
				{
					Index = new ByteSlice
					{
						ByteStart = byteStart,
						ByteEnd = byteEnd
					},
					Features = new List<object>
					{
						new TagFeature { Tag = hashtagText }
					}
				};
				facets.Add(facet);
			}

			return facets;
		}

		public async Task<string> TruncateTextToMaxLength(string text)
		{
			if (string.IsNullOrEmpty(text)) return text;

			var stringInfo = new StringInfo(text);
			if (stringInfo.LengthInTextElements <= MAX_GRAPHEME_LENGTH)
				return text;

			var prompt = "Данный текст для вставки в описание публикации в bluesky, must not be longer than 300 graphemes. " +
				"Сократи его до 300, таким образом что бы по возможности сохранить смысл и хотя бы часть хештегов. " +
				"Верни только готовый результат, без пояснений, дополнительных скобок и форматирования." +
				$"Вот само описание: {text}";

			return await _generativeLanguageModel.GeminiRequest(prompt);
		}

		protected override string GetBaseDescriptionPrompt(string base64Img)
		{
			return "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в bluesky под постом с фотографией. " +
				$"А так же придумай не более 15 хештогов, они должны соответствовать " +
				$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
				$"Вот само изображение: {base64Img}" +
				$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
				$"без всякого рода ковычек и экранирования. " +
				$"Пример ответа: ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence " +
				$"#neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";
		}
	}

	// Структура для определения диапазона символов
	public class ByteSlice
	{
		// Индекс начала (в байтах)
		[JsonPropertyName("byteStart")]
		public int ByteStart { get; set; }

		// Индекс конца (в байтах)
		[JsonPropertyName("byteEnd")]
		public int ByteEnd { get; set; }
	}

	// Структура для определения типа ссылки (Хештег)
	public class TagFeature
	{
		// Обязательный для хештегов
		[JsonPropertyName("$type")]
		public string Type { get; set; } = "app.bsky.richtext.facet#tag";

		// Само значение хештега (БЕЗ символа #)
		[JsonPropertyName("tag")]
		public string Tag { get; set; }
	}

	// Главная структура фасета
	public class Facet
	{
		// Диапазон символов в тексте
		[JsonPropertyName("index")]
		public ByteSlice Index { get; set; }

		// Определение ссылки (может быть TagFeature, LinkFeature, MentionFeature)
		[JsonPropertyName("features")]
		public List<object> Features { get; set; }
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

		[JsonPropertyName("facets")]
		public List<Facet> Facets { get; set; }

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
