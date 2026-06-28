using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;

namespace GeneradorTurnos.Services;

/// <summary>
/// Calcula los huecos libres de un profesional para un servicio en una fecha:
/// horario laboral - bloqueos - turnos existentes, en una rejilla de 15 minutos.
/// </summary>
public class DisponibilidadService
{
    private const int GranularidadMin = 15;

    private readonly IAgendaRepository _agenda;
    private readonly ITurnoRepository _turnos;

    public DisponibilidadService(IAgendaRepository agenda, ITurnoRepository turnos)
    {
        _agenda = agenda;
        _turnos = turnos;
    }

    public async Task<List<Slot>> CalcularSlotsAsync(int tenantId, int empleadoId, int duracionMinutos, DateTime fecha)
    {
        var resultado = new List<Slot>();
        if (duracionMinutos <= 0) return resultado;

        var dia = fecha.Date;
        var diaSemana = (int)dia.DayOfWeek; // 0=Domingo .. 6=Sábado

        var horarios = (await _agenda.GetHorariosAsync(tenantId, empleadoId))
            .Where(h => h.DiaSemana == diaSemana).ToList();
        if (horarios.Count == 0) return resultado;

        var bloqueos = await _agenda.GetBloqueosEnRangoAsync(tenantId, empleadoId, dia, dia.AddDays(1));
        var ocupados = await _turnos.GetActivosDelDiaAsync(tenantId, empleadoId, dia);

        var ahora = DateTime.Now;
        var dur = TimeSpan.FromMinutes(duracionMinutos);
        var paso = TimeSpan.FromMinutes(GranularidadMin);

        foreach (var h in horarios)
        {
            var jornadaInicio = dia + h.HoraInicio.ToTimeSpan();
            var jornadaFin = dia + h.HoraFin.ToTimeSpan();

            for (var inicio = jornadaInicio; inicio + dur <= jornadaFin; inicio += paso)
            {
                var fin = inicio + dur;

                if (inicio < ahora) continue; // no permitir en el pasado
                if (bloqueos.Any(b => Solapa(inicio, fin, b.FechaHoraInicio, b.FechaHoraFin))) continue;
                if (ocupados.Any(t => Solapa(inicio, fin, t.FechaHoraInicio, t.FechaHoraFin))) continue;

                resultado.Add(new Slot { Inicio = inicio, Fin = fin });
            }
        }

        return resultado.OrderBy(s => s.Inicio).ToList();
    }

    /// <summary>Devuelve el primer hueco disponible a partir de ahora, buscando hasta <paramref name="maxDias"/> días.</summary>
    public async Task<DateTime?> ProximoSlotAsync(int tenantId, int empleadoId, int duracionMinutos, int maxDias = 21)
    {
        for (int i = 0; i <= maxDias; i++)
        {
            var dia = DateTime.Today.AddDays(i);
            var slots = await CalcularSlotsAsync(tenantId, empleadoId, duracionMinutos, dia);
            if (slots.Count > 0) return slots[0].Inicio;
        }
        return null;
    }

    private static bool Solapa(DateTime aIni, DateTime aFin, DateTime bIni, DateTime bFin)
        => aIni < bFin && aFin > bIni;
}
