namespace SchulerPark.Core.Settings;

public class AzureAdSettings
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool IsConfigured => !string.IsNullOrEmpty(TenantId) && !string.IsNullOrEmpty(ClientId);
}
