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
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'DefaultConnection'.");
    }

    public IDbConnection Create() => new NpgsqlConnection(_connectionString);

    public async Task<NpgsqlConnection> CreateOpenAsync()
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }
}
