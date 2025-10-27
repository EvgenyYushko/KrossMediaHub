namespace AlinaKrossManager.Helpers
{
	public static class Logger
	{
		public static void Log(Exception ex, string text)
		{
			Console.WriteLine(ex.Message.ToString() + "\n\n" + text);
		}

		public static void Log(string text)
		{
			Console.WriteLine(text);
		}
		
		public static void Log(object obj)
		{
			Console.WriteLine(obj);
		}
	}
}
