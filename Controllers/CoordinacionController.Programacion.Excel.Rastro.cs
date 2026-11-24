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
        private static LecturaProgramacionExcel LeerProgramacionRastroExcel(XLWorkbook workbook, string periodicidad, DateTime fechaInicio, DateTime fechaFin)
        {
            if (workbook == null) throw new InvalidDataException("No fue posible abrir el archivo de programación de Rastro.");
            var resultado = new LecturaProgramacionExcel();
            var numeroHoja = 0;
            var hojasCompatibles = 0;
            foreach (var worksheet in workbook.Worksheets)
            {
                numeroHoja++;
                if (EsHojaAuxiliarRastro(worksheet.Name)) continue;
                var filaEncabezados = BuscarFilaEncabezadosRastro(worksheet);
                if (!filaEncabezados.HasValue) continue;
                hojasCompatibles++;
                var columnas = ObtenerColumnasRastro(worksheet, filaEncabezados.Value);
                if (!columnas.GranjaOrigen.HasValue || !columnas.Destino.HasValue || !columnas.Cliente.HasValue || !columnas.FechaEntrega.HasValue || !columnas.AnimalesSolicitados.HasValue)
                {
                    resultado.Advertencias.Add($"La hoja \"{worksheet.Name}\" parece corresponder a Rastro, pero no contiene todos los encabezados obligatorios.");
                    continue;
                }
                var ultimaFila = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                if (ultimaFila <= filaEncabezados.Value) continue;
                var filasAgregadas = 0;
                for (var fila = filaEncabezados.Value + 1; fila <= ultimaFila; fila++)
                {
                    var granjaOrigen = LeerTextoOpcionalRastro(worksheet, fila, columnas.GranjaOrigen);
                    var destino = LeerTextoOpcionalRastro(worksheet, fila, columnas.Destino);
                    var cliente = LeerTextoOpcionalRastro(worksheet, fila, columnas.Cliente);
                    if (string.IsNullOrWhiteSpace(granjaOrigen) && string.IsNullOrWhiteSpace(destino) && string.IsNullOrWhiteSpace(cliente)) continue;
                    if (EsFilaTotalRastro(granjaOrigen, destino, cliente)) continue;
                    if (string.IsNullOrWhiteSpace(cliente))
                    {
                        if (TieneCantidadRastro(worksheet, fila, columnas.AnimalesSolicitados))
                            resultado.Advertencias.Add($"Hoja \"{worksheet.Name}\", fila {fila}: se encontró una cantidad de animales pero no tiene cliente.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(destino))
                    {
                        resultado.Advertencias.Add($"Hoja \"{worksheet.Name}\", fila {fila}: el cliente \"{cliente}\" no tiene destino.");
                        continue;
                    }
                    if (!TryLeerCantidadExcel(worksheet.Cell(fila, columnas.AnimalesSolicitados.Value), out var animalesSolicitados) || animalesSolicitados <= 0) continue;
                    var celdaFechaCarga = columnas.FechaCarga.HasValue ? worksheet.Cell(fila, columnas.FechaCarga.Value) : null;
                    var celdaFechaEntrega = worksheet.Cell(fila, columnas.FechaEntrega.Value);
                    if (!TryResolverFechasRastro(celdaFechaCarga, celdaFechaEntrega, out var fechaCarga, out var fechaEntrega))
                    {
                        var textoEntrega = ObtenerTextoCeldaExcel(celdaFechaEntrega);
                        resultado.Advertencias.Add($"Hoja \"{worksheet.Name}\", fila {fila}: no fue posible determinar la fecha de entrega \"{textoEntrega}\" del cliente \"{cliente}\".");
                        continue;
                    }
                    if (fechaEntrega.Date < fechaInicio.Date || fechaEntrega.Date > fechaFin.Date) continue;
                    TimeSpan? horaCarga = null;
                    if (columnas.HoraCarga.HasValue && TryLeerHoraExcel(worksheet.Cell(fila, columnas.HoraCarga.Value), out var horaCargaLeida))
                        horaCarga = horaCargaLeida;
                    TimeSpan? horaEntrega = null;
                    if (columnas.HoraEntrega.HasValue && TryLeerHoraExcel(worksheet.Cell(fila, columnas.HoraEntrega.Value), out var horaEntregaLeida))
                        horaEntrega = horaEntregaLeida;
                    var kgSolicitadosTexto = LeerTextoOpcionalRastro(worksheet, fila, columnas.KgSolicitados);
                    decimal? pesoObjetivoKg = null;
                    if (columnas.KgSolicitados.HasValue && TryLeerCantidadExcel(worksheet.Cell(fila, columnas.KgSolicitados.Value), out var kgNumericos) && kgNumericos > 0)
                        pesoObjetivoKg = kgNumericos;
                    var unidadFleje = LeerTextoOpcionalRastro(worksheet, fila, columnas.UnidadFleje);
                    var placaUnidad = LeerTextoOpcionalRastro(worksheet, fila, columnas.PlacaUnidad);
                    var tipoVenta = LeerTextoOpcionalRastro(worksheet, fila, columnas.TipoVenta);
                    var facturaPedido = LeerTextoOpcionalRastro(worksheet, fila, columnas.FacturaPedido);
                    var facturaGpn = LeerTextoOpcionalRastro(worksheet, fila, columnas.FacturaGpn);
                    var pedidoVentaGpn = LeerTextoOpcionalRastro(worksheet, fila, columnas.PedidoVentaGpn);
                    var entradaGranja = LeerTextoOpcionalRastro(worksheet, fila, columnas.EntradaGranja);
                    var horaInicioCarga = LeerTextoOpcionalRastro(worksheet, fila, columnas.HoraInicioCarga);
                    var terminoCarga = LeerTextoOpcionalRastro(worksheet, fila, columnas.TerminoCarga);
                    var duracionCarga = LeerTextoOpcionalRastro(worksheet, fila, columnas.DuracionCarga);
                    var salidaGranja = LeerTextoOpcionalRastro(worksheet, fila, columnas.SalidaGranja);
                    var semana = ISOWeek.GetWeekOfYear(fechaEntrega);
                    var anio = ISOWeek.GetYear(fechaEntrega);
                    var diaEntrega = NormalizarClaveExcel(fechaEntrega.ToString("dddd", CultureInfo.GetCultureInfo("es-MX")));
                    var celdaCantidad = worksheet.Cell(fila, columnas.AnimalesSolicitados.Value);
                    var datosAdicionales = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tipoProgramacion"] = "RASTRO",
                        ["periodicidad"] = periodicidad,
                        ["granjaOrigen"] = granjaOrigen,
                        ["destino"] = destino,
                        ["fechaCarga"] = fechaCarga.HasValue ? fechaCarga.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                        ["horaCarga"] = FormatearHoraExcel(horaCarga),
                        ["fechaEntrega"] = fechaEntrega.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["horaEntrega"] = FormatearHoraExcel(horaEntrega),
                        ["animalesSolicitados"] = animalesSolicitados,
                        ["kgSolicitadosTexto"] = kgSolicitadosTexto,
                        ["pesoPromedioObjetivoKg"] = pesoObjetivoKg,
                        ["unidadFleje"] = unidadFleje,
                        ["placaUnidad"] = placaUnidad,
                        ["tipoVenta"] = tipoVenta,
                        ["facturaPedido"] = facturaPedido,
                        ["facturaGpn"] = facturaGpn,
                        ["pedidoVentaGpn"] = pedidoVentaGpn,
                        ["entradaGranja"] = entradaGranja,
                        ["horaInicioCarga"] = horaInicioCarga,
                        ["terminoCarga"] = terminoCarga,
                        ["duracionCarga"] = duracionCarga,
                        ["salidaGranja"] = salidaGranja,
                        ["nombreHoja"] = worksheet.Name,
                        ["numeroFila"] = fila
                    };
                    resultado.Celdas.Add(new CeldaProgramacionExcelDto
                    {
                        NumeroHoja = numeroHoja,
                        NombreHoja = worksheet.Name,
                        NumeroFila = fila,
                        Celda = celdaCantidad.Address.ToString(),
                        Cliente = cliente,
                        DiaProduccion = null,
                        DiaEntrega = diaEntrega,
                        Linea = null,
                        Cantidad = animalesSolicitados,
                        Semana = semana,
                        Anio = anio,
                        FechaProduccion = null,
                        FechaEntrega = fechaEntrega.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ColorHex = null,
                        DatosAdicionales = datosAdicionales
                    });
                    filasAgregadas++;
                }
                if (filasAgregadas > 0)
                    resultado.Hojas.Add(worksheet.Name);
                else
                    resultado.Advertencias.Add($"La hoja \"{worksheet.Name}\" tiene estructura de Rastro, pero no contiene entregas válidas dentro del periodo {fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}.");
            }
            resultado.Hojas = resultado.Hojas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (hojasCompatibles == 0)
                throw new InvalidDataException("El archivo seleccionado no corresponde a la plantilla de programación de Rastro. Se esperaba una tabla con GRANJA ORIGEN, DESTINO, CLIENTE, FECHA DE ENTREGA y # DE ANIMALES SOLICITADOS.");
            if (resultado.Celdas.Count == 0)
                throw new InvalidDataException($"Se encontró la estructura de programación de Rastro, pero no existen entregas válidas entre {fechaInicio:dd/MM/yyyy} y {fechaFin:dd/MM/yyyy}.");
            return resultado;
        }

        private static int? BuscarFilaEncabezadosRastro(IXLWorksheet worksheet)
        {
            if (worksheet == null) return null;
            var ultimaFila = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var ultimaColumna = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (ultimaFila <= 0 || ultimaColumna <= 0) return null;
            var limiteFilas = Math.Min(ultimaFila, 30);
            for (var fila = 1; fila <= limiteFilas; fila++)
            {
                var tieneGranja = false;
                var tieneDestino = false;
                var tieneCliente = false;
                var tieneFechaEntrega = false;
                var tieneAnimales = false;
                for (var columna = 1; columna <= ultimaColumna; columna++)
                {
                    var encabezado = ObtenerTextoCeldaExcel(worksheet.Cell(fila, columna));
                    if (string.IsNullOrWhiteSpace(encabezado)) continue;
                    if (TextoCoincideConAlguno(encabezado, "GRANJA ORIGEN", "GRANJA DE ORIGEN"))
                        tieneGranja = true;
                    else if (TextoCoincideConAlguno(encabezado, "DESTINO"))
                        tieneDestino = true;
                    else if (TextoCoincideConAlguno(encabezado, "CLIENTE"))
                        tieneCliente = true;
                    else if (TextoCoincideConAlguno(encabezado, "FECHA DE ENTREGA", "FECHA ENTREGA"))
                        tieneFechaEntrega = true;
                    else if (TextoCoincideConAlguno(encabezado, "# DE ANIMALES SOLICITADOS", "NUM DE ANIMALES SOLICITADOS", "NUMERO DE ANIMALES SOLICITADOS", "ANIMALES SOLICITADOS"))
                        tieneAnimales = true;
                }
                if (tieneGranja && tieneDestino && tieneCliente && tieneFechaEntrega && tieneAnimales)
                    return fila;
            }
            return null;
        }

        private static ColumnasProgramacionRastro ObtenerColumnasRastro(IXLWorksheet worksheet, int filaEncabezados)
        {
            return new ColumnasProgramacionRastro
            {
                GranjaOrigen = BuscarColumnaEncabezado(worksheet, filaEncabezados, "GRANJA ORIGEN", "GRANJA DE ORIGEN"),
                Destino = BuscarColumnaEncabezado(worksheet, filaEncabezados, "DESTINO"),
                Cliente = BuscarColumnaEncabezado(worksheet, filaEncabezados, "CLIENTE"),
                FechaCarga = BuscarColumnaEncabezado(worksheet, filaEncabezados, "FECHA DE CARGA", "FECHA CARGA"),
                HoraCarga = BuscarColumnaEncabezado(worksheet, filaEncabezados, "HORA DE CARGA GRANJA", "HORA CARGA GRANJA", "HORA DE CARGA", "HORA CARGA"),
                FechaEntrega = BuscarColumnaEncabezado(worksheet, filaEncabezados, "FECHA DE ENTREGA", "FECHA ENTREGA"),
                HoraEntrega = BuscarColumnaEncabezado(worksheet, filaEncabezados, "HORA DE ENTREGA", "HORA ENTREGA"),
                AnimalesSolicitados = BuscarColumnaEncabezado(worksheet, filaEncabezados, "# DE ANIMALES SOLICITADOS", "NUM DE ANIMALES SOLICITADOS", "NUMERO DE ANIMALES SOLICITADOS", "ANIMALES SOLICITADOS"),
                KgSolicitados = BuscarColumnaEncabezado(worksheet, filaEncabezados, "KG SOLICITADOS", "KILOS SOLICITADOS", "PESO SOLICITADO", "PESO PROMEDIO SOLICITADO"),
                UnidadFleje = BuscarColumnaEncabezado(worksheet, filaEncabezados, "UNIDAD", "FLEJE"),
                PlacaUnidad = BuscarColumnaEncabezado(worksheet, filaEncabezados, "PLACA UNIDAD", "PLACAS", "PLACA"),
                TipoVenta = BuscarColumnaEncabezado(worksheet, filaEncabezados, "TIPO VENTA", "TIPO DE VENTA"),
                FacturaPedido = BuscarColumnaEncabezado(worksheet, filaEncabezados, "FACTURA / PEDIDO", "FACTURA PEDIDO"),
                FacturaGpn = BuscarColumnaEncabezado(worksheet, filaEncabezados, "FACTURA GPN"),
                PedidoVentaGpn = BuscarColumnaEncabezado(worksheet, filaEncabezados, "PEDIDO DE VENTA GPN", "PEDIDO VENTA GPN"),
                EntradaGranja = BuscarColumnaEncabezado(worksheet, filaEncabezados, "ENTRADA A GRANJA"),
                HoraInicioCarga = BuscarColumnaEncabezado(worksheet, filaEncabezados, "HORA INICIO CARGA", "HORA DE INICIO CARGA"),
                TerminoCarga = BuscarColumnaEncabezado(worksheet, filaEncabezados, "TERMINO DE CARGA", "TERMINO CARGA"),
                DuracionCarga = BuscarColumnaEncabezado(worksheet, filaEncabezados, "DURACION DE CARGA", "DURACION CARGA"),
                SalidaGranja = BuscarColumnaEncabezado(worksheet, filaEncabezados, "SALIDA DE GRANJA", "SALIDA GRANJA")
            };
        }

        private static bool TryResolverFechasRastro(
     IXLCell? celdaCarga,
     IXLCell celdaEntrega,
     out DateTime? fechaCarga,
     out DateTime fechaEntrega)
        {
            fechaCarga = null;
            fechaEntrega = default;

            DateTime cargaLeida = default;
            DateTime entregaLeida = default;

            var tieneCarga =
                celdaCarga != null
                && TryLeerFechaExcel(
                    celdaCarga,
                    out cargaLeida);

            var tieneEntrega =
                TryLeerFechaExcel(
                    celdaEntrega,
                    out entregaLeida);

            if (tieneCarga)
                fechaCarga = cargaLeida.Date;

            if (tieneEntrega)
                fechaEntrega = entregaLeida.Date;

            /*
             * Algunas versiones del Excel manejan:
             * FECHA DE CARGA = FECHA DE ENTREGA - 1
             * FECHA DE CARGA = FECHA DE ENTREGA
             *
             * Si ClosedXML no pudo resolver la fórmula,
             * intentamos resolver referencias simples entre
             * las dos celdas.
             */
            if (!tieneCarga
               && tieneEntrega
               && celdaCarga != null
               && celdaCarga.HasFormula)
            {
                if (TryResolverFormulaFechaRastro(
                    celdaCarga,
                    celdaEntrega,
                    fechaEntrega,
                    out var cargaCalculada))
                {
                    fechaCarga = cargaCalculada.Date;
                    tieneCarga = true;
                }
            }

            if (!tieneEntrega
               && tieneCarga
               && celdaEntrega.HasFormula
               && celdaCarga != null
               && fechaCarga.HasValue)
            {
                if (TryResolverFormulaFechaRastro(
                    celdaEntrega,
                    celdaCarga,
                    fechaCarga.Value,
                    out var entregaCalculada))
                {
                    fechaEntrega = entregaCalculada.Date;
                    tieneEntrega = true;
                }
            }

            return tieneEntrega;
        }

        private static bool TryResolverFormulaFechaRastro(IXLCell celdaFormula, IXLCell celdaReferencia, DateTime fechaReferencia, out DateTime fecha)
        {
            fecha = default;
            if (celdaFormula == null || celdaReferencia == null || !celdaFormula.HasFormula) return false;
            var formula = celdaFormula.FormulaA1;
            if (string.IsNullOrWhiteSpace(formula)) return false;
            var normalizada = formula.Trim().ToUpperInvariant().Replace("$", string.Empty).Replace(" ", string.Empty);
            while (normalizada.StartsWith("=") || normalizada.StartsWith("+"))
                normalizada = normalizada[1..];
            var referencia = celdaReferencia.Address.ToString().ToUpperInvariant().Replace("$", string.Empty);
            if (string.Equals(normalizada, referencia, StringComparison.OrdinalIgnoreCase))
            {
                fecha = fechaReferencia.Date;
                return true;
            }
            var match = Regex.Match(normalizada, $@"^{Regex.Escape(referencia)}(?<operador>[\+\-])(?<dias>\d+)$", RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            if (!int.TryParse(match.Groups["dias"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dias)) return false;
            fecha = match.Groups["operador"].Value == "-"
                ? fechaReferencia.Date.AddDays(-dias)
                : fechaReferencia.Date.AddDays(dias);
            return true;
        }

        private static bool EsHojaAuxiliarRastro(string? nombreHoja)
        {
            var nombre = NormalizarClaveExcel(nombreHoja);
            return nombre == "RESUMEN"
                || nombre == "PRECIO CERDO"
                || nombre == "PROYECTADO"
                || nombre == "HISTORICO ENTREGAS";
        }

        private static bool EsFilaTotalRastro(string? granja, string? destino, string? cliente)
        {
            var valores = new[]
            {
                NormalizarClaveExcel(granja),
                NormalizarClaveExcel(destino),
                NormalizarClaveExcel(cliente)
            };
            return valores.Any(x =>
                x == "TOTAL"
                || x.StartsWith("TOTAL ", StringComparison.OrdinalIgnoreCase)
                || x.StartsWith("ENVIOS", StringComparison.OrdinalIgnoreCase)
                || x.StartsWith("EDAD DE VTA", StringComparison.OrdinalIgnoreCase)
                || x.StartsWith("% DE MUERTOS", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TieneCantidadRastro(IXLWorksheet worksheet, int fila, int? columna)
        {
            if (worksheet == null || !columna.HasValue || columna.Value <= 0) return false;
            return TryLeerCantidadExcel(worksheet.Cell(fila, columna.Value), out var cantidad) && cantidad > 0;
        }

        private static string? LeerTextoOpcionalRastro(IXLWorksheet worksheet, int fila, int? columna)
        {
            if (worksheet == null || !columna.HasValue || columna.Value <= 0) return null;
            return ObtenerTextoCeldaExcel(worksheet.Cell(fila, columna.Value));
        }

        private sealed class ColumnasProgramacionRastro
        {
            public int? GranjaOrigen { get; set; }
            public int? Destino { get; set; }
            public int? Cliente { get; set; }
            public int? FechaCarga { get; set; }
            public int? HoraCarga { get; set; }
            public int? FechaEntrega { get; set; }
            public int? HoraEntrega { get; set; }
            public int? AnimalesSolicitados { get; set; }
            public int? KgSolicitados { get; set; }
            public int? UnidadFleje { get; set; }
            public int? PlacaUnidad { get; set; }
            public int? TipoVenta { get; set; }
            public int? FacturaPedido { get; set; }
            public int? FacturaGpn { get; set; }
            public int? PedidoVentaGpn { get; set; }
            public int? EntradaGranja { get; set; }
            public int? HoraInicioCarga { get; set; }
            public int? TerminoCarga { get; set; }
            public int? DuracionCarga { get; set; }
            public int? SalidaGranja { get; set; }
        }
    }
}