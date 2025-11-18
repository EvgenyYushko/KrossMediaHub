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
		public static string Time => "0 0 10 * * ?";

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
				await _instagramService.SendInstagramMessage("1307933750574022", "Привет, я сейчас буду выкладывать новый пост)");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}

			//try
			//{
			//	var allUsers = _conversationService.GetAllUserConversations();

			//	Console.WriteLine("Count All Users: " + allUsers.Count);
			//	foreach (var userId in allUsers)
			//	{
			//		Console.WriteLine("UsersId: " + userId);

			//		var userHistory = _conversationService.GetHistory(userId);
			//		if (userHistory != null)
			//		{
			//			var lastMsg = userHistory.TakeLast(1).FirstOrDefault();
			//			Console.WriteLine($"Last msg Sender: {lastMsg.Sender}, Text: {lastMsg.Text}");

			//			if (lastMsg != null && lastMsg.Sender == "User")
			//			{
			//				await _instagramService.SendInstagramMessage(userId, "))))");
			//				await Task.Delay(TimeSpan.FromSeconds(5));
			//			}
			//		}
			//	}

			//	foreach (var userId in allUsers)
			//	{
			//		await _instagramService.SendInstagramMessage(userId, "💋");
			//		//Console.WriteLine("начали генерацию фото");
			//		//InstagramMedia randomItem = GetRandomMedia(_mediaList);
			//		//Console.WriteLine("получили фото");
			//		//await SendInstagramPhotoFromUrl(senderId, randomItem.Media_Url);
			//		//Console.WriteLine("закончили фото");

			//		await Task.Delay(TimeSpan.FromSeconds(6));
			//	}
			//}
			//catch (Exception ex)
			//{
			//	Console.WriteLine(ex.ToString());
			//}

			var chatId = 1231047171;

			Console.WriteLine("Генерация сцен для Instagram...\n");

			string promptForCreateImage = null;

			Message msg = null;
			List<string> images = new();
			var random = new Random();

			try
			{
				promptForCreateImage = await OriginalPrompt();

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
					await _telegramService.SendPhotoAlbumAsync(chatId, images, null, "");
				}
				else
				{
					await _telegramService.SendSinglePhotoAsync(chatId, images.First(), null, "");
					//try
					//{
					//	await _telegramService.DeleteMessage(msg.Chat.Id, msg.MessageId);
					//}
					//catch { }
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

			try
			{
				await _instagramService.SendInstagramMessage("1307933750574022", "Всё, я всё выложила, можешь проверить и постатвить лайк 💋");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
		}

		private async Task<ImageResult> CreateImage(int chatId, string promptForCreateImage, Message msg)
		{
			try
			{
				msg = await _telegramService.SendMessage(chatId, promptForCreateImage);
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
					msg = await _telegramService.SendMessage(chatId, promptForCreateImage);
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
				"Оно должно быть не менее откровенной и вызывающей чем эта, возми его за основу: Attire: She is wearing a two-piece bikini in a light, neutral, possibly beige or pale peach color. " +
				"Top: A classic triangle bikini top with thin straps, providing excellent support and emphasizing her ample cleavage. The fabric appears smooth and slightly reflective in the light. " +
				" Bottom: Matching tie-side bikini bottoms, sitting low on her curvy hips, with adjustable strings tied at each side. The cut is moderately revealing but tasteful, accentuating her figure." +
				$"\n\n**Формат ответа:** Строго только готовый промпт на английском, без пояснений и предодложений разных вариантов.";
			return await _generativeLanguageModel.GeminiRequest(prompt);
		}
		private string bodyType = "Body Type: She has a very fit, athletic, and notably curvaceous physique. She possesses a remarkably slim waist that contrasts beautifully with her fuller, shapely hips and noticeably plump, rounded breasts. Her body shows clear muscle definition, particularly in her toned arms and a flat, defined abdomen, indicating a very well-exercised and strong yet feminine physique.";

		private async Task<string> Background()
		{
			var locations = new[]
			{
				// Домашние интимные локации
				"in a cozy apartment bedroom, lying on a soft bed with fluffy pillows",
				"in a modern living room, sitting on a comfortable sofa near a large window",
				"in a stylish kitchen, leaning against the marble countertop",
				"on a balcony with city view, enjoying the sunset",
				"in a bathroom with elegant decorations, near a large mirror",
				"in a walk-in closet, trying on fashionable clothes",
				"in a home office, sitting at a minimalist desk",
				"by the window in a cozy nook, reading a book",
				"in a rooftop garden with panoramic city views",
        
				// Спальня и постельные сцены
				"lying seductively on satin sheets in a dimly lit bedroom",
				"on a luxurious king-size bed surrounded by velvet pillows",
				"in bed wearing delicate lingerie with soft morning light",
				"reclining on a fur rug in front of a fireplace",
				"on a canopy bed with sheer curtains partially drawn",
				"sprawled across a messy bed with crumpled sheets",
				"on a bed covered in rose petals with candlelight",
				"lying on stomach on the bed, looking over shoulder",
				"curled up in fetal position on soft blankets",
				"stretching sensually upon waking up in bed",
        
				// Ванная комната и душевые сцены
				"stepping out of shower with wet hair and steam",
				"in a bubble bath surrounded by candles",
				"leaning against bathroom counter in towel",
				"sitting on edge of bathtub with legs crossed",
				"steam-filled bathroom with foggy mirror",
				"in a luxurious jacuzzi with rose petals",
				"drying hair with towel in front of mirror",
				"applying makeup at vanity in silk robe",
				"relaxing in sauna with beads of sweat",
        
				// Гардеробная и примерочная
				"trying on lingerie in walk-in closet",
				"adjusting stockings in front of full-length mirror",
				"wearing only boyfriend's shirt in closet",
				"selecting clothes from extensive wardrobe",
				"in lingerie surrounded by designer clothes",
				"wearing silk robe that's slightly open",
				"barefoot on plush carpet in dressing room",
        
				// Кухня и интимные моменты
				"drinking wine alone at kitchen island",
				"leaning against refrigerator in nightgown",
				"sitting on kitchen counter barefoot",
				"preparing breakfast wearing only apron",
				"eating fruits sensually at kitchen table",
        
				// Гостиная и расслабляющие позы
				"curled up on sofa with blanket",
				"lying on Persian rug with book",
				"stretching like cat on floor pillows",
				"lounging on chaise lounge dramatically",
				"sitting by window in sheer curtains",
        
				// Балкон и приватные открытые пространства
				"on balcony wearing only silk robe at night",
				"leaning over balcony railing in moonlight",
				"sipping coffee on balcony in morning",
				"watching rain from covered balcony",
				"sunbathing on private terrace",
        
				// Неожиданные интимные локации
				"in home library leaning against bookshelf",
				"on staircase sitting on steps",
				"in wine cellar holding glass",
				"by piano in living room",
				"in attic surrounded by memories",
        
				// Сезонные и погодные сцены
				"curled up by window during thunderstorm",
				"in bed with snow falling outside",
				"under blanket during rainy afternoon",
				"by fireplace on cold winter night",
				"with summer breeze blowing curtains",
        
				// Утренние и вечерние сцены
				"waking up with messy hair and sleepy eyes",
				"morning light streaming across bed",
				"getting ready for bed in nightwear",
				"late night insomnia in living room",
				"early morning yoga in bedroom",
        
				// Эмоциональные и мечтательные сцены
				"lost in thought while staring out window",
				"crying softly in dimly lit room",
				"laughing to self while remembering something",
				"dancing alone in living room",
				"singing quietly while doing chores",
        
				// Сенсорные и тактильные сцены
				"feeling texture of velvet curtains",
				"running fingers through own hair",
				"touching own skin softly",
				"playing with necklace absentmindedly",
				"massaging own feet after long day",
        
				// Игривые и кокетливые сцены
				"peeking from behind door playfully",
				"hiding behind sheer canopy",
				"looking over shoulder seductively",
				"biting lip while thinking",
				"playing with hem of short dress",
        
				// Романтические и ностальгические сцены
				"looking at old photos in attic",
				"holding love letter in bedroom",
				"wearing partner's clothing",
				"surrounded by dried flowers",
				"with wedding dress in background"
			};

			var random = new Random();
			var randomLocation = locations[random.Next(locations.Length)];

			var prompt = $"Beautiful girl with model appearance {randomLocation}. " +
				 "Soft natural lighting, photorealistic style, high quality." +
				 "\n\n**Response format:** Strictly only the ready prompt in English, without explanations or multiple options";
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
