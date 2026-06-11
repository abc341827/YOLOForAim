using System.Text.Json;

namespace YOLOForAim;

/// <summary>
/// 负责 ui-settings.json 的持久化，避免窗体直接处理文件 IO 和 JSON 序列化。
/// </summary>
internal static class UiSettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "ui-settings.json");

    public static bool TryLoad(out UiSettings? settings)
    {
        settings = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            string json = File.ReadAllText(FilePath);
            settings = JsonSerializer.Deserialize<UiSettings>(json);
            return settings is not null;
        }
        catch
        {
            settings = null;
            return false;
        }
    }

    public static void Save(UiSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, WriteOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
        }
    }
}
