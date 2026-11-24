using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaNutri.Controllers
{
    public partial class CoordinacionController
    {
        private const string UrlProgramacion = "/Coordinacion/ProgramacionSemanal";

        [HttpGet]
        public async Task<IActionResult> ProgramacionSemanal(string? tipoPedidoCodigo = null, CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlProgramacion, "Ver", cancellationToken);
            if (acceso != null) return acceso;
            tipoPedidoCodigo = NormalizarTipoProgramacion(tipoPedidoCodigo);
            if (string.IsNullOrWhiteSpace(tipoPedidoCodigo)) tipoPedidoCodigo = "DOSIS";
            ViewData["TipoPedidoCodigo"] = tipoPedidoCodigo;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(26214400)]
        public async Task<IActionResult> AnalizarProgramacionExcel(IFormFile archivo, string periodicidad, DateTime fechaInicio, DateTime fechaFin, string? tipoPedidoCodigo = null, CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlProgramacion, "Crear", cancellationToken);
            if (acceso != null) return acceso;
            if (archivo == null || archivo.Length == 0)
                return BadRequest(new { ok = false, mensaje = "Selecciona un archivo de programación." });
            if (archivo.Length > 26214400)
                return BadRequest(new { ok = false, mensaje = "El archivo no puede exceder 25 MB." });
            if (!string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { ok = false, mensaje = "La programación debe cargarse desde un archivo .xlsx." });

            tipoPedidoCodigo = NormalizarTipoProgramacion(tipoPedidoCodigo);
            if (string.IsNullOrWhiteSpace(tipoPedidoCodigo))
                return BadRequest(new { ok = false, mensaje = "El tipo de programación debe ser Dosis, Hembras, Sementales o Rastro." });

            periodicidad = NormalizarPeriodicidad(periodicidad);
            if (string.IsNullOrWhiteSpace(periodicidad))
                return BadRequest(new { ok = false, mensaje = "La periodicidad debe ser SEMANAL, MENSUAL o SEMESTRAL." });

            fechaInicio = fechaInicio.Date;
            fechaFin = fechaFin.Date;

            if (fechaFin < fechaInicio)
                return BadRequest(new { ok = false, mensaje = "La fecha final no puede ser anterior a la fecha inicial." });

            var dias = (fechaFin - fechaInicio).Days + 1;

            if (periodicidad == "SEMANAL" && dias > 7)
                return BadRequest(new { ok = false, mensaje = "Una programación semanal no puede abarcar más de 7 días." });
            if (periodicidad == "MENSUAL" && dias > 31)
                return BadRequest(new { ok = false, mensaje = "Una programación mensual no puede abarcar más de 31 días." });
            if (periodicidad == "SEMESTRAL" && dias > 190)
                return BadRequest(new { ok = false, mensaje = "Una programación semestral no puede abarcar más de 190 días." });

            try
            {
                byte[] bytes;

                await using (var memoria = new MemoryStream())
                {
                    await archivo.CopyToAsync(memoria, cancellationToken);
                    bytes = memoria.ToArray();
                }

                if (bytes.Length == 0)
                    return BadRequest(new { ok = false, mensaje = "El archivo seleccionado está vacío." });

                var hash = CalcularHashSha256(bytes);

                var lectura = LeerProgramacionExcel(
                    bytes,
                    tipoPedidoCodigo,
                    periodicidad,
                    fechaInicio,
                    fechaFin);

                if (lectura.Celdas.Count == 0)
                    return BadRequest(new { ok = false, mensaje = "No se encontraron cantidades de programación dentro del período seleccionado." });

                var nombreHoja = lectura.Hojas.Count switch
                {
                    0 => "Sin hoja identificada",
                    1 => lectura.Hojas[0],
                    _ => $"{lectura.Hojas.Count} hojas"
                };

                if (nombreHoja.Length > 150)
                    nombreHoja = nombreHoja[..150];

                var empresaId = ObtenerEmpresaID()!.Value;
                var usuarioId = UsuarioID!.Value;

                await using var connection = await AbrirAsync(cancellationToken);

                var importacionId = await CrearImportacionProgramacionAsync(
                    connection,
                    empresaId,
                    tipoPedidoCodigo,
                    periodicidad,
                    fechaInicio,
                    fechaFin,
                    Path.GetFileName(archivo.FileName),
                    hash,
                    nombreHoja,
                    usuarioId,
                    cancellationToken);

                var opcionesJson = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var celdasJson = JsonSerializer.Serialize(
                    lectura.Celdas,
                    opcionesJson);

                await CargarCeldasProgramacionAsync(
                    connection,
                    importacionId,
                    empresaId,
                    tipoPedidoCodigo,
                    periodicidad,
                    fechaInicio,
                    fechaFin,
                    celdasJson,
                    usuarioId,
                    cancellationToken);

                await ValidarImportacionProgramacionAsync(
                    connection,
                    importacionId,
                    empresaId,
                    usuarioId,
                    cancellationToken);

                var vistaPrevia = await ObtenerVistaPreviaProgramacionAsync(
                    connection,
                    importacionId,
                    empresaId,
                    cancellationToken);

                return Json(new
                {
                    ok = true,
                    mensaje = lectura.Advertencias.Count == 0
                        ? "Archivo analizado correctamente."
                        : "Archivo analizado. Revisa las advertencias detectadas.",
                    importacionId,
                    tipoPedidoCodigo,
                    periodicidad,
                    fechaInicio,
                    fechaFin,
                    nombreArchivo = Path.GetFileName(archivo.FileName),
                    hojas = lectura.Hojas,
                    totalCeldas = lectura.Celdas.Count,
                    advertencias = lectura.Advertencias,
                    resumen = vistaPrevia.Resumen,
                    filas = vistaPrevia.Filas
                });
            }
            catch (InvalidDataException ex)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = ex.Message
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
                        mensaje = "No fue posible analizar el archivo de programación.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerVistaPreviaImportacion(long importacionId, CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlProgramacion, "Ver", cancellationToken);
            if (acceso != null) return acceso;
            if (importacionId <= 0)
                return BadRequest(new { ok = false, mensaje = "La importación no es válida." });

            try
            {
                var empresaId = ObtenerEmpresaID()!.Value;

                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await ObtenerVistaPreviaProgramacionAsync(
                    connection,
                    importacionId,
                    empresaId,
                    cancellationToken);

                return Json(new
                {
                    ok = true,
                    resumen = resultado.Resumen,
                    filas = resultado.Filas
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VincularFilaImportacion(long importacionId, long importacionDetalleId, int? clienteId, int? lineaGeneticaId, int? pedidoDetalleId, int? rutaId, CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlProgramacion, "Editar", cancellationToken);
            if (acceso != null) return acceso;

            if (importacionId <= 0 || importacionDetalleId <= 0)
                return BadRequest(new { ok = false, mensaje = "La fila de importación no es válida." });

            try
            {
                var empresaId = ObtenerEmpresaID()!.Value;
                var usuarioId = UsuarioID!.Value;

                await using var connection = await AbrirAsync(cancellationToken);

                await VincularFilaImportacionAsync(
                    connection,
                    importacionId,
                    importacionDetalleId,
                    empresaId,
                    clienteId,
                    lineaGeneticaId,
                    pedidoDetalleId,
                    rutaId,
                    usuarioId,
                    cancellationToken);

                var resultado = await ObtenerVistaPreviaProgramacionAsync(
                    connection,
                    importacionId,
                    empresaId,
                    cancellationToken);

                return Json(new
                {
                    ok = true,
                    mensaje = "Fila vinculada correctamente.",
                    resumen = resultado.Resumen,
                    filas = resultado.Filas
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarImportacionProgramacion(long importacionId, CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlProgramacion, "Editar", cancellationToken);
            if (acceso != null) return acceso;

            if (importacionId <= 0)
                return BadRequest(new { ok = false, mensaje = "La importación no es válida." });

            try
            {
                var empresaId = ObtenerEmpresaID()!.Value;

                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await ConfirmarImportacionProgramacionAsync(
                    connection,
                    importacionId,
                    empresaId,
                    UsuarioID!.Value,
                    UsuarioNombre,
                    cancellationToken);

                return Json(new
                {
                    ok = true,
                    mensaje = "Programación confirmada correctamente.",
                    programacion = resultado
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarImportacionProgramacion(long importacionId, string? motivo, CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(UrlProgramacion, "Eliminar", cancellationToken);
            if (acceso != null) return acceso;

            if (importacionId <= 0)
                return BadRequest(new { ok = false, mensaje = "La importación no es válida." });

            motivo = Limpiar(motivo);

            try
            {
                var empresaId = ObtenerEmpresaID()!.Value;

                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await CancelarImportacionProgramacionAsync(
                    connection,
                    importacionId,
                    empresaId,
                    motivo,
                    UsuarioID!.Value,
                    cancellationToken);

                return Json(new
                {
                    ok = true,
                    mensaje = "Importación cancelada correctamente.",
                    importacion = resultado
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

        private static async Task<long> CrearImportacionProgramacionAsync(SqlConnection connection, int empresaId, string tipoPedidoCodigo, string periodicidad, DateTime fechaInicio, DateTime fechaFin, string nombreArchivo, string hashArchivo, string? nombreHoja, int usuarioId, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Coordinacion_ImportacionProgramacion_Crear",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 90
            };

            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            command.Parameters.Add("@TipoPedidoCodigo", SqlDbType.VarChar, 30).Value = tipoPedidoCodigo;
            command.Parameters.Add("@PeriodicidadCodigo", SqlDbType.VarChar, 20).Value = periodicidad;
            command.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
            command.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
            command.Parameters.Add("@NombreArchivo", SqlDbType.NVarChar, 255).Value = nombreArchivo;
            AddNullableString(command, "@RutaArchivo", 1000, null);
            command.Parameters.Add("@HashArchivo", SqlDbType.VarChar, 128).Value = hashArchivo;
            AddNullableString(command, "@NombreHoja", 150, nombreHoja);
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("El procedimiento no devolvió el identificador de la importación.");

            return ReadLong(reader, "ImportacionID");
        }

        private static async Task CargarCeldasProgramacionAsync(SqlConnection connection, long importacionId, int empresaId, string tipoPedidoCodigo, string periodicidad, DateTime fechaInicio, DateTime fechaFin, string celdasJson, int usuarioId, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Coordinacion_ImportacionProgramacion_CargarCeldas",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 180
            };

            command.Parameters.Add("@ImportacionID", SqlDbType.BigInt).Value = importacionId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            command.Parameters.Add("@TipoPedidoCodigo", SqlDbType.VarChar, 30).Value = tipoPedidoCodigo;
            command.Parameters.Add("@PeriodicidadCodigo", SqlDbType.VarChar, 20).Value = periodicidad;
            command.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
            command.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
            command.Parameters.Add("@CeldasJson", SqlDbType.NVarChar, -1).Value = celdasJson;
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task ValidarImportacionProgramacionAsync(SqlConnection connection, long importacionId, int empresaId, int usuarioId, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Coordinacion_ImportacionProgramacion_Validar",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 180
            };

            command.Parameters.Add("@ImportacionID", SqlDbType.BigInt).Value = importacionId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<(Dictionary<string, object?>? Resumen, List<Dictionary<string, object?>> Filas)> ObtenerVistaPreviaProgramacionAsync(SqlConnection connection, long importacionId, int empresaId, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Coordinacion_ImportacionProgramacion_ObtenerVistaPrevia",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 90
            };

            command.Parameters.Add("@ImportacionID", SqlDbType.BigInt).Value = importacionId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            Dictionary<string, object?>? resumen = null;

            if (await reader.ReadAsync(cancellationToken))
                resumen = LeerFilaDinamica(reader);

            var filas = new List<Dictionary<string, object?>>();

            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    filas.Add(LeerFilaDinamica(reader));
            }

            return (resumen, filas);
        }

        private static async Task VincularFilaImportacionAsync(SqlConnection connection, long importacionId, long importacionDetalleId, int empresaId, int? clienteId, int? lineaGeneticaId, int? pedidoDetalleId, int? rutaId, int usuarioId, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Coordinacion_ImportacionProgramacion_VincularFila",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            command.Parameters.Add("@ImportacionID", SqlDbType.BigInt).Value = importacionId;
            command.Parameters.Add("@ImportacionDetalleID", SqlDbType.BigInt).Value = importacionDetalleId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            AddNullableInt(command, "@ClienteID", clienteId);
            AddNullableInt(command, "@LineaGeneticaID", lineaGeneticaId);
            AddNullableInt(command, "@PedidoDetalleID", pedidoDetalleId);
            AddNullableInt(command, "@RutaID", rutaId);
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<Dictionary<string, object?>> ConfirmarImportacionProgramacionAsync(SqlConnection connection, long importacionId, int empresaId, int usuarioId, string usuarioNombre, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Coordinacion_ImportacionProgramacion_Confirmar",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 180
            };

            command.Parameters.Add("@ImportacionID", SqlDbType.BigInt).Value = importacionId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            AddNullableString(command, "@UsuarioNombre", 150, usuarioNombre);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return new Dictionary<string, object?>
                {
                    ["ImportacionID"] = importacionId
                };
            }

            return LeerFilaDinamica(reader);
        }

        private static async Task<Dictionary<string, object?>> CancelarImportacionProgramacionAsync(SqlConnection connection, long importacionId, int empresaId, string? motivo, int usuarioId, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Coordinacion_ImportacionProgramacion_Cancelar",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 90
            };

            command.Parameters.Add("@ImportacionID", SqlDbType.BigInt).Value = importacionId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            AddNullableString(command, "@Motivo", 1000, motivo);
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return new Dictionary<string, object?>
                {
                    ["ImportacionID"] = importacionId
                };
            }

            return LeerFilaDinamica(reader);
        }

        private static Dictionary<string, object?> LeerFilaDinamica(SqlDataReader reader)
        {
            var fila = new Dictionary<string, object?>(
                StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                fila[reader.GetName(i)] =
                    reader.IsDBNull(i)
                        ? null
                        : reader.GetValue(i);
            }

            return fila;
        }

        private static string CalcularHashSha256(byte[] contenido)
        {
            using var sha = SHA256.Create();

            return Convert
                .ToHexString(
                    sha.ComputeHash(contenido))
                .ToLowerInvariant();
        }

        private static string NormalizarTipoProgramacion(string? tipoPedidoCodigo)
        {
            if (string.IsNullOrWhiteSpace(tipoPedidoCodigo))
                return "DOSIS";

            var codigo = NormalizarClaveExcel(tipoPedidoCodigo);

            return codigo is
                "DOSIS" or
                "HEMBRAS" or
                "SEMENTALES" or
                "RASTRO"
                    ? codigo
                    : string.Empty;
        }
    }
}