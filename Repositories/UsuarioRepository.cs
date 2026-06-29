using Dapper;
using GeneradorTurnos.Data;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Repositories;

public interface IUsuarioRepository
{
    Task<Usuario?> GetByCedulaAsync(int? tenantId, string cedula);
    Task<Usuario?> GetByEmailAsync(int tenantId, string email);
    Task<Usuario?> GetGlobalByEmailAsync(string email);
    Task<bool> EmailExistsAsync(int tenantId, string email);
    Task<Usuario?> GetByTokenAsync(string token);
    Task MarcarEmailVerificadoAsync(int id);
    Task SetTokenAsync(int id, string token, DateTime expira);
    Task<Usuario?> GetByIdAsync(int id);
    Task<Usuario?> GetByIdInTenantAsync(int tenantId, int id);
    Task<bool> CedulaExistsAsync(int? tenantId, string cedula);
    Task<int> CountByTenantAsync(int tenantId);
    Task<int> CountTrabajadoresAsync(int tenantId);
    Task<int> CreateAsync(Usuario u);
    Task UpdateAsync(Usuario u);
    Task UpdatePasswordAsync(int id, string passwordHash);
    Task SetAtiendeAsync(int id, bool atiende);
    Task SetComisionAsync(int id, ComisionTipo tipo, decimal valor);
    Task SetFotoAsync(int id, string fotoUrl);
    Task<List<Usuario>> GetByTenantAsync(int tenantId);
    Task SetRolActivoAsync(int tenantId, int id, Rol rol, bool activo);
    Task DeleteAsync(int tenantId, int id);
    Task<List<Usuario>> GetByRolAsync(int tenantId, Rol rol, bool soloActivos = true);
    Task<Usuario> UpsertClienteAsync(int tenantId, string cedula, string nombre, string? telefono);
}

public class UsuarioRepository : IUsuarioRepository
{
    private readonly IDbConnectionFactory _db;
    public UsuarioRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Usuario?> GetByCedulaAsync(int? tenantId, string cedula)
    {
        using var c = _db.Create();
        if (tenantId is null)
            return await c.QuerySingleOrDefaultAsync<Usuario>(
                "SELECT * FROM usuarios WHERE tenant_id IS NULL AND cedula = @cedula", new { cedula });
        return await c.QuerySingleOrDefaultAsync<Usuario>(
            "SELECT * FROM usuarios WHERE tenant_id = @tenantId AND cedula = @cedula",
            new { tenantId, cedula });
    }

    public async Task<Usuario?> GetByEmailAsync(int tenantId, string email)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Usuario>(
            "SELECT * FROM usuarios WHERE tenant_id=@tenantId AND lower(email)=lower(@email)",
            new { tenantId, email });
    }

    public async Task<Usuario?> GetGlobalByEmailAsync(string email)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Usuario>(
            "SELECT * FROM usuarios WHERE tenant_id IS NULL AND lower(email)=lower(@email)",
            new { email });
    }

    public async Task<bool> EmailExistsAsync(int tenantId, string email)
        => await GetByEmailAsync(tenantId, email) is not null;

    public async Task<Usuario?> GetByTokenAsync(string token)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Usuario>(
            "SELECT * FROM usuarios WHERE token_verificacion=@token AND (token_expira IS NULL OR token_expira > now())",
            new { token });
    }

    public async Task MarcarEmailVerificadoAsync(int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "UPDATE usuarios SET email_verificado=TRUE, token_verificacion=NULL, token_expira=NULL WHERE id=@id",
            new { id });
    }

    public async Task SetTokenAsync(int id, string token, DateTime expira)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "UPDATE usuarios SET token_verificacion=@token, token_expira=@expira WHERE id=@id",
            new { id, token, expira });
    }

    public async Task<Usuario?> GetByIdAsync(int id)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Usuario>(
            "SELECT * FROM usuarios WHERE id = @id", new { id });
    }

    public async Task<Usuario?> GetByIdInTenantAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        return await c.QuerySingleOrDefaultAsync<Usuario>(
            "SELECT * FROM usuarios WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId });
    }

    public async Task<bool> CedulaExistsAsync(int? tenantId, string cedula)
    {
        return await GetByCedulaAsync(tenantId, cedula) is not null;
    }

    public async Task<int> CountByTenantAsync(int tenantId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM usuarios WHERE tenant_id=@tenantId",
            new { tenantId });
    }

    public async Task<int> CountTrabajadoresAsync(int tenantId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM usuarios WHERE tenant_id=@tenantId AND rol=@barbero",
            new { tenantId, barbero = (int)Rol.Barbero });
    }

    public async Task<int> CreateAsync(Usuario u)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO usuarios (tenant_id, rol, nombre, cedula, email, telefono, password_hash, activo, atiende,
                                  email_verificado, token_verificacion, token_expira)
            VALUES (@TenantId, @Rol, @Nombre, @Cedula, @Email, @Telefono, @PasswordHash, @Activo, @Atiende,
                    @EmailVerificado, @TokenVerificacion, @TokenExpira)
            RETURNING id",
            new { u.TenantId, Rol = (int)u.Rol, u.Nombre, u.Cedula, u.Email, u.Telefono, u.PasswordHash, u.Activo, u.Atiende,
                  u.EmailVerificado, u.TokenVerificacion, u.TokenExpira });
    }

    public async Task UpdateAsync(Usuario u)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(@"
            UPDATE usuarios SET nombre=@Nombre, email=@Email, telefono=@Telefono, activo=@Activo
            WHERE id=@Id AND tenant_id=@TenantId",
            new { u.Nombre, u.Email, u.Telefono, u.Activo, u.Id, u.TenantId });
    }

    public async Task UpdatePasswordAsync(int id, string passwordHash)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE usuarios SET password_hash=@passwordHash WHERE id=@id",
            new { id, passwordHash });
    }

    public async Task SetAtiendeAsync(int id, bool atiende)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE usuarios SET atiende=@atiende WHERE id=@id", new { id, atiende });
    }

    public async Task SetComisionAsync(int id, ComisionTipo tipo, decimal valor)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE usuarios SET comision_tipo=@tipo, comision_valor=@valor WHERE id=@id",
            new { id, tipo = (int)tipo, valor });
    }

    public async Task SetFotoAsync(int id, string fotoUrl)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("UPDATE usuarios SET foto_url=@fotoUrl WHERE id=@id", new { id, fotoUrl });
    }

    public async Task<List<Usuario>> GetByTenantAsync(int tenantId)
    {
        using var c = _db.Create();
        var r = await c.QueryAsync<Usuario>(
            "SELECT * FROM usuarios WHERE tenant_id=@tenantId ORDER BY rol, nombre",
            new { tenantId });
        return r.ToList();
    }

    public async Task SetRolActivoAsync(int tenantId, int id, Rol rol, bool activo)
    {
        using var c = _db.Create();
        await c.ExecuteAsync(
            "UPDATE usuarios SET rol=@rol, activo=@activo WHERE tenant_id=@tenantId AND id=@id",
            new { tenantId, id, rol = (int)rol, activo });
    }

    public async Task DeleteAsync(int tenantId, int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM usuarios WHERE tenant_id=@tenantId AND id=@id", new { tenantId, id });
    }

    public async Task<List<Usuario>> GetByRolAsync(int tenantId, Rol rol, bool soloActivos = true)
    {
        using var c = _db.Create();
        var sql = "SELECT * FROM usuarios WHERE tenant_id=@tenantId AND rol=@rol"
                  + (soloActivos ? " AND activo = TRUE" : "")
                  + " ORDER BY nombre";
        var r = await c.QueryAsync<Usuario>(sql, new { tenantId, rol = (int)rol });
        return r.ToList();
    }

    /// <summary>Busca un cliente por cédula; si no existe lo crea. Sin contraseña.
    /// Si existe, actualiza nombre/teléfono con los datos provistos.</summary>
    public async Task<Usuario> UpsertClienteAsync(int tenantId, string cedula, string nombre, string? telefono)
    {
        var existente = await GetByCedulaAsync(tenantId, cedula);
        if (existente is not null)
        {
            using var c = _db.Create();
            await c.ExecuteAsync(
                "UPDATE usuarios SET nombre=@nombre, telefono=@telefono WHERE id=@id",
                new { nombre, telefono, id = existente.Id });
            existente.Nombre = nombre;
            existente.Telefono = telefono;
            return existente;
        }

        var nuevo = new Usuario
        {
            TenantId = tenantId, Rol = Rol.Cliente, Nombre = nombre, Cedula = cedula,
            Telefono = telefono, PasswordHash = "", Activo = true
        };
        nuevo.Id = await CreateAsync(nuevo);
        return nuevo;
    }
}
