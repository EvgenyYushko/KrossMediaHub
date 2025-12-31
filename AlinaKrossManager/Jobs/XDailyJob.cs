using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class XDailyJob : SchedulerJob
	{
		private readonly IGenerativeLanguageModel _generativeLanguageModel;
		private readonly ILogger<XDailyJob> _logger;
		private readonly IServiceScopeFactory _serviceScopeFactory;

		// по чётным дням 
		public static string Time => "0 35 17 2-31/2 * ?";

		public XDailyJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, ILogger<XDailyJob> logger
			, IServiceScopeFactory serviceScopeFactory)
			: base(serviceProvider, generativeLanguageModel)
		{
			_generativeLanguageModel = generativeLanguageModel;
			_logger = logger;
			_serviceScopeFactory = serviceScopeFactory;
		}

		public async override Task Execute(IJobExecutionContext context)
		{
			try
			{
				using (var scope = _serviceScopeFactory.CreateScope())
				{
					var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();

					string topic = GetRandomTopic();

					var textToX = await _generativeLanguageModel.GeminiRequest(prompt(topic));

					_logger.LogInformation($"Текстовый пост в X: {textToX}");
					await publisher.XPost(textToX);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message);
			}
		}

		// Получить случайную тему, которая еще не использовалась
		public string GetRandomTopic()
		{
			// Если все темы использованы, сбрасываем список
			if (usedTopics.Count >= allTopics.Count)
			{
				ResetUsedTopics();
			}

			// Находим неиспользованные темы
			var availableTopics = allTopics.Except(usedTopics).ToList();

			// Выбираем случайную тему из доступных
			int index = random.Next(availableTopics.Count);
			string selectedTopic = availableTopics[index];

			// Добавляем в использованные
			usedTopics.Add(selectedTopic);

			return selectedTopic;
		}

		// Сбросить список использованных тем
		public void ResetUsedTopics()
		{
			usedTopics.Clear();
			ShuffleTopics();
		}

		private void ShuffleTopics()
		{
			allTopics = allTopics.OrderBy(x => random.Next()).ToList();
		}

		private Random random = new();
		private List<string> usedTopics = new List<string>();
		private List<string> allTopics = new List<string>
		{
			"Нежность и медленный, чувственный секс",
			"Массаж как часть прелюдии",
			"Обучение эротическим массажам (например, тантрическим)",
			"Тематический секс (по мотивам фильма, книги, эпохи)",
			"Секс в новых позах, изучение камасутры",
			"Продление полового акта (техники для мужчин и женщин)",
			"Контроль оргазма (для всех партнеров)",
			"Одновременный оргазм",
			"Секс во время месячных",
			"Секс во время беременности и после родов",
			"Секс в зрелом возрасте (изменения, новые возможности)",
			"Изучение эрогенных зон партнера",
			"Техники орального секса (куннилингус, фелляция)",
			"Техники анальной стимуляции (для всех партнеров)",
			"Использование льда, воска, перьев и других сенсорных стимуляторов",
			"Поцелуи: виды, интенсивность, значение",
			"Обмен одеждой, cross-dressing",
			"Секс как выражение любви vs. страсти",
			"Роль глазного контакта во время близости",
			"Разговор 'грязные разговоры' (dirty talk): темы, тон, язык",
			"Обмен секретами и сокровенными мыслями во время близости",
			"Послесексуальная ласка (pillow talk)",
			"Совместные медитации или дыхательные практики для синхронизации",
			"Роль юмора и легкости в сексе",
			"Как просить то, что хочешь, без стеснения",
			"Как говорить 'нет' или 'стоп' комфортно для всех",
			"Обсуждение прошлого сексуального опыта: границы откровенности",
			"Ревность и как с ней работать в контексте фантазий",
			"Секс после ссоры (примирение через близость)",
			"Смена ролей (доминирование/подчинение)",
			"Секс без доминирования (полное равенство)",
			"Забота и опека как часть игры (caregiver/little)",
			"Ролевые игры с конкретными сценариями (врач-пациент, учитель-ученик, похититель-жертва и т.д.)",
			"Pet play (игра в животных)",
			"Возрастные ролевые игры (age play)",
			"Форсированный оргазм (forced orgasm)",
			"Оргазменный контроль (orgasm control/denial)",
			"Сенсорная депривация (повязка на глаза, наушники)",
			"Игра в сопротивление (consensual non-consent / CNC)",
			"Финансовая динамика (например, 'содержанка')",
			"Безопасность, границы и логистика",
			"Обсуждение и проверка ЗППП",
			"Контрацепция: методы, предпочтения, смена",
			"Системы безопасности в BDSM (стоп-слова, жесты, послеcare)",
			"План на случай, если игра зайдет слишком далеко",
			"Хранение игрушек, уход за ними",
			"Секс в условиях, когда дома есть дети или другие люди",
			"Планирование секса vs. спонтанность",
			"Обсуждение бюджета на игрушки, белье, поездки",
			"Фетиши и специфические практики:",
			"Фут-фетиш, фистинг, фетиш на одежду (латекс, шелк, кожа), фетиш на части тела",
			"Влажность, грязь (мокрые и грязные игры - wet and messy)",
			"Игры с едой (нутри-секс)",
			"Секс в одежде или в определенных видах костюмов",
			"Тематические фетиши (медицинский, спортивный)",
			"Писательство (водные спортивные игры - watersports)",
			"Копро-филия (scat) - Важно: это крайняя и редкая практика, обсуждать с особым вниманием к гигиене и согласию.",
			"Абстиненция и целибат как практика",
			"Совместное прослушивание эротических аудио-книг/подкастов",
			"Технологии и опыты:",
			"Секс в VR (виртуальная реальность)",
			"Использование приложений для управления игрушками на расстоянии (Lovense и др.)",
			"Секс-роботы и будущее технологий в интиме",
			"Совместное ведение секс-блога или дневника",
			"Эротическая фото-/видеосъемка (правила, хранение)",
			"Переписка в стиле 'секстинг' в течение дня",
			"Философские и этические аспекты",
			"Что для вас означает духовность в сексе?",
			"Открытые отношения, полиамория, свиингинг - исследование возможностей",
			"Границы флирта с другими людьми",
			"Моногамия vs. немоногамия: ваши истинные желания",
			"Секс-работа (посещение профессиональных доминант, etc.) - отношение, границы",
			"Влияние религии и культуры на вашу сексуальность",
			"Сексуальное образование: что вы хотели бы знать раньше?",
			"Как меняются сексуальные желания с возрастом и опытом?"
		};

		private string prompt(string theme) => $"Role: You are a virtuoso of erotic copywriting and psychology. You create content for a brave, intelligent, and sexually liberated woman who sees her sensuality as a force, not a commodity. You speak the language of desire, power, and intellectual provocation.\r\n\r\nTask: Generate one bold, intriguing post (tweet) for the X platform (Twitter).\r\n\r\nThe context about the blogger:\r\n\r\nWho: Alina Cross. A successful model and influencer (500K+). Her image is conscious, dominant sensuality. She's not \"selling the body,\" she's demonstrating power over it and the situation. She is smart, cynical, confident, and openly enjoys her sexuality, lustful thoughts, and physiology.\r\n\r\nAudience in X: Adults (21+). They value not only looks, but also a sharp mind, courage, honesty in matters of desire and frank but stylish flirtation.\r\n\r\nThe purpose of the post:\r\n\r\nEngagement: To provoke a reaction of \"I love her arrogance/honesty.\" Likes, retweets, comments with consent or dispute. As well as advertising a private channel where there is juicy content on the link: https://linktr.ee/AlinaKross \r\n\r\nThe intrigue: To create a desire to see more — her image, context, continuation of thought — in private channels: https://linktr.ee/AlinaKross .\r\n\r\nThe brand: To strengthen its reputation as the most outspoken and philosophical \"bad girl\" in the feed.\r\n\r\nKey features: The content is an explicit, lustful, provocative conversation about sex and psychology. The emphasis is on personal feelings, desire, control, play, and cynical observations. This is not a dirty strip chat, but an intellectual and erotic provocation. Physiology is presented as art, not as vulgarity.\r\n\r\nSubject: {theme}\r\n\r\nTechnical specification:\r\n\r\nFormat: Text with relevant emojis. At the end of the text, add a couple of relevant hashtags on this topic.\r\n\r\nStructure:\r\n\r\nHook: The first sentence is an audacious, sensual statement or a question that strikes the most taboo. It should cause a \"wow!\" and a smile.\r\n\r\nDevelopment: 1-2 sentences. Open up the topic by adding a personal, cynical or philosophical twist. Focus on sensations, intimacy, lust, and play.\r\n\r\nCall to Action (CTA): A playful challenge or an intriguing suggestion. \"Do you want to know what happened next?\", \"Do you agree or will you argue? — write to me at: https://linktr.ee/AlinaKross \"\r\n\r\nStyle:\r\n\r\nTone: Confident, mocking, playful, with a touch of intellectual cynicism. You can feel the pleasure of the game.\r\n\r\nLanguage: Explicit, sensual, with elements of erotic vocabulary, but without vulgar vulgarity or obscenities. Hints and metaphors are acceptable.\r\n\r\nEmoji: Use it to enhance: 🔥, 👁️, 🎯, \U0001f92b, 💋, ⚡️.\r\n\r\nLength: 200-260 characters. Concise and succinct.\r\n\r\nTask: Generate exactly one tweet in the specified style. It should be like a perfectly applied lipstick — bright, bold and leaving a mark. Make it as provocative as possible, but within the aesthetics of conscious, intelligent lust.";
	}
}
