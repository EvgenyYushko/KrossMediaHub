using AlinaKrossManager.BuisinessLogic.Services.Instagram; // Проверьте неймспейсы
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	public class CachProcessJob : SchedulerJob
	{
		private readonly InstagramService _instagramService;

		// Cron: Запускать каждые 2 минуты
		public static string Time => "0 2,3,4,6,7,8,9,11,12,13,14,16,17,18,19,21,22,23,24,26,27,28,29,31,32,33,34,36,37,38,39,41,42,43,44,46,47,48,49,51,52,53,54,56,57,58,59 * * * ?";

		public CachProcessJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, InstagramService instagramService
			)
			: base(serviceProvider, generativeLanguageModel)
		{
			_instagramService = instagramService;
		}

		public async override Task Execute(IJobExecutionContext context)
		{
			try
			{
				// 1. Получаем "снэпшот" всех данных из кэша
				// ToArray() нужен, чтобы не держать лок на коллекции во время перебора
				var allEntries = MediaMessageStorage.Storage.ToArray();

				if (allEntries.Length == 0) return;

				int processedCount = 0;
				int limitPerRun = 2; // Ограничение: обрабатываем не более 2 файлов за запуск джобы

				// 2. Бежим по сообщениям
				foreach (var kvp in allEntries)
				{
					// Если лимит исчерпан — выходим до следующего раза
					if (processedCount >= limitPerRun) break;

					string messageId = kvp.Key;
					var mediaList = kvp.Value;

					// 3. Бежим по вложениям внутри сообщения
					foreach (var media in mediaList)
					{
						if (processedCount >= limitPerRun) break;

						// САМОЕ ВАЖНОЕ: Пропускаем уже обработанные
						if (media.IsProcessed) continue;

						try
						{
							Console.WriteLine($"[Job] Фоновая обработка медиа ({media.MediaType}) для msg {messageId}...");

							// 4. Вызываем метод сервиса
							await _instagramService.ProcessAndCacheMediaAsync(media, messageId);

							processedCount++;
							Console.WriteLine($"[Job] Успешно обработано: {media.AiResult?.Substring(0, Math.Min(20, media.AiResult?.Length ?? 0))}...");
						}
						catch (Exception ex)
						{
							Console.WriteLine($"[Job] Ошибка обработки медиа: {ex.Message}");
							// Можно добавить счетчик попыток (retry count), чтобы не долбиться в битый файл вечно,
							// но для начала пойдет и так.
						}
					}
				}

				if (processedCount > 0)
				{
					Console.WriteLine($"[Job] Цикл завершен. Обработано файлов: {processedCount}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Job] Критическая ошибка в CachProcessJob: {ex.Message}");
			}
		}
	}
}