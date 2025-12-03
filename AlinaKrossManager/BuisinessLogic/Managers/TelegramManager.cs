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
			_posts.Add(new BlogPost { Caption = "Первый пост: Привет мир! Как дела пидорасы! ААААА ААААААА", PhotoFileId = "dummy", CreatedAt = DateTime.Now.AddDays(-1) });
			_posts.Add(new BlogPost { Caption = "Второй пост: Обзор кода", PhotoFileId = "dummy", CreatedAt = DateTime.Now, VkStatus = SocialStatus.Published });
			// Добавим еще постов для теста пагинации
			for (int i = 3; i <= 12; i++)
				_posts.Add(new BlogPost { Caption = $"Пост #{i}: Тестовая запись Как дела Как дела", PhotoFileId = "dummy", CreatedAt = DateTime.Now.AddMinutes(i) });
		}

		private static List<BlogPost> _posts = new();
		private static ConcurrentDictionary<long, UserState> _userStates = new();

		public class BlogPost
		{
			public Guid Id { get; set; } = Guid.NewGuid();
			public string PhotoFileId { get; set; } // ID файла в Telegram
			public string Caption { get; set; }
			public DateTime CreatedAt { get; set; } = DateTime.Now;

			// Статусы для разных соцсетей
			public SocialStatus TelegramStatus { get; set; } = SocialStatus.Published;
			public SocialStatus VkStatus { get; set; } = SocialStatus.Pending;
			public SocialStatus InstaStatus { get; set; } = SocialStatus.Error;
		}

		public enum SocialStatus { Pending, Published, Error }
		public enum UserState { None, WaitingForPhoto }

		//static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
		//{
		//	try
		//	{
		//		// 1. Обработка нажатий кнопок (CallbackQuery)
		//		if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
		//		{
		//			await HandleCallbackQuery(bot, update.CallbackQuery, ct);
		//			return;
		//		}

		//		// 2. Обработка сообщений (Message)
		//		if (update.Type == UpdateType.Message && update.Message != null)
		//		{
		//			await HandleMessage(bot, update.Message, ct);
		//			return;
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

		//	// Проверяем состояние пользователя
		//	if (_userStates.TryGetValue(chatId, out var state) && state == UserState.WaitingForPhoto)
		//	{
		//		if (message.Photo != null)
		//		{
		//			// Пользователь прислал фото
		//			var photo = message.Photo.Last(); // Берем самое лучшее качество
		//			var caption = message.Caption ?? "Без описания";

		//			var newPost = new BlogPost
		//			{
		//				PhotoFileId = photo.FileId,
		//				Caption = caption,
		//				TelegramStatus = SocialStatus.Pending,
		//				VkStatus = SocialStatus.Pending,
		//				InstaStatus = SocialStatus.Pending
		//			};

		//			_posts.Add(newPost); // Добавляем в начало списка
		//			_userStates[chatId] = UserState.None; // Сбрасываем состояние

		//			await bot.SendMessage(chatId, "✅ Фото успешно добавлено в очередь!");
		//			await ShowMainMenu(bot, chatId, ct); // Возвращаем меню
		//		}
		//		else if (text == "/cancel")
		//		{
		//			_userStates[chatId] = UserState.None;
		//			await bot.SendMessage(chatId, "Отмена загрузки.");
		//			await ShowMainMenu(bot, chatId, ct);
		//		}
		//		else
		//		{
		//			await bot.SendMessage(chatId, "⚠️ Пожалуйста, пришлите фотографию (как картинку, не файл) или нажмите /cancel.");
		//		}
		//		return;
		//	}

		//	// Стандартная команда старт
		//	if (text == "/start")
		//	{
		//		await ShowMainMenu(bot, chatId, ct);
		//	}
		//}

		//static async Task HandleCallbackQuery(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
		//{
		//	var chatId = callback.Message!.Chat.Id;
		//	var messageId = callback.Message.MessageId;
		//	var data = callback.Data;

		//	// data format: "action:param"
		//	var parts = data!.Split(':');
		//	var action = parts[0];

		//	switch (action)
		//	{
		//		case "main_menu":
		//			// Если мы были в просмотре фото (сообщение с фото), мы не можем его редактировать в текст меню.
		//			// Поэтому проверяем: если текущее сообщение фото - удаляем и шлем новое. Если текст - редактируем.
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

		//		case "upload_start":
		//			_userStates[chatId] = UserState.WaitingForPhoto;
		//			await bot.EditMessageText(chatId, messageId,
		//				"📸 **Режим загрузки**\n\nПришлите фотографию (можно с описанием). Она автоматически попадет в очередь.\n\nДля отмены введите /cancel",
		//				parseMode: ParseMode.Markdown, cancellationToken: ct);
		//			break;

		//		case "queue_list":
		//			int page = parts.Length > 1 ? int.Parse(parts[1]) : 0;
		//			await ShowQueueList(bot, chatId, messageId, page, ct);
		//			break;

		//		case "post_view":
		//			Guid postId = Guid.Parse(parts[1]);
		//			await ShowPostDetails(bot, chatId, messageId, postId, ct);
		//			break;

		//		case "post_delete":
		//			// Логика удаления (упрощено)
		//			Guid idToDelete = Guid.Parse(parts[1]);
		//			var postToDelete = _posts.FirstOrDefault(p => p.Id == idToDelete);
		//			if (postToDelete != null) _posts.Remove(postToDelete);

		//			// Возвращаемся в список (удаляем фото, шлем список)
		//			await bot.DeleteMessage(chatId, messageId, ct);
		//			await ShowQueueList(bot, chatId, null, 0, ct); // null ID - значит отправить новое
		//			await bot.AnswerCallbackQuery(callback.Id, "Пост удален");
		//			break;
		//	}
		//}

		//// --- 4. МЕТОДЫ ОТРИСОВКИ UI ---

		//// Главное меню
		//static async Task ShowMainMenu(ITelegramBotClient bot, long chatId, CancellationToken ct, int? messageIdToEdit = null)
		//{
		//	var text = $"👋 **Панель управления SMM**\n\n" +
		//			   $"В очереди: **{_posts.Count}** постов.\n" +
		//			   $"Система работает исправно.";

		//	var keyboard = new InlineKeyboardMarkup(new[]
		//	{
		//	new [] { InlineKeyboardButton.WithCallbackData("📤 Загрузить новое фото", "upload_start") },
		//	new [] { InlineKeyboardButton.WithCallbackData("🗂 Просмотр очереди", "queue_list:0") },
		//});

		//	if (messageIdToEdit.HasValue)
		//	{
		//		await bot.EditMessageText(chatId, messageIdToEdit.Value, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//	}
		//	else
		//	{
		//		await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//	}
		//}

		//// Список очереди (Пагинация)
		//static async Task ShowQueueList(ITelegramBotClient bot, long chatId, int? messageIdToEdit, int page, CancellationToken ct)
		//{
		//	const int pageSize = 5;
		//	var totalPosts = _posts.Count;
		//	var totalPages = (int)Math.Ceiling((double)totalPosts / pageSize);

		//	// Берем посты для текущей страницы
		//	var pagePosts = _posts.Skip(page * pageSize).Take(pageSize).ToList();

		//	var text = $"🗂 **Очередь публикаций**\nСтраница {page + 1} из {Math.Max(1, totalPages)}";

		//	// Создаем список строк (каждая строка - это список кнопок)
		//	var rows = new List<IEnumerable<InlineKeyboardButton>>();

		//	// 1. Генерируем кнопки для постов (ВЕРТИКАЛЬНО, НА ВСЮ ШИРИНУ)
		//	foreach (var post in pagePosts)
		//	{
		//		string statusIcon = post.VkStatus == SocialStatus.Published ? "✅" : (post.VkStatus == SocialStatus.Error ? "❌" : "⏳");

		//		// Обрезаем текст, чтобы кнопка не была гигантской
		//		string shortCaption = string.IsNullOrWhiteSpace(post.Caption) ? "Без описания" : post.Caption;
		//		if (shortCaption.Length > 40) shortCaption = shortCaption.Substring(0, 40) + "...";

		//		// ВАЖНО: Мы создаем новый массив [] { button } для КАЖДОГО поста.
		//		// Это гарантирует, что кнопка займет всю строку (Full Width).
		//		rows.Add(new[]
		//		{
		//	InlineKeyboardButton.WithCallbackData($"{statusIcon} {shortCaption}", $"post_view:{post.Id}")
		//});
		//	}

		//	// 2. Кнопки навигации (ГОРИЗОНТАЛЬНО, В ОДНУ СТРОКУ)
		//	var navButtons = new List<InlineKeyboardButton>();

		//	if (page > 0)
		//		navButtons.Add(InlineKeyboardButton.WithCallbackData("« Назад", $"queue_list:{page - 1}"));

		//	navButtons.Add(InlineKeyboardButton.WithCallbackData("🏠 Домой", "main_menu"));

		//	if (page < totalPages - 1)
		//		navButtons.Add(InlineKeyboardButton.WithCallbackData("Вперед »", $"queue_list:{page + 1}"));

		//	// Добавляем строку навигации в общий список строк
		//	if (navButtons.Any())
		//	{
		//		rows.Add(navButtons);
		//	}

		//	// Собираем клавиатуру
		//	var keyboard = new InlineKeyboardMarkup(rows);

		//	// Логика отправки/редактирования
		//	if (messageIdToEdit.HasValue)
		//	{
		//		try
		//		{
		//			await bot.EditMessageText(chatId, messageIdToEdit.Value, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//		}
		//		catch
		//		{
		//			await bot.DeleteMessage(chatId, messageIdToEdit.Value, cancellationToken: ct);
		//			await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//		}
		//	}
		//	else
		//	{
		//		await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//	}
		//}

		//// Детальный просмотр поста (ФОТО + Описание + Кнопки)
		//static async Task ShowPostDetails(ITelegramBotClient bot, long chatId, int messageIdToDelete, Guid postId, CancellationToken ct)
		//{
		//	var post = _posts.FirstOrDefault(p => p.Id == postId);
		//	if (post == null) return;

		//	// 1. Формируем красивый текст статусов
		//	var statusText =
		//		$"📄 **Детали поста**\n\n" +
		//		$"📝 **Текст:** {post.Caption}\n" +
		//		$"📅 **Дата:** {post.CreatedAt:dd.MM.yyyy HH:mm}\n\n" +
		//		$"📊 **Статусы:**\n" +
		//		$"{(post.TelegramStatus == SocialStatus.Published ? "✅" : "⏳")} Telegram\n" +
		//		$"{(post.VkStatus == SocialStatus.Published ? "✅" : "⏳")} ВКонтакте\n" +
		//		$"{(post.InstaStatus == SocialStatus.Error ? "❌ Ошибка (Image Ratio)" : "⏳")} Instagram";

		//	// 2. Кнопки управления
		//	var keyboard = new InlineKeyboardMarkup(new[]
		//	{
		//	new [] { InlineKeyboardButton.WithCallbackData("🗑 Удалить", $"post_delete:{post.Id}") },
		//	new [] { InlineKeyboardButton.WithCallbackData("🔙 Назад к списку", "queue_list:0") } // Возврат на 1ю страницу
  //      });

		//	// 3. UI Трюк: Мы не можем превратить Текстовое сообщение (Список) в Фото.
		//	// Поэтому мы удаляем старое сообщение-меню и шлем новое сообщение с фото.

		//	await bot.DeleteMessage(chatId, messageIdToDelete, ct);

		//	// Если у нас заглушка (нет реального FileId), шлем просто текст, иначе упадет
		//	if (post.PhotoFileId == "dummy")
		//	{
		//		await bot.SendMessage(chatId, "🖼 [Здесь должно быть фото, но это тестовая заглушка]\n\n" + statusText,
		//			parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		//	}
		//	else
		//	{
		//		await bot.SendPhoto(chatId, InputFile.FromFileId(post.PhotoFileId),
		//			caption: statusText,
		//			parseMode: ParseMode.Markdown,
		//			replyMarkup: keyboard,
		//			cancellationToken: ct);
		//	}
		//}

		//static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
		//{
		//	Console.WriteLine(exception.ToString());
		//	return Task.CompletedTask;
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
