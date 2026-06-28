using Dapper;
using GeneradorTurnos.Models;
using Npgsql;

namespace GeneradorTurnos.Data;

/// <summary>
/// Crea la base de datos (si no existe), aplica el esquema y siembra datos demo.
/// Se ejecuta una sola vez en el arranque (ver Program.cs).
/// </summary>
public class DatabaseInitializer
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IConfiguration config, IWebHostEnvironment env, ILogger<DatabaseInitializer> logger)
    {
        _config = config;
        _env = env;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        // Dapper: mapear columnas snake_case -> propiedades PascalCase.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        // Dapper: soporte de TimeOnly como parámetro (horarios).
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());

        if (!_config.GetValue("App:AutoMigrate", true))
        {
            _logger.LogInformation("AutoMigrate desactivado; se omite inicialización de BD.");
            return;
        }

        try
        {
            await EnsureDatabaseAsync();
            await ApplySchemaAsync();

            if (_config.GetValue("App:SeedDemoData", true))
                await SeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudo inicializar la base de datos. Revisa la contraseña de PostgreSQL en appsettings.json " +
                "(ConnectionStrings) o ejecuta setup-db.ps1. La app seguirá arrancando.");
        }
    }

    private async Task EnsureDatabaseAsync()
    {
        var dbName = _config.GetValue<string>("App:DatabaseName") ?? "generadorturnos";
        var adminConn = _config.GetConnectionString("AdminConnection")
            ?? throw new InvalidOperationException("Falta 'AdminConnection'.");

        await using var conn = new NpgsqlConnection(adminConn);
        await conn.OpenAsync();

        var exists = await conn.ExecuteScalarAsync<int?>(
            "SELECT 1 FROM pg_database WHERE datname = @db", new { db = dbName });

        if (exists is null)
        {
            // CREATE DATABASE no admite parámetros ni transacción.
            await conn.ExecuteAsync($"CREATE DATABASE \"{dbName}\"");
            _logger.LogInformation("Base de datos '{Db}' creada.", dbName);
        }
    }

    private async Task ApplySchemaAsync()
    {
        var path = Path.Combine(_env.ContentRootPath, "Data", "schema.sql");
        var sql = await File.ReadAllTextAsync(path);

        var connStr = _config.GetConnectionString("DefaultConnection")!;
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql);
        _logger.LogInformation("Esquema aplicado correctamente.");
    }

    private async Task SeedAsync()
    {
        var connStr = _config.GetConnectionString("DefaultConnection")!;
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var hayTenants = await conn.ExecuteScalarAsync<int?>("SELECT 1 FROM tenants LIMIT 1");
        if (hayTenants is not null)
        {
            _logger.LogInformation("Ya existen datos; se omite el seed.");
            return;
        }

        string Hash(string p) => BCrypt.Net.BCrypt.HashPassword(p);

        // --- SuperAdmin global ---
        await conn.ExecuteAsync(@"
            INSERT INTO usuarios (tenant_id, rol, nombre, cedula, email, password_hash)
            VALUES (NULL, @rol, 'Super Administrador', '0000', 'admin@saas.com', @hash)",
            new { rol = (int)Rol.SuperAdmin, hash = Hash("admin123") });

        // --- Empresa demo: Barbería Centro ---
        var tenantId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO tenants (nombre, slug, plan) VALUES (@n, @s, 'Pro') RETURNING id",
            new { n = "Barbería Centro", s = "barberia-centro" });

        // Dueño
        await conn.ExecuteAsync(@"
            INSERT INTO usuarios (tenant_id, rol, nombre, cedula, telefono, email, password_hash)
            VALUES (@t, @rol, 'Roberto Pérez', '1001', '3001112233', 'dueno@barberia.com', @hash)",
            new { t = tenantId, rol = (int)Rol.Dueno, hash = Hash("dueno123") });

        // Profesionales (barberos)
        var carlos = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO usuarios (tenant_id, rol, nombre, cedula, telefono, password_hash)
            VALUES (@t, @rol, 'Carlos Gómez', '2001', '3009998877', @hash) RETURNING id",
            new { t = tenantId, rol = (int)Rol.Barbero, hash = Hash("barbero123") });

        var andres = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO usuarios (tenant_id, rol, nombre, cedula, telefono, password_hash)
            VALUES (@t, @rol, 'Andrés Ríos', '2002', '3007776655', @hash) RETURNING id",
            new { t = tenantId, rol = (int)Rol.Barbero, hash = Hash("barbero123") });

        // Cliente demo
        await conn.ExecuteAsync(@"
            INSERT INTO usuarios (tenant_id, rol, nombre, cedula, telefono, password_hash)
            VALUES (@t, @rol, 'Juan Cliente', '3001', '3005554433', @hash)",
            new { t = tenantId, rol = (int)Rol.Cliente, hash = Hash("cliente123") });

        // Servicios
        async Task<int> Servicio(string nombre, int dur, decimal precio) =>
            await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO servicios (tenant_id, nombre, duracion_minutos, precio)
                VALUES (@t, @n, @d, @p) RETURNING id",
                new { t = tenantId, n = nombre, d = dur, p = precio });

        var corte = await Servicio("Corte clásico", 30, 20000m);
        var corteBarba = await Servicio("Corte + barba", 45, 30000m);
        var afeitado = await Servicio("Afeitado", 20, 12000m);
        var tinte = await Servicio("Tinte", 60, 50000m);

        // Asignar servicios a profesionales
        async Task Asignar(int emp, params int[] servs)
        {
            foreach (var s in servs)
                await conn.ExecuteAsync(@"
                    INSERT INTO empleado_servicios (tenant_id, empleado_id, servicio_id)
                    VALUES (@t, @e, @s)", new { t = tenantId, e = emp, s });
        }
        await Asignar(carlos, corte, corteBarba, afeitado);
        await Asignar(andres, corte, corteBarba, tinte);

        // Horarios: lunes(1) a sábado(6), 9:00-18:00
        async Task Horario(int emp)
        {
            for (int dia = 1; dia <= 6; dia++)
                await conn.ExecuteAsync(@"
                    INSERT INTO horarios_trabajo (tenant_id, empleado_id, dia_semana, hora_inicio, hora_fin)
                    VALUES (@t, @e, @d, TIME '09:00', TIME '18:00')",
                    new { t = tenantId, e = emp, d = dia });
        }
        await Horario(carlos);
        await Horario(andres);

        _logger.LogInformation("Datos demo sembrados. Empresa: barberia-centro");
    }
}
