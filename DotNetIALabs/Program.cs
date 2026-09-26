using Microsoft.Extensions.AI;
using OllamaSharp;


IChatClient chatClient = new OllamaApiClient("http://localhost:11434/", "qwen2.5:0.5b");

List<ChatMessage> chatHistory = new();

while(true)
{
    //get user prompt and add to chat history
    Console.WriteLine("Enter your prompt : ");
    var userPrompt = Console.ReadLine();
    chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

    //Stream the AI response and add to chat history
    Console.WriteLine("Response of the AI");
    var response = "";
    await foreach(ChatResponseUpdate item in chatClient.GetStreamingResponseAsync(chatHistory))
    {
        Console.Write(item.Text);
        response += item.Text;
    }

    chatHistory.Add (new ChatMessage(ChatRole.Assistant, response));
    Console.WriteLine();


}

