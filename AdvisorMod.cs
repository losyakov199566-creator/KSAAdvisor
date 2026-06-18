using Brutal.ImGuiApi;
using StarMap.API;

namespace KSAAdvisor;

[StarMapMod]
public class AdvisorMod
{
    private AdvisorWindow? _window;

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try
        {
            var config = Config.Load();
            var reader = new GameStateReader();
            var chats  = new ChatManager();
            var llm    = new LLMClient(config);

            _window = new AdvisorWindow(chats, reader, llm, config);

            Log("Advisor ready. Press F2 to open.");
        }
        catch (Exception ex)
        {
            Log($"Initialization error: {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
    }

    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        if (_window == null) return;

        _window.Draw();

        if (ImGui.IsKeyPressed(ImGuiKey.F2))
            _window.Toggle();
    }

    [StarMapUnload]
    public void Unload()
    {
        _window?.SaveAll();
        Log("Unloaded, history saved.");
    }

    // ── Логирование ───────────────────────────────────────────────────────
    // Пишет в консоль StarMap и в файл mods\KSAAdvisor\advisor.log

    internal static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [KSAAdvisor] {message}";
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(
                Path.Combine(Config.ModDir, "advisor.log"),
                line + Environment.NewLine);
        }
        catch { }
    }
}