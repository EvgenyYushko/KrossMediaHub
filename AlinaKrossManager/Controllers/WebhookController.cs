using System.Text.Json.Serialization;
using AlinaKrossManager.BuisinessLogic.Instagram;
using Microsoft.AspNetCore.Mvc;

namespace AlinaKrossManager.Controllers
{
	[ApiController]
	[Route("api/whatsapp")]
	public class WebhookController : ControllerBase
	{
		private readonly ConversationServiceWhatsApp _conversationService;
		private const string VerifyToken = "MY_SUPER_SECRET_TOKEN"; // Придумайте сами и впишите в поле "Подтверждение маркера"

		public WebhookController(IConfiguration configuration, ConversationServiceWhatsApp conversationService)
		{
			_conversationService = conversationService;
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

			if (!string.IsNullOrEmpty(messageText))
			{
				string targetPhone = senderPhone;

				if (!string.IsNullOrEmpty(targetPhone))
				{
					if (targetPhone.StartsWith("37529"))
					{
						// Превращаем 37529... в 3758029...
						targetPhone = targetPhone.Replace("37529", "3758029");
					}

					_conversationService.AddUserMessage(targetPhone, messageText);
				}
			}

			return Ok();
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
