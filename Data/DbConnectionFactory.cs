using System.Data;
using Npgsql;

namespace GeneradorTurnos.Data;

public interface IDbConnectionFactory
{
    IDbConnection Create();
    Task<NpgsqlConnection> CreateOpenAsync();
}

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration config)
    {
        _connectionString = ConnectionStringResolver.GetDefaultConnectionString(config);
    }

    public IDbConnection Create() => new NpgsqlConnection(_connectionString);

    public async Task<NpgsqlConnection> CreateOpenAsync()
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }
}

public static class ConnectionStringResolver
{
    public static string GetDefaultConnectionString(IConfiguration config)
        => Resolve(config, "DefaultConnection", "DB_URL", "DATABASE_URL");

    public static string GetAdminConnectionString(IConfiguration config)
        => Resolve(config, "AdminConnection", "ADMIN_DB_URL", "DATABASE_ADMIN_URL");

    private static string Resolve(IConfiguration config, string name, params string[] envNames)
    {
        foreach (var env in envNames)
        {
            var value = config[env] ?? Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(value)) return Normalize(value);
        }

        var configured = config.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(configured)) return Normalize(configured);

        throw new InvalidOperationException(
            $"Falta la cadena de conexion '{name}'. Configura {string.Join(" o ", envNames)} en variables de entorno.");
    }

    private static string Normalize(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            return value;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? ""),
            Password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? ""),
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }
}
