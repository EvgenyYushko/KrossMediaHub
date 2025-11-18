using AlinaKrossManager.Services;
using Quartz;

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

		public abstract Task Execute(IJobExecutionContext context);
	}
}
