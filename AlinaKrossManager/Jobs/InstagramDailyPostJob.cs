using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Helpers;
using AlinaKrossManager.Jobs.Base;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	public class InstagramDailyPostJob : SchedulerJob
	{
		public const string Time = "0 0 * * * ?";

		private readonly InstagramService _instagramService;

		public InstagramDailyPostJob(IServiceProvider serviceProvider
			, InstagramService instagramService
		)
			: base(serviceProvider)
		{
			_instagramService = instagramService;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			await _instagramService.SendInstagramAdminMessage($"Hello form google cloude console, now {TimeZoneHelper.DateTimeNow}");
		}
	}
}
