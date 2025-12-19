namespace AlinaKrossManager.BuisinessLogic.Managers.Models
{
	public class AlbumBuffer
	{
		public List<string> FileIds { get; set; } = new();
		public string Caption { get; set; }
		public CancellationTokenSource TokenSource { get; set; } // Чтобы сбрасывать таймер
		public long ChatId { get; set; }
	}
}
