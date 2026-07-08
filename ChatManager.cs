using System.Text.Json;

namespace KSAAdvisor;

// Одно сообщение в чате
public record Message(string Role, string Content);

// Одна сессия (вкладка)
public class ChatSession
{
    public string        Id       { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string        Name     { get; set;  } = "New chat";
    public List<Message> Messages { get; set;  } = new();
}

// Управляет всеми сессиями, сохраняет/загружает с диска
public class ChatManager
{
    private readonly string _dir;

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public List<ChatSession> Sessions     { get; } = new();
    public int               CurrentIndex { get; set; } = 0;

    public ChatSession Current =>
        Sessions.Count > 0 ? Sessions[CurrentIndex] : CreateNew();

    public ChatManager()
    {
        _dir = Path.Combine(Config.ModDir, "chats");
        Directory.CreateDirectory(_dir);
        LoadAll();
        if (Sessions.Count == 0) CreateNew();
    }

    public ChatSession CreateNew()
    {
        var s = new ChatSession();
        Sessions.Add(s);
        CurrentIndex = Sessions.Count - 1;
        return s;
    }

    public void DeleteCurrent()
    {
        if (Sessions.Count <= 1)
        {
            Current.Messages.Clear();
            Current.Name = "New chat";
            return;
        }

        var path = SessionPath(Current.Id);
        if (File.Exists(path)) File.Delete(path);

        Sessions.RemoveAt(CurrentIndex);
        CurrentIndex = Math.Max(0, CurrentIndex - 1);
    }

    public void SaveCurrent()
    {
        try
        {
            File.WriteAllText(
                SessionPath(Current.Id),
                JsonSerializer.Serialize(Current, _opts));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KSAAdvisor] Save error: {ex.Message}");
        }
    }

    public void SaveAll()
    {
        foreach (var session in Sessions)
        {
            try
            {
                File.WriteAllText(
                    SessionPath(session.Id),
                    JsonSerializer.Serialize(session, _opts));
            }
            catch { }
        }
    }

    private void LoadAll()
    {
        var config = Config.Load();
        foreach (var file in Directory.GetFiles(_dir, "*.json").Order())
        {
            try
            {
                var s = JsonSerializer.Deserialize<ChatSession>(
                    File.ReadAllText(file), _opts);
                if (s == null) continue;
                // Обрезаем старые сообщения если чат вырос за лимит
                if (s.Messages.Count > config.MaxHistoryMessages)
                    s.Messages = s.Messages
                        .TakeLast(config.MaxHistoryMessages)
                        .ToList();
                Sessions.Add(s);
            }
            catch { }
        }
    }

    private string SessionPath(string id) => Path.Combine(_dir, $"{id}.json");
}
