using Telegram.Bot.Types;
using Quartz;
using Quartz.Impl.Matchers;
using static AlinaKrossManager.Helpers.Logger;
using TimeZoneHelper = AlinaKrossManager.Helpers.TimeZoneHelper;

namespace AlinaKrossManager.Jobs.Base
{
	public class ScheduleInspectorService
	{
		private readonly IScheduler _scheduler;

		public ScheduleInspectorService(IScheduler scheduler)
		{
			_scheduler = scheduler;
		}

		//private TriggerKey GetTriggerKey(string key, long chatId) => new TriggerKey($"{key}Trigger_{chatId}", "Chats");

		//public bool ValidateCronExpression(string expression)
		//{
		//	try
		//	{
		//		new CronExpression(expression);
		//		return true;
		//	}
		//	catch
		//	{
		//		return false;
		//	}
		//}

		//public async Task InitializeAsync()
		//{
		//	//var chats = _telegramUserService.GetAllChats();

		//	//foreach (var chat in chats)
		//	//{
		//	//	if (chat.ChatPermission.SendNews)
		//	//	{
		//	//		await UpdateChatScheduleAsync(chat.TelegramChatId, AllNewsKey, chat.ChatPermission.AllNewsCronExpression);
		//	//	}

		//	//	if (chat.ChatPermission.SendHabr)
		//	//	{
		//	//		await UpdateChatScheduleAsync(chat.TelegramChatId, HabrKey, chat.ChatPermission.HabrCronExpression);
		//	//	}

		//	//	if (chat.ChatPermission.SendCurrency)
		//	//	{
		//	//		await UpdateChatScheduleAsync(chat.TelegramChatId, CurrencyKey, chat.ChatPermission.CurrencyCronExpression);
		//	//	}

		//	//	if (chat.ChatPermission.SendOnliner)
		//	//	{
		//	//		await UpdateChatScheduleAsync(chat.TelegramChatId, OnlinerKey, chat.ChatPermission.OnlinerCronExpression);
		//	//	}

		//	//	if (chat.ChatPermission.SendWeather)
		//	//	{
		//	//		await UpdateChatScheduleAsync(chat.TelegramChatId, WeatherKey, chat.ChatPermission.WeatherCronExpression);
		//	//	}
		//	//}
		//}

		//public async Task UpdateChatScheduleAsync(long chatId, string key, string cronExpression)
		//{
		//	//if (string.IsNullOrWhiteSpace(cronExpression))
		//	//{
		//	//	System.Console.WriteLine("cronExpression not be null");
		//	//	return;
		//	//}

		//	//var triggerKey = GetTriggerKey(key, chatId);

		//	//// Проверяем существующий триггер
		//	//var oldTrigger = await _scheduler.GetTrigger(triggerKey);

		//	//if (oldTrigger != null)
		//	//{
		//	//	// Обновляем триггер
		//	//	var newTrigger = TriggerBuilder.Create()
		//	//		.WithIdentity(triggerKey)
		//	//		.ForJob($"{key}Job") // Привязываем к шаблону джобы
		//	//		.WithCronSchedule(cronExpression, x => x.InTimeZone(TimeZoneHelper.GetTimeZoneInfo()))
		//	//		.UsingJobData(ChatId, chatId.ToString()) // Передаем chatId в JobDataMap
		//	//		.Build();

		//	//	await _scheduler.RescheduleJob(triggerKey, newTrigger);
		//	//}
		//	//else
		//	//{
		//	//	// Создаем новый триггер
		//	//	var trigger = TriggerBuilder.Create()
		//	//		.WithIdentity(triggerKey)
		//	//		.ForJob($"{key}Job")
		//	//		.WithCronSchedule(cronExpression, x => x.InTimeZone(TimeZoneHelper.GetTimeZoneInfo()))
		//	//		.UsingJobData(ChatId, chatId.ToString())
		//	//		.Build();

		//	//	await _scheduler.ScheduleJob(trigger);
		//	//}
		//}

		//public async Task RemoveChatTrigger(string key, long chatId)
		//{
		//	var triggerKey = GetTriggerKey(key, chatId);

		//	if (await _scheduler.CheckExists(triggerKey))
		//	{
		//		// Удаляем только триггер (сама джоба останется)
		//		await _scheduler.UnscheduleJob(triggerKey);
		//	}
		//}

		public async Task PrintScheduleInfo()
		{
			var jobGroups = await _scheduler.GetJobGroupNames();

			foreach (var group in jobGroups)
			{
				var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group));

				foreach (var jobKey in jobKeys)
				{
					var jobDetail = await _scheduler.GetJobDetail(jobKey);
					var triggers = await _scheduler.GetTriggersOfJob(jobKey);

					Log($"Job: {jobKey.Name}");

					foreach (var trigger in triggers)
					{
						var nextFireTimeUtc = trigger.GetNextFireTimeUtc();
						var nextFireTimeLocal = TimeZoneHelper.ConvertFromUtc(nextFireTimeUtc);

						Log($"  Trigger: {trigger.Key.Name}");
						//Log($"  Next fire time (UTC): {nextFireTimeUtc}");
						Log($"  Next fire time (Local): {nextFireTimeLocal}");
						Log($"  Schedule: {(trigger as ICronTrigger)?.CronExpressionString}");
					}
				}
			}
		}
	}
}
