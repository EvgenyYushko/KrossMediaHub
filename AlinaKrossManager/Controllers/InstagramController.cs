using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace AlinaKrossManager.Controllers
{
	[ApiController]
	[Route("instagram")]
	public class InstagramController : ControllerBase
	{
		private readonly InstagramOAuthService _oauthService;
		private readonly IMemoryCache _cache;
		private readonly ILogger<InstagramController> _logger;
		//private readonly ApplicationDbContext _dbContext;
		private readonly IConfiguration _configuration;

		public InstagramController(
			InstagramOAuthService oauthService,
			IMemoryCache cache,
			ILogger<InstagramController> logger,
			//ApplicationDbContext dbContext,
			IConfiguration configuration)
		{
			_oauthService = oauthService;
			_cache = cache;
			_logger = logger;
			//_dbContext = dbContext;
			_configuration = configuration;
		}

		// Эндпоинт 1: Получение OAuth ссылки
		[HttpGet("auth/url")]
		public IActionResult GetAuthUrl([FromQuery] string userId)
		{
			try
			{
				if (string.IsNullOrEmpty(userId))
				{
					return BadRequest(new { error = "User ID is required" });
				}

				// Проверяем, не превышает ли пользователь лимит
				var cacheKey = $"auth_attempts_{userId}";
				var attempts = _cache.GetOrCreate(cacheKey, entry =>
				{
					entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
					return 0;
				});

				if (attempts >= 5)
				{
					return StatusCode(429, new { error = "Too many attempts. Try again later." });
				}

				// Увеличиваем счетчик попыток
				_cache.Set(cacheKey, attempts + 1);

				// Генерируем ссылку
				var authUrl = _oauthService.GenerateSimpleOAuthUrl(userId);

				_logger.LogInformation($"Auth URL generated for user {userId}");

				return Ok(new { auth_url = authUrl });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error generating auth URL");
				return StatusCode(500, new { error = "Internal server error" });
			}
		}

		// Эндпоинт 2: Callback от Instagram (OAuth redirect)
		[HttpGet("auth/callback")]
		public async Task<IActionResult> OAuthCallback(
			[FromQuery] string code,
			[FromQuery] string state = null,
			[FromQuery] string error = null,
			[FromQuery] string error_reason = null,
			[FromQuery] string error_description = null)
		{
			try
			{
				_logger.LogInformation($"=== Callback received ===");
				_logger.LogInformation($"Raw code: {code?.Substring(0, Math.Min(20, code?.Length ?? 0))}...");
				_logger.LogInformation($"Raw state: {state}");

				string userId = "unknown";

				if (!string.IsNullOrEmpty(state))
				{
					try
					{
						// Декодируем URL-кодирование
						var decodedState = HttpUtility.UrlDecode(state);
						_logger.LogInformation($"Decoded state: {decodedState}");

						// Убираем экранирование кавычек
						var unescapedState = decodedState.Replace("\\\"", "\"");
						_logger.LogInformation($"Unescaped state: {unescapedState}");

						var stateData = JsonConvert.DeserializeObject<InstagramAuthState>(unescapedState);
						if (stateData != null)
						{
							userId = stateData.UserId ?? "unknown";
							_logger.LogInformation($"Extracted user_id: {userId}");
						}
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Failed to parse state");
					}
				}

				// Исправляем redirectUri - убираем двойной слеш
				var redirectUri = "https://krossmediahub.onrender.com/instagram/auth/callback";

				var tokenResponse = await _oauthService.ExchangeCodeForTokenAsync(code, redirectUri);

				return Ok(new
				{
					success = true,
					message = "Instagram успешно подключен!",
					user_id = userId,
					instagram_user_id = tokenResponse.UserId.ToString(),
					access_token = tokenResponse?.AccessToken?.Substring(0, Math.Min(20, tokenResponse?.AccessToken?.Length ?? 0)) + "...",
					expires_in = tokenResponse?.ExpiresIn
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in OAuth callback");
				return StatusCode(500, new { error = ex.Message });
			}
		}

		// Эндпоинт 3: Верификация вебхука (GET запрос от Instagram)
		//[HttpGet("webhook")]
		//public IActionResult VerifyWebhook(
		//	[FromQuery(Name = "hub.mode")] string mode,
		//	[FromQuery(Name = "hub.challenge")] string challenge,
		//	[FromQuery(Name = "hub.verify_token")] string verifyToken)
		//{
		//	try
		//	{
		//		_logger.LogInformation($"Webhook verification: mode={mode}, challenge={challenge}");

		//		if (mode == "subscribe")
		//		{
		//			// Проверяем verify token (можно хранить в БД или конфиге)
		//			var expectedToken = _configuration["Instagram:WebhookVerifyToken"];

		//			if (verifyToken == expectedToken)
		//			{
		//				_logger.LogInformation("Webhook verified successfully");
		//				return Ok(challenge);
		//			}
		//			else
		//			{
		//				_logger.LogWarning($"Invalid verify token: {verifyToken}");
		//				return Unauthorized();
		//			}
		//		}

		//		return BadRequest(new { error = "Invalid mode" });
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error verifying webhook");
		//		return StatusCode(500);
		//	}
		//}

		// Эндпоинт 4: Прием входящих сообщений (POST запрос от Instagram)
		//[HttpPost("webhook")]
		//public async Task<IActionResult> ReceiveWebhook()
		//{
		//	try
		//	{
		//		// Читаем тело запроса
		//		using var reader = new StreamReader(Request.Body);
		//		var payload = await reader.ReadToEndAsync();

		//		_logger.LogInformation($"Received webhook payload: {payload}");

		//		// Проверяем подпись
		//		var signature = Request.Headers["X-Hub-Signature-256"].ToString();
		//		var appSecret = _configuration["Instagram:AppSecret"];

		//		if (!_oauthService.VerifyWebhookSignature(payload, signature, appSecret))
		//		{
		//			_logger.LogWarning("Invalid webhook signature");
		//			return Unauthorized();
		//		}

		//		// Парсим payload
		//		var webhookData = JsonConvert.DeserializeObject<InstagramWebhookPayload>(payload);

		//		if (webhookData?.Entry == null || webhookData.Entry.Count == 0)
		//		{
		//			_logger.LogWarning("Empty webhook data");
		//			return Ok("EVENT_RECEIVED");
		//		}

		//		// Обрабатываем сообщения асинхронно
		//		_ = Task.Run(() => ProcessWebhookMessages(webhookData));

		//		// Возвращаем 200 OK сразу (требование Instagram)
		//		return Ok("EVENT_RECEIVED");
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error processing webhook");
		//		// Все равно возвращаем 200, чтобы Instagram не отключал вебхук
		//		return Ok("EVENT_RECEIVED");
		//	}
		//}

		//// Эндпоинт 5: Проверка статуса подключения
		//[HttpGet("status/{userId}")]
		//public async Task<IActionResult> GetConnectionStatus(string userId)
		//{
		//	try
		//	{
		//		var connection = await _dbContext.InstagramConnections
		//			.FirstOrDefaultAsync(c => c.UserId == userId);

		//		if (connection == null)
		//		{
		//			return Ok(new { connected = false });
		//		}

		//		// Проверяем, не истек ли токен
		//		var isTokenValid = connection.TokenExpiresAt > DateTime.UtcNow;

		//		return Ok(new
		//		{
		//			connected = true,
		//			username = connection.InstagramUsername,
		//			connected_at = connection.ConnectedAt,
		//			token_valid = isTokenValid,
		//			expires_at = connection.TokenExpiresAt,
		//			last_message_at = connection.LastMessageAt
		//		});
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error getting connection status");
		//		return StatusCode(500, new { error = "Internal server error" });
		//	}
		//}

		// Эндпоинт 6: Отключение Instagram
		//[HttpDelete("disconnect/{userId}")]
		//public async Task<IActionResult> Disconnect(string userId)
		//{
		//	try
		//	{
		//		var connection = await _dbContext.InstagramConnections
		//			.FirstOrDefaultAsync(c => c.UserId == userId);

		//		if (connection == null)
		//		{
		//			return NotFound(new { error = "Connection not found" });
		//		}

		//		// Отписываемся от вебхуков
		//		await UnsubscribeFromWebhooks(connection);

		//		// Удаляем из БД
		//		_dbContext.InstagramConnections.Remove(connection);
		//		await _dbContext.SaveChangesAsync();

		//		_logger.LogInformation($"Instagram disconnected for user {userId}");

		//		return Ok(new { message = "Disconnected successfully" });
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error disconnecting Instagram");
		//		return StatusCode(500, new { error = "Internal server error" });
		//	}
		//}

		// Эндпоинт 7: Тестовый эндпоинт для отправки сообщений
		//[HttpPost("test/send")]
		//public async Task<IActionResult> SendTestMessage(
		//	[FromBody] TestMessageRequest request)
		//{
		//	try
		//	{
		//		if (!ModelState.IsValid)
		//			return BadRequest(ModelState);

		//		var connection = await _dbContext.InstagramConnections
		//			.FirstOrDefaultAsync(c => c.UserId == request.UserId);

		//		if (connection == null)
		//			return NotFound(new { error = "Instagram not connected" });

		//		// Отправляем тестовое сообщение
		//		var result = await SendInstagramMessage(
		//			connection.AccessToken,
		//			connection.InstagramUserId,
		//			request.RecipientId,
		//			request.Message);

		//		if (result)
		//		{
		//			return Ok(new { success = true, message = "Message sent" });
		//		}
		//		else
		//		{
		//			return StatusCode(500, new { error = "Failed to send message" });
		//		}
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error sending test message");
		//		return StatusCode(500, new { error = ex.Message });
		//	}
		//}

		// Приватные методы
		//private async Task SaveUserToken(string userId, InstagramTokenResponse tokenResponse)
		//{
		//	//var connection = new InstagramConnection
		//	//{
		//	//	UserId = userId,
		//	//	AccessToken = tokenResponse.AccessToken,
		//	//	InstagramUserId = tokenResponse.UserId,
		//	//	ConnectedAt = DateTime.UtcNow,
		//	//	TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
		//	//	IsActive = true
		//	//};

		//	//_dbContext.InstagramConnections.Add(connection);
		//	//await _dbContext.SaveChangesAsync();

		//	_logger.LogInformation($"Token saved for user {userId}");
		//}

		//private async Task SetupInstagramWebhooks(string userId, string accessToken)
		//{
		//	try
		//	{
		//		// Здесь реализация подписки на вебхуки Instagram
		//		// Нужно вызвать Instagram API для подписки

		//		_logger.LogInformation($"Webhooks setup for user {userId}");

		//		// Сохраняем информацию о вебхуке в БД
		//		//var connection = await _dbContext.InstagramConnections
		//		//	.FirstOrDefaultAsync(c => c.UserId == userId);

		//		//if (connection != null)
		//		//{
		//		//	connection.WebhookSubscribed = true;
		//		//	connection.WebhookSubscribedAt = DateTime.UtcNow;
		//		//	await _dbContext.SaveChangesAsync();
		//		//}
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error setting up webhooks");
		//	}
		//}

		//private async Task ProcessWebhookMessages(InstagramWebhookPayload webhookData)
		//{
		//	foreach (var entry in webhookData.Entry)
		//	{
		//		foreach (var messagingEvent in entry.Messaging ?? new List<InstagramMessagingEvent>())
		//		{
		//			if (messagingEvent.Message != null && !string.IsNullOrEmpty(messagingEvent.Message.Text))
		//			{
		//				await ProcessIncomingMessage(
		//					messagingEvent.Sender.Id,
		//					messagingEvent.Recipient.Id,
		//					messagingEvent.Message.Text,
		//					messagingEvent.Message.Mid);
		//			}
		//		}
		//	}
		//}

		//private async Task ProcessIncomingMessage(
		//	string senderId,
		//	string recipientId,
		//	string messageText,
		//	string messageId)
		//{
		//	try
		//	{
		//		// Находим пользователя по recipientId (Instagram ID бота)
		//		var connection = await _dbContext.InstagramConnections
		//			.FirstOrDefaultAsync(c => c.InstagramUserId == recipientId);

		//		if (connection == null || !connection.IsActive)
		//			return;

		//		// Логируем входящее сообщение
		//		await LogIncomingMessage(
		//			connection.UserId,
		//			senderId,
		//			messageText,
		//			messageId);

		//		// Генерируем ответ через AI
		//		var aiResponse = await GenerateAIResponse(
		//			connection.UserId,
		//			messageText);

		//		// Отправляем ответ
		//		await SendInstagramMessage(
		//			connection.AccessToken,
		//			recipientId,
		//			senderId,
		//			aiResponse);

		//		// Обновляем время последнего сообщения
		//		connection.LastMessageAt = DateTime.UtcNow;
		//		await _dbContext.SaveChangesAsync();

		//		_logger.LogInformation($"Processed message from {senderId} to user {connection.UserId}");
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error processing incoming message");
		//	}
		//}

		//private async Task<string> GenerateAIResponse(string userId, string message)
		//{
		//	// Здесь интеграция с Gemini AI
		//	// Получаем промпт пользователя из БД и генерируем ответ

		//	// Заглушка
		//	return $"Спасибо за сообщение! Я получил: \"{message}\". Это автоматический ответ.";
		//}

		//private async Task<bool> SendInstagramMessage(
		//	string accessToken,
		//	string fromId,
		//	string toId,
		//	string message)
		//{
		//	try
		//	{
		//		using var httpClient = new HttpClient();

		//		var url = $"https://graph.instagram.com/v18.0/{fromId}/messages";

		//		var content = new
		//		{
		//			recipient = new { id = toId },
		//			message = new { text = message }
		//		};

		//		var jsonContent = JsonConvert.SerializeObject(content);
		//		var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

		//		var response = await httpClient.PostAsync($"{url}?access_token={accessToken}", httpContent);

		//		return response.IsSuccessStatusCode;
		//	}
		//	catch (Exception ex)
		//	{
		//		_logger.LogError(ex, "Error sending Instagram message");
		//		return false;
		//	}
		//}

		//private async Task UnsubscribeFromWebhooks(InstagramConnection connection)
		//{
		//	// Реализация отписки от вебхуков
		//	// Вызов Instagram API для удаления подписки

		//	connection.WebhookSubscribed = false;
		//	connection.WebhookUnsubscribedAt = DateTime.UtcNow;

		//	_logger.LogInformation($"Unsubscribed from webhooks for user {connection.UserId}");
		//}

		//private async Task LogIncomingMessage(string userId, string senderId, string message, string messageId)
		//{
		//	var logEntry = new MessageLog
		//	{
		//		Id = Guid.NewGuid(),
		//		UserId = userId,
		//		Platform = "instagram",
		//		SenderId = senderId,
		//		MessageId = messageId,
		//		MessageText = message,
		//		Direction = "incoming",
		//		ReceivedAt = DateTime.UtcNow,
		//		Processed = true
		//	};

		//	_dbContext.MessageLogs.Add(logEntry);
		//	await _dbContext.SaveChangesAsync();
		//}
	}

	// DTO для тестового сообщения
	//public class TestMessageRequest
	//{
	//	[Required]
	//	public string UserId { get; set; }

	//	[Required]
	//	public string RecipientId { get; set; }

	//	[Required]
	//	[MaxLength(1000)]
	//	public string Message { get; set; }
	//}

	public class InstagramOAuthService
	{
		private readonly IConfiguration _configuration;
		private readonly ILogger<InstagramOAuthService> _logger;

		public InstagramOAuthService(
			IConfiguration configuration,
			ILogger<InstagramOAuthService> logger)
		{
			_configuration = configuration;
			_logger = logger;
		}

		private const string INSTAGRAM_OAUTH_URL = "https://www.instagram.com/consent/?flow=ig_biz_login_oauth";
		private const string INSTAGRAM_TOKEN_URL = "https://api.instagram.com/oauth/access_token";
		private const string INSTAGRAM_GRAPH_URL = "https://graph.instagram.com";

		// Конфигурация из appsettings.json
		private string AppId => "1130517405203905";//_configuration["Instagram:AppId"];
		private string AppSecret => "190d6d42309964b51d0203d0520b36b3";//_configuration["Instagram:AppSecret"];
		private string BaseUrl => "https://krossmediahub.onrender.com";//_configuration["Instagram:BaseUrl"];

		// Генерация CSRF токена
		public string GenerateCsrfToken()
		{
			using var rng = RandomNumberGenerator.Create();
			byte[] tokenData = new byte[32];
			rng.GetBytes(tokenData);
			return Convert.ToBase64String(tokenData).Replace("+", "-").Replace("/", "_").Replace("=", "");
		}

		// Основной метод генерации OAuth ссылки
		public string GenerateInstagramOAuthUrl(string userId)
		{
			try
			{
				var csrfToken = GenerateCsrfToken();

				// Подготовка state объекта
				var state = new InstagramAuthState
				{
					UserId = userId,
					Provider = "instagram",
					Token = csrfToken,
					CallbackUrl = $"{BaseUrl}/instagram/auth/callback",
					MessagesCallback = $"{BaseUrl}/instagram/webhook"
				};

				var stateJson = JsonConvert.SerializeObject(state);

				// Подготовка params_json объекта
				var paramsData = new
				{
					client_id = AppId,
					redirect_uri = $"{BaseUrl}/instagram/auth/callback",
					response_type = "code",
					state = stateJson,
					scope = "instagram_business_basic,instagram_business_manage_messages",
					logger_id = Guid.NewGuid().ToString(),
					app_id = AppId,
					platform_app_id = AppId
				};

				var paramsJson = JsonConvert.SerializeObject(paramsData);

				// Кодирование для URL
				var encodedParams = HttpUtility.UrlEncode(paramsJson);

				// Формирование финальной ссылки
				var authUrl = $"{INSTAGRAM_OAUTH_URL}&params_json={encodedParams}&source=oauth_permissions_page_www";

				_logger.LogInformation($"Generated Instagram OAuth URL for user {userId}");

				return authUrl;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error generating Instagram OAuth URL");
				throw;
			}
		}

		// Альтернативная упрощенная версия
		public string GenerateSimpleOAuthUrl(string userId)
		{
			var csrfToken = GenerateCsrfToken();
			var state = new InstagramAuthState
			{
				UserId = userId,
				Provider = "instagram",
				Token = csrfToken,
				CallbackUrl = $"{BaseUrl}/instagram/auth/callback",
				MessagesCallback = $"{BaseUrl}/instagram/webhook"
			};

			var stateJson = JsonConvert.SerializeObject(state);
			var encodedState = HttpUtility.UrlEncode(stateJson);

			return $"https://api.instagram.com/oauth/authorize?" +
				   $"client_id={AppId}&" +
				   $"redirect_uri={HttpUtility.UrlEncode($"{BaseUrl}/instagram/auth/callback")}&" +
				   $"scope=instagram_business_basic,instagram_business_manage_messages&" +
				   $"response_type=code&" +
				   $"state={encodedState}";
		}

		// Обмен кода на токен
		public async Task<InstagramTokenResponse> ExchangeCodeForTokenAsync(string code, string redirectUri)
		{
			try
			{
				_logger.LogInformation($"=== Starting token exchange ===");
				_logger.LogInformation($"AppId: {AppId}");
				_logger.LogInformation($"AppSecret length: {AppSecret?.Length}");
				_logger.LogInformation($"RedirectUri: {redirectUri}");
				_logger.LogInformation($"Code length: {code?.Length}");
				_logger.LogInformation($"Code preview: {code?.Substring(0, Math.Min(20, code?.Length ?? 0))}...");

				using var client = new HttpClient();

				// ШАГ 1: Обмен кода на КРАТКОВРЕМЕННЫЙ токен (1 час)
				var shortLivedRequest = new HttpRequestMessage(HttpMethod.Post,
					"https://api.instagram.com/oauth/access_token");

				var formData = new Dictionary<string, string>
				{
					["client_id"] = AppId,
					["client_secret"] = AppSecret,
					["grant_type"] = "authorization_code",
					["redirect_uri"] = redirectUri,
					["code"] = code
				};

				shortLivedRequest.Content = new FormUrlEncodedContent(formData);

				var shortLivedResponse = await client.SendAsync(shortLivedRequest);
				var shortLivedJson = await shortLivedResponse.Content.ReadAsStringAsync();
				var shortLivedToken = JsonConvert.DeserializeObject<InstagramShortLivedToken>(shortLivedJson);

				// Логируем получение кратковременного токена
				_logger.LogInformation("Short-lived token obtained. User ID: {UserId}",
					shortLivedToken.UserId);

				// ШАГ 2: Обмен КРАТКОВРЕМЕННОГО на ДОЛГОВРЕМЕННЫЙ токен (60 дней)
				// ВАЖНО: Используем graph.instagram.com, НЕ graph.facebook.com!
				var longLivedUrl = $"https://graph.instagram.com/access_token" +
					$"?grant_type=ig_exchange_token" +
					$"&client_secret={Uri.EscapeDataString(AppSecret)}" +
					$"&access_token={Uri.EscapeDataString(shortLivedToken.AccessToken)}";

				var longLivedResponse = await client.GetAsync(longLivedUrl);
				var longLivedJson = await longLivedResponse.Content.ReadAsStringAsync();
				var longLivedToken = JsonConvert.DeserializeObject<InstagramLongLivedToken>(longLivedJson);

				// ШАГ 3: Сохраняем токен с датой истечения
				var expiresAt = DateTime.UtcNow.AddSeconds(longLivedToken.ExpiresIn);

				_logger.LogInformation("Long-lived token obtained. Expires at: {ExpiresAt}", expiresAt);
				_logger.LogInformation($"Long-lived token ={longLivedToken.AccessToken}");

				return new InstagramTokenResponse
				{
					AccessToken = longLivedToken.AccessToken,
					UserId = shortLivedToken.UserId
					//ExpiresAt = expiresAt,
					//Permissions = shortLivedToken.Permissions?.Split(',').ToList()
				};
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error exchanging code for token");
				throw;
			}
		}

		private async Task EnrichTokenWithUserInfo(InstagramTokenResponse tokenResponse)
		{
			try
			{
				_logger.LogInformation($"Token: {tokenResponse.AccessToken}");

				using var httpClient = new HttpClient();

				var url = $"{INSTAGRAM_GRAPH_URL}/{tokenResponse.UserId}?" +
						 $"fields=id,username,account_type&" +
						 $"access_token={tokenResponse.AccessToken}";

				var response = await httpClient.GetAsync(url);

				if (response.IsSuccessStatusCode)
				{
					var content = await response.Content.ReadAsStringAsync();
					_logger.LogInformation($"User info: {content}");
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to get user info");
			}
		}

		// Проверка подписи вебхука
		public bool VerifyWebhookSignature(string payload, string signature, string appSecret)
		{
			if (string.IsNullOrEmpty(signature))
				return false;

			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
			var expectedSignature = "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLower();

			return signature.Equals(expectedSignature, StringComparison.OrdinalIgnoreCase);
		}
	}

	#region Models
	public class InstagramShortLivedToken
	{
		[JsonPropertyName("access_token")]
		public string AccessToken { get; set; }

		[JsonPropertyName("user_id")]
		public long UserId { get; set; }

		// Исправляем: permissions - это МАССИВ, а не строка!
		[JsonPropertyName("permissions")]
		public List<string> Permissions { get; set; }

		// Добавляем для отладки
		[System.Text.Json.Serialization.JsonIgnore]
		public string RawResponse { get; set; }
	}

	public class InstagramLongLivedToken
	{
		[JsonPropertyName("access_token")]
		public string AccessToken { get; set; }

		[JsonPropertyName("token_type")]
		public string TokenType { get; set; }

		[JsonPropertyName("expires_in")]
		public int ExpiresIn { get; set; }
	}
	// Models/InstagramAuthModels.cs
	public class InstagramAuthState
	{
		[JsonPropertyName("user_id")]
		public string UserId { get; set; }

		[JsonPropertyName("provider")]
		public string Provider { get; set; }

		[JsonPropertyName("token")]
		public string Token { get; set; }

		[JsonPropertyName("callback_url")]
		public string CallbackUrl { get; set; }

		[JsonPropertyName("messages_callback")]
		public string MessagesCallback { get; set; }
	}

	public class InstagramTokenResponse
	{
		[JsonPropertyName("access_token")]
		public string AccessToken { get; set; }

		[JsonPropertyName("user_id")]
		public long UserId { get; set; }

		[JsonPropertyName("expires_in")]
		public int ExpiresIn { get; set; }
	}


	#endregion
}
