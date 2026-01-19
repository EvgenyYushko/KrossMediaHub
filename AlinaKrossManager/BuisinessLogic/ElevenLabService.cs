using System.Net;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace AlinaKrossManager.BuisinessLogic
{
	public class ElevenLabService
	{
		private static readonly HttpClient client = new HttpClient();
		private readonly string _token;

		public ElevenLabService(string tokent)
		{
			_token = tokent;
		}

		private static string _lastWorkingProxy = "http://190.60.36.210:999";
		private static string _lastWorkingProxy2 = "http://200.174.198.32:8888";

		public async Task TextToSpeechAsync(string text, string voiceId, string outputFileName)
		{
			
			// Список прокси (я выбрал HTTP и SOCKS5, так как они лучше поддерживаются)
			var proxyList = new List<string>
			{
				// ПОБЕДИТЕЛЬ (Бразилия) - теперь всегда первый
				"http://200.174.198.32:8888",

				// --- ОСТАЛЬНЫЕ HTTP (еще не проверенные) ---
				"http://202.169.229.139:53281", "http://223.151.55.58:8009", "http://95.227.11.203:18681",
				"http://103.125.154.161:8080", "http://116.171.106.111:3000", "http://46.150.9.111:8123",
				"http://221.153.152.43:80", "http://219.93.101.62:80", "http://50.203.147.152:80",
				"http://162.245.85.36:80", "http://177.23.54.10:6006", "http://159.203.61.169:3128",
				"http://36.138.53.26:10017", "http://41.139.151.147:80", "http://111.120.8.205:8085",
				"http://36.255.87.133:83", "http://103.65.237.92:5678", "http://203.19.38.114:1080",
				"http://113.108.13.120:4433", "http://202.58.77.235:8080", "http://45.231.170.137:999",
				"http://203.95.199.159:8080", "http://103.102.6.82:80", "http://27.46.124.4:8888",
				"http://139.99.237.62:80", "http://143.42.66.91:80", "http://115.248.66.131:3129",
				"http://154.3.236.202:3128", "http://58.249.55.222:9797", "http://182.176.164.41:8080",
				"http://103.16.71.125:83", "http://206.42.55.99:3128", "http://181.37.240.89:999",
				"http://103.165.155.68:1111",

				// --- ОСТАЛЬНЫЕ SOCKS5 (еще не проверенные) ---
				"socks5://95.188.64.220:1080", "socks5://110.235.240.166:1080", "socks5://110.235.255.191:1080",
				"socks5://31.211.142.115:8192", "socks5://141.98.168.28:1080", "socks5://85.175.219.236:1080",
				"socks5://188.191.164.55:4890", "socks5://93.91.162.222:1080", "socks5://115.127.112.178:1080",
				"socks5://203.189.135.14:1080", "socks5://212.237.125.216:6969", "socks5://102.36.127.231:1080",
				"socks5://221.202.27.194:10806", "socks5://185.66.88.86:57752", "socks5://128.199.37.92:1080"
					};

			foreach (var proxyUrl in proxyList)
			{
				Console.WriteLine($"\n[ПРОБНЫЙ ЗАПРОС] Через прокси: {proxyUrl}");

				try
				{
					var proxy = new WebProxy
					{
						Address = new Uri(proxyUrl),
						BypassProxyOnLocal = false,
						UseDefaultCredentials = false
					};

					var handler = new HttpClientHandler { Proxy = proxy };
					using (var client = new HttpClient(handler))
					{
						// Для бесплатных прокси ставим небольшой таймаут, чтобы не ждать долго "трупы"
						client.Timeout = TimeSpan.FromSeconds(12);

						var requestData = new
						{
							text = text,
							//model_id = "eleven_multilingual_v2"
						};

						string jsonPayload = JsonSerializer.Serialize(requestData);
						var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

						client.DefaultRequestHeaders.Clear();
						client.DefaultRequestHeaders.Add("xi-api-key", _token);

						var response = await client.PostAsync(
							$"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=mp3_44100_128",
							content
						);

						if (response.IsSuccessStatusCode)
						{
							Console.WriteLine("УСПЕХ! Данные получены.");
							using (var mp3Stream = await response.Content.ReadAsStreamAsync())
							using (var mp3Reader = new Mp3FileReader(mp3Stream))
							{
								WaveFileWriter.CreateWaveFile(outputFileName, mp3Reader);
							}
							return; // Завершаем метод, файл готов
						}
						else
						{
							string error = await response.Content.ReadAsStringAsync();
							Console.WriteLine($"ElevenLabs отклонил запрос: {error}");
							// Если ошибка "unusual activity", значит IP в бане, идем к следующему
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ошибка прокси {proxyUrl}: {ex.Message}");
				}
			}

			Console.WriteLine("\n[ИТОГ] Ни один прокси из списка не сработал или все забанены.");
		}

		public async Task<byte[]> TextToSpeechToWavAsync(string text, string voiceId)
		{
			// 1. Используем pcm_44100 для высокого качества (без сжатия)
			string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=pcm_44100";

			var requestData = new
			{
				text = text,
				//model_id = "eleven_multilingual_v2",
				//voice_settings = new { stability = 0.5, similarity_boost = 0.75 }
			};

			string jsonPayload = JsonSerializer.Serialize(requestData);
			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			client.DefaultRequestHeaders.Clear();
			client.DefaultRequestHeaders.Add("xi-api-key", _token);

			Console.WriteLine("Запрос PCM данных...");
			HttpResponseMessage response = await client.PostAsync(url, content);

			if (response.IsSuccessStatusCode)
			{
				byte[] pcmBytes = await response.Content.ReadAsByteArrayAsync();

				// 2. Добавляем WAV заголовок к сырым PCM байтам
				byte[] wavBytes = AddWavHeader(pcmBytes, 44100);
				return wavBytes;
				//await File.WriteAllBytesAsync(fileName, wavBytes);
				//Console.WriteLine($"Файл сохранен как WAV: {fileName}");
			}
			else
			{
				string error = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"Ошибка: {error}");
			}

			return null;
		}

		// Метод для создания структуры WAV файла (RIFF header)
		private byte[] AddWavHeader(byte[] pcmBytes, int sampleRate)
		{
			using (var stream = new MemoryStream())
			{
				using (var writer = new BinaryWriter(stream))
				{
					short channels = 1; // ElevenLabs отдает моно
					short bitsPerSample = 16; // Стандарт для pcm_
					int byteRate = sampleRate * channels * bitsPerSample / 8;
					short blockAlign = (short)(channels * bitsPerSample / 8);

					writer.Write(Encoding.ASCII.GetBytes("RIFF"));         // ChunkID
					writer.Write(36 + pcmBytes.Length);                    // ChunkSize
					writer.Write(Encoding.ASCII.GetBytes("WAVE"));         // Format
					writer.Write(Encoding.ASCII.GetBytes("fmt "));         // Subchunk1ID
					writer.Write(16);                                      // Subchunk1Size (16 для PCM)
					writer.Write((short)1);                                // AudioFormat (1 = PCM)
					writer.Write(channels);                                // NumChannels
					writer.Write(sampleRate);                              // SampleRate
					writer.Write(byteRate);                                // ByteRate
					writer.Write(blockAlign);                              // BlockAlign
					writer.Write(bitsPerSample);                           // BitsPerSample
					writer.Write(Encoding.ASCII.GetBytes("data"));         // Subchunk2ID
					writer.Write(pcmBytes.Length);                         // Subchunk2Size
					writer.Write(pcmBytes);                                // Сами аудиоданные

					return stream.ToArray();
				}
			}
		}
	}
}
