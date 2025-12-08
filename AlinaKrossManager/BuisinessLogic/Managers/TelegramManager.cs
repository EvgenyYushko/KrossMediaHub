using System.Collections.Concurrent;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using AlinaKrossManager.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static AlinaKrossManager.Helpers.TelegramUserHelper;

namespace AlinaKrossManager.BuisinessLogic.Managers
{
	public class TelegramManager
	{
		private readonly InstagramService _instagramService;
		private readonly IGenerativeLanguageModel _generativeLanguageModel;
		private readonly BlueSkyService _blueSkyService;
		private readonly FaceBookService _faceBookService;
		private readonly TelegramService _telegramService;
		private readonly PublicTelegramChanel _publicTelegramChanel;
		private readonly PrivateTelegramChanel _privateTelegramChanel;
		private readonly ITelegramBotClient bot;

		public TelegramManager(InstagramService instagramService
			, IGenerativeLanguageModel generativeLanguageModel
			, BlueSkyService blueSkyService
			, FaceBookService faceBookService
			, TelegramService telegramService
			, PublicTelegramChanel publicTelegramChanel
			, PrivateTelegramChanel privateTelegramChanel
			, ITelegramBotClient bot
		)
		{
			_instagramService = instagramService;
			_generativeLanguageModel = generativeLanguageModel;
			_blueSkyService = blueSkyService;
			_faceBookService = faceBookService;
			_telegramService = telegramService;
			_publicTelegramChanel = publicTelegramChanel;
			_privateTelegramChanel = privateTelegramChanel;
			this.bot = bot;
			//InitCups();
		}

		//private void InitCups()
		//{
		//	// 1. Пост, где тексты одинаковые
		//	var p1 = new BlogPost
		//	{
		//		PhotoFileId = "dummy",
		//		CreatedAt = DateTime.Now.AddDays(-1),
		//		TelegramStatus = SocialStatus.Published,
		//		VkStatus = SocialStatus.Pending,
		//		InstaStatus = SocialStatus.Error,
		//		// Тексты
		//		TelegramCaption = "Привет мир (Общее)",
		//		VkCaption = "Привет мир (Общее)",
		//		InstaCaption = "Привет мир (Общее)"
		//	};
		//	_posts.Add(p1);

		//	// 2. Пост, где тексты РАЗНЫЕ (то, что вы просили)
		//	var p2 = new BlogPost
		//	{
		//		PhotoFileId = "dummy",
		//		CreatedAt = DateTime.Now,
		//		TelegramStatus = SocialStatus.Pending,
		//		VkStatus = SocialStatus.Pending,
		//		InstaStatus = SocialStatus.None, // В инсту не постим

		//		TelegramCaption = "Короткая новость для телеги с ссылкой [Click]",
		//		VkCaption = "Длиннющий лонгрид для ВКонтакте потому что там любят читать...",
		//		InstaCaption = "" // Тут пусто
		//	};
		//	_posts.Add(p2);
		//}

		//private static ConcurrentDictionary<long, UserSession> _sessions = new();
		//private static List<BlogPost> _posts = new();

		//public class UserSession
		//{
		//	public UserState State { get; set; } = UserState.None;
		//	public NetworkType SelectedNetwork { get; set; } = NetworkType.All;
		//	public Guid? EditingPostId { get; set; }
		//}

		//public class BlogPost
		//{
		//	public Guid Id { get; set; } = Guid.NewGuid();
		//	public string PhotoFileId { get; set; }
		//	public DateTime CreatedAt { get; set; } = DateTime.Now;

		//	// --- ТЕПЕРЬ ОПИСАНИЯ РАЗДЕЛЬНЫЕ ---
		//	public string TelegramCaption { get; set; }
		//	public string VkCaption { get; set; }
		//	public string InstaCaption { get; set; }

		//	public SocialStatus TelegramStatus { get; set; } = SocialStatus.None;
		//	public SocialStatus VkStatus { get; set; } = SocialStatus.None;
		//	public SocialStatus InstaStatus { get; set; } = SocialStatus.None;

		//	// Хелпер: Получить текст для конкретного контекста
		//	public string GetCaption(NetworkType type)
		//	{
		//		return type switch
		//		{
		//			NetworkType.Telegram => TelegramCaption,
		//			NetworkType.Vk => VkCaption,
		//			NetworkType.Instagram => InstaCaption,
		//			_ => TelegramCaption // По умолчанию (для режима All) берем телеграм или первый непустой
		//		};
		//	}

		//	// Хелпер: Установить текст
		//	public void SetCaption(NetworkType type, string text)
		//	{
		//		switch (type)
		//		{
		//			case NetworkType.Telegram: TelegramCaption = text; break;
		//			case NetworkType.Vk: VkCaption = text; break;
		//			case NetworkType.Instagram: InstaCaption = text; break;
		//			case NetworkType.All: // Если меняем в режиме All, меняем везде, где пост запланирован
		//				if (TelegramStatus != SocialStatus.None) TelegramCaption = text;
		//				if (VkStatus != SocialStatus.None) VkCaption = text;
		//				if (InstaStatus != SocialStatus.None) InstaCaption = text;
		//				break;
		//		}
		//	}

		//	public SocialStatus GetStatus(NetworkType type)
		//	{
		//		return type switch
		//		{
		//			NetworkType.Telegram => TelegramStatus,
		//			NetworkType.Vk => VkStatus,
		//			NetworkType.Instagram => InstaStatus,
		//			_ => SocialStatus.Pending
		//		};
		//	}
		//}


		//public enum SocialStatus { None, Pending, Published, Error } // None - значит не публикуем туда
		//public enum NetworkType { All, Telegram, Vk, Instagram }     // Типы сетей для фильтрации
		//public enum UserState { None, WaitingForPhoto, WaitingForEditCaption } // Добавили состояние редактирования

		//static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
		//{
		//	try
		//	{
		//		if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
		//		{
		//			await HandleCallbackQuery(bot, update.CallbackQuery, ct);
		//		}
		//		else if (update.Type == UpdateType.Message && update.Message != null)
		//		{
		//			await HandleMessage(bot, update.Message, ct);
		//		}
		//	}
		//	catch (Exception ex)
		//	{
		//		Console.WriteLine($"Error: {ex.Message}");
		//	}
		//}

		//static async Task HandleMessage(ITelegramBotClient bot, Message message, CancellationToken ct)
		//{
		//	var chatId = message.Chat.Id;
		//	var text = message.Text;
		//	var session = _sessions.GetOrAdd(chatId, new UserSession());

		//	// --- ЗАГРУЗКА ФОТО ---
		//	if (session.State == UserState.WaitingForPhoto)
		//	{
		//		if (message.Photo != null)
		//		{
		//			var photo = message.Photo.Last();
		//			var caption = message.Caption ?? ""; // Пустое, если нет

		//			var newPost = new BlogPost
		//			{
		//				PhotoFileId = photo.FileId,
		//				// Статусы
		//				TelegramStatus = (session.SelectedNetwork == NetworkType.All || session.SelectedNetwork == NetworkType.Telegram) ? SocialStatus.Pending : SocialStatus.None,
		//				VkStatus = (session.SelectedNetwork == NetworkType.All || session.SelectedNetwork == NetworkType.Vk) ? SocialStatus.Pending : SocialStatus.None,
		//				InstaStatus = (session.SelectedNetwork == NetworkType.All || session.SelectedNetwork == NetworkType.Instagram) ? SocialStatus.Pending : SocialStatus.None,

		//				// Тексты: Изначально заполняем одним и тем же текстом только нужные поля
		//				TelegramCaption = caption,
		//				VkCaption = caption,
		//				InstaCaption = caption
		//			};

		//			_posts.Add(newPost);
		//			session.State = UserState.None;

		//			await bot.SendMessage(chatId, $"✅ Фото добавлено! Описание применено для: {session.SelectedNetwork}");
		//			await ShowMainMenu(bot, chatId, ct);
		//		}
		//		else if (text == "/cancel") { /* ...стандартная отмена... */ await ShowMainMenu(bot, chatId, ct); session.State = UserState.None; }
		//		else { await bot.SendMessage(chatId, "⚠️ Жду фото"); }
		//		return;
		//	}

		//	// --- РЕДАКТИРОВАНИЕ ТЕКСТА ---
		//	if (session.State == UserState.WaitingForEditCaption)
		//	{
		//		if (!string.IsNullOrWhiteSpace(text))
		//		{
		//			var post = _posts.FirstOrDefault(p => p.Id == session.EditingPostId);
		//			if (post != null)
		//			{
		//				// ВАЖНО: Обновляем текст в зависимости от того, в какой очереди мы находимся
		//				post.SetCaption(session.SelectedNetwork, text);

		//				string target = session.SelectedNetwork == NetworkType.All ? "всех активных сетей" : session.SelectedNetwork.ToString();
		//				await bot.SendMessage(chatId, $"✅ Описание обновлено для {target}!");

		//				session.State = UserState.None;
		//				await ShowPostDetails(bot, chatId, null, post.Id, ct);
		//			}
		//		}
		//		else if (text == "/cancel") { /* ... */ }
		//		return;
		//	}

		//	if (text == "/start") await ShowMainMenu(bot, chatId, ct);
		//}

		//// --- 3. ОБРАБОТЧИК КНОПОК ---

		//static async Task HandleCallbackQuery(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
		//{
		//	var chatId = callback.Message!.Chat.Id;
		//	var messageId = callback.Message.MessageId;
		//	var data = callback.Data;
		//	var parts = data!.Split(':');
		//	var action = parts[0];

		//	var session = _sessions.GetOrAdd(chatId, new UserSession());

		//	switch (action)
		//	{
		//		case "main_menu":
		//			// Возврат из режима просмотра фото
		//			if (callback.Message.Type == MessageType.Photo)
		//			{
		//				await bot.DeleteMessage(chatId, messageId, ct);
		//				await ShowMainMenu(bot, chatId, ct);
		//			}
		//			else
		//			{
		//				await ShowMainMenu(bot, chatId, ct, messageId);
		//			}
		//			break;

		//		// --- МЕНЮ ВЫБОРА ЗАГРУЗКИ ---
		//		case "upload_menu":
		//			await ShowNetworkSelection(bot, chatId, messageId, "upload_start", "Куда будем загружать?", ct);
		//			break;

		//		case "upload_start":
		//			// user chose network type
		//			if (Enum.TryParse<NetworkType>(parts[1], out var netType))
		//			{
		//				session.SelectedNetwork = netType;
		//				session.State = UserState.WaitingForPhoto;

		//				string dest = netType == NetworkType.All ? "во ВСЕ сети" : $"в {netType}";

		//				await bot.EditMessageText(chatId, messageId,
		//					$"📸 **Загрузка {dest}**\n\nПришлите фотографию. Она попадет в очередь только для выбранных сетей.\n/cancel - отмена",
		//					parseMode: ParseMode.Markdown, cancellationToken: ct);
		//			}
		//			break;

		//		// --- МЕНЮ ВЫБОРА ОЧЕРЕДИ ---
		//		case "browse_menu":
		//			await ShowNetworkSelection(bot, chatId, messageId, "queue_list", "Какую очередь посмотреть?", ct);
		//			break;

		//		case "queue_list":
		//			// format: queue_list:NetworkType:Page
		//			var filterNet = parts.Length > 1 ? Enum.Parse<NetworkType>(parts[1]) : NetworkType.All;
		//			int page = parts.Length > 2 ? int.Parse(parts[2]) : 0;

		//			session.SelectedNetwork = filterNet;

		//			await ShowQueueList(bot, chatId, messageId, filterNet, page, ct);
		//			break;

		//		case "post_view":
		//			Guid postId = Guid.Parse(parts[1]);
		//			await ShowPostDetails(bot, chatId, messageId, postId, ct);
		//			break;

		//		case "post_edit_start":
		//			Guid editId = Guid.Parse(parts[1]);
		//			session.EditingPostId = editId;
		//			session.State = UserState.WaitingForEditCaption;

		//			// Удаляем фото (карточку), просим текст
		//			await bot.DeleteMessage(chatId, messageId, ct);
		//			await bot.SendMessage(chatId, "✏️ **Режим редактирования**\n\nПришлите новый текст описания для этого поста.\n/cancel - отмена", parseMode: ParseMode.Markdown);
		//			break;

		//		case "post_delete":
		//			Guid idDel = Guid.Parse(parts[1]);
		//			var pDel = _posts.FirstOrDefault(p => p.Id == idDel);
		//			if (pDel != null) _posts.Remove(pDel);

		//			await bot.DeleteMessage(chatId, messageId, ct);
		//			// Возвращаемся в общий список
		//			await ShowQueueList(bot, chatId, null, NetworkType.All, 0, ct);
		//			await bot.AnswerCallbackQuery(callback.Id, "Пост удален");
		//			break;
		//	}
		//}

		//// --- 4. МЕТОДЫ UI ---

		//static async Task ShowMainMenu(ITelegramBotClient bot, long chatId, CancellationToken ct, int? messageIdToEdit = null)
		//{
		//	var text = $"👋 **Панель управления SMM**\n\n" +
		//			   $"Всего постов в базе: **{_posts.Count}**\n" +
		//			   $"Выберите действие:";

		//	// В главном меню теперь ведем на подменю выбора сетей
		//	var keyboard = new InlineKeyboardMarkup(new[]
		//	{
		//		new [] { InlineKeyboardButton.WithCallbackData("📤 Загрузить фото...", "upload_menu") },
		//		new [] { InlineKeyboardButton.WithCallbackData("🗂 Просмотр очередей...", "browse_menu") },
		//	});

		//	if (messageIdToEdit.HasValue)
		//		try { await bot.EditMessageText(chatId, messageIdToEdit.Value, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct); }
		//		catch { /* ignore edit errors */ }
		//	else
		//		await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//}

		//// Вспомогательное меню для выбора соцсети (универсальное)
		//static async Task ShowNetworkSelection(ITelegramBotClient bot, long chatId, int messageId, string actionPrefix, string title, CancellationToken ct)
		//{
		//	// actionPrefix будет "upload_start" или "queue_list"
		//	// Кнопки: [ Все ] [ TG ] [ VK ] [ Insta ]

		//	// Формат callback для очереди отличается (нужна страница), учтем это
		//	string Suffix(NetworkType t) => actionPrefix == "queue_list" ? $"{t}:0" : $"{t}";

		//	var keyboard = new InlineKeyboardMarkup(new[]
		//	{
		//		new [] { InlineKeyboardButton.WithCallbackData("🌍 Во все сети / Все посты", $"{actionPrefix}:{Suffix(NetworkType.All)}") },
		//		new []
		//		{
		//			InlineKeyboardButton.WithCallbackData("✈️ Telegram", $"{actionPrefix}:{Suffix(NetworkType.Telegram)}"),
		//			InlineKeyboardButton.WithCallbackData("📘 VK", $"{actionPrefix}:{Suffix(NetworkType.Vk)}")
		//		},
		//		new []
		//		{
		//			InlineKeyboardButton.WithCallbackData("📷 Instagram", $"{actionPrefix}:{Suffix(NetworkType.Instagram)}"),
		//		},
		//		new [] { InlineKeyboardButton.WithCallbackData("🔙 Назад", "main_menu") }
		//	});

		//	await bot.EditMessageText(chatId, messageId, $"🤔 **{title}**\nВыберите целевую платформу:", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//}

		//static async Task ShowQueueList(ITelegramBotClient bot, long chatId, int? messageIdToEdit, NetworkType filterNet, int page, CancellationToken ct)
		//{
		//	const int pageSize = 5;

		//	// Фильтр: берем только те посты, которые существуют в выбранной сети
		//	var filteredPosts = _posts.Where(p => p.GetStatus(filterNet) != SocialStatus.None).ToList();

		//	var totalPosts = filteredPosts.Count;
		//	var totalPages = (int)Math.Ceiling((double)totalPosts / pageSize);
		//	if (page >= totalPages && totalPages > 0) page = totalPages - 1;

		//	var pagePosts = filteredPosts.Skip(page * pageSize).Take(pageSize).ToList();
		//	string netName = filterNet == NetworkType.All ? "Все сети" : filterNet.ToString();
		//	var text = $"🗂 **Очередь: {netName}**\nПостов: {totalPosts} | Стр. {page + 1}/{Math.Max(1, totalPages)}";

		//	var rows = new List<IEnumerable<InlineKeyboardButton>>();

		//	foreach (var post in pagePosts)
		//	{
		//		string displayIcon = "";
		//		string displayCaption = "";

		//		if (filterNet == NetworkType.All)
		//		{
		//			// РЕЖИМ ALL: Показываем, где пост запланирован
		//			// Например: [✈️📘] или [✈️]
		//			var icons = new List<string>();
		//			if (post.TelegramStatus != SocialStatus.None) icons.Add("✈️");
		//			if (post.VkStatus != SocialStatus.None) icons.Add("📘");
		//			if (post.InstaStatus != SocialStatus.None) icons.Add("📷");

		//			displayIcon = string.Join("", icons);
		//			if (string.IsNullOrEmpty(displayIcon)) displayIcon = "⛔"; // Странный случай

		//			// В общем режиме показываем "Основное" описание (например, телеграм)
		//			displayCaption = post.TelegramCaption ?? post.VkCaption ?? "Без описания";
		//		}
		//		else
		//		{
		//			// РЕЖИМ КОНКРЕТНОЙ СЕТИ: Показываем статус и описание ИМЕННО ЭТОЙ сети
		//			var s = post.GetStatus(filterNet);
		//			displayIcon = s == SocialStatus.Published ? "✅" : (s == SocialStatus.Error ? "❌" : "⏳");
		//			displayCaption = post.GetCaption(filterNet); // <-- Берем специфичное описание
		//		}

		//		// Обрезка текста
		//		if (string.IsNullOrWhiteSpace(displayCaption)) displayCaption = "Без текста";
		//		//if (displayCaption.Length > 25) displayCaption = displayCaption.Substring(0, 25) + "...";

		//		// Добавляем воздух
		//		//if (displayCaption.Length < 20) displayCaption += new string('⠀', 10);

		//		rows.Add(new[]
		//		{
		//			InlineKeyboardButton.WithCallbackData($"{displayIcon} {displayCaption}", $"post_view:{post.Id}")
		//		});
		//	}

		//	// --- НАВИГАЦИЯ (осталась прежней) ---
		//	var navButtons = new List<InlineKeyboardButton>();

		//	bool hasBack = page > 0;
		//	bool hasNext = page < totalPages - 1;

		//	if (hasBack) navButtons.Add(InlineKeyboardButton.WithCallbackData("«", $"queue_list:{filterNet}:{page - 1}"));
		//	navButtons.Add(InlineKeyboardButton.WithCallbackData("🏠 Меню", "main_menu")); // Сократил текст для красоты
		//	if (hasNext) navButtons.Add(InlineKeyboardButton.WithCallbackData("»", $"queue_list:{filterNet}:{page + 1}"));

		//	if (navButtons.Any()) rows.Add(navButtons);

		//	var keyboard = new InlineKeyboardMarkup(rows);
		//	if (messageIdToEdit.HasValue)
		//	{
		//		try
		//		{
		//			await bot.EditMessageText(chatId, messageIdToEdit.Value, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//		}
		//		catch 
		//		{ 
		//			await bot.DeleteMessage(chatId, messageIdToEdit.Value, ct);
		//			await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//		}
		//	}
		//	else await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//}

		//static async Task ShowPostDetails(ITelegramBotClient bot, long chatId, int? messageIdToDelete, Guid postId, CancellationToken ct)
		//{
		//	var session = _sessions.GetOrAdd(chatId, new UserSession());
		//	var post = _posts.FirstOrDefault(p => p.Id == postId);
		//	if (post == null) return;

		//	// Определяем, какой текст показывать
		//	string captionToShow;
		//	string modeTitle;

		//	if (session.SelectedNetwork == NetworkType.All)
		//	{
		//		modeTitle = "Обзор (Все сети)";
		//		// В режиме "Все" показываем сводку:
		//		captionToShow =
		//			$"✈️ **TG:** {post.TelegramCaption}\n" +
		//			$"--- \n" +
		//			$"📘 **VK:** {post.VkCaption}\n" +
		//			$"--- \n" +
		//			$"📷 **Insta:** {post.InstaCaption}";
		//	}
		//	else
		//	{
		//		modeTitle = $"Детали ({session.SelectedNetwork})";
		//		// В режиме конкретной сети показываем ТОЛЬКО её текст
		//		captionToShow = post.GetCaption(session.SelectedNetwork);
		//	}

		//	// Статусы текстом
		//	string StatusStr(SocialStatus s) => s switch
		//	{
		//		SocialStatus.Published => "✅",
		//		SocialStatus.Pending => "⏳",
		//		SocialStatus.Error => "❌",
		//		SocialStatus.None => "⛔",
		//		_ => ""
		//	};

		//	var infoText =
		//		$"📄 **{modeTitle}**\n\n" +
		//		$"📝 **Описание:**\n{captionToShow}\n\n" +
		//		$"📊 **Статусы:**\n" +
		//		$"TG: {StatusStr(post.TelegramStatus)} | VK: {StatusStr(post.VkStatus)} | INST: {StatusStr(post.InstaStatus)}";

		//	// Кнопки
		//	var buttons = new List<IEnumerable<InlineKeyboardButton>>();

		//	// Кнопку редактирования показываем всегда, но логика будет разной
		//	string editLabel = session.SelectedNetwork == NetworkType.All ? "✏️ Ред. все описания" : "✏️ Ред. описание";

		//	buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(editLabel, $"post_edit_start:{post.Id}") });
		//	buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🗑 Удалить пост", $"post_delete:{post.Id}") });

		//	// Кнопка назад возвращает в тот список, откуда пришли (фильтр сохраняется в сессии/коллбеке)
		//	buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 Назад к списку", $"queue_list:{session.SelectedNetwork}:0") });

		//	var keyboard = new InlineKeyboardMarkup(buttons);

		//	if (messageIdToDelete.HasValue) await bot.DeleteMessage(chatId, messageIdToDelete.Value, ct);

		//	if (post.PhotoFileId == "dummy")
		//		await bot.SendMessage(chatId, "🖼 [ФОТО]\n\n" + infoText, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//	else
		//		await bot.SendPhoto(chatId, InputFile.FromFileId(post.PhotoFileId), caption: infoText, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//}

		public async Task HandleUpdateAsync(Update update, CancellationToken ct)
		{
			//try
			//{
			//	// 1. Обработка нажатий кнопок (CallbackQuery)
			//	if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
			//	{
			//		await HandleCallbackQuery(bot, update.CallbackQuery, ct);
			//		return;
			//	}

			//	// 2. Обработка сообщений (Message)
			//	if (update.Type == UpdateType.Message && update.Message != null)
			//	{
			//		await HandleMessage(bot, update.Message, ct);
			//		return;
			//	}
			//}
			//catch (Exception ex)
			//{
			//	Console.WriteLine($"Error: {ex.Message}");
			//}

			//return;

			//await _telegramService.SendMainButtonMessage();

			if (update.Message?.Text is not { } text)
			{
				_telegramService.HandleMediaGroup(update.Message);
				return;
			}

			//await _instagramService.SendInstagramAdminMessage($"Hello form google cloude console, now ");

			var msgText = update.Message.GetMsgText() ?? "";

			switch (update.Type)
			{
				case UpdateType.Message when msgText.IsCommand("generate_image") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;

						Message msgStart = null;
						try
						{
							msgStart = await _telegramService.SendMessage("Генерируем изображение...");
							await GenerateImageByText(update, ct);
						}
						finally
						{
							try
							{
								await _telegramService.DeleteMessage(update.Message.MessageId, ct);
								await _telegramService.DeleteMessage(msgStart.MessageId, ct);
							}
							catch { }
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_threads") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;

						// Ваши данные (должны быть уже настроены в Instagram Graph API)
						var httpClient = new HttpClient();
						try
						{

							var threadsClient = new ThreadsGraphApiClient("TH|1582164256111927|klvrRaZ9XpW0O8DUymSpfXSxESM", "1582164256111927");

							var threadsResult = await threadsClient.CreateThreadAsync("Только Threads пост! 📱");
							if (threadsResult.Success)
							{
								Console.WriteLine($"Threads пост создан: {threadsResult.Id}");
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine($"Ошибка: {ex.Message}");
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_insta") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool flowControl = await InstagramPostHandler(update, rmsg, ct);
						if (!flowControl)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("story_to_insta") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool flowControl = await InstagramStoryHandler(update, rmsg, ct);
						if (!flowControl)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_facebook") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool flowControl = await FaceBookHandler(update, rmsg, ct);
						if (!flowControl)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("story_to_facebook") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool flowControl = await FaceBookStoryHandler(update, rmsg, ct);
						if (!flowControl)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_bluesky") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool? flowControl = await BlueSkyHandler(update, rmsg, ct);
						if (flowControl == false)
						{
							break;
						}
						else if (flowControl == true)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_tg_free") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool? flowControl = await TgFreeHandler(update, rmsg, ct);
						if (flowControl == false)
						{
							break;
						}
						else if (flowControl == true)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_tg_private") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool? flowControl = await TgPrivateHandler(update, rmsg, ct, true);
						if (flowControl == false)
						{
							break;
						}
						else if (flowControl == true)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_all") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;

						bool flowControl1 = await InstagramPostHandler(update, rmsg, ct);
						bool flowControl2 = await InstagramStoryHandler(update, rmsg, ct);
						bool flowControl3 = await FaceBookHandler(update, rmsg, ct);
						bool flowControl4 = await FaceBookStoryHandler(update, rmsg, ct);
						bool? flowControl5 = await BlueSkyHandler(update, rmsg, ct);
						bool? flowControl6 = await TgFreeHandler(update, rmsg, ct, true);

						Console.WriteLine("Конце операции публикации во все сети");
					}
					break;
			}
		}

		private async Task<bool> InstagramPostHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем процесс публикации в instagram...");
			try
			{
				var replayText = rmsg.GetMsgText() ?? "";
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				if (!images.Existst)
				{
					return false;
				}

				var description = await GetDescription(rmsg, images, replayText, _instagramService);

				var result = await _instagramService.CreateMediaAsync(images.Images, description);
				if (result.Success)
				{
					var msgRes = $"✅ Post insta success!";
					Console.WriteLine(msgRes);
					try
					{
						await _telegramService.SendMessage(msgRes, rmsg.MessageId);
					}
					catch { }
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Ошибка в посте для инсты: {ex.Message}");
			}
			finally
			{
				try { await _telegramService.DeleteMessage(startMsg.MessageId, ct); } catch { }
			}

			return true;
		}

		public async Task<bool> InstagramStoryHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем выкладывать сториз в instagram...");
			try
			{
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				if (!images.Existst)
				{
					return false;
				}

				var storyId = await _instagramService.PublishStoryFromBase64(images.Images.FirstOrDefault());
				if (storyId is not null)
				{
					var msgRes = $"✅ Story insta success!";
					Console.WriteLine(msgRes);
					try
					{
						await _telegramService.SendMessage(msgRes, rmsg.MessageId);
					}
					catch { }
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Ошибка в публикации сториз для инсты: {ex.Message}");
			}
			finally
			{
				try { await _telegramService.DeleteMessage(startMsg.MessageId, ct); } catch { }
			}

			return true;
		}

		private async Task<bool> FaceBookHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем процесс публикации в facebook...");
			try
			{
				var replayText = rmsg.GetMsgText() ?? "";
				var resVideos = await _telegramService.TryGetVideoBase64FromTelegram(rmsg);
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				if (!images.Existst && string.IsNullOrEmpty(replayText) && resVideos.base64Video is null)
				{
					return false;
				}

				var description = await GetDescription(rmsg, images, replayText, _faceBookService);

				bool success = false;
				if (images.Existst)
				{
					success = await _faceBookService.PublishToPageAsync(description, images.Images);
				}
				else if (resVideos.base64Video is not null)
				{
					success = await _faceBookService.PublishReelAsync(description, resVideos.base64Video);
				}

				if (success)
				{
					var msgRes = $"✅ Post facebook success!";
					Console.WriteLine(msgRes);
					try
					{
						await _telegramService.SendMessage(msgRes, rmsg.MessageId);
					}
					catch { }
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка facebook: {ex.Message}");
			}
			finally
			{
				try { await _telegramService.DeleteMessage(startMsg.MessageId, ct); } catch { }
			}

			return true;
		}

		private async Task<bool> FaceBookStoryHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем выкладывать сториз в Facebook...");
			try
			{
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				if (!images.Existst)
				{
					return false;
				}

				var res = await _faceBookService.PublishStoryAsync(images.Images.FirstOrDefault());
				if (res)
				{
					var msgRes = $"✅ Story Facebook success";
					Console.WriteLine(msgRes);
					try
					{
						await _telegramService.SendMessage(msgRes, rmsg.MessageId);
					}
					catch { }
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Ошибка в публикации сториз для Facebook: {ex.Message}");
			}
			finally
			{
				try { await _telegramService.DeleteMessage(startMsg.MessageId, ct); } catch { }
			}

			return true;
		}

		private async Task<bool?> BlueSkyHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем процесс публикации в bluesky...");
			try
			{
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				var resVideos = await _telegramService.TryGetVideoBase64FromTelegram(rmsg);
				var replayText = rmsg.GetMsgText() ?? "";
				if (!images.Existst && string.IsNullOrWhiteSpace(replayText) && resVideos.base64Video is null)
				{
					return true;
				}

				var description = await GetDescription(rmsg, images, replayText, _blueSkyService);

				// 1. Первичный вход при запуске
				if (!_blueSkyService.BlueSkyLogin)
				{
					if (!await _blueSkyService.LoginAsync())
					{
						Console.WriteLine("Критическая ошибка bluesky: не удалось войти в аккаунт.");
						return true;
					}

					Console.WriteLine("Успешно удалось войти в аккаунт bluesky. ✅");
					_blueSkyService.BlueSkyLogin = true;
				}

				if (await _blueSkyService.UpdateSessionAsync())
				{
					// 3. Публикуем с новым токеном, который теперь хранится внутри service.AccessJwt
					List<ImageAttachment> attachments = null;
					if (images.Existst)
					{
						attachments = new();
						foreach (var image in images.Images)
						{
							attachments.Add(new ImageAttachment
							{
								Image = await _blueSkyService.UploadImageFromBase64Async(image, "image/png")
							});
						}
					}

					bool success = false;

					description = await _blueSkyService.TruncateTextToMaxLength(description);

					if (resVideos.base64Video is not null)
					{
						var videoBlob = await _blueSkyService.UploadVideoFromBase64Async(resVideos.base64Video, resVideos.mimeType);
						if (videoBlob == null)
						{
							Console.WriteLine("Ошибка bluesky: не удалось загрузить видео.");
							return true;
						}
						var ratio = new AspectRatio { Width = 9, Height = 16 };

						// 3. Постинг
						success = await _blueSkyService.CreatePostWithVideoAsync(description, videoBlob, ratio);
					}
					else if (attachments is not null)
					{
						success = await _blueSkyService.CreatePostWithImagesAsync(description, attachments);
					}
					else
					{
						success = await _blueSkyService.CreatePostAsync(description);
					}

					if (success)
					{
						var msgRes = $"✅ Post bluesky success!";
						Console.WriteLine(msgRes);
						try
						{
							await _telegramService.SendMessage(msgRes, rmsg.MessageId);
						}
						catch { }
					}
				}
				else
				{
					Console.WriteLine("bluesky Не удалось обновить токен. Попытка повторного входа...");
					// Можно попробовать LoginAsync еще раз, если Refresh Token истек.
					if (!await _blueSkyService.LoginAsync())
					{
						Console.WriteLine("bluesky Не удалось выполнить повторный вход. Завершение работы.");
						return false;
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка bluesky: {ex.Message}");
			}
			finally
			{
				try { await _telegramService.DeleteMessage(startMsg.MessageId, ct); } catch { }
			}

			return null;
		}

		private Task<bool> TgFreeHandler(Update update, Message rmsg, CancellationToken ct, bool force = false)
		{
			return TgHandler(update, rmsg, ct, PublicTelegramChanel.CHANEL_ID, _publicTelegramChanel, force);
		}

		private Task<bool> TgPrivateHandler(Update update, Message rmsg, CancellationToken ct, bool force = false)
		{
			return TgHandler(update, rmsg, ct, PrivateTelegramChanel.CHANEL_ID, _privateTelegramChanel, force);
		}

		public async Task<bool> TgHandler(Update update, Message rmsg, CancellationToken ct, long chanelId, SocialBaseService socialBaseService, bool force = false)
		{
			var serviceName = socialBaseService.ServiceName;
			var startMsg = await _telegramService.SendMessage($"Начинаем процесс публикации в {serviceName}...");
			try
			{
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				var resVideos = rmsg.Video;
				var replayText = rmsg.GetMsgText() ?? "";
				if (!images.Existst && string.IsNullOrWhiteSpace(replayText) && resVideos is null)
				{
					return true;
				}

				var description = await GetDescription(rmsg, images, replayText, socialBaseService, force);

				if (resVideos is not null)
				{
					await _telegramService.SendVideoAsync(chanelId, description, resVideos);
				}
				else if (images.Existst)
				{
					if (images.Images.Count == 1)
					{
						await _telegramService.SendSinglePhotoAsync(images.Images.First(), null, description, chanelId);
					}
					else
					{
						await _telegramService.SendPhotoAlbumAsync(images.Images, null, description, chanelId);
					}
				}
				else
				{
					await _telegramService.SendMessage(chanelId, description);
				}

				var msgRes = $"✅ Post {serviceName} success!";
				Console.WriteLine(msgRes);
				try
				{
					await _telegramService.SendMessage(msgRes, rmsg.MessageId);
				}
				catch { }
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка {serviceName}: {ex.Message}");
			}
			finally
			{
				try { await _telegramService.DeleteMessage(startMsg.MessageId, ct); } catch { }
			}

			return false;
		}

		private async Task<string> GetDescription(Message rmsg, TelegramService.ImagesTelegram images, string replayText, SocialBaseService socialBaseService, bool force = false)
		{
			string description = string.IsNullOrEmpty(replayText) ? images.Caption : replayText;

			if (force)
			{
				replayText = description = null;
			}

			if (string.IsNullOrEmpty(description) || force)
			{
				description = await socialBaseService.TryCreateDescription(replayText, images.Images);
				_telegramService.UpdateCaptionMediaGrup(rmsg, description);
			}

			return description;
		}

		public async Task GenerateImageByText(Update update, CancellationToken ct)
		{
			var imagesList = await _generativeLanguageModel.GeminiRequestGenerateImage(update.Message.ReplyToMessage.Text, 2);
			var chatId = update.Message.Chat.Id;
			var msgId = update.Message.ReplyToMessage.MessageId;
			string caption = "";
			switch (imagesList.Count)
			{
				case 0:
					await _telegramService.SendMessage("📭 Изображения не сгенерированы.\nВозможно запрос не прошёл цензуру.", msgId);
					break;
				case 1:
					await _telegramService.SendSinglePhotoAsync(imagesList[0], msgId, caption);
					break;
				default:
					await _telegramService.SendPhotoAlbumAsync(imagesList, msgId, caption);
					break;
			}
		}
	}
}
