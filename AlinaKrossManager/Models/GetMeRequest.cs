using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace AlinaKrossManager.Models
{
	public class GetMeRequest : IRequest<User>
	{
		public HttpMethod HttpMethod => HttpMethod.Get;
		public string MethodName => "getMe";
		public bool IsWebhookResponse { get; set; }

		public HttpContent? ToHttpContent() => null;
	}
}
