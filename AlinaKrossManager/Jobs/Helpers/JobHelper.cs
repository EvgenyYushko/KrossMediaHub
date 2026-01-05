using AlinaKrossManager.Jobs.Messages;

namespace AlinaKrossManager.Jobs.Helpers
{
	public static class JobHelper
	{
		public static string INSTAGRAM_DAILY_POST_KEY = "InstagramDailyPost";
		public static string INSTAGRAM_ANSWER_MESSAGE_KEY = "InstagramAnswerMessage";
		public static string INSTAGRAM_DAILY_MESSAGE_KEY = "InstagramDailyMessage";
		public static string POST_TO_PUBLIC_FROM_QUEUE = "PostToPublicFromQueue";
		public static string POST_TO_PRIVATE_FROM_QUEUE = "PostToPrivateFromQueue";
		public static string X_DAILY_QUEUE = "XDailyQueue";
		public static string TELEGRAMM_FREE_DAILY_QUEUE = "TelegrammFreeDailyQueue";
		public static string BLUE_SKY_DM_JOB_QUEUE = "BlueSkyDmJob";

		public static List<JobsSetting> JobSettings { get; set; } = new();

		static JobHelper()
		{
			//JobSettings.Add(new() { Type = typeof(HabrJob), Key = HabrKey, Time = "0 0 11 * * ?", Castum = true });
			//JobSettings.Add(new() { Type = typeof(OnlinerJob), Key = OnlinerKey, Time = "0 0 13 * * ?", Castum = true });
			//JobSettings.Add(new() { Type = typeof(CurrencyJob), Key = CurrencyKey, Time = "0 0 9 * * ?", Castum = true });
			//JobSettings.Add(new() { Type = typeof(AllNewsJob), Key = AllNewsKey, Time = "", Castum = true });
			//JobSettings.Add(new() { Type = typeof(WeatherJob), Key = WeatherKey, Time = "", Castum = true });

			JobSettings.Add(new() { Type = typeof(DilyPostJob), Key = INSTAGRAM_DAILY_POST_KEY, Time = DilyPostJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(InstagramAnswerMessageJob), Key = INSTAGRAM_ANSWER_MESSAGE_KEY, Time = InstagramAnswerMessageJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(InstagramDailyMessagesJob), Key = INSTAGRAM_DAILY_MESSAGE_KEY, Time = InstagramDailyMessagesJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(PostToPublicFromQueueJob), Key = POST_TO_PUBLIC_FROM_QUEUE, Time = PostToPublicFromQueueJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(PostToPrivateFromQueueJob), Key = POST_TO_PRIVATE_FROM_QUEUE, Time = PostToPrivateFromQueueJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(XDailyJob), Key = X_DAILY_QUEUE, Time = XDailyJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(TelegrammDailyJob), Key = TELEGRAMM_FREE_DAILY_QUEUE, Time = TelegrammDailyJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(BlueSkyDmJob), Key = BLUE_SKY_DM_JOB_QUEUE, Time = BlueSkyDmJob.Time, Castum = false });
		}
	}

	public class JobsSetting
	{
		public Type Type { get; set; }
		public string Key { get; set; }
		public string Time { get; set; }
		public bool Castum { get; set; }

		public override string ToString()
		{
			return $"Key={Key}, Time ={Time}, Castum = {Castum}";
		}
	}
}
