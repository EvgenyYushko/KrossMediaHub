using System.Globalization;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
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

		public TelegramManager(InstagramService instagramService
			, IGenerativeLanguageModel generativeLanguageModel
			, BlueSkyService blueSkyService
			, FaceBookService faceBookService
			, TelegramService telegramService
		)
		{
			_instagramService = instagramService;
			_generativeLanguageModel = generativeLanguageModel;
			_blueSkyService = blueSkyService;
			_faceBookService = faceBookService;
			_telegramService = telegramService;
		}

		private string _tempPostDescription = null;
		public async Task HandleUpdateAsync(Update update, CancellationToken ct)
		{
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
							msgStart = await _telegramService.SendMessage(update.Message.Chat.Id, "Генерируем изображение...");
							await GenerateImageByText(update, ct);
						}
						finally
						{
							try
							{
								await _telegramService.DeleteMessage(update.Message.Chat.Id, update.Message.MessageId, ct);
								await _telegramService.DeleteMessage(update.Message.Chat.Id, msgStart.MessageId, ct);
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
				case UpdateType.Message when msgText.IsCommand("post_to_insta") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						bool flowControl = await InstagramHandler(update, rmsg, ct);
						if (!flowControl)
						{
							return;
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_all") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;

						bool flowControl1 = await InstagramHandler(update, rmsg, ct);
						bool flowControl2 = await FaceBookHandler(update, rmsg, ct);
						bool? flowControl3 = await BlueSkyHandler(update, rmsg, ct);

						Console.WriteLine("Конце операции публикации во все сети");
					}
					break;
			}
		}

		private async Task<bool> InstagramHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации в instagram...");
			try
			{
				var replayText = rmsg.GetMsgText() ?? "";
				List<string> images = await _telegramService.TryGetIMagesPromTelegram(update, rmsg);
				if (images.Count == 0)
				{
					return false;
				}

				string description = await _instagramService.TryCreateDescription(replayText, images);
				_tempPostDescription = description;

				var result = await _instagramService.CreateMediaAsync(images, description);
				if (result.Success)
				{
					var msgRes = $"✅ Пост в инсте успешно создан! ID: {result.Id}";
					Console.WriteLine(msgRes);
					try
					{
						await _telegramService.SendMessage(update.Message.Chat.Id, msgRes, rmsg.MessageId);
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
				try { await _telegramService.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
			}

			return true;
		}	
		
		private async Task<bool> FaceBookHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации в facebook...");
			try
			{
				var replayText = rmsg.GetMsgText() ?? "";
				var resVideos = await _telegramService.TryGetVideoBase64FromTelegram(rmsg);
				var images = await _telegramService.TryGetIMagesPromTelegram(update, rmsg);
				if (images.Count == 0 && string.IsNullOrEmpty(replayText) && resVideos.base64Video is null)
				{
					return false;
				}

				string description = await _faceBookService.TryCreateDescription(replayText, images);
				if (string.IsNullOrEmpty(description))
				{
					description = _tempPostDescription;
				}

				bool success = false;
				if (images.Count > 0)
				{
					success = await _faceBookService.PublishToPageAsync(description, images);
				}
				else if (resVideos.base64Video is not null)
				{
					success = await _faceBookService.PublishReelAsync(description, resVideos.base64Video);
				}

				if (success)
				{
					var msgRes = $"✅ Пост в facebook успешно создан!";
					Console.WriteLine(msgRes);
					try
					{
						await _telegramService.SendMessage(update.Message.Chat.Id, msgRes, rmsg.MessageId);
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
				try { await _telegramService.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
			}

			return true;
		}	

		private async Task<bool?> BlueSkyHandler(Update update, Message rmsg, CancellationToken ct)
		{
			var startMsg = await _telegramService.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации в bluesky...");
			try
			{
				var images = await _telegramService.TryGetIMagesPromTelegram(update, rmsg);
				var resVideos = await _telegramService.TryGetVideoBase64FromTelegram(rmsg);
				var replayText = rmsg.GetMsgText() ?? "";
				if (images.Count == 0 && string.IsNullOrWhiteSpace(replayText) && resVideos.base64Video is null)
				{
					return true;
				}

				var description = await _blueSkyService.TryCreateDescription(replayText, images);
				if (string.IsNullOrEmpty(description))
				{
					description = _tempPostDescription;
				}

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
						var msgRes = $"✅ Пост в bluesky успешно создан!";
						Console.WriteLine(msgRes);
						try
						{
							await _telegramService.SendMessage(update.Message.Chat.Id, msgRes, rmsg.MessageId);
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
				try { await _telegramService.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
			}

			return null;
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
					await _telegramService.SendMessage(chatId, "📭 Изображения не сгенерированы.\nВозможно запрос не прошёл цензуру.");
					break;
				case 1:
					await _telegramService.SendSinglePhotoAsync(chatId, imagesList[0], msgId, caption);
					break;
				default:
					await _telegramService.SendPhotoAlbumAsync(chatId, imagesList, msgId, caption);
					break;
			}
		}
	}
}
