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

		public TelegramService(InstagramService instagramService, ITelegramBotClient telegramBotClient, IGenerativeLanguageModel generativeLanguageModel)
		{
			_instagramService = instagramService;
			_telegramBotClient = telegramBotClient;
			_generativeLanguageModel = generativeLanguageModel;
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
				case UpdateType.Message when msgText.IsCommand("post_to_insta") && update.Message.ReplyToMessage is Message rmsg:
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
									await _telegramBotClient.SendMessage(update.Message.Chat.Id, msgRes);
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
							try{await _telegramBotClient.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct);}catch {}
						}
					}

					break;
			}
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
