using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class WishpersJob : SchedulerJob
	{
		public static string Time => "0 11 14 * * ?";

		private readonly IServiceScopeFactory _serviceScopeFactory;
		private readonly ILogger<WishpersJob> _logger;

		public WishpersJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, IServiceScopeFactory serviceScopeFactory
			, ILogger<WishpersJob> logger
			) : base(serviceProvider, generativeLanguageModel)
		{
			_serviceScopeFactory = serviceScopeFactory;
			_logger = logger;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			try
			{
				using (var scope = _serviceScopeFactory.CreateScope())
				{
					var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
					var aiFacade = scope.ServiceProvider.GetRequiredService<AiFacade>();

					var scene = "Придумай ОДИН короткий и интересный вариант речи/интонации для голосового сообщения модели Алины Кросс. \" +\r\n\"Верни ТОЛЬКО описание стиля речи, без лишних слов и кавычек. \" +\r\n\"Делай каждый раз разный и интересный вариант. \" +\r\n\"Примеры стилей: Нежная девушка, шепчет тихо и сексуально; Страстная и хрипловатая; Игривый томный голос; Возбуждённая и прерывистая; Сладкий стонующий шёпот; Горячая и соблазнительная; Тихий развратный голос; Дыхание с лёгкими стонами и т.д.\";";
					var sampleContext = "Придумай ОДНУ короткую название сцены для эротического голосового сообщения Алины Кросс. \" +\r\n\"Верни ТОЛЬКО название сцены, без каких-либо дополнительных объяснений, описаний и кавычек. \" +\r\n\"Название должно быть в стиле: 'Имитация секса по телефону', 'Лежит на диване', 'В горячей ванне' и т.д. \" +\r\n\"Каждый раз придумывай разную сцену. Делай вариации обстановки и позы.";
					var promt = "Придумай небольшое предложение (7-10 слов) для голосового сообщения на русском языке. " +
						"Это для приватного эротичного телеграм-канала модели Алины. " +
						"Фраза должна быть максимально возбуждающей, чтобы у участников сразу вставал член. " +
						"Можно использовать развратные слова, маты, стоны, хрипы и тяжёлое дыхание. " +
						"Добавляй интонации и вздохи в квадратных скобках [стонет], [дыхание], [шепчет], [громко стонет] и т.д. " +
						"Сделай максимально пошло, сексуально и развратно." +
						"Формат вывода, верни только один, самый подходящий вариант. Строго только готовый результат, без всякого рода пояснения и вступительных слов.";

					string sceneAi = "Нежная девушка, шепчет тихо и сексуально";
					try
					{
						sceneAi = await _generativeLanguageModel.GeminiRequest(scene);
						_logger.LogInformation($"sceneAi в TG: {sceneAi}");
					}
					catch (Exception ex)
					{
						_logger.LogError(ex.Message);
					}

					string sampleContextAi = "Имитация секса по телефону";
					try
					{
						sampleContextAi = await _generativeLanguageModel.GeminiRequest(sampleContext);
						_logger.LogInformation($"sampleContextAi в TG: {sampleContextAi}");
					}
					catch (Exception ex)
					{
						_logger.LogError(ex.Message);
					}

					var promtAi = await _generativeLanguageModel.GeminiRequest(promt);
					_logger.LogInformation($"Голосове в TG: {promtAi}");

					var sexySettings = new VoiceSettings
					{
						AudioProfile = "Alina Kross",
						VoiceName = "Aoede",
						Style = "The \"Vocal Smile\": The soft palate is raised to keep the tone bright, sunny, and explicitly inviting",
						Pace = "Natural",
						Accent = "Australian",
						Scene = sceneAi,
						SampleContext = sampleContextAi
					};

					using var stream = await aiFacade.GenerateVoiceAsync(promtAi, sexySettings);
					await publisher.TelegrammPublicPost(promtAi, null, null, stream);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message);
			}
		}
	}
}
