using System.Diagnostics;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public static class AudioService
	{
		public static async Task<MemoryStream> GetMp3Stream(byte[] rawPcmData, int sampleRate, int channels)
		{
			var outputStream = new MemoryStream();

			// Настраиваем запуск FFmpeg
			// -f s16le: формат входных данных (16-bit PCM Little Endian)
			// -ar 24000: частота дискретизации (как у Gemini)
			// -ac 1: количество каналов (моно)
			// -i pipe:0: читать данные из стандартного ввода (stdin)
			// -f mp3 pipe:1: записывать результат в формате mp3 в стандартный вывод (stdout)
			var startInfo = new ProcessStartInfo
			{
				FileName = "ffmpeg",
				Arguments = $"-f s16le -ar {sampleRate} -ac {channels} -i pipe:0 -f mp3 pipe:1",
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using (var process = new Process { StartInfo = startInfo })
			{
				process.Start();

				// Записываем PCM данные в stdin FFmpeg
				var inputTask = Task.Run(async () =>
				{
					using (var stdin = process.StandardInput.BaseStream)
					{
						await stdin.WriteAsync(rawPcmData, 0, rawPcmData.Length);
						await stdin.FlushAsync();
					}
				});

				// Читаем готовый MP3 из stdout FFmpeg
				var outputTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream);

				await Task.WhenAll(inputTask, outputTask);
				await process.WaitForExitAsync();
			}

			outputStream.Position = 0;
			return outputStream;
		}
	}
}
