using Microsoft.AspNetCore.Mvc;

namespace ManDrill.Client.Controllers
{
    using ManDrill.Client.Services;
    // Controllers/ChatController.cs
    using Microsoft.AspNetCore.Mvc;
    using Mscc.GenerativeAI;
    using System.Threading.Tasks;

    public class ChatController : Controller
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SendMessage([FromBody] ChatMessage model)
        {
            if (string.IsNullOrWhiteSpace(model.Message))
            {
                return Json(new { response = "Please enter a message." });
            }

            try
            {
                // Your AWS Bedrock integration here
                string response = await ProcessWithBedrock(model.Message, model.Context);

                return Json(new { response });
            }
            catch (Exception ex)
            {
                // Log the exception
                return Json(new { response = "Sorry, I encountered an error while processing your request." });
            }
        }

        private async Task<string> ProcessWithBedrock(string userMessage, string context)
        {
            // Your AWS Bedrock implementation
            // This is where you'll integrate with AWS Bedrock
            string prompt = $"Based on this context: {context}. Answer in 1-3 sentences (markdown-formatted and styled including bullet-pointed, paragraphs, highlighted keywords): {userMessage}.";
            // Example structure:
            var response = await (new AIService()).GenerateAnswerWithClaude(prompt);
            return response;
        }
    }

    public class ChatMessage
    {
        public string Message { get; set; }
        public string Context { get; set; }
    }
}
