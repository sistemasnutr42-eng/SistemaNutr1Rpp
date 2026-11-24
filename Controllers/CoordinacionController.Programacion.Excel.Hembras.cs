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
        private static LecturaProgramacionExcel LeerProgramacionHembrasExcel(
            XLWorkbook workbook,
            string periodicidad,
            DateTime fechaInicio,
            DateTime fechaFin)
        {
            if (workbook == null)
                throw new InvalidDataException("No fue posible abrir el archivo de programación de hembras.");

            var resultado = new LecturaProgramacionExcel();
            var numeroHoja = 0;
            var hojasCompatibles = 0;

            foreach (var worksheet in workbook.Worksheets)
            {
                numeroHoja++;

                var filaEncabezados = BuscarFilaEncabezadosHembras(worksheet);

                if (!filaEncabezados.HasValue)
                    continue;

                hojasCompatibles++;

                var columnas = ObtenerColumnasHembras(
                    worksheet,
                    filaEncabezados.Value);

                if (!columnas.FechaCarga.HasValue ||
                   !columnas.Cliente.HasValue ||
                   !columnas.NumHembras.HasValue ||
                   !columnas.TipoHembra.HasValue)
                {
                    resultado.Advertencias.Add(
                        $"La hoja \"{worksheet.Name}\" parece corresponder a Hembras, pero no contiene todos los encabezados obligatorios.");
                    continue;
                }

                var ultimaFila =
                    worksheet.LastRowUsed()?.RowNumber() ?? 0;

                if (ultimaFila <= filaEncabezados.Value)
                    continue;

                var filasAgregadas = 0;

                for (
                    var fila = filaEncabezados.Value + 1;
                    fila <= ultimaFila;
                    fila++)
                {
                    var cliente =
                        ObtenerTextoCeldaExcel(
                            worksheet.Cell(
                                fila,
                                columnas.Cliente.Value));

                    var tipoHembra =
                        ObtenerTextoCeldaExcel(
                            worksheet.Cell(
                                fila,
                                columnas.TipoHembra.Value));

                    var estatus =
                        columnas.Estatus.HasValue
                            ? ObtenerTextoCeldaExcel(
                                worksheet.Cell(
                                    fila,
                                    columnas.Estatus.Value))
                            : null;

                    var almacen =
                        columnas.Almacen.HasValue
                            ? ObtenerTextoCeldaExcel(
                                worksheet.Cell(
                                    fila,
                                    columnas.Almacen.Value))
                            : null;

                    /*
                     * Una fila completamente vacía no nos interesa.
                     */
                    if (string.IsNullOrWhiteSpace(cliente) &&
                       string.IsNullOrWhiteSpace(tipoHembra) &&
                       string.IsNullOrWhiteSpace(estatus) &&
                       string.IsNullOrWhiteSpace(almacen))
                        continue;

                    if (EsEstatusHembraCancelado(estatus))
                        continue;

                   
                    if (string.IsNullOrWhiteSpace(cliente))
                    {
                       
                        continue;
                    }

                  
                    if (!TryLeerEnteroExcel(
                        worksheet.Cell(
                            fila,
                            columnas.NumHembras.Value),
                        out var numeroHembras))
                        continue;

                    if (numeroHembras <= 0)
                        continue;

                    if (string.IsNullOrWhiteSpace(tipoHembra))
                    {
                        resultado.Advertencias.Add(
                            $"Hoja \"{worksheet.Name}\", fila {fila}: se encontró el cliente \"{cliente}\" con {numeroHembras} hembras, pero no tiene tipo de hembra.");
                        continue;
                    }

                   
                    if (!TryLeerFechaExcel(
                        worksheet.Cell(
                            fila,
                            columnas.FechaCarga.Value),
                        out var fechaCarga))
                    {
                        resultado.Advertencias.Add(
                            $"Hoja \"{worksheet.Name}\", fila {fila}: no fue posible leer la fecha de carga del cliente \"{cliente}\".");
                        continue;
                    }

                    fechaCarga = fechaCarga.Date;

                    if (fechaCarga < fechaInicio.Date ||
                       fechaCarga > fechaFin.Date)
                        continue;

                    /*
                     * Datos adicionales de la plantilla.
                     */
                    decimal? kilos = null;

                    if (columnas.Kilos.HasValue &&
                       TryLeerCantidadExcel(
                           worksheet.Cell(
                               fila,
                               columnas.Kilos.Value),
                           out var kilosLeidos) &&
                       kilosLeidos >= 0)
                    {
                        kilos = kilosLeidos;
                    }

                    decimal? pesoPromedio = null;

                    if (columnas.PesoPromedio.HasValue &&
                       TryLeerCantidadExcel(
                           worksheet.Cell(
                               fila,
                               columnas.PesoPromedio.Value),
                           out var pesoLeido) &&
                       pesoLeido >= 0)
                    {
                        pesoPromedio = pesoLeido;
                    }

                    TimeSpan? horaCarga = null;

                    if (columnas.HoraCarga.HasValue &&
                       TryLeerHoraExcel(
                           worksheet.Cell(
                               fila,
                               columnas.HoraCarga.Value),
                           out var horaLeida))
                    {
                        horaCarga = horaLeida;
                    }

                    var destino =
                        LeerTextoOpcional(
                            worksheet,
                            fila,
                            columnas.Destino);

                    var vehiculo =
                        LeerTextoOpcional(
                            worksheet,
                            fila,
                            columnas.Vehiculo);

                    var telefono =
                        LeerTextoOpcional(
                            worksheet,
                            fila,
                            columnas.Telefono);

                    var folio =
                        LeerTextoOpcional(
                            worksheet,
                            fila,
                            columnas.Folio);

                    var facturaGpn =
                        LeerTextoOpcional(
                            worksheet,
                            fila,
                            columnas.FacturaGpn);

                    var facturaCliente =
                        LeerTextoOpcional(
                            worksheet,
                            fila,
                            columnas.FacturaCliente);

                    var semana =
                        ISOWeek.GetWeekOfYear(
                            fechaCarga);

                    var anioIso =
                        ISOWeek.GetYear(
                            fechaCarga);

                    var celdaCantidad =
                        worksheet.Cell(
                            fila,
                            columnas.NumHembras.Value);

                    var datosAdicionales =
                        new Dictionary<string, object?>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            ["tipoProgramacion"] = "HEMBRAS",
                            ["almacen"] = almacen,
                            ["estatus"] = estatus,
                            ["fechaCarga"] = fechaCarga.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture),
                            ["horaCarga"] = FormatearHoraExcel(
                                horaCarga),
                            ["destino"] = destino,
                            ["kilos"] = kilos,
                            ["pesoPromedioPorHembra"] = pesoPromedio,
                            ["numeroHembras"] = numeroHembras,
                            ["tipoHembra"] = tipoHembra,
                            ["vehiculo"] = vehiculo,
                            ["telefono"] = telefono,
                            ["folio"] = folio,
                            ["facturaGpn"] = facturaGpn,
                            ["facturaCliente"] = facturaCliente,
                            ["nombreHoja"] = worksheet.Name,
                            ["numeroFila"] = fila
                        };

                    /*
                     * El modelo común no debe confundir TipoHembra
                     * con Línea Genética.
                     *
                     * Por eso Linea queda NULL y TipoHembra viaja
                     * en DatosAdicionales.
                     */
                    resultado.Celdas.Add(
                        new CeldaProgramacionExcelDto
                        {
                            NumeroHoja = numeroHoja,
                            NombreHoja = worksheet.Name,
                            NumeroFila = fila,
                            Celda = celdaCantidad.Address.ToString(),
                            Cliente = cliente,
                            DiaProduccion = null,
                            DiaEntrega = null,
                            Linea = null,
                            Cantidad = numeroHembras,
                            Semana = semana,
                            Anio = anioIso,
                            FechaProduccion = null,
                            FechaEntrega =
                                fechaCarga.ToString(
                                    "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture),
                            ColorHex = null,
                            DatosAdicionales = datosAdicionales
                        });

                    filasAgregadas++;
                }

                if (filasAgregadas > 0)
                {
                    resultado.Hojas.Add(
                        worksheet.Name);
                }
                else
                {
                    resultado.Advertencias.Add(
                        $"La hoja \"{worksheet.Name}\" tiene estructura de Hembras, pero no contiene movimientos dentro del periodo {fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}.");
                }
            }

            resultado.Hojas =
                resultado.Hojas
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (hojasCompatibles == 0)
            {
                throw new InvalidDataException(
                    "El archivo seleccionado no corresponde a la plantilla de programación de Hembras. Se esperaba una hoja con los encabezados FECHA DE CARGA, CLIENTE, NUM HEMBRAS y TIPO HEMBRA.");
            }

            if (resultado.Celdas.Count == 0)
            {
                throw new InvalidDataException(
                    $"Se encontró la estructura de programación de Hembras, pero no existen movimientos válidos entre {fechaInicio:dd/MM/yyyy} y {fechaFin:dd/MM/yyyy}.");
            }

            return resultado;
        }

        private static int? BuscarFilaEncabezadosHembras(
            IXLWorksheet worksheet)
        {
            if (worksheet == null)
                return null;

            var ultimaFila =
                worksheet.LastRowUsed()?.RowNumber() ?? 0;

            var ultimaColumna =
                worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            if (ultimaFila <= 0 ||
               ultimaColumna <= 0)
                return null;

            /*
             * Normalmente los encabezados están en la fila 1,
             * pero buscamos hasta la fila 15 para tolerar títulos
             * o espacios agregados en versiones futuras.
             */
            var limiteFilas =
                Math.Min(
                    ultimaFila,
                    15);

            for (
                var fila = 1;
                fila <= limiteFilas;
                fila++)
            {
                var tieneCliente = false;
                var tieneFechaCarga = false;
                var tieneNumHembras = false;
                var tieneTipoHembra = false;

                for (
                    var columna = 1;
                    columna <= ultimaColumna;
                    columna++)
                {
                    var encabezado =
                        ObtenerTextoCeldaExcel(
                            worksheet.Cell(
                                fila,
                                columna));

                    if (string.IsNullOrWhiteSpace(
                        encabezado))
                        continue;

                    if (TextoCoincideConAlguno(
                        encabezado,
                        "CLIENTE"))
                    {
                        tieneCliente = true;
                    }
                    else if (TextoCoincideConAlguno(
                        encabezado,
                        "FECHA DE CARGA",
                        "FECHA CARGA"))
                    {
                        tieneFechaCarga = true;
                    }
                    else if (TextoCoincideConAlguno(
                        encabezado,
                        "NUM HEMBRAS",
                        "NUMERO HEMBRAS",
                        "NUMERO DE HEMBRAS",
                        "NO HEMBRAS",
                        "NO. HEMBRAS",
                        "CANTIDAD HEMBRAS"))
                    {
                        tieneNumHembras = true;
                    }
                    else if (TextoCoincideConAlguno(
                        encabezado,
                        "TIPO HEMBRA",
                        "TIPO DE HEMBRA"))
                    {
                        tieneTipoHembra = true;
                    }
                }

                if (tieneCliente &&
                   tieneFechaCarga &&
                   tieneNumHembras &&
                   tieneTipoHembra)
                {
                    return fila;
                }
            }

            return null;
        }

        private static ColumnasProgramacionHembras ObtenerColumnasHembras(
            IXLWorksheet worksheet,
            int filaEncabezados)
        {
            var columnas =
                new ColumnasProgramacionHembras
                {
                    Almacen =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "ALMACEN"),

                    FechaCarga =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "FECHA DE CARGA",
                            "FECHA CARGA"),

                    Estatus =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "ESTATUS",
                            "STATUS"),

                    Cliente =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "CLIENTE"),

                    Kilos =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "KILOS",
                            "KG",
                            "KILOGRAMOS"),

                    PesoPromedio =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "PESO PROM POR HEMBRA",
                            "PESO PROMEDIO POR HEMBRA",
                            "PESO PROM HEMBRA",
                            "PESO PROMEDIO HEMBRA"),

                    NumHembras =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "NUM HEMBRAS",
                            "NUMERO HEMBRAS",
                            "NUMERO DE HEMBRAS",
                            "NO HEMBRAS",
                            "NO. HEMBRAS",
                            "CANTIDAD HEMBRAS"),

                    TipoHembra =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "TIPO HEMBRA",
                            "TIPO DE HEMBRA"),

                    HoraCarga =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "HORA DE CARGA",
                            "HORA CARGA"),

                    Destino =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "DESTINO"),

                    Vehiculo =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "VEHICULO",
                            "UNIDAD"),

                    Telefono =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "NO TELEFONICO",
                            "NO. TELEFONICO",
                            "NUMERO TELEFONICO",
                            "TELEFONO"),

                    Folio =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "FOLIO"),

                    FacturaGpn =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "FACTURA GPN"),

                    FacturaCliente =
                        BuscarColumnaEncabezado(
                            worksheet,
                            filaEncabezados,
                            "FACTURA CLIENTE",
                            "FACTURA A CLIENTE")
                };

            return columnas;
        }

        private static string? LeerTextoOpcional(
            IXLWorksheet worksheet,
            int fila,
            int? columna)
        {
            if (worksheet == null ||
               !columna.HasValue ||
               columna.Value <= 0)
                return null;

            return ObtenerTextoCeldaExcel(
                worksheet.Cell(
                    fila,
                    columna.Value));
        }

        private static bool EsEstatusHembraCancelado(
            string? estatus)
        {
            if (string.IsNullOrWhiteSpace(
                estatus))
                return false;

            var normalizado =
                NormalizarClaveExcel(
                    estatus);

            return normalizado.Contains(
                "CANCELADO",
                StringComparison.OrdinalIgnoreCase)
                ||
                normalizado.Contains(
                    "CANCELADA",
                    StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ColumnasProgramacionHembras
        {
            public int? Almacen { get; set; }
            public int? FechaCarga { get; set; }
            public int? Estatus { get; set; }
            public int? Cliente { get; set; }
            public int? Kilos { get; set; }
            public int? PesoPromedio { get; set; }
            public int? NumHembras { get; set; }
            public int? TipoHembra { get; set; }
            public int? HoraCarga { get; set; }
            public int? Destino { get; set; }
            public int? Vehiculo { get; set; }
            public int? Telefono { get; set; }
            public int? Folio { get; set; }
            public int? FacturaGpn { get; set; }
            public int? FacturaCliente { get; set; }
        }
    }
}