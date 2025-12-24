using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static AlinaKrossManager.Helpers.TelegramUserHelper;

namespace AlinaKrossManager.BuisinessLogic.Managers
{
	public partial class TelegramManager
	{
		private readonly TelegramService _telegramService;
		private readonly PostService _postService;
		private readonly IServiceScopeFactory _scopeFactory;
		private readonly AiFacade _aiFacade;

		public TelegramManager(IServiceScopeFactory scopeFactory
			, TelegramService telegramService
			, PostService postService
			, AiFacade aiFacade
		)
		{
			_telegramService = telegramService;
			_postService = postService;
			_scopeFactory = scopeFactory;
			_aiFacade = aiFacade;
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
					await HandleCallbackQuery(update.CallbackQuery, ct);
					return;
				}

				// 2. Обработка сообщений (Message)
				if (update.Type == UpdateType.Message && update.Message != null)
				{
					await HandleMessage(update.Message, ct);
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

				var description = await GetDescription(rmsg, images, replayText, false, InstagramService.GetBaseDescriptionPrompt(images.Existst ? images.Images.First() : null));

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
				if (!images.Existst && string.IsNullOrEmpty(replayText) && resVideos is null)
				{
					return false;
				}

				var description = await GetDescription(rmsg, images, replayText, false, FaceBookService.GetBaseDescriptionPrompt(images.Existst ? images.Images.First() : null));

				bool success = false;
				if (images.Existst)
				{
					success = await publisher.FaceBookPostImages(description, images.Images);
				}
				else if (resVideos is not null)
				{
					success = await publisher.FaceBookPostReels(description, resVideos.Base64Video);
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

				var description = await GetDescription(rmsg, images, replayText, false, XService.GetBaseDescriptionPrompt(images.Existst ? images.Images.First() : null));

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
				if (!images.Existst && string.IsNullOrWhiteSpace(replayText) && resVideos is null)
				{
					return true;
				}

				var description = await GetDescription(rmsg, images, replayText, false, BlueSkyService.GetBaseDescriptionPrompt(images.Existst ? images.Images.First() : null));

				await publisher.BlueSkyPost(description, images.Images, resVideos);
				try
				{
					await _telegramService.SendMessage("✅ Post BlueSky success!", rmsg.MessageId);
				}
				catch { }
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
			return TgHandler(update, rmsg, ct, PublicTelegramChanel.CHANEL_ID, typeof(PublicTelegramChanel), publisher, force);
		}

		private Task<bool> TgPrivateHandler(Update update, Message rmsg, CancellationToken ct, SocialPublicationFacade publisher, bool force = false)
		{
			return TgHandler(update, rmsg, ct, PrivateTelegramChanel.CHANEL_ID, typeof(PrivateTelegramChanel), publisher, force);
		}

		public async Task<bool> TgHandler(Update update, Message rmsg, CancellationToken ct, long chanelId
			, Type socialBaseService, SocialPublicationFacade publisher, bool force = false)
		{
			var serviceName = socialBaseService.Name;

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

				string basePrompt = "";
				var firstImage = images.Existst ? images.Images.First() : null;

				// Сравниваем переданный тип с конкретными типами классов
				if (socialBaseService == typeof(PublicTelegramChanel))
				{
					basePrompt = PublicTelegramChanel.GetBaseDescriptionPrompt(firstImage);
				}
				else if (socialBaseService == typeof(PrivateTelegramChanel))
				{
					basePrompt = PrivateTelegramChanel.GetBaseDescriptionPrompt(firstImage);
				}

				var description = await GetDescription(rmsg, images, replayText, false, basePrompt);

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

		private async Task<string> GetDescription(Message rmsg, TelegramService.ImagesTelegram images, string replayText
			, bool force = false, string prompt = null)
		{
			string description = string.IsNullOrEmpty(replayText) ? images.Caption : replayText;

			if (force)
			{
				replayText = description = null;
			}

			if (string.IsNullOrEmpty(description) || force)
			{
				description = await _aiFacade.TryCreateDescription(replayText, images.Images, prompt);
				_telegramService.UpdateCaptionMediaGrup(rmsg, description);
			}

			return description;
		}

		public async Task GenerateImageByText(Update update, CancellationToken ct)
		{
			var imagesList = await _aiFacade.GenerateImage(update.Message.ReplyToMessage.Text, 2);
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
