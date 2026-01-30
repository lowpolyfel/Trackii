using MySql.Data.MySqlClient;
using Trackii.Models.Api;

namespace Trackii.Services.Api;

public class ProductLookupService
{
    private readonly string _cs;

    public ProductLookupService(IConfiguration cfg)
    {
        _cs = cfg.GetConnectionString("TrackiiDb")!;
    }

    public ProductInfoResponseDto? GetProductInfo(string partNumber)
    {
        using var cn = new MySqlConnection(_cs);
        cn.Open();

        var cmd = cn.CreateCommand();
        // Consulta optimizada con JOINs para traer todo de un golpe
        cmd.CommandText = @"
            SELECT 
                p.part_number, 
                f.name as family, 
                s.name as subfamily, 
                a.name as area
            FROM product p
            JOIN subfamily s ON p.id_subfamily = s.id
            JOIN family f ON s.id_family = f.id
            JOIN area a ON f.id_area = a.id
            WHERE p.part_number = @pn
            AND p.active = 1
            LIMIT 1";

        cmd.Parameters.AddWithValue("@pn", partNumber);

        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            return new ProductInfoResponseDto
            {
                PartNumber = rd.GetString("part_number"),
                Family = rd.GetString("family"),
                SubFamily = rd.GetString("subfamily"),
                Area = rd.GetString("area")
            };
        }

        return null;
    }
}