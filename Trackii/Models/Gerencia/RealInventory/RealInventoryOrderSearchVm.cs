namespace Trackii.Models.Gerencia.RealInventory;

public class RealInventoryOrderSearchVm
{
    public const int DefaultPageSize = 20;

    public string? WoNumber { get; set; }
    public string? Product { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
    public int TotalResults { get; set; }

    public List<RealInventoryOrderSearchRowVm> Results { get; } = new();

    public int TotalPages => TotalResults <= 0 ? 1 : (int)Math.Ceiling((double)TotalResults / PageSize);
    public bool HasFilters => !string.IsNullOrWhiteSpace(WoNumber) || !string.IsNullOrWhiteSpace(Product);
}

public class RealInventoryOrderSearchRowVm
{
    public uint WorkOrderId { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public string WoStatus { get; set; } = string.Empty;
    public string? WipStatus { get; set; }
    public string? CurrentLocation { get; set; }
}
