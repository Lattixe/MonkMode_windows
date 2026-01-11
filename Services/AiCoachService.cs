using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MonkMode.Models;

namespace MonkMode.Services;

/// <summary>
/// AI Coach service that analyzes session data and provides personalized coaching.
/// Uses OpenAI API (gpt-4o-mini) for cost-effective analysis.
/// </summary>
public class AiCoachService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string Model = "gpt-4o-mini";
    private bool _isDisposed;

    public AiCoachService(string? apiKey = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    /// <summary>
    /// Check if the AI Coach is properly configured with an API key.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    /// <summary>
    /// Analyze a completed session and generate coaching feedback.
    /// </summary>
    public async Task<string> AnalyzeSessionAsync(SessionLog session, string? userNotes = null)
    {
        if (!IsConfigured)
        {
            return GetFallbackAdvice(session);
        }

        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(session, userNotes);

            var requestBody = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = 300,
                temperature = 0.7
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(ApiUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return GetFallbackAdvice(session) + "\n\n(AI Coach temporarily unavailable)";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseData = JsonDocument.Parse(responseJson);
            
            var aiResponse = responseData.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return aiResponse ?? GetFallbackAdvice(session);
        }
        catch (Exception)
        {
            return GetFallbackAdvice(session);
        }
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are a compassionate but direct productivity coach named "The Monk". 
            Your role is to help users reflect on their focus sessions and improve their deep work habits.

            Guidelines:
            - Be encouraging but honest about patterns you notice
            - Give ONE specific, actionable tip (not generic advice like "turn off notifications")
            - Reference the actual data from their session
            - If they had distractions, help them understand the trigger pattern
            - If they had a clean session, acknowledge their progress
            - Keep responses concise (2-3 sentences max)
            - Use a calm, wise tone - like a meditation teacher

            Avoid:
            - Generic productivity platitudes
            - Lengthy explanations
            - Multiple tips at once
            - Judgmental language
            """;
    }

    private static string BuildUserPrompt(SessionLog session, string? userNotes)
    {
        var prompt = new StringBuilder();
        
        prompt.AppendLine($"Session Summary:");
        prompt.AppendLine($"- Task: {session.TaskName}");
        prompt.AppendLine($"- Duration: {session.Duration.TotalMinutes:F0} minutes");
        prompt.AppendLine($"- Intensity Level: {GetIntensityName(session.IntensityLevel)}");
        prompt.AppendLine($"- Distractions Blocked: {session.InterventionCount}");
        
        if (session.FlowRating > 0)
        {
            prompt.AppendLine($"- Self-Rated Flow: {session.FlowRating}/5");
        }

        if (!string.IsNullOrEmpty(userNotes))
        {
            prompt.AppendLine($"\nUser's reflection: \"{userNotes}\"");
        }

        if (session.BlockedProcesses.Count > 0)
        {
            prompt.AppendLine($"\nBlocked apps that were attempted: {string.Join(", ", session.BlockedProcesses)}");
        }

        prompt.AppendLine("\nProvide a brief coaching insight based on this session.");

        return prompt.ToString();
    }

    private static string GetIntensityName(int level) => level switch
    {
        1 => "Flow (50% dim)",
        2 => "Deep Work (90% dark)",
        3 => "Blackout (100% void)",
        _ => "Unknown"
    };

    /// <summary>
    /// Fallback advice when AI is unavailable.
    /// </summary>
    private static string GetFallbackAdvice(SessionLog session)
    {
        if (session.InterventionCount == 0)
        {
            return session.Duration.TotalMinutes switch
            {
                < 25 => "🧘 Good start. Try extending your next session to 25 minutes to build your focus muscle.",
                < 50 => "🔥 Solid focus session. You're building momentum. Consider what made this session distraction-free.",
                _ => "🏆 Exceptional focus. You've entered deep work territory. Protect this ability fiercely."
            };
        }
        
        if (session.InterventionCount <= 3)
        {
            return "💪 You faced temptation and stayed the course. Notice what triggered those urges - was it a specific thought or feeling? Awareness is the first step.";
        }
        
        return "🌊 Many attempted distractions suggest your mind was restless. Before your next session, try a 2-minute breathing exercise to settle your attention.";
    }

    /// <summary>
    /// Generate a weekly summary of focus patterns.
    /// </summary>
    public async Task<string> GenerateWeeklySummaryAsync(List<SessionLog> sessions)
    {
        if (!IsConfigured || sessions.Count == 0)
        {
            return GetFallbackWeeklySummary(sessions);
        }

        try
        {
            var totalTime = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));
            var totalInterventions = sessions.Sum(s => s.InterventionCount);
            var avgFlow = sessions.Where(s => s.FlowRating > 0).Select(s => s.FlowRating).DefaultIfEmpty(0).Average();

            var prompt = $"""
                Analyze this week's focus data and provide a brief insight:
                - Total sessions: {sessions.Count}
                - Total focus time: {totalTime.TotalHours:F1} hours
                - Total distractions blocked: {totalInterventions}
                - Average flow rating: {avgFlow:F1}/5
                - Most common tasks: {string.Join(", ", sessions.Select(s => s.TaskName).Distinct().Take(3))}

                Give a 2-sentence weekly insight focusing on patterns or progress.
                """;

            var requestBody = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = "You are a productivity coach providing weekly summaries. Be concise and insightful." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 150,
                temperature = 0.7
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(ApiUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                return GetFallbackWeeklySummary(sessions);
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseData = JsonDocument.Parse(responseJson);
            
            return responseData.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? GetFallbackWeeklySummary(sessions);
        }
        catch
        {
            return GetFallbackWeeklySummary(sessions);
        }
    }

    private static string GetFallbackWeeklySummary(List<SessionLog> sessions)
    {
        if (sessions.Count == 0)
        {
            return "📊 No sessions recorded this week. Start with just one 25-minute focus session today.";
        }

        var totalTime = TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks));
        var totalInterventions = sessions.Sum(s => s.InterventionCount);

        return $"📊 This week: {sessions.Count} sessions, {totalTime.TotalHours:F1} hours of focus, {totalInterventions} distractions blocked. Keep building the habit.";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _httpClient.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
