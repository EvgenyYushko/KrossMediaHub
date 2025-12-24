using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Managers.Enums;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class PostToPrivateFromQueueJob : SchedulerJob
	{
		public static string Time => "0 20 15 */2 * ?";

		private readonly ILogger<PostToPublicFromQueueJob> _logger;
		private readonly IServiceScopeFactory _serviceScopeFactory;

		public PostToPrivateFromQueueJob(IServiceProvider serviceProvider
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
			using (var scope = _serviceScopeFactory.CreateScope())
			{
				var postService = scope.ServiceProvider.GetRequiredService<PostService>();
				var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();

				var privatePosts = await postService.GetPendingPostsAsync(AccessLevel.Private, 1);
				if (privatePosts.Any())
				{
					_logger.LogInformation($"Найдено {privatePosts.Count} приватных постов к отправке.");
					await publisher.ProcessBatchAsync(privatePosts);
				}
			}
		}
	}
}
