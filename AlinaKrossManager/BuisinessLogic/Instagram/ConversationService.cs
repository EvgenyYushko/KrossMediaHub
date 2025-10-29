namespace AlinaKrossManager.BuisinessLogic.Instagram
{
	public class ConversationService
	{
		private readonly Dictionary<string, UserConversation> _conversations = new();
		private readonly int _maxHistoryLength = 50; // Максимальное количество сообщений в истории
		private readonly ILogger<ConversationService> _logger;

		public ConversationService(ILogger<ConversationService> logger)
		{
			_logger = logger;
		}

		// Добавляем сообщение пользователя
		public void AddUserMessage(string userId, string messageText)
		{
			if (string.IsNullOrEmpty(messageText))
				return;

			EnsureUserConversation(userId);

			_conversations[userId].Messages.Add(new ChatMessage
			{
				Sender = "user",
				Text = messageText,
				Timestamp = DateTime.UtcNow
			});

			TrimHistory(userId);
			_logger.LogInformation($"Added user message to history for {userId}");
		}

		// Добавляем ответ Алины
		public void AddBotMessage(string userId, string messageText)
		{
			if (string.IsNullOrEmpty(messageText))
				return;

			EnsureUserConversation(userId);

			_conversations[userId].Messages.Add(new ChatMessage
			{
				Sender = "alina",
				Text = messageText,
				Timestamp = DateTime.UtcNow
			});

			TrimHistory(userId);
			_logger.LogInformation($"Added bot message to history for {userId}");
		}

		// Получаем историю в формате для промпта
		public string GetFormattedHistory(string userId)
		{
			if (!_conversations.ContainsKey(userId) || !_conversations[userId].Messages.Any())
				return "No previous conversation history.";

			var history = _conversations[userId].Messages
				.OrderBy(m => m.Timestamp)
				.TakeLast(_maxHistoryLength)
				.ToList();

			var formattedHistory = new List<string>();

			foreach (var message in history)
			{
				var speaker = message.Sender == "user" ? "User" : "Alina";
				formattedHistory.Add($"{speaker}: {message.Text}");
			}

			return string.Join("\n", formattedHistory);
		}

		// Очищаем историю (например, при начале нового диалога)
		public void ClearHistory(string userId)
		{
			if (_conversations.ContainsKey(userId))
			{
				_conversations[userId].Messages.Clear();
				_logger.LogInformation($"Cleared conversation history for {userId}");
			}
		}

		private void EnsureUserConversation(string userId)
		{
			if (!_conversations.ContainsKey(userId))
			{
				_conversations[userId] = new UserConversation { UserId = userId };
			}
			_conversations[userId].LastActivity = DateTime.UtcNow;
		}

		private void TrimHistory(string userId)
		{
			if (_conversations.ContainsKey(userId) &&
				_conversations[userId].Messages.Count > _maxHistoryLength)
			{
				_conversations[userId].Messages = _conversations[userId].Messages
					.TakeLast(_maxHistoryLength)
					.ToList();
			}
		}

		// Очистка старых диалогов (можно вызывать периодически)
		public void CleanupOldConversations(TimeSpan maxAge)
		{
			var cutoff = DateTime.UtcNow - maxAge;
			var oldUsers = _conversations.Where(kvp => kvp.Value.LastActivity < cutoff).ToList();

			foreach (var oldUser in oldUsers)
			{
				_conversations.Remove(oldUser.Key);
				_logger.LogInformation($"Removed old conversation for {oldUser.Key}");
			}
		}
	}

	public class ChatMessage
	{
		public string Sender { get; set; } // "user" или "alina"
		public string Text { get; set; }
		public DateTime Timestamp { get; set; }
	}

	public class UserConversation
	{
		public string UserId { get; set; }
		public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
		public DateTime LastActivity { get; set; } = DateTime.UtcNow;
	}
}
