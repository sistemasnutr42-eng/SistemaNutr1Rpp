using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaNutri.Controllers
{
    /*
     * CALENDARIO DE COORDINACION
     * Reemplaza al archivo anterior (renómbralo antes como CoordinacionController.Calendario.cs.bak).
     * Lee lo que se importa en "Programación semanal" (En revisión) y lo ya confirmado.
     */
    public partial class CoordinacionController
    {
        // Debe coincidir con SubMenus.UrlEnlace del submenú "Calendario" (SubMenuID = 2)
        private const string UrlCalendario = "/Coordinacion/Calendario";

        [HttpGet]
        public async Task<IActionResult> Calendario(CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlCalendario, "Ver", cancellationToken);
            if (acceso != null) return acceso;

            return View();
        }

        /// <summary>
        /// Eventos del calendario entre dos fechas. Si viene importacionId se muestra sólo esa carga.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ObtenerCalendario(
            DateTime desde,
            DateTime hasta,
            string? tipoPedidoCodigo = null,
            long? importacionId = null,
            CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlCalendario, "Ver", cancellationToken);
            if (acceso != null) return acceso;

            desde = desde.Date;
            hasta = hasta.Date;

            if (hasta < desde)
                return BadRequest(new { ok = false, mensaje = "La fecha final no puede ser anterior a la inicial." });

            if ((hasta - desde).TotalDays > 62)
                return BadRequest(new { ok = false, mensaje = "El calendario no puede consultar más de 62 días." });

            if (string.IsNullOrWhiteSpace(tipoPedidoCodigo) ||
                string.Equals(tipoPedidoCodigo.Trim(), "TODOS", StringComparison.OrdinalIgnoreCase))
            {
                tipoPedidoCodigo = null;
            }
            else
            {
                tipoPedidoCodigo = NormalizarTipoProgramacion(tipoPedidoCodigo);

                if (string.IsNullOrWhiteSpace(tipoPedidoCodigo))
                    return BadRequest(new { ok = false, mensaje = "El tipo de programación no es válido." });
            }

            try
            {
                var empresaId = ObtenerEmpresaID()!.Value;

                await using var connection = await AbrirAsync(cancellationToken);

                await using var command = new SqlCommand(
                    "dbo.usp_Coordinacion_Calendario_ObtenerEventos",
                    connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 90
                };

                command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
                command.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = desde;
                command.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = hasta;
                AddNullableString(command, "@TipoPedidoCodigo", 30, tipoPedidoCodigo);

                var pImportacion = command.Parameters.Add("@ImportacionID", SqlDbType.BigInt);
                pImportacion.Value = importacionId.HasValue ? importacionId.Value : DBNull.Value;

                var eventos = new List<Dictionary<string, object?>>();

                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                        eventos.Add(LeerFilaDinamica(reader));
                }

                return Json(new
                {
                    ok = true,
                    desde,
                    hasta,
                    importacionId,
                    total = eventos.Count,
                    eventos
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new { ok = false, mensaje = LimpiarMensajeSql(ex.Message) });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { ok = false, mensaje = "No fue posible cargar el calendario.", detalle = ex.Message });
            }
        }

        /// <summary>
        /// Historial de cargas de Excel que tocan el rango (fecha, usuario, archivo, totales, estatus).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ObtenerCargasCalendario(
            DateTime desde,
            DateTime hasta,
            CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlCalendario, "Ver", cancellationToken);
            if (acceso != null) return acceso;

            desde = desde.Date;
            hasta = hasta.Date;

            if (hasta < desde)
                return BadRequest(new { ok = false, mensaje = "La fecha final no puede ser anterior a la inicial." });

            try
            {
                var empresaId = ObtenerEmpresaID()!.Value;

                await using var connection = await AbrirAsync(cancellationToken);

                await using var command = new SqlCommand(
                    "dbo.usp_Coordinacion_Calendario_ObtenerCargas",
                    connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 90
                };

                command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
                command.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = desde;
                command.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = hasta;

                var cargas = new List<Dictionary<string, object?>>();

                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                        cargas.Add(LeerFilaDinamica(reader));
                }

                return Json(new { ok = true, total = cargas.Count, cargas });
            }
            catch (SqlException ex)
            {
                return BadRequest(new { ok = false, mensaje = LimpiarMensajeSql(ex.Message) });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { ok = false, mensaje = "No fue posible cargar el historial.", detalle = ex.Message });
            }
        }
    }
}
