using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Services;
using AlinaKrossManager.BuisinessLogic.Services.Instagram;
using AlinaKrossManager.Helpers;
using AlinaKrossManager.Jobs.Base;
using AlinaKrossManager.Services;
using Quartz;
using Telegram.Bot.Types;

namespace AlinaKrossManager.Jobs
{
	[DisallowConcurrentExecution]
	public class DilyPostJob : SchedulerJob
	{
		public static string Time => "0 30 10,14,16 * * ?";

		private readonly InstagramService _instagramService;
		private readonly TelegramService _telegramService;
		private readonly TelegramManager _telegramManager;

		public DilyPostJob(IServiceProvider serviceProvider
			, InstagramService instagramService
			, IGenerativeLanguageModel generativeLanguageModel
			, TelegramService telegramService
			, TelegramManager telegramManager
		)
			: base(serviceProvider, generativeLanguageModel)
		{
			_instagramService = instagramService;
			_telegramService = telegramService;
			_telegramManager = telegramManager;
		}

		public override async Task Execute(IJobExecutionContext context)
		{
			//return;
			try
			{
				await _instagramService.SendInstagramAdminMessage("Привет, я сейчас буду выкладывать новый пост)");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}

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
					var imagesRes = await CreateImage(promptForCreateImage, msg);
					images = imagesRes.Images;
					msg = imagesRes.Msg;

					var promptVar =
						$"Измени этот шикарный промпт таким образом, что бы эта девушка немного повернулась к нам и стало более отчётливо видны её бёдра или же просто поменяй её позу" +
						$"Вот этот промпт:\n\n{promptForCreateImage}" +
						$"\n\n**Формат ответа:** Только готовый промпт на английском, без пояснений.";
					promptForCreateImage = await _generativeLanguageModel.GeminiRequest(promptVar);

					imagesRes = await CreateImage(promptForCreateImage, msg);

					if (imagesRes.Images.Count > 0)
					{
						foreach (var image in imagesRes.Images)
						{
							images.Add(image);
						}
					}

					promptVar =
						$"Измени этот шикарный промпт таким образом, что бы эта девушка стала выглядеть ещё более вульгарно и вызывающе, но в пределах разумного, что бы пройти цензуру а так же измени позу. " +
						$"Вот этот промпт:\n\n{promptForCreateImage}" +
						$"\n\n**Формат ответа:** Только готовый промпт на английском, без пояснений.";
					promptForCreateImage = await _generativeLanguageModel.GeminiRequest(promptVar);

					imagesRes = await CreateImage(promptForCreateImage, msg);

					if (imagesRes.Images.Count > 0)
					{
						foreach (var image in imagesRes.Images)
						{
							images.Add(image);
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
				await _telegramService.SendMessage("Не удалось сгенерировать изображения");
				return;
			}

			//if (images.Count > 1 && random.Next(2) == 1)
			//{
			//	images.Reverse();
			//}

			Message[] loadedPictureMessages = null;
			try
			{
				if (images.Count > 1)
				{
					loadedPictureMessages = await _telegramService.SendPhotoAlbumAsync(images, null, "");
				}
				else
				{
					var message = await _telegramService.SendSinglePhotoAsync(images.First(), null, "");
					loadedPictureMessages = [message];
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
			}

			return;

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
					await _telegramService.SendMessage(description);
				}
				catch { }

				_telegramService.UpdateCaptionMediaGrup(loadedPictureMessages.FirstOrDefault(), description);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}

			// пока не будем автоматически выкладывать в инсту.
			return;

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
						msg = await _telegramService.SendMessage(msgRes);
					}
					catch { }

					try
					{
						await Task.Delay(TimeSpan.FromSeconds(15));
						//await _telegramManager.InstagramStoryHandler(null, loadedPictureMessages?.FirstOrDefault(), new());
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Ошибка создания сторис: {ex}");
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

		private async Task<ImageResult> CreateImage(string promptForCreateImage, Message msg)
		{
			int chatId = TelegramService.EVGENY_YUSHKO_TG_ID;
			try
			{
				msg = await _telegramService.SendMessage(promptForCreateImage.ToBlockQuote());
			}
			catch { }

			Console.WriteLine("Первая попытка сгенерировать изобюражение...");
			List<string> images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage, 2);
			if (images.Count == 0)
			{
				Console.WriteLine("Вторая попытка сгенерировать изобюражение...");
				images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage, 2);
			}
			if (images.Count == 0)
			{
				Console.WriteLine("Третья попытка сгенерировать изобюражение...");
				images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage, 2);
			}

			if (images.Count == 0)
			{
				string promptVar = "По этому промпту AI не хочет генерировтаь изображение, возможно оно не проходит цензуру. Попробуй немного его смягчить " +
					$", вот этот промпт: {promptForCreateImage}" +
					$"\n\n**Формат ответа:** Только готовый промпт на английском, без пояснений.";
				promptForCreateImage = await _generativeLanguageModel.GeminiRequest(promptVar);

				try
				{
					msg = await _telegramService.SendMessage(promptForCreateImage.ToBlockQuote());
				}
				catch { }

				Console.WriteLine("Четвёртая попытка сгенерировать изобюражение изменив промпт...");
				images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage, 2);
				if (images.Count == 0)
				{
					Console.WriteLine("Пятая попытка сгенерировать изобюражение изменив промпт...");
					images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage, 2);
				}
				if (images.Count == 0)
				{
					Console.WriteLine("Шестая попытка сгенерировать изобюражение изменив промпт...");
					images = await _generativeLanguageModel.GeminiRequestGenerateImage(promptForCreateImage, 2);
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

		private static List<string> usedDress = new List<string>();

		private async Task<string> GetDress()
		{
			// 1. БАЗА ПРОМПТА (стабильная часть, требования)
			string promptBase =
				"Create a very detailed, vivid, and sexually appealing description of a woman's attire. " +
				"The description must be provocative, tasteful, and focus on texture, fit, and how it complements her figure. " +
				"Rules: Do NOT use latex, shiny/PVC materials. " +
				"Core idea for this generation: ";

			// 2. СЛОВАРЬ ПЕРЕМЕННЫХ (случайная выборка)
			// Каждый элемент массива - это "затравка" для ИИ, которую он разовьёт.
			string[] clothingOptions = new string[]
			{
				// Повседневная / Кэжуал
				"A casual yet revealing outfit: a tight, thin white tank top without a bra and very short, frayed denim cut-off shorts.",
				"An off-duty model look: high-waisted, light-wash skinny jeans ripped at the knees and thighs, paired with a tiny, cropped black t-shirt that shows her midriff.",
				"A cozy but sexy look: an oversized, soft knit sweater that falls off one shoulder, worn with tight leggings or very short shorts.",
				"Weekend vibes: perfectly fitting, low-rise vintage Levi's jeans with a simple, tight white ribbed tank top tucked in just at the front.",
				"A relaxed fit: baggy, slouchy cargo pants in olive green, paired with an extremely tight, thin-strapped camisole in black silk.",
				"A streetwear look: oversized hoodie, unzipped to reveal a tight sports bra underneath, with biker shorts that hug every curve.",
    
				// Платья
				"A summer dress: a flowing, knee-length sundress with a deep V-neckline and a slit on the thigh. The fabric is light and clings to her curves.",
				"Night-out attire: a tight, bodycon mini dress made of matte fabric. The dress is backless and has a plunging neckline.",
				"A little black dress reinterpreted: a sleeveless, mock-neck LBD made of stretch velvet, so short it barely covers her thighs, with a cut-out at the waist.",
				"A slip dress: a simple, spaghetti-strap satin slip dress in champagne color, falling just above the knee, hugging her body like a second skin.",
				"A wrap dress: a floral-print wrap dress that ties at the waist, deliberately tied loosely so it reveals significant cleavage and leg with every movement.",
				"A corset dress: a dress with a built-in, structured fabric corset top that dramatically cinches her waist and pushes up her bust, with a flared skirt.",
				"A halter-neck dress: a vibrant, solid-color dress with a high neck at the front that ties behind her neck, leaving her entire back bare down to the waist.",
				"A cut-out dress: a simple, bodycon midi dress in a solid color with bold, geometric cut-outs at the sides and across the ribs.",
    
				// Офисный / Деловой стиль (сексуальный)
				"A chic office look turned provocative: a crisp, white button-down shirt, unbuttoned dangerously low, paired with a tight, high-waisted pencil skirt.",
				"A power suit, deconstructed: a sharply tailored, single-breasted blazer worn with nothing underneath, and matching, ultra-short tailored shorts instead of trousers.",
				"A secretary fantasy: a tight, form-fitting turtleneck sweater in a light color, tucked into a very tight, knee-length pencil skirt with a back zip.",
				"A sophisticated look: a silk, cowl-neck blouse in emerald green, tucked into wide-leg, high-waisted trousers that sit low on her hips.",
				"A modern business look: a sleek, fitted waistcoat (with no shirt underneath) and tailored, straight-leg trousers with a sharp crease.",
    
				// Спортивный / Атлетический
				"A sporty, athletic look: very tight, high-waisted yoga pants that emphasize her hips and butt, and a cropped, fitted sports top.",
				"A runner's outfit: incredibly short, tight running shorts and a loose, sleeveless mesh top that reveals her sports bra underneath.",
				"A gym-to-street look: tight, seamless leggings in a dark color with a subtle, flattering sheen, and an oversized athletic jacket left open.",
				"A tennis outfit: a classic, pleated tennis skirt in white (extremely short), paired with a fitted, collared polo top.",
				"A cyclist's look: skin-tight, padded cycling shorts in a bold color and a matching, zipped cycling jersey, unzipped quite far down.",
    
				// Вечерняя / Гламурная
				"A glamorous evening look: a long, figure-hugging gown with a dramatic open back and a thigh-high slit. The fabric is luxurious and matte.",
				"A cocktail dress: a sequined, mini dress with a deep V-neck that goes down to the navel, held together with a thin chain.",
				"A mermaid silhouette gown: a tight, beaded dress that flares out at the knees, with thin, delicate straps and a low-cut back.",
				"A metallic dress: a minimalist, sleeveless dress in hammered, matte gold or silver fabric that moves liquidly over her body.",
    
				// Бохо / Этнический
				"A bohemian style: a loose, off-the-shoulder peasant blouse with embroidery, combined with a tight, wraparound skirt with a high slit.",
				"A festival look: high-waisted, denim shorts with intricate embroidery, paired with a crochet top that reveals glimpses of skin beneath.",
				"A beach cover-up as an outfit: a sheer, patterned kaftan worn over a simple bandeau top and micro shorts, the sheer fabric hinting at what's underneath.",
				"A gypsy-inspired look: a ruffled, tiered skirt in a bright print, worn with a tight, lace-up corset top.",
    
				// Альтернативная / Рок
				"A rocker/grunge style: a ripped, faded black band t-shirt tied in a knot at the waist, and tight, black leather (not shiny latex) pants or shorts.",
				"A punk look: skin-tight, ripped black skinny jeans, combat boots, and a tight, black mesh long-sleeve top worn over a lace bra.",
				"A gothic romance style: a dramatic, black lace Victorian-style blouse with a high neck and leg-of-mutton sleeves, paired with a tight, modern leather skirt.",
				"A rockabilly/pin-up look: a tight, short-sleeved sweater with a geometric pattern, paired with a full, circle skirt that has a playful, provocative print.",
    
				// Ретро / Винтаж
				"A 1950s pin-up style: a tight, striped boat-neck top and high-waisted, denim capri pants that hug her curves.",
				"A 1970s disco look: flared, high-waisted trousers in a velvety fabric and a shimmering, halter-neck top with a deep plunge.",
				"A 1980s inspired outfit: acid-wash, high-waisted denim shorts and an oversized, off-the-shoulder sweatshirt in a bright color.",
				"A 1990s minimalist look: a simple, spaghetti-strap slip dress in black, worn with a chunky, contrasting sports jacket.",
    
				// Игривая / Кокетливая
				"A preppy style turned sexy: a tight, fitted polo shirt and an extremely short pleated tennis skirt.",
				"A schoolgirl uniform fantasy: a classic, white button-up blouse with the top buttons undone, paired with a drastically shortened plaid skirt.",
				"A librarian fantasy: a sleek, knee-length pencil skirt in a dark color, a sheer, black blouse buttoned up high, and glasses.",
				"A cowgirl aesthetic: ultra-tight, light-blue denim jeans with a ornate belt, and a checked, gingham shirt tied in a knot above her navel.",
    
				// Искусственно откровенная / Вечерняя (не бикини/латтекс)
				"A lingerie-as-outerwear look: a delicate, lace bodysuit in a nude or black color, worn under a sheer, silk robe that is left completely open.",
				"A beach look (not bikini): a very thin, loose-knit crochet cover-up dress that is completely see-through, worn over a simple, solid-color bandeau and boy shorts.",
				"A resort look: a long, flowing, sheer chiffon maxi skirt in a pastel color, paired with a tiny, embellished crop top.",
				"A poolside outfit: a luxurious, Turkish-style towel wrap dress, tied tightly around her body to emphasize her waist and chest, with a deep V-neck.",
    
				// Специфические материалы / Фасоны
				"An outfit focusing on knitwear: a tight, ribbed, long-sleeved turtleneck dress that stretches and clings to every single curve of her body.",
				"A velvet outfit: wide-leg, high-waisted velvet trousers and a matching, backless halter top.",
				"A silk outfit: a set of loose, wide-leg silk trousers and a matching, draped silk camisole that slips off one shoulder.",
				"A lace overlay look: a simple, slim-fit black dress with sheer, long lace sleeves and a lace panel running down the entire torso.",
				"A sheer moment: a maxi skirt made of multiple layers of sheer, black tulle over a silk slip, paired with a simple, tight black tank top.",
    
				// Зимняя / Сезонная
				"A cozy winter look: tight, light-gray cashmere leggings and an oversized, chunky knit sweater with a wide neck that keeps slipping down one arm.",
				"A ski lodge aesthetic: skin-tight, thermal leggings in a pattern, with thick, woolen socks pulled up high, and a snug, fitted turtleneck sweater.",
				"A New Year's Eve party look: a sequined, sleeveless pantsuit with a deep, plunging V-neck that goes all the way to the waist, and wide-leg trousers.",
    
				// Фетиш-эстетика (без латекса)
				"A dominant aesthetic: tailored, high-waisted black trousers, a crisp white shirt with the sleeves rolled up, and sharp, stiletto heels. Authority is sexy.",
				"A harness look: a complex, woven leather harness worn over a simple, elegant silk slip dress, creating a striking contrast.",
				"A vinyl (not latex) look: a matte, non-shiny vinyl pencil skirt and a simple cotton t-shirt, playing with textures.",
				"A corset over clothes: a structured, black underbust corset worn laced tightly over a flowing, white poet's blouse."
			};

			var available = clothingOptions.Except(usedDress).ToArray();

			if (available.Length == 0)
			{
				// Все локации использованы, сбрасываем
				usedDress.Clear();
				available = clothingOptions;
			}

			string dress = available[random.Next(available.Length)];
			usedDress.Add(dress);

			// 4. ФИНАЛЬНЫЙ ПРОМПТ
			string finalPrompt = promptBase + dress +
				"\n\n**Response Format:** Strictly ONLY the final, detailed attire description in English, ready for use. No introductions, explanations, or multiple options.";

			return await _generativeLanguageModel.GeminiRequest(finalPrompt);
		}
		private string bodyType = "Body Type: She has a very fit, athletic, and notably curvaceous physique. She possesses a remarkably slim waist that contrasts beautifully with her fuller, shapely hips and noticeably plump, rounded breasts. Her body shows clear muscle definition, particularly in her toned arms and a flat, defined abdomen, indicating a very well-exercised and strong yet feminine physique.";

		private async Task<string> Background()
		{
			var randomLocation = GetUniqueRandomLocation();

			var prompt = $"Beautiful girl with model appearance {randomLocation}. " +
				 "Soft natural lighting, photorealistic style, high quality." +
				 "\n\n**Response format:** Strictly only the ready prompt in English, without explanations or multiple options";
			return await _generativeLanguageModel.GeminiRequest(prompt);
		}

		private static List<string> usedLocations = new List<string>();
		private static Random random = new Random();

		public static string GetUniqueRandomLocation()
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

			var available = locations.Except(usedLocations).ToArray();

			if (available.Length == 0)
			{
				// Все локации использованы, сбрасываем
				usedLocations.Clear();
				available = locations;
			}

			string location = available[random.Next(available.Length)];
			usedLocations.Add(location);

			return location;
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
