using System.Text.Json;

namespace KSAAdvisor;

public class Config
{
    public string ApiKey    { get; set; } = "";
    public string BaseUrl   { get; set; } = "";
    public string Model     { get; set; } = "";
    public int    MaxTokens { get; set; } = 512;
    public int HistoryLimit { get; set; } = 10;
    public string UserSkillLevel { get; set; } = "experienced";

    public static string ModDir =>
        Path.GetDirectoryName(typeof(Config).Assembly.Location) ?? ".";

    private static string ConfigPath =>
        Path.Combine(ModDir, "config.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<Config>(
                    File.ReadAllText(ConfigPath), _opts) ?? new Config();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KSAAdvisor] Config load error: {ex.Message}");
        }
        return new Config();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ModDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, _opts));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KSAAdvisor] Config save error: {ex.Message}");
        }
    }
}