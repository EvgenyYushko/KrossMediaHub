namespace AlinaKrossManager.BuisinessLogic.Instagram
{
	public class ConversationServiceWhatsApp
	{
		private readonly Dictionary<string, UserConversationWhatsApp> _conversations = new();
		private readonly int _maxHistoryLength = 40; // Максимальное количество сообщений в истории
		private readonly ILogger<ConversationServiceWhatsApp> _logger;

		public ConversationServiceWhatsApp(ILogger<ConversationServiceWhatsApp> logger)
		{
			_logger = logger;
		}

		public List<string> GetAllUserConversations()
		{
			return _conversations.Keys.ToList();
		}

		// Добавляем сообщение пользователя
		public void AddUserMessage(string userId, string messageText)
		{
			if (string.IsNullOrEmpty(messageText))
				return;

			EnsureUserConversation(userId);

			_conversations[userId].Messages.Add(new ChatMessageWhatsApp
			{
				Sender = "User",
				Text = messageText,
				Timestamp = DateTime.UtcNow,
				Readed = false
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

			_conversations[userId].Messages.Add(new ChatMessageWhatsApp
			{
				Sender = "Alina",
				Text = messageText,
				Timestamp = DateTime.UtcNow
			});

			TrimHistory(userId);
			_logger.LogInformation($"Added bot message to history for {userId}");
		}

		// Получаем историю в формате для промпта
		public string GetFormattedHistory(string userId)
		{
			var history = GetHistory(userId);
			if (history == null)
			{
				return "No previous conversation history.";
			}

			var formattedHistory = new List<string>();

			foreach (var message in history)
			{
				formattedHistory.Add($"{message.Sender}{(message.Readed ? "" : "[Unreaded]")}: {message.Text}");
			}

			return string.Join("\n", formattedHistory);
		}

		public bool MakeHistoryAsReaded(string userId)
		{
			if (!_conversations.ContainsKey(userId) || !_conversations[userId].Messages.Any())
				return false;

			_conversations[userId].Messages.ForEach(m => m.Readed = true);

			return true;
		}

		public List<ChatMessageWhatsApp> GetHistory(string userId)
		{
			if (!_conversations.ContainsKey(userId) || !_conversations[userId].Messages.Any())
				return null;

			return _conversations[userId].Messages
				.OrderBy(m => m.Timestamp)
				.TakeLast(_maxHistoryLength)
				.ToList();
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
				_conversations[userId] = new UserConversationWhatsApp { UserId = userId };
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

	public class ChatMessageWhatsApp
	{
		public string Sender { get; set; } // "user" или "alina"
		public string Text { get; set; }
		public bool Readed { get; set; }
		public DateTime Timestamp { get; set; }
	}

	public class UserConversationWhatsApp
	{
		public string UserId { get; set; }
		public List<ChatMessageWhatsApp> Messages { get; set; } = new List<ChatMessageWhatsApp>();
		public DateTime LastActivity { get; set; } = DateTime.UtcNow;
	}
}
