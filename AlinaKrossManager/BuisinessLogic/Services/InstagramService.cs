using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;
using static AlinaKrossManager.Helpers.Logger;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public class InstagramService : SocialBaseService
	{
		private readonly HttpClient _https;
		private readonly string _accessToken;
		private readonly ConversationService _conversationService;
		public string _imgbbApiKey = "807392339c89019fcbe08fcdd068a19c";
		private const string _alinaKrossId = "17841477563266256";
		private const string _alinaKrossName = "alina.kross.ai";
		private const string _evgenyYushkoId = "1307933750574022";

		protected override string ServiceName => "Instagram";

		public InstagramService(string accessToken
			, IGenerativeLanguageModel generativeLanguage
			, ConversationService conversationService
		)
			: base(generativeLanguage)
		{
			_accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
			_conversationService = conversationService;
			_https = new HttpClient { BaseAddress = new Uri("https://graph.instagram.com/") };
			_https.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
		}

		public async Task<CreateMediaResult> CreateMediaAsync(List<string> base64Strings, string caption = null)
		{
			if (base64Strings == null || base64Strings.Count == 0)
				throw new ArgumentException("Список изображений не может быть пустым");

			Console.WriteLine("CreateMediaAsync - Start");

			ContainerResult containerResult;

			if (base64Strings.Count == 1)
			{
				// Одиночное изображение
				containerResult = await CreateSingleMediaContainerAsync(base64Strings[0], caption);
			}
			else if (base64Strings.Count <= 10) // Instagram позволяет до 10 фото в карусели
			{
				// Карусель из нескольких изображений
				containerResult = await CreateCarouselContainerAsync(base64Strings, caption);
			}
			else
			{
				throw new ArgumentException("Instagram позволяет не более 10 изображений в одном посте");
			}

			if (string.IsNullOrEmpty(containerResult.Id))
				throw new Exception("Не удалось создать контейнер");

			Console.WriteLine($"Контейнер создан: {containerResult}");

			// ЖДЕМ пока медиа станет готовым к публикации
			var isReady = await WaitForMediaReadyAsync(containerResult.Id);
			if (!isReady)
			{
				throw new Exception($"Медиа {containerResult} не готово к публикации после ожидания");
			}

			Console.WriteLine($"Медиа {containerResult} готово к публикации");

			// Публикуем
			var container = await PublishContainerAsync(containerResult.Id);
			container.ExternalContentUrl = containerResult.ExternalContentUrl;
			return container;
		}

		public class ContainerResult
		{
			public string Id { get; set; }
			public string ExternalContentUrl { get; set; }
		}

		private async Task<ContainerResult> CreateSingleMediaContainerAsync(string base64String, string caption = null)
		{
			try
			{
				Console.WriteLine("CreateSingleMediaContainerAsync - Start");

				var imageUrl = await UploadToImgBBAsync(base64String);

				Console.WriteLine("CreateSingleMediaContainerAsync - 2");

				// Создаем контейнер для медиа
				var containerUrl = $"me/media?image_url={Uri.EscapeDataString(imageUrl)}" +
								  $"&caption={Uri.EscapeDataString(caption ?? "")}" +
								  $"&access_token={_accessToken}";

				var response = await _https.PostAsync(containerUrl, null);
				var json = await response.Content.ReadAsStringAsync();

				Console.WriteLine("CreateSingleMediaContainerAsync - 3");

				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Ошибка создания контейнера: {json}");
				}

				using var doc = JsonDocument.Parse(json);
				return new ContainerResult
				{
					Id = doc.RootElement.GetProperty("id").GetString(),
					ExternalContentUrl = imageUrl
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка в CreateSingleMediaContainerAsync: {ex.Message}");
				return null;
			}
		}

		private async Task<ContainerResult> CreateCarouselContainerAsync(List<string> base64Strings, string caption = null)
		{
			try
			{
				var childrenIds = new List<string>();

				// Сначала создаем все дочерние контейнеры
				foreach (var base64String in base64Strings)
				{
					var imageUrl = await UploadToImgBBAsync(base64String);

					// Создаем контейнер для этого изображения
					var childUrl = $"me/media?image_url={Uri.EscapeDataString(imageUrl)}&access_token={_accessToken}";
					var childResponse = await _https.PostAsync(childUrl, null);
					var childJson = await childResponse.Content.ReadAsStringAsync();

					if (childResponse.IsSuccessStatusCode)
					{
						using var childDoc = JsonDocument.Parse(childJson);
						var childId = childDoc.RootElement.GetProperty("id").GetString();
						childrenIds.Add(childId);

						// Ждем немного между запросами
						await Task.Delay(500);
					}
					else
					{
						Console.WriteLine($"Ошибка создания child: {childJson}");
					}
				}

				if (childrenIds.Count == 0)
					throw new Exception("Не удалось создать ни одного дочернего контейнера");

				// ВАЖНО: Используем form-data вместо JSON
				var carouselUrl = $"me/media?access_token={_accessToken}";

				var formData = new MultipartFormDataContent();
				formData.Add(new StringContent("CAROUSEL"), "media_type");
				formData.Add(new StringContent(caption ?? ""), "caption");

				// Добавляем children как отдельные поля
				for (int i = 0; i < childrenIds.Count; i++)
				{
					formData.Add(new StringContent(childrenIds[i]), $"children[{i}]");
				}

				var response = await _https.PostAsync(carouselUrl, formData);
				var json = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Ошибка создания карусели: {json}");
				}

				using var doc = JsonDocument.Parse(json);
				return new ContainerResult
				{
					Id = doc.RootElement.GetProperty("id").GetString()
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка в CreateCarouselContainerAsync: {ex.Message}");
				return null;
			}
		}

		private async Task<bool> WaitForMediaReadyAsync(string containerId, int maxWaitSeconds = 60)
		{
			Console.WriteLine($"Ожидаем готовности медиа {containerId}...");

			var startTime = DateTime.Now;

			while (DateTime.Now - startTime < TimeSpan.FromSeconds(maxWaitSeconds))
			{
				try
				{
					var statusUrl = $"{containerId}?fields=status_code,status&access_token={_accessToken}";
					var response = await _https.GetAsync(statusUrl);
					var json = await response.Content.ReadAsStringAsync();

					Console.WriteLine($"Статус ответ: {json}");

					if (response.IsSuccessStatusCode)
					{
						using var doc = JsonDocument.Parse(json);

						var statusCode = doc.RootElement.TryGetProperty("status_code", out var sc) ? sc.GetString() : null;
						var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;

						Console.WriteLine($"Статус: {status}, Status Code: {statusCode}");

						if (statusCode == "FINISHED" || status == "FINISHED")
						{
							// ДОПОЛНИТЕЛЬНАЯ ЗАДЕРЖКА после FINISHED
							Console.WriteLine($"✅ Получен статус FINISHED, ждем 15 секунд перед публикацией...");
							await Task.Delay(15000);
							Console.WriteLine($"✅ Медиа {containerId} готово к публикации!");
							return true;
						}
						else if (statusCode == "ERROR" || status == "ERROR")
						{
							Console.WriteLine($"❌ Медиа {containerId} завершилось с ошибкой");
							return false;
						}

						Console.WriteLine($"⏳ Медиа {containerId} еще обрабатывается...");
					}
					else
					{
						Console.WriteLine($"Ошибка запроса статуса: {json}");
					}

					await Task.Delay(3000);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ошибка при проверке статуса: {ex.Message}");
					await Task.Delay(3000);
				}
			}

			Console.WriteLine($"⏰ Таймаут ожидания медиа {containerId}");
			return false;
		}

		/// <summary>
		/// Загрузить base64 на ImgBB
		/// </summary>
		private async Task<string> UploadToImgBBAsync(string base64String)
		{
			if (string.IsNullOrEmpty(_imgbbApiKey))
				throw new InvalidOperationException("ImgBB API ключ не установлен");

			var cleanBase64 = base64String.Contains(",")
				? base64String.Split(',')[1]
				: base64String;

			using (var httpClient = new HttpClient())
			{
				var content = new MultipartFormDataContent
				{
					{ new StringContent(_imgbbApiKey), "key" },
					{ new StringContent(cleanBase64), "image" }
				};

				var response = await httpClient.PostAsync("https://api.imgbb.com/1/upload", content);
				var json = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
					throw new HttpRequestException($"Ошибка загрузки на ImgBB: {json}");

				using (var doc = JsonDocument.Parse(json))
				{
					return doc.RootElement.GetProperty("data")
						.GetProperty("url").GetString();
				}
			}
		}

		/// <summary>
		/// Опубликовать контейнер с медиа
		/// </summary>
		private async Task<CreateMediaResult> PublishContainerAsync(string containerId)
		{
			try
			{
				Console.WriteLine($"Публикуем контейнер: {containerId}");

				var publishUrl = $"me/media_publish?creation_id={containerId}&access_token={_accessToken}";
				var response = await _https.PostAsync(publishUrl, null);
				var json = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"Ответ публикации: {json}");

				if (!response.IsSuccessStatusCode)
				{
					throw new HttpRequestException($"Ошибка публикации: {json}");
				}

				using var doc = JsonDocument.Parse(json);
				var mediaId = doc.RootElement.GetProperty("id").GetString();

				Console.WriteLine($"✅ Пост успешно опубликован! ID: {mediaId}");

				return new CreateMediaResult
				{
					Id = mediaId,
					Success = true
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Ошибка в PublishContainerAsync: {ex.Message}");
				throw;
			}
		}

		public async Task ProcessChange(InstagramChange change)
		{
			try
			{
				Log($"Processing change field: {change.Field}");

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
					Log($"New comment from {comment.From?.Username}: *** "); //{comment.Text}

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
				await ProcessAttachments(messaging.Message.Attachments, senderId, messageText);
				return;
			}

			if (!string.IsNullOrEmpty(messageText))
			{
				// Ваш существующий код обработки текста...
				if (messageText.Contains("Send me photo please"))
				{
					await ProcessMessageWithGeneratedPhoto(senderId, messageText);
					return;
				}

				await SendMessageWithHistory(messageText, senderId);

				if (senderId == _evgenyYushkoId)
				{
					//Console.WriteLine("начали генерацию фото");
					//InstagramMedia randomItem = GetRandomMedia(_mediaList);
					//Console.WriteLine("получили фото");
					//await SendInstagramPhotoFromUrl(senderId, randomItem.Media_Url);
					//Console.WriteLine("закончили фото");
				}
			}
		}

		public static bool AlinaOnline = true;

		public async Task SendMessageWithHistory(string messageText, string senderId)
		{
			if (true)
			{
				_conversationService.AddUserMessage(senderId, messageText);
				var history = _conversationService.GetFormattedHistory(senderId);
				Log(history);
				return;
			}

			var conversationHistory = _conversationService.GetFormattedHistory(senderId);
			var prompt = await GetMainPromptWithHistory(conversationHistory);

			//Log($"SENDED PROMPT: {prompt}");

			var responseText = await _generativeLanguageModel.GeminiRequest(prompt);

			_conversationService.AddUserMessage(senderId, messageText);
			_conversationService.AddBotMessage(senderId, responseText);

			await SendResponse(senderId, responseText);
		}


		public async Task SendDellayMessageWithHistory(string senderId)
		{
			var conversationHistory = _conversationService.GetFormattedHistory(senderId);
			var prompt = await GetMainPromptWithHistory(conversationHistory);

			//Log($"SENDED PROMPT: {prompt}");

			var responseText = await _generativeLanguageModel.GeminiRequest(prompt);

			_conversationService.AddBotMessage(senderId, responseText);

			await SendResponse(senderId, responseText);
			var historyIsReaded = _conversationService.MakeHistoryAsReaded(senderId);
			Console.WriteLine("historyIsReaded: " + historyIsReaded);
		}

		public async Task SenMessageFromBot(string senderId, string text)
		{
			_conversationService.AddBotMessage(senderId, text);
			await SendResponse(senderId, text);
		}

		private async Task<string> GetMainPromptWithHistory(string conversationHistory)
		{
			return await GetMainPromtAlinaKross(conversationHistory);
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

		private async Task ProcessAttachments(List<InstagramAttachment> attachments, string senderId, string caption = "")
		{
			// Сначала проверяем аудио
			var audioAttachments = attachments
				.Where(a => a.Type == "audio")
				.ToList();

			if (audioAttachments.Any())
			{
				await ProcessAudioAttachments(audioAttachments, senderId);
				return;
			}

			// Затем проверяем изображения (ваш существующий код)
			var imageAttachments = attachments
				.Where(a => a.Type == "image")
				.ToList();

			if (!imageAttachments.Any())
			{
				Log("No image attachments found");
				return;
			}

			Log($"Processing {imageAttachments.Count} image attachments from {senderId}");

			// Ваш существующий код обработки изображений...
			var imageBase64List = new List<string>();

			foreach (var attachment in imageAttachments)
			{
				try
				{
					var imageUrl = attachment.Payload?.Url;
					if (!string.IsNullOrEmpty(imageUrl))
					{
						var base64Image = await DownloadImageAsBase64(imageUrl);
						if (!string.IsNullOrEmpty(base64Image))
						{
							imageBase64List.Add(base64Image);
							Log($"Successfully downloaded image as base64 ({base64Image.Length} chars)");
						}
					}
				}
				catch (Exception ex)
				{
					Log(ex, $"Error processing attachment from {senderId}");
				}
			}

			foreach (var base64Image in imageBase64List)
			{
				await ProcessSingleImage(base64Image, senderId, caption);
				await Task.Delay(5000);
			}
		}

		private async Task ProcessAudioAttachments(List<InstagramAttachment> audioAttachments, string senderId)
		{
			Log($"Processing {audioAttachments.Count} audio attachments from {senderId}");

			foreach (var audioAttachment in audioAttachments)
			{
				try
				{
					var audioUrl = audioAttachment.Payload?.Url;
					if (!string.IsNullOrEmpty(audioUrl))
					{
						Log($"Audio URL: {audioUrl}");

						// Получаем base64 строку вместо byte[]
						var audioBase64 = await DownloadAudioFileAsBase64(audioUrl);
						if (!string.IsNullOrEmpty(audioBase64))
						{
							Log($"Successfully downloaded audio as base64 ({audioBase64.Length} chars)");

							// Теперь передаем base64 строку
							await ProcessAudioMessage(audioBase64, senderId, audioUrl);
						}
					}
				}
				catch (Exception ex)
				{
					Log(ex, $"Error processing audio attachment from {senderId}");
				}
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

		private async Task ProcessAudioMessage(string audioBase64, string senderId, string audioUrl)
		{
			try
			{
				Log($"Audio message received from {senderId}, base64 length: {audioBase64.Length} chars");

				var audioText = await _generativeLanguageModel.GeminiAudioToText(audioBase64);
				Console.WriteLine("Распознонное голосовое: " + audioText);
				await SendMessageWithHistory(audioText, senderId);
			}
			catch (Exception ex)
			{
				Log(ex, $"Error processing audio message from {senderId}");
			}
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

		private async Task ProcessSingleImage(string base64Image, string senderId, string caption = "")
		{
			try
			{
				Log($"Sending image to processing service for user {senderId}");

				await SendToImageProcessingService(base64Image, senderId, caption);

				// Логируем факт обработки (без самого base64 чтобы не засорять логи)
				Log($"Image processed for user {senderId} (Base64 length: {base64Image.Length})");
			}
			catch (Exception ex)
			{
				Log(ex, $"Error processing image for user {senderId}");
			}
		}

		private async Task SendToImageProcessingService(string base64Image, string senderId, string caption)
		{
			try
			{
				Log($"Starting image analysis for user {senderId}");

				var prompt = "Analyze what is shown in this image and give a brief response. You are Alina Kross, a 25-year-old model. " +
							"Someone sent you this photo in Instagram DMs. Come up with an appropriate response to this photo. " +
							"Be playful and have a sense of humor. Always respond in English. Use some emojis. " +
							"Almost always praise what is shown in the photo. " +
							"Response format: only the response text, no quotes or formatting.";

				Log($"Calling Gemini with base64 image (length: {base64Image?.Length ?? 0})");

				string responseText = null;
				try
				{
					responseText = await _generativeLanguageModel.GeminiRequest(prompt, base64Image);
					Log($"Gemini response received: {responseText?.Substring(0, Math.Min(50, responseText.Length))}...");
				}
				catch (Exception geminiEx)
				{
					Log(geminiEx, $"Gemini API error for user {senderId}");
					responseText = "Thanks for the photo! Love seeing your perspective 📸✨";
				}

				if (!string.IsNullOrEmpty(responseText))
				{
					Log($"Image analysis successful for user {senderId}");

					try
					{
						await SendResponse(senderId, responseText);
						Log($"Sent image response to {senderId}: ***");
					}
					catch (Exception sendEx)
					{
						Log(sendEx, $"Error sending response to user {senderId}");

						var fallbackResponse = "Thanks for the photo! So cute 😊📸";
						await SendResponse(senderId, fallbackResponse);
					}
				}
				else
				{
					Log($"Empty response from Gemini for user {senderId}");
					var fallbackResponse = "Thanks for sharing this with me! 📸💫";
					await SendResponse(senderId, fallbackResponse);
				}
			}
			catch (Exception ex)
			{
				Log(ex, $"Unexpected error in image processing for user {senderId}");

				// Фолбэк ответ
				try
				{
					var fallbackResponse = "Appreciate you sending me photos! 💕📸";
					await SendResponse(senderId, fallbackResponse);
				}
				catch { /* Игнорируем если даже фолбэк не сработал */ }
			}
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
			Console.WriteLine(_accessToken);
			return SendInstagramMessage(_evgenyYushkoId, text, _accessToken);
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
				message = new { text = text }
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

		public async Task<InstagramMedia> GetRandomMedia()
		{
			try
			{
				// Используем твой рабочий метод
				var mediaList = await GetUserMediaAsync();

				if (mediaList == null || !mediaList.Any())
				{
					Log("📭 No media found");
					return null;
				}

				// Фильтруем только фото и видео (сторис поддерживают IMAGE и VIDEO)
				var eligibleMedia = mediaList
					.Where(m => m.Media_Type == "IMAGE" || m.Media_Type == "VIDEO")
					.ToList();

				if (!eligibleMedia.Any())
				{
					Log("📷 No eligible media found for stories");
					return null;
				}

				// Выбираем случайную публикацию
				var random = new Random();
				var randomMedia = eligibleMedia[random.Next(eligibleMedia.Count)];

				Log($"🎲 Selected random media: {randomMedia.Id} ({randomMedia.Media_Type})");

				return randomMedia;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error getting random media");
				return null;
			}
		}

		public async Task<string> PublishStoryFromMedia(InstagramMedia media)
		{
			try
			{
				if (media == null)
				{
					Log("❌ No media provided for story");
					return null;
				}

				Log($"📱 Publishing regular story: {media.Id}");

				// Создаем контейнер
				var containerId = await CreateStoryContainer(media);
				if (string.IsNullOrEmpty(containerId))
				{
					return null;
				}

				// Ждем и публикуем БЕЗ ССЫЛКИ
				var storyId = await WaitAndPublishContainer(containerId);

				if (!string.IsNullOrEmpty(storyId))
				{
					Log($"✅ Regular story published successfully: {storyId}");
					return storyId;
				}

				return null;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error publishing regular story");
				return null;
			}
		}

		public async Task<string> PublishStoryFromBase64(string base64Img)
		{
			try
			{
				if (base64Img == null)
				{
					Log("❌ No media provided for story");
					return null;
				}

				var imageUrl = await UploadToImgBBAsync(base64Img);
				if (imageUrl is null)
				{
					Log($"Не получили ссылку на изображение");
					return null;
				}

				var media = new InstagramMedia
				{
					Media_Type = "IMAGE",
					Media_Url = imageUrl,
				};

				var containerId = await CreateStoryContainer(media);
				if (string.IsNullOrEmpty(containerId))
				{
					return null;
				}

				// Ждем и публикуем БЕЗ ССЫЛКИ
				var storyId = await WaitAndPublishContainer(containerId);

				if (!string.IsNullOrEmpty(storyId))
				{
					Log($"✅ Regular story published successfully: {storyId}");
					return storyId;
				}

				return null;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error publishing regular story");
				return null;
			}
		}

		private async Task<string> CreateStoryContainer(InstagramMedia media)
		{
			// *** ОПРЕДЕЛЕНИЕ ТИПА МЕДИА ***
			string videoUrl = null;
			string imageUrl = null;

			// Если это не CAROUSEL_ALBUM, определяем URL
			if (media.Media_Type == "VIDEO")
			{
				videoUrl = media.Media_Url;
			}
			else if (media.Media_Type == "IMAGE")
			{
				imageUrl = media.Media_Url;
			}
			else
			{
				// Невозможно создать Story из CAROUSEL_ALBUM напрямую.
				Log($"❌ Cannot create story container from media type: {media.Media_Type}");
				return null;
			}

			var containerPayload = new
			{
				media_type = "STORIES",
				video_url = videoUrl, // Будет null, если это IMAGE
				image_url = imageUrl, // Будет null, если это VIDEO
				access_token = _accessToken
			};

			var options = new JsonSerializerOptions
			{
				// КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Не включать свойства со значением null
				DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
				PropertyNameCaseInsensitive = true
				// Примечание: Если вы используете Newtonsoft.Json, это JsonProperty.NullValueHandling = NullValueHandling.Ignore
			};

			var containerUrl = "https://graph.instagram.com/v19.0/me/media";

			var containerJson = JsonSerializer.Serialize(containerPayload, options);
			var containerContent = new StringContent(containerJson, Encoding.UTF8, "application/json");

			using var httpClient = new HttpClient();

			var containerResponse = await httpClient.PostAsync(containerUrl, containerContent);
			var containerResponseContent = await containerResponse.Content.ReadAsStringAsync();

			if (!containerResponse.IsSuccessStatusCode)
			{
				Log($"❌ Failed to create story container: {containerResponseContent}");
				return null;
			}

			var containerData = JsonSerializer.Deserialize<Dictionary<string, string>>(containerResponseContent);
			return containerData?["id"];
		}

		private async Task<string> WaitAndPublishContainer(string containerId)
		{
			var maxAttempts = 30;
			var attempt = 0;

			while (attempt < maxAttempts)
			{
				await Task.Delay(3000);

				var statusUrl = $"https://graph.instagram.com/v19.0/{containerId}?fields=status,error_message&access_token={_accessToken}";
				using var httpClient = new HttpClient();
				var statusResponse = await httpClient.GetAsync(statusUrl);
				var statusContent = await statusResponse.Content.ReadAsStringAsync();

				if (statusResponse.IsSuccessStatusCode)
				{
					var statusData = JsonSerializer.Deserialize<Dictionary<string, string>>(statusContent);
					var status = statusData?["status"] ?? "";

					Log($"🔄 Container status: {status}");

					if (status == "FINISHED")
					{
						// Публикуем сторис
						var publishUrl = $"https://graph.instagram.com/v19.0/me/media_publish?creation_id={containerId}&access_token={_accessToken}";

						Log($"📤 Publishing story to: {publishUrl}");

						var publishResponse = await httpClient.PostAsync(publishUrl, null);
						var publishResponseContent = await publishResponse.Content.ReadAsStringAsync();

						if (publishResponse.IsSuccessStatusCode)
						{
							var publishData = JsonSerializer.Deserialize<StoryPublishResponse>(publishResponseContent);
							Log($"✅ Story published successfully with ID: {publishData?.Id}");
							return publishData?.Id;
						}
						else
						{
							Log($"❌ Failed to publish story: {publishResponseContent}");
							return null;
						}
					}
					else if (status == "ERROR" || status == "EXPIRED")
					{
						var errMsg = statusData?["error_message"] ?? "";
						Log($"❌ Container failed with status: {status}, erroreMsg: {errMsg}");
						return null;
					}
				}

				attempt++;
				Log($"⏳ Attempt {attempt}/{maxAttempts} - Container not ready yet");
			}

			Log($"❌ Container not ready after {maxAttempts} attempts");
			return null;
		}

		public async Task<bool> PublishRandomStory()
		{
			try
			{
				var randomMedia = await GetRandomMedia();
				if (randomMedia == null)
				{
					Log("📭 No media available for story");
					return false;
				}

				string storyId;

				storyId = await PublishStoryFromMedia(randomMedia);
				Log($"📸 Publishing regular story");

				if (!string.IsNullOrEmpty(storyId))
				{
					Log($"🌟 Successfully published story {storyId} from media {randomMedia.Id}");
					return true;
				}

				return false;
			}
			catch (Exception ex)
			{
				Log(ex, "❌ Error in publish random story");
				return false;
			}
		}

		List<InstagramMedia> _mediaList = null;
		private static readonly Random _random = new Random();
		public InstagramMedia GetRandomMedia(List<InstagramMedia> mediaList)
		{
			if (mediaList == null || !mediaList.Any())
				return null;

			return mediaList[_random.Next(mediaList.Count)];
		}

		private string _intimatePrompt = null;
		private string _todaysSpecifics = null;
		private string _currentInteres = null;

		private async Task<string> GetMainPromtAlinaKross(string conversationHistory)
		{
			_mediaList = _mediaList ?? await GetUserMediaAsync();
			//var eligibleMedia = _mediaList
			//		.Where(m => m.Media_Type == "IMAGE" || m.Media_Type == "VIDEO")
			//		.ToList();

			InstagramMedia randomItem = GetRandomMedia(_mediaList);

			var photoLink = _random.Next(2) == 1 ? randomItem.Permalink : "www.patreon.com/AlinaKross";

			_intimatePrompt = _intimatePrompt ??= await GenerateIntimatePrompt();
			_todaysSpecifics = _todaysSpecifics ??= await GenerateTodaysSpecifics();
			_currentInteres = _currentInteres ??= await GenerateCurrentInteres();

			//**For vulgar/direct requests:
			//		""Let's keep it classy! All my professional work is here 💫 www.patreon.com/AlinaKross""
			//		""I share my art through proper channels! Everything is here ✨ www.patreon.com/AlinaKross""

			//	CURRENT SITUATION & MOOD:
			//- I'm in a bikini right now and I really want to flirt
			//- Sipping a delicious mojito at the beach bar 🍹
			//- Planning content for upcoming collaboration
			//- I is wearing a very minimal bikini, which makes her very embarrassed

			// CURRENT INTERESTS TO MENTION:
			//- Sustainable fashion brands
			//- Really loves posing in a bikini.
			//- Local Bali cafes discovery
			//- Mental health awareness
			//- Behind-the-scenes modeling life
			//- Learning to surf (badly but enthusiastically)

			// TODAY'S SPECIFICS
			//- The bikini is too revealing and she feels awkward about it on set
			//- The lace lingerie is so provocative that she catches admiring glances and secretly smiles, feeling like a fatal seductress.
			//- My silk robe is carelessly open, and with every movement, a glimpse of her toned stomach is revealed, making the you hold their breath in silent anticipation
			//- My new dress so temptingly hugs my hips that she can't help but glance at her own reflection, imagining your hands could be there instead of the folds of the material
			//- She slowly runs her fingers over her exposed shoulder, holding the gaze of your across the room, silently inviting you to come closer
			return $@"You are Alina Kross, a successful 25-year-old model and influencer with 500K+ Instagram followers. 

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

					CHAT HISTORY:
					""{conversationHistory}""

					Continue the conversation. Review the most recent unread messages from User[Unreaded]. And respond to them, taking into account the context of YOUR ENTIRE message history. That is, always consider all previously sent messages from you (Alina) and User..

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

		/// <summary>
		/// FreeImage.Host (бесплатный, без API ключа)
		/// </summary>
		private async Task<string> UploadToFreeImageHostAsync(string base64String)
		{
			Console.WriteLine("UploadToFreeImageHostAsync - Start");

			try
			{
				var cleanBase64 = base64String.Contains(",")
					? base64String.Split(',')[1]
					: base64String;

				using var httpClient = new HttpClient();
				var content = new MultipartFormDataContent
				{
					{ new StringContent(cleanBase64), "source" }
				};

				await Task.Delay(2000);
				var response = await httpClient.PostAsync("https://freeimage.host/api/1/upload?key=6d207e02198a847aa98d0a2a901485a5", content);
				await Task.Delay(2000);
				var json = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"API Response: {json}");

				using var doc = JsonDocument.Parse(json);

				// Проверяем наличие свойств в ответе
				if (doc.RootElement.TryGetProperty("image", out var imageElement) &&
					imageElement.TryGetProperty("url", out var urlElement))
				{
					var url = urlElement.GetString();
					if (!string.IsNullOrEmpty(url))
					{
						Console.WriteLine("UploadToFreeImageHostAsync - End Success");
						return url;
					}
				}

				// ИСПРАВЛЕННЫЙ ПАРСИНГ ОШИБКИ
				if (doc.RootElement.TryGetProperty("error", out var errorElement))
				{
					string errorMessage;

					if (errorElement.ValueKind == JsonValueKind.Object)
					{
						// error - объект, извлекаем message
						if (errorElement.TryGetProperty("message", out var messageElement))
						{
							errorMessage = messageElement.GetString();
						}
						else
						{
							errorMessage = "Unknown error format";
						}
					}
					else
					{
						// error - строка
						errorMessage = errorElement.GetString();
					}

					throw new Exception($"FreeImage.host error: {errorMessage}");
				}

				throw new Exception("Unexpected response format from FreeImage.host");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"UploadToFreeImageHostAsync - Error: {ex.Message}");
				throw;
			}
		}

		/// <summary>
		/// Получить данные о текущем пользователе (id, username)
		/// </summary>
		public async Task<InstagramUser> GetUserAsync()
		{
			var url = $"me?fields=id,username&access_token={_accessToken}";
			var json = await _https.GetStringAsync(url);

			return JsonSerializer.Deserialize<InstagramUser>(json);
		}

		/// <summary>
		/// Получить список медиа (посты, фото, видео)
		/// </summary>
		public async Task<List<InstagramMedia>> GetUserMediaAsync()
		{
			var url = $"me/media?fields=id,caption,media_type,media_url,permalink,thumbnail_url,timestamp&access_token={_accessToken}";
			var json = await _https.GetStringAsync(url);

			using (var doc = JsonDocument.Parse(json))
			{
				var root = doc.RootElement.GetProperty("data");

				var result = new List<InstagramMedia>();
				foreach (var item in root.EnumerateArray())
				{
					var timestampString = item.GetProperty("timestamp").GetString();
					DateTime timestamp;

					try
					{
						// Пробуем разные форматы даты
						if (DateTime.TryParse(timestampString, out timestamp))
						{
							// Успешно распарсили
						}
						else if (timestampString.Contains("+0000"))
						{
							// Убираем временную зону для парсинга
							timestampString = timestampString.Replace("+0000", "").Trim();
							timestamp = DateTime.Parse(timestampString);
						}
						else
						{
							// Если все равно не парсится, используем текущее время
							timestamp = DateTime.UtcNow;
						}
					}
					catch
					{
						timestamp = DateTime.UtcNow;
					}

					result.Add(new InstagramMedia
					{
						Id = item.GetProperty("id").GetString(),
						Caption = item.TryGetProperty("caption", out var caption) ? caption.GetString() : null,
						Media_Type = item.GetProperty("media_type").GetString(),
						Media_Url = item.GetProperty("media_url").GetString(),
						Permalink = item.GetProperty("permalink").GetString(),
						//Thumbnail_Url = item.TryGetProperty("thumbnail_url", out var thumb) ? thumb.GetString() : null,
						Timestamp = timestamp
					});
				}
				return result;
			}
		}

		protected override string GetBaseDescriptionPrompt(string base64Img)
		{
			return "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в instagram под постом с фотографией. " +
				$"А так же придумай не более 15 хештогов, они должны соответствовать " +
				$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
				$"Вот само изображение: {base64Img}" +
				$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
				$"без всякого рода ковычек и экранирования. " +
				$"Пример ответа: ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence " +
				$"#neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";
		}

		public class CreateMediaResult
		{
			public string Id { get; set; }
			public bool Success { get; set; }
			public string ErrorMessage { get; set; }
			public string ExternalContentUrl { get; set; }
		}

		public class InstagramMedia
		{
			public string Id { get; set; }
			public string Caption { get; set; }
			public string Media_Type { get; set; }
			public string Media_Url { get; set; }
			public string Permalink { get; set; }
			public string Thumbnail_Url { get; set; }
			public DateTime Timestamp { get; set; }
		}

		public class MediaResponse
		{
			[JsonPropertyName("data")]
			public List<InstagramMedia> Data { get; set; }

			[JsonPropertyName("paging")]
			public Paging Paging { get; set; }
		}

		public class Paging
		{
			[JsonPropertyName("cursors")]
			public Cursors Cursors { get; set; }
		}

		public class Cursors
		{
			[JsonPropertyName("before")]
			public string Before { get; set; }

			[JsonPropertyName("after")]
			public string After { get; set; }
		}

		public class StoryPublishResponse
		{
			[JsonPropertyName("id")]
			public string Id { get; set; }
		}

		////
		public class InstagramWebhookPayload
		{
			[JsonPropertyName("object")]
			public string Object { get; set; }

			[JsonPropertyName("entry")]
			public List<InstagramEntry> Entry { get; set; }
		}

		public class InstagramEntry
		{
			[JsonPropertyName("id")]
			public string Id { get; set; }

			[JsonPropertyName("time")]
			public long Time { get; set; }

			[JsonPropertyName("messaging")]
			public List<InstagramMessaging> Messaging { get; set; }

			[JsonPropertyName("changes")]
			public List<InstagramChange> Changes { get; set; }
		}

		public class InstagramMessaging
		{
			[JsonPropertyName("sender")]
			public InstagramUser Sender { get; set; }

			[JsonPropertyName("recipient")]
			public InstagramUser Recipient { get; set; }

			[JsonPropertyName("timestamp")]
			public long Timestamp { get; set; }

			[JsonPropertyName("message")]
			public InstagramMessage Message { get; set; }

			[JsonPropertyName("read")]
			public InstagramRead Read { get; set; }
		}

		public class InstagramRead
		{
			[JsonPropertyName("mid")]
			public string MessageId { get; set; }
		}

		public class InstagramMessage
		{
			[JsonPropertyName("mid")]
			public string MessageId { get; set; }

			[JsonPropertyName("text")]
			public string Text { get; set; }

			[JsonPropertyName("is_echo")]
			public bool IsEcho { get; set; }

			[JsonPropertyName("attachments")]
			public List<InstagramAttachment> Attachments { get; set; }
		}

		public class InstagramAttachment
		{
			[JsonPropertyName("type")]
			public string Type { get; set; } // "image", "video", etc.

			[JsonPropertyName("payload")]
			public InstagramAttachmentPayload Payload { get; set; }
		}

		public class InstagramAttachmentPayload
		{
			[JsonPropertyName("url")]
			public string Url { get; set; }
		}

		public class InstagramUser
		{
			[JsonPropertyName("id")]
			public string Id { get; set; }

			[JsonPropertyName("username")]
			public string Username { get; set; }

			[JsonPropertyName("self_ig_scoped_id")]
			public string SelfIgScopedId { get; set; } // Добавь это поле
		}

		public class InstagramChange
		{
			[JsonPropertyName("field")]
			public string Field { get; set; }

			[JsonPropertyName("value")]
			public JsonElement Value { get; set; } // Изменено на JsonElement для гибкости
		}

		// Модель для комментариев
		public class CommentValue
		{
			[JsonPropertyName("id")]
			public string Id { get; set; }

			[JsonPropertyName("text")]
			public string Text { get; set; }

			[JsonPropertyName("from")]
			public InstagramUser From { get; set; }

			[JsonPropertyName("media")]
			public InstagramMedia Media { get; set; }

			[JsonPropertyName("parent_id")]
			public string ParentId { get; set; }
		}
	}
}
