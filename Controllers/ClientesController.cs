using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SistemaNutri.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaNutri.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ClientesController : Controller
    {
        private readonly string _connectionString;
        private const int RolAdministradorId = 1;
        private const string UrlClientes = "/Clientes/Index";
        private const string UrlProgramacion = "/Coordinacion/ProgramacionSemanal";

        public ClientesController(IConfiguration configuration)
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
                TempData["ErrorMessage"] = "Selecciona una empresa antes de ingresar a Clientes.";
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
                    UrlClientes,
                    accion,
                    cancellationToken))
                return Forbid();

            return null;
        }

        private static async Task<bool> UsuarioPerteneceEmpresaAsync(
            SqlConnection connection,
            int usuarioId,
            int empresaId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT CAST
(
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.UsuariosEmpresas UE
            INNER JOIN dbo.Empresas E
                ON E.EmpresaID=UE.EmpresaID
            INNER JOIN dbo.Usuarios U
                ON U.UsuarioID=UE.UsuarioID
            WHERE UE.UsuarioID=@UsuarioID
              AND UE.EmpresaID=@EmpresaID
              AND UE.Activo=1
              AND E.Activa=1
              AND U.Activo=1
        )
        THEN 1
        ELSE 0
    END
    AS bit
);";

            await using var command = new SqlCommand(sql, connection);

            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;

            var value = await command.ExecuteScalarAsync(cancellationToken);

            return value != DBNull.Value && Convert.ToBoolean(value);
        }

        private static async Task<bool> TienePermisoAccionAsync(
            SqlConnection connection,
            int usuarioId,
            int empresaId,
            string urlSubMenu,
            string accion,
            CancellationToken cancellationToken)
        {
            const string sql = @"
DECLARE @SubMenuID INT;
DECLARE @RolID INT;

SELECT @RolID=RolID
FROM dbo.Usuarios
WHERE UsuarioID=@UsuarioID
  AND Activo=1;

SELECT TOP(1)
    @SubMenuID=SM.SubMenuID
FROM dbo.SubMenus SM
WHERE SM.Activo=1
  AND LOWER(REPLACE(LTRIM(RTRIM(ISNULL(SM.UrlEnlace,''))),'~',''))
      =
      LOWER(REPLACE(LTRIM(RTRIM(@UrlSubMenu)),'~',''));

SELECT CAST
(
    CASE
        WHEN @RolID=@RolAdministradorId THEN 1

        WHEN @SubMenuID IS NULL THEN 0

        WHEN NOT EXISTS
        (
            SELECT 1
            FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID,@EmpresaID) P
            WHERE P.SubMenuID=@SubMenuID
              AND P.TienePermiso=1
        )
        THEN 0

        WHEN @Accion=N'Ver' THEN 1

        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.PermisosPorRol PR
            INNER JOIN dbo.SubMenuAcciones SMA
                ON SMA.SubMenuAccionID=PR.SubMenuAccionID
               AND SMA.Activo=1
            INNER JOIN dbo.Acciones A
                ON A.AccionID=SMA.AccionID
            WHERE PR.RolID=@RolID
              AND PR.Activo=1
              AND SMA.SubMenuID=@SubMenuID
              AND LTRIM(RTRIM(A.Nombre)) COLLATE Latin1_General_CI_AI
                  =
                  LTRIM(RTRIM(@Accion)) COLLATE Latin1_General_CI_AI
        )
        THEN 1

        ELSE 0
    END
    AS bit
);";

            await using var command = new SqlCommand(sql, connection);

            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            command.Parameters.Add("@UrlSubMenu", SqlDbType.NVarChar, 500).Value = urlSubMenu;
            command.Parameters.Add("@Accion", SqlDbType.NVarChar, 50).Value = accion;
            command.Parameters.Add("@RolAdministradorId", SqlDbType.Int).Value = RolAdministradorId;

            var value = await command.ExecuteScalarAsync(cancellationToken);

            return value != DBNull.Value && Convert.ToBoolean(value);
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            [FromQuery] ClientesFiltroVm? filtros,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync("Ver", cancellationToken);
            if (acceso != null)
                return acceso;

            var empresaId = ObtenerEmpresaID()!.Value;
            filtros ??= new ClientesFiltroVm();

            await using var connection = await AbrirAsync(cancellationToken);

            var vm = new ClientesIndexVm
            {
                EmpresaID = empresaId,
                EmpresaNombre = await ObtenerEmpresaNombreAsync(
                    connection,
                    empresaId,
                    cancellationToken),
                Filtros = filtros,
                Clientes = await ObtenerCatalogoAsync(
                    connection,
                    empresaId,
                    filtros.Buscar,
                    filtros.IncluirInactivos,
                    cancellationToken),
                Catalogos = await ObtenerCatalogosAsync(
                    connection,
                    empresaId,
                    cancellationToken),
                PuedeCrear = await TienePermisoAccionAsync(
                    connection,
                    UsuarioID!.Value,
                    empresaId,
                    UrlClientes,
                    "Crear",
                    cancellationToken),
                PuedeEditar = await TienePermisoAccionAsync(
                    connection,
                    UsuarioID!.Value,
                    empresaId,
                    UrlClientes,
                    "Editar",
                    cancellationToken),
                PuedeEliminar = await TienePermisoAccionAsync(
                    connection,
                    UsuarioID!.Value,
                    empresaId,
                    UrlClientes,
                    "Eliminar",
                    cancellationToken)
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerDetalle(
            int clienteId,
            CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync("Ver", cancellationToken);
            if (acceso != null)
                return acceso;

            if (clienteId <= 0)
                return Json(new
                {
                    success = false,
                    message = "El cliente indicado no es válido."
                });

            var empresaId = ObtenerEmpresaID()!.Value;

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                var vm = await ObtenerDetalleClienteAsync(
                    connection,
                    clienteId,
                    empresaId,
                    cancellationToken);

                if (vm == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se encontró el cliente solicitado."
                    });
                }

                var catalogos = await ObtenerCatalogosAsync(
                    connection,
                    empresaId,
                    cancellationToken);

                vm.TiposUbicacion = catalogos.TiposUbicacion;

                vm.PuedeEditar = await TienePermisoAccionAsync(
                    connection,
                    UsuarioID!.Value,
                    empresaId,
                    UrlClientes,
                    "Editar",
                    cancellationToken);

                vm.PuedeEliminar = await TienePermisoAccionAsync(
                    connection,
                    UsuarioID!.Value,
                    empresaId,
                    UrlClientes,
                    "Eliminar",
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    data = vm
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(
            ClientesGuardarVm model,
            CancellationToken cancellationToken)
        {
            if (model.ClienteID.HasValue && model.ClienteID.Value <= 0)
                model.ClienteID = null;

            var accion = model.ClienteID.HasValue
                ? "Editar"
                : "Crear";

            var acceso = await ValidarAccesoAsync(
                accion,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return JsonErrorModelo();

            var empresaId = ObtenerEmpresaID()!.Value;

            model.EmpresasSeleccionadas ??= new List<int>();

            model.EmpresasSeleccionadas = model.EmpresasSeleccionadas
                .Where(x => x > 0)
                .Distinct()
                .ToList();

           /* if (!model.EmpresasSeleccionadas.Contains(empresaId))
            {
                return Json(new
                {
                    success = false,
                    message = "El cliente debe estar relacionado al menos con la empresa actualmente seleccionada."
                });
            }*/

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await GuardarClienteAsync(
                    connection,
                    model,
                    UsuarioID!.Value,
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    message = model.ClienteID.HasValue
                        ? "Cliente actualizado correctamente."
                        : "Cliente registrado correctamente.",
                    data = resultado
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarUbicacion(
            ClientesGuardarUbicacionVm model,
            CancellationToken cancellationToken)
        {
            if (model.ClienteUbicacionID.HasValue &&
                model.ClienteUbicacionID.Value <= 0)
                model.ClienteUbicacionID = null;

            var accion = model.ClienteUbicacionID.HasValue
                ? "Editar"
                : "Crear";

            var acceso = await ValidarAccesoAsync(
                accion,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return JsonErrorModelo();

            var empresaId = ObtenerEmpresaID()!.Value;

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await GuardarUbicacionAsync(
                    connection,
                    model,
                    empresaId,
                    UsuarioID!.Value,
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    message = model.ClienteUbicacionID.HasValue
                        ? "Ubicación actualizada correctamente."
                        : "Ubicación registrada correctamente.",
                    data = resultado
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarAlias(
            ClientesGuardarAliasVm model,
            CancellationToken cancellationToken)
        {
            if (model.ClienteAliasID.HasValue &&
                model.ClienteAliasID.Value <= 0)
                model.ClienteAliasID = null;

            var accion = model.ClienteAliasID.HasValue
                ? "Editar"
                : "Crear";

            var acceso = await ValidarAccesoAsync(
                accion,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return JsonErrorModelo();

            var empresaId = ObtenerEmpresaID()!.Value;

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await GuardarAliasAsync(
                    connection,
                    model,
                    empresaId,
                    UsuarioID!.Value,
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    message = model.ClienteAliasID.HasValue
                        ? "Alias actualizado correctamente."
                        : "Alias registrado correctamente.",
                    data = resultado
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarActivo(
            ClientesCambiarActivoVm model,
            CancellationToken cancellationToken)
        {
            var accion = model.Activo
                ? "Editar"
                : "Eliminar";

            var acceso = await ValidarAccesoAsync(
                accion,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return JsonErrorModelo();

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await CambiarActivoClienteAsync(
                    connection,
                    model,
                    UsuarioID!.Value,
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    message = model.Activo
                        ? "Cliente activado correctamente."
                        : "Cliente desactivado correctamente.",
                    data = resultado
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarActivoUbicacion(
            ClientesCambiarActivoUbicacionVm model,
            CancellationToken cancellationToken)
        {
            var accion = model.Activo
                ? "Editar"
                : "Eliminar";

            var acceso = await ValidarAccesoAsync(
                accion,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return JsonErrorModelo();

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await CambiarActivoUbicacionAsync(
                    connection,
                    model,
                    UsuarioID!.Value,
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    message = model.Activo
                        ? "Ubicación activada correctamente."
                        : "Ubicación desactivada correctamente.",
                    data = resultado
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarActivoAlias(
            ClientesCambiarActivoAliasVm model,
            CancellationToken cancellationToken)
        {
            var accion = model.Activo
                ? "Editar"
                : "Eliminar";

            var acceso = await ValidarAccesoAsync(
                accion,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return JsonErrorModelo();

            try
            {
                await using var connection = await AbrirAsync(cancellationToken);

                var resultado = await CambiarActivoAliasAsync(
                    connection,
                    model,
                    UsuarioID!.Value,
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    message = model.Activo
                        ? "Alias activado correctamente."
                        : "Alias desactivado correctamente.",
                    data = resultado
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> BuscarParaVinculo(
            string? buscar,
            int limite = 30,
            CancellationToken cancellationToken = default)
        {
            if (!UsuarioID.HasValue || UsuarioID.Value <= 0)
                return Unauthorized();

            var empresaId = ObtenerEmpresaID();

            if (!empresaId.HasValue)
            {
                return Json(new
                {
                    success = false,
                    message = "No hay una empresa seleccionada."
                });
            }

            await using var connection = await AbrirAsync(cancellationToken);

            if (!await UsuarioPerteneceEmpresaAsync(
                    connection,
                    UsuarioID.Value,
                    empresaId.Value,
                    cancellationToken))
                return Forbid();

            var puedeVerClientes = await TienePermisoAccionAsync(
                connection,
                UsuarioID.Value,
                empresaId.Value,
                UrlClientes,
                "Ver",
                cancellationToken);

            var puedeVerProgramacion = await TienePermisoAccionAsync(
                connection,
                UsuarioID.Value,
                empresaId.Value,
                UrlProgramacion,
                "Ver",
                cancellationToken);

            if (!puedeVerClientes && !puedeVerProgramacion)
                return Forbid();

            try
            {
                var resultados = await BuscarClientesParaVinculoAsync(
                    connection,
                    empresaId.Value,
                    buscar,
                    limite,
                    cancellationToken);

                return Json(new
                {
                    success = true,
                    data = resultados
                });
            }
            catch (SqlException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        private static async Task<List<ClientesCatalogoItemVm>> ObtenerCatalogoAsync(
            SqlConnection connection,
            int empresaId,
            string? buscar,
            bool incluirInactivos,
            CancellationToken cancellationToken)
        {
            var resultado = new List<ClientesCatalogoItemVm>();

            await using var command = new SqlCommand(
                "dbo.usp_Clientes_ObtenerCatalogo",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            AddNullableNVarChar(command, "@Buscar", 200, buscar);
            command.Parameters.Add("@IncluirInactivos", SqlDbType.Bit).Value = incluirInactivos;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(new ClientesCatalogoItemVm
                {
                    ClienteID = ReadInt(reader, "ClienteID"),
                    CodigoCliente = ReadNullableString(reader, "CodigoCliente"),
                    NombreComercial = ReadString(reader, "NombreComercial"),
                    RazonSocial = ReadNullableString(reader, "RazonSocial"),
                    RFC = ReadNullableString(reader, "RFC"),
                    EsBioHypor = ReadBool(reader, "EsBioHypor"),
                    Telefono = ReadNullableString(reader, "Telefono"),
                    Correo = ReadNullableString(reader, "Correo"),
                    Observaciones = ReadNullableString(reader, "Observaciones"),
                    Activo = ReadBool(reader, "Activo"),
                    FechaCreacion = ReadDateTime(reader, "FechaCreacion"),
                    FechaModificacion = ReadNullableDateTime(reader, "FechaModificacion"),
                    TotalUbicaciones = ReadInt(reader, "TotalUbicaciones"),
                    TotalAlias = ReadInt(reader, "TotalAlias"),
                    Empresas = ReadNullableString(reader, "Empresas")
                });
            }

            return resultado;
        }

        private static async Task<ClientesCatalogosVm> ObtenerCatalogosAsync(
            SqlConnection connection,
            int empresaId,
            CancellationToken cancellationToken)
        {
            var resultado = new ClientesCatalogosVm();

            await using var command = new SqlCommand(
                "dbo.usp_Clientes_ObtenerCatalogos",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Empresas.Add(new ClientesEmpresaVm
                {
                    EmpresaID = ReadInt(reader, "EmpresaID"),
                    Nombre = ReadString(reader, "Nombre"),
                  /* EsEmpresaActual = ReadBool(reader, "EsEmpresaActual"),
                    Seleccionada = ReadBool(reader, "EsEmpresaActual")*/
                });
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado.TiposUbicacion.Add(new ClientesTipoUbicacionVm
                    {
                        TipoUbicacionClienteID = ReadInt(
                            reader,
                            "TipoUbicacionClienteID"),
                        Codigo = ReadString(reader, "Codigo"),
                        Nombre = ReadString(reader, "Nombre"),
                        Descripcion = ReadNullableString(reader, "Descripcion"),
                        Activo = ReadBool(reader, "Activo")
                    });
                }
            }

            return resultado;
        }

        private static async Task<ClientesDetallePaginaVm?> ObtenerDetalleClienteAsync(
            SqlConnection connection,
            int clienteId,
            int empresaId,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Clientes_ObtenerDetalle",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            var resultado = new ClientesDetallePaginaVm
            {
                Cliente = new ClientesDetalleVm
                {
                    ClienteID = ReadInt(reader, "ClienteID"),
                    CodigoCliente = ReadNullableString(reader, "CodigoCliente"),
                    NombreComercial = ReadString(reader, "NombreComercial"),
                    RazonSocial = ReadNullableString(reader, "RazonSocial"),
                    RFC = ReadNullableString(reader, "RFC"),
                    EsBioHypor = ReadBool(reader, "EsBioHypor"),
                    Telefono = ReadNullableString(reader, "Telefono"),
                    Correo = ReadNullableString(reader, "Correo"),
                    Observaciones = ReadNullableString(reader, "Observaciones"),
                    Activo = ReadBool(reader, "Activo"),
                    FechaCreacion = ReadDateTime(reader, "FechaCreacion"),
                    FechaModificacion = ReadNullableDateTime(reader, "FechaModificacion")
                }
            };

            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = ReadInt(reader, "EmpresaID");

                    resultado.Empresas.Add(new ClientesEmpresaVm
                    {
                        EmpresaID = id,
                        Nombre = ReadString(reader, "Nombre"),
                        EsEmpresaActual = id == empresaId,
                        Seleccionada = ReadBool(reader, "Seleccionada")
                    });
                }
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado.Ubicaciones.Add(new ClientesUbicacionVm
                    {
                        ClienteUbicacionID = ReadInt(
                            reader,
                            "ClienteUbicacionID"),
                        ClienteID = ReadInt(reader, "ClienteID"),
                        TipoUbicacionClienteID = ReadNullableInt(
                            reader,
                            "TipoUbicacionClienteID"),
                        TipoCodigo = ReadNullableString(reader, "TipoCodigo"),
                        Tipo = ReadNullableString(reader, "Tipo"),
                        Codigo = ReadNullableString(reader, "Codigo"),
                        Nombre = ReadString(reader, "Nombre"),
                        Direccion = ReadNullableString(reader, "Direccion"),
                        Municipio = ReadNullableString(reader, "Municipio"),
                        Estado = ReadNullableString(reader, "Estado"),
                        CodigoPostal = ReadNullableString(reader, "CodigoPostal"),
                        Latitud = ReadNullableDecimal(reader, "Latitud"),
                        Longitud = ReadNullableDecimal(reader, "Longitud"),
                        UrlMaps = ReadNullableString(reader, "UrlMaps"),
                        Contacto = ReadNullableString(reader, "Contacto"),
                        Telefono = ReadNullableString(reader, "Telefono"),
                        Correo = ReadNullableString(reader, "Correo"),
                        Observaciones = ReadNullableString(reader, "Observaciones"),
                        Activo = ReadBool(reader, "Activo"),
                        FechaCreacion = ReadDateTime(reader, "FechaCreacion"),
                        FechaModificacion = ReadNullableDateTime(
                            reader,
                            "FechaModificacion")
                    });
                }
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado.Alias.Add(new ClientesAliasVm
                    {
                        ClienteAliasID = ReadInt(reader, "ClienteAliasID"),
                        ClienteID = ReadInt(reader, "ClienteID"),
                        Alias = ReadString(reader, "Alias"),
                        Activo = ReadBool(reader, "Activo"),
                        FechaCreacion = ReadDateTime(reader, "FechaCreacion"),
                        FechaModificacion = ReadNullableDateTime(
                            reader,
                            "FechaModificacion")
                    });
                }
            }

            return resultado;
        }

        private static async Task<ClientesGuardarResultadoVm> GuardarClienteAsync(
            SqlConnection connection,
            ClientesGuardarVm model,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Clientes_Guardar",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                model.ClienteID.HasValue
                    ? model.ClienteID.Value
                    : DBNull.Value;

            AddNullableNVarChar(
                command,
                "@CodigoCliente",
                50,
                model.CodigoCliente);

            AddNullableNVarChar(
                command,
                "@NombreComercial",
                200,
                model.NombreComercial);

            AddNullableNVarChar(
                command,
                "@RazonSocial",
                200,
                model.RazonSocial);

            AddNullableNVarChar(
                command,
                "@RFC",
                20,
                model.RFC);

            command.Parameters.Add(
                "@EsBioHypor",
                SqlDbType.Bit).Value = model.EsBioHypor;

            AddNullableNVarChar(
                command,
                "@Telefono",
                30,
                model.Telefono);

            AddNullableNVarChar(
                command,
                "@Correo",
                150,
                model.Correo);

            AddNullableNVarChar(
                command,
                "@Observaciones",
                500,
                model.Observaciones);

            var empresasJson = JsonSerializer.Serialize(
                model.EmpresasSeleccionadas
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList());

            command.Parameters.Add(
                "@EmpresasJson",
                SqlDbType.NVarChar,
                -1).Value = empresasJson;

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "El procedimiento no devolvió el cliente guardado.");

            return new ClientesGuardarResultadoVm
            {
                ClienteID = ReadInt(reader, "ClienteID"),
                CodigoCliente = ReadNullableString(reader, "CodigoCliente"),
                NombreComercial = ReadString(reader, "NombreComercial"),
                RazonSocial = ReadNullableString(reader, "RazonSocial"),
                RFC = ReadNullableString(reader, "RFC"),
                EsBioHypor = ReadBool(reader, "EsBioHypor"),
                Telefono = ReadNullableString(reader, "Telefono"),
                Correo = ReadNullableString(reader, "Correo"),
                Observaciones = ReadNullableString(reader, "Observaciones"),
                Activo = ReadBool(reader, "Activo")
            };
        }

        private static async Task<ClientesUbicacionVm> GuardarUbicacionAsync(
            SqlConnection connection,
            ClientesGuardarUbicacionVm model,
            int empresaId,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Clientes_GuardarUbicacion",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add(
                "@ClienteUbicacionID",
                SqlDbType.Int).Value =
                model.ClienteUbicacionID.HasValue
                    ? model.ClienteUbicacionID.Value
                    : DBNull.Value;

            command.Parameters.Add(
                "@ClienteID",
                SqlDbType.Int).Value = model.ClienteID;

            command.Parameters.Add(
                "@EmpresaID",
                SqlDbType.Int).Value = empresaId;

            command.Parameters.Add(
                "@TipoUbicacionClienteID",
                SqlDbType.Int).Value =
                model.TipoUbicacionClienteID.HasValue
                    ? model.TipoUbicacionClienteID.Value
                    : DBNull.Value;

            AddNullableNVarChar(command, "@Codigo", 50, model.Codigo);
            AddNullableNVarChar(command, "@Nombre", 150, model.Nombre);
            AddNullableNVarChar(command, "@Direccion", 300, model.Direccion);
            AddNullableNVarChar(command, "@Municipio", 100, model.Municipio);
            AddNullableNVarChar(command, "@Estado", 100, model.Estado);
            AddNullableNVarChar(command, "@CodigoPostal", 10, model.CodigoPostal);

            AddNullableDecimal(
                command,
                "@Latitud",
                model.Latitud,
                10,
                7);

            AddNullableDecimal(
                command,
                "@Longitud",
                model.Longitud,
                10,
                7);

            AddNullableNVarChar(command, "@UrlMaps", 500, model.UrlMaps);
            AddNullableNVarChar(command, "@Contacto", 150, model.Contacto);
            AddNullableNVarChar(command, "@Telefono", 30, model.Telefono);
            AddNullableNVarChar(command, "@Correo", 150, model.Correo);
            AddNullableNVarChar(command, "@Observaciones", 500, model.Observaciones);

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "El procedimiento no devolvió la ubicación guardada.");

            return new ClientesUbicacionVm
            {
                ClienteUbicacionID = ReadInt(reader, "ClienteUbicacionID"),
                ClienteID = ReadInt(reader, "ClienteID"),
                TipoUbicacionClienteID = ReadNullableInt(
                    reader,
                    "TipoUbicacionClienteID"),
                TipoCodigo = ReadNullableString(reader, "TipoCodigo"),
                Tipo = ReadNullableString(reader, "Tipo"),
                Codigo = ReadNullableString(reader, "Codigo"),
                Nombre = ReadString(reader, "Nombre"),
                Direccion = ReadNullableString(reader, "Direccion"),
                Municipio = ReadNullableString(reader, "Municipio"),
                Estado = ReadNullableString(reader, "Estado"),
                CodigoPostal = ReadNullableString(reader, "CodigoPostal"),
                Latitud = ReadNullableDecimal(reader, "Latitud"),
                Longitud = ReadNullableDecimal(reader, "Longitud"),
                UrlMaps = ReadNullableString(reader, "UrlMaps"),
                Contacto = ReadNullableString(reader, "Contacto"),
                Telefono = ReadNullableString(reader, "Telefono"),
                Correo = ReadNullableString(reader, "Correo"),
                Observaciones = ReadNullableString(reader, "Observaciones"),
                Activo = ReadBool(reader, "Activo")
            };
        }

        private static async Task<ClientesAliasVm> GuardarAliasAsync(
            SqlConnection connection,
            ClientesGuardarAliasVm model,
            int empresaId,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Clientes_GuardarAlias",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add(
                "@ClienteAliasID",
                SqlDbType.Int).Value =
                model.ClienteAliasID.HasValue
                    ? model.ClienteAliasID.Value
                    : DBNull.Value;

            command.Parameters.Add(
                "@ClienteID",
                SqlDbType.Int).Value = model.ClienteID;

            command.Parameters.Add(
                "@EmpresaID",
                SqlDbType.Int).Value = empresaId;

            AddNullableNVarChar(
                command,
                "@Alias",
                200,
                model.Alias);

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "El procedimiento no devolvió el alias guardado.");

            return new ClientesAliasVm
            {
                ClienteAliasID = ReadInt(reader, "ClienteAliasID"),
                ClienteID = ReadInt(reader, "ClienteID"),
                Alias = ReadString(reader, "Alias"),
                Activo = ReadBool(reader, "Activo"),
                FechaCreacion = ReadDateTime(reader, "FechaCreacion"),
                FechaModificacion = ReadNullableDateTime(
                    reader,
                    "FechaModificacion")
            };
        }

        private static async Task<ClientesCambiarActivoResultadoVm> CambiarActivoClienteAsync(
            SqlConnection connection,
            ClientesCambiarActivoVm model,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Clientes_CambiarActivo",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add(
                "@ClienteID",
                SqlDbType.Int).Value = model.ClienteID;

            command.Parameters.Add(
                "@Activo",
                SqlDbType.Bit).Value = model.Activo;

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "No fue posible obtener el resultado del cambio de estatus.");

            return new ClientesCambiarActivoResultadoVm
            {
                ClienteID = ReadInt(reader, "ClienteID"),
                NombreComercial = ReadString(reader, "NombreComercial"),
                Activo = ReadBool(reader, "Activo")
            };
        }

        private static async Task<ClientesCambiarActivoUbicacionResultadoVm> CambiarActivoUbicacionAsync(
            SqlConnection connection,
            ClientesCambiarActivoUbicacionVm model,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Clientes_CambiarActivoUbicacion",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add(
                "@ClienteUbicacionID",
                SqlDbType.Int).Value = model.ClienteUbicacionID;

            command.Parameters.Add(
                "@ClienteID",
                SqlDbType.Int).Value = model.ClienteID;

            command.Parameters.Add(
                "@Activo",
                SqlDbType.Bit).Value = model.Activo;

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "No fue posible obtener el resultado del cambio de estatus de la ubicación.");

            return new ClientesCambiarActivoUbicacionResultadoVm
            {
                ClienteUbicacionID = ReadInt(
                    reader,
                    "ClienteUbicacionID"),
                ClienteID = ReadInt(reader, "ClienteID"),
                Nombre = ReadString(reader, "Nombre"),
                Activo = ReadBool(reader, "Activo")
            };
        }

        private static async Task<ClientesCambiarActivoAliasResultadoVm> CambiarActivoAliasAsync(
            SqlConnection connection,
            ClientesCambiarActivoAliasVm model,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "dbo.usp_Clientes_CambiarActivoAlias",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add(
                "@ClienteAliasID",
                SqlDbType.Int).Value = model.ClienteAliasID;

            command.Parameters.Add(
                "@ClienteID",
                SqlDbType.Int).Value = model.ClienteID;

            command.Parameters.Add(
                "@Activo",
                SqlDbType.Bit).Value = model.Activo;

            command.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "No fue posible obtener el resultado del cambio de estatus del alias.");

            return new ClientesCambiarActivoAliasResultadoVm
            {
                ClienteAliasID = ReadInt(reader, "ClienteAliasID"),
                ClienteID = ReadInt(reader, "ClienteID"),
                Alias = ReadString(reader, "Alias"),
                Activo = ReadBool(reader, "Activo")
            };
        }

        private static async Task<List<ClientesBusquedaVinculoVm>> BuscarClientesParaVinculoAsync(
            SqlConnection connection,
            int empresaId,
            string? buscar,
            int limite,
            CancellationToken cancellationToken)
        {
            var resultado = new List<ClientesBusquedaVinculoVm>();

            limite = Math.Clamp(limite, 1, 100);

            await using var command = new SqlCommand(
                "dbo.usp_Clientes_BuscarParaVinculo",
                connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add(
                "@EmpresaID",
                SqlDbType.Int).Value = empresaId;

            AddNullableNVarChar(
                command,
                "@Buscar",
                200,
                buscar);

            command.Parameters.Add(
                "@Limite",
                SqlDbType.Int).Value = limite;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(new ClientesBusquedaVinculoVm
                {
                    ClienteID = ReadInt(reader, "ClienteID"),
                    CodigoCliente = ReadNullableString(
                        reader,
                        "CodigoCliente"),
                    NombreComercial = ReadString(
                        reader,
                        "NombreComercial"),
                    RazonSocial = ReadNullableString(
                        reader,
                        "RazonSocial"),
                    EsBioHypor = ReadBool(
                        reader,
                        "EsBioHypor"),
                    NombreCoincidente = ReadNullableString(
                        reader,
                        "NombreCoincidente"),
                    TipoNombre = ReadNullableString(
                        reader,
                        "TipoNombre")
                });
            }

            return resultado;
        }

        private static async Task<string> ObtenerEmpresaNombreAsync(
            SqlConnection connection,
            int empresaId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1)
    Nombre
FROM dbo.Empresas
WHERE EmpresaID=@EmpresaID;";

            await using var command = new SqlCommand(sql, connection);

            command.Parameters.Add(
                "@EmpresaID",
                SqlDbType.Int).Value = empresaId;

            var value = await command.ExecuteScalarAsync(cancellationToken);

            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value) ?? string.Empty;
        }

        private IActionResult JsonErrorModelo()
        {
            var errores = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors)
                .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage)
                    ? "Uno o más datos no son válidos."
                    : x.ErrorMessage)
                .Distinct()
                .ToList();

            return Json(new
            {
                success = false,
                message = errores.FirstOrDefault()
                    ?? "Revisa los datos capturados.",
                errors = errores
            });
        }

        private static void AddNullableNVarChar(
            SqlCommand command,
            string nombre,
            int longitud,
            string? valor)
        {
            var parameter = command.Parameters.Add(
                nombre,
                SqlDbType.NVarChar,
                longitud);

            parameter.Value = string.IsNullOrWhiteSpace(valor)
                ? DBNull.Value
                : valor.Trim();
        }

        private static void AddNullableDecimal(
            SqlCommand command,
            string nombre,
            decimal? valor,
            byte precision,
            byte scale)
        {
            var parameter = command.Parameters.Add(
                nombre,
                SqlDbType.Decimal);

            parameter.Precision = precision;
            parameter.Scale = scale;

            parameter.Value = valor.HasValue
                ? valor.Value
                : DBNull.Value;
        }

        private static int Ordinal(
            SqlDataReader reader,
            string nombre)
        {
            return reader.GetOrdinal(nombre);
        }

        private static bool IsNull(
            SqlDataReader reader,
            string nombre)
        {
            return reader.IsDBNull(
                Ordinal(reader, nombre));
        }

        private static string ReadString(
            SqlDataReader reader,
            string nombre)
        {
            return IsNull(reader, nombre)
                ? string.Empty
                : Convert.ToString(reader[nombre])
                  ?? string.Empty;
        }

        private static string? ReadNullableString(
            SqlDataReader reader,
            string nombre)
        {
            return IsNull(reader, nombre)
                ? null
                : Convert.ToString(reader[nombre]);
        }

        private static int ReadInt(
            SqlDataReader reader,
            string nombre)
        {
            return IsNull(reader, nombre)
                ? 0
                : Convert.ToInt32(reader[nombre]);
        }

        private static int? ReadNullableInt(
            SqlDataReader reader,
            string nombre)
        {
            return IsNull(reader, nombre)
                ? null
                : Convert.ToInt32(reader[nombre]);
        }

        private static decimal? ReadNullableDecimal(
            SqlDataReader reader,
            string nombre)
        {
            return IsNull(reader, nombre)
                ? null
                : Convert.ToDecimal(reader[nombre]);
        }

        private static bool ReadBool(
            SqlDataReader reader,
            string nombre)
        {
            return !IsNull(reader, nombre)
                   && Convert.ToBoolean(reader[nombre]);
        }

        private static DateTime ReadDateTime(
            SqlDataReader reader,
            string nombre)
        {
            return IsNull(reader, nombre)
                ? DateTime.MinValue
                : Convert.ToDateTime(reader[nombre]);
        }

        private static DateTime? ReadNullableDateTime(
            SqlDataReader reader,
            string nombre)
        {
            return IsNull(reader, nombre)
                ? null
                : Convert.ToDateTime(reader[nombre]);
        }
    }
}