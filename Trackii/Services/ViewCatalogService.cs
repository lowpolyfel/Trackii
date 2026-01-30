using MySql.Data.MySqlClient;
using Trackii.Models.ViewCatalog;

namespace Trackii.Services;

public class ViewCatalogService
{
    private readonly string _conn;

    public ViewCatalogService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public ViewCatalogVm GetCatalog()
    {
        var vm = new ViewCatalogVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.Tables.Add(BuildEntry(cn, "route_step", "Pasos configurados por ruta."));
        vm.Tables.Add(BuildEntry(cn, "scan_event", "Eventos de escaneo por WIP."));
        vm.Tables.Add(BuildEntry(cn, "tokens", "Tokens disponibles para autenticación."));
        vm.Tables.Add(BuildEntry(cn, "wip_item", "Piezas en proceso (WIP)."));
        vm.Tables.Add(BuildEntry(cn, "wip_step_execution", "Ejecuciones por paso de ruta."));
        vm.Tables.Add(BuildEntry(cn, "work_order", "Órdenes de trabajo y su estado."));

        return vm;
    }

    private static ViewCatalogVm.TableEntry BuildEntry(MySqlConnection cn, string table, string description)
    {
        using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", cn);
        var rows = Convert.ToInt32(cmd.ExecuteScalar());

        return new ViewCatalogVm.TableEntry
        {
            Name = table,
            Description = description,
            Rows = rows
        };
    }
}
