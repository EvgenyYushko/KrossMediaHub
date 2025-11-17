using AlinaKrossManager.Services;
using Quartz;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace AlinaKrossManager.Jobs.Base
{
	public abstract class SchedulerJob : IJob
	{
		private readonly IServiceProvider _serviceProvider;
		protected readonly IGenerativeLanguageModel _generativeLanguageModel;

		protected SchedulerJob(IServiceProvider serviceProvider, IGenerativeLanguageModel generativeLanguageModel)
		{
			_serviceProvider = serviceProvider;
			_generativeLanguageModel = generativeLanguageModel;
		}

		public async virtual Task Execute(IJobExecutionContext context)
		{
			
		}
	}
}
