namespace HP.Wand.Actions;

public class SpellDefinition
{
    public string Name { get; set; } = "";
    public string Gesture { get; set; } = "";
    public ActionStep[] Steps { get; set; } = [];
}
