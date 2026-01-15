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

		public async Task TextToSpeechAsync(string text, string voiceId, string outputFileName)
		{
			
			// Список прокси (я выбрал HTTP и SOCKS5, так как они лучше поддерживаются)
			var proxyList = new List<string>
		{
            // HTTP PROXIES
            "http://131.72.68.160:40033", "http://103.141.180.254:80", "http://124.108.19.6:9292",
			"http://211.230.49.122:3128", "http://50.203.147.158:80", "http://61.158.175.38:9002",
			"http://121.101.135.94:8089", "http://122.3.121.231:8082", "http://95.173.218.67:8082",
			"http://185.32.6.131:8090", "http://202.131.159.222:80", "http://102.88.13.62:8080",
			"http://102.68.128.210:8080", "http://186.250.29.225:8080", "http://190.60.36.210:999",
			"http://39.109.130.95:80", "http://103.118.124.137:6969", "http://1.1.220.100:8080",
			"http://190.145.227.114:999", "http://54.201.87.119:80", "http://24.172.82.94:53281",
			"http://91.99.181.245:80", "http://190.2.209.59:999", "http://177.234.217.83:999",
			"http://134.209.29.120:3128", "http://36.92.106.41:8080", "http://190.52.165.120:8080",
			"http://167.249.29.218:999", "http://104.251.81.87:14270", "http://188.245.218.56:80",
			"http://62.113.119.14:8080", "http://219.93.101.60:80", "http://23.247.136.254:80",
			"http://69.75.140.157:8080", "http://136.233.136.41:48976", "http://113.204.79.230:9091",
			"http://150.107.140.238:3128", "http://198.199.86.11:8080", "http://109.108.107.122:8080",
			"http://183.110.216.128:8091", "http://65.109.176.217:80", "http://38.54.116.95:80",
			"http://95.173.218.67:8081", "http://103.133.27.143:8080", "http://141.11.45.91:80",
			"http://153.0.171.163:8085", "http://103.171.241.9:8080", "http://50.203.147.156:80",
			"http://12.218.209.130:53281", "http://12.50.107.217:80", "http://144.124.227.90:10808",
			"http://5.75.198.72:80", "http://188.132.222.6:8080", "http://41.65.160.172:1976",
			"http://213.178.39.170:8080", "http://35.199.93.247:90", "http://160.202.42.156:8080",
			"http://35.202.49.74:80", "http://41.191.203.164:80", "http://45.238.220.1:8181",
			"http://61.216.156.222:60808",

            // SOCKS5 PROXIES
            "socks5://45.138.69.29:562", "socks5://45.138.69.35:561", "socks5://203.189.135.73:1080",
			"socks5://103.191.218.119:8199", "socks5://141.98.168.28:1080", "socks5://85.175.219.236:1080",
			"socks5://188.191.164.55:4890", "socks5://93.91.162.222:1080", "socks5://115.127.112.178:1080",
			"socks5://203.189.135.14:1080"
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
