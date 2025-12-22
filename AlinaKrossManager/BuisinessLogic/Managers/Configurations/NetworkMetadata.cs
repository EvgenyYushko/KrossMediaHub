using AlinaKrossManager.BuisinessLogic.Managers.Enums;

namespace AlinaKrossManager.BuisinessLogic.Managers.Configurations
{
	// --- 1. НАСТРОЙКИ СЕТЕЙ (ЕДИНАЯ ТОЧКА КОНФИГУРАЦИИ) ---
	// Чтобы добавить соцсеть, добавьте её в Enum и сюда.
	public static class NetworkMetadata
	{
		public static readonly Dictionary<NetworkType, (string Name, string Icon)> Info = new()
		{
			{ NetworkType.Instagram, ("Instagram", "📸") },
			{ NetworkType.Facebook, ("Facebook", "👥") } ,
			{ NetworkType.BlueSky,   ("BlueSky", "💠") },
			{ NetworkType.TelegramPublic, ("Telegram Public", "📱") },
			{ NetworkType.X, ("X", "✗") },
			{ NetworkType.TelegramPrivate, ("Telegram Private", "💋") },
		};

		// Список поддерживаемых сетей (исключая All)
		public static IEnumerable<NetworkType> Supported => Info.Keys;

		// Куда постить, если нажали "Во все Публичные"
		public static readonly List<NetworkType> PublicSet = new()
		{
			NetworkType.Instagram,
			NetworkType.Facebook,
			NetworkType.BlueSky,
			NetworkType.TelegramPublic,
			NetworkType.X,
		};

		// Куда постить, если нажали "Во все Приватные"
		public static readonly List<NetworkType> PrivateSet = new()
		{
			NetworkType.TelegramPrivate // Пока только телеграм
			// В будущем добавите сюда другие приватные каналы
		};
	}
}
