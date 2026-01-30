namespace Trackii.Models.ViewCatalog;

public class ViewCatalogVm
{
    public List<TableEntry> Tables { get; set; } = new();

    public class TableEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Rows { get; set; }
    }
}
