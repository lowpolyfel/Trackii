namespace Trackii.Models.Api;

public class ScanRequest
{
    public uint DeviceId { get; set; }

    // LOTE: exactamente 7 dígitos numéricos
    public string Lot { get; set; } = "";

    // NO. PARTE: libre (texto / números)
    public string PartNumber { get; set; } = "";

    public uint Qty { get; set; }
}

public class ScanResponse
{
    public bool Ok { get; set; }
    public bool Advanced { get; set; }
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public uint? CurrentStep { get; set; }
    public string? ExpectedLocation { get; set; }
    public uint? QtyIn { get; set; }
    public uint? PreviousQty { get; set; }
    public uint? Scrap { get; set; }
    public uint? NextStep { get; set; }
    public string? NextLocation { get; set; }
}

// Para autollenar la pantalla SIN grabar nada
public class ScanResolveResponse
{
    public bool Ok { get; set; }
    public string Reason { get; set; } = "";

    public uint ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Area { get; set; } = "";
    public string Family { get; set; } = "";
    public string Subfamily { get; set; } = "";

    public uint RouteId { get; set; }
    public int RouteVersion { get; set; }

    public uint CurrentStep { get; set; }
    public string ExpectedLocation { get; set; } = "";

    // Para habilitar cantidad:
    public uint SuggestedQty { get; set; }
    public uint MaxQty { get; set; }
}
public class ProductInfoResponseDto
{
    public string PartNumber { get; set; } = "";
    public string Family { get; set; } = "";
    public string SubFamily { get; set; } = "";
    public string Area { get; set; } = "";
}