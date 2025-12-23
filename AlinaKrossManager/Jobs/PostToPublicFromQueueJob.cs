using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Managers.Enums;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class PostToPublicFromQueueJob : SchedulerJob
	{
		public static string Time => "0 51 11 * * ?";

		private readonly ILogger<PostToPublicFromQueueJob> _logger;
		private readonly IServiceScopeFactory _serviceScopeFactory;

		public PostToPublicFromQueueJob(IServiceProvider serviceProvider
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

				var publicPosts = await postService.GetPendingPostsAsync(AccessLevel.Public, 1);
				if (publicPosts.Any())
				{
					_logger.LogInformation($"Найдено {publicPosts.Count} публичных постов к отправке.");
					await publisher.ProcessBatchAsync(publicPosts);
				}
			}
		}
	}
}
