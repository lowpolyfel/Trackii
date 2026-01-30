using MySql.Data.MySqlClient;
using System.Data;
using Trackii.Models.Admin.Route;

namespace Trackii.Services.Admin;

public class RouteService
{
    private readonly string _conn;

    public RouteService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")!;
    }

    // ==========================================
    // LOOKUPS (Listas para los Select)
    // ==========================================
    public List<(uint Id, string Name)> GetActiveSubfamilies()
    {
        var list = new List<(uint, string)>();
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT sf.id, CONCAT(a.name,' / ', f.name,' / ', sf.name) AS name
            FROM subfamily sf
            JOIN family f ON f.id = sf.id_family
            JOIN area a ON a.id = f.id_area
            WHERE sf.active = 1 AND f.active = 1 AND a.active = 1
            ORDER BY a.name, f.name, sf.name
        ", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32("id"), rd.GetString("name")));

        return list;
    }

    public List<(uint Id, string Name)> GetActiveLocations()
    {
        var list = new List<(uint, string)>();
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT id, name
            FROM location
            WHERE active = 1
            ORDER BY name
        ", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32("id"), rd.GetString("name")));

        return list;
    }

    // ==========================================
    // INDEX / LISTADO
    // ==========================================
    public RouteListVm GetPaged(uint? subfamilyId, string? search, bool showInactive, int page, int pageSize)
    {
        var vm = new RouteListVm
        {
            SubfamilyId = subfamilyId,
            Search = search,
            ShowInactive = showInactive,
            Page = page < 1 ? 1 : page,
            PageSize = pageSize < 1 ? 10 : pageSize
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND r.active = 1 ";
        if (subfamilyId.HasValue) where += " AND r.subfamily_id = @sf ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND (r.name LIKE @q OR r.version LIKE @q) ";

        // 1. Contar Total
        using (var countCmd = new MySqlCommand($@"SELECT COUNT(*) FROM route r {where}", cn))
        {
            if (subfamilyId.HasValue) countCmd.Parameters.AddWithValue("@sf", subfamilyId.Value);
            if (!string.IsNullOrWhiteSpace(search)) countCmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");

            vm.Total = Convert.ToInt32(countCmd.ExecuteScalar());
        }

        // 2. Calcular Paginación
        vm.TotalPages = (int)Math.Ceiling(vm.Total / (double)vm.PageSize);
        if (vm.TotalPages <= 0) vm.TotalPages = 1;
        if (vm.Page > vm.TotalPages) vm.Page = vm.TotalPages;
        var offset = (vm.Page - 1) * vm.PageSize;

        // 3. Obtener Datos
        using var cmd = new MySqlCommand($@"
            SELECT
                r.id,
                r.name,
                r.version,
                r.active,
                CONCAT(a.name,' / ', f.name,' / ', sf.name) AS subfamily,
                COALESCE(GROUP_CONCAT(l.name ORDER BY rs.step_number SEPARATOR ' -> '), '') AS steps
            FROM route r
            JOIN subfamily sf ON sf.id = r.subfamily_id
            JOIN family f ON f.id = sf.id_family
            JOIN area a ON a.id = f.id_area
            LEFT JOIN route_step rs ON rs.route_id = r.id
            LEFT JOIN location l ON l.id = rs.location_id
            {where}
            GROUP BY r.id, r.name, r.version, r.active, subfamily
            ORDER BY r.id DESC
            LIMIT @ps OFFSET @off
        ", cn);

        if (subfamilyId.HasValue) cmd.Parameters.AddWithValue("@sf", subfamilyId.Value);
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
        cmd.Parameters.AddWithValue("@ps", vm.PageSize);
        cmd.Parameters.AddWithValue("@off", offset);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new RouteRowVm
            {
                Id = rd.GetUInt32("id"),
                Name = rd.GetString("name"),
                Version = rd.GetString("version"),
                Active = rd.GetBoolean("active"),
                Subfamily = rd.GetString("subfamily"),
                StepsSummary = rd.IsDBNull("steps") ? "" : rd.GetString("steps")
            });
        }

        return vm;
    }

    // ==========================================
    // GET FOR CREATE / EDIT
    // ==========================================
    public RouteEditVm GetForCreate()
    {
        return new RouteEditVm(); // Vacío, listo para llenar
    }

    public RouteEditVm GetForEdit(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var vm = new RouteEditVm { Id = id };

        // Cargar cabecera
        using (var cmd = new MySqlCommand("SELECT id, subfamily_id, name, version FROM route WHERE id = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) throw new InvalidOperationException("Ruta no encontrada.");

            vm.SubfamilyId = rd.GetUInt32("subfamily_id");
            vm.Name = rd.GetString("name");
            vm.Version = rd.GetString("version");
        }

        // Cargar pasos
        using (var stepsCmd = new MySqlCommand(@"
            SELECT rs.step_number, rs.location_id, l.name AS location_name
            FROM route_step rs
            JOIN location l ON l.id = rs.location_id
            WHERE rs.route_id = @id
            ORDER BY rs.step_number
        ", cn))
        {
            stepsCmd.Parameters.AddWithValue("@id", id);
            using var rd2 = stepsCmd.ExecuteReader();
            while (rd2.Read())
            {
                vm.Steps.Add(new RouteStepVm
                {
                    StepNumber = (int)rd2.GetUInt32("step_number"),
                    LocationId = rd2.GetUInt32("location_id"),
                    LocationName = rd2.GetString("location_name")
                });
            }
        }

        return vm;
    }

    // ==========================================
    // ACTIVATE (Lógica Crítica)
    // ==========================================
    public void Activate(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        uint subfamilyId;
        bool targetActive;

        // 1. Obtener info de la ruta que queremos activar
        using (var cmd = new MySqlCommand("SELECT subfamily_id, active FROM route WHERE id=@id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) throw new InvalidOperationException("Ruta no encontrada.");
            subfamilyId = rd.GetUInt32("subfamily_id");
            targetActive = rd.GetBoolean("active");
        }

        if (targetActive)
        {
            tx.Commit(); // Ya estaba activa, no hacemos nada
            return;
        }

        // 2. Verificar WIP en la ruta activa ACTUAL (si existe)
        uint? currentActiveId = null;
        using (var cmd = new MySqlCommand("SELECT id FROM route WHERE subfamily_id=@sf AND active=1 LIMIT 1", cn, tx))
        {
            cmd.Parameters.AddWithValue("@sf", subfamilyId);
            var obj = cmd.ExecuteScalar();
            if (obj != null) currentActiveId = Convert.ToUInt32(obj);
        }

        if (currentActiveId.HasValue)
        {
            if (CountWipInRoute(cn, tx, currentActiveId.Value) > 0)
                throw new InvalidOperationException("No se puede cambiar la ruta activa: hay piezas (WIP) en proceso en la ruta actual.");
        }

        // 3. Desactivar TODAS las rutas de esa subfamilia
        using (var off = new MySqlCommand("UPDATE route SET active=0 WHERE subfamily_id=@sf", cn, tx))
        {
            off.Parameters.AddWithValue("@sf", subfamilyId);
            off.ExecuteNonQuery();
        }

        // 4. Activar la ruta seleccionada
        using (var on = new MySqlCommand("UPDATE route SET active=1 WHERE id=@id", cn, tx))
        {
            on.Parameters.AddWithValue("@id", id);
            on.ExecuteNonQuery();
        }

        // 5. IMPORTANTE: Actualizar el puntero en la tabla SUBFAMILY
        using (var ptr = new MySqlCommand("UPDATE subfamily SET active_route_id=@rid WHERE id=@sf", cn, tx))
        {
            ptr.Parameters.AddWithValue("@rid", id);
            ptr.Parameters.AddWithValue("@sf", subfamilyId);
            ptr.ExecuteNonQuery();
        }

        tx.Commit();
    }
    // ... dentro de RouteService.cs ...

    public void Save(RouteEditVm vm)
    {
        if (vm.SubfamilyId == 0) throw new InvalidOperationException("Selecciona una Subfamily.");
        vm.Name = (vm.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(vm.Name)) throw new InvalidOperationException("El nombre es requerido.");

        // Limpiar y ordenar steps
        var steps = vm.Steps.Where(s => s.LocationId != 0).OrderBy(s => s.StepNumber).ToList();

        // Asignar orden secuencial si viene vacío
        if (steps.Count > 0 && steps.All(s => s.StepNumber == 0))
        {
            for (int i = 0; i < steps.Count; i++) steps[i].StepNumber = i + 1;
        }

        // Deduplicar Locations consecutivos
        var finalSteps = new List<RouteStepVm>();
        var seen = new HashSet<uint>();
        foreach (var s in steps)
        {
            if (seen.Add(s.LocationId)) finalSteps.Add(s);
        }
        steps = finalSteps;

        if (steps.Count < 1) throw new InvalidOperationException("La ruta debe tener al menos 1 paso (Location).");

        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        // Validar que la Subfamily/Family/Area estén activas
        using (var chk = new MySqlCommand(@"
            SELECT COUNT(*) 
            FROM subfamily sf
            JOIN family f ON f.id = sf.id_family
            JOIN area a ON a.id = f.id_area
            WHERE sf.id=@sf AND sf.active=1 AND f.active=1 AND a.active=1
        ", cn, tx))
        {
            chk.Parameters.AddWithValue("@sf", vm.SubfamilyId);
            if (Convert.ToInt32(chk.ExecuteScalar()) == 0)
                throw new InvalidOperationException("No puedes asignar ruta: la Subfamily (o su padre) está inactiva.");
        }

        // ================= CREACION =================
        if (vm.Id == 0)
        {
            // (Mismo código de creación que ya tenías, funciona bien)
            uint? currentActiveId = null;
            using (var cmd = new MySqlCommand("SELECT id FROM route WHERE subfamily_id=@sf AND active=1 LIMIT 1", cn, tx))
            {
                cmd.Parameters.AddWithValue("@sf", vm.SubfamilyId);
                var obj = cmd.ExecuteScalar();
                if (obj != null) currentActiveId = Convert.ToUInt32(obj);
            }

            if (currentActiveId.HasValue && CountWipInRoute(cn, tx, currentActiveId.Value) > 0)
                throw new InvalidOperationException("No se puede crear nueva versión: hay WIP en la ruta activa actual.");

            var nextVersion = GetNextVersion(cn, tx, vm.SubfamilyId);
            vm.Version = nextVersion.ToString();

            using (var off = new MySqlCommand("UPDATE route SET active=0 WHERE subfamily_id=@sf", cn, tx))
            {
                off.Parameters.AddWithValue("@sf", vm.SubfamilyId);
                off.ExecuteNonQuery();
            }

            using (var ins = new MySqlCommand(@"
                INSERT INTO route (subfamily_id, name, version, active)
                VALUES (@sf, @n, @v, 1)
            ", cn, tx))
            {
                ins.Parameters.AddWithValue("@sf", vm.SubfamilyId);
                ins.Parameters.AddWithValue("@n", vm.Name);
                ins.Parameters.AddWithValue("@v", vm.Version);
                ins.ExecuteNonQuery();
                vm.Id = (uint)ins.LastInsertedId;
            }

            using (var ptr = new MySqlCommand("UPDATE subfamily SET active_route_id=@rid WHERE id=@sf", cn, tx))
            {
                ptr.Parameters.AddWithValue("@rid", vm.Id);
                ptr.Parameters.AddWithValue("@sf", vm.SubfamilyId);
                ptr.ExecuteNonQuery();
            }

            InsertSteps(cn, tx, vm.Id, steps);
        }
        // ================= EDICION (ACTUALIZADO) =================
        else
        {
            var isActive = false;
            uint oldSubfamilyId = 0;

            // Obtener estado actual antes de editar
            using (var st = new MySqlCommand("SELECT active, subfamily_id FROM route WHERE id=@id", cn, tx))
            {
                st.Parameters.AddWithValue("@id", vm.Id);
                using var rd = st.ExecuteReader();
                if (rd.Read())
                {
                    isActive = rd.GetBoolean("active");
                    oldSubfamilyId = rd.GetUInt32("subfamily_id");
                }
            }

            // Si está activa y tiene WIP, solo permitimos cambio de Nombre, NO de pasos NI de subfamilia
            if (isActive && CountWipInRoute(cn, tx, vm.Id) > 0)
            {
                if (oldSubfamilyId != vm.SubfamilyId)
                    throw new InvalidOperationException("No se puede cambiar la Subfamilia: hay WIP en proceso en esta ruta activa.");

                // Solo permitimos cambiar el nombre y pasos si son iguales (lo cual el user podría intentar pero fallará al intentar borrar pasos)
                // Para simplificar: Si hay WIP, bloqueamos cambios estructurales.
                throw new InvalidOperationException("No se puede editar una ruta activa con WIP en proceso. Desactívala primero o termina las órdenes.");
            }

            // Si cambiamos de subfamilia
            if (oldSubfamilyId != vm.SubfamilyId)
            {
                // Si esta ruta ERA la activa de la vieja subfamilia, debemos limpiar el puntero en la vieja subfamilia
                if (isActive)
                {
                    using (var cleanOld = new MySqlCommand("UPDATE subfamily SET active_route_id=NULL WHERE id=@oldSf AND active_route_id=@rid", cn, tx))
                    {
                        cleanOld.Parameters.AddWithValue("@oldSf", oldSubfamilyId);
                        cleanOld.Parameters.AddWithValue("@rid", vm.Id);
                        cleanOld.ExecuteNonQuery();
                    }
                    // Y al moverla, dejará de ser activa en la NUEVA (para no romper la nueva si ya tiene activa)
                    // O podemos decidir que se vuelva la activa de la nueva.
                    // REGLA: Al mover, pasa a ser INACTIVA en la nueva subfamilia para evitar conflictos.
                    isActive = false;

                    // Recalcular versión para la nueva subfamilia
                    var nextVersion = GetNextVersion(cn, tx, vm.SubfamilyId);
                    vm.Version = nextVersion.ToString();
                }
            }

            // Actualizar Datos (Nombre, Subfamily, Active, Version si cambió)
            using (var upd = new MySqlCommand(@"
                UPDATE route 
                SET name=@n, subfamily_id=@sf, active=@act, version=@ver
                WHERE id=@id", cn, tx))
            {
                upd.Parameters.AddWithValue("@n", vm.Name);
                upd.Parameters.AddWithValue("@sf", vm.SubfamilyId);
                upd.Parameters.AddWithValue("@act", isActive); // Forzamos false si se movió de familia
                upd.Parameters.AddWithValue("@ver", vm.Version); // Actualizamos versión si se recalculó
                upd.Parameters.AddWithValue("@id", vm.Id);
                upd.ExecuteNonQuery();
            }

            // Reemplazar pasos
            using (var del = new MySqlCommand("DELETE FROM route_step WHERE route_id=@id", cn, tx))
            {
                del.Parameters.AddWithValue("@id", vm.Id);
                del.ExecuteNonQuery();
            }
            InsertSteps(cn, tx, vm.Id, steps);
        }

        tx.Commit();
    }
    // ==========================================
    // HELPERS PRIVADOS
    // ==========================================
    private static void InsertSteps(MySqlConnection cn, MySqlTransaction tx, uint routeId, List<RouteStepVm> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            using var ins = new MySqlCommand(@"
                INSERT INTO route_step (route_id, step_number, location_id)
                VALUES (@r, @sn, @loc)
            ", cn, tx);
            ins.Parameters.AddWithValue("@r", routeId);
            ins.Parameters.AddWithValue("@sn", i + 1); // Forzamos 1,2,3...
            ins.Parameters.AddWithValue("@loc", steps[i].LocationId);
            ins.ExecuteNonQuery();
        }
    }

    private static int CountWipInRoute(MySqlConnection cn, MySqlTransaction tx, uint routeId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT COUNT(*) FROM wip_item wi
            JOIN route_step rs ON rs.id = wi.current_step_id
            WHERE rs.route_id = @rid AND wi.status IN ('ACTIVE','HOLD')
        ", cn, tx);
        cmd.Parameters.AddWithValue("@rid", routeId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetNextVersion(MySqlConnection cn, MySqlTransaction tx, uint subfamilyId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT COALESCE(MAX(CAST(version AS UNSIGNED)), 0)
            FROM route WHERE subfamily_id=@sf
        ", cn, tx);
        cmd.Parameters.AddWithValue("@sf", subfamilyId);
        return Convert.ToInt32(cmd.ExecuteScalar()) + 100;
    }
}