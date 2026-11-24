using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using A = DocumentFormat.OpenXml.Drawing;

namespace SistemaNutri.Controllers
{
    internal static class SemanaProg
    {
        // Domingo anterior o igual a la fecha
        public static DateTime InicioSemana(DateTime d) =>
            d.Date.AddDays(-(int)d.DayOfWeek);

        // Semana 1 = la que contiene el 1 de enero
        public static DateTime Semana1(int anio) =>
            InicioSemana(new DateTime(anio, 1, 1));

        // El año lo define el sábado con el que termina la semana
        public static (int Anio, int N) De(DateTime ini)
        {
            var anio = ini.AddDays(6).Year;
            return (anio, (ini - Semana1(anio)).Days / 7 + 1);
        }

        // Posición de cada día dentro de la semana (domingo = 0)
        public static readonly Dictionary<string, int> Dia = new()
        {
            ["DOMINGO"] = 0, ["LUNES"] = 1, ["MARTES"] = 2, ["MIERCOLES"] = 3,
            ["JUEVES"] = 4, ["VIERNES"] = 5, ["SABADO"] = 6
        };
    }

    /// <summary>
    /// Convierte el color de relleno de una celda (RGB directo o color de tema de Excel con tinte)
    /// a un hexadecimal "#RRGGBB". Es lo que identifica la ruta del cliente.
    /// </summary>
    internal sealed class ExcelColorResolver
    {
        // Índices de tema de Excel: 0 lt1, 1 dk1, 2 lt2, 3 dk2, 4-9 accent1-6, 10 hlink, 11 folHlink
        private readonly string?[] _tema = new string?[12];

        public ExcelColorResolver(byte[] contenido)
        {
            try
            {
                using var ms = new MemoryStream(contenido, false);
                using var doc = SpreadsheetDocument.Open(ms, false);
                var cs = doc.WorkbookPart?.ThemePart?.Theme?.ThemeElements?.ColorScheme;
                if (cs == null) return;

                _tema[0] = HexTema(cs.Light1Color);
                _tema[1] = HexTema(cs.Dark1Color);
                _tema[2] = HexTema(cs.Light2Color);
                _tema[3] = HexTema(cs.Dark2Color);
                _tema[4] = HexTema(cs.Accent1Color);
                _tema[5] = HexTema(cs.Accent2Color);
                _tema[6] = HexTema(cs.Accent3Color);
                _tema[7] = HexTema(cs.Accent4Color);
                _tema[8] = HexTema(cs.Accent5Color);
                _tema[9] = HexTema(cs.Accent6Color);
                _tema[10] = HexTema(cs.Hyperlink);
                _tema[11] = HexTema(cs.FollowedHyperlinkColor);
            }
            catch
            {
                // Sin tema: sólo se resolverán los colores RGB directos
            }
        }

        private static string? HexTema(DocumentFormat.OpenXml.OpenXmlCompositeElement? c)
        {
            if (c == null) return null;

            return c.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value
                   ?? c.GetFirstChild<A.SystemColor>()?.LastColor?.Value;
        }

        private static int IndiceTema(XLThemeColor t) => t switch
        {
            XLThemeColor.Background1 => 0,
            XLThemeColor.Text1 => 1,
            XLThemeColor.Background2 => 2,
            XLThemeColor.Text2 => 3,
            XLThemeColor.Accent1 => 4,
            XLThemeColor.Accent2 => 5,
            XLThemeColor.Accent3 => 6,
            XLThemeColor.Accent4 => 7,
            XLThemeColor.Accent5 => 8,
            XLThemeColor.Accent6 => 9,
            XLThemeColor.Hyperlink => 10,
            XLThemeColor.FollowedHyperlink => 11,
            _ => -1
        };

        public string? Hex(IXLCell cell)
        {
            try
            {
                var fill = cell.Style.Fill;
                if (fill.PatternType != XLFillPatternValues.Solid) return null;

                var c = fill.BackgroundColor;

                switch (c.ColorType)
                {
                    case XLColorType.Color:
                    case XLColorType.Indexed:
                        return "#" + (c.Color.ToArgb() & 0xFFFFFF).ToString("X6");

                    case XLColorType.Theme:
                        var i = IndiceTema(c.ThemeColor);
                        if (i < 0 || _tema[i] == null) return null;
                        return AplicarTinte(_tema[i]!, c.ThemeTint);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string AplicarTinte(string hex, double tinte)
        {
            if (Math.Abs(tinte) < 0.0001) return "#" + hex.ToUpperInvariant();

            double r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255d;
            double g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255d;
            double b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255d;

            RgbAHls(r, g, b, out var h, out var l, out var s);
            l = tinte < 0 ? l * (1 + tinte) : l * (1 - tinte) + tinte;
            HlsARgb(h, Math.Clamp(l, 0, 1), s, out r, out g, out b);

            return $"#{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
        }

        private static void RgbAHls(double r, double g, double b, out double h, out double l, out double s)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;

            if (Math.Abs(max - min) < 1e-9) { h = 0; s = 0; return; }

            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        private static void HlsARgb(double h, double l, double s, out double r, out double g, out double b)
        {
            if (s == 0) { r = g = b = l; return; }

            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Canal(p, q, h + 1d / 3);
            g = Canal(p, q, h);
            b = Canal(p, q, h - 1d / 3);
        }

        private static double Canal(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1d / 6) return p + (q - p) * 6 * t;
            if (t < 1d / 2) return q;
            if (t < 2d / 3) return p + (q - p) * (2d / 3 - t) * 6;
            return p;
        }
    }

    public partial class CoordinacionController
    {
        // CAMBIO EN LeerProgramacionExcel (archivo donde está el switch por tipo):
        //   "DOSIS" => LeerProgramacionDosisExcel(workbook, contenido, periodicidad, fechaInicio, fechaFin),
        private static LecturaProgramacionExcel LeerProgramacionDosisExcel(
            XLWorkbook workbook, byte[] contenido, string periodicidad, DateTime fechaInicio, DateTime fechaFin)
        {
            var lectura = new LecturaProgramacionExcel();
            var colores = new ExcelColorResolver(contenido);

            if (periodicidad == "SEMANAL" &&
                (fechaInicio.DayOfWeek != DayOfWeek.Sunday || fechaFin != fechaInicio.AddDays(6)))
                throw new InvalidDataException("La semana debe ir de domingo a sábado.");

            // Solo hojas visibles llamadas "Semana N"
            var hojas = new Dictionary<int, IXLWorksheet>();
            foreach (var ws in workbook.Worksheets)
            {
                if (ws.Visibility != XLWorksheetVisibility.Visible) continue;
                var m = Regex.Match(NormalizarClaveExcel(ws.Name), @"^SEMANA\s*(\d+)$");
                if (m.Success) hojas[int.Parse(m.Groups[1].Value)] = ws;
            }

            if (hojas.Count == 0)
                throw new InvalidDataException("El archivo no contiene hojas con el nombre \"Semana N\".");

            // Semanas (domingo a sábado) que toca el rango elegido
            for (var ini = SemanaProg.InicioSemana(fechaInicio); ini <= fechaFin; ini = ini.AddDays(7))
            {
                var (anio, n) = SemanaProg.De(ini);

                if (!hojas.TryGetValue(n, out var hoja))
                {
                    lectura.Advertencias.Add(
                        $"El archivo no tiene la hoja \"Semana {n}\" ({ini:dd/MM/yyyy} al {ini.AddDays(6):dd/MM/yyyy}).");
                    continue;
                }

                LeerHojaDosis(hoja, colores, ini, anio, n, fechaInicio, fechaFin, lectura);
            }

            return lectura;
        }

        // Limpia una nota de Excel. Los comentarios encadenados traen un texto de compatibilidad;
        // se conserva sólo el contenido real.
        private static string? LimpiarNotaExcel(string? texto)
        {
            texto = LimpiarTextoExcel(texto);
            if (texto == null) return null;

            var m = Regex.Match(texto, @"Comentario:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (m.Success)
                texto = m.Groups[1].Value.Trim();
            else if (texto.StartsWith("[Comentario encadenado]", StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.IsNullOrWhiteSpace(texto)) return null;

            return texto.Length > 500 ? texto[..500] : texto;
        }

        private static string? NotaDeCelda(IXLCell celda)
        {
            try
            {
                return celda.HasComment ? LimpiarNotaExcel(celda.GetComment().Text) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void LeerHojaDosis(
            IXLWorksheet ws, ExcelColorResolver colores, DateTime ini, int anio, int semana,
            DateTime desde, DateTime hasta, LecturaProgramacionExcel lectura)
        {
            lectura.Hojas.Add(ws.Name.Trim());

            // Fila 1 = día de producción (celdas combinadas), fila 2 = día de entrega, fila 3 = líneas
            var h1 = ws.Row(1).CellsUsed().Where(c => c.Address.ColumnNumber > 1).ToList();
            var h2 = ws.Row(2).CellsUsed().Where(c => c.Address.ColumnNumber > 1)
                       .ToDictionary(c => c.Address.ColumnNumber, c => NormalizarClaveExcel(c.GetString()));

            var ultimaCelda = ws.Row(3).LastCellUsed();
            if (ultimaCelda == null || h1.Count == 0)
            {
                lectura.Advertencias.Add($"Hoja \"{ws.Name.Trim()}\": no se encontraron los encabezados de días y líneas.");
                return;
            }

            // La tabla termina antes de "CUMPLIMIENTO DE PEDIDO" / "COMENTARIOS"
            int ultima = ultimaCelda.Address.ColumnNumber;
            var corte = h2.Where(k => k.Value.StartsWith("CUMPLIMIENTO") || k.Value.StartsWith("COMENTARIOS"))
                          .Select(k => k.Key).DefaultIfEmpty(ultima + 1).Min();
            ultima = Math.Min(ultima, corte - 1);

            // Cada día ocupa un bloque de columnas (el ancho cambia entre hojas)
            var bloques = new List<(int sc, int ec, string nomProd, string nomEnt, DateTime prod, DateTime ent)>();
            for (int i = 0; i < h1.Count; i++)
            {
                int sc = h1[i].Address.ColumnNumber;
                int ec = (i + 1 < h1.Count ? h1[i + 1].Address.ColumnNumber : ultima + 1) - 1;
                var nomProd = NormalizarClaveExcel(h1[i].GetString());

                if (!SemanaProg.Dia.TryGetValue(nomProd, out var dp) ||
                    !h2.TryGetValue(sc, out var nomEnt) ||
                    !SemanaProg.Dia.TryGetValue(nomEnt, out var de))
                {
                    lectura.Advertencias.Add(
                        $"Hoja \"{ws.Name.Trim()}\": no se reconoció el bloque de la columna {h1[i].Address.ColumnLetter}.");
                    continue;
                }

                bloques.Add((sc, ec, nomProd, nomEnt, ini.AddDays(dp), ini.AddDays(de)));
            }

            decimal leido = 0;
            int? filaTotales = null;
            int ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;

            for (int r = 4; r <= ultimaFila; r++)
            {
                var celdaCliente = ws.Cell(r, 1);
                var clienteClave = NormalizarClaveExcel(celdaCliente.GetString());
                if (clienteClave == "") continue;
                if (clienteClave.StartsWith("TOTAL")) { filaTotales = r; break; }

                var cliente = LimpiarTextoExcel(celdaCliente.GetString()) ?? string.Empty;

                // Color de la fila = ruta del cliente; nota del cliente (por ejemplo "pide cada 15 días")
                var colorHex = colores.Hex(celdaCliente);
                var notaCliente = NotaDeCelda(celdaCliente);

                foreach (var b in bloques)
                {
                    // Solo se toma lo que se entrega dentro del rango elegido
                    if (b.ent < desde || b.ent > hasta) continue;

                    for (int col = b.sc; col <= b.ec; col++)
                    {
                        var celda = ws.Cell(r, col);
                        if (!TryLeerCantidadExcel(celda, out var q) || q <= 0) continue;

                        leido += q;

                        var dto = new CeldaProgramacionExcelDto
                        {
                            NumeroHoja = ws.Position,
                            NombreHoja = ws.Name.Trim(),
                            NumeroFila = r,
                            Celda = celda.Address.ToString(),
                            Cliente = cliente,
                            DiaProduccion = b.nomProd,
                            DiaEntrega = b.nomEnt,
                            Linea = LimpiarTextoExcel(ws.Cell(3, col).GetString()),
                            Cantidad = q,
                            Semana = semana,
                            Anio = anio,
                            FechaProduccion = FormatearFechaExcel(b.prod),
                            FechaEntrega = FormatearFechaExcel(b.ent),
                            ColorHex = colorHex
                        };

                        var nota = NotaDeCelda(celda);
                        if (nota != null) dto.DatosAdicionales["nota"] = nota;
                        if (notaCliente != null) dto.DatosAdicionales["notaCliente"] = notaCliente;

                        lectura.Celdas.Add(dto);
                    }
                }
            }

            // Cuadre contra la fila TOTALES (solo si se eligió la semana completa): únicamente advertencia
            if (filaTotales is int ft && desde <= ini && hasta >= ini.AddDays(6))
            {
                decimal totalExcel = 0;
                foreach (var b in bloques)
                    for (int col = b.sc; col <= b.ec; col++)
                        if (TryLeerCantidadExcel(ws.Cell(ft, col), out var t) && t > 0) totalExcel += t;

                if (totalExcel != leido)
                    lectura.Advertencias.Add(
                        $"Hoja \"{ws.Name.Trim()}\": se leyeron {leido:N0} piezas y la fila TOTALES del Excel suma {totalExcel:N0}.");
            }
        }
    }
}
