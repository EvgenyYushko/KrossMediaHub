using System.Text.Json;
using System.Text.Json.Serialization;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AlinaKrossManager.Controllers
{
	[ApiController]
	[Route("api/webhooks/facebook")]
	public class FacebookWebhookController : ControllerBase
	{
		private readonly ILogger<FacebookWebhookController> _logger;
		private readonly FaceBookService _fbService;
		private readonly IGenerativeLanguageModel _aiModel;

		// Этот токен ты придумываешь сам и вводишь в поле "Подтверждение маркера" на сайте FB
		private const string VerifyToken = "alina_kross_secret_verify_token_123";

		public FacebookWebhookController(
			ILogger<FacebookWebhookController> logger,
			FaceBookService fbService,
			IGenerativeLanguageModel aiModel)
		{
			_logger = logger;
			_fbService = fbService;
			_aiModel = aiModel;
		}

		// 1. ПОДТВЕРЖДЕНИЕ ВЕБХУКА (GET)
		// Facebook дергает этот метод, когда ты нажимаешь "Подтвердить и сохранить" в админке
		[HttpGet]
		public IActionResult VerifyWebhook(
			[FromQuery(Name = "hub.mode")] string mode,
			[FromQuery(Name = "hub.verify_token")] string token,
			[FromQuery(Name = "hub.challenge")] string challenge)
		{
			_logger.LogInformation("VerifyWebhook start");

			if (!string.IsNullOrEmpty(mode) && !string.IsNullOrEmpty(token))
			{
				if (mode == "subscribe" && token == VerifyToken)
				{
					_logger.LogInformation("WEBHOOK_VERIFIED");
					// Важно: нужно вернуть challenge просто как текст (не JSON!)
					return Ok(challenge);
				}
			}

			return Forbid(); // 403, если токен не совпал
		}

		// 2. ПРИЕМ СООБЩЕНИЙ (POST)
		// Сюда приходят реальные сообщения
		[HttpPost]
		public async Task<IActionResult> ReceiveEvent([FromBody] JsonElement body)
		{
			try
			{
				// так как пока приложение не прошло проверку то вебхуки приходят только от админа.
				// поэтому я сделал джобу которая опрашивает все сообщения и отвчает им
				return Ok();

				_logger.LogInformation("ReceiveEvent start");

				// Логируем сырой запрос для отладки
				string rawJson = body.ToString();
				// _logger.LogInformation($"FB Webhook Received: {rawJson}");

				var webhookEvent = JsonSerializer.Deserialize<FbWebhookPayload>(rawJson);

				if (webhookEvent?.Object == "page")
				{
					foreach (var entry in webhookEvent.Entry)
					{
						// Получаем только сообщения (messaging)
						if (entry.Messaging != null)
						{
							foreach (var msgEvent in entry.Messaging)
							{
								// Игнорируем эхо (наши собственные сообщения) и уведомления о доставке
								if (msgEvent.Message != null && !msgEvent.Message.IsEcho)
								{
									await HandleIncomingMessage(msgEvent.Sender.Id, msgEvent.Message.Text);
								}
							}
						}
					}
					return Ok("EVENT_RECEIVED");
				}

				return NotFound();
			}
			catch (Exception ex)
			{
				_logger.LogError($"Ошибка обработки вебхука FB: {ex.Message}");
				// Всегда возвращаем 200 OK, иначе FB отключит вебхук
				return Ok();
			}
		}

		private async Task HandleIncomingMessage(string senderId, string messageText)
		{
			if (string.IsNullOrEmpty(messageText)) return;

			_logger.LogInformation($"Входящее сообщение от {senderId}: {messageText}");

			// 1. Генерируем ответ через AI
			// (Важно: ответ должен быть быстрым, FB ждет 200 OK, поэтому лучше AI вызывать асинхронно
			// но для простоты делаем await, если AI отвечает < 5 сек)
			string prompt = $"Role: You are Alina Kross. Reply to this message: \"{messageText}\". Keep it flirty and short.";
			string replyText = await _aiModel.GeminiRequest(prompt);

			// 2. Отправляем ответ через ваш сервис
			await _fbService.SendReplyAsync(senderId, replyText);
		}
	}

	// --- DTO КЛАССЫ ДЛЯ ПАРСИНГА ВЕБХУКА ---

	public class FbWebhookPayload
	{
		[JsonPropertyName("object")]
		public string Object { get; set; }

		[JsonPropertyName("entry")]
		public List<FbEntry> Entry { get; set; }
	}

	public class FbEntry
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("time")]
		public long Time { get; set; }

		[JsonPropertyName("messaging")]
		public List<FbMessagingEvent> Messaging { get; set; }
	}

	public class FbMessagingEvent
	{
		[JsonPropertyName("sender")]
		public FbEntity Sender { get; set; }

		[JsonPropertyName("recipient")]
		public FbEntity Recipient { get; set; }

		[JsonPropertyName("timestamp")]
		public long Timestamp { get; set; }

		[JsonPropertyName("message")]
		public FbWebhookMessage Message { get; set; }
	}

	public class FbEntity
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }
	}

	public class FbWebhookMessage
	{
		[JsonPropertyName("mid")]
		public string Mid { get; set; }

		[JsonPropertyName("text")]
		public string Text { get; set; }

		// Поле is_echo присутствует, если сообщение отправлено самой страницей
		[JsonPropertyName("is_echo")]
		public bool IsEcho { get; set; }
	}
}
