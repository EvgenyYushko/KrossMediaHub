namespace AlinaKrossManager.Models
{
	public class TextToSpeechModel
	{
		public string Text { get; set; }
		public string LanguageCode { get; set; }
		public string LanguageName { get; set; }
		public string AudioEncoding { get; set; }

		public long ChatId { get; set; }
		public int MessageId { get; set; }
	}
}
