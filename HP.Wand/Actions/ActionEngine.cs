using System.Text.Json;
using HP.Wand.GPIO;

namespace HP.Wand.Actions;

public class ActionEngine
{
    private readonly GpioService _gpio;
    private List<SpellDefinition> _spells = [];

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ActionEngine(GpioService gpio) => _gpio = gpio;

    public void LoadSpells(string spellsJsonPath)
    {
        if (!File.Exists(spellsJsonPath))
        {
            Console.Error.WriteLine($"spells.json not found at {spellsJsonPath}");
            return;
        }

        try
        {
            var json = File.ReadAllText(spellsJsonPath);
            var doc = JsonSerializer.Deserialize<SpellsFile>(json, _json);
            _spells = doc?.Spells ?? [];
            Console.WriteLine($"Loaded {_spells.Count} spell(s).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load spells: {ex.Message}");
        }
    }

    public bool ExecuteSpell(string gestureName)
    {
        var spell = _spells.FirstOrDefault(s =>
            string.Equals(s.Gesture, gestureName, StringComparison.OrdinalIgnoreCase));

        if (spell == null) return false;

        Console.WriteLine($"Casting: {spell.Name}");
        Task.Run(() => RunSteps(spell.Steps));
        return true;
    }

    private void RunSteps(ActionStep[] steps)
    {
        foreach (var step in steps)
        {
            switch (step.Type.ToLowerInvariant())
            {
                case "digital":
                    _gpio.SetDigital(step.Pin, step.Value);
                    break;

                case "pwm":
                    _gpio.SetPwm(step.Pin, step.Value);
                    break;

                case "delay":
                    Thread.Sleep(step.Ms);
                    break;

                case "sound":
                    _gpio.PlaySound(step.File);
                    break;

                default:
                    Console.Error.WriteLine($"Unknown action type: {step.Type}");
                    break;
            }
        }
    }

    public IReadOnlyList<SpellDefinition> Spells => _spells;

    // ── Deserialisation wrapper ───────────────────────────────────────────────

    private class SpellsFile
    {
        public List<SpellDefinition> Spells { get; set; } = [];
    }
}
