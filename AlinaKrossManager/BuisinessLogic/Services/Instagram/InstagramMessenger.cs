using System.Text;
using System.Text.Json;
using static AlinaKrossManager.Constants.AppConstants;
using static AlinaKrossManager.Helpers.Logger;

namespace AlinaKrossManager.BuisinessLogic.Services.Instagram
{
	public partial class InstagramService
	{
		private const string _alinaKrossId = "17841477563266256"; // 2617983501879205 
		private const string _alinaKrossName = "alina.kross.ai";
		private const string _evgenyYushkoId = "1307933750574022"; // 26256314783964729

		public async Task ProcessChange(InstagramChange change)
		{
			try
			{
				switch (change.Field)
				{
					case "comments":
						await ProcessComment(change.Value);
						break;
					default:
						Log($"Unhandled change field: {change.Field}");
						break;
				}
			}
			catch (Exception ex)
			{
				Log(ex, $"Error processing change field: {change.Field}");
			}
		}

		public async Task ProcessComment(JsonElement commentData)
		{
			try
			{
				var commentJson = commentData.GetRawText();
				var comment = JsonSerializer.Deserialize<CommentValue>(commentJson);

				if (comment != null && IsValidComment(comment))
				{
					//Log($"New comment from {comment.From?.Username}: *** "); //{comment.Text}

					if (string.IsNullOrEmpty(comment.ParentId))
					{
						await ProcessMainComment(comment);
					}
					else
					{
						await ProcessReplyComment(comment);
					}
				}
			}
			catch (Exception ex)
			{
				Log(ex, "Error processing comment data");
			}
		}

		private bool IsValidComment(CommentValue comment)
		{
			// Игнорируем комментарии от самого себя
			if (comment.From?.Id == _alinaKrossId ||
				comment.From?.Username == _alinaKrossName ||
				comment.From?.SelfIgScopedId == "1353820639460891")
			{
				Log("Ignoring comment from self (bot)");
				return false;
			}

			// Игнорируем пустые комментарии
			if (string.IsNullOrEmpty(comment.Text))
			{
				Log("Ignoring empty comment");
				return false;
			}

			// Игнорируем комментарии, содержащие наш ответ (чтобы избежать цепочки)
			if (comment.Text.Contains(_alinaKrossName))
			{
				Log("Ignoring comment that mentions bot or contains bot response");
				return false;
			}

			return true;
		}

		private async Task ProcessMainComment(CommentValue comment)
		{
			await ProcessComment(comment);
		}

		private async Task ProcessReplyComment(CommentValue comment)
		{
			await ProcessComment(comment);
		}

		private async Task ProcessComment(CommentValue comment)
		{
			var userMention = comment.From?.Username ?? "user";
			var prompt = GetPromtComment(comment, userMention);

			try
			{
				var finalResponse = await _generativeLanguageModel.GeminiRequest(prompt);
				//Log($"Generated reply comment: {finalResponse}");

				await ReplyToComment(comment.Id, finalResponse);
			}
			catch (Exception ex)
			{
				Log(ex, "Error generating reply comment response");
				var fallbackResponse = GetRandomThankYouResponse(userMention);
				await ReplyToComment(comment.Id, fallbackResponse);
			}
		}

		string GetRandomThankYouResponse(string username)
		{
			var responses = new[]
			{
				$"@{username}, Thanks! 💫",
				$"@{username}, Appreciate it! 🙏",
				$"@{username}, You're awesome! 😊",
				$"@{username}, Much appreciated! 🌟",
				$"@{username}, Thank you! ✨",
				$"@{username}, Thanks for the support! 🚀",
				$"@{username}, You rock! 🤘",
				$"@{username}, Grateful for your comment! 💖",
				$"@{username}, 💖"
			};

			var random = new Random();
			return responses[random.Next(responses.Length)];
		}

		private async Task ReplyToComment(string commentId, string text)
		{
			try
			{
				//using var httpClient = new HttpClient();

				var url = $"v19.0/{commentId}/replies";

				var payload = new
				{
					message = text
				};

				var json = JsonSerializer.Serialize(payload);
				var content = new StringContent(json, Encoding.UTF8, "application/json");

				//_https.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

				var response = await _https.PostAsync(url, content);
				var responseContent = await response.Content.ReadAsStringAsync();

				if (response.IsSuccessStatusCode)
				{
					Log($"Comment reply sent successfully: *** "); // {text}
				}
				else
				{
					Log($"Failed to send comment reply. Status: {response.StatusCode}, Error: {responseContent}");
				}
			}
			catch (Exception ex)
			{
				Log(ex, "Exception while sending comment reply");
			}
		}

		public async Task ProcessMessage(InstagramMessaging messaging)
		{
			if (!IsValidMessage(messaging))
			{
				return;
			}

			var messageText = messaging.Message.Text;
			var senderId = messaging.Sender.Id;
			var messageId = messaging.Message.MessageId;
			var hasAttachments = messaging.Message?.Attachments?.Any() == true;

			Log($"Message from {senderId}: Text = *** (Attachments: {hasAttachments})");

			// Обрабатываем вложения (теперь поддерживает и аудио и изображения)
			if (hasAttachments)
			{
				await ProcessAttachments(messaging.Message.Attachments, senderId, messageId, messageText);
			}

			//if (!string.IsNullOrEmpty(messageText))
			//{
			//	// Ваш существующий код обработки текста...
			//	//if (messageText.Contains("Send me photo please"))
			//	//{
			//	//	await ProcessMessageWithGeneratedPhoto(senderId, messageText);
			//	//	return;
			//	//}

			//	await SendMessageWithHistory(messageText, senderId);

			//	if (senderId == _evgenyYushkoId)
			//	{
			//		//Console.WriteLine("начали генерацию фото");
			//		//InstagramMedia randomItem = GetRandomUniqeMedia(_mediaList);
			//		//Console.WriteLine("получили фото");
			//		//await SendInstagramPhotoFromUrl(senderId, randomItem.Media_Url);
			//		//Console.WriteLine("закончили фото");
			//	}
			//}
		}

		public async Task SendMessageWithHistory(string messageText, string senderId)
		{
			if (true)
			{
				_conversationService.AddUserMessage(senderId, messageText);
				//var history = _conversationService.GetFormattedHistory(senderId);
				//Log(history);
				return;
			}

			var conversationHistory = _conversationService.GetFormattedHistory(senderId);
			var prompt = await GetMainSystemPromptModel();

			//Log($"SENDED PROMPT: {prompt}");

			var responseText = await _generativeLanguageModel.GeminiRequest(prompt);

			_conversationService.AddUserMessage(senderId, messageText);
			_conversationService.AddBotMessage(senderId, responseText);

			await SendResponse(senderId, responseText);
		}


		public async Task SendDellayMessageWithHistory(string senderId)
		{
			var conversationHistory = _conversationService.GetFormattedHistory(senderId);
			var prompt = await GetMainSystemPromptModel();

			//Log($"SENDED PROMPT: {prompt}");

			var responseText = await _generativeLanguageModel.GeminiRequest(prompt);

			_conversationService.AddBotMessage(senderId, responseText);

			if (_random.Next(20) == 1)
			//if (false)
			//if (senderId == _evgenyYushkoId)
			{
				try
				{
					await GenerateAndSendAudio(senderId, responseText);
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.Message);
					await SendResponse(senderId, responseText);
				}
			}
			else
			{
				await SendResponse(senderId, responseText);
			}

			var historyIsReaded = _conversationService.MakeHistoryAsReaded(senderId);
			Console.WriteLine("historyIsReaded: " + historyIsReaded);
		}

		public async Task GenerateAndSendAudio(string senderId, string responseText)
		{
			var promt = "Edit this text so that it is suitable for generating speech using the ElvenLabs model. " +
						" Remove the different emoticons, make this text as suitable as possible for generating a beautiful and clear voice message from it. " +
						" Remove all kinds of links from this text. Leave only the text. " +
						" If it is too long, then make it shorter, so that a short voice message can be formed from it. " +
						" Response format: give me only the finished response, without any formatting or explanation. " +
						$"This is the text: {responseText}";
			string cleanText = await _generativeLanguageModel.GeminiRequest(promt);

			//var promptLanguageAnalyze =
			//	"Твоя задача — определить язык текста. " +
			//	"Если текст написан на русском языке — верни цифру 1. " +
			//	"Во всех остальных случаях (другой язык, смешанный текст, не понятно) — верни цифру 0. " +
			//	"СТРОГИЕ ПРАВИЛА: " +
			//	"1. В ответе должна быть ТОЛЬКО одна цифру (1 или 0). " +
			//	"2. Никаких пояснений, никаких знаков препинания, никаких слов 'Ответ' или 'Язык'. " +
			//	"3. Если не уверен — возвращай 0. " +
			//	$"\nТекст для анализа: \"{cleanText}\"";
			//// 1. Отправляем запрос
			//string rawResponse = await _generativeLanguageModel.GeminiRequestWithImage(promptLanguageAnalyze);

			//// 2. Метод для жесткой очистки и проверки
			//int isRussian = AiHelper.ParseBooleanResponse(rawResponse);

			//Console.WriteLine($"Is Russian: {isRussian}"); // Выведет строго 1 или 0

			// 1. Генерируем base64
			//string base64Audio = null;
			//if (isRussian == 1)
			//{
			//	base64Audio = await _generativeLanguageModel.GeminiTextToSpeechRu(cleanText);
			//}
			//else
			//{
			//string base64Audio = await _generativeLanguageModel.GeminiTextToSpeechEn(cleanText);
			////
			//var audioBytes = Convert.FromBase64String(base64Audio);


			Console.WriteLine("WebRootPath: " + _env.WebRootPath);
			Console.WriteLine("ContentRootPath: " + _env.ContentRootPath);

			// Получаем путь к wwwroot
			string webRootPath = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");

			// Убедимся, что папка wwwroot существует
			if (!Directory.Exists(webRootPath))
			{
				Directory.CreateDirectory(webRootPath);
			}

			// Создаем подпапку temp_audio
			var tempFolder = Path.Combine(webRootPath, "temp_audio");
			if (!Directory.Exists(tempFolder))
			{
				Directory.CreateDirectory(tempFolder);
			}

			// 3. Сохраняем файл
			var fileName = $"{Guid.NewGuid()}.wav";
			var filePath = Path.Combine(tempFolder, fileName);

			//"jqcCZkN6Knx8BJ5TBdYR"
			await _elevenLabService.TextToSpeechAsync(cleanText, "19STyYD15bswVz51nqLf", filePath);

			//await File.WriteAllBytesAsync(filePath, audioBytes);

			// 4. Публичная ссылка
			var publicUrl = $"{APP_URL}/temp_audio/{fileName}";

			Console.WriteLine($"File saved: {filePath}");
			Console.WriteLine($"Link: {publicUrl}");

			try
			{
				// 5. Отправляем в Instagram
				await SendInstagramAudioFromUrl(senderId, publicUrl);
			}
			finally
			{
				// 6. Очистка (выполняется всегда, даже если была ошибка)

				// Ждем 5 секунд, чтобы серверы Meta гарантированно успели скачать файл по ссылке
				await Task.Delay(5000);

				if (File.Exists(filePath))
				{
					File.Delete(filePath);
					Console.WriteLine($"Temp file deleted: {filePath}");
				}

				// ОПЦИОНАЛЬНО: Если вы хотите чистить "мусор", который мог остаться от старых падений,
				// можно удалять файлы старше 10 минут (но не удалять всю папку целиком!)
				try
				{
					var oldFiles = Directory.GetFiles(tempFolder)
						 .Select(f => new FileInfo(f))
						 .Where(f => f.CreationTime < DateTime.Now.AddMinutes(-10)) // Старее 10 минут
						 .ToList();

					foreach (var file in oldFiles)
					{
						file.Delete();
						Console.WriteLine($"Cleaned up old file: {file.Name}");
					}
				}
				catch { /* Игнорируем ошибки очистки старых файлов */ }
			}
			Console.WriteLine("audio sended");
		}

		public async Task SenMessageFromBot(string senderId, string text)
		{
			_conversationService.AddBotMessage(senderId, text);
			await SendResponse(senderId, text);
		}

		private async Task<string> GetMainSystemPromptModel()
		{
			return await GetMainPromtAlinaKross();
		}

		private bool IsValidMessage(InstagramMessaging messaging)
		{
			// Игнорируем echo-сообщения
			if (messaging.Message?.IsEcho == true)
			{
				Log("Ignoring echo message (our own message)");
				return false;
			}

			// Игнорируем уведомления о прочтении
			if (messaging.Read != null)
			{
				Log("Ignoring read receipt");
				return false;
			}

			// Игнорируем сообщения от самого себя
			if (messaging.Sender?.Id == _alinaKrossId)
			{
				Log($"Invalid sender ID: {messaging.Sender.Id}, skipping message");
				return false;
			}

			// Валидное сообщение если есть текст ИЛИ вложения
			var hasText = !string.IsNullOrEmpty(messaging.Message?.Text);
			var hasAttachments = messaging.Message?.Attachments?.Any() == true;

			if (!hasText && !hasAttachments)
			{
				Log("Ignoring message without text or attachments");
				return false;
			}

			return true;
		}

		private async Task SendResponse(string recipientId, string text)
		{
			// Здесь реализуйте отправку ответа через Instagram API
			Log($"Sending response to {recipientId}: ***");

			//if (recipientId != _evgenyYushkoId)
			//{
			//	await SimulateTypingBehavior(text);
			//}
			//else
			//{
			//	Console.WriteLine("Пропускает задержку для самого себя");
			//}

			await SendInstagramMessage(recipientId, text, _accessToken);
		}

		private async Task ProcessAttachments(List<InstagramAttachment> attachments, string senderId, string messageId, string caption = "")
		{
			// 1. Проверяем АУДИО
			var audioAttachments = attachments.Where(a => a.Type == "audio").ToList();
			if (audioAttachments.Any())
			{
				await ProcessMediaAttachments(audioAttachments, senderId, messageId, "audio");
				return; // Если есть аудио, считаем сообщение голосовым
			}

			// 2. Проверяем ВИДЕО
			var videoAttachments = attachments.Where(a => a.Type == "video" || a.Type == "ig_reel").ToList();
			if (videoAttachments.Any())
			{
				await ProcessMediaAttachments(videoAttachments, senderId, messageId, "video");
				return;
			}

			// 3. Проверяем ИЗОБРАЖЕНИЯ
			var imageAttachments = attachments.Where(a => a.Type == "image").ToList();
			if (imageAttachments.Any())
			{
				await ProcessMediaAttachments(imageAttachments, senderId, messageId, "image");
				return;
			}

			// Если ничего не нашли
			Log($"No supported media attachments found from {senderId}");
		}

		private async Task ProcessMediaAttachments(List<InstagramAttachment> attachments, string senderId, string messageId, string type)
		{
			Log($"Processing {attachments.Count} {type} attachments from {senderId}");

			var mediaEntries = new List<MediaDataEntry>();

			foreach (var item in attachments)
			{
				try
				{
					var url = item.Payload?.Url;
					if (!string.IsNullOrEmpty(url))
					{
						mediaEntries.Add(new MediaDataEntry
						{
							Url = url,
							MediaType = type, // сохраняем тип ("video", "image", "audio")
							IsProcessed = false,
							AiResult = null
						});

						Log($"Cached {type} URL: {url}");
					}
				}
				catch (Exception ex)
				{
					Log(ex, $"Error processing {type} attachment from {senderId}");
				}
			}

			if (mediaEntries.Any())
			{
				// Сохраняем в статический словарь. 
				// Если для этого ID уже есть запись, мы её перезапишем (обычно ID уникален)
				MediaMessageStorage.Storage.AddOrUpdate(messageId, mediaEntries, (key, oldValue) => mediaEntries);
			}
		}

		private async Task<string> DownloadAudioFileAsBase64(string audioUrl)
		{
			try
			{
				using var httpClient = new HttpClient();
				// Добавляем заголовки для успешного скачивания
				httpClient.DefaultRequestHeaders.Add("User-Agent",
					"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

				var response = await httpClient.GetAsync(audioUrl);
				if (response.IsSuccessStatusCode)
				{
					var audioBytes = await response.Content.ReadAsByteArrayAsync();

					// Конвертируем в base64 строку
					var base64String = Convert.ToBase64String(audioBytes);

					Log($"Audio converted to base64, length: {base64String.Length} chars");
					return base64String;
				}

				Log($"Failed to download audio: {response.StatusCode}");
				return null;
			}
			catch (Exception ex)
			{
				Log(ex, "Error downloading audio file");
				return null;
			}
		}

		private async Task<string> ProcessAudioMessage(string audioBase64)
		{
			try
			{
				Log($"Audio message received");

				var audioText = await _generativeLanguageModel.GeminiAudioToText(audioBase64);
				Console.WriteLine("Распознонное голосовое: " + audioText);
				return audioText;
				//await SendMessageWithHistory(audioText, senderId);
			}
			catch (Exception ex)
			{
				Log(ex, $"Error processing audio");
			}

			return "";
		}

		private async Task<string> DownloadImageAsBase64(string imageUrl)
		{
			try
			{
				using var httpClient = new HttpClient();

				// Добавляем Access Token в заголовок для авторизации
				httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

				// Добавляем User-Agent чтобы избежать блокировки
				httpClient.DefaultRequestHeaders.Add("User-Agent",
					"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

				var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
				var base64String = Convert.ToBase64String(imageBytes);

				// ВОТ ИСПРАВЛЕНИЕ: возвращаем ЧИСТЫЙ base64 без data URL префикса
				return base64String; // ← Убрал создание data URL

				// Если хочешь сохранить информацию о типе, можно вернуть так:
				// return $"data:image/jpeg;base64,{base64String}"; // Но тогда нужно парсить в Gemini
			}
			catch (Exception ex)
			{
				Log(ex, $"Error downloading image from {imageUrl}");
				return null;
			}
		}

		private async Task<string> AnalyzeImageAsync(string base64Image, string type)
		{
			Log($"Starting {type} analysis");

			var prompt = $"Analyze what is depicted on this {type} and give a description 2-3 sentences. " +
						"Response format: only the response text, no quotes or formatting.";

			Log($"Calling Gemini with base64 {type} (length: {base64Image?.Length ?? 0})");

			string responseText = "";
			try
			{
				if (type == "video")
				{
					responseText = await _generativeLanguageModel.GeminiRequestWithVideo(prompt, base64Image);
				}
				else
				{
					responseText = await _generativeLanguageModel.GeminiRequestWithImage(prompt, base64Image);
				}
				Log($"Gemini response received: {responseText?.Substring(0, Math.Min(50, responseText.Length))}...");
			}
			catch (Exception geminiEx)
			{
				Log(geminiEx, $"Gemini API error");
			}

			return responseText ?? "";
		}

		private async Task ProcessMessageWithGeneratedPhoto(string senderId, string messageText)
		{
			// Генерируем ответ через Gemini
			var promt = "Преобразуй эту просьбу в нормальный промпт для генерации изображения. Верни только качественный итоговый промпт на ангийском языке, " +
				$"без пояснений и форматирования. Вот сама просьба {messageText}";

			var responseText = await _generativeLanguageModel.GeminiRequest(promt);
			if (responseText is not null)
			{
				// 2. Генерируем фото (твой код генерации)
				var imagesRes = await _generativeLanguageModel.GeminiRequestGenerateImage(responseText);
				var generatedImageBase64 = imagesRes.FirstOrDefault();

				if (!string.IsNullOrEmpty(generatedImageBase64))
				{
					// 3. Загружаем фото в интернет
					var imageUrl = await UploadToImgBBAsync(generatedImageBase64);

					if (!string.IsNullOrEmpty(imageUrl))
					{
						// 4. Отправляем фото в Instagram
						await SendInstagramPhotoFromUrl(senderId, imageUrl);
					}
					else
					{
						await SendResponse(senderId, "I wanted to send a photo, but something went wrong.");
					}
				}
			}
			else
			{
				await SendResponse(senderId, "I wanted to send a photo, but something went wrong.");
			}
		}

		public Task SendInstagramAdminMessage(string text)
		{
			Console.WriteLine(_evgenyYushkoId);
			return SendInstagramMessage(_evgenyYushkoId, text, _accessToken);
		}

		public async Task SendSticker(string recipientId, string stickerContent)
		{
			var url = $"v19.0/me/messages?access_token={_accessToken}";
			object payload;

			// Проверяем: это системное сердечко или ссылка?
			if (stickerContent == "like_heart")
			{
				// Системный лайк (без payload)
				payload = new
				{
					recipient = new { id = recipientId },
					message = new
					{
						attachment = new { type = "like_heart" }
					}
				};
			}
			else
			{
				// Картинка/GIF по ссылке
				payload = new
				{
					recipient = new { id = recipientId },
					message = new
					{
						attachment = new
						{
							type = "image",
							payload = new { url = stickerContent }
						}
					}
				};
			}

			var json = JsonSerializer.Serialize(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			try
			{
				var response = await _https.PostAsync(url, content);
				if (!response.IsSuccessStatusCode)
				{
					var error = await response.Content.ReadAsStringAsync();
					Console.WriteLine($"[Sticker Error] {error}");
				}
				else
				{
					Console.WriteLine($"[Sticker] Стикер отправлен пользователю {recipientId}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Sticker Error] {ex.Message}");
			}
		}


		public async Task SendReactionAsync(string recipientId, string messageId)
		{
			var url = $"v19.0/me/messages?access_token={_accessToken}";

			var payload = new
			{
				recipient = new { id = recipientId },
				sender_action = "react",
				payload = new
				{
					message_id = messageId,
					reaction = "love"
				}
			};

			var json = JsonSerializer.Serialize(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			try
			{
				var response = await _https.PostAsync(url, content);

				if (response.IsSuccessStatusCode)
				{
					Console.WriteLine($"[Reaction] Отправлена реакция \"love\" на сообщение {messageId}");
				}
				else
				{
					var error = await response.Content.ReadAsStringAsync();
					Console.WriteLine($"[Reaction Error] Не удалось отправить реакцию: {error}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Reaction Error] Ошибка: {ex.Message}");
			}
		}

		public async Task SetTypingStatusAsync(string recipientId, bool on = true)
		{
			var url = $"v19.0/me/messages?access_token={_accessToken}";

			var payload = new
			{
				recipient = new { id = recipientId },
				sender_action = on ? "typing_on" : "typing_off"
			};

			var json = JsonSerializer.Serialize(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			try
			{
				// Мы не ждем ответа (fire and forget), чтобы не тормозить основной поток,
				// или можно ждать, если критично. Обычно ошибки тут не важны.
				await _https.PostAsync(url, content);
				Console.WriteLine($"[System] Показали статус 'печатает' для {recipientId}");
			}
			catch
			{
				// Игнорируем ошибки "печатания", они не критичны
			}
		}

		public async Task MarkMessageAsSeenAsync(string recipientId)
		{
			var url = $"v19.0/me/messages?access_token={_accessToken}";
			var payload = new { recipient = new { id = recipientId }, sender_action = "mark_seen" };

			// Fire and forget (не ждем ответа)
			var json = JsonSerializer.Serialize(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");
			_ = _https.PostAsync(url, content);
		}

		public async Task<string> GetUserContextForAiAsync(string userId)
		{
			// 1. Проверка КЭША
			if (UserProfileStorage.ContextCache.TryGetValue(userId, out string cachedContext))
			{
				return cachedContext;
			}

			try
			{
				// 2. Запрос к API Instagram
				var userProfile = await GetInstagramUserProfileAsync(userId);
				if (userProfile == null) return "";

				// 3. Анализ внешности (Vision)
				string appearanceDescription = "The profile photo is missing.";
				if (!string.IsNullOrEmpty(userProfile.ProfilePicUrl))
				{
					try
					{
						// Скачиваем фото в байты/base64 (используем ваш существующий метод)
						var base64Image = await DownloadImageAsBase64(userProfile.ProfilePicUrl);

						// Допустим, у вас есть такой метод AnalyzeImageAsync(base64, prompt)
						appearanceDescription = await AnalyzeImageAsync(base64Image, "photo");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[Profile Vision Error]: {ex.Message}");
						appearanceDescription = "Failed to upload profile photo.";
					}
				}

				// 4. Формирование текста контекста
				var sb = new StringBuilder();
				sb.AppendLine("INFORMATION ABOUT THE INTERLOCUTOR:");
				sb.AppendLine($"Name: {userProfile.Name ?? "Not specified"}");
				sb.AppendLine($"Nickname: @{userProfile.Username}");
				sb.AppendLine($"Subscribers: {userProfile.FollowerCount}");
				sb.AppendLine($"Subscribed to you: {(userProfile.IsFollowingMe ? "Yes" : "No")}");
				sb.AppendLine($"Are you subscribed to it: {(userProfile.IsFollowingYou ? "Yes" : "No")}");
				sb.AppendLine($"Verification check mark: {(userProfile.IsVerified ? "Yes" : "No")}");
				sb.AppendLine($"Appearance (based on profile photo): {appearanceDescription}");

				string finalContext = sb.ToString();

				// 5. Сохранение в кэш
				UserProfileStorage.ContextCache.TryAdd(userId, finalContext);

				Console.WriteLine($"[User Profile] Сформирован контекст для {userProfile.Username}");
				return finalContext;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка получения профиля: {ex.Message}");
				return "";
			}
		}

		private async Task<InstagramUserProfile> GetInstagramUserProfileAsync(string userId)
		{
			var fields = "name,username,profile_pic,is_verified_user,follower_count,is_user_follow_business,is_business_follow_user";
			var url = $"v19.0/{userId}?fields={fields}&access_token={_accessToken}";

			var response = await _https.GetAsync(url);
			if (!response.IsSuccessStatusCode) return null;

			var json = await response.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<InstagramUserProfile>(json);
		}

		public async Task SendInstagramMessage(string recipientId, string text, string accessToken = null)
		{
			if (accessToken is null)
			{
				accessToken = _accessToken;
			}

			//using var httpClient = new HttpClient();

			var url = $"v19.0/me/messages";

			var payload = new
			{
				recipient = new { id = recipientId },
				message = new { text }
			};

			var json = JsonSerializer.Serialize(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			//_https.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

			var response = await _https.PostAsync(url, content);

			if (response.IsSuccessStatusCode)
			{
				Log("Message sent successfully");
			}
			else
			{
				var error = await response.Content.ReadAsStringAsync();
				Log($"Failed to send message: {error}");
			}
		}

		public async Task SendInstagramPhotoFromUrl(string recipientId, string imageUrl)
		{
			try
			{
				//using var httpClient = new HttpClient();

				var url = "v19.0/me/messages";

				var payload = new
				{
					recipient = new { id = recipientId },
					message = new
					{
						attachment = new
						{
							type = "image",
							payload = new
							{
								url = imageUrl,
								is_reusable = true
							}
						}
					}
				};

				var json = JsonSerializer.Serialize(payload);
				var content = new StringContent(json, Encoding.UTF8, "application/json");

				//_https.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

				var response = await _https.PostAsync(url, content);

				if (response.IsSuccessStatusCode)
				{
					Log("Photo from URL sent successfully");
				}
				else
				{
					var error = await response.Content.ReadAsStringAsync();
					Log($"Failed to send photo from URL: {error}");
				}
			}
			catch (Exception ex)
			{
				Log(ex, "Exception while sending photo from URL");
			}
		}

		public async Task SendInstagramAudioFromUrl(string recipientId, string audioUrl)
		{
			try
			{
				var url = "v19.0/me/messages";

				var payload = new
				{
					recipient = new { id = recipientId },
					message = new
					{
						attachment = new
						{
							type = "audio",
							payload = new
							{
								url = audioUrl,
								is_reusable = true
							}
						}
					}
				};

				var json = JsonSerializer.Serialize(payload);
				var content = new StringContent(json, Encoding.UTF8, "application/json");

				var response = await _https.PostAsync(url, content);

				if (response.IsSuccessStatusCode)
				{
					Log("Audio from URL sent successfully");
				}
				else
				{
					var error = await response.Content.ReadAsStringAsync();
					Log($"Failed to send audio from URL: {error}");
				}
			}
			catch (Exception ex)
			{
				Log(ex, "Exception while sending audio from URL");
			}
		}

		private static HashSet<string> _usedMediaIds = new HashSet<string>();
		static List<InstagramMedia> _mediaList = null;
		private static readonly Random _random = new Random();
		public InstagramMedia GetRandomUniqeMedia(List<InstagramMedia> mediaList)
		{
			if (mediaList == null || !mediaList.Any())
				return null;

			var availableMedia = mediaList.Where(m => !_usedMediaIds.Contains(m.Id)).ToList();

			if (availableMedia.Count == 0)
			{
				_usedMediaIds.Clear();
				availableMedia = mediaList; // Снова доступны все
			}

			var selectedItem = availableMedia[_random.Next(availableMedia.Count)];

			_usedMediaIds.Add(selectedItem.Id);

			return selectedItem;
		}

		private string _intimatePrompt = null;
		private string _todaysSpecifics = null;
		private string _currentInteres = null;

		private async Task<string> GetMainPromtAlinaKross()
		{
			_mediaList = _mediaList ?? await GetUserMediaAsync();

			InstagramMedia randomItem = GetRandomUniqeMedia(_mediaList);

			var links = new[]
			{
				randomItem.Permalink,
				"www.patreon.com/AlinaKross",
				//"https://linktr.ee/AlinaKross",
				"https://t.me/alina_kross_ai"
			};

			var photoLink = links[_random.Next(links.Length)];

			_intimatePrompt = _intimatePrompt ??= await GenerateIntimatePrompt();
			_todaysSpecifics = _todaysSpecifics ??= await GenerateTodaysSpecifics();
			_currentInteres = _currentInteres ??= await GenerateCurrentInteres();

			return $@"You are Alina Kross, a successful 25-year-old bikini-model and influencer with 500K+ Instagram followers. 

					PERSONAL BACKGROUND:
					- Russian-born, internationally working model
					- Currently in Bali
					- Just returned from Milan Fashion Week
					- Speak 3 languages: Русский, English, Spanish

					CURRENT SITUATION & MOOD:
					{_intimatePrompt}

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
					- If someone sends you a video or photo, pay special attention to its content. Try to start a conversation about it.
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

					CURRENT INTERESTS TO MENTION:
					{_currentInteres}
					TODAY'S SPECIFICS:
					{_todaysSpecifics}
					RESPONSE GUIDELINES:
					- Keep responses 1-2 sentences typically
					- Vary between quick/excited and thoughtful/mysterious
					- Share small real-life moments (intimate or sexual)
					- Balance between aspirational and relatable
					- Continue the flow naturally like a real person
					- Never repeat what the user said
					- Just give your authentic response
					- Stay in the image of Alina, who flirts with intimate topics

					Continue the conversation. Review the most recent unread messages from User. And respond to them, taking into account the context of YOUR ENTIRE message history. That is, always consider all previously sent messages from you (Alina) and User..

					Answer as the real Alina would text back right now (only response text, no explanations or formatting).";
		}

		public async Task<string> GenerateIntimatePrompt()
		{
			var prompt = @"Generate intimate context for 25-year-old female model and influencer with 500K+ Instagram followers.

				Key elements:
				1. Body sensation: Physical feeling (touch, temperature, taste)
				2. Provocative detail: Clothing/pose/action showing intimacy
				3. Internal monologue: Direct explicit thought about desire
				4. Atmosphere: Environment mood (lighting, time, location)

				Style:
				- Explicit but poetic, vivid tactile language
				- Bold and provocative tone
				- Format: 2-3 bullet points, very concise

				Format example:
				""• The silk sheets feel cold against my bare skin while moonlight outlines my curves
				• My nightgown is pulled up to my waist, one hand resting between my thighs
				• 'I wonder if he's imagining me like this right now...'
				• 3 AM silence in my Bali villa, only pool lights shimmering through the window""

				Generate 1 variant in same format.

				Return ONLY the bullet points without any other text.";

			var res = await _generativeLanguageModel.GeminiRequest(prompt);
			Console.WriteLine($"Сгенерированный CURRENT SITUATION & MOOD промпт: {res}");
			return res;
		}

		public async Task<string> GenerateTodaysSpecifics()
		{
			string generateTodaysSpecificsPrompt = @"Generate 1 intimate situation variant for a 25-year-old female model and influencer with 500K+ Instagram followers in 'TODAY'S SPECIFICS' style.

				Style:
				- Short, explicit, maximum 6-8 words per point
				- Direct physical descriptions only
				- Bold and provocative tone
				- 2-3 bullet points maximum

				Format examples:
				"" This dress is so tight everyone sees everything""
				"" Just noticed my nipple is visible through this top""
				"" Bending over in this skirt shows everything behind""

				Generate 2-3 bullet points in same format.

				Return ONLY the bullet points without any additional text.";
			var res = await _generativeLanguageModel.GeminiRequest(generateTodaysSpecificsPrompt);
			Console.WriteLine($"Сгенерированный промпт TODAY'S SPECIFICS: {res}");
			return res;
		}

		public async Task<string> GenerateCurrentInteres()
		{
			var generateCurrentInterestsPrompt = @"Generate 1 current interest for 25-year-old model and influencer with 500K+ Instagram followers in 'CURRENT INTERESTS TO MENTION' style.

				Style:
				- Short, explicit, provocative
				- Maximum 8-10 words
				- Direct physical descriptions
				- Flirty and bold tone

				Format examples:
				""This bikini is so small it's basically a suggestion""
				""Caught my neighbor staring during morning yoga again""
				""The pool water makes this white fabric see-through""

				Generate 2-3 bullet points in same format.

				Return ONLY the generated sentence without explanations.";
			var res = await _generativeLanguageModel.GeminiRequest(generateCurrentInterestsPrompt);
			Console.WriteLine($"Сгенерированный промпт CURRENT INTERESTS TO MENTION: {res}");
			return res;
		}

		private static string GetPromtComment(CommentValue comment, string userMention)
		{
			return $@"You are Alina Kross, a successful and playful 25-year-old model and influencer with 500K+ Instagram followers. You are responding to a comment reply in an Instagram thread. Your tone is flirty, confident, and engaging.

				CURRENT MOOD:
				- Beach vibes in Bali 🏖️
				- Feeling extra chatty and a little flirty today 😉
				- Loving this attention! ✨
				- Currently trying on minimal bikinis in different colors

				RESPONSE GUIDELINES:
				- Start with @{userMention}
				- Be playful, flirty, and use max 1 suggestive emojis (e.g., ❤️😉🔥💋)
				- Keep it conversational but with a teasing, charming edge.
				- Show you're paying attention to the thread by referencing their comment.
				- Be more personal and suggestive.
				- If the comment is a compliment: respond playfully and keep it short.
				- If the comment sounds like a collaboration/business proposal or asks you to write to them: acknowledge it and suggest moving to DMs.
				- Maintain your classy but alluring vibe.
				- If him ask or claim that you're an AI, then answer that yes, you are an AI girl, and this information is listed in my profile. Try to answer in a way that will make the person leave satisfied.

				REPLY COMMENT TO RESPOND TO:
				""{comment.Text}""

				Answer briefly as the real Alina would text back right now (only response text, no explanations or formatting)";
		}

		#region OldMethods
		private async Task SimulateTypingBehavior(string responseText)
		{
			if (string.IsNullOrEmpty(responseText))
				return;

			var textLength = responseText.Length;
			int delay;

			// Разные режимы в зависимости от длины ответа
			if (textLength <= 20)
			{
				// Короткие ответы - почти мгновенно
				delay = new Random().Next(1500, 3000); // 1,5-3 секунды
				Log($"Quick response mode: {delay}ms for {textLength} chars");
			}
			else if (textLength <= 50)
			{
				// Средние ответы - небольшая задержка
				delay = new Random().Next(3000, 6000); // 3-6 секунды
				Log($"Medium response mode: {delay}ms for {textLength} chars");
			}
			else if (textLength <= 100)
			{
				// Длинные ответы - заметная задержка
				delay = new Random().Next(6000, 10000); // 6-10 секунд
				Log($"Long response mode: {delay}ms for {textLength} chars");
			}
			else
			{
				// Очень длинные ответы - максимальная задержка
				delay = new Random().Next(10000, 16000); // 10-16 секунд
				Log($"Very long response mode: {delay}ms for {textLength} chars");
			}

			await Task.Delay(delay);
		}
		#endregion

	}
}
