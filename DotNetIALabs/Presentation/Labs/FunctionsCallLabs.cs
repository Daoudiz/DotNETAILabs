using DotNetIALabs.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Presentation.Labs
{
    public sealed class FunctionsCallLabs (
        IChatClient chatClient,
        IOptions<AiOptions> aiOptions)
    {
        private readonly GenerationOptions _generation = aiOptions.Value.Generation;

        public async Task RunSimpleDirectFunctionCall()
        {
            Delegate getWeatherdelegate = (AIFunctionArguments args) =>
            {
                // AccessViolationException named parameters from the arguments dictionary
                string? location = args.TryGetValue("location", out object? loc) ? loc.ToString() : "Unknown";
                string? units = args.TryGetValue("units", out object? u) ? u.ToString() : "celsius";

                return $"Weather in {location} : 35° {units}";
            };

            //Create the AIFunction
            AIFunction getWeather = AIFunctionFactory.Create(getWeatherdelegate);

            //Call the function manually 
            var result = await getWeather.InvokeAsync(new AIFunctionArguments
            {
                {"location", "Seattle" },
                {"units", "F" }
            });

            Console.WriteLine($"function result : {result}");
        }

        public async Task RunSimpleFunctionCallAsync(
           CancellationToken cancellationToken)
        {
             var chatOptions = new ChatOptions
             {
                 MaxOutputTokens = _generation.MaxOutputTokens,
                 Temperature = _generation.Temperature,

                 Tools = [AIFunctionFactory.Create((string location, string unit) =>
                 {
                    // Here you would call a weather API
                    // to get the weather for the location.
                     Console.WriteLine(
                    $"Outil météo appelé : location={location}, unit={unit}");
                    return "Periods of rain or drizzle, 15 C";
                },
                "get_current_weather",
                "Gets the current weather in a given location")]

             };


        List<ChatMessage> chatHistory = [new(ChatRole.System, """
                            You are a hiking enthusiast who helps people discover fun hikes in their area. You are upbeat and friendly.   
                            When the user asks about current weather, you MUST call the
                            get_current_weather tool before answering.
                            Never claim that you cannot access current weather.
                            """)];

            // Weather conversation relevant to the registered function.
            chatHistory.Add(new ChatMessage(ChatRole.User,
                "I live in Montreal and I'm looking for a moderate intensity hike. What's the current weather like?"));
            Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last()}");

            ChatResponse response = await chatClient.GetResponseAsync(chatHistory, chatOptions, cancellationToken: cancellationToken);
            Console.WriteLine($"Assistant >>> {response.Text}");

        }

        public async Task RunMultipleFunctionCallAsync(
            CancellationToken cancellationToken)
        {
             var chatOptions = new ChatOptions
             {
                 MaxOutputTokens = _generation.MaxOutputTokens,
                 Temperature = _generation.Temperature,

                 Tools = [


                AIFunctionFactory.Create((string location, string unit) =>
                 {
                    // Here you would call a weather API
                    // to get the weather for the location.
                     Console.WriteLine(
                    $"Outil météo appelé : location={location}, unit={unit}");
                    return "Periods of rain or drizzle, 15 C";
                },
                "get_current_weather",
                "Gets the current weather in a given location"),

                AIFunctionFactory.Create((string curruncy) =>
                 {
                    // Here you would call a currency API
                    // to get the value of the given currency in Moroccan Dirahms.
                     Console.WriteLine(
                    $"Outil devise appelé : currency={curruncy}");
                    return "The value of the currecny in Moroccan Dirhams is low";
                },
                "get_currency",
                "Gets the current value of the currency")

                ]

             };


        List<ChatMessage> chatHistory = [new(ChatRole.System, """                
                    You are a currency expert who help people find the exact value of a given currency in Moroccan Dirhams.
                    """)];

            // Weather conversation relevant to the registered function.
            chatHistory.Add(new ChatMessage(ChatRole.User,
                "I'm looking for the value of Euro in Moroccan Dirhams?"));
            Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last()}");

            ChatResponse response = await chatClient.GetResponseAsync(chatHistory, chatOptions, cancellationToken: cancellationToken);
            Console.WriteLine($"Assistant >>> {response.Text}");

        }

        public async Task RunFunctionInvokingChatClient(
            CancellationToken cancellationToken)
        {
            FunctionInvokingChatClient client = new(chatClient);

            AIFunction getWeather = AIFunctionFactory.Create(() =>
            {
                //Access named parameters from the arguments dictionary.
                AdditionalPropertiesDictionary props =
                    FunctionInvokingChatClient.CurrentContext.Options.AdditionalProperties;

                string location = props["location"].ToString();
                string units = props["units"].ToString();

                return $"Weather in {location} : 35°{units}";

            });

            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = _generation.MaxOutputTokens,
                Temperature = _generation.Temperature,

                Tools = [getWeather],
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["location"] = "Seattle",
                    ["units"] = "F"
                },
            };

            List<ChatMessage> chatHistory = [
                new (ChatRole.System, """
                    You are a helpful weather assistant. When the user asks about current weather, 
                    you MUST call the appropriate  tool before answering.
                    """)];

            chatHistory.Add(new ChatMessage(ChatRole.User, "Waht's the weather like?"));

            ChatResponse response = await client.GetResponseAsync(chatHistory, chatOptions, cancellationToken);
            Console.WriteLine($"Response : {response.Text}");
        }

       
       

    }
}
