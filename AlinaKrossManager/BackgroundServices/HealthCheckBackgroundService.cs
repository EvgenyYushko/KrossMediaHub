using static AlinaKrossManager.Constants.AppConstants;
using static AlinaKrossManager.Helpers.Logger;

namespace AlinaKrossManager.BackgroundServices
{
	public class HealthCheckBackgroundService : BackgroundService
	{
		private const string HEALTH_URL = "/health";
		private readonly HttpClient _httpClient;

		private static List<string> URLS = new();

		private void InitUrsl()
		{
			URLS.Add(APP_URL + HEALTH_URL);
			//URLS.Add("https://mail-service-eu04.onrender.com" + HEALTH_URL);
			//URLS.Add("https://speech-service-7600.onrender.com" + HEALTH_URL);
			//URLS.Add("https://google-services-kdg8.onrender.com" + HEALTH_URL);
			//URLS.Add("https://gemini-code-inspector-z7rq.onrender.com" + HEALTH_URL);
		}

		public HealthCheckBackgroundService()
		{
			_httpClient = new HttpClient();
			InitUrsl();
		}

		/// <inheritdoc />
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				var tasks = new List<Task>();
				URLS.ForEach(url => tasks.Add(CheckUrlHealth(url, stoppingToken)));
				await Task.WhenAll(tasks);

				await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
			}
		}

		private async Task CheckUrlHealth(string url, CancellationToken stoppingToken)
		{
			try
			{
				var response = await _httpClient.GetAsync(url, stoppingToken);
				if (response.IsSuccessStatusCode)
				{
					var content = await response.Content.ReadAsStringAsync(stoppingToken);
					if (content is not null)
					{
						Log(url + " - is OK");
						return;
					}

					Log(url + " - content is null");
				}
				else
				{
					Log($"Health check failed: {response.StatusCode}");
				}
			}
			catch (Exception ex)
			{
				Log($"Error in health check: {ex.Message}");
			}
		}

		public override void Dispose()
		{
			_httpClient.Dispose();
			base.Dispose();
		}
	}
}
