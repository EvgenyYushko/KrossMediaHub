using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class TelegramService
	{
		private const long EVGENY_YUSHKO_TG_ID = 1231047171;
		private readonly ITelegramBotClient _telegramBotClient;

		public TelegramService(ITelegramBotClient telegramBotClient)
		{
			_telegramBotClient = telegramBotClient;
		}

		public async Task<List<string>> TryGetIMagesPromTelegram(Update update, Message rmsg)
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
				await _telegramBotClient.SendMessage(update.Message.Chat.Id, "❌ Не найдено фото для публикации");
				return images;
			}

			return images;
		}

		public Task<Message> SendMessage(ChatId chatId, string text, int? replayMsgId = null)
		{
			if (replayMsgId is null)
			{
				return _telegramBotClient.SendMessage(chatId, text);
			}

			return _telegramBotClient.SendMessage(chatId, text, replyParameters: new ReplyParameters { MessageId = replayMsgId.Value });
		}

		public async Task<(string? base64Video, string? mimeType)> TryGetVideoBase64FromTelegram(Message rmsg)
		{
			// 1. Проверяем, есть ли видео в сообщении
			if (rmsg.Video == null)
			{
				//await _telegramBotClient.SendMessage(rmsg.Chat.Id, "❌ В сообщении не найдено видео для публикации.");
				return (null, null);
			}

			// 2. Получаем информацию о видео
			var video = rmsg.Video;

			// 3. Проверяем наличие FileId и MIME-типа
			if (string.IsNullOrEmpty(video.FileId) || string.IsNullOrEmpty(video.MimeType))
			{
				await _telegramBotClient.SendMessage(rmsg.Chat.Id, "❌ Видео найдено, но отсутствует FileId или MIME-тип.");
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
				await _telegramBotClient.SendMessage(rmsg.Chat.Id, $"❌ Критическая ошибка при загрузке видео: {ex.Message}");
			}

			return (null, null);
		}

		public async Task<bool> CanUseBot(Update update, CancellationToken ct)
		{
			if (update.Message.Chat.Id != EVGENY_YUSHKO_TG_ID || update.Message.Chat.Type is not ChatType.Private)
			{
				await _telegramBotClient.SendMessage(update.Message.Chat.Id, "Данная комманда доступна только в ЛС чата, и только для его администратора!");
				return false;
			}
			return true;
		}

		public async Task SendSinglePhotoAsync(long chatId, string base64Image, int? msgId, string caption = "")
		{
			var imageBytes = Convert.FromBase64String(base64Image);
			using var stream = new MemoryStream(imageBytes);

			if (msgId is not null)
			{
				var sentMessage = await _telegramBotClient.SendPhoto(chatId,
					InputFile.FromStream(stream, "image.jpg"),
					caption,
					replyParameters:
						new ReplyParameters
						{
							MessageId = msgId.Value
						});
			}
			else
			{
				var sentMessage = await _telegramBotClient.SendPhoto(chatId, InputFile.FromStream(stream, "image.jpg"), caption);
			}
		}

		public async Task SendPhotoAlbumAsync(long chatId, List<string> base64Images, int? msgId, string caption = "")
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

				if (msgId is not null)
				{
					var sentMessages = await _telegramBotClient.SendMediaGroup(chatId, media, new ReplyParameters { MessageId = msgId.Value });
				}
				else
				{
					var sentMessages = await _telegramBotClient.SendMediaGroup(chatId, media);

					try
					{
						Console.WriteLine("Пробуем сохранить MediaGroupId");
						foreach (var message in sentMessages)
						{
							HandleMediaGroup(message);
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
						return;	
					}
					Console.WriteLine("MediaGroupId успешно сохранили");
				}
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

		public Task DeleteMessage(ChatId chatId, int messageId)
		{
			var ct = new CancellationToken();
			return _telegramBotClient.DeleteMessage(chatId, messageId, ct);
		}

		public Task DeleteMessage(ChatId chatId, int messageId, CancellationToken ct)
		{
			return _telegramBotClient.DeleteMessage(chatId, messageId, ct);
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
				//_activeMediaGroups.Remove(mediaGroupId);
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
