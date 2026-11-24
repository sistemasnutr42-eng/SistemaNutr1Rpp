using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SistemaNutri.Controllers
{
    public partial class CoordinacionController
    {
        private static LecturaProgramacionExcel LeerProgramacionExcel(byte[] contenido, string tipoPedidoCodigo, string periodicidad, DateTime fechaInicio, DateTime fechaFin)
        {
            if (contenido == null || contenido.Length == 0)
                throw new InvalidDataException("El archivo de programación está vacío.");

            tipoPedidoCodigo = NormalizarClaveExcel(tipoPedidoCodigo);
            periodicidad = NormalizarPeriodicidad(periodicidad);
            fechaInicio = fechaInicio.Date;
            fechaFin = fechaFin.Date;

            if (string.IsNullOrWhiteSpace(tipoPedidoCodigo))
                throw new InvalidDataException("Debes indicar el tipo de programación.");

            if (tipoPedidoCodigo is not ("DOSIS" or "HEMBRAS" or "SEMENTALES" or "RASTRO"))
                throw new InvalidDataException($"El tipo de programación \"{tipoPedidoCodigo}\" no está configurado.");

            if (string.IsNullOrWhiteSpace(periodicidad))
                throw new InvalidDataException("La periodicidad debe ser SEMANAL, MENSUAL o SEMESTRAL.");

            if (fechaFin < fechaInicio)
                throw new InvalidDataException("La fecha final no puede ser anterior a la fecha inicial.");

            using var memoria = new MemoryStream(contenido, false);
            using var workbook = new XLWorkbook(memoria);

            if (!workbook.Worksheets.Any())
                throw new InvalidDataException("El archivo Excel no contiene hojas para analizar.");

            return tipoPedidoCodigo switch
            {
                "DOSIS" => LeerProgramacionDosisExcel(workbook, periodicidad, fechaInicio, fechaFin),
                "HEMBRAS" => LeerProgramacionHembrasExcel(workbook, periodicidad, fechaInicio, fechaFin),
                "SEMENTALES" => LeerProgramacionSementalesExcel(workbook, periodicidad, fechaInicio, fechaFin),
                "RASTRO" => LeerProgramacionRastroExcel(workbook, periodicidad, fechaInicio, fechaFin),
                _ => throw new InvalidDataException($"El tipo de programación \"{tipoPedidoCodigo}\" no está configurado.")
            };
        }

        private static bool TryLeerCantidadExcel(IXLCell cell, out decimal cantidad)
        {
            cantidad = 0m;

            if (cell == null || cell.IsEmpty())
                return false;

            if (cell.TryGetValue<double>(out var numero))
            {
                cantidad = Convert.ToDecimal(numero);
                return true;
            }

            if (cell.TryGetValue<decimal>(out var numeroDecimal))
            {
                cantidad = numeroDecimal;
                return true;
            }

            var texto = cell.GetFormattedString()?.Trim();

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            texto = texto
                .Replace("$", string.Empty)
                .Replace("\u00A0", " ")
                .Trim();

            if (decimal.TryParse(
                texto,
                NumberStyles.Any,
                CultureInfo.GetCultureInfo("es-MX"),
                out cantidad))
                return true;

            if (decimal.TryParse(
                texto,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out cantidad))
                return true;

            cantidad = 0m;
            return false;
        }

        private static bool TryLeerEnteroExcel(IXLCell cell, out int valor)
        {
            valor = 0;

            if (cell == null || cell.IsEmpty())
                return false;

            if (cell.TryGetValue<int>(out valor))
                return true;

            if (cell.TryGetValue<double>(out var numero))
            {
                if (Math.Abs(numero - Math.Round(numero)) > 0.000001d)
                    return false;

                if (numero < int.MinValue || numero > int.MaxValue)
                    return false;

                valor = Convert.ToInt32(
                    Math.Round(numero));

                return true;
            }

            var texto =
                LimpiarTextoExcel(
                    cell.GetFormattedString());

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            return
                int.TryParse(
                    texto,
                    NumberStyles.Integer,
                    CultureInfo.GetCultureInfo("es-MX"),
                    out valor)
                ||
                int.TryParse(
                    texto,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out valor);
        }

        private static bool TryLeerFechaExcel(IXLCell cell, out DateTime fecha)
        {
            fecha = default;

            if (cell == null || cell.IsEmpty())
                return false;

            if (cell.TryGetValue<DateTime>(
                out var fechaExcel))
            {
                fecha = fechaExcel.Date;
                return true;
            }

            if (cell.TryGetValue<double>(
                out var numeroExcel))
            {
                try
                {
                    if (numeroExcel > 0 &&
                       numeroExcel < 2958466)
                    {
                        var posibleFecha =
                            DateTime.FromOADate(
                                numeroExcel);

                       
                        if (posibleFecha.Year >= 1900)
                        {
                            fecha = posibleFecha.Date;
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            var texto =
                LimpiarTextoExcel(
                    cell.GetFormattedString());

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            var formatos = new[]
            {
                "dd/MM/yyyy",
                "d/M/yyyy",
                "dd-MM-yyyy",
                "d-M-yyyy",
                "yyyy-MM-dd",
                "dd/MM/yy",
                "d/M/yy",
                "dd-MM-yy",
                "d-M-yy",
                "dd.MM.yyyy",
                "d.M.yyyy"
            };

            if (DateTime.TryParseExact(
                texto,
                formatos,
                CultureInfo.GetCultureInfo("es-MX"),
                DateTimeStyles.AllowWhiteSpaces,
                out fecha))
            {
                fecha = fecha.Date;
                return true;
            }

            if (DateTime.TryParse(
                texto,
                CultureInfo.GetCultureInfo("es-MX"),
                DateTimeStyles.AllowWhiteSpaces,
                out fecha))
            {
                fecha = fecha.Date;
                return true;
            }

            if (DateTime.TryParse(
                texto,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out fecha))
            {
                fecha = fecha.Date;
                return true;
            }

            fecha = default;
            return false;
        }

        private static bool TryLeerHoraExcel(IXLCell cell, out TimeSpan hora)
        {
            hora = default;

            if (cell == null || cell.IsEmpty())
                return false;

            if (cell.TryGetValue<TimeSpan>(
                out var horaExcel))
            {
                hora = horaExcel;
                return true;
            }

            if (cell.TryGetValue<DateTime>(
                out var fechaHora))
            {
                hora = fechaHora.TimeOfDay;
                return true;
            }

            if (cell.TryGetValue<double>(
                out var numeroExcel))
            {
                var fraccion =
                    numeroExcel -
                    Math.Floor(numeroExcel);

                if (fraccion >= 0 &&
                   fraccion < 1)
                {
                    hora =
                        TimeSpan.FromDays(
                            fraccion);

                    return true;
                }
            }

            var texto =
                LimpiarTextoExcel(
                    cell.GetFormattedString());

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            var formatos = new[]
            {
                @"h\:mm",
                @"hh\:mm",
                @"h\:mm\:ss",
                @"hh\:mm\:ss"
            };

            foreach (var formato in formatos)
            {
                if (TimeSpan.TryParseExact(
                    texto,
                    formato,
                    CultureInfo.InvariantCulture,
                    out hora))
                    return true;
            }

            if (TimeSpan.TryParse(
                texto,
                CultureInfo.GetCultureInfo("es-MX"),
                out hora))
                return true;

            if (DateTime.TryParse(
                texto,
                CultureInfo.GetCultureInfo("es-MX"),
                DateTimeStyles.AllowWhiteSpaces,
                out var fechaParseada))
            {
                hora =
                    fechaParseada.TimeOfDay;

                return true;
            }

            hora = default;
            return false;
        }

        private static string? ObtenerTextoCeldaExcel(
            IXLCell cell)
        {
            if (cell == null || cell.IsEmpty())
                return null;

            return LimpiarTextoExcel(
                cell.GetFormattedString());
        }

        private static string? LimpiarTextoExcel(
            string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return null;

            texto =
                texto.Replace(
                    "\u00A0",
                    " ");

            return Regex.Replace(
                texto.Trim(),
                @"\s+",
                " ");
        }

        private static string NormalizarClaveExcel(
            string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            var normalizado =
                texto
                    .Trim()
                    .ToUpperInvariant()
                    .Normalize(
                        NormalizationForm.FormD);

            var caracteres =
                normalizado
                    .Where(
                        c => CharUnicodeInfo
                            .GetUnicodeCategory(c)
                            !=
                            UnicodeCategory.NonSpacingMark)
                    .ToArray();

            return Regex.Replace(
                new string(caracteres)
                    .Normalize(
                        NormalizationForm.FormC),
                @"\s+",
                " ");
        }

        private static string NormalizarEncabezadoExcel(
            string? texto)
        {
            var normalizado =
                NormalizarClaveExcel(
                    texto);

            if (string.IsNullOrWhiteSpace(normalizado))
                return string.Empty;

            normalizado =
                normalizado
                    .Replace("#", " NUM ")
                    .Replace("N°", " NUM ")
                    .Replace("Nº", " NUM ")
                    .Replace("NO.", " NUM ")
                    .Replace("NO ", " NUM ")
                    .Replace("NÚM.", " NUM ")
                    .Replace("NUM.", " NUM ");

            normalizado =
                Regex.Replace(
                    normalizado,
                    @"[^\p{L}\p{N}\s]",
                    " ");

            return Regex.Replace(
                    normalizado,
                    @"\s+",
                    " ")
                .Trim();
        }

        private static string NormalizarPeriodicidad(
            string? periodicidad)
        {
            var codigo =
                NormalizarClaveExcel(
                    periodicidad);

            return codigo is
                "SEMANAL" or
                "MENSUAL" or
                "SEMESTRAL"
                    ? codigo
                    : string.Empty;
        }

        private static bool TextoCoincideConAlguno(
            string? valor,
            params string[] opciones)
        {
            var normalizado =
                NormalizarEncabezadoExcel(
                    valor);

            if (string.IsNullOrWhiteSpace(normalizado) ||
               opciones == null ||
               opciones.Length == 0)
                return false;

            foreach (var opcion in opciones)
            {
                var opcionNormalizada =
                    NormalizarEncabezadoExcel(
                        opcion);

                if (string.Equals(
                    normalizado,
                    opcionNormalizada,
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static int? BuscarColumnaEncabezado(
            IXLWorksheet worksheet,
            int filaEncabezado,
            params string[] nombres)
        {
            if (worksheet == null ||
               filaEncabezado <= 0 ||
               nombres == null ||
               nombres.Length == 0)
                return null;

            var ultimaColumna =
                worksheet
                    .LastColumnUsed()?
                    .ColumnNumber()
                ?? 0;

            if (ultimaColumna <= 0)
                return null;

            for (
                var columna = 1;
                columna <= ultimaColumna;
                columna++)
            {
                var encabezado =
                    ObtenerTextoCeldaExcel(
                        worksheet.Cell(
                            filaEncabezado,
                            columna));

                if (TextoCoincideConAlguno(
                    encabezado,
                    nombres))
                    return columna;
            }

            return null;
        }

        private static int? BuscarFilaEncabezados(
            IXLWorksheet worksheet,
            int maximoFilasBusqueda,
            params string[] encabezadosRequeridos)
        {
            if (worksheet == null ||
               encabezadosRequeridos == null ||
               encabezadosRequeridos.Length == 0)
                return null;

            var ultimaFila =
                worksheet
                    .LastRowUsed()?
                    .RowNumber()
                ?? 0;

            var ultimaColumna =
                worksheet
                    .LastColumnUsed()?
                    .ColumnNumber()
                ?? 0;

            if (ultimaFila <= 0 ||
               ultimaColumna <= 0)
                return null;

            var limite =
                Math.Min(
                    ultimaFila,
                    Math.Max(
                        1,
                        maximoFilasBusqueda));

            for (
                var fila = 1;
                fila <= limite;
                fila++)
            {
                var encontrados = 0;

                foreach (
                    var requerido
                    in encabezadosRequeridos)
                {
                    var requeridoNormalizado =
                        NormalizarEncabezadoExcel(
                            requerido);

                    var existe = false;

                    for (
                        var columna = 1;
                        columna <= ultimaColumna;
                        columna++)
                    {
                        var encabezado =
                            NormalizarEncabezadoExcel(
                                worksheet.Cell(
                                    fila,
                                    columna)
                                .GetFormattedString());

                        if (string.Equals(
                            encabezado,
                            requeridoNormalizado,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            existe = true;
                            break;
                        }
                    }

                    if (existe)
                        encontrados++;
                }

                if (encontrados ==
                   encabezadosRequeridos.Length)
                    return fila;
            }

            return null;
        }

        private static string FormatearFechaExcel(
            DateTime? fecha)
        {
            return fecha.HasValue
                ? fecha.Value.Date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string? FormatearHoraExcel(
            TimeSpan? hora)
        {
            return hora.HasValue
                ? hora.Value.ToString(
                    @"hh\:mm",
                    CultureInfo.InvariantCulture)
                : null;
        }

        private sealed class LecturaProgramacionExcel
        {
            public List<CeldaProgramacionExcelDto> Celdas { get; set; } = new();

            public List<string> Hojas { get; set; } = new();

            public List<string> Advertencias { get; set; } = new();
        }

        private sealed class CeldaProgramacionExcelDto
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

            public int? Semana { get; set; }

            public int? Anio { get; set; }

            public string? FechaProduccion { get; set; }

            public string? FechaEntrega { get; set; }

            public string? ColorHex { get; set; }

            public Dictionary<string, object?> DatosAdicionales { get; set; } =
                new(
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}