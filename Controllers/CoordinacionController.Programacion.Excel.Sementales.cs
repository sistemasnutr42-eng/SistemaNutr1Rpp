using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SistemaNutri.Controllers
{
    public partial class CoordinacionController
    {
        private static LecturaProgramacionExcel LeerProgramacionSementalesExcel(
            XLWorkbook workbook,
            string periodicidad,
            DateTime fechaInicio,
            DateTime fechaFin)
        {
            if (workbook == null)
                throw new InvalidDataException("No fue posible abrir el archivo de programación de sementales.");

            var resultado = new LecturaProgramacionExcel();
            var numeroHoja = 0;
            var hojasCompatibles = 0;

            foreach (var worksheet in workbook.Worksheets)
            {
                numeroHoja++;
                var filaEncabezados = BuscarFilaEncabezadosSementales(worksheet);

                if (!filaEncabezados.HasValue)
                    continue;

                hojasCompatibles++;
                var columnas = ObtenerColumnasSementales(worksheet, filaEncabezados.Value);

                if (!columnas.FechaCarga.HasValue ||
                    !columnas.Cliente.HasValue ||
                    !columnas.NumSementales.HasValue)
                {
                    resultado.Advertencias.Add(
                        $"La hoja \"{worksheet.Name}\" parece corresponder a Sementales, pero no contiene todos los encabezados obligatorios.");
                    continue;
                }

                var ultimaFila = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                if (ultimaFila <= filaEncabezados.Value)
                    continue;

                var filasAgregadas = 0;

                for (var fila = filaEncabezados.Value + 1; fila <= ultimaFila; fila++)
                {
                    var cliente = ObtenerTextoCeldaExcel(
                        worksheet.Cell(fila, columnas.Cliente.Value));
                    var estatus = LeerTextoOpcional(worksheet, fila, columnas.Estatus);
                    var almacen = LeerTextoOpcional(worksheet, fila, columnas.Almacen);

                    if (string.IsNullOrWhiteSpace(cliente) &&
                        string.IsNullOrWhiteSpace(estatus) &&
                        string.IsNullOrWhiteSpace(almacen))
                        continue;

                    if (EsEstatusSementalCancelado(estatus) || string.IsNullOrWhiteSpace(cliente))
                        continue;

                    var celdaCantidad = worksheet.Cell(fila, columnas.NumSementales.Value);
                    if (!TryLeerCantidadExcel(celdaCantidad, out var cantidad) || cantidad <= 0)
                        continue;

                    if (!TryLeerFechaExcel(
                        worksheet.Cell(fila, columnas.FechaCarga.Value),
                        out var fechaCarga))
                    {
                        resultado.Advertencias.Add(
                            $"Hoja \"{worksheet.Name}\", fila {fila}: no fue posible leer la fecha de carga del cliente \"{cliente}\".");
                        continue;
                    }

                    fechaCarga = fechaCarga.Date;
                    if (fechaCarga < fechaInicio.Date || fechaCarga > fechaFin.Date)
                        continue;

                    TimeSpan? horaCarga = null;
                    if (columnas.HoraCarga.HasValue &&
                        TryLeerHoraExcel(worksheet.Cell(fila, columnas.HoraCarga.Value), out var horaLeida))
                        horaCarga = horaLeida;

                    var lineaGenetica = LeerTextoOpcional(worksheet, fila, columnas.LineaGenetica);
                    var caracteristicas = LeerTextoOpcional(worksheet, fila, columnas.Caracteristicas);
                    var destino = LeerTextoOpcional(worksheet, fila, columnas.Destino);
                    var importacion = LeerTextoOpcional(worksheet, fila, columnas.Importacion);
                    var paisOrigen = LeerTextoOpcional(worksheet, fila, columnas.PaisOrigen);
                    var referenciaImportacion = LeerTextoOpcional(worksheet, fila, columnas.ReferenciaImportacion);
                    var folio = LeerTextoOpcional(worksheet, fila, columnas.Folio);

                    var datosAdicionales = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tipoProgramacion"] = "SEMENTALES",
                        ["periodicidad"] = periodicidad,
                        ["almacen"] = almacen,
                        ["estatus"] = estatus,
                        ["fechaCarga"] = fechaCarga.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["horaCarga"] = FormatearHoraExcel(horaCarga),
                        ["destino"] = destino,
                        ["numeroSementales"] = cantidad,
                        ["lineaGenetica"] = lineaGenetica,
                        ["caracteristicasSemental"] = caracteristicas,
                        ["importacion"] = importacion,
                        ["paisOrigen"] = paisOrigen,
                        ["referenciaImportacion"] = referenciaImportacion,
                        ["folio"] = folio,
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
                        DiaEntrega = null,
                        Linea = lineaGenetica,
                        Cantidad = cantidad,
                        Semana = ISOWeek.GetWeekOfYear(fechaCarga),
                        Anio = ISOWeek.GetYear(fechaCarga),
                        FechaProduccion = null,
                        FechaEntrega = fechaCarga.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ColorHex = null,
                        DatosAdicionales = datosAdicionales
                    });

                    filasAgregadas++;
                }

                if (filasAgregadas > 0)
                    resultado.Hojas.Add(worksheet.Name);
                else
                    resultado.Advertencias.Add(
                        $"La hoja \"{worksheet.Name}\" tiene estructura de Sementales, pero no contiene movimientos dentro del periodo {fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}.");
            }

            resultado.Hojas = resultado.Hojas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (hojasCompatibles == 0)
                throw new InvalidDataException(
                    "El archivo seleccionado no corresponde a la plantilla de programación de Sementales. Se esperaba una hoja con los encabezados FECHA DE CARGA, CLIENTE y NUM SEMENTALES.");

            if (resultado.Celdas.Count == 0)
                throw new InvalidDataException(
                    $"Se encontró la estructura de programación de Sementales, pero no existen movimientos válidos entre {fechaInicio:dd/MM/yyyy} y {fechaFin:dd/MM/yyyy}.");

            return resultado;
        }

        private static int? BuscarFilaEncabezadosSementales(IXLWorksheet worksheet)
        {
            if (worksheet == null)
                return null;

            var ultimaFila = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var ultimaColumna = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (ultimaFila <= 0 || ultimaColumna <= 0)
                return null;

            for (var fila = 1; fila <= Math.Min(ultimaFila, 15); fila++)
            {
                var tieneCliente = false;
                var tieneFechaCarga = false;
                var tieneCantidad = false;

                for (var columna = 1; columna <= ultimaColumna; columna++)
                {
                    var encabezado = ObtenerTextoCeldaExcel(worksheet.Cell(fila, columna));
                    if (string.IsNullOrWhiteSpace(encabezado))
                        continue;

                    if (TextoCoincideConAlguno(encabezado, "CLIENTE"))
                        tieneCliente = true;
                    else if (TextoCoincideConAlguno(encabezado, "FECHA DE CARGA", "FECHA CARGA"))
                        tieneFechaCarga = true;
                    else if (TextoCoincideConAlguno(
                        encabezado,
                        "NUM SEMENTALES",
                        "NUMERO SEMENTALES",
                        "NUMERO DE SEMENTALES",
                        "NO SEMENTALES",
                        "NO. SEMENTALES",
                        "CANTIDAD SEMENTALES",
                        "CANTIDAD DE SEMENTALES"))
                        tieneCantidad = true;
                }

                if (tieneCliente && tieneFechaCarga && tieneCantidad)
                    return fila;
            }

            return null;
        }

        private static ColumnasProgramacionSementales ObtenerColumnasSementales(
            IXLWorksheet worksheet,
            int filaEncabezados)
        {
            return new ColumnasProgramacionSementales
            {
                Almacen = BuscarColumnaEncabezado(worksheet, filaEncabezados, "ALMACEN"),
                FechaCarga = BuscarColumnaEncabezado(worksheet, filaEncabezados, "FECHA DE CARGA", "FECHA CARGA"),
                Estatus = BuscarColumnaEncabezado(worksheet, filaEncabezados, "ESTATUS", "STATUS"),
                Cliente = BuscarColumnaEncabezado(worksheet, filaEncabezados, "CLIENTE"),
                NumSementales = BuscarColumnaEncabezado(
                    worksheet,
                    filaEncabezados,
                    "NUM SEMENTALES",
                    "NUMERO SEMENTALES",
                    "NUMERO DE SEMENTALES",
                    "NO SEMENTALES",
                    "NO. SEMENTALES",
                    "CANTIDAD SEMENTALES",
                    "CANTIDAD DE SEMENTALES"),
                HoraCarga = BuscarColumnaEncabezado(worksheet, filaEncabezados, "HORA DE CARGA", "HORA CARGA"),
                Destino = BuscarColumnaEncabezado(worksheet, filaEncabezados, "DESTINO"),
                LineaGenetica = BuscarColumnaEncabezado(worksheet, filaEncabezados, "LINEA GENETICA", "LINEA"),
                Caracteristicas = BuscarColumnaEncabezado(worksheet, filaEncabezados, "CARACTERISTICAS", "CARACTERISTICAS SEMENTAL"),
                Importacion = BuscarColumnaEncabezado(worksheet, filaEncabezados, "IMPORTACION", "ES IMPORTACION"),
                PaisOrigen = BuscarColumnaEncabezado(worksheet, filaEncabezados, "PAIS DE ORIGEN", "PAIS ORIGEN"),
                ReferenciaImportacion = BuscarColumnaEncabezado(worksheet, filaEncabezados, "REFERENCIA DE IMPORTACION", "REFERENCIA IMPORTACION"),
                Folio = BuscarColumnaEncabezado(worksheet, filaEncabezados, "FOLIO")
            };
        }

        private static bool EsEstatusSementalCancelado(string? estatus)
        {
            if (string.IsNullOrWhiteSpace(estatus))
                return false;

            var normalizado = NormalizarClaveExcel(estatus);
            return normalizado.Contains("CANCELADO", StringComparison.OrdinalIgnoreCase) ||
                   normalizado.Contains("CANCELADA", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ColumnasProgramacionSementales
        {
            public int? Almacen { get; set; }
            public int? FechaCarga { get; set; }
            public int? Estatus { get; set; }
            public int? Cliente { get; set; }
            public int? NumSementales { get; set; }
            public int? HoraCarga { get; set; }
            public int? Destino { get; set; }
            public int? LineaGenetica { get; set; }
            public int? Caracteristicas { get; set; }
            public int? Importacion { get; set; }
            public int? PaisOrigen { get; set; }
            public int? ReferenciaImportacion { get; set; }
            public int? Folio { get; set; }
        }
    }
}
