using System.Text.Json;

namespace HP.Wand.Gesture;

public class GestureStore
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly string _directory;

    public GestureStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public List<GestureTemplate> Load()
    {
        var templates = new List<GestureTemplate>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var list = JsonSerializer.Deserialize<List<GestureTemplate>>(json, _json);
                if (list != null) templates.AddRange(list);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: could not load {file}: {ex.Message}");
            }
        }
        return templates;
    }

    /// <summary>
    /// Appends <paramref name="template"/> to the gesture file for its name.
    /// Multiple recordings per name are stored in the same file as a JSON array.
    /// </summary>
    public void Save(GestureTemplate template)
    {
        string path = Path.Combine(_directory, $"{template.Name}.json");

        var existing = new List<GestureTemplate>();
        if (File.Exists(path))
        {
            try
            {
                var raw = File.ReadAllText(path);
                existing = JsonSerializer.Deserialize<List<GestureTemplate>>(raw, _json) ?? [];
            }
            catch { /* start fresh if file is corrupt */ }
        }

        existing.Add(template);
        File.WriteAllText(path, JsonSerializer.Serialize(existing, _json));
    }

    public IEnumerable<string> KnownNames() =>
        Directory.EnumerateFiles(_directory, "*.json")
                 .Select(f => Path.GetFileNameWithoutExtension(f));
}
