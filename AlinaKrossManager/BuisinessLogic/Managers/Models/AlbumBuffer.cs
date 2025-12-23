namespace AlinaKrossManager.BuisinessLogic.Managers.Models
{
	public class AlbumBuffer
	{
		public List<string> FileIds { get; set; } = new();
		// Добавляем список для ID сообщений, чтобы потом их удалить
		public List<int> MessageIds { get; set; } = new();
		public string Caption { get; set; }
		public CancellationTokenSource TokenSource { get; set; }
		public long ChatId { get; set; }
	}
}
