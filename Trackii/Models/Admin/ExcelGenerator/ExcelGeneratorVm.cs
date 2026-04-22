namespace Trackii.Models.Admin.ExcelGenerator;

public class ExcelGeneratorVm
{
    public int TotalRows { get; set; }
    public int MaxSteps { get; set; }
    public int StaleOrdersTotalRows { get; set; }
    public List<string> Headers { get; set; } = new();
    public List<ExcelGeneratorRowVm> PreviewRows { get; set; } = new();
}

public class ExcelGeneratorRowVm
{
    public string Product { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public List<string> RouteSteps { get; set; } = new();
}
