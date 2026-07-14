namespace SchulerPark.Core.Settings;

public class AppSettings
{
    /// <summary>Public base URL of the app, used to build absolute links in emails.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
