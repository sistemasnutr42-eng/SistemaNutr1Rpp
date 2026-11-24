using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using SistemaNutri.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaNutri.Controllers
{
    public partial class CoordinacionController : Controller
    {
        private const string UrlPrecioMercadoCerdo = "/Coordinacion/PrecioMercadoCerdo";

        [HttpGet]
        public async Task<IActionResult> PrecioMercadoCerdo(
            [FromQuery] CoordinacionPrecioMercadoCerdoFiltroVm filtros,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlPrecioMercadoCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            filtros ??= new CoordinacionPrecioMercadoCerdoFiltroVm();
            filtros.Normalizar();

            if (!filtros.Anio.HasValue &&
                !filtros.FechaDesde.HasValue &&
                !filtros.FechaHasta.HasValue)
            {
                filtros.Anio = DateTime.Today.Year;
            }

            if (filtros.FechaDesde.HasValue &&
                filtros.FechaHasta.HasValue &&
                filtros.FechaHasta.Value.Date < filtros.FechaDesde.Value.Date)
            {
                ModelState.AddModelError(
                    nameof(filtros.FechaHasta),
                    "La fecha final no puede ser anterior a la fecha inicial.");
            }

            var model = new CoordinacionPrecioMercadoCerdoPaginaVm
            {
                Filtros = filtros,
                Captura = new CoordinacionPrecioMercadoCerdoCapturaVm
                {
                    Fecha = DateTime.Today
                }
            };

            await using var connection =
                await AbrirAsync(cancellationToken);

            model.PrecioActual =
                await ObtenerPrecioMercadoCerdoActualAsync(
                    connection,
                    DateTime.Today,
                    cancellationToken);

            if (model.PrecioActual != null &&
                model.PrecioActual.EsPrecioDelDia)
            {
                model.Captura.PrecioMercadoCerdoID =
                    model.PrecioActual.PrecioMercadoCerdoID;

                model.Captura.Fecha =
                    model.PrecioActual.Fecha;

                model.Captura.PrecioKg =
                    model.PrecioActual.PrecioKg;

                model.Captura.ContactoReferencia =
                    model.PrecioActual.ContactoReferencia;

                model.Captura.Observaciones =
                    model.PrecioActual.Observaciones;

                model.Captura.EsEdicion = true;
            }

            if (ModelState.IsValid)
            {
                model.Historico =
                    await ObtenerHistoricoPrecioMercadoCerdoAsync(
                        connection,
                        filtros,
                        cancellationToken);
            }

            var anioResumen =
                filtros.Anio
                ?? filtros.FechaHasta?.Year
                ?? filtros.FechaDesde?.Year
                ?? DateTime.Today.Year;

            model.Resumen =
                await ObtenerResumenPrecioMercadoCerdoAsync(
                    connection,
                    anioResumen,
                    cancellationToken);

            model.AniosDisponibles =
                await ObtenerAniosPrecioMercadoCerdoAsync(
                    connection,
                    cancellationToken);

            if (!model.AniosDisponibles.Contains(DateTime.Today.Year))
            {
                model.AniosDisponibles.Add(DateTime.Today.Year);
                model.AniosDisponibles =
                    model.AniosDisponibles
                        .Distinct()
                        .OrderByDescending(x => x)
                        .ToList();
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> HistoricoPrecioMercadoCerdo(
            [FromQuery] CoordinacionPrecioMercadoCerdoFiltroVm filtros,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlPrecioMercadoCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            filtros ??= new CoordinacionPrecioMercadoCerdoFiltroVm();
            filtros.Normalizar();

            if (!filtros.Anio.HasValue &&
                !filtros.FechaDesde.HasValue &&
                !filtros.FechaHasta.HasValue)
            {
                filtros.Anio = DateTime.Today.Year;
            }

            if (filtros.FechaDesde.HasValue &&
                filtros.FechaHasta.HasValue &&
                filtros.FechaHasta.Value.Date < filtros.FechaDesde.Value.Date)
            {
                ModelState.AddModelError(
                    nameof(filtros.FechaHasta),
                    "La fecha final no puede ser anterior a la fecha inicial.");
            }

            var model = new CoordinacionPrecioMercadoCerdoPaginaVm
            {
                Filtros = filtros,
                Captura = new CoordinacionPrecioMercadoCerdoCapturaVm
                {
                    Fecha = DateTime.Today
                }
            };

            await using var connection =
                await AbrirAsync(cancellationToken);

            model.PrecioActual =
                await ObtenerPrecioMercadoCerdoActualAsync(
                    connection,
                    DateTime.Today,
                    cancellationToken);

            if (ModelState.IsValid)
            {
                model.Historico =
                    await ObtenerHistoricoPrecioMercadoCerdoAsync(
                        connection,
                        filtros,
                        cancellationToken);
            }

            var anioResumen =
                filtros.Anio
                ?? filtros.FechaHasta?.Year
                ?? filtros.FechaDesde?.Year
                ?? DateTime.Today.Year;

            model.Resumen =
                await ObtenerResumenPrecioMercadoCerdoAsync(
                    connection,
                    anioResumen,
                    cancellationToken);

            model.AniosDisponibles =
                await ObtenerAniosPrecioMercadoCerdoAsync(
                    connection,
                    cancellationToken);

            if (!model.AniosDisponibles.Contains(DateTime.Today.Year))
            {
                model.AniosDisponibles.Add(DateTime.Today.Year);
            }

            model.AniosDisponibles =
                model.AniosDisponibles
                    .Distinct()
                    .OrderByDescending(x => x)
                    .ToList();

            return View("HistoricoPrecioMercadoCerdo", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPrecioMercadoCerdo(
            [FromBody] CoordinacionPrecioMercadoCerdoCapturaVm model,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlPrecioMercadoCerdo,
                "Editar",
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (model == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "No se recibió la información del precio."
                });
            }

            model.ContactoReferencia =
                LimpiarPrecioTexto(model.ContactoReferencia, 150);

            model.Observaciones =
                LimpiarPrecioTexto(model.Observaciones, 500);

            model.MotivoCambio =
                LimpiarPrecioTexto(model.MotivoCambio, 500);

            if (model.Fecha == default)
            {
                ModelState.AddModelError(
                    nameof(model.Fecha),
                    "La fecha es obligatoria.");
            }

            if (model.PrecioKg <= 0)
            {
                ModelState.AddModelError(
                    nameof(model.PrecioKg),
                    "El precio por kilogramo debe ser mayor a cero.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = ObtenerErroresModelState()
                });
            }

            if (!UsuarioID.HasValue || UsuarioID.Value <= 0)
            {
                return Unauthorized();
            }

            try
            {
                await using var connection =
                    await AbrirAsync(cancellationToken);

                var resultado =
                    await GuardarPrecioMercadoCerdoAsync(
                        connection,
                        model,
                        UsuarioID.Value,
                        cancellationToken);

                return Json(new
                {
                    ok = true,
                    mensaje = model.EsEdicion
                        ? "Precio del día actualizado correctamente."
                        : "Precio del día guardado correctamente.",
                    precio = resultado
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = LimpiarMensajeSql(ex.Message)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        ok = false,
                        mensaje = "No fue posible guardar el precio de mercado.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerPrecioMercadoCerdoActual(
            DateTime? fecha,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlPrecioMercadoCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            try
            {
                await using var connection =
                    await AbrirAsync(cancellationToken);

                var resultado =
                    await ObtenerPrecioMercadoCerdoActualAsync(
                        connection,
                        fecha?.Date ?? DateTime.Today,
                        cancellationToken);

                return Json(new
                {
                    ok = true,
                    precio = resultado
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = LimpiarMensajeSql(ex.Message)
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerHistoricoPrecioMercadoCerdo(
            [FromQuery] CoordinacionPrecioMercadoCerdoFiltroVm filtros,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlPrecioMercadoCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            filtros ??= new CoordinacionPrecioMercadoCerdoFiltroVm();
            filtros.Normalizar();

            if (filtros.FechaDesde.HasValue &&
                filtros.FechaHasta.HasValue &&
                filtros.FechaHasta.Value.Date < filtros.FechaDesde.Value.Date)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La fecha final no puede ser anterior a la fecha inicial."
                });
            }

            try
            {
                await using var connection =
                    await AbrirAsync(cancellationToken);

                var historico =
                    await ObtenerHistoricoPrecioMercadoCerdoAsync(
                        connection,
                        filtros,
                        cancellationToken);

                return Json(new
                {
                    ok = true,
                    total = historico.Count,
                    historico
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = LimpiarMensajeSql(ex.Message)
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerResumenPrecioMercadoCerdo(
            int? anio,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlPrecioMercadoCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            var anioConsulta =
                anio.HasValue &&
                anio.Value >= 2000 &&
                anio.Value <= 2100
                    ? anio.Value
                    : DateTime.Today.Year;

            try
            {
                await using var connection =
                    await AbrirAsync(cancellationToken);

                var resumen =
                    await ObtenerResumenPrecioMercadoCerdoAsync(
                        connection,
                        anioConsulta,
                        cancellationToken);

                return Json(new
                {
                    ok = true,
                    resumen
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = LimpiarMensajeSql(ex.Message)
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerHistorialCambiosPrecioMercadoCerdo(
            int precioMercadoCerdoId,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlPrecioMercadoCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (precioMercadoCerdoId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El registro de precio no es válido."
                });
            }

            try
            {
                await using var connection =
                    await AbrirAsync(cancellationToken);

                var historial =
                    await ObtenerHistorialCambiosPrecioMercadoCerdoAsync(
                        connection,
                        precioMercadoCerdoId,
                        cancellationToken);

                return Json(new
                {
                    ok = true,
                    historial
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = LimpiarMensajeSql(ex.Message)
                });
            }
        }

        private static async Task<CoordinacionPrecioMercadoCerdoResultadoGuardarVm>
            GuardarPrecioMercadoCerdoAsync(
                SqlConnection connection,
                CoordinacionPrecioMercadoCerdoCapturaVm model,
                int usuarioId,
                CancellationToken cancellationToken)
        {
            await using var command =
                new SqlCommand(
                    "dbo.usp_Coordinacion_PrecioMercadoCerdo_Guardar",
                    connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

            command.Parameters.Add(
                "@Fecha",
                SqlDbType.Date).Value = model.Fecha.Date;

            command.Parameters.Add(
                "@PrecioKg",
                SqlDbType.Decimal).Value = model.PrecioKg;

            command.Parameters["@PrecioKg"].Precision = 10;
            command.Parameters["@PrecioKg"].Scale = 2;

            AddNullableString(
                command,
                "@ContactoReferencia",
                150,
                model.ContactoReferencia);

            AddNullableString(
                command,
                "@Observaciones",
                500,
                model.Observaciones);

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            AddNullableString(
                command,
                "@MotivoCambio",
                500,
                model.MotivoCambio);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var resultado =
                new CoordinacionPrecioMercadoCerdoResultadoGuardarVm();

            if (!await reader.ReadAsync(cancellationToken))
                return resultado;

            resultado.PrecioMercadoCerdoID =
                ReadInt(reader, "PrecioMercadoCerdoID");

            resultado.Fecha =
                ReadDateTime(reader, "Fecha");

            resultado.PrecioKg =
                ReadDecimal(reader, "PrecioKg");

            resultado.FuenteCodigo =
                ReadString(reader, "FuenteCodigo");

            resultado.ContactoReferencia =
                ReadNullableString(
                    reader,
                    "ContactoReferencia");

            resultado.Observaciones =
                ReadNullableString(
                    reader,
                    "Observaciones");

            resultado.UsuarioCapturaID =
                ReadInt(
                    reader,
                    "UsuarioCapturaID");

            resultado.FechaCaptura =
                ReadDateTime(
                    reader,
                    "FechaCaptura");

            resultado.UsuarioModificacionID =
                ReadNullableInt(
                    reader,
                    "UsuarioModificacionID");

            resultado.FechaModificacion =
                ReadNullableDateTime(
                    reader,
                    "FechaModificacion");

            resultado.Activo =
                ReadBool(reader, "Activo");

            return resultado;
        }

        private static async Task<CoordinacionPrecioMercadoCerdoVm?>
            ObtenerPrecioMercadoCerdoActualAsync(
                SqlConnection connection,
                DateTime fecha,
                CancellationToken cancellationToken)
        {
            await using var command =
                new SqlCommand(
                    "dbo.usp_Coordinacion_PrecioMercadoCerdo_ObtenerActual",
                    connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

            command.Parameters.Add(
                "@Fecha",
                SqlDbType.Date).Value = fecha.Date;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new CoordinacionPrecioMercadoCerdoVm
            {
                PrecioMercadoCerdoID =
                    ReadInt(
                        reader,
                        "PrecioMercadoCerdoID"),

                Fecha =
                    ReadDateTime(
                        reader,
                        "Fecha"),

                PrecioKg =
                    ReadDecimal(
                        reader,
                        "PrecioKg"),

                FuenteCodigo =
                    ReadString(
                        reader,
                        "FuenteCodigo"),

                ContactoReferencia =
                    ReadNullableString(
                        reader,
                        "ContactoReferencia"),

                Observaciones =
                    ReadNullableString(
                        reader,
                        "Observaciones"),

                UsuarioCapturaID =
                    ReadInt(
                        reader,
                        "UsuarioCapturaID"),

                FechaCaptura =
                    ReadDateTime(
                        reader,
                        "FechaCaptura"),

                UsuarioModificacionID =
                    ReadNullableInt(
                        reader,
                        "UsuarioModificacionID"),

                FechaModificacion =
                    ReadNullableDateTime(
                        reader,
                        "FechaModificacion"),

                EsPrecioDelDia =
                    ReadBool(
                        reader,
                        "EsPrecioDelDia"),

                Activo = true
            };
        }

        private static async Task<List<CoordinacionPrecioMercadoCerdoHistoricoVm>>
            ObtenerHistoricoPrecioMercadoCerdoAsync(
                SqlConnection connection,
                CoordinacionPrecioMercadoCerdoFiltroVm filtros,
                CancellationToken cancellationToken)
        {
            var resultado =
                new List<CoordinacionPrecioMercadoCerdoHistoricoVm>();

            await using var command =
                new SqlCommand(
                    "dbo.usp_Coordinacion_PrecioMercadoCerdo_ObtenerHistorico",
                    connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 90
                };

            AddNullableInt(
                command,
                "@Anio",
                filtros.Anio);

            AddNullableInt(
                command,
                "@Mes",
                filtros.Mes);

            AddNullableDate(
                command,
                "@FechaDesde",
                filtros.FechaDesde);

            AddNullableDate(
                command,
                "@FechaHasta",
                filtros.FechaHasta);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(
                    new CoordinacionPrecioMercadoCerdoHistoricoVm
                    {
                        PrecioMercadoCerdoID =
                            ReadInt(
                                reader,
                                "PrecioMercadoCerdoID"),

                        Fecha =
                            ReadDateTime(
                                reader,
                                "Fecha"),

                        DiaSemana =
                            ReadString(
                                reader,
                                "DiaSemana"),

                        PrecioKg =
                            ReadDecimal(
                                reader,
                                "PrecioKg"),

                        PrecioAnterior =
                            ReadNullableDecimal(
                                reader,
                                "PrecioAnterior"),

                        VariacionPrecio =
                            ReadNullableDecimal(
                                reader,
                                "VariacionPrecio"),

                        VariacionPorcentaje =
                            ReadNullableDecimal(
                                reader,
                                "VariacionPorcentaje"),

                        FuenteCodigo =
                            ReadString(
                                reader,
                                "FuenteCodigo"),

                        ContactoReferencia =
                            ReadNullableString(
                                reader,
                                "ContactoReferencia"),

                        Observaciones =
                            ReadNullableString(
                                reader,
                                "Observaciones"),

                        UsuarioCapturaID =
                            ReadInt(
                                reader,
                                "UsuarioCapturaID"),

                        FechaCaptura =
                            ReadDateTime(
                                reader,
                                "FechaCaptura"),

                        UsuarioModificacionID =
                            ReadNullableInt(
                                reader,
                                "UsuarioModificacionID"),

                        FechaModificacion =
                            ReadNullableDateTime(
                                reader,
                                "FechaModificacion")
                    });
            }

            return resultado;
        }

        private static async Task<CoordinacionPrecioMercadoCerdoResumenVm>
            ObtenerResumenPrecioMercadoCerdoAsync(
                SqlConnection connection,
                int anio,
                CancellationToken cancellationToken)
        {
            await using var command =
                new SqlCommand(
                    "dbo.usp_Coordinacion_PrecioMercadoCerdo_ObtenerResumen",
                    connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

            command.Parameters.Add(
                "@Anio",
                SqlDbType.Int).Value = anio;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var resultado =
                new CoordinacionPrecioMercadoCerdoResumenVm
                {
                    Anio = anio
                };

            if (!await reader.ReadAsync(cancellationToken))
                return resultado;

            resultado.Anio =
                ReadInt(reader, "Anio");

            resultado.DiasCapturados =
                ReadInt(
                    reader,
                    "DiasCapturados");

            resultado.PrecioMinimo =
                ReadNullableDecimal(
                    reader,
                    "PrecioMinimo");

            resultado.PrecioMaximo =
                ReadNullableDecimal(
                    reader,
                    "PrecioMaximo");

            resultado.PrecioPromedio =
                ReadNullableDecimal(
                    reader,
                    "PrecioPromedio");

            resultado.PrecioInicioAnio =
                ReadNullableDecimal(
                    reader,
                    "PrecioInicioAnio");

            resultado.PrecioUltimo =
                ReadNullableDecimal(
                    reader,
                    "PrecioUltimo");

            resultado.FechaUltimoPrecio =
                ReadNullableDateTime(
                    reader,
                    "FechaUltimoPrecio");

            return resultado;
        }

        private static async Task<List<int>>
            ObtenerAniosPrecioMercadoCerdoAsync(
                SqlConnection connection,
                CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT DISTINCT
    YEAR(Fecha) AS Anio
FROM dbo.Coordinacion_PrecioMercadoCerdo
WHERE Activo=1
ORDER BY Anio DESC;";

            var resultado = new List<int>();

            await using var command =
                new SqlCommand(sql, connection)
                {
                    CommandTimeout = 60
                };

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                    resultado.Add(
                        Convert.ToInt32(reader.GetValue(0)));
            }

            return resultado;
        }


        private static string? LimpiarPrecioTexto(string? value, int maxLength)
        {
            var texto = Limpiar(value);
            if (string.IsNullOrWhiteSpace(texto)) return null;
            if (maxLength <= 0 || texto.Length <= maxLength) return texto;
            return texto.Substring(0, maxLength);
        }

        private static async Task<List<CoordinacionPrecioMercadoCerdoHistorialCambioVm>>
            ObtenerHistorialCambiosPrecioMercadoCerdoAsync(
                SqlConnection connection,
                int precioMercadoCerdoId,
                CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT
    H.PrecioMercadoCerdoHistorialID,
    H.PrecioMercadoCerdoID,
    H.Fecha,
    H.PrecioAnterior,
    H.PrecioNuevo,
    H.Motivo,
    H.UsuarioID,
    H.FechaMovimiento
FROM dbo.Coordinacion_PrecioMercadoCerdoHistorial H
WHERE H.PrecioMercadoCerdoID=@PrecioMercadoCerdoID
ORDER BY
    H.FechaMovimiento DESC,
    H.PrecioMercadoCerdoHistorialID DESC;";

            var resultado =
                new List<CoordinacionPrecioMercadoCerdoHistorialCambioVm>();

            await using var command =
                new SqlCommand(sql, connection)
                {
                    CommandTimeout = 60
                };

            command.Parameters.Add(
                "@PrecioMercadoCerdoID",
                SqlDbType.Int).Value = precioMercadoCerdoId;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(
                    new CoordinacionPrecioMercadoCerdoHistorialCambioVm
                    {
                        PrecioMercadoCerdoHistorialID =
                            ReadLong(
                                reader,
                                "PrecioMercadoCerdoHistorialID"),

                        PrecioMercadoCerdoID =
                            ReadInt(
                                reader,
                                "PrecioMercadoCerdoID"),

                        Fecha =
                            ReadDateTime(
                                reader,
                                "Fecha"),

                        PrecioAnterior =
                            ReadNullableDecimal(
                                reader,
                                "PrecioAnterior"),

                        PrecioNuevo =
                            ReadDecimal(
                                reader,
                                "PrecioNuevo"),

                        Motivo =
                            ReadNullableString(
                                reader,
                                "Motivo"),

                        UsuarioID =
                            ReadInt(
                                reader,
                                "UsuarioID"),

                        FechaMovimiento =
                            ReadDateTime(
                                reader,
                                "FechaMovimiento")
                    });
            }

            return resultado;
        }
    }
}