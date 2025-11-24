namespace AlinaKrossManager.Helpers
{
	public static class AiHelper
	{
		public static int ParseBooleanResponse(string llmResponse)
		{
			if (string.IsNullOrWhiteSpace(llmResponse))
				return 0; // Если пусто — считаем, что нет

			// Убираем всё лишнее (пробелы, переносы строк, кавычки, markdown)
			// Например, превратит "  1. \n" в "1"
			string clean = llmResponse.Trim()
									  .Replace(".", "")
									  .Replace("`", "")
									  .Replace("'", "")
									  .Replace("\"", "");

			// Пытаемся превратить в число
			if (int.TryParse(clean, out int result))
			{
				// Проверяем, что это именно 1 или 0 (на случай если модель сглючит и вернет 5)
				if (result == 1) return 1;
				if (result == 0) return 0;
			}

			// Если модель вернула текст типа "Это русский", ищем цифру 1 внутри
			if (llmResponse.Contains("1")) return 1;

			// Безопасный дефолт - если ничего не понятно, считаем за 0
			return 0;
		}
	}
}
