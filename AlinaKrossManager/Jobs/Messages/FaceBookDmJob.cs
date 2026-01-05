using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs.Messages
{
	[DisallowConcurrentExecution]
	public class FaceBookDmJob : IJob
	{
		private readonly FaceBookService _fbService;
		private readonly IGenerativeLanguageModel _aiModel;
		private readonly ILogger<FaceBookDmJob> _logger;

		// Запускаем каждые 5 минут
		public static string Time => "0 0 0 0 * ?";

		public FaceBookDmJob(FaceBookService fbService, IGenerativeLanguageModel aiModel, ILogger<FaceBookDmJob> logger)
		{
			_fbService = fbService;
			_aiModel = aiModel;
			_logger = logger;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			return;
			try
			{
				// 1. Получаем сообщения, на которые нужно ответить
				var incomingMessages = await _fbService.GetUnreadMessagesAsync();

				if (incomingMessages == null || !incomingMessages.Any()) return;

				foreach (var msg in incomingMessages)
				{
					_logger.LogInformation($"Входящее FB сообщение от {msg.from.name}: {msg.message}");

					// 2. Генерируем ответ (Gemini)
					string prompt = $"Role: You are Alina Kross. Reply to a Facebook message: \"{msg.message}\". " +
									"Be engaging, slightly dominant or mysterious. Keep it short. Max 1 sentence.";

					string replyText = await _aiModel.GeminiRequest(prompt);

					// 3. Отправляем
					// Важно: msg.from.id - это ID пользователя (Recipient ID)
					await _fbService.SendReplyAsync(msg.from.id, replyText);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"Ошибка в FaceBookDmJob: {ex.Message}");
			}
		}
	}
}
