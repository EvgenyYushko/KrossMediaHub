using AlinaKrossManager.Services;

namespace AlinaKrossManager.BuisinessLogic.Services.Base
{
	public abstract class SocialBaseService
	{
		protected readonly IGenerativeLanguageModel _generativeLanguageModel;

		protected SocialBaseService(IGenerativeLanguageModel generativeLanguageModel)
		{
			_generativeLanguageModel = generativeLanguageModel;
		}

		public abstract string ServiceName { get; }
	}
}
