namespace XorexTweaker.Models;

public class AppSettings
{
    public bool DisableAnimations { get; set; } = false;
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoCreateRestorePoint { get; set; } = false;
    public bool ConfirmRiskyTweaks { get; set; } = true;
}