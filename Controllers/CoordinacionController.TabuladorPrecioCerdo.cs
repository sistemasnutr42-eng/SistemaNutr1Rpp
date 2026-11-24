using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        private const string UrlTabuladorPrecioCerdo = "/Coordinacion/TabuladorPrecioCerdo";

        [HttpGet]
        public async Task<IActionResult> TabuladorPrecioCerdo(
            int? pedidoId,
            int? pedidoDetalleId,
            DateTime? fecha,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlTabuladorPrecioCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            var fechaCotizacion = fecha?.Date ?? DateTime.Today;

            await using var connection =
                await AbrirAsync(cancellationToken);

            var configuracion =
                await ObtenerConfiguracionTabuladorPrecioCerdoVigenteAsync(
                    connection,
                    fechaCotizacion,
                    cancellationToken);

            var precioMercado =
                await ObtenerPrecioMercadoCerdoActualAsync(
                    connection,
                    fechaCotizacion,
                    cancellationToken);

            var versiones =
                await ObtenerVersionesTabuladorPrecioCerdoAsync(
                    connection,
                    cancellationToken);

            var model =
                new CoordinacionTabuladorPrecioCerdoPaginaVm
                {
                    Configuracion =
                        configuracion
                        ?? CrearConfiguracionTabuladorPredeterminada(
                            fechaCotizacion),

                    PrecioMercadoActual = precioMercado,

                    Versiones = versiones,

                    Captura =
                        new CoordinacionTabuladorPrecioCerdoCapturaVm
                        {
                            PedidoID = pedidoId,
                            PedidoDetalleID = pedidoDetalleId,
                            FechaCotizacion = fechaCotizacion,
                            PesoOfrecidoKg =
                                configuracion?.PesoBaseKg
                                ?? 115m
                        }
                };

            if (configuracion != null)
            {
                model.Filas =
                    GenerarFilasTabuladorPrecioCerdo(
                        configuracion,
                        precioMercado?.PrecioKg);
            }

            if (pedidoId.HasValue &&
                pedidoId.Value > 0)
            {
                var datosPedido =
                    await ObtenerDatosPedidoRastroParaTabuladorAsync(
                        connection,
                        pedidoId.Value,
                        pedidoDetalleId,
                        cancellationToken);

                if (datosPedido != null)
                {
                    model.Captura.NumeroCerdos =
                        datosPedido.Value.NumeroCerdos;

                    model.Captura.PesoOfrecidoKg =
                        datosPedido.Value.PesoObjetivoKg > 0
                            ? datosPedido.Value.PesoObjetivoKg
                            : model.Configuracion.PesoBaseKg;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CalcularTabuladorPrecioCerdo([FromBody] CoordinacionTabuladorPrecioCerdoCalculoRequestVm model, CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(UrlTabuladorPrecioCerdo, "Ver", cancellationToken);
            if (acceso != null) return acceso;
            if (model == null) return BadRequest(new { ok = false, mensaje = "No se recibieron datos para realizar el cálculo." });
            if (model.NumeroCerdos <= 0) return BadRequest(new { ok = false, mensaje = "El número de cerdos debe ser mayor a cero." });
            if (model.PesoOfrecidoKg <= 0) return BadRequest(new { ok = false, mensaje = "El peso ofrecido debe ser mayor a cero." });
            if (model.FleteTotal < 0) return BadRequest(new { ok = false, mensaje = "El flete no puede ser negativo." });
            if (model.NumeroCerdosReales.HasValue && model.NumeroCerdosReales.Value < 0) return BadRequest(new { ok = false, mensaje = "El número de animales entregados no es válido." });
            var fechaCotizacion = model.FechaCotizacion?.Date ?? DateTime.Today;
            try
            {
                await using var connection = await AbrirAsync(cancellationToken);
                var configuracion = await ObtenerConfiguracionTabuladorPrecioCerdoVigenteAsync(connection, fechaCotizacion, cancellationToken);
                if (configuracion == null) return BadRequest(new { ok = false, mensaje = "No existe una configuración vigente para el tabulador." });
                var precioMercado = await ObtenerPrecioMercadoCerdoActualAsync(connection, fechaCotizacion, cancellationToken);
                var resultado = CalcularResultadoTabuladorPrecioCerdo(configuracion, model, precioMercado);
                var filas = GenerarFilasTabuladorPrecioCerdo(configuracion, precioMercado?.PrecioKg, resultado.FletePorKgEstimado);
                return Json(new CoordinacionTabuladorPrecioCerdoCalculoResponseVm
                {
                    Ok = true,
                    Mensaje = "Cálculo realizado correctamente.",
                    Resultado = resultado,
                    Filas = filas
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new { ok = false, mensaje = LimpiarMensajeSql(ex.Message) });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible realizar el cálculo del tabulador.", detalle = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarConfiguracionTabuladorPrecioCerdo(
            [FromBody] CoordinacionTabuladorPrecioCerdoGuardarConfiguracionVm model,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlTabuladorPrecioCerdo,
                "Editar",
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!UsuarioID.HasValue ||
                UsuarioID.Value <= 0)
            {
                return Unauthorized();
            }

            if (model == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "No se recibió la configuración."
                });
            }

            model.Codigo =
                Limpiar(model.Codigo, 50)
                ?? string.Empty;

            model.Nombre =
                Limpiar(model.Nombre, 150)
                ?? string.Empty;

            model.MonedaCodigo =
                Limpiar(model.MonedaCodigo, 10)
                ?? "MXN";

            if (string.IsNullOrWhiteSpace(model.Codigo))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El código del tabulador es obligatorio."
                });
            }

            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El nombre del tabulador es obligatorio."
                });
            }

            if (model.PesoBaseKg <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El peso base debe ser mayor a cero."
                });
            }

            if (model.PrecioBaseDestinoKg <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El precio base debe ser mayor a cero."
                });
            }

            if (model.AjustePorKg < 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El ajuste por kilogramo no puede ser negativo."
                });
            }

            if (model.PesoMinimoKg <= 0 ||
                model.PesoMaximoKg <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Los pesos mínimo y máximo deben ser mayores a cero."
                });
            }

            if (model.PesoMaximoKg <
                model.PesoMinimoKg)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El peso máximo no puede ser menor al peso mínimo."
                });
            }

            if (model.VigenciaHasta.HasValue &&
                model.VigenciaHasta.Value.Date <
                model.VigenciaDesde.Date)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La vigencia final no puede ser anterior a la vigencia inicial."
                });
            }

            try
            {
                await using var connection =
                    await AbrirAsync(cancellationToken);

                await using var transaction =
                    (SqlTransaction)await connection
                        .BeginTransactionAsync(
                            IsolationLevel.Serializable,
                            cancellationToken);

                try
                {
                    var id =
                        await GuardarNuevaVersionConfiguracionTabuladorAsync(
                            connection,
                            transaction,
                            model,
                            UsuarioID.Value,
                            cancellationToken);

                    await transaction.CommitAsync(
                        cancellationToken);

                    return Json(new
                    {
                        ok = true,
                        mensaje = "La configuración del tabulador se guardó correctamente.",
                        tabuladorPrecioCerdoID = id
                    });
                }
                catch
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    throw;
                }
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
                        mensaje = "No fue posible guardar la configuración.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarCotizacionRastro(
            [FromBody] CoordinacionTabuladorPrecioCerdoCapturaVm model,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlTabuladorPrecioCerdo,
                "Editar",
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!UsuarioID.HasValue ||
                UsuarioID.Value <= 0)
            {
                return Unauthorized();
            }

            if (model == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "No se recibieron los datos de la cotización."
                });
            }

            if (!model.PedidoID.HasValue ||
                model.PedidoID.Value <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La cotización debe estar relacionada con un pedido de Rastro."
                });
            }

            if (model.NumeroCerdos <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El número de cerdos debe ser mayor a cero."
                });
            }

            if (model.PesoOfrecidoKg <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El peso ofrecido debe ser mayor a cero."
                });
            }

            model.Observaciones =
                Limpiar(model.Observaciones, 500);

            try
            {
                await using var connection =
                    await AbrirAsync(cancellationToken);

                var fechaCotizacion =
                    model.FechaCotizacion.Date;

                var configuracion =
                    await ObtenerConfiguracionTabuladorPrecioCerdoVigenteAsync(
                        connection,
                        fechaCotizacion,
                        cancellationToken);

                if (configuracion == null)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "No existe una configuración vigente del tabulador."
                    });
                }

                var precioMercado =
                    await ObtenerPrecioMercadoCerdoActualAsync(
                        connection,
                        fechaCotizacion,
                        cancellationToken);

                var request =
                    new CoordinacionTabuladorPrecioCerdoCalculoRequestVm
                    {
                        PedidoID = model.PedidoID,
                        PedidoDetalleID = model.PedidoDetalleID,
                        FechaCotizacion = model.FechaCotizacion,
                        NumeroCerdos = model.NumeroCerdos,
                        PesoOfrecidoKg = model.PesoOfrecidoKg,
                        PesoRealKg = model.PesoRealKg,
                        NumeroCerdosReales = model.NumeroCerdosReales,
                        FleteTotal = model.FleteTotal
                    };

                var resultado =
                    CalcularResultadoTabuladorPrecioCerdo(
                        configuracion,
                        request,
                        precioMercado);

                resultado.Observaciones =
                    model.Observaciones;

                var cotizacionId =
                    await GuardarCotizacionRastroAsync(
                        connection,
                        resultado,
                        UsuarioID.Value,
                        cancellationToken);

                return Json(new
                {
                    ok = true,
                    mensaje = "La cotización de Rastro se guardó correctamente.",
                    cotizacionRastroID = cotizacionId,
                    resultado
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
                        mensaje = "No fue posible guardar la cotización.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerConfiguracionTabuladorPrecioCerdo(
            DateTime? fecha,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(
                UrlTabuladorPrecioCerdo,
                "Ver",
                cancellationToken);

            if (acceso != null)
                return acceso;

            var fechaConsulta =
                fecha?.Date
                ?? DateTime.Today;

            await using var connection =
                await AbrirAsync(cancellationToken);

            var configuracion =
                await ObtenerConfiguracionTabuladorPrecioCerdoVigenteAsync(
                    connection,
                    fechaConsulta,
                    cancellationToken);

            return Json(new
            {
                ok = true,
                configuracion
            });
        }

        private static CoordinacionTabuladorPrecioCerdoResultadoVm CalcularResultadoTabuladorPrecioCerdo(CoordinacionTabuladorPrecioCerdoConfiguracionVm configuracion, CoordinacionTabuladorPrecioCerdoCalculoRequestVm model, CoordinacionPrecioMercadoCerdoVm? precioMercado)
        {
            var kilosEstimados = model.NumeroCerdos * configuracion.PesoBaseKg;
            var fletePorCerdo = model.NumeroCerdos > 0 ? model.FleteTotal / model.NumeroCerdos : 0m;
            var fletePorKg = kilosEstimados > 0 ? model.FleteTotal / kilosEstimados : 0m;
            var precioOfrecidoDestino = CalcularPrecioDestinoPorPeso(configuracion, model.PesoOfrecidoKg);
            var precioOfrecidoOrigen = Math.Max(0m, precioOfrecidoDestino - fletePorKg);
            var precioAjustadoDestino = precioOfrecidoDestino;
            var precioAjustadoOrigen = precioOfrecidoOrigen;
            decimal? kilosReales = null;
            decimal? diferenciaPeso = null;
            if (model.PesoRealKg.HasValue && model.PesoRealKg.Value > 0)
            {
                diferenciaPeso = model.PesoRealKg.Value - model.PesoOfrecidoKg;
                precioAjustadoDestino = CalcularPrecioDestinoPorPeso(configuracion, model.PesoRealKg.Value);
                precioAjustadoOrigen = Math.Max(0m, precioAjustadoDestino - fletePorKg);
                var numeroReal = model.NumeroCerdosReales.HasValue && model.NumeroCerdosReales.Value > 0 ? model.NumeroCerdosReales.Value : model.NumeroCerdos;
                kilosReales = numeroReal * model.PesoRealKg.Value;
            }
            var importeDestinoAjustado = precioAjustadoDestino * kilosEstimados;
            var importeOrigenAjustado = precioAjustadoOrigen * kilosEstimados;
            var importeDestinoOfrecido = precioOfrecidoDestino * kilosEstimados;
            var importeOrigenOfrecido = precioOfrecidoOrigen * kilosEstimados;
            var ajusteTotal = (precioAjustadoDestino - precioOfrecidoDestino) * kilosEstimados;
            decimal? diferenciaMercado = null;
            decimal? diferenciaMercadoPorcentaje = null;
            if (precioMercado != null && precioMercado.PrecioKg > 0)
            {
                diferenciaMercado = precioAjustadoDestino - precioMercado.PrecioKg;
                diferenciaMercadoPorcentaje = diferenciaMercado.Value / precioMercado.PrecioKg * 100m;
            }
            return new CoordinacionTabuladorPrecioCerdoResultadoVm
            {
                PedidoID = model.PedidoID,
                PedidoDetalleID = model.PedidoDetalleID,
                TabuladorPrecioCerdoID = configuracion.TabuladorPrecioCerdoID,
                FechaCotizacion = model.FechaCotizacion?.Date ?? DateTime.Today,
                NumeroCerdos = model.NumeroCerdos,
                PesoBaseKg = configuracion.PesoBaseKg,
                PesoOfrecidoKg = model.PesoOfrecidoKg,
                PesoRealKg = model.PesoRealKg,
                NumeroCerdosReales = model.NumeroCerdosReales,
                PrecioBaseDestinoKg = configuracion.PrecioBaseDestinoKg,
                AjustePorKg = configuracion.AjustePorKg,
                FleteTotal = RedondearDinero(model.FleteTotal),
                KilosEstimados = RedondearPeso(kilosEstimados),
                KilosReales = kilosReales.HasValue ? RedondearPeso(kilosReales.Value) : null,
                FletePorCerdo = RedondearDinero(fletePorCerdo),
                FletePorKgEstimado = RedondearPrecio(fletePorKg),
                FletePorKgReal = model.PesoRealKg.HasValue ? RedondearPrecio(fletePorKg) : null,
                PrecioDestinoCalculadoKg = RedondearPrecio(precioOfrecidoDestino),
                PrecioOrigenCalculadoKg = RedondearPrecio(precioOfrecidoOrigen),
                PrecioDestinoRealKg = model.PesoRealKg.HasValue ? RedondearPrecio(precioAjustadoDestino) : null,
                PrecioOrigenRealKg = model.PesoRealKg.HasValue ? RedondearPrecio(precioAjustadoOrigen) : null,
                ImporteDestinoEstimado = RedondearDinero(importeDestinoAjustado),
                ImporteOrigenEstimado = RedondearDinero(importeOrigenAjustado),
                ImporteDestinoReal = model.PesoRealKg.HasValue ? RedondearDinero(importeDestinoAjustado) : null,
                ImporteOrigenReal = model.PesoRealKg.HasValue ? RedondearDinero(importeOrigenAjustado) : null,
                PrecioMercadoKg = precioMercado?.PrecioKg,
                FechaPrecioMercado = precioMercado?.Fecha,
                PrecioMercadoEsDelDia = precioMercado?.EsPrecioDelDia ?? false,
                DiferenciaVsMercadoKg = diferenciaMercado.HasValue ? RedondearPrecio(diferenciaMercado.Value) : null,
                DiferenciaVsMercadoPorcentaje = diferenciaMercadoPorcentaje.HasValue ? Math.Round(diferenciaMercadoPorcentaje.Value, 2, MidpointRounding.AwayFromZero) : null
            };
        }
        private static decimal CalcularPrecioDestinoPorPeso(CoordinacionTabuladorPrecioCerdoConfiguracionVm configuracion, decimal pesoKg)
        {
            if (pesoKg >= configuracion.PesoBaseKg) return configuracion.PrecioBaseDestinoKg;
            var diferencia = configuracion.PesoBaseKg - pesoKg;
            var ajuste = diferencia * configuracion.AjustePorKg;
            return Math.Max(0m, configuracion.PrecioBaseDestinoKg - ajuste);
        }

        private static List<CoordinacionTabuladorPrecioCerdoFilaVm> GenerarFilasTabuladorPrecioCerdo(CoordinacionTabuladorPrecioCerdoConfiguracionVm configuracion, decimal? precioMercadoKg, decimal fletePorKg = 0m)
        {
            var resultado = new List<CoordinacionTabuladorPrecioCerdoFilaVm>();
            var pesoInicial = Math.Ceiling(configuracion.PesoMinimoKg);
            var pesoFinal = Math.Floor(configuracion.PesoMaximoKg);
            if (pesoFinal < pesoInicial) return resultado;
            for (var peso = pesoInicial; peso <= pesoFinal; peso += 1m)
            {
                var precioDestino = CalcularPrecioDestinoPorPeso(configuracion, peso);
                var precioOrigen = Math.Max(0m, precioDestino - fletePorKg);
                var diferenciaPeso = peso - configuracion.PesoBaseKg;
                var ajusteTotal = precioDestino - configuracion.PrecioBaseDestinoKg;
                resultado.Add(new CoordinacionTabuladorPrecioCerdoFilaVm
                {
                    PesoKg = peso,
                    DiferenciaPesoKg = diferenciaPeso,
                    AjusteTotal = RedondearPrecio(ajusteTotal),
                    PrecioDestinoKg = RedondearPrecio(precioDestino),
                    PrecioOrigenKg = RedondearPrecio(precioOrigen),
                    PrecioMercadoKg = precioMercadoKg,
                    DiferenciaDestinoMercadoKg = precioMercadoKg.HasValue ? RedondearPrecio(precioDestino - precioMercadoKg.Value) : null,
                    DiferenciaOrigenMercadoKg = precioMercadoKg.HasValue ? RedondearPrecio(precioOrigen - precioMercadoKg.Value) : null,
                    EsPesoBase = peso == configuracion.PesoBaseKg,
                    EstaDebajoPesoBase = peso < configuracion.PesoBaseKg,
                    EstaSobrePesoBase = peso > configuracion.PesoBaseKg
                });
            }
            return resultado;
        }

        private static async Task<CoordinacionTabuladorPrecioCerdoConfiguracionVm?>
            ObtenerConfiguracionTabuladorPrecioCerdoVigenteAsync(
                SqlConnection connection,
                DateTime fecha,
                CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1)
    T.TabuladorPrecioCerdoID,
    T.Codigo,
    T.Nombre,
    T.VigenciaDesde,
    T.VigenciaHasta,
    T.PesoBaseKg,
    T.PrecioBaseDestinoKg,
    T.AjustePorKg,
    T.PesoMinimoKg,
    T.PesoMaximoKg,
    T.AjustarSobrePesoBase,
    T.MonedaCodigo,
    T.Activo,
    T.UsuarioCreacionID,
    T.FechaCreacion,
    T.UsuarioModificacionID,
    T.FechaModificacion
FROM dbo.Coordinacion_TabuladorPrecioCerdo T
WHERE T.Activo=1
  AND T.VigenciaDesde<=@Fecha
  AND
  (
      T.VigenciaHasta IS NULL
      OR T.VigenciaHasta>=@Fecha
  )
ORDER BY
    T.VigenciaDesde DESC,
    T.TabuladorPrecioCerdoID DESC;";

            await using var command =
                new SqlCommand(
                    sql,
                    connection)
                {
                    CommandTimeout = 60
                };

            command.Parameters.Add(
                "@Fecha",
                SqlDbType.Date).Value =
                fecha.Date;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(
                    cancellationToken))
            {
                return null;
            }

            return LeerConfiguracionTabulador(
                reader);
        }

        private static async Task<List<CoordinacionTabuladorPrecioCerdoConfiguracionVm>>
            ObtenerVersionesTabuladorPrecioCerdoAsync(
                SqlConnection connection,
                CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT
    T.TabuladorPrecioCerdoID,
    T.Codigo,
    T.Nombre,
    T.VigenciaDesde,
    T.VigenciaHasta,
    T.PesoBaseKg,
    T.PrecioBaseDestinoKg,
    T.AjustePorKg,
    T.PesoMinimoKg,
    T.PesoMaximoKg,
    T.AjustarSobrePesoBase,
    T.MonedaCodigo,
    T.Activo,
    T.UsuarioCreacionID,
    T.FechaCreacion,
    T.UsuarioModificacionID,
    T.FechaModificacion
FROM dbo.Coordinacion_TabuladorPrecioCerdo T
ORDER BY
    T.VigenciaDesde DESC,
    T.TabuladorPrecioCerdoID DESC;";

            var resultado =
                new List<CoordinacionTabuladorPrecioCerdoConfiguracionVm>();

            await using var command =
                new SqlCommand(
                    sql,
                    connection)
                {
                    CommandTimeout = 60
                };

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                resultado.Add(
                    LeerConfiguracionTabulador(
                        reader));
            }

            return resultado;
        }

        private static CoordinacionTabuladorPrecioCerdoConfiguracionVm
            LeerConfiguracionTabulador(
                SqlDataReader reader)
        {
            return new CoordinacionTabuladorPrecioCerdoConfiguracionVm
            {
                TabuladorPrecioCerdoID =
                    ReadInt(
                        reader,
                        "TabuladorPrecioCerdoID"),

                Codigo =
                    ReadString(
                        reader,
                        "Codigo"),

                Nombre =
                    ReadString(
                        reader,
                        "Nombre"),

                VigenciaDesde =
                    ReadDateTime(
                        reader,
                        "VigenciaDesde"),

                VigenciaHasta =
                    ReadNullableDateTime(
                        reader,
                        "VigenciaHasta"),

                PesoBaseKg =
                    ReadDecimal(
                        reader,
                        "PesoBaseKg"),

                PrecioBaseDestinoKg =
                    ReadDecimal(
                        reader,
                        "PrecioBaseDestinoKg"),

                AjustePorKg =
                    ReadDecimal(
                        reader,
                        "AjustePorKg"),

                PesoMinimoKg =
                    ReadDecimal(
                        reader,
                        "PesoMinimoKg"),

                PesoMaximoKg =
                    ReadDecimal(
                        reader,
                        "PesoMaximoKg"),

                AjustarSobrePesoBase =
                    ReadBool(
                        reader,
                        "AjustarSobrePesoBase"),

                MonedaCodigo =
                    ReadString(
                        reader,
                        "MonedaCodigo"),

                Activo =
                    ReadBool(
                        reader,
                        "Activo"),

                UsuarioCreacionID =
                    ReadInt(
                        reader,
                        "UsuarioCreacionID"),

                FechaCreacion =
                    ReadDateTime(
                        reader,
                        "FechaCreacion"),

                UsuarioModificacionID =
                    ReadNullableInt(
                        reader,
                        "UsuarioModificacionID"),

                FechaModificacion =
                    ReadNullableDateTime(
                        reader,
                        "FechaModificacion")
            };
        }

        private static async Task<int>
            GuardarNuevaVersionConfiguracionTabuladorAsync(
                SqlConnection connection,
                SqlTransaction transaction,
                CoordinacionTabuladorPrecioCerdoGuardarConfiguracionVm model,
                int usuarioId,
                CancellationToken cancellationToken)
        {
            const string sqlCerrar = @"
UPDATE dbo.Coordinacion_TabuladorPrecioCerdo
SET
    VigenciaHasta=DATEADD(DAY,-1,@VigenciaDesde),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE Activo=1
  AND VigenciaDesde<@VigenciaDesde
  AND
  (
      VigenciaHasta IS NULL
      OR VigenciaHasta>=@VigenciaDesde
  );";

            await using (var cerrar =
                new SqlCommand(
                    sqlCerrar,
                    connection,
                    transaction))
            {
                cerrar.Parameters.Add(
                    "@VigenciaDesde",
                    SqlDbType.Date).Value =
                    model.VigenciaDesde.Date;

                cerrar.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioId;

                await cerrar.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            const string sqlInsertar = @"
INSERT INTO dbo.Coordinacion_TabuladorPrecioCerdo
(
    Codigo,
    Nombre,
    VigenciaDesde,
    VigenciaHasta,
    PesoBaseKg,
    PrecioBaseDestinoKg,
    AjustePorKg,
    PesoMinimoKg,
    PesoMaximoKg,
    AjustarSobrePesoBase,
    MonedaCodigo,
    Activo,
    UsuarioCreacionID,
    FechaCreacion
)
VALUES
(
    @Codigo,
    @Nombre,
    @VigenciaDesde,
    @VigenciaHasta,
    @PesoBaseKg,
    @PrecioBaseDestinoKg,
    @AjustePorKg,
    @PesoMinimoKg,
    @PesoMaximoKg,
    @AjustarSobrePesoBase,
    @MonedaCodigo,
    @Activo,
    @UsuarioID,
    SYSDATETIME()
);

SELECT CONVERT(INT,SCOPE_IDENTITY());";

            await using var insertar =
                new SqlCommand(
                    sqlInsertar,
                    connection,
                    transaction);

            insertar.Parameters.Add(
                "@Codigo",
                SqlDbType.VarChar,
                50).Value =
                model.Codigo;

            insertar.Parameters.Add(
                "@Nombre",
                SqlDbType.NVarChar,
                150).Value =
                model.Nombre;

            insertar.Parameters.Add(
                "@VigenciaDesde",
                SqlDbType.Date).Value =
                model.VigenciaDesde.Date;

            var pHasta =
                insertar.Parameters.Add(
                    "@VigenciaHasta",
                    SqlDbType.Date);

            pHasta.Value =
                model.VigenciaHasta.HasValue
                    ? model.VigenciaHasta.Value.Date
                    : DBNull.Value;

            AgregarDecimal(
                insertar,
                "@PesoBaseKg",
                model.PesoBaseKg,
                10,
                2);

            AgregarDecimal(
                insertar,
                "@PrecioBaseDestinoKg",
                model.PrecioBaseDestinoKg,
                10,
                2);

            AgregarDecimal(
                insertar,
                "@AjustePorKg",
                model.AjustePorKg,
                10,
                2);

            AgregarDecimal(
                insertar,
                "@PesoMinimoKg",
                model.PesoMinimoKg,
                10,
                2);

            AgregarDecimal(
                insertar,
                "@PesoMaximoKg",
                model.PesoMaximoKg,
                10,
                2);

            insertar.Parameters.Add(
                "@AjustarSobrePesoBase",
                SqlDbType.Bit).Value =
                model.AjustarSobrePesoBase;

            insertar.Parameters.Add(
                "@MonedaCodigo",
                SqlDbType.VarChar,
                10).Value =
                model.MonedaCodigo;

            insertar.Parameters.Add(
                "@Activo",
                SqlDbType.Bit).Value =
                model.Activo;

            insertar.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            var resultado =
                await insertar.ExecuteScalarAsync(
                    cancellationToken);

            return Convert.ToInt32(
                resultado);
        }

        private static async Task<int>
            GuardarCotizacionRastroAsync(
                SqlConnection connection,
                CoordinacionTabuladorPrecioCerdoResultadoVm model,
                int usuarioId,
                CancellationToken cancellationToken)
        {
            const string sql = @"
INSERT INTO dbo.Coordinacion_CotizacionRastro
(
    PedidoID,
    PedidoDetalleID,
    TabuladorPrecioCerdoID,
    FechaCotizacion,
    NumeroCerdos,
    NumeroCerdosReales,
    PesoOfrecidoKg,
    PesoRealKg,
    FleteTotal,
    FletePorCerdo,
    FletePorKgEstimado,
    FletePorKgReal,
    PrecioBaseDestinoKg,
    AjustePorKg,
    PrecioDestinoCalculadoKg,
    PrecioOrigenCalculadoKg,
    PrecioDestinoRealKg,
    PrecioOrigenRealKg,
    KilosEstimados,
    KilosReales,
    ImporteDestinoEstimado,
    ImporteOrigenEstimado,
    ImporteDestinoReal,
    ImporteOrigenReal,
    PrecioMercadoKg,
    FechaPrecioMercado,
    Observaciones,
    EstatusCodigo,
    UsuarioCreacionID,
    FechaCreacion
)
VALUES
(
    @PedidoID,
    @PedidoDetalleID,
    @TabuladorPrecioCerdoID,
    @FechaCotizacion,
    @NumeroCerdos,
    @NumeroCerdosReales,
    @PesoOfrecidoKg,
    @PesoRealKg,
    @FleteTotal,
    @FletePorCerdo,
    @FletePorKgEstimado,
    @FletePorKgReal,
    @PrecioBaseDestinoKg,
    @AjustePorKg,
    @PrecioDestinoCalculadoKg,
    @PrecioOrigenCalculadoKg,
    @PrecioDestinoRealKg,
    @PrecioOrigenRealKg,
    @KilosEstimados,
    @KilosReales,
    @ImporteDestinoEstimado,
    @ImporteOrigenEstimado,
    @ImporteDestinoReal,
    @ImporteOrigenReal,
    @PrecioMercadoKg,
    @FechaPrecioMercado,
    @Observaciones,
    'COTIZADA',
    @UsuarioID,
    SYSDATETIME()
);

SELECT CONVERT(INT,SCOPE_IDENTITY());";

            await using var command =
                new SqlCommand(
                    sql,
                    connection)
                {
                    CommandTimeout = 60
                };

            command.Parameters.Add(
                "@PedidoID",
                SqlDbType.Int).Value =
                model.PedidoID!.Value;

            var pDetalle =
                command.Parameters.Add(
                    "@PedidoDetalleID",
                    SqlDbType.Int);

            pDetalle.Value =
                model.PedidoDetalleID.HasValue
                    ? model.PedidoDetalleID.Value
                    : DBNull.Value;

            command.Parameters.Add(
                "@TabuladorPrecioCerdoID",
                SqlDbType.Int).Value =
                model.TabuladorPrecioCerdoID;

            command.Parameters.Add(
                "@FechaCotizacion",
                SqlDbType.Date).Value =
                model.FechaCotizacion.Date;

            command.Parameters.Add(
                "@NumeroCerdos",
                SqlDbType.Int).Value =
                model.NumeroCerdos;

            var pNumeroReal =
                command.Parameters.Add(
                    "@NumeroCerdosReales",
                    SqlDbType.Int);

            pNumeroReal.Value =
                model.NumeroCerdosReales.HasValue
                    ? model.NumeroCerdosReales.Value
                    : DBNull.Value;

            AgregarDecimal(
                command,
                "@PesoOfrecidoKg",
                model.PesoOfrecidoKg,
                10,
                2);

            AgregarNullableDecimal(
                command,
                "@PesoRealKg",
                model.PesoRealKg,
                10,
                2);

            AgregarDecimal(
                command,
                "@FleteTotal",
                model.FleteTotal,
                18,
                2);

            AgregarDecimal(
                command,
                "@FletePorCerdo",
                model.FletePorCerdo,
                18,
                4);

            AgregarDecimal(
                command,
                "@FletePorKgEstimado",
                model.FletePorKgEstimado,
                18,
                4);

            AgregarNullableDecimal(
                command,
                "@FletePorKgReal",
                model.FletePorKgReal,
                18,
                4);

            AgregarDecimal(
                command,
                "@PrecioBaseDestinoKg",
                model.PrecioBaseDestinoKg,
                10,
                2);

            AgregarDecimal(
                command,
                "@AjustePorKg",
                model.AjustePorKg,
                10,
                2);

            AgregarDecimal(
                command,
                "@PrecioDestinoCalculadoKg",
                model.PrecioDestinoCalculadoKg,
                10,
                4);

            AgregarDecimal(
                command,
                "@PrecioOrigenCalculadoKg",
                model.PrecioOrigenCalculadoKg,
                10,
                4);

            AgregarNullableDecimal(
                command,
                "@PrecioDestinoRealKg",
                model.PrecioDestinoRealKg,
                10,
                4);

            AgregarNullableDecimal(
                command,
                "@PrecioOrigenRealKg",
                model.PrecioOrigenRealKg,
                10,
                4);

            AgregarDecimal(
                command,
                "@KilosEstimados",
                model.KilosEstimados,
                18,
                3);

            AgregarNullableDecimal(
                command,
                "@KilosReales",
                model.KilosReales,
                18,
                3);

            AgregarDecimal(
                command,
                "@ImporteDestinoEstimado",
                model.ImporteDestinoEstimado,
                18,
                2);

            AgregarDecimal(
                command,
                "@ImporteOrigenEstimado",
                model.ImporteOrigenEstimado,
                18,
                2);

            AgregarNullableDecimal(
                command,
                "@ImporteDestinoReal",
                model.ImporteDestinoReal,
                18,
                2);

            AgregarNullableDecimal(
                command,
                "@ImporteOrigenReal",
                model.ImporteOrigenReal,
                18,
                2);

            AgregarNullableDecimal(
                command,
                "@PrecioMercadoKg",
                model.PrecioMercadoKg,
                10,
                4);

            var pFechaMercado =
                command.Parameters.Add(
                    "@FechaPrecioMercado",
                    SqlDbType.Date);

            pFechaMercado.Value =
                model.FechaPrecioMercado.HasValue
                    ? model.FechaPrecioMercado.Value.Date
                    : DBNull.Value;

            AddNullableString(
                command,
                "@Observaciones",
                500,
                model.Observaciones);

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            var resultado =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            return Convert.ToInt32(
                resultado);
        }

        private static async Task<(int NumeroCerdos, decimal PesoObjetivoKg)?>
    ObtenerDatosPedidoRastroParaTabuladorAsync(
        SqlConnection connection,
        int pedidoId,
        int? pedidoDetalleId,
        CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1)
    CONVERT(
        INT,
        ISNULL(D.CantidadSolicitada, 0)
    ) AS NumeroCerdos,

    ISNULL(
        R.PesoPromedioObjetivoKg,
        CASE
            WHEN D.PesoMinimoKg IS NOT NULL
             AND D.PesoMaximoKg IS NOT NULL
                THEN (D.PesoMinimoKg + D.PesoMaximoKg) / 2.0

            WHEN D.PesoMaximoKg IS NOT NULL
                THEN D.PesoMaximoKg

            WHEN D.PesoMinimoKg IS NOT NULL
                THEN D.PesoMinimoKg

            ELSE 0
        END
    ) AS PesoObjetivoKg

FROM dbo.Nutri_Pedidos P

INNER JOIN dbo.Nutri_PedidoDetalle D
    ON D.PedidoID = P.PedidoID

INNER JOIN dbo.Nutri_TiposPedido TP
    ON TP.TipoPedidoID = P.TipoPedidoID

LEFT JOIN dbo.Nutri_PedidoDetalleRastro R
    ON R.PedidoDetalleID = D.PedidoDetalleID

WHERE P.PedidoID = @PedidoID
  AND TP.Codigo = 'RASTRO'
  AND P.Activo = 1
  AND D.Activo = 1
  AND
  (
      @PedidoDetalleID IS NULL
      OR D.PedidoDetalleID = @PedidoDetalleID
  )

ORDER BY
    CASE
        WHEN @PedidoDetalleID IS NOT NULL
         AND D.PedidoDetalleID = @PedidoDetalleID
            THEN 0
        ELSE 1
    END,
    D.Renglon,
    D.PedidoDetalleID;";

            await using var command =
                new SqlCommand(
                    sql,
                    connection)
                {
                    CommandTimeout = 60
                };

            command.Parameters.Add(
                "@PedidoID",
                SqlDbType.Int).Value =
                pedidoId;

            var pDetalle =
                command.Parameters.Add(
                    "@PedidoDetalleID",
                    SqlDbType.Int);

            pDetalle.Value =
                pedidoDetalleId.HasValue
                    ? pedidoDetalleId.Value
                    : DBNull.Value;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(
                    cancellationToken))
            {
                return null;
            }

            return
            (
                ReadInt(
                    reader,
                    "NumeroCerdos"),

                ReadDecimal(
                    reader,
                    "PesoObjetivoKg")
            );
        }

        private static CoordinacionTabuladorPrecioCerdoConfiguracionVm
            CrearConfiguracionTabuladorPredeterminada(
                DateTime fecha)
        {
            return new CoordinacionTabuladorPrecioCerdoConfiguracionVm
            {
                TabuladorPrecioCerdoID = 0,
                Codigo = "CERDO-BASE",
                Nombre = "Tabulador base de precio de cerdo",
                VigenciaDesde = fecha.Date,
                VigenciaHasta = null,
                PesoBaseKg = 115m,
                PrecioBaseDestinoKg = 29m,
                AjustePorKg = 0.20m,
                PesoMinimoKg = 70m,
                PesoMaximoKg = 115m,
                AjustarSobrePesoBase = false,
                MonedaCodigo = "MXN",
                Activo = true
            };
        }

        private static void AgregarDecimal(
            SqlCommand command,
            string nombre,
            decimal valor,
            byte precision,
            byte escala)
        {
            var parametro =
                command.Parameters.Add(
                    nombre,
                    SqlDbType.Decimal);

            parametro.Precision =
                precision;

            parametro.Scale =
                escala;

            parametro.Value =
                valor;
        }

        private static void AgregarNullableDecimal(
            SqlCommand command,
            string nombre,
            decimal? valor,
            byte precision,
            byte escala)
        {
            var parametro =
                command.Parameters.Add(
                    nombre,
                    SqlDbType.Decimal);

            parametro.Precision =
                precision;

            parametro.Scale =
                escala;

            parametro.Value =
                valor.HasValue
                    ? valor.Value
                    : DBNull.Value;
        }

        private static decimal RedondearPrecio(
            decimal valor)
        {
            return Math.Round(
                valor,
                4,
                MidpointRounding.AwayFromZero);
        }

        private static decimal RedondearDinero(
            decimal valor)
        {
            return Math.Round(
                valor,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static decimal RedondearPeso(
            decimal valor)
        {
            return Math.Round(
                valor,
                3,
                MidpointRounding.AwayFromZero);
        }
    }
}