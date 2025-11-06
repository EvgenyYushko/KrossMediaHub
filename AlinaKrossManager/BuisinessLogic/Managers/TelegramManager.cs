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

		public async Task HandleUpdateAsync(Update update, CancellationToken ct)
		{
			if (update.Message?.Text is not { } text)
			{
				_telegramService.HandleMediaGroup(update.Message);
				return;
			}

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

						var startMsg = await _telegramService.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации...");

						try
						{
							List<string> images = await _telegramService.TryGetIMagesPromTelegram(update, rmsg);
							var resVideos = await _telegramService.TryGetVideoBase64FromTelegram(rmsg);
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
										await _telegramService.SendMessage(update.Message.Chat.Id, msgRes, rmsg.MessageId);
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
							try { await _telegramService.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_facebook") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;

						var replayText = rmsg.GetMsgText() ?? "";
						var resVideos = await _telegramService.TryGetVideoBase64FromTelegram(rmsg);
						List<string> images = await _telegramService.TryGetIMagesPromTelegram(update, rmsg);
						if (images.Count == 0 && string.IsNullOrEmpty(replayText) && resVideos.base64Video is null)
						{
							return;
						}

						var startMsg = await _telegramService.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации...");
						try
						{
							bool success = false;
							if (images.Count > 0)
							{
								success = await _faceBookService.PublishToPageAsync(replayText, images);
							}
							else if (resVideos.base64Video is not null)
							{
								success = await _faceBookService.PublishReelAsync(replayText, resVideos.base64Video);
							}

							if (success)
							{
								var msgRes = $"✅ Пост успешно создан!";
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
							Console.WriteLine($"Ошибка: {ex.Message}");
						}
						finally
						{
							try { await _telegramService.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
						}
					}
					break;
				case UpdateType.Message when msgText.IsCommand("post_to_insta") && update.Message.ReplyToMessage is Message rmsg:
					{
						if (!await _telegramService.CanUseBot(update, ct)) return;

						var replayText = rmsg.GetMsgText();
						List<string> images = await _telegramService.TryGetIMagesPromTelegram(update, rmsg);
						if (images.Count == 0)
						{
							return;
						}

						string description = "";
						var startMsg = await _telegramService.SendMessage(update.Message.Chat.Id, "Начинаем процесс публикации...");
						try
						{
							var promptForeDescriptionPost = "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в инстаграм под постом с фотографией. " +
								$"А так же придумай не более 15 хештогов, они должны соответствовать " +
								$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
								$"Вот само изображение: {images.FirstOrDefault()}" +
								$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
								$"без всякого рода ковычек и экранирования. " +
								$"Пример ответа: Golden hour glow ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence #neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";

							description = replayText ?? await _generativeLanguageModel.GeminiRequest(promptForeDescriptionPost);
							try
							{
								await _telegramService.SendMessage(update.Message.Chat.Id, $"{description}", rmsg.MessageId);
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
									await _telegramService.SendMessage(update.Message.Chat.Id, msgRes, rmsg.MessageId);
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
							try { await _telegramService.DeleteMessage(update.Message.Chat.Id, startMsg.MessageId, ct); } catch { }
						}
					}

					break;
			}
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
