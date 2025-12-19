using System.Collections.Concurrent;
using System.Text;
using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.BuisinessLogic.Managers.Configurations;
using AlinaKrossManager.BuisinessLogic.Managers.Enums;
using AlinaKrossManager.BuisinessLogic.Managers.Models;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static AlinaKrossManager.BuisinessLogic.Services.TelegramService;

namespace AlinaKrossManager.BuisinessLogic.Managers
{
	public partial class TelegramManager
	{
		/// <summary>
		/// Оперативное хранилище сессий пользователей (in-memory).
		/// Используется для отслеживания контекста диалога с каждым конкретным пользователем.
		/// <br/>
		/// <b>Ключ:</b> ChatId пользователя (long).
		/// <br/>
		/// <b>Значение:</b> Объект сессии <see cref="UserSession"/>, содержащий текущее состояние (FSM), 
		/// выбранные фильтры, настройки загрузки и ID редактируемого поста.
		/// </summary>
		private static ConcurrentDictionary<long, UserSession> _sessions = new();

		/// <summary>
		/// Временный буфер для накопления частей медиа-альбомов (MediaGroup).
		/// <br/>
		/// Telegram отправляет фотографии из одного альбома как серию отдельных сообщений с одинаковым MediaGroupId.
		/// Этот словарь используется для агрегации этих сообщений в единую сущность перед созданием поста.
		/// Логика обработки использует таймер (Debounce), чтобы дождаться всех частей.
		/// <br/>
		/// <b>Ключ:</b> MediaGroupId (строка).
		/// <br/>
		/// <b>Значение:</b> Буфер <see cref="AlbumBuffer"/> со списком файлов и токеном отмены таймера.
		/// </summary>
		private static ConcurrentDictionary<string, AlbumBuffer> _albumBuffers = new();


		private string _tempCaption = null;
		private async Task<string> GenerateCaptionForNetworkAsync(NetworkType network, ImagesTelegram images)
		{
			// 1. Конфигурация: определяем сервис и стратегию кеширования
			var (service, useCache) = network switch
			{
				NetworkType.Instagram => ((SocialBaseService)_instagramService, true),
				NetworkType.Facebook => (_faceBookService, true),
				NetworkType.BlueSky => (_blueSkyService, true),
				NetworkType.TelegramPublic => (_publicTelegramChanel, false),
				NetworkType.TelegramPrivate => (_privateTelegramChanel, false),
				_ => (null, false)
			};

			// 2. Если сервис не найден (default case)
			if (service == null) return "Автоматическое описание";

			// 3. Выполнение логики
			string inputCaption = useCache ? _tempCaption : null;
			string result = await GetDescription(null, images, inputCaption, service);

			if (useCache)
			{
				_tempCaption = result;
			}

			return result;
		}

		private async Task HandleMessage(ITelegramBotClient bot, Message message, CancellationToken ct)
		{
			_tempCaption = null;
			var chatId = message.Chat.Id;
			var text = message.Text;
			var session = _sessions.GetOrAdd(chatId, new UserSession());

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
				// 1. СНАЧАЛА проверяем команду отмены
				if (text == "/cancel")
				{
					session.State = UserState.None; // Сбрасываем состояние

					await bot.SendMessage(chatId, "❌ Редактирование отменено.");

					// Возвращаем пользователя обратно к карточке поста, который он редактировал
					if (session.EditingPostId.HasValue)
					{
						await ShowPostDetails(bot, chatId, null, session.EditingPostId.Value, ct);
					}
					else
					{
						// Если ID потерялся (маловероятно), возвращаем в главное меню
						await ShowMainMenu(bot, chatId, ct);
					}

					// Очищаем временный ID
					session.EditingPostId = null;
					return;
				}

				// 2. Если это не отмена, значит пользователь прислал новый текст
				if (!string.IsNullOrWhiteSpace(text))
				{
					var post = await _postService.GetPostByIdAsync(session.EditingPostId.Value);
					if (post != null)
					{
						// Обновляем текст
						post.SetCaption(session.SelectedNetwork, text);
						await _postService.UpdatePostAsync(post);

						string target = session.SelectedNetwork == NetworkType.All ? "всех активных сетей" : session.SelectedNetwork.ToString();
						await bot.SendMessage(chatId, $"✅ Описание обновлено для {target}!");

						session.State = UserState.None;
						session.EditingPostId = null; // Очищаем ID

						// Возвращаем обновленную карточку поста
						await ShowPostDetails(bot, chatId, null, post.Id, ct);
					}
					else
					{
						// Если пост вдруг удалили, пока мы его редактировали
						session.State = UserState.None;
						await bot.SendMessage(chatId, "⚠️ Пост не найден.");
						await ShowMainMenu(bot, chatId, ct);
					}
				}

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
							"📢 **Загрузка: ВСЕ ПУБЛИЧНЫЕ**\n\nПришлите фото.", parseMode: ParseMode.Markdown, cancellationToken: ct);
					}

					// Сценарий "Во все ПРИВАТНЫЕ"
					else if (parts[1] == "AllPrivate")
					{
						session.SelectedNetwork = NetworkType.All;
						session.UploadAccess = AccessLevel.Private; // <--- Ставим флаг
						session.State = UserState.WaitingForPhoto;

						await bot.EditMessageText(chatId, messageId,
							"🔒 **Загрузка: ВСЕ ПРИВАТНЫЕ**\n\nПришлите фото.", parseMode: ParseMode.Markdown, cancellationToken: ct);
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
					// 1. Очищаем старые фото из чата (если вдруг они висят)
					await CleanupAlbumAsync();

					Guid retryId = Guid.Parse(parts[1]);

					// Получаем свежую версию из БД
					var postToRetry = await _postService.GetPostByIdAsync(retryId);

					if (postToRetry != null)
					{
						int countRetried = 0;

						// ЛОГИКА: Меняем Error -> Pending

						if (session.SelectedNetwork == NetworkType.All)
						{
							// СЦЕНАРИЙ 1: Мы в режиме "Все сети". 
							// Ищем ошибки во ВСЕХ сетях этого поста и сбрасываем их.
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
							// СЦЕНАРИЙ 2: Мы в конкретной сети (например, Instagram).
							// Сбрасываем ошибку ТОЛЬКО для этой сети.
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
							await _postService.UpdatePostAsync(postToRetry);
							await bot.AnswerCallbackQuery(callback.Id, $"✅ {countRetried} сетей отправлено на повтор. Запускаю...");
							await ShowPostDetails(bot, chatId, messageId, retryId, ct);

							_ = Task.Run(async () =>
							{
								try
								{
									// Создаем Scope, так как мы вышли из контекста запроса
									using (var scope = _scopeFactory.CreateScope())
									{
										// Достаем наш новый сервис публикации
										var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();

										// Достаем СВЕЖУЮ версию поста из БД (важно, чтобы подтянулись Pending статусы)
										var postToProcess = await scope.ServiceProvider.GetRequiredService<PostService>()
											.GetPostByIdAsync(retryId);

										if (postToProcess != null)
										{
											// Запускаем публикацию прямо сейчас!
											await publisher.ProcessSinglePostAsync(postToProcess);
										}
									}
								}
								catch (Exception ex)
								{
									Console.WriteLine($"Ошибка мгновенного повтора: {ex.Message}");
								}
							});
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
	}
}
