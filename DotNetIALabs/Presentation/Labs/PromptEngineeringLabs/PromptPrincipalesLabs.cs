using Azure;
using DotNetIALabs.Configuration;
using DotNetIALabs.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace DotNetIALabs.Presentation.Labs.PromptEngineeringLabs
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

        #region "Principle 2: Give the model time to “think” "
        public async Task RunTactic1SpecifySteps(CancellationToken cancellationToken)
        {
            string text = """                
                    In a charming village, siblings Jack and Jill set out on \                
                    a quest to fetch water from a hilltop \                 
                    well.As they climbed, singing joyfully, misfortune \ 
                    struck—Jack tripped on a stone and tumbled \ 
                    down the hill, with Jill following suit. \ 
                    Though slightly battered, the pair returned home to \ 
                    comforting embraces. Despite the mishap, \ 
                    their adventurous spirits remained undimmed, and they \ 
                    continued exploring with delight.                
                    """;

            string prompt = $"""
                Perform the following actions: 
                    1 - Summarize the following text delimited by triple \
                        backticks with 1 sentence.
                    2 - Translate the summary into French.
                    3 - List each name in the French summary.
                    4 - Output a json object that contains the following \
                        keys: french_summary, num_names.
                        Separate your answers with line breaks.
                    Text:
                ```{text}```
                """;

            ChatResponse response = await chatClient.GetResponseAsync(prompt, CreateChatOptions(), cancellationToken);

            Console.WriteLine(response.Text); 

        }

        public async Task RunTactic2InstructOwnSolution(CancellationToken cancellationToken)
        {
            string simplePrompt = """
                Determine if the student's solution is correct or not.
                Question:
                I'm building a solar power installation and I need \
                 help working out the financials. 
                - Land costs $100 / square foot
                - I can buy solar panels for $250 / square foot
                - I negotiated a contract for maintenance that will cost \ 
                me a flat $100k per year, and an additional $10 / square \
                foot
                What is the total cost for the first year of operations 
                as a function of the number of square feet.
                Student's Solution:
                Let x be the size of the installation in square feet.
                Costs:
                1. Land cost: 100x
                2. Solar panel cost: 250x
                3. Maintenance cost: 100,000 + 100x
                Total cost: 100x + 250x + 100,000 + 100x = 450x + 100,000
                """;

            string promptWithInstruction = """
                Your task is to determine if the student's solution \
                is correct or not.
                To solve the problem do the following:
                - First, work out your own solution to the problem including the final total. 
                - Then compare your solution to the student's solution \ 
                and evaluate if the student's solution is correct or not. 
                Don't decide if the student's solution is correct until 
                you have done the problem yourself.
                Use the following format:
                
                Question:
                ```
                question here
                ```
                Student's solution:
                ```
                student's solution here
                ```
                Actual solution:
                ```
                steps to work out the solution and your solution here
                ```
                Is the student's solution the same as actual solution \
                just calculated:
                ```
                yes or no
                ```
                Student grade:
                ```
                correct or incorrect
                ```

                Question:
                ```
                I'm building a solar power installation and I need help \
                working out the financials. 
                - Land costs $100 / square foot
                - I can buy solar panels for $250 / square foot
                - I negotiated a contract for maintenance that will cost \
                me a flat $100k per year, and an additional $10 / square \
                foot
                What is the total cost for the first year of operations \
                as a function of the number of square feet.
                ``` 
                
                Student's solution:
                ```
                Let x be the size of the installation in square feet.
                Costs:
                1. Land cost: 100x
                2. Solar panel cost: 250x
                3. Maintenance cost: 100,000 + 100x
                Total cost: 100x + 250x + 100,000 + 100x = 450x + 100,000
                ```
                Actual solution:
                """;


            ChatResponse response  = await chatClient.GetResponseAsync(simplePrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine($"Response without instruction :  {response.ToString()}");

            response = await chatClient.GetResponseAsync(promptWithInstruction, CreateChatOptions(), cancellationToken);
            Console.WriteLine($"Response with instruction : {response.ToString()}");
        }



        #endregion

        public async Task RunHallucinationSample(CancellationToken cancellationToken)
        {
            string prompt = """
                Tell me about AeroGlide UltraSlim Smart Toothbrush by Boie 
                """;

            ChatResponse response = await chatClient.GetResponseAsync(prompt, CreateChatOptions(), cancellationToken);

            Console.WriteLine(response.Text);
        }

        public async Task RunIterativePrompt(CancellationToken cancellationToken)
        {
            string fact_sheet_chair = """
                OVERVIEW
                - Part of a beautiful family of mid-century inspired office furniture, 
                including filing cabinets, desks, bookcases, meeting tables, and more.
                - Several options of shell color and base finishes.
                - Available with plastic back and front upholstery (SWC-100) 
                or full upholstery (SWC-110) in 10 fabric and 6 leather options.
                - Base finish options are: stainless steel, matte black, 
                gloss white, or chrome.
                - Chair is available with or without armrests.
                - Suitable for home or business settings.
                - Qualified for contract use.

                CONSTRUCTION
                - 5-wheel plastic coated aluminum base.
                - Pneumatic chair adjust for easy raise/lower action.

                DIMENSIONS
                - WIDTH 53 CM | 20.87”
                - DEPTH 51 CM | 20.08”
                - HEIGHT 80 CM | 31.50”
                - SEAT HEIGHT 44 CM | 17.32”
                - SEAT DEPTH 41 CM | 16.14”

                OPTIONS
                - Soft or hard-floor caster options.
                - Two choices of seat foam densities: 
                 medium (1.8 lb/ft3) or high (2.8 lb/ft3)
                - Armless or 8 position PU armrests 

                MATERIALS
                SHELL BASE GLIDER
                - Cast Aluminum with modified nylon PA6/PA66 coating.
                - Shell thickness: 10 mm.
                SEAT
                - HD36 foam

                COUNTRY OF ORIGIN
                - Italy
                """;

            string firstPrompt = $"""
                Your task is to help a marketing team create a 
                description for a retail website of a product based 
                on a technical fact sheet.

                Write a product description based on the information 
                provided in the technical specifications delimited by 
                triple backticks.

                Technical specifications: ```{fact_sheet_chair}```
                """;
            string limitNumberCharactersPrompt = $"""
                                Your task is to help a marketing team create a 
                description for a retail website of a product based 
                on a technical fact sheet.

                Write a product description based on the information 
                provided in the technical specifications delimited by 
                triple backticks.

                Use at most 50 words.

                Technical specifications: ```{fact_sheet_chair}```
                """;

            string focusOnDetailsPrompt = $"""
                Your task is to help a marketing team create a 
                description for a retail website of a product based 
                on a technical fact sheet.

                Write a product description based on the information 
                provided in the technical specifications delimited by 
                triple backticks.

                The description is intended for furniture retailers, 
                so should be technical in nature and focus on the 
                materials the product is constructed from.

                Use at most 50 words.

                Technical specifications: ```{fact_sheet_chair}```
                """;

            string dimensionsTablePrompt = $"""
                Your task is to help a marketing team create a 
                description for a retail website of a product based 
                on a technical fact sheet.

                Write a product description based on the information 
                provided in the technical specifications delimited by 
                triple backticks.

                The description is intended for furniture retailers, 
                so should be technical in nature and focus on the 
                materials the product is constructed from.

                At the end of the description, include every 7-character 
                Product ID in the technical specification.

                After the description, include a table that gives the 
                product's dimensions. The table should have two columns.
                In the first column include the name of the dimension. 
                In the second column include the measurements in inches only.

                Give the table the title 'Product Dimensions'.

                Format everything as HTML that can be used in a website. 
                Place the description in a <div> element.

                Technical specifications: ```{fact_sheet_chair}```
                """;

            ChatResponse response = new ChatResponse();

            response = await chatClient.GetResponseAsync(firstPrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine($"Direct prompt : {response.Text}");
            Console.WriteLine("-----------------------------------------------------------------------------");

            response = await chatClient.GetResponseAsync(limitNumberCharactersPrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine($"Prompt with limit the number of words/characters {response.Text}");
            Console.WriteLine("-----------------------------------------------------------------------------");

            response = await chatClient.GetResponseAsync(focusOnDetailsPrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine($"Prompt with focus on some specifics details {response.Text}");
            Console.WriteLine("-----------------------------------------------------------------------------");


            response = await chatClient.GetResponseAsync(dimensionsTablePrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Prompt that ask toformat description as a table of dimensions using HTML :");
            Console.WriteLine(response.Text);

        }

        public async Task RunSummarizePrompt(CancellationToken cancellationToken)
        {
            #region "Reviews"

            string prod_review = """
                    Got this panda plush toy for my daughter's birthday, \
                    who loves it and takes it everywhere. It's soft and \ 
                    super cute, and its face has a friendly look. It's \ 
                    a bit small for what I paid though. I think there \ 
                    might be other options that are bigger for the \ 
                    same price. It arrived a day earlier than expected, \ 
                    so I got to play with it myself before I gave it \ 
                    to her.
                    """;

            List<string> reviews = new()
            {
                prod_review,

                """
                Needed a nice lamp for my bedroom, and this one \
                had additional storage and not too high of a price \
                point. Got it fast - arrived in 2 days. The string \
                to the lamp broke during the transit and the company \
                happily sent over a new one. Came within a few days \
                as well. It was easy to put together. Then I had a \
                missing part, so I contacted their support and they \
                very quickly got me the missing piece! Seems to me \
                to be a great company that cares about their customers \
                and products. 
                """,

                """
                My dental hygienist recommended an electric toothbrush, \
                which is why I got this. The battery life seems to be \
                pretty impressive so far. After initial charging and \
                leaving the charger plugged in for the first week to \
                condition the battery, I've unplugged the charger and \
                been using it for twice daily brushing for the last \
                3 weeks all on the same charge. But the toothbrush head \
                is too small. I’ve seen baby toothbrushes bigger than \
                this one. I wish the head was bigger with different \
                length bristles to get between teeth better because \
                this one doesn’t.  Overall if you can get this one \
                around the $50 mark, it's a good deal. The manufactuer's \
                replacements heads are pretty expensive, but you can \
                get generic ones that're more reasonably priced. This \
                toothbrush makes me feel like I've been to the dentist \
                every day. My teeth feel sparkly clean! 
                """,

                """
                So, they still had the 17 piece system on seasonal \
                sale for around $49 in the month of November, about \
                half off, but for some reason (call it price gouging) \
                around the second week of December the prices all went \
                up to about anywhere from between $70-$89 for the same \
                system. And the 11 piece system went up around $10 or \
                so in price also from the earlier sale price of $29. \
                So it looks okay, but if you look at the base, the part \
                where the blade locks into place doesn’t look as good \
                as in previous editions from a few years ago, but I \
                plan to be very gentle with it (example, I crush \
                very hard items like beans, ice, rice, etc. in the \ 
                blender first then pulverize them in the serving size \
                I want in the blender then switch to the whipping \
                blade for a finer flour, and use the cross cutting blade \
                first when making smoothies, then use the flat blade \
                if I need them finer/less pulpy). Special tip when making \
                smoothies, finely cut and freeze the fruits and \
                vegetables (if using spinach-lightly stew soften the \ 
                spinach then freeze until ready for use-and if making \
                sorbet, use a small to medium sized food processor) \ 
                that you plan to use that way you can avoid adding so \
                much ice if at all-when making your smoothie. \
                After about a year, the motor was making a funny noise. \
                I called customer service but the warranty expired \
                already, so I had to buy another one. FYI: The overall \
                quality has gone done in these types of products, so \
                they are kind of counting on brand recognition and \
                consumer loyalty to maintain sales. Got it in about \
                two days.
                """,              
            };

            //Transforme the review list to simple text 
            string reviewsText = string.Join("\n", reviews.Select((review, index) => $"{index + 1}.{review}"));

            #endregion

            #region "Prompts"

            string summarizeFocusOnShippingPrompt = $"""
                Your task is to generate a short summary of a product \
                review from an ecommerce site to give feedback to the \
                Shipping deparmtment. 

                Summarize the review below, delimited by triple 
                backticks, in at most 30 words, and focusing on any aspects \
                that mention shipping and delivery of the product. 

                Review: ```{prod_review}```
                """;

            string summarizeFocusOnPricePrompt = $"""
                Your task is to generate a short summary of a product \
                review from an ecommerce site to give feedback to the \
                pricing deparmtment, responsible for determining the \
                price of the product.  

                Summarize the review below, delimited by triple 
                backticks, in at most 30 words, and focusing on any aspects \
                that are relevant to the price and perceived value. 

                Review: ```{prod_review}```
                """;

            string extractPrompt = $"""
                Your task is to extract relevant information from \ 
                a product review from an ecommerce site to give \
                feedback to the Shipping department. 

                From the review below, delimited by triple quotes \
                extract the information relevant to shipping and \ 
                delivery. Limit to 30 words. 

                Review: ```{prod_review}```
                """;

            string extractFromMultipleReviewsprompt = $"""
                    Your task is to extract relevant information from  \ 
                    a product reviews from an e-commerce site to give \
                    feedback to the commercial departement.

                    From the reviews below, delimited by triple quotes \
                    extract the information relevant to price then  \ 
                    produce a table of two rows : review number, product name, review concerning price, customer's sentiment \ 
                    Then, give a separate and concise sentence to summarize the relevant information about price of all reviews limited to 30 words.\
                    An d finally, propose in french a new commercial strategy based on the result of reviews analysis.

                    Reviews: ```{reviewsText}```
                """;
            

            #endregion

            
            
            ChatResponse response = await chatClient.GetResponseAsync(summarizeFocusOnShippingPrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Summarized text with a focus on Shipping and devlivry : ");
            Console.WriteLine("--------------------------------------------------------");
            Console.WriteLine(response.Text);
            Console.WriteLine();
            

            response = await chatClient.GetResponseAsync(summarizeFocusOnPricePrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Summarized text with a focus on price and value : ");
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(response.Text);
            Console.WriteLine();

            response = await chatClient.GetResponseAsync(extractPrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Extract relevants informations instead of summarizing text : ");
            Console.WriteLine("-------------------------------------------------------------");
            Console.WriteLine(response.Text);
            Console.WriteLine();

            response = await chatClient.GetResponseAsync(extractFromMultipleReviewsprompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Extract relevants informations from multiple reviews : ");
            Console.WriteLine("-------------------------------------------------------------");
            Console.WriteLine(response.Text);
            Console.WriteLine();


        }

        public async Task RunInferringPrompt(CancellationToken cancellationToken)
        {
            string lamp_review = """
                    Needed a nice lamp for my bedroom, and this one had \
                    additional storage and not too high of a price point. \
                    Got it fast.  The string to our lamp broke during the \
                    transit and the company happily sent over a new one. \
                    Came within a few days as well. It was easy to put \
                    together.  I had a missing part, so I contacted their \
                    support and they very quickly got me the missing piece! \
                    Lumina seems to me to be a great company that cares \
                    about their customers and products!!
                    """;

            string story = """
                    In a recent survey conducted by the government, 
                public sector employees were asked to rate their level 
                of satisfaction with the department they work at. 
                The results revealed that NASA was the most popular 
                department with a satisfaction rating of 95%.

                One NASA employee, John Smith, commented on the findings, 
                stating, "I'm not surprised that NASA came out on top. 
                It's a great place to work with amazing people and 
                incredible opportunities. I'm proud to be a part of 
                such an innovative organization."

                The results were also welcomed by NASA's management team, 
                with Director Tom Johnson stating, "We are thrilled to 
                hear that our employees are satisfied with their work at NASA. 
                We have a talented and dedicated team who work tirelessly 
                to achieve our goals, and it's fantastic to see that their 
                hard work is paying off."

                The survey also revealed that the 
                Social Security Administration had the lowest satisfaction 
                rating, with only 45% of employees indicating they were 
                satisfied with their job. The government has pledged to 
                address the concerns raised by employees in the survey and 
                work towards improving job satisfaction across all departments.
                """;

            string askForSentimentPrompt = $"""
                What is the sentiment of the following product review, 
                which is delimited with triple backticks?

                Give your answer as a single word, either "positive" \
                or "negative".

                Review text: '''{lamp_review}'''
                """;

            string emotionsPrompt = $"""
                Identify a list of emotions that the writer of the \
                following review is expressing. Include no more than \
                five items in the list. Format your answer as a list of \
                lower-case words separated by commas.

                Review text: '''{lamp_review}'''
                """;

            string identifyAnger = $"""
                Is the writer of the following review expressing anger?\
                The review is delimited with triple backticks. \
                Give your answer as either yes or no.

                Review text: '''{lamp_review}'''
                """;

            string extractProductCompanyNamePrompt = $"""
                Identify the following items from the review text: 
                - Item purchased by reviewer
                - Company that made the item

                The review is delimited with triple backticks. \
                Format your response as a JSON object with \
                "Item" and "Brand" as the keys. 
                If the information isn't present, use "unknown" \
                as the value.
                Make your response as short as possible.\

                Review text: '''{lamp_review}'''
                """;

            string multipleTaskPrompt = $"""
                Identifiez les élements suivants à partir du texte de l'avis client :
                    - Sentiment (positive ou négatif)
                    - Est ce que le client a exprimé un sentiment de colère (vrai ou faux)
                    - Produit acheté par le client
                    - La désignation de la société qui a fabriquée le produit
                Le texte de l'avis est mis entre triple backticks. \
                La réponse doit être formatée en tant qu'objet json avec  \
                "Sentiment", "Anger", "Produit" et "Brand" comme clés. \
                Si une information est manquante, utiliser "inconnue" comme valeur \
                Produire une réponse concise et breve autant que possible \
                La valuer concernant la coleur doit être mis au format boolean.

                Texte de l'avis : '''{lamp_review}'''
                """;

            string inferringPrompt = $"""
                Determinez cinq thèmes discutés dans le texte suivant \
                mis entre triple backticks.

                Chaque thème doit être identifié par un ou deux mots significatifs.
                La réponse doit être formaté sous la forme d'une liste d'élèments séparés par commas.

                Text sample: '''{story}'''
                """;

            ChatResponse response = await chatClient.GetResponseAsync(askForSentimentPrompt, CreateChatOptions(), cancellationToken);
            //Console.WriteLine("Ask for sentiment from a customer review  : ");
            //Console.WriteLine("--------------------------------------------");
            //Console.WriteLine(response.Text);
            //Console.WriteLine();

            //response = await chatClient.GetResponseAsync(emotionsPrompt, CreateChatOptions(), cancellationToken);
            //Console.WriteLine("Identify emotions from a customer review  : ");
            //Console.WriteLine("--------------------------------------------");
            //Console.WriteLine(response.Text);
            //Console.WriteLine();

            //response = await chatClient.GetResponseAsync(identifyAnger, CreateChatOptions(), cancellationToken);
            //Console.WriteLine("Identify possible anger emotion from a customer review  : ");
            //Console.WriteLine("----------------------------------------------------------");
            //Console.WriteLine(response.Text);
            //Console.WriteLine();

            //response = await chatClient.GetResponseAsync(extractProductCompanyNamePrompt, CreateChatOptions(), cancellationToken);
            //Console.WriteLine("Extract product and company name from customer review  : ");
            //Console.WriteLine("---------------------------------------------------------");
            //Console.WriteLine(response.Text);
            //Console.WriteLine();

            response = await chatClient.GetResponseAsync(multipleTaskPrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Réalisation de tâches multiple concernant un avis client  : ");
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine(response.Text);
            Console.WriteLine();

            response = await chatClient.GetResponseAsync(inferringPrompt, CreateChatOptions(), cancellationToken);
            Console.WriteLine("Inferrence de cinq thèmes à partir d'un texte  : ");
            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine(response.Text);
            Console.WriteLine();
        }

        public async Task RunExpandingPrompt(CancellationToken cancellationToken)
        {
            string review = """
                    So, they still had the 17 piece system on seasonal \
                sale for around $49 in the month of November, about \
                half off, but for some reason (call it price gouging) \
                around the second week of December the prices all went \
                up to about anywhere from between $70-$89 for the same \
                system. And the 11 piece system went up around $10 or \
                so in price also from the earlier sale price of $29. \
                So it looks okay, but if you look at the base, the part \
                where the blade locks into place doesn’t look as good \
                as in previous editions from a few years ago, but I \
                plan to be very gentle with it (example, I crush \
                very hard items like beans, ice, rice, etc. in the \ 
                blender first then pulverize them in the serving size \
                I want in the blender then switch to the whipping \
                blade for a finer flour, and use the cross cutting blade \
                first when making smoothies, then use the flat blade \
                if I need them finer/less pulpy). Special tip when making \
                smoothies, finely cut and freeze the fruits and \
                vegetables (if using spinach-lightly stew soften the \ 
                spinach then freeze until ready for use-and if making \
                sorbet, use a small to medium sized food processor) \ 
                that you plan to use that way you can avoid adding so \
                much ice if at all-when making your smoothie. \
                After about a year, the motor was making a funny noise. \
                I called customer service but the warranty expired \
                already, so I had to buy another one. FYI: The overall \
                quality has gone done in these types of products, so \
                they are kind of counting on brand recognition and \
                consumer loyalty to maintain sales. Got it in about \
                two days.
                """;

            string prompt = $"""
                You are a customer service AI assistant.
                Your task is to send an email reply to a valued customer.
                Given the customer email delimited by ```, \
                Generate a reply to thank the customer for their review.
                If the sentiment is positive or neutral, thank them for \
                their review.
                If the sentiment is negative, apologize and suggest that \
                they can reach out to customer service. 
                Make sure to use specific details from the review.
                Write in a concise and professional tone.
                Sign the email as `AI customer agent`.

                Customer review: ```{review}```                
                """;

            ChatResponse response = await chatClient.GetResponseAsync(prompt, CreateChatOptions(), cancellationToken);

            Console.WriteLine(response.Text);
        }        

        private ChatOptions CreateChatOptions(float? temperature = null) => new()
        {
            MaxOutputTokens = _generation.MaxOutputTokens,
            Temperature = temperature ?? _generation.Temperature
        };
    }

}
