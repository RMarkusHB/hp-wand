namespace HP.Wand.Actions;

public class ActionStep
{
    public string Type { get; set; } = "";   // digital | pwm | delay | sound
    public int Pin { get; set; }
    public int Value { get; set; }
    public int Ms { get; set; }
    public string File { get; set; } = "";
}
