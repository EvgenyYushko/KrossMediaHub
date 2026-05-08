using NAudio.Lame;
using NAudio.Wave;

namespace AlinaKrossManager.BuisinessLogic.Services
{
	public static class AudioService
	{
		public static MemoryStream GetMp3Stream(byte[] rawPcmData, int sampleRate, int channels)
		{
			var waveFormat = new WaveFormat(sampleRate, 16, channels);
			byte[] mp3Bytes;

			// Используем временный MemoryStream для записи
			using (var tempMs = new MemoryStream())
			{
				using (var pcmStream = new MemoryStream(rawPcmData))
				using (var reader = new RawSourceWaveStream(pcmStream, waveFormat))
				using (var writer = new LameMP3FileWriter(tempMs, waveFormat, 64))
				{
					reader.CopyTo(writer);
					writer.Flush();
					// Здесь, при выходе из using, writer финализирует MP3 и закрывает tempMs
				}

				// MemoryStream.ToArray() работает даже после того, как поток закрыт!
				mp3Bytes = tempMs.ToArray();
			}

			// Возвращаем НОВЫЙ открытый поток из байтов
			return new MemoryStream(mp3Bytes);
		}
	}
}
