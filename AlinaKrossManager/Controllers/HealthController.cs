using Microsoft.AspNetCore.Mvc;
using static AlinaKrossManager.Helpers.TimeZoneHelper;

namespace AlinaKrossManager.Controllers
{
	[ApiExplorerSettings(IgnoreApi = true)]
	[ApiController]
	[Route("[controller]")]
	public class HealthController : ControllerBase
	{
		[HttpGet("/health")]
		[HttpHead("/health")]
		public IActionResult GetHealthStatus()
		{
			return Ok($"App is running {DateTimeNow}");
		}
	}
}
