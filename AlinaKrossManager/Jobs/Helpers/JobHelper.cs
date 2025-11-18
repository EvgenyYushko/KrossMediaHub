namespace AlinaKrossManager.Jobs.Helpers
{
	public static class JobHelper
	{
		public static string INSTAGRAM_DAILY_POST_JOB_KEY = "InstagramDailyPostJob";
		public static string INSTAGRAM_MESSAGE_MANAGER_JOB_KEY = "InstagramMessageManagerJob";

		public static List<JobsSetting> JobSettings { get; set; } = new();

		static JobHelper()
		{
			//JobSettings.Add(new() { Type = typeof(HabrJob), Key = HabrKey, Time = "0 0 11 * * ?", Castum = true });
			//JobSettings.Add(new() { Type = typeof(OnlinerJob), Key = OnlinerKey, Time = "0 0 13 * * ?", Castum = true });
			//JobSettings.Add(new() { Type = typeof(CurrencyJob), Key = CurrencyKey, Time = "0 0 9 * * ?", Castum = true });
			//JobSettings.Add(new() { Type = typeof(AllNewsJob), Key = AllNewsKey, Time = "", Castum = true });
			//JobSettings.Add(new() { Type = typeof(WeatherJob), Key = WeatherKey, Time = "", Castum = true });

			JobSettings.Add(new() { Type = typeof(DilyPostJob), Key = INSTAGRAM_DAILY_POST_JOB_KEY, Time = DilyPostJob.Time, Castum = false });
			JobSettings.Add(new() { Type = typeof(InstagramMessageManagerJob), Key = INSTAGRAM_MESSAGE_MANAGER_JOB_KEY, Time = InstagramMessageManagerJob.Time, Castum = false });
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
