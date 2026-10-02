namespace Handly.WebAPI.Auth;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public required string Secret { get; set; }
    public string Issuer { get; set; } = "Handly";
    public string Audience { get; set; } = "Handly.Dashboard";
    public int ExpirationMinutes { get; set; } = 480; // 8 hours
}
