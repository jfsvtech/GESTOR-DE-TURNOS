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
            await EnsureConfiguredSuperAdminsAsync();

            if (_config.GetValue("App:SeedDemoData", false))
                _logger.LogWarning("App:SeedDemoData esta activo, pero el sembrado demo fue deshabilitado para produccion limpia.");
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
        var adminConn = ConnectionStringResolver.GetAdminConnectionString(_config);

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

        var connStr = ConnectionStringResolver.GetDefaultConnectionString(_config);
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql);
        _logger.LogInformation("Esquema aplicado correctamente.");
    }

    private async Task EnsureConfiguredSuperAdminsAsync()
    {
        var password = _config["SuperAdmin:BootstrapPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "SuperAdmin:BootstrapPassword no configurado; no se crean superadmins globales automaticamente. " +
                "Configura esta variable en Railway y reinicia para recrear jfsvtech@gmail.com y juliansernavasco@gmail.com.");
            return;
        }

        var emails = _config.GetSection("SuperAdmin:Emails").Get<string[]>()
            ?? ["jfsvtech@gmail.com", "juliansernavasco@gmail.com"];
        emails = emails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (emails.Length == 0) return;

        var connStr = ConnectionStringResolver.GetDefaultConnectionString(_config);
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        foreach (var email in emails)
        {
            var nombre = email.Equals("juliansernavasco@gmail.com", StringComparison.OrdinalIgnoreCase)
                ? "Julian Serna"
                : "JFSV Tech";
            var cedula = "super-" + new string(email.Where(char.IsLetterOrDigit).Take(32).ToArray());

            await conn.ExecuteAsync(@"
                INSERT INTO usuarios
                    (tenant_id, rol, nombre, cedula, email, password_hash, activo, email_verificado, atiende)
                SELECT NULL, @rol, @nombre, @cedula, @email, @hash, TRUE, TRUE, FALSE
                WHERE NOT EXISTS (
                    SELECT 1 FROM usuarios
                    WHERE tenant_id IS NULL AND lower(email) = lower(@email)
                );

                UPDATE usuarios
                SET rol = @rol,
                    nombre = @nombre,
                    password_hash = @hash,
                    activo = TRUE,
                    email_verificado = TRUE,
                    token_verificacion = NULL,
                    token_expira = NULL
                WHERE tenant_id IS NULL AND lower(email) = lower(@email);",
                new { rol = (int)Rol.SuperAdmin, nombre, cedula, email, hash });
        }

        _logger.LogInformation("Superadmins globales verificados: {Emails}", string.Join(", ", emails));
    }
}
