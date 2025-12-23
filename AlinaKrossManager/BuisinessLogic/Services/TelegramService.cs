using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class TelegramService
	{
		public const int EVGENY_YUSHKO_TG_ID = 1231047171;
		private readonly ITelegramBotClient _telegramBotClient;

		public TelegramService(ITelegramBotClient telegramBotClient)
		{
			_telegramBotClient = telegramBotClient;
		}

		public async Task<ImagesTelegram> TryGetImagesPromTelegram(string? mediaGroupId, PhotoSize[]? photo)
		{
			ImagesTelegram images = new();

			// Проверяем, это фотоальбом или одиночное фото
			if (mediaGroupId != null)
			{
				// Это фотоальбом - нужно получить все фото из группы
				images = await TryGetAllImagesFromMediaGroup(mediaGroupId);
			}
			else if (photo != null && photo.Length > 0)
			{
				// Одиночное фото - берем самый большой размер
				var base64Image = await TryGetImage(photo);
				images = new ImagesTelegram { Images = new List<string> { base64Image } };
			}

			if (images.Images.Count == 0)
			{
				await _telegramBotClient.SendMessage(EVGENY_YUSHKO_TG_ID, "❌ Не найдено фото для публикации");
				return images;
			}

			return images;
		}

		public Task<Message> SendMessage(long senderId, string text, ReplyMarkup replyMarkup)
		{
			return _telegramBotClient.SendMessage(senderId, text, replyMarkup: replyMarkup);
		}

		public Task<Message> SendMessage(long senderId, string text)
		{
			return _telegramBotClient.SendMessage(senderId, text);
		}

		public Task AnswerCallbackQuery(string callbackQueryId, string text)
		{
			return _telegramBotClient.AnswerCallbackQuery(callbackQueryId, text);
		}

		public Task<Message> SendMessage(string text, int? replayMsgId = null, ParseMode parseMode = ParseMode.Html, ReplyMarkup replyMarkup = null, CancellationToken cancellationToken = default)
		{
			if (text.Length > 4096)
			{
				text = text.Substring(0, 4000) + "\n...(обрезано)";
				parseMode = ParseMode.Html;
			}

			if (replayMsgId is null)
			{
				return _telegramBotClient.SendMessage(EVGENY_YUSHKO_TG_ID, text, parseMode: parseMode, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
			}

			return _telegramBotClient.SendMessage(EVGENY_YUSHKO_TG_ID, text,
				replyParameters: new ReplyParameters { MessageId = replayMsgId.Value },
				parseMode: parseMode, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
		}

		public class VideoModel
		{
			public string Base64Video { get; set; }
			public string MimeType { get; set; }
		}

		public async Task<VideoModel> TryGetVideoBase64FromTelegram(Message rmsg)
		{
			// 1. Проверяем, есть ли видео в сообщении
			if (rmsg.Video == null)
			{
				//await _telegramBotClient.SendMessage(EVGENY_YUSHKO_TG_ID, "❌ В сообщении не найдено видео для публикации.");
				return null;
			}

			// 2. Получаем информацию о видео
			var video = rmsg.Video;

			// 3. Проверяем наличие FileId и MIME-типа
			if (string.IsNullOrEmpty(video.FileId) || string.IsNullOrEmpty(video.MimeType))
			{
				await _telegramBotClient.SendMessage(EVGENY_YUSHKO_TG_ID, "❌ Видео найдено, но отсутствует FileId или MIME-тип.");
				return null;
			}

			// 4. Загружаем файл и конвертируем его в Base64
			try
			{
				// Используем вспомогательный метод для загрузки по FileId
				var base64Video = await TryGetFileBase64(rmsg.Video);

				if (!string.IsNullOrEmpty(base64Video))
				{
					Console.WriteLine($"✅ Видео успешно загружено. Размер байт: {video.FileSize}. MIME: {video.MimeType}");
					return new VideoModel { Base64Video = base64Video, MimeType = video.MimeType };
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при загрузке видео из Telegram: {ex.Message}");
				await _telegramBotClient.SendMessage(EVGENY_YUSHKO_TG_ID, $"❌ Критическая ошибка при загрузке видео: {ex.Message}");
			}

			return null;
		}

		public async Task<bool> CanUseBot(Update update, CancellationToken ct)
		{
			if (update.Message.Chat.Id != EVGENY_YUSHKO_TG_ID || update.Message.Chat.Type is not ChatType.Private)
			{
				await _telegramBotClient.SendMessage(EVGENY_YUSHKO_TG_ID, "Данная комманда доступна только в ЛС чата, и только для его администратора!");
				return false;
			}
			return true;
		}

		public async Task<Message> SendVideoAsync(long senderId, string text, Video video)
		{
			return await _telegramBotClient.SendVideo(senderId, video, text);
		}

		public async Task<Message> SendSinglePhotoAsync(string base64Image, int? msgId, string caption = "", ParseMode parseMode = ParseMode.None, ReplyMarkup replyMarkup = null, long senderId = EVGENY_YUSHKO_TG_ID)
		{
			var imageBytes = Convert.FromBase64String(base64Image);

			// Проверка длины ДО отправки (1024 - лимит Telegram для caption)
			bool isCaptionTooLong = caption.Length > 1024;

			using (var stream = new MemoryStream(imageBytes))
			{
				if (isCaptionTooLong)
				{
					// Сценарий: Длинное описание
					// 1. Шлем фото пустным
					var photoMsg = await _telegramBotClient.SendPhoto(senderId, InputFile.FromStream(stream, "image.jpg"));

					// 2. Шлем текст отдельно
					await _telegramBotClient.SendMessage(senderId, caption, replyMarkup: replyMarkup, parseMode: parseMode);

					return photoMsg;
				}
				else
				{
					// Сценарий: Нормальное описание
					return await _telegramBotClient.SendPhoto(senderId, InputFile.FromStream(stream, "image.jpg"), caption, replyMarkup: replyMarkup, parseMode: parseMode);
				}
			}
		}

		public async Task<Message[]> SendPhotoAlbumAsync(List<string> base64Images, int? msgId, string caption = "", long senderId = EVGENY_YUSHKO_TG_ID)
		{
			var media = new List<IAlbumInputMedia>();
			var streams = new List<MemoryStream>(); // храним ссылки на стримы
			Message[] messages = null;

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
						inputMedia.Caption = caption;
						inputMedia.ParseMode = ParseMode.Html;
					}

					media.Add(inputMedia);
				}

				if (msgId is not null)
				{
					messages = await _telegramBotClient.SendMediaGroup(senderId, media, new ReplyParameters { MessageId = msgId.Value });
					try
					{
						Console.WriteLine("Пробуем сохранить MediaGroupId");
						foreach (var message in messages)
						{
							HandleMediaGroup(message);
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
						return messages;
					}
					Console.WriteLine("MediaGroupId успешно сохранили");
				}
				else
				{
					messages = await _telegramBotClient.SendMediaGroup(senderId, media);

					try
					{
						Console.WriteLine("Пробуем сохранить MediaGroupId");
						foreach (var message in messages)
						{
							HandleMediaGroup(message);
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
						return messages;
					}
					Console.WriteLine("MediaGroupId успешно сохранили");
				}

				return messages;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return null;
			}
			finally
			{
				// Освобождаем ресурсы после отправки
				foreach (var stream in streams)
				{
					stream?.Dispose();
				}
			}
		}

		public async Task<Message> SendPaidPhotosAsync(IEnumerable<string> base64Images, int starCount, string caption = "", ParseMode parseMode = ParseMode.None,
			long senderId = EVGENY_YUSHKO_TG_ID)
		{
			if (base64Images == null || !base64Images.Any())
			{
				Console.WriteLine("Список изображений пуст.");
				return null;
			}

			// Список для хранения потоков, чтобы закрыть их после отправки
			var streams = new List<MemoryStream>();

			// Список медиа-объектов для Телеграма
			var paidMediaItems = new List<InputPaidMedia>();

			try
			{
				int index = 1;
				foreach (var base64 in base64Images)
				{
					// Конвертируем строку в байты
					var imageBytes = Convert.FromBase64String(base64);

					// Создаем поток
					var stream = new MemoryStream(imageBytes);

					// Добавляем поток в список очистки (чтобы он не потерялся и мы могли его закрыть)
					streams.Add(stream);

					// Добавляем фото в список медиа
					paidMediaItems.Add(new InputPaidMediaPhoto
					{
						Media = InputFile.FromStream(stream, $"paid_image_{index}.jpg")
					});

					index++;
				}

				// Отправляем весь пакет
				return await _telegramBotClient.SendPaidMedia(
					chatId: senderId,
					starCount: starCount,
					media: paidMediaItems,
					caption: caption,
					parseMode: parseMode
				);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка отправки платного альбома: {ex.Message}");
				throw;
			}
			finally
			{
				// ВАЖНО: Очищаем все потоки после отправки (или ошибки)
				foreach (var stream in streams)
				{
					stream.Dispose();
				}
			}
		}

		public async Task<Message> SendPaidVideosAsync(IEnumerable<string> base64Videos, int starCount, string caption = "", ParseMode parseMode = ParseMode.None,
			long senderId = EVGENY_YUSHKO_TG_ID)
		{
			if (base64Videos == null || !base64Videos.Any())
			{
				Console.WriteLine("Список видео пуст.");
				return null;
			}

			// Список потоков для последующей очистки памяти
			var streams = new List<MemoryStream>();

			// Список объектов медиа
			var paidMediaItems = new List<InputPaidMedia>();

			try
			{
				int index = 1;
				foreach (var base64 in base64Videos)
				{
					var videoBytes = Convert.FromBase64String(base64);
					var stream = new MemoryStream(videoBytes);

					// Сохраняем ссылку на поток, чтобы закрыть его в finally
					streams.Add(stream);

					paidMediaItems.Add(new InputPaidMediaVideo
					{
						// Важно: указываем расширение .mp4
						Media = InputFile.FromStream(stream, $"paid_video_{index}.mp4"),
						SupportsStreaming = true // Оптимизация для просмотра
					});

					index++;
				}

				return await _telegramBotClient.SendPaidMedia(
					chatId: senderId,
					starCount: starCount,
					media: paidMediaItems,
					caption: caption,
					parseMode: parseMode
				);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка отправки платных видео: {ex.Message}");
				throw;
			}
			finally
			{
				// Обязательно освобождаем память
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

		public Task EditMessageText(int messageId, string text, ParseMode parseMode, InlineKeyboardMarkup replyMarkup = null, CancellationToken cancellationToken = default)
		{
			return _telegramBotClient.EditMessageText(new ChatId(EVGENY_YUSHKO_TG_ID), messageId, text, parseMode, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
		}

		public Task DeleteMessage(int messageId, CancellationToken ct)
		{
			return _telegramBotClient.DeleteMessage(new ChatId(EVGENY_YUSHKO_TG_ID), messageId, ct);
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
		private async Task<ImagesTelegram> TryGetAllImagesFromMediaGroup(string mediaGroupId)
		{
			var base64Images = new List<string>();
			string caption = null;

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
							caption = message.Caption ?? caption;
						}
					}
				}

				// Удаляем обработанную группу
				//_activeMediaGroups.Remove(mediaGroupId);
			}

			return new ImagesTelegram { Images = base64Images, Caption = caption };
		}

		public class ImagesTelegram
		{
			public List<string> Images { get; set; } = new();
			public string Caption { get; set; }
			public bool Existst => Images?.Count > 0;
		}

		// И где-то в обработке сообщений нужно собирать медиагруппы:
		public void HandleMediaGroup(Message message)
		{
			if (message?.MediaGroupId == null || message.Photo == null)
				return;

			var mediaGroupId = message.MediaGroupId;

			// Гарантируем что группа существует
			if (!_activeMediaGroups.TryGetValue(mediaGroupId, out var groupMessages))
			{
				groupMessages = new List<Message>();
				_activeMediaGroups[mediaGroupId] = groupMessages;
			}

			// Обновляем или добавляем
			var existingIndex = groupMessages.FindIndex(m => m.MessageId == message.MessageId);

			if (existingIndex >= 0)
				groupMessages[existingIndex] = message;
			else
				groupMessages.Add(message);
		}

		public void UpdateCaptionMediaGrup(Message rmsg, string description)
		{
			if (rmsg is not null)
			{
				rmsg.Caption = description;
				HandleMediaGroup(rmsg);
			}
		}
	}
}
