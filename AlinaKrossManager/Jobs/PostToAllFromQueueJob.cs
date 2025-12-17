using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Telegram;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;
using static AlinaKrossManager.BuisinessLogic.Managers.TelegramManager;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class PostToAllFromQueueJob : SchedulerJob
	{
		public static string Time => "0 46 17 * * ?";

		private readonly InstagramService _instagramService;
		private readonly FaceBookService _faceBookService;
		private readonly BlueSkyService _blueSkyService;
		private readonly PostService _postService;
		private readonly TelegramService _telegramService;
		private readonly PublicTelegramChanel _publicTelegramChanel;
		private readonly PrivateTelegramChanel _privateTelegramChanel;
		private readonly ILogger<PostToAllFromQueueJob> _logger;

		public PostToAllFromQueueJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, InstagramService instagramService
			, FaceBookService faceBookService
			, BlueSkyService blueSkyService
			, PostService postService
			, TelegramService telegramService
			, PublicTelegramChanel publicTelegramChanel
			, PrivateTelegramChanel privateTelegramChanel
			, ILogger<PostToAllFromQueueJob> logger
			)
			: base(serviceProvider, generativeLanguageModel)
		{
			_instagramService = instagramService;
			_faceBookService = faceBookService;
			_blueSkyService = blueSkyService;
			_postService = postService;
			_telegramService = telegramService;
			_publicTelegramChanel = publicTelegramChanel;
			_privateTelegramChanel = privateTelegramChanel;
			_logger = logger;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			var publicPosts = await _postService.GetPendingPostsAsync(AccessLevel.Public, 1);
			if (publicPosts.Any())
			{
				_logger.LogInformation($"Найдено {publicPosts.Count} публичных постов к отправке.");
				await ProcessBatchAsync(publicPosts);
			}

			// --- 2. Обработка ПРИВАТНЫХ постов ---
			var privatePosts = await _postService.GetPendingPostsAsync(AccessLevel.Private, 1);
			if (privatePosts.Any())
			{
				_logger.LogInformation($"Найдено {privatePosts.Count} приватных постов к отправке.");
				await ProcessBatchAsync(privatePosts);
			}
		}

		private async Task ProcessBatchAsync(List<BlogPost> posts)
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
							// Можно записать текст ошибки в лог или даже в БД (если добавить поле ErrorMessage)
							_logger.LogError(ex, $"❌ Ошибка публикации в {network}");
						}

						postChanged = true;
					}
				}

				// Если были изменения статусов, сохраняем в БД
				if (postChanged)
				{
					await _postService.UpdatePostAsync(post);
				}
			}
		}

		// Метод-роутер, который выбирает нужный сервис
		private async Task PublishToNetworkAsync(
			NetworkType network,
			BlogPost post)
		{
			string caption = post.GetCaption(network);
			List<string> files = post.Images; // Это Base64 строки из БД!

			switch (network)
			{
				case NetworkType.Instagram:
					var result = await _instagramService.CreateMediaAsync(files, caption);
					if (result.Success)
					{
						var msgRes = $"✅ Post insta success!";
						Console.WriteLine(msgRes);
					}
					break;
				case NetworkType.Facebook:
					{
						bool success = false;
						if (files.Count() > 0)
						{
							success = await _faceBookService.PublishToPageAsync(caption, files);
						}

						if (success)
						{
							var msgRes = $"✅ Post facebook success!";
							Console.WriteLine(msgRes);
						}
					}
					break;
				case NetworkType.BlueSky:
					{
						// 1. Первичный вход при запуске
						if (!_blueSkyService.BlueSkyLogin)
						{
							if (!await _blueSkyService.LoginAsync())
							{
								Console.WriteLine("Критическая ошибка bluesky: не удалось войти в аккаунт.");
								break;
							}

							Console.WriteLine("Успешно удалось войти в аккаунт bluesky. ✅");
							_blueSkyService.BlueSkyLogin = true;
						}

						if (await _blueSkyService.UpdateSessionAsync())
						{
							// 3. Публикуем с новым токеном, который теперь хранится внутри service.AccessJwt
							List<ImageAttachment> attachments = null;
							if (files.Count() > 0)
							{
								attachments = new();
								foreach (var image in files)
								{
									attachments.Add(new ImageAttachment
									{
										Image = await _blueSkyService.UploadImageFromBase64Async(image, "image/png")
									});
								}
							}

							bool success = false;

							var description = await _blueSkyService.TruncateTextToMaxLength(caption);

							if (attachments is not null)
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
							}
						}
						else
						{
							Console.WriteLine("bluesky Не удалось обновить токен. Попытка повторного входа...");
							// Можно попробовать LoginAsync еще раз, если Refresh Token истек.
							if (!await _blueSkyService.LoginAsync())
							{
								Console.WriteLine("bluesky Не удалось выполнить повторный вход. Завершение работы.");
								break;
							}
						}
					}
					break;
				case NetworkType.TelegramPublic:
					{
						await TgHandler(new CancellationToken(), PublicTelegramChanel.CHANEL_ID, _publicTelegramChanel, files, caption);
					}
					break;
				case NetworkType.TelegramPrivate:
					{
						await TgHandler(new CancellationToken(), PrivateTelegramChanel.CHANEL_ID, _privateTelegramChanel, files, caption);
					}
					break;

				default:
					throw new NotImplementedException($"Публикация в {network} не реализована.");
			}
		}

		public async Task<bool> TgHandler(CancellationToken ct, long chanelId, SocialBaseService socialBaseService, List<string> files, string caption)
		{
			var serviceName = socialBaseService.ServiceName;
			try
			{
				if (files.Count() > 0)
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
					await _telegramService.SendMessage(chanelId, caption);
				}

				var msgRes = $"✅ Post {serviceName} success!";
				Console.WriteLine(msgRes);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка {serviceName}: {ex.Message}");
			}

			return false;
		}
	}
}
