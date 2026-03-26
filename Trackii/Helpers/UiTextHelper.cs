namespace Trackii.Helpers;

public static class UiTextHelper
{
    public static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "-";

        return status.Trim().ToUpperInvariant() switch
        {
            "OPEN" => "Activa",
            "IN_PROGRESS" => "En progreso",
            "CANCELLED" => "Cancelada",
            "FINISHED" => "Finalizada",
            "ACTIVE" => "Activa",
            "INACTIVE" => "Inactiva",
            "HOLD" => "En espera",
            "SCRAPPED" => "Desechada",
            _ => status.Replace('_', ' ').ToLowerInvariant()
        };
    }
}
