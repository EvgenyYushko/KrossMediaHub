using AlinaKrossManager.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static AlinaKrossManager.Helpers.TelegramUserHelper;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class TelegramService
	{
		private readonly InstagramService _instagramService;
		private readonly ITelegramBotClient _telegramBotClient;
		private readonly IGenerativeLanguageModel _generativeLanguageModel;
		private readonly BlueSkyService _blueSkyService;

		public TelegramService(InstagramService instagramService
			, ITelegramBotClient telegramBotClient
			, IGenerativeLanguageModel generativeLanguageModel
			, BlueSkyService blueSkyService
		)
		{
			_instagramService = instagramService;
			_telegramBotClient = telegramBotClient;
			_generativeLanguageModel = generativeLanguageModel;
			_blueSkyService = blueSkyService;
		}

		public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
		{
			if (update.Message?.Text is not { } text)
			{
				HandleMediaGroup(update.Message);
				return;
			}

			var msgText = update.Message.GetMsgText() ?? "";

			switch (update.Type)
			{
				case UpdateType.Message when msgText.IsCommand("generate_image"):
					{
						if (update.Message.ReplyToMessage is Message rmsg)
						{
							if (update.Message.Chat.Type is not ChatType.Private)
							{
								await SendMsgBotOnly(update, ct);
								return;
							}

							Message msgStart = null;
							try
							{
								msgStart = await botClient.SendMessage(update.Message.Chat.Id, "Генерируем изображение...");
								await GenerateImageByText(update, ct);
							}
							finally
							{
								try
								{
									await _telegramBotClient.DeleteMessage(update.Message.Chat.Id, update.Message.MessageId, ct);
									await _telegramBotClient.DeleteMessage(update.Message.Chat.Id, msgStart.MessageId, ct);
								}
								catch { }
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_threads") && update.Message.ReplyToMessage is Message rmsg:
					{
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
				case UpdateType.Message when msgText.IsCommand("post_to_bluesky") && update.Message.ReplyToMessage is Message rmsg:
					{
						var startMsg = await botClient.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации...");

						try
						{
							List<string> images = await TryGetIMagesPromTelegram(botClient, update, rmsg);
							var resVideos = await TryGetVideoBase64FromTelegram(botClient, rmsg);
							var replayText = rmsg.GetMsgText() ?? "";
							if (images.Count == 0 && string.IsNullOrWhiteSpace(replayText) && resVideos.base64Video is null)
							{
								return;
							}

							// 1. Первичный вход при запуске
							if (!_blueSkyService.BlueSkyLogin)
							{
								if (!await _blueSkyService.LoginAsync())
								{
									Console.WriteLine("Критическая ошибка: не удалось войти в аккаунт.");
									return;
								}

								Console.WriteLine("Успешно удалось войти в аккаунт. ✅");
								_blueSkyService.BlueSkyLogin = true;
							}

							if (await _blueSkyService.UpdateSessionAsync())
							{
								// 3. Публикуем с новым токеном, который теперь хранится внутри service.AccessJwt

								List<ImageAttachment> attachments = null;
								if (images.Count > 0)
								{
									attachments = new();
									foreach (var image in images)
									{
										attachments.Add(new ImageAttachment
										{
											Image = await _blueSkyService.UploadImageFromBase64Async(image, "image/png")
										});
									}
								}

								bool success = false;

								if (resVideos.base64Video is not null)
								{
									var videoBlob = await _blueSkyService.UploadVideoFromBase64Async(resVideos.base64Video, resVideos.mimeType);
									if (videoBlob == null)
									{
										Console.WriteLine("Ошибка: не удалось загрузить видео.");
										return;
									}
									var ratio = new AspectRatio { Width = 9, Height = 16 };

									// 3. Постинг
									success = await _blueSkyService.CreatePostWithVideoAsync(replayText, videoBlob, ratio);
								}
								else if (attachments is not null)
								{
									success = await _blueSkyService.CreatePostWithImagesAsync(replayText, attachments);
								}
								else
								{
									success = await _blueSkyService.CreatePostAsync(replayText);
								}

								if (success)
								{
									var msgRes = $"✅ Пост успешно создан!";
									Console.WriteLine(msgRes);
									try
									{
										await _telegramBotClient.SendMessage(update.Message.Chat.Id, msgRes, replyParameters: new ReplyParameters { MessageId = rmsg.MessageId });
									}
									catch { }
								}
							}
							else
							{
								Console.WriteLine("Не удалось обновить токен. Попытка повторного входа...");
								// Можно попробовать LoginAsync еще раз, если Refresh Token истек.
								if (!await _blueSkyService.LoginAsync())
								{
									Console.WriteLine("Не удалось выполнить повторный вход. Завершение работы.");
									break;
								}
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine($"Ошибка: {ex.Message}");
						}
						finally
						{
							try { await _telegramBotClient.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_facebook") && update.Message.ReplyToMessage is Message rmsg:
					{
						List<string> images = await TryGetIMagesPromTelegram(botClient, update, rmsg);
						if (images.Count == 0)
						{
							return;
						}

						var startMsg = await botClient.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации...");
						try
						{
							var longLiveToken = "EAAY5A6MrJHgBPZBQrANTL62IRrEdPNAFCTMBBRg1PraciiqfarhG98YZCdGO9wxEhza3uk7BE56KEDGtWHagB8hgaUsQUFiQ3x3uhPZBbZBDZC6BtGsmoQURUAO7aVSEktmGeer6TtQZC9PWA6ZAM0EEgInZAFtWmjkz7ow4IDsCl7B55O80n2VW9wsNil3Nh8F5lkRfbIpj";
							var faceBookService = new FaceBook(longLiveToken);

							var res = await faceBookService.PublishToPageAsync("Hello from API", images);
							if (res)
							{
								var msgRes = $"✅ Пост успешно создан!";
								Console.WriteLine(msgRes);
								try
								{
									await _telegramBotClient.SendMessage(update.Message.Chat.Id, msgRes, replyParameters: new ReplyParameters { MessageId = rmsg.MessageId });
								}
								catch { }
							}

						}
						catch (Exception ex)
						{
							Console.WriteLine($"Ошибка: {ex.Message}");
						}
						finally
						{
							try { await _telegramBotClient.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_insta") && update.Message.ReplyToMessage is Message rmsg:
					{
						List<string> images = await TryGetIMagesPromTelegram(botClient, update, rmsg);
						if (images.Count == 0)
						{
							return;
						}

						string description = "";
						var startMsg = await botClient.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации...");
						try
						{

							var promptForeDescriptionPost = "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в инстаграм под постом с фотографией. " +
								$"А так же придумай не более 15 хештогов, они должны соответствовать " +
								$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
								$"Вот само изображение: {images.FirstOrDefault()}" +
								$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
								$"без всякого рода ковычек и экранирования. " +
								$"Пример ответа: Golden hour glow ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence #neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";

							description = await _generativeLanguageModel.GeminiRequest(promptForeDescriptionPost);
							try
							{
								await _telegramBotClient.SendMessage(update.Message.Chat.Id, $"{description}", replyParameters: new ReplyParameters { MessageId = rmsg.MessageId });
							}
							catch { }
						}
						catch (Exception e)
						{
							Console.WriteLine(e.Message);
						}

						try
						{
							var result = await _instagramService.CreateMediaAsync(images, description);
							if (result.Success)
							{
								var msgRes = $"✅ Пост успешно создан! ID: {result.Id}";
								Console.WriteLine(msgRes);
								try
								{
									await _telegramBotClient.SendMessage(update.Message.Chat.Id, msgRes, replyParameters: new ReplyParameters { MessageId = rmsg.MessageId });
								}
								catch { }
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine($"❌ Ошибка: {ex.Message}");
						}
						finally
						{
							try { await _telegramBotClient.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
						}
					}

					break;
			}
		}

		private async Task<List<string>> TryGetIMagesPromTelegram(ITelegramBotClient botClient, Update update, Message rmsg)
		{
			List<string> images = new();

			// Проверяем, это фотоальбом или одиночное фото
			if (rmsg.MediaGroupId != null)
			{
				// Это фотоальбом - нужно получить все фото из группы
				images = await TryGetAllImagesFromMediaGroup(rmsg.MediaGroupId);
			}
			else if (rmsg.Photo != null && rmsg.Photo.Length > 0)
			{
				// Одиночное фото - берем самый большой размер
				var base64Image = await TryGetImage(rmsg.Photo);
				images = new List<string>() { base64Image };
			}

			if (images.Count == 0)
			{
				await botClient.SendMessage(update.Message.Chat.Id, "❌ Не найдено фото для публикации");
				return images;
			}

			return images;
		}

		public async Task<(string? base64Video, string? mimeType)> TryGetVideoBase64FromTelegram(ITelegramBotClient botClient, Message rmsg)
		{
			// 1. Проверяем, есть ли видео в сообщении
			if (rmsg.Video == null)
			{
				await botClient.SendMessage(rmsg.Chat.Id, "❌ В сообщении не найдено видео для публикации.");
				return (null, null);
			}

			// 2. Получаем информацию о видео
			var video = rmsg.Video;

			// 3. Проверяем наличие FileId и MIME-типа
			if (string.IsNullOrEmpty(video.FileId) || string.IsNullOrEmpty(video.MimeType))
			{
				await botClient.SendMessage(rmsg.Chat.Id, "❌ Видео найдено, но отсутствует FileId или MIME-тип.");
				return (null, null);
			}

			// 4. Загружаем файл и конвертируем его в Base64
			try
			{
				// Используем вспомогательный метод для загрузки по FileId
				var base64Video = await TryGetFileBase64(rmsg.Video);

				if (!string.IsNullOrEmpty(base64Video))
				{
					Console.WriteLine($"✅ Видео успешно загружено. Размер байт: {video.FileSize}. MIME: {video.MimeType}");
					return (base64Video, video.MimeType);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при загрузке видео из Telegram: {ex.Message}");
				await botClient.SendMessage(rmsg.Chat.Id, $"❌ Критическая ошибка при загрузке видео: {ex.Message}");
			}

			return (null, null);
		}



		public async Task SendMsgBotOnly(Update update, CancellationToken ct)
		{
			await _telegramBotClient.SendMessage(update.Message.Chat.Id, "Данная комманда доступна только в ЛС чата");
		}

		public async Task GenerateImageByText(Update update, CancellationToken ct)
		{
			var imagesList = await _generativeLanguageModel.GeminiRequestGenerateImage(update.Message.ReplyToMessage.Text);
			var chatId = update.Message.Chat.Id;
			var msgId = update.Message.ReplyToMessage.MessageId;
			string caption = "";
			switch (imagesList.Count)
			{
				case 0:
					await _telegramBotClient.SendMessage(chatId, "📭 Изображения не сгенерированы.\nВозможно запрос не прошёл цензуру.");
					break;
				case 1:
					await SendSinglePhotoAsync(chatId, imagesList[0], msgId, caption);
					break;
				default:
					await SendPhotoAlbumAsync(chatId, imagesList, msgId, caption);
					break;
			}
		}

		public async Task SendSinglePhotoAsync(long chatId, string base64Image, int msgId, string caption = "")
		{
			var imageBytes = Convert.FromBase64String(base64Image);
			using var stream = new MemoryStream(imageBytes);

			var sentMessage = await _telegramBotClient.SendPhoto(chatId,
				InputFile.FromStream(stream, "image.jpg"),
				caption,
				replyParameters:
					new ReplyParameters
					{
						MessageId = msgId
					});
		}

		public async Task SendPhotoAlbumAsync(long chatId, List<string> base64Images, int msgId, string caption = "")
		{
			var media = new List<IAlbumInputMedia>();
			var streams = new List<MemoryStream>(); // храним ссылки на стримы

			try
			{
				for (int i = 0; i < base64Images.Count; i++)
				{
					var imageBytes = Convert.FromBase64String(base64Images[i]);
					var stream = new MemoryStream(imageBytes); // без using!
					streams.Add(stream); // сохраняем ссылку

					var inputMedia = new InputMediaPhoto(InputFile.FromStream(stream, $"image_{i}.jpg"));

					if (i == 0 && !string.IsNullOrEmpty(caption))
					{
						//inputMedia.Caption = caption;
						inputMedia.ParseMode = ParseMode.Html;
					}

					media.Add(inputMedia);
				}

				var sentMessages = await _telegramBotClient.SendMediaGroup(chatId, media, new ReplyParameters { MessageId = msgId });
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
			finally
			{
				// Освобождаем ресурсы после отправки
				foreach (var stream in streams)
				{
					stream.Dispose();
				}
			}
		}

		private async Task<string> TryGetFileBase64(Video? video)
		{
			// Проверка наличия объекта Video и FileId
			if (video is null || string.IsNullOrEmpty(video.FileId))
			{
				return null;
			}

			// 1. Получаем информацию о файле (включая FilePath)
			// Аналогично вашему примеру: _telegramBotClient.GetFile
			// !!! УБЕДИТЕСЬ, ЧТО ЭТОТ МЕТОД ПРИНИМАЕТ ТОЛЬКО fileId ИЛИ ОБЪЕКТ Video
			// Если ваш _telegramBotClient.GetFile принимает только string fileId:
			var file = await _telegramBotClient.GetFile(video.FileId);

			if (file.FilePath is null)
			{
				// Если FilePath не получен, значит, файл недоступен
				return null;
			}

			// 2. Скачиваем видеофайл
			string base64Video;
			using (var ms = new MemoryStream())
			{
				try
				{
					// Вызываем DownloadFile, который есть на интерфейсе ITelegramBotClient
					// (Используем FilePath, полученный на Шаге 1)
					await _telegramBotClient.DownloadFile(file.FilePath, ms);

					// 3. Конвертируем байты в Base64
					byte[] videoBytes = ms.ToArray();
					base64Video = Convert.ToBase64String(videoBytes);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ошибка при скачивании видео {video.FileId}: {ex.Message}");
					return null;
				}
			}

			return base64Video;
		}

		private async Task<string> TryGetImage(PhotoSize[] photo)
		{
			if (photo is null || photo.Length == 0)
			{
				return null;
			}

			// 1. Получаем самый большой размер фото
			var photoSize = photo[^1];

			// 2. ЗАМЕНА GetFileAsync на SendRequest<File> (для получения file.FilePath)
			// TelegramBotClientExtensions.GetFileAsync -> telegramClient.SendRequest<File>(new GetFileRequest)
			var file = await _telegramBotClient.GetFile(photoSize.FileId);

			if (file.FilePath is null)
			{
				return null;
			}

			// 3. Скачиваем изображение
			// ЗАМЕНА DownloadFileAsync на DownloadFile (метод на ITelegramBotClient)
			string base64Image;
			using (var ms = new MemoryStream())
			{
				// Вызываем DownloadFile, который есть на интерфейсе ITelegramBotClient
				await _telegramBotClient.DownloadFile(file.FilePath, ms);

				byte[] imageBytes = ms.ToArray();
				base64Image = Convert.ToBase64String(imageBytes);
			}

			return base64Image;
		}

		// Добавьте этот словарь в ваш класс бота
		private readonly Dictionary<string, List<Message>> _activeMediaGroups = new();

		// Метод для получения всех фото из медиагруппы
		private async Task<List<string>> TryGetAllImagesFromMediaGroup(string mediaGroupId)
		{
			var base64Images = new List<string>();

			// Проверяем, есть ли у нас все сообщения из этой группы
			if (_activeMediaGroups.ContainsKey(mediaGroupId))
			{
				foreach (var message in _activeMediaGroups[mediaGroupId])
				{
					if (message.Photo != null && message.Photo.Length > 0)
					{
						var base64Image = await TryGetImage(message.Photo);
						if (base64Image != null)
						{
							base64Images.Add(base64Image);
						}
					}
				}

				// Удаляем обработанную группу
				_activeMediaGroups.Remove(mediaGroupId);
			}

			return base64Images;
		}

		// И где-то в обработке сообщений нужно собирать медиагруппы:
		public void HandleMediaGroup(Message message)
		{
			if (message.MediaGroupId != null && message.Photo != null)
			{
				var mediaGroupId = message.MediaGroupId;

				if (!_activeMediaGroups.ContainsKey(mediaGroupId))
				{
					_activeMediaGroups[mediaGroupId] = new List<Message>();
				}

				_activeMediaGroups[mediaGroupId].Add(message);

				// Можно добавить таймер для автоматической очистки старых групп
			}
		}
	}
}
