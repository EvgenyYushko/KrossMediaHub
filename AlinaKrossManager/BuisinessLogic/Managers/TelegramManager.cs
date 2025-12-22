using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using AlinaKrossManager.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static AlinaKrossManager.Helpers.TelegramUserHelper;

namespace AlinaKrossManager.BuisinessLogic.Managers
{
	public partial class TelegramManager
	{
		private readonly InstagramService _instagramService;
		private readonly IGenerativeLanguageModel _generativeLanguageModel;
		private readonly BlueSkyService _blueSkyService;
		private readonly FaceBookService _faceBookService;
		private readonly TelegramService _telegramService;
		private readonly PublicTelegramChanel _publicTelegramChanel;
		private readonly PrivateTelegramChanel _privateTelegramChanel;
		private readonly XService _xService;
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
			, XService xService
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
			_xService = xService;
			_postService = postService;
			_scopeFactory = scopeFactory;
			this.bot = bot;
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
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool flowControl = await InstagramPostHandler(update, rmsg, ct, publisher);
							if (!flowControl)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("story_to_insta") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool flowControl = await InstagramStoryHandler(update, rmsg, ct, publisher);
							if (!flowControl)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_facebook") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool flowControl = await FaceBookHandler(update, rmsg, ct, publisher);
							if (!flowControl)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("story_to_facebook") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool flowControl = await FaceBookStoryHandler(update, rmsg, ct, publisher);
							if (!flowControl)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_bluesky") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool? flowControl = await BlueSkyHandler(update, rmsg, ct, publisher);
							if (flowControl == false)
							{
								break;
							}
							else if (flowControl == true)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_tg_free") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool? flowControl = await TgFreeHandler(update, rmsg, ct, publisher, false);
							if (flowControl == false)
							{
								break;
							}
							else if (flowControl == true)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_tg_private") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool? flowControl = await TgPrivateHandler(update, rmsg, ct, publisher, true);
							if (flowControl == false)
							{
								break;
							}
							else if (flowControl == true)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_x") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;
						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();
							bool? flowControl = await XPostHandler(update, rmsg, ct, publisher);
							if (flowControl == false)
							{
								break;
							}
							else if (flowControl == true)
							{
								return;
							}
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_all") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;

						using (var scope = _scopeFactory.CreateScope())
						{
							var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();

							bool flowControl1 = await InstagramPostHandler(update, rmsg, ct, publisher);
							bool flowControl2 = await InstagramStoryHandler(update, rmsg, ct, publisher);
							bool flowControl3 = await FaceBookHandler(update, rmsg, ct, publisher);
							bool flowControl4 = await FaceBookStoryHandler(update, rmsg, ct, publisher);
							bool? flowControl5 = await BlueSkyHandler(update, rmsg, ct, publisher);
							bool? flowControl6 = await TgFreeHandler(update, rmsg, ct, publisher, true);
							bool? flowControl7 = await XPostHandler(update, rmsg, ct, publisher);
						}
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

		private async Task<bool> InstagramPostHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher)
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

				var result = await publisher.InstagramPost(description, images.Images);
				if (result)
				{
					try
					{
						await _telegramService.SendMessage("✅ Post insta success!", rmsg.MessageId);
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

		public async Task<bool> InstagramStoryHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем выкладывать сториз в instagram...");
			try
			{
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				if (!images.Existst)
				{
					return false;
				}

				var storyId = await publisher.InstagramStory(images.Images);
				if (storyId is not null)
				{
					try
					{
						await _telegramService.SendMessage("✅ Story insta success!", rmsg.MessageId);
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

		private async Task<bool> FaceBookHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher)
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
					success = await publisher.FaceBookPostImages(description, images.Images);
				}
				else if (resVideos.base64Video is not null)
				{
					success = await publisher.FaceBookPostReels(description, resVideos.base64Video);
				}

				if (success)
				{
					try
					{
						await _telegramService.SendMessage("✅ Post facebook success!", rmsg.MessageId);
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

		private async Task<bool> FaceBookStoryHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем выкладывать сториз в Facebook...");
			try
			{
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				if (!images.Existst)
				{
					return false;
				}

				var res = await publisher.FaceBookStory(images.Images);
				if (res)
				{
					try { await _telegramService.SendMessage("✅ Story Facebook success", rmsg.MessageId); } catch { }
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

		private async Task<bool> XPostHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher)
		{
			var startMsg = await _telegramService.SendMessage("Начинаем процесс публикации в X...");
			try
			{
				var replayText = rmsg.GetMsgText() ?? "";
				var images = await _telegramService.TryGetImagesPromTelegram(rmsg.MediaGroupId, rmsg.Photo);
				if (!images.Existst && string.IsNullOrEmpty(replayText))
				{
					return false;
				}

				var description = await GetDescription(rmsg, images, replayText, _xService);

				if (images.Existst)
				{
					var success = await publisher.XPost(description, images.Images);
					if (success)
					{
						try
						{
							await _telegramService.SendMessage("✅ Post X success!", rmsg.MessageId);
						}
						catch { }
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка X: {ex.Message}");
			}
			finally
			{
				try { await _telegramService.DeleteMessage(startMsg.MessageId, ct); } catch { }
			}

			return true;
		}

		private async Task<bool?> BlueSkyHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher)
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

				//await publisher.BlueSkyPost(description, images.Images);

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

		private Task<bool> TgFreeHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher, bool force = false)
		{
			return TgHandler(update, rmsg, ct, PublicTelegramChanel.CHANEL_ID, _publicTelegramChanel, publisher, force);
		}

		private Task<bool> TgPrivateHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher, bool force = false)
		{
			return TgHandler(update, rmsg, ct, PrivateTelegramChanel.CHANEL_ID, _privateTelegramChanel, publisher, force);
		}

		public async Task<bool> TgHandler(Update update, Message rmsg, CancellationToken ct, long chanelId
			, SocialBaseService socialBaseService, SocialPublicationFacade publisher, bool force = false)
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

				await publisher.TgHandler(ct, chanelId, serviceName, images.Images, description, resVideos);

				try
				{
					await _telegramService.SendMessage($"✅ Post {serviceName} success!", rmsg.MessageId);
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
