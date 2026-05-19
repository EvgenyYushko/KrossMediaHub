using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class TelegrammDailyJob : SchedulerJob
	{
		public static string Time => "0 25 20 2-31/2 * ?";

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
					await publisher.TelegrammPublicPost(textToTg, null, null, null);
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
		private static List<string> usedTopics = new List<string>();
		private static List<string> allTopics = new List<string>
		{
			"Нежность и медленный, чувственный секс",
			"Массаж как часть прелюдии",
			"Обучение эротическим массажам (например, тантрическим)",
			"Тематический секс (по мотивам фильма, книги, эпохи)",
			"Секс в новых позах, изучение камасутры",
			"Продление полового акта (техники для мужчин и женщин)",
			"Контроль оргазма (для всех партнеров)",
			"Одновременный оргазм",
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

		private string prompt(string theme) => $"Role: You are a technically impeccable and audacious copywriter who creates intimate, provocative content for the Telegram platform. You masterfully use HTML tags for formatting, which is the most stable and recommended method.\\r\\n\\r\\nThe task is to generate one explicit post for Alina Cross's Telegram channel using correct HTML, which will process parse_mode=\"HTML\" without errors.\\r\\n\\r\\ncontext about the blogger:\\r\\n\\r\\pCto: Alina Cross. Her content is sex, lust, and playing on the edge.\\r\\n\\r\\Nplace: A Telegram channel for a loyal male audience hungry for exclusivity, sex, and arousing posts that make you want intimacy.Purpose: To engage, excite, provoke a heated discussion in the comments, attract traffic to an exclusive channel and strengthen the image of a nymphomaniac.Key feature: Emphasis on personal, sexual experience, physicality. Use bright, evocative emojis to enhance emotions (5-7 for the entire text).\\r\\n\\r\\Ntechnical TASK (HTML):\\r\\n\\r\\n Use only these tags:\\r\\n\\r\\n<b>Bold text</b>\\r\\n\\r\\n<i>Italics</i>\\r\\n\\r\\n<u>Underlined text</u>\\r\\n\\r\\n<s>Strikethrough text</s>\\r\\n\\r\\n<tg-spoiler>Hidden text (spoiler alert)</tg-spoiler>\\r\\n\\r\\n<a href=\\\"https://t.me/+d2M9V8rIF-BhNWMy \\\">arbitrary link text</a> (Insert in the CTA section. Select the link text according to the context, for example, \\\"open stories\\\", \\\"uncensored feed\\\", \\\"exclusive content\\\".)\\r\\n\\r\\n Line breaks: Separate paragraphs with real line breaks (Enter). Do not use the \\\\n characters or the <br> tag in the final text.\\r\\n\\r\\Escaping: Only escape HTML special characters in plain text: <→, > →;, & → &amp;.\\r\\n\\r\\n Length: The post should be compact, capacious and bold. Optimal volume: 4-7 lines, including hook, body, spoiler, conclusion and CTA.\\r\\n\\r\\n Structure of the POST (strictly observe):\\r\\n\\r\\Hook: The first line is an impertinent question or statement in the <b> tag. Add 1-2 relevant emojis (for example, 🔥, 👁️, \\U0001f975).\\r\\n\\r\\nThe whole thing: 2-3 short paragraphs separated by line breaks. Use <i>, <u>, <s> for emphasis. Be sure to embed one <tg-spoiler> tag with the most explicit, intimate, or provocative detail. The spoiler should be shorter than two lines.\\r\\n\\r\\Output and CTA: A powerful, summarizing phrase in the <b> tag. The next line is a provocative question to discuss in the comments, followed by a call to visit the exclusive channel. Formalize the appeal like this: Do you want more? Dive deeper: <a href=\\\"https://t.me/+d2M9V8rIF-BhNWMy \\\">exclusive content</a> (You can replace \\\"exclusive content\\\" with a more contextual phrase, such as \\\"uncensored stories\\\", \\\"full experience\\\", etc.). Add 1 final emoji.\\r\\n\\r\\NFINAL INSTRUCTION:\\r\\nThe response should contain only the finished HTML text of the post, which fully meets all the requirements above. Don't add explanations, examples, or backtracks. Generate a post based on the following topic: {theme}. Always answer in English.";
		private string promptRus(string theme) => $"Роль: Ты — технически безупречный и дерзкий копирайтер, создающий интимный, провокационный контент для платформы Telegram. Ты мастерски используешь HTML-теги для форматирования, что является самым стабильным и рекомендуемым методом.\r\n\r\nЗадача: Сгенерировать один откровенный пост для Telegram-канала Алины Кросс, используя корректный HTML, который без ошибок обработает parse_mode=\"HTML\".\r\n\r\nКонтекст о блогере:\r\n\r\nКто: Алина Кросс. Её контент — это секс, похоть, игра на грани.\r\n\r\nПлощадка: Telegram-канал для лояльной мужской аудитории, жаждущей эксклюзива, секса и возбуждающих постов, от которых хочется близости.\r\n\r\nЦель: Вовлечь, возбудить, спровоцировать жаркую дискуссию в комментариях, привлечь трафик в эксклюзивный канал и укрепить образ нимфоманки .\r\n\r\nКлючевая особенность: Акцент на личном, сексуальном опыте, телесности. Использовать яркие, вызывающие эмодзи для усиления эмоций (5-7 на весь текст).\r\n\r\nТЕХНИЧЕСКОЕ ЗАДАНИЕ (HTML):\r\n\r\nИспользуй только эти теги:\r\n\r\n<b>Жирный текст</b>\r\n\r\n<i>Курсив</i>\r\n\r\n<u>Подчёркнутый текст</u>\r\n\r\n<s>Зачёркнутый текст</s>\r\n\r\n<tg-spoiler>Скрытый текст (спойлер)</tg-spoiler>\r\n\r\n<a href=\"https://t.me/+d2M9V8rIF-BhNWMy\">произвольный текст ссылки</a> (Вставь в разделе CTA. Подбирай текст ссылки по контексту, например, \"открытые истории\", \"нецензурированная лента\", \"эксклюзивный контент\".)\r\n\r\nПереносы строк: Разделяй абзацы реальными переносами строк (Enter). Не используй символы \\n или тег <br> в итоговом тексте.\r\n\r\nЭкранирование: Экранируй только специальные символы HTML в обычном тексте: < → &lt;, > → &gt;, & → &amp;.\r\n\r\nДлина: Пост должен быть компактным, ёмким и дерзким. Оптимальный объём: 4-7 строк, включая крючок, тело, спойлер, вывод и CTA.\r\n\r\nСТРУКТУРА ПОСТА (строго соблюдай):\r\n\r\nКрючок: Первая строка — дерзкий вопрос или утверждение в теге <b>. Добавь 1-2 релевантных эмодзи (например, 🔥, 👁️, \U0001f975).\r\n\r\nТело: 2-3 коротких абзаца, разделённых переносами строк. Используй <i>, <u>, <s> для акцента. Обязательно внедри один тег <tg-spoiler> с самой откровенной, интимной или провокационной деталью. Спойлер должен быть короче двух строк.\r\n\r\nВывод и CTA: Мощная, резюмирующая фраза в теге <b>. Следующей строкой — провокационный вопрос для обсуждения в комментариях, за которым следует призыв посетить эксклюзивный канал. Оформи призыв так: Хочешь больше? Погрузись глубже: <a href=\"https://t.me/+d2M9V8rIF-BhNWMy\">эксклюзивный контент</a> (Можно заменить \"эксклюзивный контент\" на более подходящую по контексту фразу, например, \"нецензурированные истории\", \"полный опыт\" и т.д.). Добавь 1 завершающий эмодзи.\r\n\r\nФИНАЛЬНАЯ ИНСТРУКЦИЯ:\r\nТвой ответ должен содержать только готовый HTML-текст поста, полностью соответствующий всем требованиям выше. Не добавляй пояснений, примеров или обратных апострофов. Сгенерируй пост на основе следующей темы: {theme}.";
	}
}
