using AlinaKrossManager.BuisinessLogic.Instagram;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;
using Telegram.Bot.Types;

namespace AlinaKrossManager.Jobs
{
	public class DilyPostJob : SchedulerJob
	{
		public const string Time = "0 0 12 * * ?";

		private readonly InstagramService _instagramService;
		private readonly ConversationService _conversationService;
		private readonly TelegramService _telegramService;

		public DilyPostJob(IServiceProvider serviceProvider
			, InstagramService instagramService
			, ConversationService conversationService
			, IGenerativeLanguageModel generativeLanguageModel
			, TelegramService telegramService
		)
			: base(serviceProvider, generativeLanguageModel)
		{
			_instagramService = instagramService;
			_conversationService = conversationService;
			_telegramService = telegramService;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			try
			{
				await _instagramService.SendInstagramMessage("1307933750574022", "Привет, 💋");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}

			try
			{
				var allUsers = _conversationService.GetAllUserConversations();

				Console.WriteLine("Count All Users: " + allUsers.Count);
				foreach (var userId in allUsers)
				{
					Console.WriteLine("UsersId: " + userId);

					var userHistory = _conversationService.GetHistory(userId);
					if (userHistory != null)
					{
						var lastMsg = userHistory.TakeLast(1).FirstOrDefault();
						Console.WriteLine($"Last msg Sender: {lastMsg.Sender}, Text: {lastMsg.Text}");

						if (lastMsg != null && lastMsg.Sender == "User")
						{
							await _instagramService.SendInstagramMessage(userId, "))))");
							await Task.Delay(TimeSpan.FromSeconds(5));
						}
					}
				}

				foreach (var userId in allUsers)
				{
					await _instagramService.SendInstagramMessage(userId, "💋");
					//Console.WriteLine("начали генерацию фото");
					//InstagramMedia randomItem = GetRandomMedia(_mediaList);
					//Console.WriteLine("получили фото");
					//await SendInstagramPhotoFromUrl(senderId, randomItem.Media_Url);
					//Console.WriteLine("закончили фото");

					await Task.Delay(TimeSpan.FromSeconds(6));
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}

			var chatId = 1231047171;

			Console.WriteLine("Генерация сцен для Instagram...\n");

			string promptForCreateImage = null;//_randomGenerator.GenerateRandomPrompt();

			Message msg = null;
			List<string> images = new();
			var random = new Random();

			try
			{
				promptForCreateImage = await OriginalPrompt();
				//promptForCreateImage = await _generativeLanguageModel.GeminiRequest(promptVar);
				if (promptForCreateImage is not null)
				{
					var imagesRes = await CreateImage(chatId, promptForCreateImage, msg);
					images = imagesRes.Images;
					msg = imagesRes.Msg;

					if (random.Next(4) != 1)
					{
						var promptVar =
							$"Измени этот шикарный промпт таким образом, что бы эта девушка немного повернулась к нам и стало более отчётливо видны её бёдра или же просто поменяй её позу" +
							$"Вот этот промпт:\n\n{promptForCreateImage}" +
							$"\n\n**Формат ответа:** Только готовый промпт на английском, без пояснений.";
						promptForCreateImage = await _generativeLanguageModel.GeminiRequest(promptVar);

						imagesRes = await CreateImage(chatId, promptForCreateImage, msg);

						if (imagesRes.Images.Count > 0)
						{
							images.Add(imagesRes.Images.First());
						}

						if (random.Next(2) != 1)
						{
							promptVar =
								$"Измени этот шикарный промпт таким образом, что бы эта девушка стала выглядеть ещё более вульгарно и вызывающе, но в пределах разумного, что бы пройти цензуру а так же измени позу. " +
								$"Вот этот промпт:\n\n{promptForCreateImage}" +
								$"\n\n**Формат ответа:** Только готовый промпт на английском, без пояснений.";
							promptForCreateImage = await _generativeLanguageModel.GeminiRequest(promptVar);

							imagesRes = await CreateImage(chatId, promptForCreateImage, msg);

							if (imagesRes.Images.Count > 0)
							{
								images.Add(imagesRes.Images.First());
							}
						}
					}

					Console.WriteLine("images.Count = " + images.Count);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
				Console.WriteLine(e.InnerException.Message);
			}

			if (images.Count == 0)
			{
				await _telegramService.SendMessage(chatId, "Не удалось сгенерировать изображения");
				return;
			}

			if (images.Count > 1 && random.Next(2) == 1)
			{
				images.Reverse();
			}

			try
			{
				if (_telegramService is null)
				{
					Console.WriteLine("_telegramService is null");
				}


				if (images.Count > 1)
				{
					Console.WriteLine($"Первое изображение null: {images.First() == null}");
					//Console.WriteLine($"Длина base64 строки: {images.First()?.Length ?? 0}");
					await _telegramService.SendPhotoAlbumAsync(chatId, images, null, "Сгенерированное фото по данному промпту:\n\n" + promptForCreateImage);
				}
				else
				{
					await _telegramService.SendSinglePhotoAsync(chatId, images.First(), null, "Сгенерированное фото по данному промпту:\n\n" + promptForCreateImage);
					try
					{
						await _telegramService.DeleteMessage(msg.Chat.Id, msg.MessageId);
					}
					catch { }
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
			}

			string description = "";
			try
			{
				var promptForeDescriptionPost = "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в инстаграм под постом с фотографией" +
					$", которая могла бы быть сгенерирована вот по этому промпту. А так же придумай не более 15 хештогов, они должны соответствовать " +
					$"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
					$"Вот сам промпт: {promptForCreateImage}" +
					$"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
					$"без всякого рода ковычек и экранирования. " +
					$"Пример ответа: Golden hour glow ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence #neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";

				description = await _generativeLanguageModel.GeminiRequest(promptForeDescriptionPost);
				try
				{
					await _telegramService.SendMessage(chatId, $"{description}");
				}
				catch { }
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}

			Console.WriteLine($"Начнаем отправку в инсту");

			try
			{
				var result = await _instagramService.CreateMediaAsync(images, description);
				if (result.Success)
				{
					var msgRes = $"✅ Пост успешно создан! ID: {result.Id}";
					Console.WriteLine(msgRes);
					try
					{
						msg = await _telegramService.SendMessage(chatId, msgRes);
					}
					catch { }

					try
					{
						await Task.Delay(TimeSpan.FromSeconds(15));

						msg = await _telegramService.SendMessage(chatId, "Начинаем публиковать его в сториc..");

						var allMedia = await _instagramService.GetUserMediaAsync();
						var newMedia = allMedia.FirstOrDefault(all => all.Id == result.Id);
						newMedia.Media_Url = result.ExternalContentUrl ?? newMedia.Media_Url;

						Console.WriteLine("Найдена новая публикация ExternalContentUrl: " + result.ExternalContentUrl);
						Console.WriteLine("Найдена новая публикация Media_Url: " + newMedia.Media_Url);

						var storyId = await _instagramService.PublishStoryFromMedia(newMedia);
						if (storyId is not null)
						{
							msg = await _telegramService.SendMessage(chatId, $"✅ Сториз успешно опублиткованна: {storyId}");
						}
					}
					catch (Exception ex)
					{
						throw new Exception($"Ошибка создания сторис: {ex}");
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Ошибка: {ex.Message}");
			}
		}

		private async Task<ImageResult> CreateImage(int chatId, string promptForCreateImage, Message msg)
		{
			try
			{
				msg = await _telegramService.SendMessage(chatId, $"Сгенерированный промпт:\n\n{promptForCreateImage}");
			}
			catch { }

			Console.WriteLine("Первая попытка сгенерировать изобюражение...");
			List<string> images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage);
			if (images.Count == 0)
			{
				Console.WriteLine("Вторая попытка сгенерировать изобюражение...");
				images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage);
			}
			if (images.Count == 0)
			{
				Console.WriteLine("Третья попытка сгенерировать изобюражение...");
				images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage);
			}

			if (images.Count == 0)
			{
				string promptVar = "По этому промпту AI не хочет генерировтаь изображение, возможно оно не проходит цензуру. Попробуй немного его смягчить " +
					$", вот этот промпт: {promptForCreateImage}" +
					$"\n\n**Формат ответа:** Только готовый промпт на английском, без пояснений.";
				promptForCreateImage = await _generativeLanguageModel.GeminiRequest(promptVar);

				try
				{
					msg = await _telegramService.SendMessage(chatId, $"Смягчённый промпт:\n\n{promptForCreateImage}");
				}
				catch { }

				Console.WriteLine("Четвёртая попытка сгенерировать изобюражение изменив промпт...");
				images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage);
				if (images.Count == 0)
				{
					Console.WriteLine("Пятая попытка сгенерировать изобюражение изменив промпт...");
					images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage);
				}
				if (images.Count == 0)
				{
					Console.WriteLine("Шестая попытка сгенерировать изобюражение изменив промпт...");
					images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage);
				}
			}

			return new ImageResult { Images = images, Msg = msg };
		}

		private async Task<string> OriginalPrompt()
		{
			var dress = await GetDress();
			var becrgound = await Background();
			var decsPhoto = await DecsPhotoNew();
			return face + "\n" + dress + "\n" + bodyType + "\n" + becrgound + "\n" + decsPhoto;
		}

		private string face => "A stunning young woman in her late 20s, with long glossy dark brown hair, radiant warm smile, and natural makeup, at golden hour. ";
		private async Task<string> GetDress()
		{
			var prompt = "Измени или оставь это описание сексуальной девушки. " +
				"Оно должно быть не менее откровенной и вызывающей чем эта: Attire: She is wearing a two-piece bikini in a light, neutral, possibly beige or pale peach color. " +
				"Top: A classic triangle bikini top with thin straps, providing excellent support and emphasizing her ample cleavage. The fabric appears smooth and slightly reflective in the light. " +
				" Bottom: Matching tie-side bikini bottoms, sitting low on her curvy hips, with adjustable strings tied at each side. The cut is moderately revealing but tasteful, accentuating her figure." +
				$"\n\n**Формат ответа:** Строго только готовый промпт на английском, без пояснений и предодложений разных вариантов.";
			return await _generativeLanguageModel.GeminiRequest(prompt);
		}
		private string bodyType = "Body Type: She has a very fit, athletic, and notably curvaceous physique. She possesses a remarkably slim waist that contrasts beautifully with her fuller, shapely hips and noticeably plump, rounded breasts. Her body shows clear muscle definition, particularly in her toned arms and a flat, defined abdomen, indicating a very well-exercised and strong yet feminine physique.";

		private async Task<string> Background()
		{
			var prompt = "Придумай описание местанахождения девушки, модельной внешности, где бы она могла оказаться. Например где-то у себя дома в квартире. ВОзможно лежит на кровати и т.п." +
				"Вот пример стиля описания места: Background: soft white sandy beach, turquoise ocean waves gently rolling, palm trees silhouetted against a warm orange-pink sunset sky." +
				$"\n\n**Формат ответа:** Строго только готовый промпт на английском, без пояснений и предодложений разных вариантов.";
			return await _generativeLanguageModel.GeminiRequest(prompt);
		}
		private string descPhoto => "Soft ambient lighting, cinematic shallow depth of field, photorealistic, ultra-detailed skin texture, 8K resolution, professional fashion photography style, sharp focus on face and figure. --ar 9:16 --v 6.0 --style raw --q 2 --s 750";
		private async Task<string> DecsPhotoNew()
		{
			var prompt = "Измени если посчитаешь нужным этот промпт для описания стиля фотографии для девушки модели:" +
				"Overall Aesthetic:\r\nThe image has a high-quality, professional, and slightly editorial feel. It is well-composed, with a shallow depth of field that keeps the woman in sharp focus while subtly blurring the background. The colors are warm and natural, evoking a sense of calm and beauty." +
				$"\n\n**Формат ответа:** Строго только готовый промпт на английском, без пояснений и предодложений разных вариантов.";
			return await _generativeLanguageModel.GeminiRequest(prompt);
		}
	}

	public class ImageResult
	{
		public List<string> Images { get; set; }
		public Message Msg { get; set; }
	}
}
