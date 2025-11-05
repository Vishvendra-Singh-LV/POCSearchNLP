using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using POCSearchNLP.Models;

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
    CREATE TABLE Make (
        MakeID INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL UNIQUE
    );

    CREATE TABLE Model (
        ModelID INT IDENTITY(1,1) PRIMARY KEY,
        MakeID INT NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        YearFrom SMALLINT NULL,
        YearTo SMALLINT NULL,
        BodyStyle NVARCHAR(50) NULL,
        CONSTRAINT FK_Model_Make FOREIGN KEY (MakeID)
            REFERENCES Make (MakeID),
        CONSTRAINT UQ_Model UNIQUE (MakeID, Name, YearFrom, YearTo)
    );

    CREATE TABLE PartsInfo (
        PartID INT IDENTITY(1,1) PRIMARY KEY,
        ModelID INT NOT NULL,
        PartNumber NVARCHAR(50) NOT NULL,
        PartName NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500) NULL,
        Category NVARCHAR(50) NULL,
        Price DECIMAL(10,2) NULL,
        CONSTRAINT FK_PartsInfo_Model FOREIGN KEY (ModelID)
            REFERENCES Model (ModelID),
        CONSTRAINT UQ_PartsInfo UNIQUE (ModelID, PartNumber)
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
				// TODO: Implement your NLP logic here
				// This is where you'll integrate with Azure OpenAI
				var result = await ProcessNaturalLanguageQuery(query);

				ViewBag.Query = query;
				ViewBag.Result = result;
				ViewBag.Success = true;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing search query: {Query}", query);
				ViewBag.Error = "An error occurred while processing your query. Please try again.";
			}

			return View("Index");
		}

		private async Task<string> ProcessNaturalLanguageQuery(string query)
		{
			// Placeholder for your NLP processing logic
			// Replace this with your Azure OpenAI integration
			await Task.Delay(1000); // Simulate processing time

			return $"Processed query: '{query}'\n\nGenerated SQL: SELECT * FROM PartsInfo WHERE PartName LIKE '%{query}%'\n\nResults: Found 5 matching parts.";
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
	

        // POST: /Home/QueryOpenAI
        [HttpPost]
        public async Task<IActionResult> QueryOpenAI([FromForm] string prompt, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return BadRequest(new { error = "Prompt is required." });

            var apiKey = _configuration["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { error = "OpenAI API key not configured. Set OpenAI:ApiKey in appsettings or user secrets." });

            var systemInstruction =
                $"You are a text to SQL converter. Use the following database schema (SQL Server dialect) to generate a query from the user prompt. Return only SQL, no explanation.\n\n{_vehicleSchema}";

            var requestPayload = new
            {
                model = "gpt-5-nano",
                messages = new[]
                {
                        new { role = "system", content = systemInstruction },
                        new { role = "user", content = prompt }
                    },
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OpenAI API error {Status}: {Body}", response.StatusCode, raw);
                    return StatusCode((int)response.StatusCode, new { error = "OpenAI API call failed.", details = raw });
                }

                string? answer = null;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    answer = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                }
                catch (Exception exParse)
                {
                    _logger.LogError(exParse, "Failed to parse OpenAI response.");
                    return StatusCode(500, new { error = "Failed to parse OpenAI response.", raw });
                }

                return Json(new
                {
                    prompt,
                    reply = answer,
                    model = requestPayload.model
                });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, new { error = "Request cancelled." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error calling OpenAI.");
                return StatusCode(500, new { error = "Unexpected server error." });
            }
        }
    }
}
