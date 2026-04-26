using HP.Wand.Actions;
using HP.Wand.Camera;
using HP.Wand.Gesture;
using HP.Wand.GPIO;

// ── Paths ─────────────────────────────────────────────────────────────────────
string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
string gestureDir = Path.Combine(dataDir, "gestures");
string spellsPath = Path.Combine(dataDir, "spells.json");

// ── Services ──────────────────────────────────────────────────────────────────
var store = new GestureStore(gestureDir);
var recognizer = new GestureRecognizer();
var gpio = new GpioService(simulation: DetectSimulation());
var engine = new ActionEngine(gpio);

engine.LoadSpells(spellsPath);

// ── CLI dispatch ──────────────────────────────────────────────────────────────
string command = args.Length > 0 ? args[0].ToLowerInvariant() : "run";
string? param = args.Length > 1 ? args[1] : null;

switch (command)
{
    case "run":    await RunMode();    break;
    case "learn":  await LearnMode(param); break;
    case "list":   ListMode();        break;
    case "test":   TestMode(param);   break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Usage: run | learn <name> | list | test <name>");
        Environment.Exit(1);
        break;
}

gpio.Dispose();

// ── run mode ──────────────────────────────────────────────────────────────────
async Task RunMode()
{
    LoadTemplates();
    Console.WriteLine("HP Wand running. Cast a spell! (Ctrl+C to quit)");

    using var tracker = new CameraTracker();
    tracker.OnGestureEnd += points =>
    {
        var (name, score) = recognizer.Recognize(points);
        if (name != null)
        {
            Console.WriteLine($"Recognized: {name} (score={score:F2})");
            if (!engine.ExecuteSpell(name))
                Console.WriteLine($"  No spell mapped to gesture '{name}'.");
        }
        else
        {
            Console.WriteLine($"Gesture not recognized (best score={score:F2}).");
        }
    };

    tracker.Start();

    var tcs = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
    await tcs.Task;

    tracker.Stop();
    Console.WriteLine("Stopped.");
}

// ── learn mode ────────────────────────────────────────────────────────────────
async Task LearnMode(string? name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        Console.Error.WriteLine("Usage: learn <gesture-name>");
        Environment.Exit(1);
    }

    bool recording = true;
    while (recording)
    {
        Console.WriteLine($"\nPreparing to record '{name}'... Get ready!");
        for (int i = 3; i >= 1; i--)
        {
            Console.WriteLine($"  {i}...");
            await Task.Delay(1000);
        }
        Console.WriteLine("  GO! Cast the gesture now.");

        GesturePoint[]? captured = null;
        var cts = new CancellationTokenSource();

        using var tracker = new CameraTracker();
        tracker.OnGestureEnd += points =>
        {
            captured = points;
            cts.Cancel();
        };

        try { tracker.Start(); } catch (Exception ex)
        {
            Console.Error.WriteLine($"Camera error: {ex.Message}");
            break;
        }

        try { await Task.Delay(10_000, cts.Token); } catch (OperationCanceledException) { }
        tracker.Stop();

        if (captured == null || captured.Length < 2)
        {
            Console.WriteLine("No gesture detected. Try again? (y/n)");
        }
        else
        {
            var template = new GestureTemplate { Name = name!, Points = captured };
            store.Save(template);
            Console.WriteLine($"Saved template for '{name}' ({captured.Length} points).");
        }

        Console.Write("Record another sample for the same gesture? (y/n): ");
        recording = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";
    }

    Console.WriteLine("Done learning.");
}

// ── list mode ─────────────────────────────────────────────────────────────────
void ListMode()
{
    Console.WriteLine("=== Gestures ===");
    foreach (var n in store.KnownNames())
        Console.WriteLine($"  {n}");

    Console.WriteLine("\n=== Spells ===");
    foreach (var s in engine.Spells)
        Console.WriteLine($"  {s.Name}  (gesture: {s.Gesture}, steps: {s.Steps.Length})");
}

// ── test mode ─────────────────────────────────────────────────────────────────
void TestMode(string? name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        Console.Error.WriteLine("Usage: test <spell-name-or-gesture-name>");
        Environment.Exit(1);
    }

    // Try gesture name first, then spell name
    if (!engine.ExecuteSpell(name!))
    {
        var spell = engine.Spells.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (spell != null)
            engine.ExecuteSpell(spell.Gesture);
        else
            Console.Error.WriteLine($"No spell found for '{name}'.");
    }

    Thread.Sleep(500); // give background thread time to start
    Console.WriteLine("Test complete. Press Enter to exit.");
    Console.ReadLine();
}

// ── helpers ───────────────────────────────────────────────────────────────────
void LoadTemplates()
{
    var templates = store.Load();
    recognizer.SetTemplates(templates);
    Console.WriteLine($"Loaded {templates.Count} gesture template(s).");
}

bool DetectSimulation()
{
    // On non-Linux systems or when /dev/gpiomem doesn't exist, run in sim mode
    if (!OperatingSystem.IsLinux()) return true;
    if (!File.Exists("/dev/gpiomem") && !File.Exists("/dev/gpiochip0")) return true;
    return false;
}
