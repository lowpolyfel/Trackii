namespace Trackii.Models.Api;

public class RegisterRequestDto
{
    public string Token { get; set; } = "";
    public string DeviceUid { get; set; } = "";
    public string? DeviceName { get; set; }
    public string Password { get; set; } = "";

    // AGREGE ESTO: Para recibir la ubicación seleccionada
    public uint LocationId { get; set; }
}

public class RegisterResponseDto
{
    public uint UserId { get; set; }
    public uint DeviceId { get; set; }
    public string Username { get; set; } = "";
    public string Jwt { get; set; } = "";
}