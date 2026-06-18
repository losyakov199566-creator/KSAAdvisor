using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KSAAdvisor;

// Вызывает OpenRouter API со стримингом ответа по SSE
public class LLMClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private Config _config;

    public LLMClient(Config config) => _config = config;

    public void UpdateConfig(Config config) => _config = config;

    public async IAsyncEnumerable<string> StreamAsync(
        string           systemPrompt,
        List<Message>    history,
        string           userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        foreach (var m in history.TakeLast(10))
            messages.Add(new { role = m.Role, content = m.Content });
        messages.Add(new { role = "user", content = userMessage });

        var body = JsonSerializer.Serialize(new
        {
            model      = _config.Model,
            max_tokens = _config.MaxTokens,
            stream     = true,
            messages
        });

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            _config.BaseUrl.TrimEnd('/') + "/chat/completions");

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        req.Headers.Add("HTTP-Referer", "https://github.com/ksa-advisor");
        req.Headers.Add("X-Title", "KSA AI Advisor");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // yield нельзя внутри catch — сохраняем ошибку в переменную
        HttpResponseMessage? resp = null;
        string? connError = null;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            connError = ex.Message;
        }

        if (connError != null)
        {
            AdvisorMod.Log($"Connection error: {connError}");
            yield return $"[Connection error: {connError}]";
            yield break;
        }

        // Не используем resp.StatusCode/IsSuccessStatusCode —
        // конфликт с System.Net.Http.dll из папки игры.
        // Определяем ошибку по Content-Type:
        // успех (SSE) → text/event-stream
        // ошибка API → application/json
        var contentType = resp!.Content.Headers.ContentType?.MediaType ?? "";
        if (!contentType.Contains("text/"))
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            var errMsg  = $"[API error: {errBody[..Math.Min(300, errBody.Length)]}]";
            AdvisorMod.Log(errMsg);
            yield return errMsg;
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            // yield нельзя в catch — используем флаг
            string? line   = null;
            bool readError = false;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch
            {
                readError = true;
            }

            if (readError)    yield break;
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line))  continue;
            if (!line.StartsWith("data: "))        continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            string? token = null;
            try
            {
                var node = JsonNode.Parse(data);
                token = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            }
            catch { }

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    public void Dispose() => _http.Dispose();
}