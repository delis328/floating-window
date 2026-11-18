using System.Text.Json;
using System.IO;

namespace FloatingTxtReader;

public static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatingTxtReader");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    internal static SettingsWriter Writer { get; } = new(json => WriteSnapshot(FilePath, json));

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

    public static Task SaveAsync(AppState state) => Writer.SaveAsync(state);

    public static void Save(AppState state) => SaveAsync(state).GetAwaiter().GetResult();

    internal static void WriteSnapshot(string filePath, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, filePath, true);
    }
}
