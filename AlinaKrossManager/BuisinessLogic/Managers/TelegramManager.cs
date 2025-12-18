using System.Collections.Concurrent;
using System.Text;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using AlinaKrossManager.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static AlinaKrossManager.BuisinessLogic.Services.TelegramService;
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
		private readonly PostService _postService;
		private readonly IServiceScopeFactory _scopeFactory;

		public TelegramManager(InstagramService instagramService
			, IGenerativeLanguageModel generativeLanguageModel
			, BlueSkyService blueSkyService
			, FaceBookService faceBookService
			, TelegramService telegramService
			, PublicTelegramChanel publicTelegramChanel
			, PrivateTelegramChanel privateTelegramChanel
			, ITelegramBotClient bot
			, PostService postService
			, IServiceScopeFactory scopeFactory
		)
		{
			_instagramService = instagramService;
			_generativeLanguageModel = generativeLanguageModel;
			_blueSkyService = blueSkyService;
			_faceBookService = faceBookService;
			_telegramService = telegramService;
			_publicTelegramChanel = publicTelegramChanel;
			_privateTelegramChanel = privateTelegramChanel;
			_postService = postService;
			_scopeFactory = scopeFactory;
			this.bot = bot;
		}


		// --- 1. НАСТРОЙКИ СЕТЕЙ (ЕДИНАЯ ТОЧКА КОНФИГУРАЦИИ) ---
		// Чтобы добавить соцсеть, добавьте её в Enum и сюда.
		public static class NetworkMetadata
		{
			public static readonly Dictionary<NetworkType, (string Name, string Icon)> Info = new()
			{
				{ NetworkType.Instagram, ("Instagram", "📸") },
				{ NetworkType.Facebook, ("Facebook",  "👥") } ,
				{ NetworkType.BlueSky,   ("BlueSky",   "💠") },
				{ NetworkType.TelegramPublic, ("Telegram Public",  "📱") },
				{ NetworkType.TelegramPrivate, ("Telegram Private",  "💋") },
			};

			// Список поддерживаемых сетей (исключая All)
			public static IEnumerable<NetworkType> Supported => Info.Keys;

			// Куда постить, если нажали "Во все Публичные"
			public static readonly List<NetworkType> PublicSet = new()
			{
				NetworkType.Instagram,
				NetworkType.Facebook,
				NetworkType.BlueSky,
				NetworkType.TelegramPublic,
			};

			// Куда постить, если нажали "Во все Приватные"
			public static readonly List<NetworkType> PrivateSet = new()
			{
				NetworkType.TelegramPrivate // Пока только телеграм
				// В будущем добавите сюда другие приватные каналы
			};
		}

		// Добавим Enum для фильтрации просмотра
		public enum AccessLevel { Public, Private }         // Свойство поста
		public enum AccessFilter { All, Public, Private }   // Фильтр для просмотра списка

		private static ConcurrentDictionary<long, UserSession> _sessions = new();

		public class UserSession
		{
			public UserState State { get; set; } = UserState.None;
			public NetworkType SelectedNetwork { get; set; } = NetworkType.All;
			public Guid? EditingPostId { get; set; }
			public List<int> ActiveAlbumMessageIds { get; set; } = new();
			// Хранит режим загрузки (какую кнопку нажал юзер: Публичную или Приватную)
			public AccessLevel UploadAccess { get; set; } = AccessLevel.Public;

			// Хранит последний выбранный фильтр в списке, чтобы кнопка "Назад" возвращала куда надо
			public AccessFilter LastFilter { get; set; } = AccessFilter.All;
		}

		// Для хранения промежуточных частей альбома
		private static ConcurrentDictionary<string, AlbumBuffer> _albumBuffers = new();

		private class AlbumBuffer
		{
			public List<string> FileIds { get; set; } = new();
			public string Caption { get; set; }
			public CancellationTokenSource TokenSource { get; set; } // Чтобы сбрасывать таймер
			public long ChatId { get; set; }
		}

		private string _tempCaption = null;

		private async Task<string> GenerateCaptionForNetworkAsync(NetworkType network, ImagesTelegram images)
		{
			switch (network)
			{
				case NetworkType.Instagram:
					{
						_tempCaption = await GetDescription(null, images, _tempCaption, _instagramService);
						return _tempCaption;
					}
				case NetworkType.Facebook:
					{
						_tempCaption = await GetDescription(null, images, _tempCaption, _faceBookService);
						return _tempCaption;
					}
				case NetworkType.BlueSky:
					{
						_tempCaption = await GetDescription(null, images, _tempCaption, _blueSkyService);
						return _tempCaption;
					}
				case NetworkType.TelegramPublic:
					{
						return await GetDescription(null, images, null, _publicTelegramChanel);
					}
				case NetworkType.TelegramPrivate:
					{
						return await GetDescription(null, images, null, _privateTelegramChanel);
					}
				default:
					return "Автоматическое описание"; // Заглушка, если генератора нет
			}
		}

		public class BlogPost
		{
			public Guid Id { get; set; } = Guid.NewGuid();
			public List<string> Images { get; set; } = new();
			public DateTime CreatedAt { get; set; } = DateTime.Now;

			public AccessLevel Access { get; set; } = AccessLevel.Public; // Пост публичный или приватный?

			// ВМЕСТО КУЧИ СВОЙСТВ - ОДИН СЛОВАРЬ
			// Хранит данные только для тех сетей, куда планируем постить
			public Dictionary<NetworkType, NetworkPostData> Networks { get; set; } = new();

			public BlogPost()
			{
				// Инициализируем словарь для всех известных сетей (по умолчанию Status = None)
				foreach (var net in NetworkMetadata.Supported)
				{
					Networks[net] = new NetworkPostData();
				}
			}

			// Хелпер: Получить текст
			public string GetCaption(NetworkType type)
			{
				if (type == NetworkType.All)
				{
					// Ищем первый непустой текст или возвращаем дефолтный
					return Networks.Values.FirstOrDefault(x => !string.IsNullOrEmpty(x.Caption))?.Caption ?? "";
				}
				return Networks.ContainsKey(type) ? Networks[type].Caption : "";
			}

			// Хелпер: Установить текст
			public void SetCaption(NetworkType type, string text)
			{
				if (type == NetworkType.All)
				{
					// Обновляем везде, где статус не None (то есть где пост активен)
					foreach (var net in Networks.Values.Where(n => n.Status != SocialStatus.None))
					{
						net.Caption = text;
					}
				}
				else if (Networks.ContainsKey(type))
				{
					Networks[type].Caption = text;
				}
			}

			// Хелпер: Получить статус
			public SocialStatus GetStatus(NetworkType type)
			{
				if (type == NetworkType.All) return SocialStatus.Pending; // Заглушка для All
				return Networks.ContainsKey(type) ? Networks[type].Status : SocialStatus.None;
			}

			// Хелпер: Активировать сеть (перевести в Pending)
			public void ActivateNetwork(NetworkType type, string initialCaption)
			{
				if (type == NetworkType.All)
				{
					foreach (var kvp in Networks)
					{
						kvp.Value.Status = SocialStatus.Pending;
						kvp.Value.Caption = initialCaption;
					}
				}
				else if (Networks.ContainsKey(type))
				{
					Networks[type].Status = SocialStatus.Pending;
					Networks[type].Caption = initialCaption;
				}
			}

			public void ActivateSet(List<NetworkType> networks, string caption)
			{
				foreach (var net in networks)
				{
					// Активируем только те, что есть в списке
					if (Networks.ContainsKey(net))
					{
						Networks[net].Status = SocialStatus.Pending;
						Networks[net].Caption = caption;
					}
				}
			}
		}


		public enum SocialStatus { None, Pending, Published, Error } // None - значит не публикуем туда
		public enum NetworkType { All, Instagram, Facebook, BlueSky, TelegramPublic, TelegramPrivate }     // Типы сетей для фильтрации
		public enum UserState { None, WaitingForPhoto, WaitingForEditCaption } // Добавили состояние редактирования

		public class NetworkPostData
		{
			public string Caption { get; set; } = "";
			public SocialStatus Status { get; set; } = SocialStatus.None;
		}

		private async Task HandleMessage(ITelegramBotClient bot, Message message, CancellationToken ct)
		{
			var chatId = message.Chat.Id;
			var text = message.Text;
			var session = _sessions.GetOrAdd(chatId, new UserSession());
			_tempCaption = null;

			// --- ЗАГРУЗКА ФОТО (С Поддержкой Альбомов) ---
			if (session.State == UserState.WaitingForPhoto)
			{
				if (message.Photo != null)
				{
					var photo = message.Photo.Last(); // Лучшее качество
					var caption = message.Caption; // Может быть null, если подпись не у первого фото

					// Сценарий 1: ЭТО АЛЬБОМ (есть GroupId)
					if (!string.IsNullOrEmpty(message.MediaGroupId))
					{
						var groupId = message.MediaGroupId;

						// Получаем или создаем буфер для этого альбома
						var buffer = _albumBuffers.GetOrAdd(groupId, new AlbumBuffer
						{
							ChatId = chatId,
							TokenSource = new CancellationTokenSource()
						});

						// Добавляем ID фото
						lock (buffer.FileIds)
						{
							buffer.FileIds.Add(photo.FileId);
							// Если у этого куска альбома есть описание, берем его (обычно оно у 1-го элемента)
							if (!string.IsNullOrEmpty(caption)) buffer.Caption = caption;
						}

						// СБРОС ТАЙМЕРА: Отменяем предыдущую задачу финализации
						buffer.TokenSource.Cancel();
						buffer.TokenSource = new CancellationTokenSource();

						// Запускаем новую задачу ожидания (например, 2 секунды)
						_ = Task.Run(async () =>
						{
							try
							{
								await Task.Delay(2000, buffer.TokenSource.Token);

								// Создаем НОВЫЙ Scope, так как старый уже давно умер
								using (var scope = _scopeFactory.CreateScope())
								{
									// Получаем НОВЫЙ экземпляр менеджера с ЖИВОЙ базой данных
									// Если мы тут, значит 2 секунды прошло и новых фото не было -> Финализируем
									var freshManager = scope.ServiceProvider.GetRequiredService<TelegramManager>();

									// Вызываем финализацию через свежий менеджер
									await freshManager.FinalizeAlbumAsync(bot, groupId, ct);
								}
							}
							catch (TaskCanceledException)
							{
								// Пришло новое фото, таймер сброшен, ничего не делаем
							}
							catch (Exception ex)
							{
								Console.WriteLine($"Ошибка в таймере альбома: {ex}");
							}
						}, buffer.TokenSource.Token);

						return; // Выходим, не отправляем пока ответ пользователю
					}

					// Сценарий 2: ОДИНОЧНОЕ ФОТО (нет GroupId)
					// Действуем как раньше, но сразу создаем пост
					var images = await _telegramService.TryGetImagesPromTelegram(null, message.Photo);
					var newPost = await CreatePostFromDataAsync(session, images, caption ?? "");
					await _postService.AddPostAsync(newPost);

					session.State = UserState.None;
					await bot.SendMessage(chatId, $"✅ Одиночное фото добавлено!");
					await ShowMainMenu(bot, chatId, ct);
				}
				else if (text == "/cancel")
				{
					session.State = UserState.None;
					await bot.SendMessage(chatId, "Отмена.");
					await ShowMainMenu(bot, chatId, ct);
				}
				else if (session.State == UserState.WaitingForPhoto) // Игнорируем текст если ждем фото
				{
					await bot.SendMessage(chatId, "⚠️ Пришлите фото (или альбом)!");
				}
				return;
			}

			// --- РЕДАКТИРОВАНИЕ ТЕКСТА ---
			if (session.State == UserState.WaitingForEditCaption)
			{
				if (!string.IsNullOrWhiteSpace(text))
				{
					var post = await _postService.GetPostByIdAsync(session.EditingPostId.Value);
					if (post != null)
					{
						// ВАЖНО: Обновляем текст в зависимости от того, в какой очереди мы находимся
						post.SetCaption(session.SelectedNetwork, text);

						await _postService.UpdatePostAsync(post);

						string target = session.SelectedNetwork == NetworkType.All ? "всех активных сетей" : session.SelectedNetwork.ToString();
						await bot.SendMessage(chatId, $"✅ Описание обновлено для {target}!");

						session.State = UserState.None;
						await ShowPostDetails(bot, chatId, null, post.Id, ct);
					}
				}
				else if (text == "/cancel") { /* ... */ }
				return;
			}

			if (text == "/start") await ShowMainMenu(bot, chatId, ct);
		}

		private async Task<BlogPost> CreatePostFromDataAsync(UserSession session, ImagesTelegram images, string manualCaption)
		{
			// 1. Определяем реальный уровень доступа (как и было)
			AccessLevel finalAccess;

			if (session.SelectedNetwork == NetworkType.All)
			{
				finalAccess = session.UploadAccess;
			}
			else
			{
				if (NetworkMetadata.PrivateSet.Contains(session.SelectedNetwork))
					finalAccess = AccessLevel.Private;
				else
					finalAccess = AccessLevel.Public;
			}

			// 2. Создаем заготовку поста
			var post = new BlogPost
			{
				Images = images.Images,
				Access = finalAccess
			};

			// 3. Определяем список сетей, в которые нужно постить
			List<NetworkType> networksToActivate = new();

			if (session.SelectedNetwork == NetworkType.All)
			{
				// Берем список из метаданных в зависимости от Public/Private
				var set = (finalAccess == AccessLevel.Private)
					? NetworkMetadata.PrivateSet
					: NetworkMetadata.PublicSet;

				networksToActivate.AddRange(set);
			}
			else
			{
				// Одиночная сеть
				networksToActivate.Add(session.SelectedNetwork);
			}

			// 4. Проходим по сетям и устанавливаем тексты
			bool hasManualCaption = !string.IsNullOrWhiteSpace(manualCaption);

			foreach (var net in networksToActivate)
			{
				// Пропускаем, если такой сети нет в словаре поста (защита)
				if (!post.Networks.ContainsKey(net)) continue;

				string finalCaptionForNetwork;

				if (hasManualCaption)
				{
					// Если юзер прислал текст — используем его везде
					finalCaptionForNetwork = manualCaption;
				}
				else
				{
					// Если текста нет — генерируем СВОЙ для каждой сети
					// (Можно отправить уведомление в чат "Генерирую для Instagram...", если долго)
					finalCaptionForNetwork = await GenerateCaptionForNetworkAsync(net, images);
				}

				// Активируем сеть
				post.Networks[net].Status = SocialStatus.Pending;
				post.Networks[net].Caption = finalCaptionForNetwork;
			}

			return post;
		}

		// Метод, который вызывается, когда альбом "собрался" целиком
		private async Task FinalizeAlbumAsync(ITelegramBotClient bot, string groupId, CancellationToken ct)
		{
			if (_albumBuffers.TryRemove(groupId, out var buffer))
			{
				var session = _sessions.GetOrAdd(buffer.ChatId, new UserSession());

				// Создаем пост из накопленных данных

				var images = await _telegramService.TryGetImagesPromTelegram(groupId, null);
				var newPost = await CreatePostFromDataAsync(session, images, buffer.Caption ?? "");
				await _postService.AddPostAsync(newPost);

				// Сбрасываем состояние
				session.State = UserState.None;

				await bot.SendMessage(buffer.ChatId, $"✅ Альбом из {newPost.Images.Count} фото добавлен!");
				await ShowMainMenu(bot, buffer.ChatId, ct);
			}
		}

		// --- 3. ОБРАБОТЧИК КНОПОК ---
		private async Task HandleCallbackQuery(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
		{
			var chatId = callback.Message!.Chat.Id;
			var messageId = callback.Message.MessageId;
			var data = callback.Data;
			var parts = data!.Split(':');
			var action = parts[0];

			var session = _sessions.GetOrAdd(chatId, new UserSession());

			// --- ВСПОМОГАТЕЛЬНАЯ ФУНКЦИЯ ДЛЯ УДАЛЕНИЯ АЛЬБОМА ---
			async Task CleanupAlbumAsync()
			{
				if (session.ActiveAlbumMessageIds.Any())
				{
					foreach (var id in session.ActiveAlbumMessageIds)
					{
						try { await bot.DeleteMessage(chatId, id, ct); } catch { /* игнорируем, если уже удалено */ }
					}
					session.ActiveAlbumMessageIds.Clear();
				}
			}

			switch (action)
			{
				case "main_menu":
					// Возврат из режима просмотра фото
					if (callback.Message.Type == MessageType.Photo)
					{
						await bot.DeleteMessage(chatId, messageId, ct);
						await ShowMainMenu(bot, chatId, ct);
					}
					else
					{
						await ShowMainMenu(bot, chatId, ct, messageId);
					}
					break;

				// --- МЕНЮ ВЫБОРА ЗАГРУЗКИ ---
				case "upload_menu":
					await ShowNetworkSelection(bot, chatId, messageId, "upload_start", "Куда будем загружать?", ct);
					break;

				case "upload_start":

					// Сценарий "Во все ПУБЛИЧНЫЕ"
					if (parts[1] == "AllPublic")
					{
						session.SelectedNetwork = NetworkType.All;
						session.UploadAccess = AccessLevel.Public; // <--- Ставим флаг
						session.State = UserState.WaitingForPhoto;

						await bot.EditMessageText(chatId, messageId,
							"📢 **Загрузка: ВСЕ ПУБЛИЧНЫЕ**\n(Telegram, BlueSky, Instagram)\n\nПришлите фото.", parseMode: ParseMode.Markdown, cancellationToken: ct);
					}

					// Сценарий "Во все ПРИВАТНЫЕ"
					else if (parts[1] == "AllPrivate")
					{
						session.SelectedNetwork = NetworkType.All;
						session.UploadAccess = AccessLevel.Private; // <--- Ставим флаг
						session.State = UserState.WaitingForPhoto;

						await bot.EditMessageText(chatId, messageId,
							"🔒 **Загрузка: ВСЕ ПРИВАТНЫЕ**\n(Только Telegram Private)\n\nПришлите фото.", parseMode: ParseMode.Markdown, cancellationToken: ct);
					}

					if (Enum.TryParse<NetworkType>(parts[1], out var netType))
					{
						session.SelectedNetwork = netType;
						session.UploadAccess = AccessLevel.Public; // По умолчанию одиночные - публичные
						session.State = UserState.WaitingForPhoto;

						string dest = netType == NetworkType.All ? "во ВСЕ сети" : $"в {netType}";

						await bot.EditMessageText(chatId, messageId,
							$"📸 **Загрузка {dest}**\n\nПришлите фотографию. Она попадет в очередь только для выбранных сетей.\n/cancel - отмена",
							parseMode: ParseMode.Markdown, cancellationToken: ct);
					}
					break;

				// --- МЕНЮ ВЫБОРА ОЧЕРЕДИ ---
				case "browse_menu":
					await ShowNetworkSelection(bot, chatId, messageId, "queue_list", "Какую очередь посмотреть?", ct);
					break;

				case "queue_list":
					var filterNet = parts.Length > 1 ? Enum.Parse<NetworkType>(parts[1]) : NetworkType.All;
					var accessFilter = parts.Length > 2 ? Enum.Parse<AccessFilter>(parts[2]) : AccessFilter.All;
					int page = parts.Length > 3 ? int.Parse(parts[3]) : 0;
					session.SelectedNetwork = filterNet;
					session.LastFilter = accessFilter;
					// Проверяем: это возврат из просмотра поста или просто листание страниц?
					// Если ActiveAlbumMessageIds не пуст, значит мы точно смотрели пост с фото.
					// Или если сообщение было с фото (для одиночных постов).
					bool isReturningFromPost = session.ActiveAlbumMessageIds.Any() || callback.Message.Type == MessageType.Photo;

					// Чистим фотки (если есть)
					await CleanupAlbumAsync();

					if (isReturningFromPost)
					{
						// Сценарий 1: Вернулись из поста (были фотки).
						// Нужно удалить старое меню (которое было под фотками) и прислать чистое новое.
						try { await bot.DeleteMessage(chatId, messageId, ct); } catch { }
						await ShowQueueList(bot, chatId, null, filterNet, accessFilter, page, ct);
					}
					else
					{
						// Сценарий 2: Просто листаем страницы списка.
						// Сообщение удалять НЕ НАДО, его можно просто отредактировать. Это плавнее.
						await ShowQueueList(bot, chatId, messageId, filterNet, accessFilter, page, ct);
					}
					break;

				case "post_view":
					// При входе в просмотр, если вдруг висел старый альбом (баг), почистим его
					await CleanupAlbumAsync();

					Guid postId = Guid.Parse(parts[1]);
					await ShowPostDetails(bot, chatId, messageId, postId, ct);
					break;

				case "post_edit_start":
					// При начале редактирования мы удаляем всё: и меню, и альбом
					await CleanupAlbumAsync(); // Чистим фото

					Guid editId = Guid.Parse(parts[1]);
					session.EditingPostId = editId;
					session.State = UserState.WaitingForEditCaption;

					// Удаляем фото (карточку), просим текст
					await bot.DeleteMessage(chatId, messageId, ct);
					await bot.SendMessage(chatId, "✏️ **Режим редактирования**\n\nПришлите новый текст описания для этого поста.\n/cancel - отмена", parseMode: ParseMode.Markdown);
					break;

				case "post_delete":
					// 1.Убираем фото из чата
					await CleanupAlbumAsync();

					Guid idDel = Guid.Parse(parts[1]);
					var postToDelete = await _postService.GetPostByIdAsync(idDel);

					if (postToDelete != null)
					{
						// СЦЕНАРИЙ А: Мы в режиме "Все сети" -> Удаляем пост полностью
						if (session.SelectedNetwork == NetworkType.All)
						{
							// Удаляем целиком
							await _postService.DeletePostAsync(postToDelete.Id);
							await bot.AnswerCallbackQuery(callback.Id, "Пост удален полностью.");
						}
						// СЦЕНАРИЙ Б: Мы в конкретной сети -> Ставим статус None только для нее
						else
						{
							// Ставим статус None (отменяем публикацию в эту сеть)
							if (postToDelete.Networks.ContainsKey(session.SelectedNetwork))
							{
								postToDelete.Networks[session.SelectedNetwork].Status = SocialStatus.None;
								postToDelete.Networks[session.SelectedNetwork].Caption = "";
							}

							// ПРОВЕРКА НА МУСОР:
							// Если пост теперь имеет статус None ВО ВСЕХ сетях, его нет смысла хранить, удаляем совсем.
							bool isActiveAnywhere = postToDelete.Networks.Values.Any(n => n.Status != SocialStatus.None);

							if (!isActiveAnywhere)
							{
								await _postService.DeletePostAsync(postToDelete.Id);
								await bot.AnswerCallbackQuery(callback.Id, "Пост удален (не осталось активных сетей).");
							}
							else
							{
								await _postService.UpdatePostAsync(postToDelete); // Просто обновляем
								string netName = NetworkMetadata.Info[session.SelectedNetwork].Name;
								await bot.AnswerCallbackQuery(callback.Id, $"Пост исключен из {netName}.");
							}
						}
					}

					// Удаляем меню с кнопками
					try { await bot.DeleteMessage(chatId, messageId, ct); } catch { }

					// Возвращаемся в список (текущий пост исчезнет из него, так как сработает фильтр по статусу)
					await ShowQueueList(bot, chatId, null, session.SelectedNetwork, session.LastFilter, 0, ct);
					break;
				case "post_retry":
					Guid retryId = Guid.Parse(parts[1]);
					var postToRetry = await _postService.GetPostByIdAsync(retryId);
					if (postToRetry != null)
					{
						int countRetried = 0;

						// ЛОГИКА: Меняем Error -> Pending

						if (session.SelectedNetwork == NetworkType.All)
						{
							// Проходимся по всем сетям этого поста
							foreach (var netData in postToRetry.Networks.Values)
							{
								if (netData.Status == SocialStatus.Error)
								{
									netData.Status = SocialStatus.Pending; // Сбрасываем в ожидание
									countRetried++;
								}
							}
						}
						else
						{
							// Только для конкретной сети
							if (postToRetry.Networks.TryGetValue(session.SelectedNetwork, out var netData))
							{
								if (netData.Status == SocialStatus.Error)
								{
									netData.Status = SocialStatus.Pending;
									countRetried++;
								}
							}
						}

						if (countRetried > 0)
						{
							await _postService.UpdatePostAsync(postToRetry); // Сохраняем
							await bot.AnswerCallbackQuery(callback.Id, $"✅ {countRetried} публикаций отправлено на повтор.");
							// Обновляем карточку поста, чтобы увидеть смену статуса и исчезновение кнопки
							await ShowPostDetails(bot, chatId, messageId, retryId, ct);
						}
						else
						{
							await bot.AnswerCallbackQuery(callback.Id, "⚠️ Нет ошибок для повторения.");
						}
					}
					break;
			}
		}

		// --- 4. МЕТОДЫ UI ---

		private async Task ShowMainMenu(ITelegramBotClient bot, long chatId, CancellationToken ct, int? messageIdToEdit = null)
		{
			var allCount = await _postService.GetTotalCountAsync(NetworkType.All, AccessFilter.All);
			var text = $"👋 **Панель управления SMM**\n\n" +
					   $"Всего постов в базе: **{allCount}**\n" +
					   $"Выберите действие:";

			// В главном меню теперь ведем на подменю выбора сетей
			var keyboard = new InlineKeyboardMarkup(new[]
			{
				new [] { InlineKeyboardButton.WithCallbackData("📤 Загрузить фото...", "upload_menu") },
				new [] { InlineKeyboardButton.WithCallbackData("🗂 Просмотр очередей...", "browse_menu") },
			});

			if (messageIdToEdit.HasValue)
				try { await bot.EditMessageText(chatId, messageIdToEdit.Value, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct); }
				catch { /* ignore edit errors */ }
			else
				await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		}

		// Вспомогательное меню для выбора соцсети (универсальное)
		static async Task ShowNetworkSelection(ITelegramBotClient bot, long chatId, int messageId, string actionPrefix, string title, CancellationToken ct)
		{
			var rows = new List<IEnumerable<InlineKeyboardButton>>();

			// --- СЦЕНАРИЙ 1: МЕНЮ ЗАГРУЗКИ ---
			if (actionPrefix == "upload_start")
			{
				// Вместо переключателя и одной кнопки "Все", делаем две конкретные
				rows.Add(new[]
				{
					InlineKeyboardButton.WithCallbackData("📢 Во все ПУБЛИЧНЫЕ", "upload_start:AllPublic")
				});
				rows.Add(new[]
				{
					InlineKeyboardButton.WithCallbackData("🔒 Во все ПРИВАТНЫЕ", "upload_start:AllPrivate")
				});

				// Разделитель
				rows.Add(new[] { InlineKeyboardButton.WithCallbackData("👇 Или выберите конкретную сеть 👇", "ignore") });
			}

			// --- СЦЕНАРИЙ 2: МЕНЮ ПРОСМОТРА ---
			else if (actionPrefix == "queue_list")
			{
				// Три кнопки фильтрации:
				// Формат: queue_list:{NetworkType}:{AccessFilter}:{Page}
				// NetworkType.All здесь означает "Любая сеть", а фильтр доступа уточняет какая база

				rows.Add(new[]
				{
					InlineKeyboardButton.WithCallbackData("♾️ Все посты", $"queue_list:All:{AccessFilter.All}:0")
				});

				rows.Add(new[]
				{
					InlineKeyboardButton.WithCallbackData("📢 Публичные", $"queue_list:All:{AccessFilter.Public}:0"),
					InlineKeyboardButton.WithCallbackData("🔒 Приватные", $"queue_list:All:{AccessFilter.Private}:0")
				});

				rows.Add(new[] { InlineKeyboardButton.WithCallbackData("👇 Фильтр по соцсети 👇", "ignore") });
			}

			// --- КНОПКИ КОНКРЕТНЫХ СЕТЕЙ (Общие для обоих меню) ---
			// Для загрузки мы считаем одиночные нажатия Публичными по умолчанию (можно усложнить, но пока так)
			// Для просмотра добавляем AccessFilter.All (показывать и то и то в этой сети)

			var currentButtons = new List<InlineKeyboardButton>();
			foreach (var net in NetworkMetadata.Supported)
			{
				var meta = NetworkMetadata.Info[net];

				string callback;
				if (actionPrefix == "upload_start")
					callback = $"{actionPrefix}:{net}"; // Одиночная загрузка
				else
					callback = $"{actionPrefix}:{net}:{AccessFilter.All}:0"; // Просмотр конкретной сети (всех типов)

				currentButtons.Add(InlineKeyboardButton.WithCallbackData($"{meta.Icon} {meta.Name}", callback));

				if (currentButtons.Count == 2)
				{
					rows.Add(currentButtons.ToList());
					currentButtons.Clear();
				}
			}
			if (currentButtons.Any()) rows.Add(currentButtons);

			rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 Назад", "main_menu") });

			var keyboard = new InlineKeyboardMarkup(rows);
			await bot.EditMessageText(chatId, messageId, $"🤔 **{title}**\nВыберите режим:", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		}

		private async Task ShowQueueList(ITelegramBotClient bot, long chatId, int? messageIdToEdit, NetworkType filterNet,
			 AccessFilter accessFilter, int page, CancellationToken ct)
		{
			const int pageSize = 5;

			// 1. БАЗОВАЯ ФИЛЬТРАЦИЯ (По наличию в сети)
			// Нам нужно общее количество для пагинации
			int totalPosts = await _postService.GetTotalCountAsync(filterNet, accessFilter);

			// И сами посты
			var pagePosts = await _postService.GetPostsAsync(filterNet, accessFilter, page, 5); // pageSize = 5

			var totalPages = (int)Math.Ceiling((double)totalPosts / pageSize);
			if (page >= totalPages && totalPages > 0) page = totalPages - 1;

			string filterName = accessFilter switch
			{
				AccessFilter.Public => "(Только Public)",
				AccessFilter.Private => "(Только Private)",
				_ => "(Все типы)"
			};
			var text = $"🗂 **Очередь: {filterNet} {filterName}**\nПостов: {totalPosts} | Стр. {page + 1} ...";

			var rows = new List<IEnumerable<InlineKeyboardButton>>();

			foreach (var post in pagePosts)
			{
				string displayIcon = "";
				string displayCaption = "";

				if (filterNet == NetworkType.All)
				{
					// --- ЛОГИКА СВОДНОГО СТАТУСА ---

					// 1. Получаем статусы всех активных сетей этого поста
					var activeStatuses = post.Networks.Values
						.Where(n => n.Status != SocialStatus.None)
						.Select(n => n.Status)
						.ToList();

					string summaryStatusIcon = "⚪"; // По умолчанию (если нет активных сетей)

					if (activeStatuses.Any())
					{
						bool allPublished = activeStatuses.All(s => s == SocialStatus.Published);
						bool allErrors = activeStatuses.All(s => s == SocialStatus.Error);
						bool hasError = activeStatuses.Any(s => s == SocialStatus.Error);

						if (allPublished)
						{
							summaryStatusIcon = "✅"; // Всё ок
						}
						else if (allErrors)
						{
							summaryStatusIcon = "❌"; // Всё упало
						}
						else if (hasError)
						{
							summaryStatusIcon = "⚠️"; // Смешано: есть ошибки, но что-то живо
						}
						else
						{
							summaryStatusIcon = "⏳"; // Ошибок нет, но не всё опубликовано (Pending)
						}
					}

					// 2. Собираем иконки сетей (как раньше)
					var sbIcons = new StringBuilder();
					foreach (var net in NetworkMetadata.Supported)
					{
						if (post.Networks[net].Status != SocialStatus.None)
							sbIcons.Append(NetworkMetadata.Info[net].Icon);
					}

					// 3. Формируем итоговую иконку: "✅ | ✈️📘"
					displayIcon = $"{summaryStatusIcon} | {sbIcons}";

					displayCaption = post.GetCaption(NetworkType.All);
				}
				else
				{
					// РЕЖИМ КОНКРЕТНОЙ СЕТИ (без изменений)
					var s = post.GetStatus(filterNet);
					displayIcon = s == SocialStatus.Published ? "✅" : (s == SocialStatus.Error ? "❌" : "⏳");
					displayCaption = post.GetCaption(filterNet);
				}

				if (string.IsNullOrWhiteSpace(displayCaption)) displayCaption = "Без текста";

				rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"{displayIcon} {displayCaption}", $"post_view:{post.Id}") });
			}

			// Навигация
			var navButtons = new List<InlineKeyboardButton>();
			bool hasBack = page > 0;
			bool hasNext = page < totalPages - 1;
			if (hasBack) navButtons.Add(InlineKeyboardButton.WithCallbackData("«", $"queue_list:{filterNet}:{accessFilter}:{page - 1}"));
			navButtons.Add(InlineKeyboardButton.WithCallbackData("🏠 Меню", "main_menu"));
			if (hasNext) navButtons.Add(InlineKeyboardButton.WithCallbackData("»", $"queue_list:{filterNet}:{accessFilter}:{page + 1}"));
			if (navButtons.Any()) rows.Add(navButtons);

			var keyboard = new InlineKeyboardMarkup(rows);

			if (messageIdToEdit.HasValue)
				try { await bot.EditMessageText(chatId, messageIdToEdit.Value, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct); }
				catch { await bot.DeleteMessage(chatId, messageIdToEdit.Value, ct); await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct); }
			else await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
		}

		private async Task ShowPostDetails(ITelegramBotClient bot, long chatId, int? messageIdToDelete, Guid postId, CancellationToken ct)
		{
			var session = _sessions.GetOrAdd(chatId, new UserSession());
			var post = await _postService.GetPostByIdAsync(postId);
			if (post == null) return;

			session.ActiveAlbumMessageIds.Clear();

			// -----------------------------------------------------------
			// 1. ОПРЕДЕЛЯЕМ ЦЕЛЕВЫЕ СЕТИ (С учетом фильтра Public/Private)
			// -----------------------------------------------------------
			IEnumerable<NetworkType> targetNetworks;
			string modeTitle;

			if (session.SelectedNetwork != NetworkType.All)
			{
				// Сценарий: Конкретная сеть
				targetNetworks = new[] { session.SelectedNetwork };
				modeTitle = $"Детали ({NetworkMetadata.Info[session.SelectedNetwork].Name})";
			}
			else
			{
				// Сценарий: Все сети (Смотрим на LastFilter!)
				switch (session.LastFilter)
				{
					case AccessFilter.Public:
						targetNetworks = NetworkMetadata.PublicSet; // Только публичные
						modeTitle = "Обзор (Public)";
						break;
					case AccessFilter.Private:
						targetNetworks = NetworkMetadata.PrivateSet; // Только приватные
						modeTitle = "Обзор (Private)";
						break;
					default: // AccessFilter.All
						targetNetworks = NetworkMetadata.Supported; // Вообще все
						modeTitle = "Обзор (Все сети)";
						break;
				}
			}

			// -----------------------------------------------------------
			// 2. ГЕНЕРИРУЕМ ТЕКСТ И СТАТУСЫ
			// -----------------------------------------------------------
			string StatusStr(SocialStatus s) => s switch { SocialStatus.Published => "✅", SocialStatus.Pending => "⏳", SocialStatus.Error => "❌", _ => "⛔" };

			var sbCaption = new StringBuilder();
			var sbStatus = new StringBuilder();

			foreach (var net in targetNetworks)
			{
				// Защита от отсутствующего ключа
				if (!NetworkMetadata.Info.ContainsKey(net)) continue;

				var meta = NetworkMetadata.Info[net];
				var data = post.Networks[net];

				// --- ОПИСАНИЕ ---
				// Если выбрана конкретная сеть - просто выводим текст (без заголовка)
				if (session.SelectedNetwork != NetworkType.All)
				{
					sbCaption.Append(data.Caption);
				}
				// Если список сетей - выводим с заголовками и иконками
				else
				{
					// Показываем, если статус активен ИЛИ если смотрим общий обзор (чтобы видеть пустоты)
					if (data.Status != SocialStatus.None || session.LastFilter == AccessFilter.All)
					{
						sbCaption.AppendLine($"{meta.Icon} **{meta.Name}:** {data.Caption}");
						sbCaption.AppendLine("------------");
					}
				}

				// --- СТАТУСЫ (Внизу) ---
				string shortName =/* meta.Name.Length > 2 ? meta.Name.Substring(0, 2).ToUpper() :*/ meta.Name;
				sbStatus.Append($"{meta.Icon}:{StatusStr(data.Status)} | ");
			}

			string accessHeader = post.Access == AccessLevel.Private ? "🔒 **ПРИВАТНЫЙ ПОСТ**" : "📢 **ПУБЛИЧНЫЙ ПОСТ**";
			var captionToShow = sbCaption.ToString().TrimEnd('-', '\n', '\r');
			var statusLine = sbStatus.ToString().TrimEnd('|', ' ');

			var infoText = $"📄 **{modeTitle}**\n{accessHeader}\n\n{captionToShow}\n\n{statusLine}";

			// -----------------------------------------------------------
			// 3. КНОПКИ
			// -----------------------------------------------------------

			// Проверяем ошибки ТОЛЬКО в текущих отображаемых сетях
			bool hasRelevantErrors = false;
			foreach (var net in targetNetworks)
			{
				if (post.Networks.TryGetValue(net, out var d) && d.Status == SocialStatus.Error)
				{
					hasRelevantErrors = true;
					break;
				}
			}

			var buttons = new List<IEnumerable<InlineKeyboardButton>>();
			var row1 = new List<InlineKeyboardButton>();

			string editLabel = session.SelectedNetwork == NetworkType.All ? "✏️ Ред. описание" : "✏️ Ред. описание";
			row1.Add(InlineKeyboardButton.WithCallbackData(editLabel, $"post_edit_start:{post.Id}"));

			if (hasRelevantErrors)
			{
				row1.Add(InlineKeyboardButton.WithCallbackData("🔄 Повторить (Error)", $"post_retry:{post.Id}"));
			}
			buttons.Add(row1);

			string deleteLabel;
			if (session.SelectedNetwork == NetworkType.All)
				deleteLabel = "❌ Удалить пост (Везде)";
			else
				deleteLabel = $"❌ Исключить из {NetworkMetadata.Info[session.SelectedNetwork].Name}";

			buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(deleteLabel, $"post_delete:{post.Id}") });
			buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 Назад", $"queue_list:{session.SelectedNetwork}:{session.LastFilter}:0") });

			var keyboard = new InlineKeyboardMarkup(buttons);

			// -----------------------------------------------------------
			// 4. ОТПРАВКА (ВАШ КОД)
			// -----------------------------------------------------------

			if (messageIdToDelete.HasValue) try { await bot.DeleteMessage(chatId, messageIdToDelete.Value, ct); } catch { }

			if (post.Images.Count > 0 && post.Images[0] == "dummy")
			{
				await bot.SendMessage(chatId, "🖼 [Альбом заглушек]\n\n" + infoText, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
			}
			else if (post.Images.Count == 1)
			{
				// Ваш сервис
				await _telegramService.SendSinglePhotoAsync(post.Images[0], null, infoText, ParseMode.Markdown, keyboard);
			}
			else
			{
				var sentMessages = await _telegramService.SendPhotoAlbumAsync(post.Images, null, "");
				session.ActiveAlbumMessageIds = sentMessages.Select(m => m.MessageId).ToList();

				await bot.SendMessage(chatId, infoText, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
			}
		}

		public async Task HandleUpdateAsync(Update update, CancellationToken ct)
		{
			if (update.Message != null && update.Message?.Text is not { } text)
			{
				_telegramService.HandleMediaGroup(update.Message);
			}

			//await _telegramService.SendMainButtonMessage();

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

			try
			{
				// 1. Обработка нажатий кнопок (CallbackQuery)
				if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
				{
					await HandleCallbackQuery(bot, update.CallbackQuery, ct);
					return;
				}

				// 2. Обработка сообщений (Message)
				if (update.Type == UpdateType.Message && update.Message != null)
				{
					await HandleMessage(bot, update.Message, ct);
					return;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
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
						await _telegramService.SendSinglePhotoAsync(images.Images.First(), null, description, senderId: chanelId);
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
