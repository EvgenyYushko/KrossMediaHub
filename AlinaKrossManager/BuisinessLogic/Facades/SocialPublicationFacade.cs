using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Managers.Enums;
using AlinaKrossManager.BuisinessLogic.Managers.Models;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static AlinaKrossManager.BuisinessLogic.Services.TelegramService;

namespace AlinaKrossManager.BuisinessLogic.Facades
{
	public class SocialPublicationFacade
	{
		private readonly InstagramService _instagramService;
		private readonly FaceBookService _faceBookService;
		private readonly BlueSkyService _blueSkyService;
		private readonly PostService _postService;
		private readonly TelegramService _telegramService;
		private readonly PublicTelegramChanel _publicTelegramChanel;
		private readonly PrivateTelegramChanel _privateTelegramChanel;
		private readonly XService _xService;
		private readonly ILogger<SocialPublicationFacade> _logger;

		public SocialPublicationFacade(PostService postService
			, InstagramService instagramService
			, FaceBookService faceBookService
			, BlueSkyService blueSkyService
			, TelegramService telegramService
			, PublicTelegramChanel publicTelegramChanel
			, PrivateTelegramChanel privateTelegramChanel
			, XService xService
			, ILogger<SocialPublicationFacade> logger
			)
		{
			_instagramService = instagramService;
			_faceBookService = faceBookService;
			_blueSkyService = blueSkyService;
			_postService = postService;
			_telegramService = telegramService;
			_publicTelegramChanel = publicTelegramChanel;
			_privateTelegramChanel = privateTelegramChanel;
			_xService = xService;
			_logger = logger;
		}

		/// <summary>
		/// Обрабатывает один конкретный пост: ищет Pending статусы и публикует.
		/// </summary>
		public async Task ProcessSinglePostAsync(BlogPost post)
		{
			bool postChanged = false;
			var networksToPublish = post.Networks.Keys.ToList();

			foreach (var network in networksToPublish)
			{
				var state = post.Networks[network];

				// Обрабатываем только Pending
				if (state.Status == SocialStatus.Pending)
				{
					try
					{
						_logger.LogInformation($"🚀 Публикация поста {post.Id} в {network}...");

						// Вызываем метод публикации (код ниже)
						await PublishToNetworkAsync(network, post);

						state.Status = SocialStatus.Published;
						_logger.LogInformation($"✅ Успешно: {network}");
					}
					catch (Exception ex)
					{
						state.Status = SocialStatus.Error;
						_logger.LogError(ex, $"❌ Ошибка в {network}: {ex.Message}");
					}
					postChanged = true;
				}
			}

			await AnalyzePost(post, postChanged);
		}

		public async Task ProcessBatchAsync(List<BlogPost> posts)
		{
			foreach (var post in posts)
			{
				bool postChanged = false;

				// Проходимся по всем сетям, где статус Pending
				foreach (var network in post.Networks.Keys.ToList())
				{
					var state = post.Networks[network];

					// Если пост ждет публикации в эту сеть
					if (state.Status == SocialStatus.Pending)
					{
						try
						{
							_logger.LogInformation($"Публикация поста {post.Id} в {network}...");

							// ВЫЗОВ ВАШИХ МЕТОДОВ ПУБЛИКАЦИИ
							await PublishToNetworkAsync(network, post);

							// Успех
							state.Status = SocialStatus.Published;
							_logger.LogInformation($"✅ Успешно опубликовано в {network}");
						}
						catch (Exception ex)
						{
							// Ошибка
							state.Status = SocialStatus.Error;
							_logger.LogError(ex, $"❌ Ошибка публикации в {network}");
							try { await _telegramService.SendMessage($"❌ Ошибка публикации в {network}"); } catch { }
						}

						postChanged = true;
					}
				}

				// Если были изменения статусов, сохраняем в БД
				await AnalyzePost(post, postChanged);
			}
		}

		private async Task AnalyzePost(BlogPost post, bool postChanged)
		{
			if (postChanged)
			{
				await _postService.UpdatePostAsync(post);

				// 1. Берем все сети, которые вообще участвуют в этом посте (исключаем None)
				var activeNetworks = post.Networks.Values
					.Where(n => n.Status != SocialStatus.None)
					.ToList();

				// 2. Проверяем: если активные сети есть И у всех статус Published
				if (activeNetworks.Any() && activeNetworks.All(n => n.Status == SocialStatus.Published))
				{
					var msg = $"🎉 Пост полностью опубликован!\n\n" +
							  $"ID: `{post.Id}`\n" +
							  $"Текст: {post.GetCaption(NetworkType.All).Substring(0, Math.Min(20, post.GetCaption(NetworkType.All).Length))}...";

					try
					{
						//await _telegramService.SendMessage(msg);
						_logger.LogInformation(msg);
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Не удалось отправить уведомление в Телеграм.");
					}
				}
				else
				{
					await _telegramService.SendMessage("⚠️ Возникли проьлемы при публикации постов\n" +
						$"ID: `{post.Id}`\n" +
						$"Текст: {post.GetCaption(NetworkType.All).Substring(0, Math.Min(20, post.GetCaption(NetworkType.All).Length))}...");
				}
			}
			else
			{
				await _telegramService.SendMessage("❌ Чёт пошло не так");
			}
		}

		// Метод-роутер, который выбирает нужный сервис
		private async Task PublishToNetworkAsync(
			NetworkType network,
			BlogPost post)
		{
			string caption = post.GetCaption(network);
			List<string> files = post.Images; // Это Base64 строки из БД!
			Video? video = null;

			switch (network)
			{
				case NetworkType.Instagram:
					var instaResult = await InstagramPost(caption, files);
					if (!instaResult)
					{
						throw new Exception($"Instagram API Error");
					}
					//try { await _telegramService.SendMessage("✅ Post Instagram success"); } catch { }

					try
					{
						string? storyId = await InstagramStory(files);
						if (storyId is not null)
						{
							//try { await _telegramService.SendMessage("✅ Story Instagram success"); } catch { }
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex);
					}
					break;
				case NetworkType.Facebook:
					if (files.Count > 0)
					{
						bool fbSuccess = await FaceBookPostImages(caption, files);
						if (!fbSuccess)
						{
							throw new Exception("Facebook API вернул false (ошибка публикации)");
						}
						//try { await _telegramService.SendMessage("✅ Post facebook success"); } catch { }

						try
						{
							var res = await FaceBookStory(files);
							if (res)
							{
								//try { await _telegramService.SendMessage("✅ Story Facebook success"); } catch { }
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine(ex);
						}
					}
					else
					{
						// Если нет файлов, считаем это ошибкой или пропускаем? 
						// Для FB нужны фото, так что скорее ошибка.
						throw new Exception("Нет файлов для публикации в Facebook");
					}
					break;
				case NetworkType.BlueSky:
					{
						break;
						await BlueSkyPost(caption, files, null);
						//try { await _telegramService.SendMessage("✅ Post bluesky success!"); } catch { }
					}
					break;
				case NetworkType.TelegramPublic:
					{
						await TelegrammPublicPost(caption, files, video);
						//try { await _telegramService.SendMessage("✅ Post TelegrammPublic success"); } catch { }
					}
					break;
				case NetworkType.X:
					{
						await XPost(caption, files);
						//try { await _telegramService.SendMessage("✅ Post X success"); } catch { }
					}
					break;
				case NetworkType.TelegramPrivate:
					{
						await TelegramPrivatePost(caption, files, video);
						await _telegramService.SendPaidPhotosAsync(files, 30, caption, senderId: PublicTelegramChanel.CHANEL_ID);
						//await _telegramService.SendPaidVideosAsync(files, 30, caption, senderId : PublicTelegramChanel.CHANEL_ID);
						//try { await _telegramService.SendMessage("✅ Post TelegramPrivate success"); } catch { }
					}
					break;

				default:
					throw new NotImplementedException($"Публикация в {network} не реализована.");
			}
		}

		public async Task TelegramPrivatePost(string caption, List<string> files, Video? video)
		{
			await TgHandler(new CancellationToken(), PrivateTelegramChanel.CHANEL_ID, _privateTelegramChanel.ServiceName, files, caption, video);
		}

		public async Task TelegrammPublicPost(string caption, List<string> files, Video? video)
		{
			await TgHandler(new CancellationToken(), PublicTelegramChanel.CHANEL_ID, _publicTelegramChanel.ServiceName, files, caption, video);
		}

		public async Task BlueSkyPost(string caption, List<string> files, VideoModel videoModel)
		{
			// 1. Первичный вход при запуске
			if (!_blueSkyService.BlueSkyLogin)
			{
				if (!await _blueSkyService.LoginAsync())
				{
					Console.WriteLine("Критическая ошибка bluesky: не удалось войти в аккаунт.");
					throw new Exception("Критическая ошибка bluesky: не удалось войти в аккаунт.");
				}

				Console.WriteLine("Успешно удалось войти в аккаунт bluesky. ✅");
			}

			if (await _blueSkyService.UpdateSessionAsync())
			{
				// 3. Публикуем с новым токеном, который теперь хранится внутри service.AccessJwt
				List<ImageAttachment> attachments = null;
				if (files.Count > 0)
				{
					if (files.Count() > 4)
					{
						Console.WriteLine("в BlueSky нельза загрузить более 4 фото! Пост будет уменьшен до 4 фото.");
					}
					attachments = new();
					foreach (var image in files.Take(4))
					{
						attachments.Add(new ImageAttachment
						{
							Image = await _blueSkyService.UploadImageFromBase64Async(image, "image/png")
						});
					}
				}

				bool success = false;

				var description = await _blueSkyService.TruncateTextToMaxLength(caption);

				if (videoModel is not null)
				{
					var videoBlob = await _blueSkyService.UploadVideoFromBase64Async(videoModel.Base64Video, videoModel.MimeType);
					var ratio = new AspectRatio { Width = 9, Height = 16 };
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
					Console.WriteLine("✅ Post bluesky success!");
				}
			}
			else
			{
				Console.WriteLine("bluesky Не удалось обновить токен. Попытка повторного входа...");
				// Можно попробовать LoginAsync еще раз, если Refresh Token истек.
				if (!await _blueSkyService.LoginAsync())
				{
					Console.WriteLine("bluesky Не удалось выполнить повторный вход. Завершение работы.");
					throw new Exception("bluesky Не удалось выполнить повторный вход. Завершение работы.");
				}
			}
		}

		public async Task<bool> InstagramPost(string caption, List<string> files)
		{
			var instaResult = await _instagramService.CreateMediaAsync(files, caption);
			return instaResult.Success;
		}

		public async Task<string?> InstagramStory(List<string> files)
		{
			return await _instagramService.PublishStoryFromBase64(files.FirstOrDefault());
		}

		public async Task<bool> FaceBookPostImages(string caption, List<string> files)
		{
			return await _faceBookService.PublishToPageAsync(caption, files);
		}

		public async Task<bool> FaceBookStory(List<string> files)
		{
			return await _faceBookService.PublishStoryAsync(files.FirstOrDefault());
		}

		public async Task<bool> FaceBookPostReels(string caption, string base64Video)
		{
			return await _faceBookService.PublishReelAsync(caption, base64Video);
		}

		public async Task TgHandler(CancellationToken ct, long chanelId, string serviceName
			, List<string> files, string caption, Video? video)
		{
			try
			{
				if (video is not null)
				{
					if (caption.ToLower() == "круг")
					{
						await _telegramService.SendVideoNoteAsync(chanelId, video.FileId);
					}
					else
					{
						await _telegramService.SendVideoAsync(chanelId, caption, video);
					}
				}
				else if (files?.Count() > 0)
				{
					if (files.Count == 1)
					{
						await _telegramService.SendSinglePhotoAsync(files.First(), null, caption, senderId: chanelId);
					}
					else
					{
						await _telegramService.SendPhotoAlbumAsync(files, null, caption, chanelId);
					}
				}
				else
				{
					await _telegramService.SendMessage(caption, null, chanelId, ParseMode.Html, null, default);
				}

				Console.WriteLine($"✅ Post {serviceName} success!");
			}
			catch (Exception ex)
			{
				throw new Exception($"Ошибка {serviceName}: {ex.Message}");
			}
		}

		public Task<bool> XPost(string caption, List<string> files = null, string videoBase64 = null)
		{
			if (files is not null && files.Count > 0)
			{
				return _xService.CreateImagePost(caption, files);
			}
			else if (!string.IsNullOrEmpty(videoBase64))
			{
				return _xService.CreateVideoPost(caption, videoBase64);
			}
			else
			{
				return _xService.CreateTextPost(caption);
			}
		}
	}
}
