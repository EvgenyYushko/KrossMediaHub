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
					var instaResult = await _instagramService.CreateMediaAsync(files, caption);
					if (!instaResult.Success)
					{
						throw new Exception($"Instagram API Error: {instaResult.ErrorMessage ?? "Unknown error"}");
					}
					Console.WriteLine($"✅ Post insta success!");

					try
					{
						var storyId = await _instagramService.PublishStoryFromBase64(files.FirstOrDefault());
						if (storyId is not null)
						{
							Console.WriteLine("✅ Story insta success!");
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
						bool fbSuccess = await _faceBookService.PublishToPageAsync(caption, files);
						if (!fbSuccess)
						{
							throw new Exception("Facebook API вернул false (ошибка публикации)");
						}
						Console.WriteLine($"✅ Post facebook success!");

						try
						{
							var res = await _faceBookService.PublishStoryAsync(files.FirstOrDefault());
							if (res)
							{
								Console.WriteLine("✅ Story Facebook success");
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
						// 1. Первичный вход при запуске
						if (!_blueSkyService.BlueSkyLogin && !await _blueSkyService.LoginAsync())
						{
							throw new Exception("BlueSky: не удалось войти в аккаунт.");
						}

						Console.WriteLine("Успешно удалось войти в аккаунт bluesky. ✅");
						_blueSkyService.BlueSkyLogin = true;

						if (await _blueSkyService.UpdateSessionAsync())
						{
							// 3. Публикуем с новым токеном, который теперь хранится внутри service.AccessJwt
							List<ImageAttachment> attachments = null;
							if (files.Count() > 0)
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

							bool bsSuccess = false;

							var description = await _blueSkyService.TruncateTextToMaxLength(caption);

							if (attachments is not null)
							{
								bsSuccess = await _blueSkyService.CreatePostWithImagesAsync(description, attachments);
							}
							else
							{
								bsSuccess = await _blueSkyService.CreatePostAsync(description);
							}

							if (!bsSuccess)
							{
								throw new Exception("BlueSky: CreatePost вернул false");
							}
							Console.WriteLine($"✅ Post bluesky success!");
						}
						else
						{
							throw new Exception("BlueSky: Не удалось обновить сессию");
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

		public async Task TgHandler(CancellationToken ct, long chanelId, SocialBaseService socialBaseService, List<string> files, string caption)
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
				throw new Exception($"Ошибка {serviceName}: {ex.Message}");
			}
		}
	}
}
