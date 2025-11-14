using Quartz;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace AlinaKrossManager.Jobs.Base
{
	public abstract class SchedulerJob : IJob
	{
		private readonly IServiceProvider _serviceProvider;

		protected SchedulerJob(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider;
		}

		public async virtual Task Execute(IJobExecutionContext context)
		{
			
		}
	}
}
