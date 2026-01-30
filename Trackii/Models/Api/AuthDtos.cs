namespace Trackii.Models.Api;

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public string Token { get; set; } = "";
    public uint UserId { get; set; }
    public string Role { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
