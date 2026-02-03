using Protos.GoogleGeminiService;

namespace AlinaKrossManager.Services
{
	public interface IGenerativeLanguageModel
	{
		Task<string> GeminiRequest(string prompt);
		Task<string> GeminiRequestWithImage(string prompt, string base64Image);
		Task<string> GeminiRequestWithVideo(string prompt, string base64Video);
		Task<List<string>> GeminiRequestGenerateImage(string prompt, int countImage = 1);
		Task<string> GeminiAudioToText(string base64Iaudio);
		Task<string> GeminiTextToSpeechEn(string text);
		Task<string> GeminiTextToSpeechRu(string text);
		Task<string> RequestWithChatAsync(List<ChatMessage> messages, string systemInstruction = null);
	}
}
