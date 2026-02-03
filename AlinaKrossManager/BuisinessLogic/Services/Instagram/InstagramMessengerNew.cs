using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Protos.GoogleGeminiService;
using static AlinaKrossManager.BuisinessLogic.Services.Instagram.InstagramService;

namespace AlinaKrossManager.BuisinessLogic.Services.Instagram
{
	public static class MediaMessageStorage
	{
		public static ConcurrentDictionary<string, List<MediaDataEntry>> Storage = new();
	}

	public class MediaDataEntry
	{
		public string Url { get; set; }
		public string AiResult { get; set; } // Распознанный текст
		public string MediaType { get; set; }
		public bool IsProcessed { get; set; }    // Флаг: false - только ссылка, true - текст готов
	}

	public partial class InstagramService
	{
		// Вместо списка игнора используем Очередь диалогов.
		// Это наш "Snapshot" текущего состояния.
		private static Queue<ConversationItem> _conversationQueue = new Queue<ConversationItem>();

		public async Task ProcessNextUnreadMessageAsync()
		{
			try
			{
				Console.WriteLine($"[{DateTime.Now}] Запуск обработчика...");

				// 1. ПРОВЕРКА / ЗАПОЛНЕНИЕ ОЧЕРЕДИ
				// Если очередь пуста, значит мы прошли полный круг (или это первый запуск).
				// Нужно сделать новый "снимок" диалогов из API.
				if (_conversationQueue.Count == 0)
				{
					Console.WriteLine("Очередь пуста. Запрашиваем свежий список диалогов из API...");

					var freshConversations = await GetRecentConversationsAsync();

					// Фильтруем и заполняем очередь
					int addedCount = 0;
					foreach (var convo in freshConversations)
					{
						// Сразу отсекаем старые диалоги, чтобы не засорять очередь
						if (IsRecent(convo.UpdatedTime, hours: 24))
						{
							_conversationQueue.Enqueue(convo);
							addedCount++;
						}
					}

					Console.WriteLine($"Сформирована новая очередь из {addedCount} диалогов.");

					if (addedCount == 0)
					{
						Console.WriteLine("Нет активных диалогов за последние 24 часа. Ждем следующего таймера.");
						return;
					}
				}

				// 2. ОБРАБОТКА ОЧЕРЕДИ
				// Мы будем доставать диалоги из очереди ПО ОДНОМУ, пока не найдем тот, 
				// которому нужно ответить, ИЛИ пока очередь не кончится.
				while (_conversationQueue.Count > 0)
				{
					// Достаем следующий диалог (и удаляем его из очереди)
					var convo = _conversationQueue.Dequeue();

					Console.WriteLine($"Проверяем диалог {convo.Id}. Осталось в очереди: {_conversationQueue.Count}");

					// Получаем историю сообщений
					var messages = await GetConversationMessagesAsync(convo.Id, limit: 10);

					if (messages == null || messages.Count == 0) continue; // Диалог пуст, берем следующий из while

					var lastMsg = messages[0];
					string senderId = lastMsg.From.Id;

					// Если последнее сообщение от НАС (бота) -> пропускаем.
					// ВНИМАНИЕ: Мы просто идем на следующий виток while. 
					// Мы не выходим из метода (return), мы ищем дальше в ЭТОЙ ЖЕ итерации таймера,
					// пока не найдем кому ответить.
					if (senderId == _alinaKrossId) continue;

					// --- ЕСЛИ МЫ ЗДЕСЬ, ЗНАЧИТ НУЖНО ОТВЕЧАТЬ ---

					// Формирование контекста (код без изменений)
					// ==============================================================
					int unreadCount = 0;
					foreach (var msg in messages) { if (msg.From.Id != _alinaKrossId) unreadCount++; else break; }

					await MarkMessageAsSeenAsync(senderId);

					//await Task.Delay(2000);

					await SetTypingStatusAsync(senderId);

					var chatHistory = new List<ChatMessage>();
					var unreadUserMessageIds = new List<string>(); // Для реакций

					// Идем по списку с конца (от старых) к началу (к новым), чтобы сохранить хронологию
					for (int i = messages.Count - 1; i >= 0; i--)
					{
						var msg = messages[i];

						// 1. Получаем текстовое содержание (с учетом кэша, фото, видео)
						string content = await ResolveMessageContentAsync(msg);

						// 2. Определяем роль для AI (model - это бот, user - это пользователь)
						string role = (msg.From.Id == _alinaKrossId) ? "model" : "user";

						// 3. Добавляем в историю в формате объектов
						chatHistory.Add(new ChatMessage
						{
							Role = role,
							Text = string.IsNullOrEmpty(content) ? "[Empty message]" : content
						});

						// 4. Логика для Реакций:
						// Проверяем, является ли сообщение непрочитанным (по индексу unreadCount) и от пользователя
						// (unreadCount вычисляется перед этим циклом, как в прошлом коде)
						bool isUnread = i < unreadCount;
						if (isUnread && role == "user")
						{
							unreadUserMessageIds.Add(msg.Id);
						}
					}

					// ========================================================================
					// ЛОГИКА ОТПРАВКИ РЕАКЦИИ (Random)
					// ========================================================================
					var random = new Random();

					// Если есть непрочитанные сообщения от юзера и выпал шанс (например > 50 из 100)
					if (unreadUserMessageIds.Count > 0 && random.Next(1, 101) > 50)
					{
						// Выбираем случайное сообщение из списка непрочитанных
						string targetMessageId = unreadUserMessageIds[random.Next(unreadUserMessageIds.Count)];

						// Отправляем реакцию (без await, чтобы не задерживать процесс, или с await для надежности)
						await SendReactionAsync(senderId, targetMessageId); // Например "love" или рандом

						// Небольшая пауза для реалистичности перед тем как "печатать"
						await Task.Delay(1500);
					}

					// ========================================================================

					// 1. Показываем статус "Печатает..."
					await SetTypingStatusAsync(senderId);

					// Логируем для отладки (опционально, в консоль можно вывести JSON или просто кол-во)
					Console.WriteLine($"--- CONTEXT FOR AI ({chatHistory.Count} msgs) ---");
					//Console.WriteLine(JsonSerializer.Serialize(chatHistory)); // Если нужно видеть структуру

					// 2. Генерация ответа
					// Теперь передаем список объектов, а не строку
					try
					{
						string replyText = await GenerateAiResponse(chatHistory);
						await SendLongMessageAsHumanAsync(senderId, replyText);

						Console.WriteLine($"Ответ отправлен пользователю {senderId}.");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Ошибка отправки ответа пользаку {senderId} в инсте: {ex.Message}");
					}

					// ВАЖНО: Мы ответили ОДНОМУ человеку.
					// Делаем return, чтобы выйти из метода и освободить ресурсы до следующего тика таймера (через 5 мин).
					// Очередь _conversationQueue сохранит свое состояние в памяти.
					return;
				}

				// Если while закончился, а мы так и не сделали return,
				// значит мы перебрали всю очередь, и никому отвечать не надо было.
				Console.WriteLine("Очередь разобрана полностью, отвечать некому. В следующем запуске загрузим новый список.");

				// В следующий раз _conversationQueue.Count будет 0, и мы загрузим новый список из API.
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка в ProcessNextUnreadMessageAsync: {ex.Message}");
			}
		}

		private async Task<string> ResolveMessageContentAsync(MessageItem msg)
		{
			string userContent = null;
			string messageId = msg.Id;

			// 1. Проверка КЭША (Webhooks)
			if (MediaMessageStorage.Storage.TryGetValue(messageId, out var mediaList) && mediaList.Any())
			{
				var media = mediaList.First();
				if (media.IsProcessed)
				{
					return media.AiResult; // Вернули готовое
				}
				else
				{
					// Есть ссылка, но не обработано. Обрабатываем сейчас.
					return await ProcessAndCacheMediaAsync(media, messageId);
				}
			}

			// 2. Если в кэше пусто, смотрим поля API
			if (string.IsNullOrEmpty(userContent))
			{
				// А. Текст
				if (!string.IsNullOrEmpty(msg.Text))
				{
					return msg.Text;
				}
				// Б. Вложения API (Фото/Видео)
				else if (msg.Attachments?.Data != null && msg.Attachments.Data.Count > 0)
				{
					var attachment = msg.Attachments.Data[0];
					MediaDataEntry newEntry = null;

					if (attachment.VideoData != null && !string.IsNullOrEmpty(attachment.VideoData.Url))
					{
						newEntry = new MediaDataEntry { Url = attachment.VideoData.Url, MediaType = "video", IsProcessed = false };
					}
					else if (attachment.ImageData != null && !string.IsNullOrEmpty(attachment.ImageData.Url))
					{
						newEntry = new MediaDataEntry { Url = attachment.ImageData.Url, MediaType = "image", IsProcessed = false };
					}

					if (newEntry != null)
					{
						// Анализируем и сохраняем в кэш
						string aiResult = await ProcessAndCacheMediaAsync(newEntry, messageId);

						// Сохраняем в static storage, чтобы в следующий раз брать готовое
						newEntry.AiResult = aiResult;
						newEntry.IsProcessed = true;
						MediaMessageStorage.Storage.TryAdd(messageId, new List<MediaDataEntry> { newEntry });

						return aiResult;
					}
				}
				// В. Unsupported (Аудио/Стикеры без кэша)
				else if (msg.IsUnsupported)
				{
					return "[The user sent a sticker, reaction, or audio]";
				}
			}

			return "[Empty message]";
		}

		public async Task<string> ProcessAndCacheMediaAsync(MediaDataEntry media, string messageId)
		{
			string resultText = "";
			try
			{
				switch (media.MediaType)
				{
					case "audio":
						{
							var base64 = await DownloadAudioFileAsBase64(media.Url);
							resultText = $"[voice message]: {await ProcessAudioMessage(base64)}";
						}
						break;
					case "image":
						{
							var base64 = await DownloadImageAsBase64(media.Url);
							resultText = $"[Photo]: {await AnalyzeImageAsync(base64, "photo")}";
						}
						break;
					case "video":
						{
							var base64 = await DownloadImageAsBase64(media.Url);
							resultText = $"[Video]: {await AnalyzeImageAsync(base64, "video")}";
						}
						break;
					default:
						resultText = $"[Медиа: {media.MediaType}]";
						break;
				}

				// Записываем результат в сам объект
				media.AiResult = resultText;
				media.IsProcessed = true;

				// Если это объект из КЭША, он там уже лежит по ссылке, изменения отразятся сразу.
				// Если это новый объект из API, вызывающий код сам добавит его в словарь.
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка обработки медиа ({media.MediaType}): {ex.Message}");
				resultText = $"[Failed to process {media.MediaType}]";
			}

			return resultText;
		}

		public async Task SendLongMessageAsHumanAsync(string userId, string fullText)
		{
			// 1. Разбиваем текст на части (например, по ~200 символов или по предложениям)
			var chunks = SplitMessageIntoHumanChunks(fullText, 100);

			for (int i = 0; i < chunks.Count; i++)
			{
				await SetTypingStatusAsync(userId);

				var chunk = chunks[i];

				// 3. Рассчитываем паузу для ТЕКУЩЕГО куска
				// Чем короче кусок, тем быстрее мы его "печатаем"
				int typingTime = Math.Clamp(chunk.Length * 70, 1500, 5000);
				await Task.Delay(typingTime);

				await SendInstagramMessage(userId, chunk);

				// 5. Маленькая пауза между отправкой и началом печати следующего (как будто человек нажал Enter)
				if (i < chunks.Count - 1)
				{
					await Task.Delay(Random.Shared.Next(500, 1200));
				}
			}

			if (chunks.Count == 1)
			{
				var random = new Random();

				// Если выпадает число от 1 до 3 (из 10), то отправляем стикер. Шанс 30%.
				if (random.Next(1, 11) <= 3)
				{
					await SetTypingStatusAsync(userId);

					// Небольшая задержка перед стикером, чтобы выглядело естественно (1-3 сек)
					await Task.Delay(random.Next(1000, 3000));

					string stickerToSend;

					if (random.Next(1, 101) > 10)
					{
						stickerToSend = "like_heart";
					}
					else
					{
						// Берем случайный URL из нашей коллекции
						int index = random.Next(StickerCollection.Urls.Count);
						stickerToSend = StickerCollection.Urls[index];
					}

					await SendSticker(userId, stickerToSend);
				}
			}
		}

		private List<string> SplitMessageIntoHumanChunks(string text, int maxChunkLength)
		{
			var chunks = new List<string>();
			if (string.IsNullOrEmpty(text)) return chunks;

			// 1. Сначала разбиваем по переносам строк (абзацам)
			var paragraphs = text.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

			foreach (var paragraph in paragraphs)
			{
				// Если абзац короткий, добавляем его как есть
				if (paragraph.Length <= maxChunkLength)
				{
					chunks.Add(paragraph.Trim());
					continue;
				}

				// 2. Если абзац длинный, бьем его на предложения
				// Используем регулярку, чтобы оставить знаки препинания (.!?) на месте
				var sentences = System.Text.RegularExpressions.Regex.Split(paragraph, @"(?<=[.!?])\s+");

				var currentChunk = "";

				foreach (var sentence in sentences)
				{
					// Если текущий кусок + новое предложение влезают в лимит — склеиваем
					if ((currentChunk.Length + sentence.Length) <= maxChunkLength)
					{
						currentChunk += (currentChunk.Length > 0 ? " " : "") + sentence;
					}
					else
					{
						// Если не влезают — сохраняем текущий кусок и начинаем новый
						if (!string.IsNullOrEmpty(currentChunk)) chunks.Add(currentChunk.Trim());
						currentChunk = sentence;
					}
				}

				// Добавляем хвостик
				if (!string.IsNullOrEmpty(currentChunk)) chunks.Add(currentChunk.Trim());
			}

			return chunks;
		}

		private async Task<List<MessageItem>> GetConversationMessagesAsync(string conversationId, int limit)
		{
			// Запрашиваем messages с лимитом
			var fields = $"messages.limit({limit}){{from,message,created_time,attachments,is_unsupported}}";
			var url = $"v19.0/{conversationId}?fields={fields}&access_token={_accessToken}";

			var response = await _https.GetAsync(url);
			if (!response.IsSuccessStatusCode) return new List<MessageItem>();

			var json = await response.Content.ReadAsStringAsync();
			var convoData = JsonSerializer.Deserialize<ConversationMessagesResponse>(json);

			return convoData?.Messages?.Data ?? new List<MessageItem>();
		}

		private async Task<List<ConversationItem>> GetRecentConversationsAsync()
		{
			// Берем список диалогов. platform=instagram обязательно.
			var url = $"v19.0/me/conversations?platform=instagram&access_token={_accessToken}";

			var response = await _https.GetAsync(url);
			if (!response.IsSuccessStatusCode) return new List<ConversationItem>();

			var json = await response.Content.ReadAsStringAsync();
			var result = JsonSerializer.Deserialize<ConversationsResponse>(json);

			return result?.Data ?? new List<ConversationItem>();
		}

		public async Task SendInstagramMessage(string recipientId, string text)
		{
			var url = $"v19.0/me/messages?access_token={_accessToken}";

			var payload = new
			{
				recipient = new { id = recipientId },
				message = new { text }
			};

			var json = JsonSerializer.Serialize(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			var response = await _https.PostAsync(url, content);

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine("Сообщение успешно отправлено.");
			}
			else
			{
				var error = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"Ошибка отправки: {error}");
			}
		}

		// --- Утилиты ---
		private bool IsRecent(string timeString, int hours)
		{
			if (DateTime.TryParse(timeString, out DateTime dt))
			{
				return dt > DateTime.UtcNow.AddHours(-hours);
			}
			return false;
		}

		private async Task<string> GenerateAiResponse(List<ChatMessage> chatHistory)
		{
			var systemInstruction = await GetMainSystemPromptModel();

			return await _generativeLanguageModel.RequestWithChatAsync(chatHistory, systemInstruction);
		}
	}

	public class ConversationsResponse
	{
		[JsonPropertyName("data")]
		public List<ConversationItem> Data { get; set; }

		[JsonPropertyName("paging")]
		public Paging Paging { get; set; }
	}

	public class ConversationItem
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("updated_time")]
		public string UpdatedTime { get; set; }
	}

	public class ConversationMessagesResponse
	{
		[JsonPropertyName("messages")]
		public MessageDataWrapper Messages { get; set; }

		[JsonPropertyName("id")]
		public string Id { get; set; }
	}

	public class MessageDataWrapper
	{
		[JsonPropertyName("data")]
		public List<MessageItem> Data { get; set; }
	}

	public class MessageItem
	{
		[JsonPropertyName("id")]
		public string Id { get; set; }

		[JsonPropertyName("created_time")]
		public string CreatedTime { get; set; }

		[JsonPropertyName("from")]
		public InstagramUser From { get; set; }

		[JsonPropertyName("to")]
		public MessageToWrapper To { get; set; }

		[JsonPropertyName("message")]
		public string Text { get; set; }

		[JsonPropertyName("attachments")]
		public AttachmentDataWrapper Attachments { get; set; }

		[JsonPropertyName("is_unsupported")]
		public bool IsUnsupported { get; set; }
	}

	public class AttachmentDataWrapper
	{
		[JsonPropertyName("data")]
		public List<MessageAttachment> Data { get; set; }
	}

	public class MessageAttachment
	{
		[JsonPropertyName("image_data")]
		public InstagramMediaData ImageData { get; set; }

		[JsonPropertyName("video_data")]
		public InstagramMediaData VideoData { get; set; }
	}

	public class InstagramMediaData
	{
		[JsonPropertyName("url")]
		public string Url { get; set; }

		[JsonPropertyName("preview_url")]
		public string PreviewUrl { get; set; } // Полезно для превью видео
	}

	public class MessageToWrapper
	{
		[JsonPropertyName("data")]
		public List<InstagramUser> Data { get; set; }
	}

	public static class StickerCollection
	{
		// Список URL стикеров (GIF или PNG с прозрачным фоном)
		public static readonly List<string> Urls = new List<string>
		{
			"https://media.giphy.com/media/c76IJLufpNwSULPk77/giphy.gif", // Heart
			"https://media.giphy.com/media/M9NbzZjAcxq9jS9LZJ/giphy.gif", // Thumbs up
			"https://media.giphy.com/media/26gsjCZpPolPr3sBy/giphy.gif", // Wow
			"https://media.giphy.com/media/3o6UB3VhArvomJHtdK/giphy.gif", // Laugh
			//"https://media.giphy.com/media/3o7TKr3nzbh5WgCFxe/giphy.gif", // Love
			"https://media.giphy.com/media/3o7abKhOpu0NwenH3O/giphy.gif", // Cheers
			"https://media.giphy.com/media/l2R0eYcNq9rJUsVAA/giphy.gif",  // Confetti
			//"https://media.giphy.com/media/xT8qB7Sbwskk27Rdy8/giphy.gif", // Fire
		};
	}
}
