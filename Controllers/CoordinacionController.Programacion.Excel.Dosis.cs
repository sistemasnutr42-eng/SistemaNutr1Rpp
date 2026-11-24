using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SistemaNutri.Controllers
{ 
public partial class CoordinacionController
{
    private static LecturaProgramacionExcel LeerProgramacionDosisExcel(
        XLWorkbook workbook,
        string periodicidad,
        DateTime fechaInicio,
        DateTime fechaFin)
    {
        var resultado = new LecturaProgramacionExcel();
        return resultado;
    }
}

public sealed class LecturaProgramacionExcel
{
    public List<CeldaProgramacionExcelDto> Celdas { get; } = new();
    public List<string> Hojas { get; set; } = new();
    public List<string> Advertencias { get; } = new();
}

public sealed class CeldaProgramacionExcelDto
{
    public int NumeroHoja { get; set; }
    public string NombreHoja { get; set; } = string.Empty;
    public int NumeroFila { get; set; }
    public string Celda { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string? DiaProduccion { get; set; }
    public string? DiaEntrega { get; set; }
    public string? Linea { get; set; }
    public decimal Cantidad { get; set; }
    public int Semana { get; set; }
    public int Anio { get; set; }
    public string? FechaProduccion { get; set; }
    public string? FechaEntrega { get; set; }
    public string? ColorHex { get; set; }
    public Dictionary<string, object?> DatosAdicionales { get; set; } = new(StringComparer.OrdinalIgnoreCase);
   }
}