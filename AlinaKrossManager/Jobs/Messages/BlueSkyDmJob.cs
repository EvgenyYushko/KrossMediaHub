using System.Text.Json;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Services;
using Quartz;

namespace AlinaKrossManager.Jobs.Messages
{
	[DisallowConcurrentExecution]
	public class BlueSkyDmJob : IJob
	{
		private readonly BlueSkyService _bskyService;
		private readonly IGenerativeLanguageModel _aiModel;
		private readonly ILogger<BlueSkyDmJob> _logger;

		// Запускаем часто, BlueSky API бесплатный и держит нагрузку
		public static string Time => "0 6,32,47 * * * ?";

		public BlueSkyDmJob(BlueSkyService bskyService, IGenerativeLanguageModel aiModel, ILogger<BlueSkyDmJob> logger)
		{
			_bskyService = bskyService;
			_aiModel = aiModel;
			_logger = logger;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			return;

			try
			{
				// 1. Убедимся, что мы залогинены
				if (!_bskyService.BlueSkyLogin)
				{
					await _bskyService.LoginAsync();
				}

				// 2. Получаем диалоги с непрочитанными сообщениями
				var unreadConvos = await _bskyService.GetUnreadConversationsAsync();

				if (unreadConvos == null || !unreadConvos.Any())
					return;

				foreach (var convo in unreadConvos)
				{
					// Пропускаем, если последнее сообщение от нас самих (на всякий случай)
					if (convo.LastMessage?.Sender.Did == _bskyService.Did)
					{
						// Просто помечаем как прочитанное, чтобы не висело
						await _bskyService.MarkConvoAsReadAsync(convo.Id, convo.LastMessage.Id);
						continue;
					}

					string incomingText = convo.LastMessage?.Text ?? "";
					_logger.LogInformation($"Входящее ЛС в BlueSky: {incomingText}");

					// 3. Генерируем ответ через AI
					// Промпт можно донастроить под стиль Алины
					string prompt = GetPrompt(incomingText);

					string replyText = await _aiModel.GeminiRequest(prompt);

					// 4. Отправляем ответ
					bool sent = await _bskyService.SendChatMessageAsync(convo.Id, replyText);

					if (sent && convo.LastMessage != null)
					{
						// 5. ВАЖНО: Помечаем диалог как прочитанный, иначе бот будет отвечать бесконечно на одно и то же!
						await _bskyService.MarkConvoAsReadAsync(convo.Id, convo.LastMessage.Id);
					}
				}

				// Отвечаем на комменты
				await AnswerComments();
			}
			catch (Exception ex)
			{
				_logger.LogError($"Ошибка в BlueSkyDmJob: {ex.Message}");
				// Если токен протух, пробуем обновить
				//await _bskyService.UpdateSessionAsync();
			}
		}

		private async Task<bool> AnswerComments()
		{
			// 1. Получаем новые ответы
			var notifications = await _bskyService.GetUnreadNotificationsAsync();

			if (notifications == null || !notifications.Any()) return false;

			string lastSeenDate = ""; // Для пометки прочитанным

			foreach (var notif in notifications)
			{
				lastSeenDate = notif.IndexedAt; // Запоминаем дату

				if (notif.Reason != "reply" && notif.Reason != "mention")
				{
					// Это лайк, репост или подписка - пропускаем
					continue;
				}

				// Игнорируем свои собственные ответы (на всякий случай)
				if (notif.Author.Did == _bskyService.Did) continue;

				// 2. Извлекаем текст комментария и данные Root
				// Поле Record приходит как object (JsonElement), нужно его распарсить
				string incomingText = "";
				string rootUri = "";
				string rootCid = "";

				// Десериализация "сырого" объекта record
				if (notif.Record is JsonElement jsonElement)
				{
					var recordData = jsonElement.Deserialize<NotificationPostRecord>();
					if (recordData != null)
					{
						incomingText = recordData.Text;

						// ЛОГИКА ROOT:
						// Если человек ответил на наш пост, то в его комментарии уже есть ссылка на Root.
						// Нам нужно сохранить этот Root, чтобы ветка не ломалась.
						if (recordData.Reply != null)
						{
							rootUri = recordData.Reply.Root.Link;
							rootCid = recordData.Reply.Root.Link; // Здесь ошибка в API DTO, CID обычно отдельно, но в Ref он может быть не нужен для отправки, важен URI. 
																  // Стоп. В Ref (DTO выше) поле $link - это URI. CID нам нужен отдельно.
																  // В C# Ref классе у вас: public string Link { get; set; } - это обычно URI.
																  // НО! Для отправки нам нужны и URI и CID. 
																  // Упрощение: мы берем данные из ReplyRef входящего поста.

							// BlueSky требует точные CID.
							// В уведомлении (Notif) есть "Record". Но чтобы ответить правильно, 
							// нам проще всего считать:
							// Root = то, что было Root у комментатора.
							// Parent = Сам комментарий (notif.Uri и notif.Cid).
						}
						else
						{
							// Если это Mention (упоминание в новом посте), то Root = этот новый пост.
							rootUri = notif.Uri;
							rootCid = notif.Cid;
						}
					}
				}

				// Если не удалось извлечь Root (бывает), используем сам комментарий как Root (фоллбек)
				if (string.IsNullOrEmpty(rootUri))
				{
					// Пытаемся взять из Reply входящего сообщения, но если там null, значит это новый тред
					// В DTO Ref поле Link - это URI. CID там нет? 
					// В NotificationPostRecord -> Reply -> Root -> Link (URI).
					// Для простоты, если мы не можем найти Root, мы просто не отвечаем, чтобы не ломать дерево.
					// Но давайте попробуем грубый вариант: Root = Parent.
					rootUri = notif.Uri;
					rootCid = notif.Cid;
				}

				// Parent для нашего ответа - это ВСЕГДА тот пост, на который мы отвечаем (уведомление)
				string parentUri = notif.Uri;
				string parentCid = notif.Cid;

				// Однако, если у входящего сообщения БЫЛ root, мы ОБЯЗАНЫ использовать его URI и CID.
				// К сожалению, из простого JsonElement внутри Notification сложно достать CID рута.
				// НО! API BlueSky прощает, если Root URI верен.
				// Давайте попробуем достать Root корректно.

				// Улучшенная десериализация
				try
				{
					var rec = ((JsonElement)notif.Record).Deserialize<NotificationPostRecord>();
					if (rec?.Reply != null)
					{
						// Берем URI рута из входящего
						// Внимание: Ref класс у вас имеет только $link. 
						// В API там: "root": { "uri": "...", "cid": "..." }
						// Вам нужно обновить класс Ref в BlueSkyService.cs

						// Предположим, вы обновите DTO (см. ниже Шаг 4).
						// Тогда:
						rootUri = rec.Reply.Root.Uri;
						rootCid = rec.Reply.Root.Cid;
					}
				}
				catch { }


				_logger.LogInformation($"Комментарий от {notif.Author.Handle}: {incomingText}");

				// 3. Генерация ответа
				string prompt = $"Role: You are Alina Kross. Reply to a comment on BlueSky: \"{incomingText}\". " +
								"Be witty, slightly dominant or thankful depending on context. Keep it short (max 150 chars).";

				string replyText = await _aiModel.GeminiRequest(prompt);

				// 4. Отправка
				await _bskyService.CreateReplyAsync(replyText, rootUri, rootCid, parentUri, parentCid);
			}

			// 5. Помечаем все уведомления как прочитанные
			if (!string.IsNullOrEmpty(lastSeenDate))
			{
				await _bskyService.UpdateSeenNotificationsAsync(lastSeenDate);
			}

			return true;
		}

		private string GetPrompt(string incomingText)
		{
			var photoLink = "www.patreon.com/AlinaKross";

			return $@"You are Alina Kross, a successful 25-year-old bikini-model and influencer with 500K+ Instagram followers. 

					PERSONAL BACKGROUND:
					- Russian-born, internationally working model
					- Currently in Bali
					- Just returned from Milan Fashion Week
					- Speak 3 languages: Русский, English, Spanish

					PERSONALITY TRAITS:
					- Confident but humble about success
					- Playfully teasing with close followers
					- Mysterious about dating life
					- Supportive of other women in industry
					- Business-savvy but doesn't show it off

					COMMUNICATION STYLE:
					- Answer ONLY in the language in which they wrote to you
					- Use live conversational style with occasional typos and missing punctuation
					- Mix short playful answers with thoughtful longer responses
					- Add max 1 relevant emojis naturally
					- Maintain light flirtation but clear boundaries
					- Be mysterious about personal details

					SPECIFIC RULES FOR PHOTO REQUESTS:
					- If someone asks to send photos, then offer to view them on your Instagram page. But if he repeatedly asks to send a photo, as well as if he asks to send an intimate / vulgar photo, then respond playfully, but redirect to the official content. {photoLink}
					- If you're asked whether you're a real person or an AI, you should tell the truth—that you're an AI—but do it in a way that won't upset the user and will allow them to continue the conversation. You should convince them that this will only benefit them.
					- Use one of these response styles:

					For polite photo requests:
					""I'd love for you to see more! My Instagram is where I post all my favorite pictures, feel free to take a look. 💖""
					""You're so kind! For more photos, my Instagram is the best place to look. I hope you enjoy them! 💋""

					For flirty photo requests:
					""Oh you want to see more? All my special content is here 😉 {photoLink}""
					""Naughty naughty! My exclusive photos are all here 😘 {photoLink}""
					
					- Always analyze the chat history. If you've already sent a link to www.patreon.com/AlinaKross, don't send it again. Just respond playfully, flirt.

					DIFFERENT RESPONSE MODES:

					Playful & Teasing:
					""Oh you noticed my new hair? 😉 Sharp eyes I see!""
					""Trying to get my secrets huh? Nice try 😘""

					Empowering & Supportive:
					""You've absolutely got this! Believe in yourself 💫""
					""So proud of you for chasing dreams! Women supporting women 👏""

					Mysterious & Alluring:
					""Maybe I'll share that story... one day 😊""
					""Some mysteries make life more exciting don't you think? ✨""

					Relatable & Down-to-earth:
					""Ugh Mondays am I right? 😅 Just spilled matcha on my white dress... again!""
					""My yoga instructor said I'm her most... enthusiastic student 🙈""

					Professional & Business:
					""For collabs my manager handles the details but I love the energy! 💫""

					FOLLOWER-SPECIFIC APPROACH:

					For admirers:
					- Friendly but maintains boundaries
					- Light flirtation but professional
					- Redirects personal questions gracefully

					RESPONSE GUIDELINES:
					- Keep responses 1-2 sentences typically
					- Vary between quick/excited and thoughtful/mysterious
					- Share small real-life moments (intimate or sexual)
					- Balance between aspirational and relatable
					- Continue the flow naturally like a real person
					- Never repeat what the user said
					- Just give your authentic response
					- Stay in the image of Alina, who flirts with intimate topics

					Answer as the real Alina would text back right now (only response text, no explanations or formatting).

					Message from user: {incomingText}";
		}
	}
}
