using AlinaKrossManager.BuisinessLogic;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs.Messages
{
	[DisallowConcurrentExecution]
	public class WhatsAppAnswerMessageJob : SchedulerJob
	{
		public static string Time => "0 * * * * ?";

		private readonly WhatsAppService _whatsAppService;
		private readonly ConversationServiceWhatsApp _conversationService;

		public WhatsAppAnswerMessageJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, WhatsAppService whatsAppService
			, ConversationServiceWhatsApp conversationService
			)
			: base(serviceProvider, generativeLanguageModel)
		{
			_whatsAppService = whatsAppService;
			_conversationService = conversationService;
		}

		public async override Task Execute(IJobExecutionContext context)
		{
			var allUsers = _conversationService.GetAllUserConversations();

			foreach (var phoneNumber in allUsers)
			{
				Console.WriteLine("Processing PhoneNumber: " + phoneNumber);

				var userHistory = _conversationService.GetHistory(phoneNumber);
				if (userHistory != null)
				{
					var lastMsg = userHistory.TakeLast(1).FirstOrDefault();
					Console.WriteLine($"Last msg Sender: {lastMsg?.Sender}, Text: {lastMsg?.Text}");

					if (lastMsg != null && lastMsg.Sender == "User")
					{
						try
						{
							if (Random.Shared.Next(100) < 40)
							{
								try
								{
									var randomUnreadMsgId = _conversationService.GetLastUnreadUserMessageId(phoneNumber);
									if (randomUnreadMsgId != null)
									{
										await _whatsAppService.ReactToUnreadMessageAsync(phoneNumber, randomUnreadMsgId);
									}
								}
								catch (Exception ex)
								{
									Console.WriteLine(ex.Message);
								}
							}
							await _whatsAppService.SendDellayMessageWithHistory(phoneNumber, lastMsg.Id);
						}
						catch (Exception ex)
						{
							Console.WriteLine(ex.Message);
						}

						Console.WriteLine($"Sent message to {phoneNumber} and marked as processed");

						// Прерываем цикл после отправки одному пользователю
						break;
					}
					else
					{
						// Если последнее сообщение не от пользователя, тоже помечаем как обработанного
						Console.WriteLine($"PhoneNumber {phoneNumber} doesn't need response - marked as processed");
						continue;
					}
				}
			}
		}
	}
}
