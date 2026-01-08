using AlinaKrossManager.BuisinessLogic.Managers;
using AlinaKrossManager.BuisinessLogic.Managers.Enums;

namespace AlinaKrossManager.Helpers
{
	public static class TelegramQueueHelper
	{
		public static void NewMethod(AccessLevel access, PostService.PostCountsDto statsPrivate, System.Text.StringBuilder sb)
		{
			sb.AppendLine($"🔒 **{access.ToString()} Режим**");
			sb.AppendLine($"⏳ Очередь: **{statsPrivate.Pending}**");
			sb.AppendLine($"✅ Готово: **{statsPrivate.Published}**");
			sb.AppendLine($"❌ Ошибки: **{statsPrivate.Errors}**");
			sb.AppendLine($"Всего: {statsPrivate.Total}");
		}
	}
}
