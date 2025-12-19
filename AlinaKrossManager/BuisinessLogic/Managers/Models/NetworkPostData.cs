using AlinaKrossManager.BuisinessLogic.Managers.Enums;

namespace AlinaKrossManager.BuisinessLogic.Managers.Models
{
	public class NetworkPostData
	{
		public string Caption { get; set; } = "";
		public SocialStatus Status { get; set; } = SocialStatus.None;
	}
}
