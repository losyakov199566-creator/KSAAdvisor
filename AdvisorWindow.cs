using System.Runtime.InteropServices;
using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSAAdvisor;

public class AdvisorWindow
{
    private readonly ChatManager     _chats;
    private readonly GameStateReader _reader;
    private readonly LLMClient       _llm;
    private readonly Config          _config;

    private bool   _open          = false;
    private byte[] _inputBuf      = new byte[2048];
    private byte[] _apiKeyBuf     = new byte[256];
    private byte[] _modelBuf      = new byte[128];
    private byte[] _baseUrlBuf    = new byte[256];
    private byte[] _nameBuf       = new byte[64];
    private string _lastSessId    = "";
    private bool   _isStreaming   = false;
    private bool   _scrollToEnd   = false;
    private string _streamingText = "";
    private readonly object _streamLock = new();
    private CancellationTokenSource? _cts;

    private bool _showSetup          = false;
    private bool _setupBuffersLoaded = false;

    // ── Бенчмарк ──────────────────────────────────────────────────────────
    private bool          _benchmarkRunning   = false;
    private int           _benchmarkIndex     = 0;
    private List<string>  _benchmarkQuestions = new();
    private StringBuilder _benchmarkLog       = new();
    private const string  BenchmarkFile       = "benchmark_questions.txt";
    private const string  BenchmarkResults    = "benchmark_results.txt";

    // ── Win32 буфер обмена ────────────────────────────────────────────────

    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint f);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);

    private static string GetClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero)) return "";
        try
        {
            var h = GetClipboardData(13); // CF_UNICODETEXT
            if (h == IntPtr.Zero) return "";
            var p = GlobalLock(h);
            try { return Marshal.PtrToStringUni(p) ?? ""; }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    public AdvisorWindow(ChatManager chats, GameStateReader reader, LLMClient llm, Config config)
    {
        _chats  = chats;
        _reader = reader;
        _llm    = llm;
        _config = config;
    }

    public void Toggle() => _open = !_open;
    public void SaveAll() => _chats.SaveAll();

    public void Draw()
    {
        if (!_open) return;

        ImGui.SetNextWindowSize(new float2(620, 520), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("KSA AI Advisor  [F2]", ref _open))
        {
            ImGui.End();
            return;
        }

        bool needSetup = string.IsNullOrEmpty(_config.ApiKey) || string.IsNullOrEmpty(_config.Model);

        if (needSetup || _showSetup)
        {
            if (!_setupBuffersLoaded)
            {
                LoadSetupBuffers();
                _setupBuffersLoaded = true;
            }
            DrawSetup(allowCancel: !needSetup);
        }
        else
        {
            DrawChat();
        }

        ImGui.End();
    }

    private void LoadSetupBuffers()
    {
        ClearSetupBuffers();

        void Fill(byte[] buf, string? val)
        {
            if (string.IsNullOrEmpty(val)) return;
            var b = Encoding.UTF8.GetBytes(val);
            Array.Copy(b, buf, Math.Min(b.Length, buf.Length - 1));
        }

        Fill(_apiKeyBuf,  _config.ApiKey);
        Fill(_modelBuf,   _config.Model);
        Fill(_baseUrlBuf, _config.BaseUrl);
    }

    private void ClearSetupBuffers()
    {
        Array.Clear(_apiKeyBuf,  0, _apiKeyBuf.Length);
        Array.Clear(_modelBuf,   0, _modelBuf.Length);
        Array.Clear(_baseUrlBuf, 0, _baseUrlBuf.Length);
    }

    private void DrawSetup(bool allowCancel)
    {
        ImGui.Spacing();
        ImGui.TextWrapped(allowCancel
            ? "Update your API credentials."
            : "Enter your API credentials to use the advisor.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var fullW = ImGui.GetContentRegionAvail().X;

        ImGui.Text("API Key");
        ImGui.SetNextItemWidth(fullW);
        ImGui.InputText("##apikey", _apiKeyBuf, ImGuiInputTextFlags.Password);
        ImGui.Spacing();

        ImGui.Text("Model");
        ImGui.SetNextItemWidth(fullW);
        ImGui.InputText("##model", _modelBuf);
        ImGui.Spacing();

        ImGui.Text("Provider URL");
        ImGui.SetNextItemWidth(fullW);
        ImGui.InputText("##baseurl", _baseUrlBuf);
        ImGui.TextWrapped("Leave empty to use OpenRouter (default).");
        ImGui.Spacing();

        if (ImGui.Button("Save", new float2(120, 0)))
        {
            var key     = Encoding.UTF8.GetString(_apiKeyBuf).TrimEnd('\0').Trim();
            var model   = Encoding.UTF8.GetString(_modelBuf).TrimEnd('\0').Trim();
            var baseUrl = Encoding.UTF8.GetString(_baseUrlBuf).TrimEnd('\0').Trim();

            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(model))
            {
                _config.ApiKey  = key;
                _config.Model   = model;
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    if (!baseUrl.StartsWith("http"))
                        baseUrl = "https://" + baseUrl;
                    _config.BaseUrl = baseUrl;
                }
                _config.Save();
                _llm.UpdateConfig(_config);
                ClearSetupBuffers();
                _showSetup          = false;
                _setupBuffersLoaded = false;
            }
        }

        if (allowCancel)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new float2(120, 0)))
            {
                ClearSetupBuffers();
                _showSetup          = false;
                _setupBuffersLoaded = false;
            }
        }
    }

    private void DrawChat()
    {
        DrawSessionTabs();
        DrawRenameField();
        DrawHistory();
        ImGui.Separator();
        DrawInput();
    }

    private void DrawSessionTabs()
    {
        if (!ImGui.BeginTabBar("##sessions")) return;

        for (int i = 0; i < _chats.Sessions.Count; i++)
        {
            var session = _chats.Sessions[i];
            bool tabOpen = true;

            if (ImGui.BeginTabItem($"{session.Name}##tab{session.Id}", ref tabOpen))
            {
                _chats.CurrentIndex = i;
                ImGui.EndTabItem();
            }

            if (!tabOpen)
            {
                _chats.CurrentIndex = i;
                _chats.DeleteCurrent();
            }
        }

        if (ImGui.TabItemButton("+"))
            _chats.CreateNew();

        ImGui.EndTabBar();
    }

    private void DrawRenameField()
    {
        var session = _chats.Current;

        if (session.Id != _lastSessId)
        {
            Array.Clear(_nameBuf, 0, _nameBuf.Length);
            var b = Encoding.UTF8.GetBytes(session.Name);
            Array.Copy(b, _nameBuf, Math.Min(b.Length, _nameBuf.Length - 1));
            _lastSessId = session.Id;
        }

        // Строка 1: имя чата на всю ширину
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##chatname", _nameBuf, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            var name = Encoding.UTF8.GetString(_nameBuf).TrimEnd('\0').Trim();
            session.Name = string.IsNullOrEmpty(name) ? "New chat" : name;
            _lastSessId  = "";
            _chats.SaveCurrent();
        }

        // Строка 2: кнопки
        if (ImGui.Button("Settings", new float2(80, 0)))
        {
            _showSetup          = true;
            _setupBuffersLoaded = false;
        }

        ImGui.SameLine();
        if (_benchmarkRunning)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.6f, 0.2f, 0.2f, 1.0f));
            if (ImGui.Button("Stop BM", new float2(80, 0)))
            {
                _benchmarkRunning = false;
                _cts?.Cancel();
                AdvisorMod.Log("Benchmark stopped by user");
            }
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.2f, 0.4f, 0.6f, 1.0f));
            if (ImGui.Button("Benchmark", new float2(90, 0)))
                StartBenchmark();
            ImGui.PopStyleColor();
        }
    }

    private void DrawHistory()
    {
        var childH = ImGui.GetContentRegionAvail().Y - 52;

        ImGui.BeginChild("##history", new float2(0, childH), ImGuiChildFlags.None);

        var messages = _chats.Current.Messages;
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role == "user")
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new ImColor8(140, 199, 255, 255));
                ImGui.TextWrapped($"You: {msg.Content}");
                ImGui.PopStyleColor();
            }
            else if (msg.Role == "assistant")
            {
                ImGui.TextWrapped($"Advisor: {msg.Content}");
            }
            ImGui.Spacing();
        }

        if (_isStreaming)
        {
            string partial;
            lock (_streamLock) partial = _streamingText;
            ImGui.PushStyleColor(ImGuiCol.Text, new ImColor8(166, 255, 166, 255));
            ImGui.TextWrapped($"Advisor: {partial}\u258b");
            ImGui.PopStyleColor();
        }

        if (_scrollToEnd)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToEnd = false;
        }

        ImGui.EndChild();
    }

    private void DrawInput()
    {
        var inputW = ImGui.GetContentRegionAvail().X - 88;
        ImGui.SetNextItemWidth(inputW);

        bool enter = ImGui.InputText("##q", _inputBuf, ImGuiInputTextFlags.EnterReturnsTrue);

        if (ImGui.IsItemFocused() && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.V))
        {
            var paste = GetClipboard();
            if (!string.IsNullOrEmpty(paste))
            {
                var cur = Encoding.UTF8.GetString(_inputBuf).TrimEnd('\0');
                var neu = Encoding.UTF8.GetBytes(cur + paste);
                Array.Clear(_inputBuf, 0, _inputBuf.Length);
                Array.Copy(neu, _inputBuf, Math.Min(neu.Length, _inputBuf.Length - 1));
            }
        }

        ImGui.SameLine();

        if (_isStreaming)
        {
            if (ImGui.Button("Stop", new float2(80, 0)))
                _cts?.Cancel();
        }
        else
        {
            if (ImGui.Button("Ask", new float2(80, 0)) || enter)
            {
                var q = Encoding.UTF8.GetString(_inputBuf).TrimEnd('\0').Trim();
                if (!string.IsNullOrEmpty(q))
                {
                    Array.Clear(_inputBuf, 0, _inputBuf.Length);
                    SendQuestion(_chats.Current, q);
                }
            }
        }
    }

    private void SendQuestion(ChatSession session, string question)
    {
        var systemPrompt = _reader.BuildStaticSystemPrompt();
        var userMsg      = _reader.BuildDynamicUserMessage(question);

        AdvisorMod.Log($"Q: {question}");

        session.Messages.Add(new Message("user", question));

        var history = session.Messages.SkipLast(1).ToList();

        _isStreaming  = true;
        _scrollToEnd  = true;
        lock (_streamLock) _streamingText = "";

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            var sb = new StringBuilder();
            try
            {
                await foreach (var chunk in _llm.StreamAsync(systemPrompt, history, userMsg, token))
                {
                    sb.Append(chunk);
                    lock (_streamLock) _streamingText = sb.ToString();
                    _scrollToEnd = true;
                }
            }
            catch (OperationCanceledException)
            {
                sb.Append(" [cancelled]");
            }
            catch (Exception ex)
            {
                sb.Append($" [error: {ex.Message}]");
            }
            finally
            {
                var response = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(response))
                    session.Messages.Add(new Message("assistant", response));
                else
                    AdvisorMod.Log("Empty response — message not added to chat");

                lock (_streamLock) _streamingText = "";
                _isStreaming = false;
                _scrollToEnd = true;
                _chats.SaveCurrent();
            }
        }, token);
    }

    private void StartBenchmark()
    {
        var path = Path.Combine(Config.ModDir, BenchmarkFile);
        if (!File.Exists(path))
        {
            AdvisorMod.Log($"Benchmark: file not found: {path}");
            _chats.Current.Messages.Add(new Message("assistant",
                $"Benchmark file not found: {path}"));
            return;
        }

        var questions = File.ReadAllLines(path, Encoding.UTF8)
            .Select(q => q.Trim())
            .Where(q => !string.IsNullOrEmpty(q) && !q.StartsWith("#"))
            .ToList();

        if (questions.Count == 0)
        {
            AdvisorMod.Log("Benchmark: no questions in file");
            return;
        }

        _benchmarkQuestions = questions;
        _benchmarkIndex     = 0;
        _benchmarkLog       = new StringBuilder();
        _benchmarkLog.AppendLine($"Benchmark — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _benchmarkLog.AppendLine(new string('=', 60));
        _benchmarkRunning   = true;

        AdvisorMod.Log($"Benchmark started: {questions.Count} questions");
        _chats.Current.Messages.Add(new Message("assistant",
            $"[BENCHMARK] Starting {questions.Count} questions..."));

        RunNextBenchmarkQuestion();
    }

    private void RunNextBenchmarkQuestion()
    {
        if (!_benchmarkRunning || _benchmarkIndex >= _benchmarkQuestions.Count)
        {
            FinishBenchmark();
            return;
        }

        var question = _benchmarkQuestions[_benchmarkIndex];
        _benchmarkIndex++;

        AdvisorMod.Log($"Benchmark Q{_benchmarkIndex}: {question}");
        _benchmarkLog.AppendLine();
        _benchmarkLog.AppendLine($"Q{_benchmarkIndex}: {question}");

        var session      = _chats.Current;
        var systemPrompt = _reader.BuildStaticSystemPrompt();
        var userMsg      = _reader.BuildDynamicUserMessage(question);

        session.Messages.Add(new Message("user", $"[BM {_benchmarkIndex}/{_benchmarkQuestions.Count}] {question}"));

        var history = session.Messages.SkipLast(1).ToList();

        _isStreaming  = true;
        _scrollToEnd  = true;
        lock (_streamLock) _streamingText = "";

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            var sb = new StringBuilder();
            try
            {
                await foreach (var chunk in _llm.StreamAsync(systemPrompt, history, userMsg, token))
                {
                    sb.Append(chunk);
                    lock (_streamLock) _streamingText = sb.ToString();
                    _scrollToEnd = true;
                }
            }
            catch (OperationCanceledException)
            {
                sb.Append(" [cancelled]");
                _benchmarkRunning = false;
            }
            catch (Exception ex)
            {
                sb.Append($" [error: {ex.Message}]");
            }
            finally
            {
                var response = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(response))
                    session.Messages.Add(new Message("assistant", response));

                _benchmarkLog.AppendLine($"A: {response}");

                lock (_streamLock) _streamingText = "";
                _isStreaming  = false;
                _scrollToEnd  = true;
                _chats.SaveCurrent();

                if (_benchmarkRunning)
                {
                    await Task.Delay(3000);
                    RunNextBenchmarkQuestion();
                }
            }
        }, token);
    }

    private void FinishBenchmark()
    {
        _benchmarkRunning = false;
        _benchmarkLog.AppendLine();
        _benchmarkLog.AppendLine(new string('=', 60));
        _benchmarkLog.AppendLine($"Done: {_benchmarkQuestions.Count} questions");

        var resultPath = Path.Combine(Config.ModDir, BenchmarkResults);
        try
        {
            File.WriteAllText(resultPath, _benchmarkLog.ToString(), Encoding.UTF8);
            AdvisorMod.Log($"Benchmark results saved to {resultPath}");
            _chats.Current.Messages.Add(new Message("assistant",
                $"[BENCHMARK] Done! Results saved to {resultPath}"));
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"Benchmark: failed to save results: {ex.Message}");
        }

        _scrollToEnd = true;
    }
}
