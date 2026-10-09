using DotNetIALabs.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace DotNetIALabs.Presentation.Labs
{
    public sealed class PromptPrincipalesLabs (
         IChatClient chatClient,
        IOptions<AiOptions> aiOptions)
    {
        private readonly GenerationOptions _generation = aiOptions.Value.Generation;

        #region "Principle 1: Write clear and specific instructions"
        public async Task RunTactic1UsingDelimeters (CancellationToken cancellationToken)
        {
            string text = """
                           You should express what you want a model to do by
                           providing instructions that are as clear and
                           specific as you can possibly make them.
                           This will guide the model towards the desired output,
                           and reduce the chances of receiving irrelevant
                           or incorrect responses. Don't confuse writing a
                           clear prompt with writing a short prompt.
                           In many cases, longer prompts provide more clarity
                           and context for the model, which can lead to
                           more detailed and relevant outputs.
                           """;

            string goodPrompt = $"Summarize the text delimited by triple backticks into into s single sentence. ```{text}```";
            string badPrompt = """
                Summarize the following text into s single sentence :                
                """ + text;

            ChatResponse response = await  chatClient.GetResponseAsync(goodPrompt, CreateChatOptions(), cancellationToken);

            Console.WriteLine(response.Text);
        }

        public async Task RunTactic2AskStructredOutput(CancellationToken cancellationToken)
        {

            string prompt = """
                Generate a list of three made-up book titles along 
                with their authors and genres. The list must have at least three items, and each item should include the book's title, author, and genre.
                at least you must provide on item with math genre
                Provide them in JSON format with the following keys: book_id, title, author, genre.                
                """;

            ChatResponse response = await chatClient.GetResponseAsync(prompt, CreateChatOptions(), cancellationToken);

            Console.WriteLine(response.Text);
        }

        public async Task RunTactic3AskCheckConditions(CancellationToken cancellationToken)
        {
            string text = """
                Making a cup of tea is easy! First, you need to get some \ 
                water boiling. While that's happening, \ 
                grab a cup and put a tea bag in it. Once the water is \ 
                hot enough, just pour it over the tea bag. \ 
                Let it sit for a bit so the tea can steep. After a \ 
                few minutes, take out the tea bag. If you \ 
                like, you can add some sugar or milk to taste. \ 
                And that's it! You've got yourself a delicious \ 
                cup of tea to enjoy.                
                """;

            string text2 = """
                The sun is shining brightly today, and the birds are \
                singing. It's a beautiful day to go for a \ 
                walk in the park. The flowers are blooming, and the \ 
                trees are swaying gently in the breeze. People \ 
                are out and about, enjoying the lovely weather. \ 
                Some are having picnics, while others are playing \ 
                games or simply relaxing on the grass. It's a \ 
                perfect day to spend time outdoors and appreciate the \ 
                beauty of nature.                
                """;

            string promptWithSteps = $"""
                                You will be provided with text delimited by triple quotes. 
                If it contains a sequence of instructions, \ 
                re-write those instructions in the following format:

                Step 1 - ...
                Step 2 - …
                …
                Step N - …

                If the text does not contain a sequence of instructions, \ 
                then simply write \"No steps provided.\"
                \"\"\"{text}\"\"\"                
                """;

            string promptWithoutSteps = $"""
                You will be provided with text delimited by triple quotes. 
                If it contains a sequence of instructions, \ 
                re-write those instructions in the following format:

                Step 1 - ...
                Step 2 - …
                …
                Step N - …

                If the text does not contain a sequence of instructions, \ 
                then simply write \"No steps provided.\"
                \"\"\"{text2}\"\"\"                
                """;
            ChatResponse response = await chatClient.GetResponseAsync(promptWithSteps, CreateChatOptions(), cancellationToken);

            Console.WriteLine("Prompt with steps : ");
            Console.WriteLine(response.Text);

            response = await chatClient.GetResponseAsync(promptWithoutSteps, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Prompt without steps : ");
            Console.WriteLine(response.Text);

        }

        public async Task RunTactic4Fewshot(CancellationToken cancellationToken)
        {
            string prompt = """
                Your task is to answer in a consistent style.

                <child>: Teach me about patience.

                <grandparent>: The river that carves the deepest \ 
                valley flows from a modest spring; the \ 
                grandest symphony originates from a single note; \ 
                the most intricate tapestry begins with a solitary thread.

                <child>: Teach me about resilience.
                
                """;

            ChatResponse response = await chatClient.GetResponseAsync(prompt, CreateChatOptions(), cancellationToken);

            Console.WriteLine(response.Text);
        }
        #endregion
        
        
        
        private ChatOptions CreateChatOptions(float? temperature = null) => new()
        {
            MaxOutputTokens = _generation.MaxOutputTokens,
            Temperature = temperature ?? _generation.Temperature
        };
    }

}
