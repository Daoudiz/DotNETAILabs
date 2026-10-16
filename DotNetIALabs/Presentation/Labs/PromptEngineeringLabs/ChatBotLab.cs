using DotNetIALabs.Configuration;
using DotNetIALabs.Presentation.Labs.PromptEngineeringLabs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static System.Net.WebRequestMethods;

namespace DotNetIALabs.Presentation.Labs.PromptEngineeringLabs
{
    public record ChatMessage(string Role, string Content);

    public sealed class ChatApi(IServiceProvider serviceProvider) : IDisposable
    {
       

        private HttpClient _httpClient;

        public async Task<string> GetCompletionFromMessagesAsync(
            IReadOnlyList<ChatMessage> messages,
            bool jasonObject = false)
        {
            var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;

            string host = options.Providers.Ollama.Endpoint;
            string model = options.Providers.Ollama.Model;
            double temperature = options.Generation.Temperature;

            if (host.EndsWith("/")) host += "/";
            _httpClient = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeSpan.FromMinutes(10) };

            var body = new

            {
                model,
                messages = messages.Select(m => new { role = m.Role, content = m.Content }),
                temperature,
                stream = false,
                response_format = jasonObject ? new { type = "json_object" } : null
            };

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };

            using var response = await _httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Erreur Ollama {(int)response.StatusCode} : {responseText}");
            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        public Task<string> GetCompletionAsync(string prompt, double temperature = 0) =>
                GetCompletionFromMessagesAsync(new List<ChatMessage> { new("user", prompt) });

        public void Dispose()
        {
            if (_httpClient is not null) _httpClient.Dispose();
        }

        
    }

    public sealed class OrderBot 
    {
        private readonly ChatApi _chatApi;
        public List<ChatMessage> History { get; }

        public OrderBot(ChatApi api, string systemPrompt)
        {
            _chatApi = api;
            History = new List<ChatMessage> { new("system", systemPrompt) };
        }

        public async Task<string> SendAsync(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                throw new ArgumentException("Le message ne peut pas être vide.", nameof(userInput));
            History.Add(new ChatMessage("user", userInput));
            try
            {
                string reply = await _chatApi.GetCompletionFromMessagesAsync(History);
                History.Add(new ChatMessage("assistant", reply));
                return reply;
            }
            catch
            {
                History.RemoveAt(History.Count - 1); // ne pas conserver une requête qui a échoué
                throw;
            }
        }
    }

    public sealed class ChatBotLabs(ChatApi chatApi, IChatClient chatClient,
                    IOptions<AiOptions> aiOptions)
    {
        private readonly GenerationOptions _generation = aiOptions.Value.Generation;

        public async Task RunUndestandRoles(CancellationToken cancellationToken)
        {
            var shakespeareMessages = new List<ChatMessage>
            {
                new("system", "You are an assistant that speaks like Shakespeare."),
                new("user", "tell me a joke"),
                new("assistant", "Why did the chicken cross the road"),
                new("user", "I don't know")
            };
            Console.WriteLine(await chatApi.GetCompletionFromMessagesAsync(shakespeareMessages));
        }

        public async Task RunSimulateRoles(CancellationToken cancellationToken)
        {
            var rememberedMessages = new List<ChatMessage>
{
            new("system", "You are friendly chatbot."),
            new("user", "Hi, my name is Isa"),
            new("assistant", "Hi Isa! It's nice to meet you. Is there anything I can help you with today?"),
            new("user", "Yes, you can remind me, What is my name?")
};
            Console.WriteLine(await chatApi.GetCompletionFromMessagesAsync(rememberedMessages));
        }

        public async Task RunOrderBotWithIChatClient(CancellationToken cancellationToken)
        {
            var history = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, """
                        You are OrderBot, an automated service to collect orders for a pizza restaurant. 
                        You first greet the customer, then collects the order, 
                        and then asks if it's a pickup or delivery. 
                        You wait to collect the entire order, then summarize it and check for a final 
                        time if the customer wants to add anything else. 
                        If it's a delivery, you ask for an address. 
                        Finally you collect the payment.
                        Make sure to clarify all options, extras and sizes to uniquely 
                        identify the item from the menu.
                        You respond in a short, very conversational friendly style. 
                        The menu includes 
                        pepperoni pizza  12.95, 10.00, 7.00 
                        cheese pizza   10.95, 9.25, 6.50 
                        eggplant pizza   11.95, 9.75, 6.75 
                        fries 4.50, 3.50 
                        greek salad 7.25 
                        Toppings: 
                        extra cheese 2.00, 
                        mushrooms 1.50 
                        sausage 3.00 
                        canadian bacon 3.50 
                        AI sauce 1.50 
                        peppers 1.00 
                        Drinks: 
                        coke 3.00, 2.00, 1.00 
                        sprite 3.00, 2.00, 1.00 
                        bottled water 5.00
                    """)
            };
    
            foreach (var userText in new[] {
                        "Hi!",
                        "I'd like one cheese pizza, please.",
                        "I'm a bit confused, to please advise me about the best menu you have!",
                        "Great, bu i'm a little bit concerned about my fitness. So, what are menu that contain the less calories possible."
                        //"Small size, no toppings. I will pick it up. That's all."
                    })
            {
                history.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userText));
                var response = await chatClient.GetResponseAsync(history, CreateChatOptions(), cancellationToken);               
                history.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, response.Text));
                Console.WriteLine($"Client : {userText}");
                Console.WriteLine($"OrderBot : {response.Text}\n");
            }
        }

        public async Task RunOrderBot(CancellationToken cancellationToken)
        {
            var orderBotPrompt = """
                    You are OrderBot, an automated service to collect orders for a pizza restaurant. 
                    You first greet the customer, then collects the order, 
                    and then asks if it's a pickup or delivery. 
                    You wait to collect the entire order, then summarize it and check for a final 
                    time if the customer wants to add anything else. 
                    If it's a delivery, you ask for an address. 
                    Finally you collect the payment.
                    Make sure to clarify all options, extras and sizes to uniquely 
                    identify the item from the menu.
                    You respond in a short, very conversational friendly style. 
                    The menu includes 
                    pepperoni pizza  12.95, 10.00, 7.00 
                    cheese pizza   10.95, 9.25, 6.50 
                    eggplant pizza   11.95, 9.75, 6.75 
                    fries 4.50, 3.50 
                    greek salad 7.25 
                    Toppings: 
                    extra cheese 2.00, 
                    mushrooms 1.50 
                    sausage 3.00 
                    canadian bacon 3.50 
                    AI sauce 1.50 
                    peppers 1.00 
                    Drinks: 
                    coke 3.00, 2.00, 1.00 
                    sprite 3.00, 2.00, 1.00 
                    bottled water 5.00
                    """;

            var orderBot = new OrderBot(chatApi, orderBotPrompt);
            Console.WriteLine("OrderBot initialisé. Exécutez le lab suivant pour lui parler.");

            foreach (var userText in new[] {
                        "Hi!",
                        "I'd like one cheese pizza, please.",
                        "Small size, no toppings. I will pick it up. That's all."
                    })
            {
                Console.WriteLine($"Client : {userText}");
                Console.WriteLine($"OrderBot : {await orderBot.SendAsync(userText)}\n");
            }
        }

        private ChatOptions CreateChatOptions(float? temperature = null) => new()
        {
            MaxOutputTokens = _generation.MaxOutputTokens,
            Temperature = temperature ?? _generation.Temperature
        };
    }
}

    



