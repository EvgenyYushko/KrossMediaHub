using System.Text;
using System.Text.Json;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace AlinaKrossManager.Models
{
	// Класс для отправки сообщения
	public class SendMessageRequest : IRequest<Message>
	{
		public long ChatId { get; }
		public string Text { get; }

		public HttpMethod HttpMethod => HttpMethod.Post;
		public string MethodName => "sendMessage";
		public bool IsWebhookResponse { get; set; }

		public SendMessageRequest(long chatId, string text)
		{
			ChatId = chatId;
			Text = text;
		}

		public HttpContent? ToHttpContent()
		{
			var payload = new
			{
				chat_id = ChatId,
				text = Text
			};

			var json = JsonSerializer.Serialize(payload);
			return new StringContent(json, Encoding.UTF8, "application/json");
		}
	}
}
