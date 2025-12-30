using System.Text;
using AlinaKrossManager.BuisinessLogic.Services.Base;
using AlinaKrossManager.Services;
using Newtonsoft.Json;
using Tweetinvi;

namespace AlinaKrossManager.BuisinessLogic.Services
{
    public class XService : SocialBaseService
    {
        private readonly TwitterClient _twitterClient;

        public XService(IGenerativeLanguageModel generativeLanguageModel, TwitterClient twitterClient) : base(generativeLanguageModel)
        {
            _twitterClient = twitterClient;
        }

        public override string ServiceName => "XService";

        /// <summary>
        /// Метод для публикации ТОЛЬКО текста
        /// </summary>
        public async Task<bool> CreateTextPost(string text)
        {
            try
            {
                var tweetRequest = new TweetV2Request
                {
                    Text = text
                };

                return await SendTweetV2Async(tweetRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке текстового твита: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Метод для публикации текста с картинками
        /// </summary>
        public async Task<bool> CreatePostPost(string caption, List<string> base64Files)
        {
            // Твиттер разрешает максимум 4 картинки на один твит
            var filesToUpload = base64Files?.Take(4).ToList();

            try
            {
                var uploadedMediaIds = new List<string>();

                // 1. Загрузка картинок (V1.1 API)
                if (filesToUpload != null && filesToUpload.Any())
                {
                    foreach (var base64String in filesToUpload)
                    {
                        try
                        {
                            // А. Очистка Base64
                            string cleanBase64 = base64String;
                            if (cleanBase64.Contains(","))
                            {
                                cleanBase64 = cleanBase64.Split(',')[1];
                            }

                            // Б. Конвертация
                            byte[] imageBytes = Convert.FromBase64String(cleanBase64);

                            Console.WriteLine("Загрузка изображения в X...");

                            // В. Загрузка
                            var uploadedMedia = await _twitterClient.Upload.UploadTweetImageAsync(imageBytes);

                            if (uploadedMedia != null)
                            {
                                Console.WriteLine($"Фото загружено. ID: {uploadedMedia.Id}");
                                uploadedMediaIds.Add(uploadedMedia.Id.ToString());
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Не удалось загрузить одно из фото: {ex.Message}");
                        }
                    }
                }

                // 2. Формирование запроса V2
                var tweetRequest = new TweetV2Request
                {
                    Text = caption
                };

                // Если удалось загрузить картинки, прикрепляем их
                if (uploadedMediaIds.Count > 0)
                {
                    tweetRequest.Media = new TweetV2Media
                    {
                        MediaIds = uploadedMediaIds
                    };
                }

                // 3. Отправка через общий метод
                return await SendTweetV2Async(tweetRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Общая ошибка метода публикации с фото: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Приватный метод для непосредственной отправки JSON в Twitter V2 API
        /// </summary>
        private async Task<bool> SendTweetV2Async(TweetV2Request tweetRequest)
        {
            try
            {
                Console.WriteLine("Публикуем пост в X (V2 API)...");

                var result = await _twitterClient.Execute.AdvanceRequestAsync(request =>
                {
                    request.Query.Url = "https://api.twitter.com/2/tweets";
                    request.Query.HttpMethod = Tweetinvi.Models.HttpMethod.POST;

                    var jsonBody = JsonConvert.SerializeObject(tweetRequest);
                    request.Query.HttpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                });

                if (result.Response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Успех! Твит опубликован.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Ошибка публикации X: {result.Response.StatusCode}");
                    Console.WriteLine($"Детали ошибки: {result.Content}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при выполнении запроса к API X: {ex.Message}");
                return false;
            }
        }

        public static string GetBaseDescriptionPrompt(string base64Img)
        {
            return "Придумай красивое, краткое описание на английском языке, возможно добавь эмодзи, к посту в X(Twitter) под постом с фотографией. " +
                $"А так же придумай не более 15 хештогов, они должны соответствовать " +
                $"теме изображения, а так же всегда включать пару обязательных хештегов для указания что это AI контент, например #aigirls. " +
                $"Вот само изображение: {base64Img}" +
                $"\n\n Формат ответа: Ответь строго только готовое описание с хештегами, " +
                $"без всякого рода ковычек и экранирования. " +
                $"Пример ответа: ✨ Feeling the magic of the sunset.\r\n\r\n#ai #aiart #aigenerated #aiartwork #artificialintelligence " +
                $"#neuralnetwork #digitalart #generativeart #aigirl #virtualmodel #digitalmodel #aiwoman #aibeauty #aiportrait #aiphotography";
        }
    }

    public class TweetV2Request
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("media", NullValueHandling = NullValueHandling.Ignore)]
        public TweetV2Media Media { get; set; }
    }

    public class TweetV2Media
    {
        [JsonProperty("media_ids")]
        public List<string> MediaIds { get; set; }
    }
}