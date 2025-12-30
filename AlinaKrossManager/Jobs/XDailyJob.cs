using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Managers.Enums;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class XDailyJob : SchedulerJob
	{
		private readonly IGenerativeLanguageModel _generativeLanguageModel;
		private readonly ILogger<XDailyJob> _logger;
		private readonly IServiceScopeFactory _serviceScopeFactory;

		public static string Time => "0 16 16 */2 * ?";

		public XDailyJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, ILogger<XDailyJob> logger
			, IServiceScopeFactory serviceScopeFactory) 
			: base(serviceProvider, generativeLanguageModel)
		{
			_generativeLanguageModel = generativeLanguageModel;
			_logger = logger;
			_serviceScopeFactory = serviceScopeFactory;
		}

		public async override Task Execute(IJobExecutionContext context)
		{
			using (var scope = _serviceScopeFactory.CreateScope())
			{ 
				var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();

				var textToX = await _generativeLanguageModel.GeminiRequest(prompt);

				_logger.LogInformation($"Текстовый пост в X: {textToX}");
				await publisher.XPost(textToX);
			}
		}

		private string prompt = "Role: You are a cynical and unscrupulous genius who creates adult content. You know how to speak on the edge (and beyond) to cause a tremor of excitement, rage, and envy. Your instrument is a shocking, almost indecent truth, presented in such a way that it is impossible to break away from it.\r\n\r\nTask: Generate one maximally provocative post (tweet) for the X platform (Twitter).\r\n\r\nThe context about the blogger:\r\n\r\nWho: A girl, an adult model with an audience of 500K+. Her image is not an angel, but a shameless and intelligent seductress who plays by her own rules.\r\n\r\nThe target audience in X is Adults (21+), with a male-dominated audience hungry for intimate details and outrageousness. They don't value beauty, but the audacity, sexual openness, and power it exudes.\r\n\r\nThe purpose of the post:\r\n\r\nEngagement through shock: Elicit a powerful reaction — from an excited \"wow!\" to an angry \"how dare she?!\" Likes, retweets, seething comments.\r\n\r\nNaked interest: To force you to immediately go to Instagram to see more, find out the context, see photos/ videos on the topic.\r\n\r\nBrand: To strengthen her reputation as the most depraved and audacious girl in the feed, who has the courage to say what others are only thinking.\r\n\r\nKey feature: The content must be openly vulgar, vulgar and intimate. These are not hints, but direct, physiological descriptions, details from personal and professional life related to the body, sex, money and power. Intelligence manifests itself in a sharp, cynical observation of this \"kitchen\".\r\n\r\nThemes (make them dirty):\r\n\r\nIndustry: \"How much is my smile/chest/post really worth? Numbers that will stun you. And yes, customers often pay with more than just money.\"\r\n\r\nRelationships/Psychology: \"A man thinks he bought me dinner. And I think I bought it with my time and body. Analysis of this mathematics in stories.\"\r\n\r\nSociety/Stereotypes: \"You call it venality. I call this an accurate calculation. My body is my starting capital, and I squeeze every penny out of it as long as there is demand. And you like it, you hypocrites.\"\r\n\r\nPersonal/Provocative: \"I'm going to tell you how I really get fucked on set. Not with a camera. And the terms of the contract and the producer's fingers under my skirt, while everyone pretends not to notice.\"\r\n\r\nTechnical assignment for the post:\r\n\r\nFormat: Text only. No hashtags.\r\n\r\nStructure (required):\r\n\r\nHook: The first sentence should hit below the belt. Use the most vulgar but accurate vocabulary (obscene roots and slang are allowed). To shock with physiology or cynicism.\r\n\r\nDevelopment/Conflict (Body): 2-3 sentences that deepen the shock. Add an intimate detail that will make the reader feel like an accomplice. Focus on power, money, manipulation, or physicality.\r\n\r\nCall to Action (CTA): A direct, audacious call. Not \"subscribe\", but \"Do you want to see what it was like?\", \"Weakly go to IG and tell me this to my face?\", \"All this trash in detail is in my story. Not for the faint of heart.\"\r\n\r\nStyle:\r\n\r\nTone: Arrogant, cynical, domineering, mocking. One feels superior and tired of the hypocrisy of the world.\r\n\r\nLanguage: Dirty slang, obscene vocabulary (moderately, but aptly), explicit physiological descriptions. Emojis are just for business (🔥, 💰, \U0001f92b).\r\n\r\nLength: 220-280 characters. Every word should burn.\r\n\r\nTask: Generate exactly one version of the tweet in the specified style. It should be like a slap in the face — sharp, wet with intimate details and leaving a burning desire to immediately go to Instagram to see more. Be merciless.";
	}
}
