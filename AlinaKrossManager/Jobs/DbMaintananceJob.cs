using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Managers.Enums;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Microsoft.Extensions.Logging;
using Quartz;
using Telegram.Bot.Types.Enums;
using static AlinaKrossManager.Helpers.TelegramQueueHelper;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class DbMaintananceJob : SchedulerJob
	{
		public static string Time => "0 43 16 * * ?";

		private readonly ILogger<PostToPublicFromQueueJob> _logger;
		private readonly IServiceScopeFactory _serviceScopeFactory;

		public DbMaintananceJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, ILogger<PostToPublicFromQueueJob> logger
			, IServiceScopeFactory serviceScopeFactory
			)
			: base(serviceProvider, generativeLanguageModel)
		{
			_logger = logger;
			_serviceScopeFactory = serviceScopeFactory;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			try
			{
				using (var scope = _serviceScopeFactory.CreateScope())
				{
					var postService = scope.ServiceProvider.GetRequiredService<PostService>();
					var telegramService = scope.ServiceProvider.GetRequiredService<TelegramService>();

					// 1. ОЧИСТКА СТАРЫХ ПОСТОВ (И Public, и Private)
					var oldPublicPosts = await postService.GetOldPublishedPostsAsync(AccessLevel.Public);
					if (oldPublicPosts.Any())
					{
						foreach (var post in oldPublicPosts)
						{
							await postService.DeletePostAsync(post.Id);
						}
					}

					var oldPrivatePosts = await postService.GetOldPublishedPostsAsync(AccessLevel.Private);
					if (oldPrivatePosts.Any())
					{
						foreach (var post in oldPrivatePosts)
						{
							await postService.DeletePostAsync(post.Id);
						}
					}

					// 2. СБОР СТАТИСТИКИ
					var statsPublic = await postService.GetPostCountsAsync(AccessLevel.Public);

					var statsPrivate = await postService.GetPostCountsAsync(AccessLevel.Private);

					// 3. ФОРМИРОВАНИЕ ОТЧЕТА
					var sb = new System.Text.StringBuilder();
					sb.AppendLine("🧹 **Ежедневный отчет и очистка**");
					sb.AppendLine($"Удалено старых постов: {oldPublicPosts.Count + oldPrivatePosts.Count}");
					sb.AppendLine("-----------------------------");

					NewMethod(AccessLevel.Public, statsPublic, sb);
					sb.AppendLine();
					NewMethod(AccessLevel.Private, statsPrivate, sb);

					await telegramService.SendMessage(sb.ToString(), parseMode: ParseMode.Markdown);
				}
			}
			catch (Exception ex)
			{
				_logger.LogInformation(ex.ToString());
			}
		}
	}
}
