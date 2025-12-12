using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlinaKrossManager.DataAccess.Models
{
	// Главная таблица постов
	[Table("Posts")]
	public class PostEntity
	{
		[Key]
		public Guid Id { get; set; }

		public DateTime CreatedAt { get; set; }

		// Храним Enum как int (0 = Public, 1 = Private)
		public int AccessLevel { get; set; }

		// Связь с картинками (Один пост -> Много картинок)
		public virtual List<PostImageEntity> Images { get; set; } = new();

		// Связь с состояниями сетей (Один пост -> Много состояний)
		public virtual List<NetworkStateEntity> NetworkStates { get; set; } = new();
	}
}
