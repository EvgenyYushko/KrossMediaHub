using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs.Messages
{
	[DisallowConcurrentExecution]
	public class InstagramAnswerMessageJob : SchedulerJob
	{
		public static string Time => "0 1,10,20,30,40,50 * * * ?";
		private const string _evgenyYushkoId = "1307933750574022";
		private readonly IWebHostEnvironment _env;
		private readonly ConversationService _conversationService;
		private readonly InstagramService _instagramService;

		public InstagramAnswerMessageJob(IServiceProvider serviceProvider
			, InstagramService instagramService
			, ConversationService conversationService
			, IGenerativeLanguageModel generativeLanguageModel
			, IWebHostEnvironment env
			)
		: base(serviceProvider, generativeLanguageModel)
		{
			_env = env;
			_instagramService = instagramService;
			_conversationService = conversationService;
		}

		private readonly HashSet<string> _processedUsers = new();
		private const string ProcessedUsersFile = "processed_users.txt";

		public override async Task Execute(IJobExecutionContext context)
		{
			try
			{
				//await _instagramService.ProcessNextUnreadMessageAsync();
				//// 1. Генерируем base64 (здесь симуляция)
				//string base64Audio = await _generativeLanguageModel.GeminiTextToSpeechEn("Hello, how are you");
				//var audioBytes = Convert.FromBase64String(base64Audio);

				//Console.WriteLine("WebRootPath: " + _env.WebRootPath); // <-- ДОБАВЬТЕ ЭТУ СТРОКУ
				//Console.WriteLine("ContentRootPath: " + _env.ContentRootPath); // <-- И ЭТУ, ДЛЯ ИНТЕРЕСА

				//// Получаем путь к wwwroot. Если WebRootPath null, строим путь вручную от корня приложения
				//string webRootPath = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");

				//// 1. Убедимся, что папка wwwroot существует (на случай, если её нет в контейнере)
				//if (!Directory.Exists(webRootPath))
				//{
				//	Directory.CreateDirectory(webRootPath);
				//}

				//Console.WriteLine("webRootPath = " + webRootPath);

				//// 2. Создаем подпапку temp_audio ds
				//var tempFolder = Path.Combine(webRootPath, "temp_audio");
				//if (!Directory.Exists(tempFolder))
				//{
				//	Directory.CreateDirectory(tempFolder);
				//}

				//// 3. Сохраняем файл
				//var fileName = $"{Guid.NewGuid()}.wav";
				//var filePath = Path.Combine(tempFolder, fileName);
				//await System.IO.File.WriteAllBytesAsync(filePath, audioBytes);

				//// 4. Публичная ссылка
				//var serverBaseUrl = "https://krossmediahub-783314764029.europe-west1.run.app";
				//var publicUrl = $"{serverBaseUrl}/temp_audio/{fileName}";

				//Console.WriteLine($"File saved: {filePath}");
				//Console.WriteLine($"Link: {publicUrl}");

				////"https://freetestdata.com/wp-content/uploads/2021/09/Free_Test_Data_500KB_WAV.wav"
				//await _instagramService.SendInstagramAudioFromUrl(_evgenyYushkoId, publicUrl);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}

			//try
			//{
			//	var allUsers = _conversationService.GetAllUserConversations();

			//	foreach (var userId in allUsers)
			//	{
			//		await _instagramService.SendInstagramMessage(userId, "💋");
			//		//Console.WriteLine("начали генерацию фото");
			//		//InstagramMedia randomItem = GetRandomUniqeMedia(_mediaList);
			//		//Console.WriteLine("получили фото");
			//		//await SendInstagramPhotoFromUrl(senderId, randomItem.Media_Url);
			//		//Console.WriteLine("закончили фото");

			//		await Task.Delay(TimeSpan.FromSeconds(6));
			//	}
			//}
			//catch (Exception ex)
			//{
			//	Console.WriteLine(ex.ToString());
			//}
			//try
			//{
			//	// Загружаем обработанных пользователей из файла
			//	LoadProcessedUsers();

			//	var allUsers = _conversationService.GetAllUserConversations();
			//	Console.WriteLine("start - ✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨");
			//	Console.WriteLine(" Count All Users: " + allUsers.Count);
			//	Console.WriteLine(" Count _processedUsers: " + _processedUsers.Count);

			//	bool allUsersProcessed = true;

			//	foreach (var userId in allUsers)
			//	{
			//		// Пропускаем уже обработанных пользователей
			//		if (_processedUsers.Contains(userId))
			//		{
			//			Console.WriteLine($"User {userId} already processed - skipping");
			//			continue;
			//		}

			//		allUsersProcessed = false;
			//		Console.WriteLine("Processing UserId: " + userId);

			//		var userHistory = _conversationService.GetHistory(userId);
			//		if (userHistory != null)
			//		{
			//			var lastMsg = userHistory.TakeLast(1).FirstOrDefault();
			//			Console.WriteLine($"Last msg Sender: {lastMsg?.Sender}, Text: {lastMsg?.Text}");

			//			if (lastMsg != null && lastMsg.Sender == "User")
			//			{
			//				try
			//				{
			//					await _instagramService.SendDellayMessageWithHistory(userId);
			//				}
			//				catch (Exception ex)
			//				{
			//					Console.WriteLine(ex.Message);
			//				}

			//				// Добавляем пользователя в обработанные
			//				_processedUsers.Add(userId);
			//				SaveProcessedUsers();

			//				Console.WriteLine($"Sent message to {userId} and marked as processed");

			//				// Прерываем цикл после отправки одному пользователю
			//				break;
			//			}
			//			else
			//			{
			//				// Если последнее сообщение не от пользователя, тоже помечаем как обработанного
			//				_processedUsers.Add(userId);
			//				SaveProcessedUsers();
			//				Console.WriteLine($"User {userId} doesn't need response - marked as processed");
			//				continue;
			//			}
			//		}
			//	}

			//	// Если все пользователи обработаны, очищаем список
			//	if (allUsersProcessed || _processedUsers.Count >= allUsers.Count)
			//	{
			//		Console.WriteLine("All users processed! Clearing processed users list...");
			//		_processedUsers.Clear();
			//		SaveProcessedUsers();
			//		Console.WriteLine("Ready to start new cycle!");
			//	}
			//	Console.WriteLine("end - ✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨✨");

				//foreach (var userId in allUsers)
				//{
				//	await _instagramService.SendInstagramMessage(userId, "💋");
				//	//Console.WriteLine("начали генерацию фото");
				//	//InstagramMedia randomItem = GetRandomUniqeMedia(_mediaList);
				//	//Console.WriteLine("получили фото");
				//	//await SendInstagramPhotoFromUrl(senderId, randomItem.Media_Url);
				//	//Console.WriteLine("закончили фото");

				//	await Task.Delay(TimeSpan.FromSeconds(6));
				//}
			//}
			//catch (Exception ex)
			//{
			//	Console.WriteLine(ex.ToString());
			//}
		}

		private void LoadProcessedUsers()
		{
			if (File.Exists(ProcessedUsersFile))
			{
				var lines = File.ReadAllLines(ProcessedUsersFile);
				_processedUsers.Clear();
				foreach (var line in lines)
				{
					if (!string.IsNullOrWhiteSpace(line))
						_processedUsers.Add(line.Trim());
				}
				Console.WriteLine($"Loaded {_processedUsers.Count} processed users from file");
			}
		}

		private void SaveProcessedUsers()
		{
			File.WriteAllLines(ProcessedUsersFile, _processedUsers);
		}
	}
}
