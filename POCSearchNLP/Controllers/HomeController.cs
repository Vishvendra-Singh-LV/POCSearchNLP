using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using POCSearchNLP.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace POCSearchNLP.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        // Database schema used for text-to-SQL generation.
        // SQL Server style (IDENTITY, NVARCHAR, DECIMAL). Adjust dialect if targeting PostgreSQL.
        private const string _vehicleSchema = """
        CREATE SCHEMA IF NOT EXISTS dbo;
        SET search_path TO dbo;

        CREATE TABLE dbo.Oem (
            OemID SERIAL PRIMARY KEY,
            Name VARCHAR(100) NOT NULL UNIQUE
        );

        CREATE TABLE dbo.EquipmentModel (
            ModelID SERIAL PRIMARY KEY,
            OemID INT NOT NULL,
            ModelName VARCHAR(120) NOT NULL,
            Category VARCHAR(50),
            EngineBrand VARCHAR(50),
            EngineModel VARCHAR(60),
            DeckSizeInch SMALLINT,
            YearFrom SMALLINT,
            YearTo SMALLINT,
            CONSTRAINT FK_EquipmentModel_Oem FOREIGN KEY (OemID)
                REFERENCES dbo.Oem (OemID)
        );

        CREATE TABLE dbo.Part (
            PartID SERIAL PRIMARY KEY,
            ModelID INT NOT NULL,
            PartNumber VARCHAR(50) NOT NULL,
            PartName VARCHAR(120) NOT NULL,
            Category VARCHAR(50),
            Description TEXT,
            IsPremium BOOLEAN,
            PriceUSD DECIMAL(10,2),
            OemUrl TEXT,
            CONSTRAINT FK_Part_Model FOREIGN KEY (ModelID)
                REFERENCES dbo.EquipmentModel (ModelID)
        );
    """;

        public HomeController(
            ILogger<HomeController> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
        }

        public IActionResult Index()
        {
            return View();
        }

		[HttpPost]
        public async Task<IActionResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ViewBag.Error = "Please enter a search query.";
                return View("Index");
            }

            try
            {
                var (success, result) = await QueryOpenAI(query,HttpContext.RequestAborted);

                ViewBag.Query = query;
                ViewBag.Result = result;
                ViewBag.Success = true;

                // If result is a valid SQL query, execute it
                if (ViewBag.Success && result.Contains("SELECT"))
                {
                    
                    var dbResults = await ExecuteSqlQueryAsync(result);
                    ViewBag.DbResults = dbResults;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing search query: {Query}", query);
                ViewBag.Error = "An error occurred while processing your query. Please try again.";
            }

            return View("Index");
        }

        // Execute SQL query against PostgreSQL
        private async Task<List<Dictionary<string, object>>> ExecuteSqlQueryAsync(string sql)
        {
            var results = new List<Dictionary<string, object>>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i);
                }
                results.Add(row);
            }

            return results;
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
	

        private async Task<(bool, string)> QueryOpenAI([FromForm] string input, CancellationToken cancellationToken)
        {
            string result;
            var apiKey = _configuration["OpenAI:ApiKey"];
            
            var systemInstruction =
                $"You are a text to SQL converter. Use the following database schema (SQL Server dialect) to generate a query from the user prompt. Return only SQL, no explanation.\n\n{_vehicleSchema}";

            var requestPayload = new
            {
                model = "gpt-5-nano",
                messages = new[]
                {
                        new { role = "system", content = systemInstruction },
                        new { role = "user", content = input }
                    },
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OpenAI API error {Status}: {Body}", response.StatusCode, raw);
                    result = "OpenAI API call failed. " + raw;
                    return (false, result);
                }

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    result = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "";
                }
                catch (Exception exParse)
                {
                    _logger.LogError(exParse, "Failed to parse OpenAI response.");
                    result = "Failed to parse OpenAI response." + raw;
                    return (false, result);
                }

                return (true, result);
            }
            catch (OperationCanceledException)
            {
                result = "Request cancelled.";
                return (false, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error calling OpenAI.");
                result = "Unexpected server error";
                return (false, result);
            }
        }
    }
}
