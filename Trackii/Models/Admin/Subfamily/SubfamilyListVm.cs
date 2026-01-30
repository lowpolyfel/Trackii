using System.Collections.Generic;

namespace Trackii.Models.Admin.Subfamily
{
    public class SubfamilyListVm
    {
        // Filtros actuales
        public uint? AreaId { get; set; }
        public uint? FamilyId { get; set; }
        public string? Search { get; set; }
        public bool ShowInactive { get; set; }

        // Paginación
        public int Page { get; set; }
        public int TotalPages { get; set; }

        // Listas para los Dropdowns de Filtro
        public List<(uint Id, string Name)> Areas { get; set; } = new();
        public List<(uint Id, string Name)> Families { get; set; } = new();

        public List<Row> Rows { get; set; } = new();

        public class Row
        {
            public uint Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string AreaName { get; set; } = string.Empty;
            public string FamilyName { get; set; } = string.Empty;
            public bool Active { get; set; }
            public string ActiveRouteVersion { get; set; } = "-";
        }
    }
}