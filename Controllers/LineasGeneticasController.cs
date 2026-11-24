using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SistemaNutri.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaNutri.Controllers
{
    /*
     * CATALOGO DE LINEAS GENETICAS  (Administración > Líneas genéticas)
     * Sigue el mismo patrón que ClientesController: conexión propia, usuario y empresa desde la sesión,
     * acceso validado por la URL de la pantalla.
     *
     * Los métodos UsuarioPerteneceEmpresaAsync y TienePermisoAccionAsync viven en
     * LineasGeneticasController.Seguridad.cs (copiados de ClientesController).
     */
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public partial class LineasGeneticasController : Controller
    {
        private readonly string _connectionString;
        private const int RolAdministradorId = 1;
        private const string UrlLineas = "/LineasGeneticas/Index";

        public LineasGeneticasController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");
        }

        private int? UsuarioID => HttpContext.Session.GetInt32("UsuarioID");

        private int? ObtenerEmpresaID()
        {
            var empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId.HasValue && empresaId.Value > 0)
                return empresaId.Value;

            var empresaTexto = HttpContext.Session.GetString("EmpresaId");
            if (int.TryParse(empresaTexto, out var empresaParseada) && empresaParseada > 0)
                return empresaParseada;

            empresaTexto = HttpContext.Session.GetString("EmpresaID");

            return int.TryParse(empresaTexto, out empresaParseada) && empresaParseada > 0
                ? empresaParseada
                : null;
        }

        private async Task<SqlConnection> AbrirAsync(CancellationToken cancellationToken)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            string accion,
            CancellationToken cancellationToken)
        {
            if (!UsuarioID.HasValue || UsuarioID.Value <= 0)
                return RedirectToAction("Login", "Login");

            var empresaId = ObtenerEmpresaID();

            if (!empresaId.HasValue)
            {
                TempData["ErrorMessage"] = "Selecciona una empresa antes de ingresar a Líneas genéticas.";
                return RedirectToAction("Index", "Menu");
            }

            await using var connection = await AbrirAsync(cancellationToken);

            if (!await UsuarioPerteneceEmpresaAsync(
                    connection,
                    UsuarioID.Value,
                    empresaId.Value,
                    cancellationToken))
                return Forbid();

            if (!await TienePermisoAccionAsync(
                    connection,
                    UsuarioID.Value,
                    empresaId.Value,
                    UrlLineas,
                    accion,
                    cancellationToken))
                return Forbid();

            return null;
        }

        // ------------------------------------------------------------------ utilidades

        private static Dictionary<string, object?> LeerFilaDinamica(SqlDataReader reader)
        {
            var fila = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < reader.FieldCount; i++)
                fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

            return fila;
        }

        private static void AddNullableString(SqlCommand command, string nombre, int largo, string? valor)
        {
            var p = command.Parameters.Add(nombre, SqlDbType.NVarChar, largo);
            p.Value = string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();
        }

        // Los THROW de los procedimientos ya traen un texto claro para el usuario.
        private static string MensajeSql(SqlException ex)
        {
            if (ex.Number is 2627 or 2601)
                return "Ese registro ya existe.";

            return ex.Number >= 50000
                ? ex.Message
                : "No fue posible completar la operación en la base de datos.";
        }

        private IActionResult Error(SqlException ex) =>
            BadRequest(new { ok = false, mensaje = MensajeSql(ex) });

        private IActionResult ErrorGeneral(string mensaje, Exception ex) =>
            StatusCode(
                StatusCodes.Status500InternalServerError,
                new { ok = false, mensaje, detalle = ex.Message });

        // ------------------------------------------------------------------ pantalla

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync("Ver", cancellationToken);
            if (acceso != null) return acceso;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Listar(
            string? texto = null,
            string? estatus = null,
            CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync("Ver", cancellationToken);
            if (acceso != null) return acceso;

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                await using var command = new SqlCommand("dbo.usp_Nutri_LineasGeneticas_Listar", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                AddNullableString(command, "@Texto", 100, texto);
                command.Parameters.Add("@Estatus", SqlDbType.VarChar, 10).Value =
                    string.IsNullOrWhiteSpace(estatus) ? "TODAS" : estatus.Trim().ToUpperInvariant();

                var lineas = new List<Dictionary<string, object?>>();
                var alias = new List<Dictionary<string, object?>>();

                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                        lineas.Add(LeerFilaDinamica(reader));

                    if (await reader.NextResultAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                            alias.Add(LeerFilaDinamica(reader));
                    }
                }

                return Json(new { ok = true, lineas, alias });
            }
            catch (SqlException ex)
            {
                return Error(ex);
            }
            catch (Exception ex)
            {
                return ErrorGeneral("No fue posible cargar las líneas genéticas.", ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(
            int? lineaGeneticaId,
            string? codigo,
            string? nombre,
            string? descripcion,
            bool activo = true,
            CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync(lineaGeneticaId.HasValue ? "Editar" : "Crear", cancellationToken);
            if (acceso != null) return acceso;

            codigo = codigo?.Trim();
            nombre = nombre?.Trim();
            descripcion = descripcion?.Trim();

            if (string.IsNullOrWhiteSpace(codigo))
                return BadRequest(new { ok = false, mensaje = "El código es obligatorio." });
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(new { ok = false, mensaje = "El nombre es obligatorio." });
            if (codigo.Length > 60)
                return BadRequest(new { ok = false, mensaje = "El código no puede exceder 60 caracteres." });
            if (nombre.Length > 200)
                return BadRequest(new { ok = false, mensaje = "El nombre no puede exceder 200 caracteres." });
            if (descripcion != null && descripcion.Length > 600)
                return BadRequest(new { ok = false, mensaje = "La descripción no puede exceder 600 caracteres." });

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                await using var command = new SqlCommand("dbo.usp_Nutri_LineasGeneticas_Guardar", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@LineaGeneticaID", SqlDbType.Int).Value =
                    lineaGeneticaId.HasValue ? lineaGeneticaId.Value : DBNull.Value;
                command.Parameters.Add("@Codigo", SqlDbType.NVarChar, 60).Value = codigo;
                command.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = nombre;
                AddNullableString(command, "@Descripcion", 600, descripcion);
                command.Parameters.Add("@Activo", SqlDbType.Bit).Value = activo;
                command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID!.Value;

                Dictionary<string, object?>? linea = null;

                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    if (await reader.ReadAsync(cancellationToken))
                        linea = LeerFilaDinamica(reader);
                }

                return Json(new
                {
                    ok = true,
                    mensaje = lineaGeneticaId.HasValue
                        ? "Línea actualizada correctamente."
                        : "Línea registrada. Ahora puedes agregarle alias.",
                    linea
                });
            }
            catch (SqlException ex)
            {
                return Error(ex);
            }
            catch (Exception ex)
            {
                return ErrorGeneral("No fue posible guardar la línea genética.", ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstatus(
            int lineaGeneticaId,
            bool activo,
            CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync("Editar", cancellationToken);
            if (acceso != null) return acceso;

            if (lineaGeneticaId <= 0)
                return BadRequest(new { ok = false, mensaje = "La línea no es válida." });

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                await using var command = new SqlCommand("dbo.usp_Nutri_LineasGeneticas_CambiarEstatus", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@LineaGeneticaID", SqlDbType.Int).Value = lineaGeneticaId;
                command.Parameters.Add("@Activo", SqlDbType.Bit).Value = activo;
                command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID!.Value;

                await command.ExecuteNonQueryAsync(cancellationToken);

                return Json(new { ok = true, mensaje = activo ? "Línea activada." : "Línea desactivada." });
            }
            catch (SqlException ex)
            {
                return Error(ex);
            }
            catch (Exception ex)
            {
                return ErrorGeneral("No fue posible cambiar el estatus de la línea.", ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarAlias(
            int lineaGeneticaId,
            string? alias,
            CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync("Editar", cancellationToken);
            if (acceso != null) return acceso;

            alias = alias?.Trim();

            if (lineaGeneticaId <= 0)
                return BadRequest(new { ok = false, mensaje = "La línea no es válida." });
            if (string.IsNullOrWhiteSpace(alias))
                return BadRequest(new { ok = false, mensaje = "Escribe el alias." });
            if (alias.Length > 100)
                return BadRequest(new { ok = false, mensaje = "El alias no puede exceder 100 caracteres." });

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                await using var command = new SqlCommand("dbo.usp_Nutri_LineasGeneticas_AliasGuardar", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@LineaGeneticaID", SqlDbType.Int).Value = lineaGeneticaId;
                command.Parameters.Add("@Alias", SqlDbType.NVarChar, 100).Value = alias;
                command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID!.Value;

                await command.ExecuteNonQueryAsync(cancellationToken);

                return Json(new { ok = true, mensaje = "Alias agregado." });
            }
            catch (SqlException ex)
            {
                return Error(ex);
            }
            catch (Exception ex)
            {
                return ErrorGeneral("No fue posible guardar el alias.", ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarAlias(
            int lineaAliasId,
            CancellationToken cancellationToken = default)
        {
            var acceso = await ValidarAccesoAsync("Editar", cancellationToken);
            if (acceso != null) return acceso;

            if (lineaAliasId <= 0)
                return BadRequest(new { ok = false, mensaje = "El alias no es válido." });

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                await using var command = new SqlCommand("dbo.usp_Nutri_LineasGeneticas_AliasEliminar", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@LineaAliasID", SqlDbType.Int).Value = lineaAliasId;

                await command.ExecuteNonQueryAsync(cancellationToken);

                return Json(new { ok = true, mensaje = "Alias eliminado." });
            }
            catch (SqlException ex)
            {
                return Error(ex);
            }
            catch (Exception ex)
            {
                return ErrorGeneral("No fue posible eliminar el alias.", ex);
            }
        }
    }
}
