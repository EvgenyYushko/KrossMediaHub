using System.Text.Json;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using Microsoft.AspNetCore.Mvc;
using static AlinaKrossManager.BuisinessLogic.Services.Instagram.InstagramService;

namespace AlinaKrossManager.Controllers
{
	[ApiController]
	[Route("[controller]")]
	public class InstagramWebhookController : ControllerBase
	{
		private readonly ILogger<InstagramWebhookController> _logger;
		private readonly InstagramService _instagramService;
		private const string VerifyToken = "your_verify_token_here"; // Задайте свой токен

		public InstagramWebhookController(ILogger<InstagramWebhookController> logger, InstagramService instagramService)
		{
			_logger = logger;
			_instagramService = instagramService;
		}

		[HttpGet("webhook")]
		public IActionResult VerifyWebhook(
			[FromQuery(Name = "hub.mode")] string mode,
			[FromQuery(Name = "hub.verify_token")] string token,
			[FromQuery(Name = "hub.challenge")] string challenge)
		{
			_logger.LogInformation($"Webhook verification: mode={mode}, token={token}");

			// Проверяем токен верификации
			if (mode == "subscribe" && token == VerifyToken)
			{
				_logger.LogInformation("Webhook verified successfully");
				return Ok(challenge);
			}
			else
			{
				_logger.LogWarning("Webhook verification failed");
				return Forbid();
			}
		}

		[HttpPost("webhook")]
		public async Task<IActionResult> ReceiveWebhook()
		{
			try
			{
				using var reader = new StreamReader(Request.Body);
				var body = await reader.ReadToEndAsync();

				_logger.LogInformation($"Received Instagram webhook: {body}");

				var payload = JsonSerializer.Deserialize<InstagramWebhookPayload>(body);

				if (payload?.Entry != null)
				{
					foreach (var entry in payload.Entry)
					{
						// Обрабатываем сообщения
						if (entry.Messaging != null)
						{
							foreach (var messaging in entry.Messaging)
							{
								await _instagramService.ProcessMessage(messaging);
								return Ok();
							}
						}

						// Обрабатываем изменения (комментарии)
						if (entry.Changes != null)
						{
							foreach (var change in entry.Changes)
							{
								await _instagramService.ProcessChange(change);
							}
						}
					}
				}

				return Ok();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing Instagram webhook");
				return StatusCode(500);
			}
		}
	}
}
