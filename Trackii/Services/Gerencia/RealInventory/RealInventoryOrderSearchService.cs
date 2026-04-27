using MySql.Data.MySqlClient;
using Trackii.Models.Gerencia.RealInventory;

namespace Trackii.Services.Gerencia.RealInventory;

public class RealInventoryOrderSearchService
{
    private readonly string _conn;

    public RealInventoryOrderSearchService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb") ?? throw new InvalidOperationException("Connection string 'TrackiiDb' not found.");
    }

    public RealInventoryOrderSearchVm Search(string? query, int page)
    {
        var vm = new RealInventoryOrderSearchVm
        {
            Query = query?.Trim(),
            Page = page < 1 ? 1 : page
        };

        if (!vm.HasFilters)
        {
            return vm;
        }

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.TotalResults = GetTotalCount(cn, vm.Query);

        var offset = (vm.Page - 1) * vm.PageSize;
        using var cmd = new MySqlCommand(@"
            SELECT wo.id AS work_order_id,
                   wo.wo_number,
                   wo.status AS wo_status,
                   p.part_number,
                   COALESCE(f.name, 'Sin familia') AS family_name,
                   COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name,
                   wip.status AS wip_status,
                   l.name AS current_location
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
            LEFT JOIN family f ON f.id = sf.id_family
            LEFT JOIN (
                SELECT wi1.*
                FROM wip_item wi1
                INNER JOIN (
                    SELECT wo_order_id, MAX(id) AS max_wip_id
                    FROM wip_item
                    GROUP BY wo_order_id
                ) wi_latest ON wi_latest.max_wip_id = wi1.id
            ) wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            WHERE wo.active = 1
              AND (wo.wo_number LIKE @searchLike OR p.part_number LIKE @searchLike)
            ORDER BY wo.wo_number
            LIMIT @take OFFSET @skip", cn);

        cmd.Parameters.AddWithValue("@searchLike", $"%{vm.Query}%");
        cmd.Parameters.AddWithValue("@take", vm.PageSize);
        cmd.Parameters.AddWithValue("@skip", offset);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var workOrderIdOrdinal = rd.GetOrdinal("work_order_id");
            var wipStatusOrdinal = rd.GetOrdinal("wip_status");
            var locationOrdinal = rd.GetOrdinal("current_location");

            vm.Results.Add(new RealInventoryOrderSearchRowVm
            {
                WorkOrderId = rd.GetFieldValue<uint>(workOrderIdOrdinal),
                WoNumber = rd.GetString("wo_number"),
                Product = rd.GetString("part_number"),
                Family = rd.GetString("family_name"),
                Subfamily = rd.GetString("subfamily_name"),
                WoStatus = rd.GetString("wo_status"),
                WipStatus = rd.IsDBNull(wipStatusOrdinal) ? null : rd.GetString(wipStatusOrdinal),
                CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal)
            });
        }

        return vm;
    }

    private static int GetTotalCount(MySqlConnection cn, string? query)
    {
        using var cmd = new MySqlCommand(@"
            SELECT COUNT(DISTINCT wo.id)
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            WHERE wo.active = 1
              AND (wo.wo_number LIKE @searchLike OR p.part_number LIKE @searchLike)", cn);

        cmd.Parameters.AddWithValue("@searchLike", $"%{query}%");

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
}
