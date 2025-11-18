using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	public class InstagramMessageManagerJob : SchedulerJob
	{
		public static string Time =>  "0 5 * * * ?";

		private readonly ConversationService _conversationService;
		private readonly InstagramService _instagramService;

		public InstagramMessageManagerJob(IServiceProvider serviceProvider
			, InstagramService instagramService
			, ConversationService conversationService
			, IGenerativeLanguageModel generativeLanguageModel
			) 
		: base(serviceProvider, generativeLanguageModel)
		{
			_instagramService = instagramService;
			_conversationService = conversationService;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			try
			{
				var allUsers = _conversationService.GetAllUserConversations();

				Console.WriteLine("Count All Users: " + allUsers.Count);
				foreach (var userId in allUsers)
				{
					Console.WriteLine("UsersId: " + userId);

					var userHistory = _conversationService.GetHistory(userId);
					if (userHistory != null)
					{
						var lastMsg = userHistory.TakeLast(1).FirstOrDefault();
						Console.WriteLine($"Last msg Sender: {lastMsg.Sender}, Text: {lastMsg.Text}");

						if (lastMsg != null && lastMsg.Sender == "User")
						{
							await _instagramService.SendMessageWithHistory(userId, lastMsg.Text);
							//await Task.Delay(TimeSpan.FromSeconds(5));
						}
					}
				}

				//foreach (var userId in allUsers)
				//{
				//	await _instagramService.SendInstagramMessage(userId, "💋");
				//	//Console.WriteLine("начали генерацию фото");
				//	//InstagramMedia randomItem = GetRandomMedia(_mediaList);
				//	//Console.WriteLine("получили фото");
				//	//await SendInstagramPhotoFromUrl(senderId, randomItem.Media_Url);
				//	//Console.WriteLine("закончили фото");

				//	await Task.Delay(TimeSpan.FromSeconds(6));
				//}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
		}
	}
}
