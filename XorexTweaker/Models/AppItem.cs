namespace XorexTweaker.Models;

public class AppItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Прочее";
    public bool IsSelected { get; set; }
}

public class AppConfig { public List<AppItem> Apps { get; set; } = new(); }