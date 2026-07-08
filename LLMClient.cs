using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using KSA;

namespace KSAAdvisor;

public class LLMClient
{
    private readonly HttpClient      _http;
    private readonly GameStateReader _reader;
    private Config                   _config;

    private const string DefaultBaseUrl = "https://openrouter.ai/api/v1";

    private string EffectiveBaseUrl =>
        string.IsNullOrWhiteSpace(_config.BaseUrl) ? DefaultBaseUrl : _config.BaseUrl;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object[] Tools =
    {
        new {
            type     = "function",
            function = new {
                name        = "get_vessel_telemetry",
                description = "Get current vessel telemetry: orbit, altitude, fuel, delta-V, TWR, attitude, situation.",
                parameters  = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            }
        },
        new {
            type     = "function",
            function = new {
                name        = "get_body_info",
                description = "Get detailed info about a celestial body: orbital elements, mass, SOI, atmosphere, day length.",
                parameters  = new {
                    type       = "object",
                    properties = new {
                        name = new { type = "string", description = "Body name from the BODIES IN SYSTEM list" }
                    },
                    required = new[] { "name" }
                }
            }
        },
        new {
            type     = "function",
            function = new {
                name        = "get_transfer_window",
                description = "Calculate Hohmann transfer window to a target body. Returns days to depart and transit time.",
                parameters  = new {
                    type       = "object",
                    properties = new {
                        target = new { type = "string", description = "Target body name: Luna, Mars, Venus, Jupiter, Saturn" }
                    },
                    required = new[] { "target" }
                }
            }
        },
        new {
            type     = "function",
            function = new {
                name        = "get_planet_positions",
                description = "Get current true anomalies (orbital positions) of all planets and moons.",
                parameters  = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            }
        },
        new {
            type     = "function",
            function = new {
                name        = "get_burns",
                description = "Get currently planned maneuvers (burns) in the vessel flight plan. Always check this before creating a new maneuver.",
                parameters  = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            }
        },
        new {
            type     = "function",
            function = new {
                name        = "get_other_vessels",
                description = "Get list of other vessels in the system with their orbits.",
                parameters  = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            }
        },
        new {
            type     = "function",
            function = new {
                name        = "create_circularization_burn",
                description = "Create a real maneuver node to circularize the orbit. Check get_burns() first to avoid duplicates.",
                parameters  = new {
                    type       = "object",
                    properties = new {
                        at = new { type = "string", description = "Where to circularize: 'apoapsis' or 'periapsis'" }
                    },
                    required = new[] { "at" }
                }
            }
        },
        new {
            type     = "function",
            function = new {
                name        = "warp_time",
                description = "Warp game time forward by specified number of days.",
                parameters  = new {
                    type       = "object",
                    properties = new {
                        days = new { type = "number", description = "Number of days to warp forward" }
                    },
                    required = new[] { "days" }
                }
            }
        }
    };

    public LLMClient(Config config, GameStateReader reader)
    {
        _config = config;
        _reader = reader;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        ApplyConfig();
    }

    public void UpdateConfig(Config config)
    {
        _config = config;
        ApplyConfig();
    }

    private void ApplyConfig()
    {
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization",  $"Bearer {_config.ApiKey}");
        _http.DefaultRequestHeaders.Add("HTTP-Referer",   "https://github.com/losyakov199566-creator/KSAAdvisor");
        _http.DefaultRequestHeaders.Add("X-Title",        "KSA AI Advisor");
    }

    private string ExecuteTool(string name, string argsJson)
    {
        try
        {
            switch (name)
            {
                case "get_vessel_telemetry":
                    return _reader.GetVesselTelemetry();

                case "get_body_info":
                {
                    using var doc = JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
                    var bodyName = doc.RootElement.TryGetProperty("name", out var n)
                        ? n.GetString() ?? "" : "";
                    return _reader.GetBodyInfo(bodyName);
                }

                case "get_planet_positions":
                    return _reader.GetPlanetPositions();

                case "get_burns":
                    return _reader.GetBurns();

                case "get_other_vessels":
                    return _reader.GetOtherVessels();

                case "create_circularization_burn":
                {
                    using var doc = JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
                    var at = doc.RootElement.TryGetProperty("at", out var a)
                        ? a.GetString() ?? "apoapsis" : "apoapsis";
                    return _reader.CreateCircularizationBurn(at);
                }

                case "get_transfer_window":
                {
                    using var doc  = JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
                    var target = doc.RootElement.TryGetProperty("target", out var t)
                        ? t.GetString() ?? "" : "";
                    return _reader.GetTransferWindow(target);
                }

                case "warp_time":
                {
                    using var doc = JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
                    var days = doc.RootElement.TryGetProperty("days", out var d)
                        ? d.GetDouble() : 0;
                    var targetTime = Universe.GetElapsedSimTime() + (days * 86400.0);
                    InputEvents.AutoWarpBuffer.Add(new InputEvents.AutoWarpData
                    {
                        StopWarp   = false,
                        WarpToTime = targetTime
                    });
                    var human = GameStateReader.FmtDuration(days);
                    AdvisorMod.Log($"Warp initiated: +{days:F5} days (~{human})");
                    return $"Time warp initiated: +{days:F5} days (~{human}).";
                }

                default:
                    return $"Unknown tool: {name}";
            }
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"Tool '{name}' error: {ex.Message}");
            return $"Tool error: {ex.Message}";
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string        systemPrompt,
        List<Message> history,
        string        userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<object>();
        messages.Add(new { role = "system", content = systemPrompt });
        foreach (var m in history.TakeLast(_config.HistoryLimit))
            messages.Add(new { role = m.Role, content = m.Content });
        messages.Add(new { role = "user", content = userMessage });

        for (int round = 0; round < 4; round++)
        {
            bool   isLastRound = round == 3;
            string? connError  = null;
            HttpResponseMessage? resp = null;

            var body = JsonSerializer.Serialize(new
            {
                model       = _config.Model,
                max_tokens  = _config.MaxTokens,
                stream      = true,
                messages,
                tools       = isLastRound ? null : (object)Tools,
                tool_choice = isLastRound ? null : (object)"auto",
                provider    = new { require_parameters = true }
            }, JsonOpts);

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post,
                    EffectiveBaseUrl.TrimEnd('/') + "/chat/completions")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) { connError = ex.Message; }

            if (connError != null)
            {
                AdvisorMod.Log($"Connection error (round {round}): {connError}");
                yield return $"[Connection error: {connError}]";
                yield break;
            }

            var contentType = resp!.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("text/"))
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                AdvisorMod.Log($"API error ({contentType}): {errBody[..Math.Min(200, errBody.Length)]}");
                yield return $"[API error: {errBody[..Math.Min(200, errBody.Length)]}]";
                yield break;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var toolCalls   = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
            bool hasTools   = false;
            string? finish  = null;
            int yieldedChunks = 0;

            while (true)
            {
                string? line    = null;
                bool    lineErr = false;
                try { line = await reader.ReadLineAsync(); }
                catch { lineErr = true; }

                if (lineErr || line == null) break;
                if (!line.StartsWith("data: ")) continue;

                var data = line[6..];
                if (data == "[DONE]") break;

                string? deltaContent = null;
                bool    hasTcDelta   = false;
                var     tcDeltas     = new List<(int idx, string? id, string? name, string? args)>();

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var choices   = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0) continue;
                    var choice    = choices[0];

                    if (choice.TryGetProperty("finish_reason", out var fr) &&
                        fr.ValueKind != JsonValueKind.Null)
                        finish = fr.GetString();

                    if (!choice.TryGetProperty("delta", out var delta)) continue;

                    if (delta.TryGetProperty("content", out var c) &&
                        c.ValueKind == JsonValueKind.String)
                        deltaContent = c.GetString();

                    if (delta.TryGetProperty("tool_calls", out var tcArr))
                    {
                        hasTcDelta = true;
                        foreach (var tc in tcArr.EnumerateArray())
                        {
                            var idx              = tc.GetProperty("index").GetInt32();
                            string? id           = null, tname = null, targs = null;
                            if (tc.TryGetProperty("id",   out var idEl)) id    = idEl.GetString();
                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name",      out var nEl)) tname = nEl.GetString();
                                if (fn.TryGetProperty("arguments", out var aEl) &&
                                    aEl.ValueKind == JsonValueKind.String)       targs = aEl.GetString();
                            }
                            tcDeltas.Add((idx, id, tname, targs));
                        }
                    }
                }
                catch { continue; }

                if (hasTcDelta)
                {
                    hasTools = true;
                    foreach (var (idx, id, tname, targs) in tcDeltas)
                    {
                        if (!toolCalls.ContainsKey(idx))
                            toolCalls[idx] = (id ?? "", tname ?? "", new StringBuilder());
                        var entry = toolCalls[idx];
                        toolCalls[idx] = (id ?? entry.Id, tname ?? entry.Name, entry.Args);
                        if (targs != null) toolCalls[idx].Args.Append(targs);
                    }
                }

                if (!string.IsNullOrEmpty(deltaContent))
                {
                    yieldedChunks++;
                    yield return deltaContent;
                }
            }

            AdvisorMod.Log($"Round {round}: finish={finish ?? "null"}, hasTools={hasTools}, chunks={yieldedChunks}");

            if (hasTools && finish == "tool_calls" && toolCalls.Count > 0)
            {
                var tcList = toolCalls.Values.Select(tc => new
                {
                    id       = tc.Id,
                    type     = "function",
                    function = new { name = tc.Name, arguments = tc.Args.ToString() }
                }).ToArray();
                messages.Add(new { role = "assistant", content = (string?)null, tool_calls = tcList });

                foreach (var (_, (id, name, args)) in toolCalls)
                {
                    AdvisorMod.Log($"Tool call: {name}({args})");
                    var result = ExecuteTool(name, args.ToString());
                    AdvisorMod.Log($"Tool result ({name}): {result[..Math.Min(120, result.Length)]}");
                    messages.Add(new { role = "tool", tool_call_id = id, content = result });
                }

                toolCalls.Clear();
                hasTools = false;
                continue;
            }

            yield break;
        }
    }
}
