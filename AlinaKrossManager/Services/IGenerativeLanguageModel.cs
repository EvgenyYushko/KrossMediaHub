namespace AlinaKrossManager.Services
{
	public interface IGenerativeLanguageModel
	{
		Task<string> GeminiRequest(string prompt);
		Task<string> GeminiRequest(string prompt, string base64Image);
		Task<List<string>> GeminiRequestGenerateImage(string prompt);
		Task<string> GeminiAudioToText(string base64Iaudio);
		Task<string> GeminiTextToSpeechEn(string text);
		Task<string> GeminiTextToSpeechRu(string text);
	}
}
