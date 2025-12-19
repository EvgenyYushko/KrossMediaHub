using AlinaKrossManager.BuisinessLogic.Managers.Enums;

namespace AlinaKrossManager.BuisinessLogic.Managers.Models
{
	public class UserSession
	{
		public UserState State { get; set; } = UserState.None;
		public NetworkType SelectedNetwork { get; set; } = NetworkType.All;
		public Guid? EditingPostId { get; set; }
		public List<int> ActiveAlbumMessageIds { get; set; } = new();
		// Хранит режим загрузки (какую кнопку нажал юзер: Публичную или Приватную)
		public AccessLevel UploadAccess { get; set; } = AccessLevel.Public;

		// Хранит последний выбранный фильтр в списке, чтобы кнопка "Назад" возвращала куда надо
		public AccessFilter LastFilter { get; set; } = AccessFilter.All;
	}
}
