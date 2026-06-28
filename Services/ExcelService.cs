using ClosedXML.Excel;
using GeneradorTurnos.Models;

namespace GeneradorTurnos.Services;

/// <summary>Exporta turnos a un archivo Excel (.xlsx) con ClosedXML.</summary>
public class ExcelService
{
    public byte[] ExportarTurnos(string nombreEmpresa, DateTime desde, DateTime hasta, IEnumerable<TurnoDetalle> turnos)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Turnos");

        ws.Cell(1, 1).Value = $"Reporte de turnos - {nombreEmpresa}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Período: {desde:yyyy-MM-dd} a {hasta.AddDays(-1):yyyy-MM-dd}";

        var headers = new[] { "Fecha", "Inicio", "Fin", "Cliente", "Cédula", "Teléfono", "Profesional", "Servicio", "Estado", "Precio" };
        int hr = 4;
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(hr, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6efd");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = hr + 1;
        foreach (var t in turnos)
        {
            ws.Cell(row, 1).Value = t.FechaHoraInicio.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = t.FechaHoraInicio.ToString("HH:mm");
            ws.Cell(row, 3).Value = t.FechaHoraFin.ToString("HH:mm");
            ws.Cell(row, 4).Value = t.ClienteNombre;
            ws.Cell(row, 5).Value = t.ClienteCedula;
            ws.Cell(row, 6).Value = t.ClienteTelefono ?? "";
            ws.Cell(row, 7).Value = t.EmpleadoNombre;
            ws.Cell(row, 8).Value = t.ServicioNombre;
            ws.Cell(row, 9).Value = t.Estado.Display();
            ws.Cell(row, 10).Value = t.Precio;
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
            row++;
        }

        // Total de ingresos (solo completados)
        var total = turnos.Where(t => t.Estado == EstadoTurno.Completado).Sum(t => t.Precio);
        ws.Cell(row + 1, 9).Value = "Total ingresos:";
        ws.Cell(row + 1, 9).Style.Font.Bold = true;
        ws.Cell(row + 1, 10).Value = total;
        ws.Cell(row + 1, 10).Style.Font.Bold = true;
        ws.Cell(row + 1, 10).Style.NumberFormat.Format = "#,##0";

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
