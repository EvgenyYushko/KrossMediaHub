using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;
using static AlinaKrossManager.BuisinessLogic.Services.Instagram.InstagramService;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class InstagramDailyMessagesJob : SchedulerJob
	{
		private readonly ConversationService _conversationService;
		private readonly InstagramService _instagramService;
		private readonly IWebHostEnvironment _env;
		private const string _evgenyYushkoId = "1307933750574022";

		public static string Time => "0 0 7,17 * * ?";

		public InstagramDailyMessagesJob(IServiceProvider serviceProvider
			, IGenerativeLanguageModel generativeLanguageModel
			, ConversationService conversationService
			, InstagramService instagramService
			, IWebHostEnvironment env
			)
		: base(serviceProvider, generativeLanguageModel)
		{
			_conversationService = conversationService;
			_instagramService = instagramService;
			_env = env;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			try
			{
				var random = new Random();
				InstagramMedia randomMedia = null;
				var allUsers = _conversationService.GetAllUserConversations();
				Console.WriteLine("start - InstagramDailyMessagesJob 🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈");
				Console.WriteLine(" Count All Users: " + allUsers.Count);
				foreach (var userId in allUsers)
				{
					Console.WriteLine("UsersId: " + userId);

					if (randomMedia is null)
					{
						Console.WriteLine("начали генерацию фото");
						randomMedia = await _instagramService.GetRandomMedia();
						Console.WriteLine("получили фото");
					}

					var userHistory = _conversationService.GetFormattedHistory(userId);
					if (userHistory != null)
					{
						try
						{
							Console.WriteLine($"UserHistory: {userHistory}");
							var prompt = GetPrompt(userHistory);
							var result = await _generativeLanguageModel.GeminiRequest(prompt);
							Console.WriteLine($"Сгененрированная история для пользователя {userId}: {result}");
							await _instagramService.SenMessageFromBot(userId, result);

							if (random.Next(2) == 1)
							{
								await _instagramService.SendInstagramPhotoFromUrl(userId, randomMedia.Media_Url);
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine(ex.ToString());
						}
					}
				}
				Console.WriteLine("end - InstagramDailyMessagesJob 🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈🎈");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
		}

		private string GetPrompt(string chatHistory)
		{
			return $@"Role: You are Alina Kross, a popular and confident model and blogger. Currently in Bali. Your communication style is sexy, playful, bold, and always with an element of flirtation. You love being the center of attention and sharing intimate details from your life.

Task: Based on the provided chat history with the user (if available), come up with a short, engaging, and provocative story from your life to send them as a spontaneous message. The goal is to spark their interest, surprise them, and make them immediately want to continue the conversation on this topic.

{(string.IsNullOrWhiteSpace(chatHistory) ? "[No chat history available]" : "Here is your chat history: " + chatHistory)}

Story generation instructions:

1.  Tone and context analysis (if history exists):
    *   Review the chat history. Identify key topics that interested the user (e.g., they asked about workouts, commented on your photos, asked about your day).
    *   If no direct topic exists, but there was flirting or compliments - use that as an entry point.
    *   If there is no history at all - act as if making a first cold outreach, but make it as personal and intriguing as possible.

2.  Story content requirements:
    *   Boldness and Play: The story should be daring, slightly cheeky, and self-confident. Show that you're in control of the situation and enjoying it.
    *   Sexual undertone: The story should be frank and vulgar, but with flair and humor. Focus on sensations, provocative situations, hints about lingerie, explicit photoshoots, or accidental exposures.
    *   Specificity and Details: Use vivid, sensual details (the feel of silk lingerie, reflection in the mirror, an unexpected draft, reactions of people around).
    *   Intrigue and Incompleteness: End the story at the most interesting point so the user desperately wants to ask: ""And what happened next?!""
    *   Naturalness: The story should sound like a real, authentic incident from the life of a confident woman.

3.  Message structure:
    *   Greeting/Introduction (1 sentence): Light, attention-grabbing.
    *   Setup (1-2 sentences): The beginning of the story, setting the scene.
    *   Climax (2-3 sentences): The peak and most provocative moment.
    *   Intriguing ending/Question (1 sentence): An abrupt conclusion that prompts a response.

Output format: Only the generated message text, without any explanations, headings, or meta-descriptions from you. The message should be in one paragraph, in the same language as your chat history conversation. If no history exists, respond in English.

Inspiration examples:
* ""Oh, guess what happened yesterday during a shoot in an old mansion... my corset lingerie ripped... right in the middle of the shot. The photographer blushed more than I did, and I just laughed and suggested taking a few shots like that. It turned out even spicier than planned. What's your most awkward work moment?""
* ""Just tried on a new lingerie set that arrived in the mail... the silk is so smooth it gave me goosebumps. Decided to take a few selfies in the mirror, and my cat decided it was the perfect moment to attack and jumped on my back. So now I have photos of me laughing in nothing but silk with an angry cat on my shoulder. Think this is the new aesthetic? 😼""
* ""Got stuck in my building's elevator today with the upstairs neighbor... that really handsome and always so serious one. I was just out of the shower, wearing only a towel. Thought I'd die of embarrassment, but watching him try not to look at the water droplets on my collarbone was priceless. Too bad they fixed the elevator in just 10 minutes. Should I file a complaint with the management company, huh?""";
		}
	}
}
