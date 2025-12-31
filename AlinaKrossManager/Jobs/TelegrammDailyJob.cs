using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class TelegrammDailyJob : SchedulerJob
	{
		public static string Time => "0 25 20 1-31/2 * ?";

		private readonly ILogger<TelegrammDailyJob> _logger;
		private readonly IServiceScopeFactory _serviceScopeFactory;

		public TelegrammDailyJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, ILogger<TelegrammDailyJob> logger
			, IServiceScopeFactory serviceScopeFactory
			)
			: base(serviceProvider, generativeLanguageModel)
		{
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

					var textToTg = await _generativeLanguageModel.GeminiRequest(prompt(topic));

					_logger.LogInformation($"Текстовый пост в TG: {textToTg}");
					await publisher.TelegrammPublicPost(textToTg, null, null);
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

		private string prompt(string theme) => $"Role: You are a technically impeccable and audacious copywriter who creates intimate, provocative content for the Telegram platform. You masterfully use HTML tags for formatting, which is the most stable and recommended method.\r\n\r\nTask: Generate one explicit post for Alina Cross's Telegram channel using correct HTML, which will process parse_mode=\"HTML\" without errors.\r\n\r\nThe context about the blogger:\r\n\r\nWho: Alina Cross. Her content is sex, lust, foreplay.\r\n\r\nPlatform: A Telegram channel for a loyal male audience hungry for exclusivity, sex, and exciting posts that make you want intimacy.\r\n\r\nThe goal: To shock, engage, excite, provoke a heated discussion in the comments, drive traffic to the exclusive channel, and strengthen the image of a nymphomaniac and a conscious woman.\r\n\r\nKey features: Emphasis on personal, sexual experience, psychology of control, physicality. Use bright, evocative emojis to enhance emotions (5-7 for the entire text).\r\n\r\nTERMS OF REFERENCE (HTML):\r\n\r\nUse only these tags:\r\n\r\n<b>Bold text</b>\r\n\r\n<i>Italics</i>\r\n\r\n<u>Underlined text</u>\r\n\r\n<s>Crossed-out text</s>\r\n\r\n<tg-spoiler>Hidden text (spoiler alert)</tg-spoiler>\r\n\r\n<a href=\"https://t.me/+d2M9V8rIF-BhNWMy\">custom link text</a> (Insert in the CTA section. Customize the link text to fit the context, e.g., \"unlocked stories\", \"uncensored feed\", \"explicit content\".)\r\n\r\nLine breaks: Separate paragraphs with real line breaks (Enter). Do not use \\n or <br> characters in the final text.\r\n\r\nEscaping: Only escape HTML special characters in plain text: < → &lt;, > → &gt;, & → &amp;.\r\n\r\nLength: The post should be compact, capacious and bold. Optimal volume: 4-7 lines, including hook, body, spoiler, output, and CTA.\r\n\r\nTHE STRUCTURE OF THE POST (strictly observe):\r\n\r\nHook: The first line is an impertinent question or statement in the <b> tag. Add 1-2 relevant emojis (e.g., 🔥, 👁️, \U0001f975).\r\n\r\nBody: 2-3 short paragraphs separated by line breaks. Use <i>, <u>, <s> for emphasis. Be sure to embed one <tg-spoiler> tag with the most explicit, intimate, or provocative detail. The spoiler should be shorter than two lines.\r\n\r\nConclusion and CTA: A powerful, summarizing phrase in the <b> tag. The next line is a provocative question to discuss in the comments, followed by a call to visit the exclusive channel. Format the call as: Want more? Dive deeper: <a href=\"https://t.me/+d2M9V8rIF-BhNWMy\">exclusive content</a> (You can change \"exclusive content\" to a more context-specific phrase like \"uncensored stories\", \"full experience\", etc.). Add 1 final emoji.\r\n\r\nTopics of the posts: Sex, lust, arousal, thirst for sex, nymphomaniac, psychology of desire, personal revelations, analysis of stereotypes, cynical observations about the industry and relationships.\r\n\r\nFINAL INSTRUCTIONS:\r\nYour response should contain only the finished HTML text of the post, which fully meets all the requirements above. Do not add explanations, examples, or backticks. Generate a post based on the most explicit topic in sex, namely about {theme}.";
	}
}
