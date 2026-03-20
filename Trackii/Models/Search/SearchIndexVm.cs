namespace Trackii.Models.Search;

public class SearchIndexVm
{
    public string Query { get; set; } = string.Empty;
    public List<SearchResultVm> Results { get; set; } = new();
}

public class SearchResultVm
{
    public string Type { get; set; } = string.Empty;
    public uint ProductId { get; set; }
    public uint? WorkOrderId { get; set; }
    public string PrimaryValue { get; set; } = string.Empty;
    public string SecondaryValue { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CurrentLocation { get; set; }
}
