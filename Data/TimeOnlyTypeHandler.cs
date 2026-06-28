using System.Data;
using Dapper;

namespace GeneradorTurnos.Data;

/// <summary>
/// Permite usar <see cref="TimeOnly"/> como parámetro/resultado en Dapper.
/// Npgsql mapea TimeOnly &lt;-&gt; columna `time`, pero Dapper necesita este handler para parámetros.
/// </summary>
public class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
        => parameter.Value = value; // Npgsql infiere el tipo `time`

    public override TimeOnly Parse(object value) => value switch
    {
        TimeOnly t => t,
        TimeSpan ts => TimeOnly.FromTimeSpan(ts),
        DateTime dt => TimeOnly.FromDateTime(dt),
        _ => TimeOnly.Parse(value.ToString()!)
    };
}
