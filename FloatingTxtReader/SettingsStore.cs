using System.Text.Json;
using System.IO;

namespace FloatingTxtReader;

public static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatingTxtReader");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static AppState Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new AppState();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, FilePath, true);
    }
}
