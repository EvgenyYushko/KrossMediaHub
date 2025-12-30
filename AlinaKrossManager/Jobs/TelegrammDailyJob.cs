using AlinaKrossManager.BuisinessLogic.Facades;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class TelegrammDailyJob : SchedulerJob
	{
		public static string Time => "0 25 20 1-31/2 * ?";

		private readonly ILogger<TelegrammDailyJob> _logger;
		private readonly IServiceScopeFactory _serviceScopeFactory;

		public TelegrammDailyJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, ILogger<TelegrammDailyJob> logger
			, IServiceScopeFactory serviceScopeFactory
			)
			: base(serviceProvider, generativeLanguageModel)
		{
			_logger = logger;
			_serviceScopeFactory = serviceScopeFactory;
		}

		public async override Task Execute(IJobExecutionContext context)
		{
			try
			{
				using (var scope = _serviceScopeFactory.CreateScope())
				{
					var publisher = scope.ServiceProvider.GetRequiredService<SocialPublicationFacade>();

					var textToTg = await _generativeLanguageModel.GeminiRequest(prompt);

					_logger.LogInformation($"Текстовый пост в TG: {textToTg}");
					await publisher.TelegrammPublicPost(textToTg, null, null);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message);
			}
		}

		private string prompt = "Role: You are a technically impeccable and audacious copywriter who creates intimate, provocative content for the Telegram platform. You masterfully use HTML tags for formatting, which is the most stable and recommended method.\r\n\r\nTask: Generate one explicit post for Alina Cross's Telegram channel using correct HTML, which will process parse_mode=\"HTML\" without errors.\r\n\r\nThe context about the blogger:\r\n\r\nWho: Alina Cross. Her content is about power, sex, psychology, and cynical honesty without embellishment.\r\n\r\nPlatform: Telegram channel for a loyal audience hungry for exclusivity.\r\n\r\nThe goal: To shock, engage, provoke a heated discussion in the comments and strengthen the image of a domineering and conscious woman.\r\n\r\nKey Features: An emphasis on personal, sexual experience, the psychology of control, power, and physicality. Use bright, evocative emojis to enhance emotions (3-5 for the entire text).\r\n\r\nTERMS OF REFERENCE (HTML):\r\n\r\nUse only these tags.:\r\n\r\n<b>Bold text</b>\r\n\r\n<i>Italics</i>\r\n\r\n<u>Underlined text</u>\r\n\r\n<s>Crossed-out text</s>\r\n\r\n<tg-spoiler>Hidden text (spoiler alert)</tg-spoiler>\r\n\r\nLine breaks: Separate paragraphs with real line breaks (Enter). Do not use \\n or <br> characters in the final text.\r\n\r\nEscaping: Only escape HTML special characters in plain text: <→<, > →>;, & → &amp;.\r\n\r\nLength: The post should be compact, capacious and bold. Optimal volume: 4-7 lines, including hook, body, spoiler, output, and CTA.\r\n\r\nTHE STRUCTURE OF THE POST (strictly observe):\r\n\r\nHook: The first line is an impertinent question or statement in the <b> tag. Add 1-2 relevant emojis (for example, 🔥, 👁️, \U0001f975).\r\n\r\nBody: 2-3 short paragraphs separated by line breaks. Use <i>, <u>, <s> for emphasis. Be sure to embed one <tg-spoiler> tag with the most explicit, intimate, or provocative detail. The spoiler should be shorter than two lines.\r\n\r\nConclusion and CTA: A powerful, summarizing phrase in the <b> tag. The next line is a provocative question to discuss in the comments. Add 1 emoji.\r\n\r\nTopics of the posts: Sex, power, psychology of desire, personal revelations, analysis of stereotypes, cynical observations about the industry and relationships.\r\n\r\nFINAL INSTRUCTIONS:\r\nYour response should contain only the finished HTML text of the post, which fully meets all the requirements above. Don't add explanations, examples, or backtracking apostrophes. Generate a post based on the most explicit topic in sex.";
	}
}
