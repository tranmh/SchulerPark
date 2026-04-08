namespace SchulerPark.Core.Settings;

public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@schulerpark.local";
    public string FromName { get; set; } = "SchulerPark";
    public bool IsConfigured => !string.IsNullOrEmpty(Host);
}
