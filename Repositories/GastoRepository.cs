using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

public interface IGastoRepository
{
    Task<List<Gasto>> GetEnRangoAsync(int tenantId, DateTime desde, DateTime hasta);
    Task<decimal> TotalEnRangoAsync(int tenantId, DateTime desde, DateTime hasta);
    Task<int> CrearAsync(Gasto g);
    Task EliminarAsync(int tenantId, int id);
}

public class GastoRepository : IGastoRepository
{
    private readonly IDbConnectionFactory _db;
    public GastoRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<Gasto>> GetEnRangoAsync(int tenantId, DateTime desde, DateTime hasta)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<Gasto>(@"
            SELECT id, tenant_id, concepto, categoria, monto,
                   fecha::timestamp AS fecha, fecha_creacion
            FROM gastos
            WHERE tenant_id=@tenantId AND fecha >= @desde AND fecha < @hasta
            ORDER BY fecha DESC, id DESC",
            new { tenantId, desde = desde.Date, hasta = hasta.Date });
        return r.ToList();
    }

    public async Task<decimal> TotalEnRangoAsync(int tenantId, DateTime desde, DateTime hasta)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<decimal>(@"
            SELECT COALESCE(SUM(monto), 0) FROM gastos
            WHERE tenant_id=@tenantId AND fecha >= @desde AND fecha < @hasta",
            new { tenantId, desde = desde.Date, hasta = hasta.Date });
    }

    public async Task<int> CrearAsync(Gasto g)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO gastos (tenant_id, concepto, categoria, monto, fecha)
            VALUES (@TenantId, @Concepto, @Categoria, @Monto, @Fecha) RETURNING id", g);
    }

    public async Task EliminarAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM gastos WHERE id=@id AND tenant_id=@tenantId", new { id, tenantId });
    }
}
