namespace TiktokStreakSaver.Models;

[Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
public class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Account";
    public string UserAgent { get; set; } = string.Empty;
    public bool SessionValid { get; set; }
    public DateTime? LastSnapshot { get; set; }
}
