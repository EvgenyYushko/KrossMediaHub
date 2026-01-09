using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace AlinaKrossManager.BuisinessLogic
{
	public class ElevenLabService
	{
		private static readonly HttpClient client = new HttpClient();
		private readonly string _tokent;

		public ElevenLabService(string tokent)
		{
			_tokent = tokent;
		}

		public async Task TextToSpeechAsync(string text, string voiceId, string outputWavFileName)
		{
			// 1. Запрашиваем MP3 в высоком качестве (разрешено на всех тарифах)
			string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=mp3_44100_128";

			var requestData = new
			{
				text = text,
				//model_id = "eleven_multilingual_v2",
				//voice_settings = new { stability = 0.5, similarity_boost = 0.75 }
			};

			string jsonPayload = JsonSerializer.Serialize(requestData);
			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			client.DefaultRequestHeaders.Clear();
			client.DefaultRequestHeaders.Add("xi-api-key", _tokent);

			Console.WriteLine("Загрузка MP3 из ElevenLabs...");
			var response = await client.PostAsync(url, content);

			if (!response.IsSuccessStatusCode)
			{
				string error = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"Ошибка API: {error}");
			}

			// 2. Получаем поток MP3 данных
			using (var mp3Stream = await response.Content.ReadAsStreamAsync())
			{
				// 3. Конвертируем MP3 поток в WAV файл
				using (var mp3Reader = new Mp3FileReader(mp3Stream))
				{
					// Сохраняем как стандартный WAV (PCM)
					WaveFileWriter.CreateWaveFile(outputWavFileName, mp3Reader);
				}
			}

			Console.WriteLine($"Успешно! Файл сохранен как WAV: {outputWavFileName}");
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
			client.DefaultRequestHeaders.Add("xi-api-key", _tokent);

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
