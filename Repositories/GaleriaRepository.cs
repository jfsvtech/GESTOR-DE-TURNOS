using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

public interface IGaleriaRepository
{
    Task<List<GaleriaFoto>> GetByTenantAsync(int tenantId, int limite = 30);
    Task<List<GaleriaFoto>> GetByEmpleadoAsync(int tenantId, int empleadoId, int limite = 30);
    Task<int> CrearAsync(GaleriaFoto f);
    Task EliminarAsync(int tenantId, int id);
}

public class GaleriaRepository : IGaleriaRepository
{
    private readonly IDbConnectionFactory _db;
    public GaleriaRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<GaleriaFoto>> GetByTenantAsync(int tenantId, int limite = 30)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<GaleriaFoto>(
            "SELECT * FROM galeria WHERE tenant_id=@tenantId ORDER BY fecha_creacion DESC LIMIT @limite",
            new { tenantId, limite });
        return r.ToList();
    }

    public async Task<List<GaleriaFoto>> GetByEmpleadoAsync(int tenantId, int empleadoId, int limite = 30)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<GaleriaFoto>(
            "SELECT * FROM galeria WHERE tenant_id=@tenantId AND empleado_id=@empleadoId ORDER BY fecha_creacion DESC LIMIT @limite",
            new { tenantId, empleadoId, limite });
        return r.ToList();
    }

    public async Task<int> CrearAsync(GaleriaFoto f)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO galeria (tenant_id, empleado_id, foto_url, descripcion)
            VALUES (@TenantId, @EmpleadoId, @FotoUrl, @Descripcion) RETURNING id", f);
    }

    public async Task EliminarAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM galeria WHERE id=@id AND tenant_id=@tenantId", new { id, tenantId });
    }
}
