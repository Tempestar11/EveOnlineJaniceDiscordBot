using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EveOnlineBot
{
    class Program
    {
        private readonly DiscordSocketClient _client;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public Program()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("X-ApiKey", _configuration["Janice:ApiKey"]);
        }

        public static async Task Main(string[] args)
        {
            var program = new Program();
            await program.MainAsync();
        }

        public async Task MainAsync()
        {
            _client.Log += Log;
            _client.MessageReceived += MessageReceived;

            await _client.LoginAsync(TokenType.Bot, _configuration["Discord:Token"]);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{msg.Severity}] {msg.Source}: {msg.Message}");
            return Task.CompletedTask;
        }

        private async Task MessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            if (Regex.IsMatch(message.Content, @"^!appraise\b"))
            {
                // Appraisal command
                var content = Regex.Replace(message.Content, @"^!appraise\b", "").Trim();
                if (string.IsNullOrEmpty(content))
                {
                    await message.Channel.SendMessageAsync("Please provide items to appraise.");
                    return;
                }

                try
                {
                    // Split the input into lines and process each line
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var requestBody = string.Join("\n", lines);
                    
                    var fullAppraisal = await GetAppraisal(requestBody, 1d, 2);
                    var ninetyPercentAppraisal = await GetAppraisal(requestBody, 0.9d, 2);

                    if (!fullAppraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                    {
                        await message.Channel.SendMessageAsync("No valid items found in the appraisal.");
                        return;
                    }

                    var totalSellValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                    var totalBuyValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalSplitValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                    var totalBuyValue90Percent = ninetyPercentAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalVolume = fullAppraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = fullAppraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = fullAppraisal.GetProperty("market").GetProperty("name").GetString();
                    var fullAppraisalCode = fullAppraisal.GetProperty("code").GetString();
                    var ninetyPercentAppraisalCode = ninetyPercentAppraisal.GetProperty("code").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("Total Appraisal")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Sell Value: {totalSellValue:N2} ISK\n" +
                        $"Buy Value: {totalBuyValue:N2} ISK\n" +
                        $"Split Value: {totalSplitValue:N2} ISK\n" +
                        $"Buy Value @90%: {totalBuyValue90Percent:N2} ISK", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Full Appraisal Code", fullAppraisalCode, false);
                    embed.AddField("90% Appraisal Code", ninetyPercentAppraisalCode, false);

                    await message.Channel.SendMessageAsync(embed: embed.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error getting appraisal: {ex.Message}");
                }
            }

            // Recall command
            else if (Regex.IsMatch(message.Content, @"^!recall\b"))
            {
                var code = Regex.Replace(message.Content, @"^!recall\b", "").Trim();
                if (string.IsNullOrEmpty(code))
                {
                    await message.Channel.SendMessageAsync("Please provide an appraisal code to recall.");
                    return;
                }

                try
                {
                    var url = $"{_configuration["Janice:BaseUrl"]}/appraisal/{code}";
                    var response = await _httpClient.GetAsync(url);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"API request failed with status {response.StatusCode}: {responseContent}");
                    }

                    var appraisal = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var totalSellValue = appraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                    var totalBuyValue = appraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalSplitValue = appraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                    var totalVolume = appraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = appraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = appraisal.GetProperty("market").GetProperty("name").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("Recalled Appraisal")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Sell Value: {totalSellValue:N2} ISK\n" +
                        $"Buy Value: {totalBuyValue:N2} ISK\n" +
                        $"Split Value: {totalSplitValue:N2} ISK\n", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Appraisal Code", code, false);

                    await message.Channel.SendMessageAsync(embed: embed.Build());
                }                
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error recalling appraisal: {ex.Message}");
                }
            }

            // NPC market
            else if (Regex.IsMatch(message.Content, @"^!npcbuy90\b", RegexOptions.IgnoreCase))
            {
                var content = Regex.Replace(message.Content, @"^!npcbuy90\b", "", RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrEmpty(content))
                {
                    await message.Channel.SendMessageAsync("Please provide items to appraise.");
                    return;
                }

                try
                {
                    // Split the input into lines and process each line
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var requestBody = string.Join("\n", lines);
                    

                    var appraisal = await GetAppraisal(requestBody, 0.9d, 6);
                    if (!appraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                    {
                        await message.Channel.SendMessageAsync("No valid items found in the appraisal.");
                        return;
                    }

                    var totalBuyValue = appraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalVolume = appraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = appraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = appraisal.GetProperty("market").GetProperty("name").GetString();
                    var appraisalCode = appraisal.GetProperty("code").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("NPC Buy @90%")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Buy Value: {totalBuyValue:N2} ISK\n", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Appraisal Code", appraisalCode, false);

                    await message.Channel.SendMessageAsync(embed: embed.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error getting appraisal: {ex.Message}");
                }
            }

            // NPC market
            else if (Regex.IsMatch(message.Content, @"^!npcbuy"))
            {
                var content = Regex.Replace(message.Content, @"^!npcbuy", "").Trim();
                if (string.IsNullOrEmpty(content))
                {
                    await message.Channel.SendMessageAsync("Please provide items to appraise.");
                    return;
                }

                try
                {
                    // Split the input into lines and process each line
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var requestBody = string.Join("\n", lines);

                    var appraisal = await GetAppraisal(requestBody, 1d, 6);
                    if (!appraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                    {
                        await message.Channel.SendMessageAsync("No valid items found in the appraisal.");
                        return;
                    }

                    var totalBuyValue = appraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalVolume = appraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = appraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = appraisal.GetProperty("market").GetProperty("name").GetString();
                    var appraisalCode = appraisal.GetProperty("code").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("Total Appraisal")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Buy Value: {totalBuyValue:N2} ISK\n", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Appraisal Code", appraisalCode, false);

                    await message.Channel.SendMessageAsync(embed: embed.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error getting appraisal: {ex.Message}");
                }
            }

            
        }

        private async Task<JsonElement> GetAppraisal(string items, double percentage, int market)

        {
            /// <summary>
            /// Makes a request to the Janice API to get an appraisal for the given items.
            /// </summary>
            /// <param name="items">The items to appraise, formatted as a string.</param>
            /// <param name="percentage">The percentage of the appraisal to return (100 for full, 90 for 90%).</param>
            /// <returns>A JsonElement containing the appraisal data.</returns>
            /// <exception cref="Exception">Thrown if the API request fails or returns an error.</exception>
            /// <exception cref="JsonException">Thrown if the response cannot be deserialized into a JsonElement.</exception>
            /// <exception cref="ArgumentNullException">Thrown if the items string is null or empty.</exception>
            /// <exception cref="HttpRequestException">Thrown if there is an issue with the HTTP request.</exception>
            try
            {
                var url = $"{_configuration["Janice:BaseUrl"]}/appraisal?market={market}&persist=true&compactize=true&pricePercentage={percentage}";

                Console.WriteLine($"Making request to: {url}");
                Console.WriteLine($"Using API Key: {_configuration["Janice:ApiKey"]}");

                var content = new StringContent(
                    items,
                    System.Text.Encoding.UTF8,
                    "text/plain"
                );

                // Add headers to match curl example
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"Response Status: {response.StatusCode}");
                Console.WriteLine($"Response Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
                Console.WriteLine($"Response Content: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API request failed with status {response.StatusCode}: {responseContent}");
                }

                if (string.IsNullOrEmpty(responseContent))
                {
                    throw new Exception("API returned empty response");
                }

                if (responseContent.StartsWith("<"))
                {
                    throw new Exception("API returned HTML instead of JSON. Please check your API key and endpoint configuration.");
                }

                return JsonSerializer.Deserialize<JsonElement>(responseContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAppraisal: {ex}");
                throw;
            }
        }

        private Embed CreateAppraisalEmbed(JsonElement appraisal, string itemName)
        {
            // Ensure the title doesn't exceed Discord's 256 character limit
            var title = $"Appraisal for {itemName}";
            if (title.Length > 256)
            {
                title = title.Substring(0, 253) + "...";
            }

            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            try
            {
                if (appraisal.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var item = items[0];
                    if (item.TryGetProperty("effectivePrices", out var prices))
                    {
                        // Add immediate prices
                        builder.AddField("Buy Price", $"{prices.GetProperty("buyPrice").GetDecimal():N2} ISK", true);
                        builder.AddField("Sell Price", $"{prices.GetProperty("sellPrice").GetDecimal():N2} ISK", true);
                        builder.AddField("Split Price", $"{prices.GetProperty("splitPrice").GetDecimal():N2} ISK", true);

                        // Add 5-day median prices
                        builder.AddField("5-Day Median Buy", $"{prices.GetProperty("buyPrice5DayMedian").GetDecimal():N2} ISK", true);
                        builder.AddField("5-Day Median Sell", $"{prices.GetProperty("sellPrice5DayMedian").GetDecimal():N2} ISK", true);
                        builder.AddField("5-Day Median Split", $"{prices.GetProperty("splitPrice5DayMedian").GetDecimal():N2} ISK", true);

                        // Add market information
                        if (appraisal.TryGetProperty("market", out var market))
                        {
                            builder.AddField("Market", market.GetProperty("name").GetString(), true);
                        }

                        // Add volume information
                        if (item.TryGetProperty("buyOrderCount", out var buyOrders) && 
                            item.TryGetProperty("sellOrderCount", out var sellOrders))
                        {
                            builder.AddField("Buy Orders", buyOrders.GetInt32().ToString("N0"), true);
                            builder.AddField("Sell Orders", sellOrders.GetInt32().ToString("N0"), true);
                        }

                        // Add timestamp
                        if (appraisal.TryGetProperty("datasetTime", out var datasetTime))
                        {
                            builder.WithFooter($"Data from {DateTime.Parse(datasetTime.GetString()).ToLocalTime():g}");
                        }
                    }
                    else
                    {
                        builder.AddField("Error", "No price data available", false);
                    }
                }
                else
                {
                    builder.AddField("Error", "Item not found", false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateAppraisalEmbed: {ex}");
                builder.AddField("Error", "Failed to parse price data", false);
            }

            return builder.Build();
        }
    }
} 
