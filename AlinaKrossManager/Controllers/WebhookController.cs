using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AlinaKrossManager.Controllers
{
	[ApiController]
	[Route("api/whatsapp")]
	public class WebhookController : ControllerBase
	{
		private readonly IConfiguration _configuration;
		private readonly IHttpClientFactory _httpClientFactory;

		// Данные возьмите из вашего Dashboard (Скриншот 1 и 2)
		// Лучше вынести их в appsettings.json
		private const string VerifyToken = "MY_SUPER_SECRET_TOKEN"; // Придумайте сами и впишите в поле "Подтверждение маркера"
		private const string AccessToken = "EAATFga3INnABQr098IsrGPZAK1yDgBJhXFqVPfvGn9diXZBZBXvxlIpZB2uXZA6OnZABd2TkN92Ba8aFZBtN83P2q1GnAmpaLDxSofoijYMFfT1QYNOQhOod3700MOryssGOHametgI3aZBgkZBpG0YDhD80FPirtTj08DchPHaXE4541m3L7vDybq0A6L1IwykGUuiVVZBFhjytpJcNnKAzxEJn5jovRqCzulTyUD4KnDbZBe6FfUCZCLwDeM8RXxDgLkf15uKHfFwbnuYgvL59vSfU"; // Ваш временный или постоянный токен доступа
		private const string PhoneNumberId = "966767783183438"; // ID номера телефона со скриншота

		public WebhookController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
		{
			_configuration = configuration;
			_httpClientFactory = httpClientFactory;
		}

		// 1. ПОДТВЕРЖДЕНИЕ WEBHOOK (GET)
		// Meta дергает этот url, когда вы нажимаете "Подтвердить и сохранить"
		[HttpGet]
		public IActionResult VerifyWebhook(
			[FromQuery(Name = "hub.mode")] string mode,
			[FromQuery(Name = "hub.challenge")] string challenge,
			[FromQuery(Name = "hub.verify_token")] string token)
		{
			// Проверяем, совпадает ли токен с тем, что вы ввели в настройках
			if (mode == "subscribe" && token == VerifyToken)
			{
				Console.WriteLine("Webhook verified successfully!");
				// Важно вернуть именно challenge как plain text, иначе проверка не пройдет
				return Ok(int.Parse(challenge));
			}

			return Unauthorized();
		}

		// 2. ПРИЕМ СООБЩЕНИЙ (POST)
		[HttpPost]
		public async Task<IActionResult> ReceiveMessage([FromBody] WhatsAppWebhookPayload payload)
		{
			// Проверка, есть ли данные
			if (payload?.Entry == null || !payload.Entry.Any())
				return NotFound();

			var change = payload.Entry.First().Changes?.First();
			if (change?.Value?.Messages == null || !change.Value.Messages.Any())
			{
				// Это может быть уведомление о статусе (sent, delivered), просто игнорируем
				return Ok();
			}

			var message = change.Value.Messages.First();
			var senderPhone = message.From; // Номер телефона отправителя
			var messageText = message.Text?.Body; // Текст сообщения

			Console.WriteLine($"Получено сообщение от {senderPhone}: {messageText}");

			string targetPhone = senderPhone;

			if (targetPhone.StartsWith("37529"))
			{
				// Превращаем 37529... в 3758029...
				targetPhone = targetPhone.Replace("37529", "3758029");
			}

			// --- ЛОГИКА АВТООТВЕТА ---
			if (!string.IsNullOrEmpty(targetPhone))
			{
				await SendReplyAsync(targetPhone, "Привет! Я получил твое сообщение: " + messageText);
			}

			return Ok();
		}

		// Метод отправки ответа через API
		private async Task SendReplyAsync(string toPhoneNumber, string message)
		{
			var url = $"https://graph.facebook.com/v22.0/{PhoneNumberId}/messages";

			var payload = new
			{
				messaging_product = "whatsapp",
				to = toPhoneNumber,
				type = "text",
				text = new { body = message }
			};

			var json = JsonSerializer.Serialize(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			var client = _httpClientFactory.CreateClient();
			client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AccessToken}");

			var response = await client.PostAsync(url, content);

			if (!response.IsSuccessStatusCode)
			{
				var error = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"Ошибка отправки: {error}");
			}
		}
	}

	// Корневой объект входящего Webhook
	public class WhatsAppWebhookPayload
	{
		[JsonPropertyName("object")]
		public string Object { get; set; }

		[JsonPropertyName("entry")]
		public List<Entry> Entry { get; set; }
	}

	public class Entry
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("changes")]
		public List<Change> Changes { get; set; }
	}

	public class Change
	{
		[JsonPropertyName("value")]
		public ChangeValue Value { get; set; }

		[JsonPropertyName("field")]
		public string Field { get; set; }
	}

	public class ChangeValue
	{
		[JsonPropertyName("messaging_product")]
		public string MessagingProduct { get; set; }

		[JsonPropertyName("metadata")]
		public Metadata Metadata { get; set; }

		[JsonPropertyName("contacts")]
		public List<Contact> Contacts { get; set; }

		[JsonPropertyName("messages")]
		public List<Message> Messages { get; set; }
	}

	public class Metadata
	{
		[JsonPropertyName("display_phone_number")]
		public string DisplayPhoneNumber { get; set; }

		[JsonPropertyName("phone_number_id")]
		public string PhoneNumberId { get; set; }
	}

	public class Contact
	{
		[JsonPropertyName("profile")]
		public Profile Profile { get; set; }

		[JsonPropertyName("wa_id")]
		public string WaId { get; set; }
	}

	public class Profile
	{
		[JsonPropertyName("name")]
		public string Name { get; set; }
	}

	public class Message
	{
		[JsonPropertyName("from")]
		public string From { get; set; }

		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("timestamp")]
		public string Timestamp { get; set; }

		[JsonPropertyName("type")]
		public string Type { get; set; }

		[JsonPropertyName("text")]
		public TextMessage Text { get; set; }
	}

	public class TextMessage
	{
		[JsonPropertyName("body")]
		public string Body { get; set; }
	}
}
