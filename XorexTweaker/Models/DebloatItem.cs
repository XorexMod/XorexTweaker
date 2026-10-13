namespace XorexTweaker.Models;

public class DebloatItem
{
    public string PackageName { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsSelected { get; set; }
}

public class DebloatConfig { public List<DebloatItem> Packages { get; set; } = new(); }