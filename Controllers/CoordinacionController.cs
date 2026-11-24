using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
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
    public partial class CoordinacionController : Controller
    {
        private readonly string _connectionString;
        private const int RolAdministradorId = 1;
        private const string UrlPedidosDosis = "/Coordinacion/PedidosDosis";
        private const string UrlPedidosHembras = "/Coordinacion/PedidosHembras";
        private const string UrlPedidosSementales = "/Coordinacion/PedidosSementales";
        private const string UrlPedidosRastro = "/Coordinacion/PedidosRastro";
        private const string UrlPedidosFueraDeTiempo = "/Coordinacion/PedidosFueraDeTiempo";
        private const string UrlHistoricoPedidos = "/Coordinacion/HistoricoPedidos";

        public CoordinacionController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");
        }

        private int? UsuarioID => HttpContext.Session.GetInt32("UsuarioID");

        private string UsuarioNombre =>
            HttpContext.Session.GetString("NombreMostrar")
            ?? HttpContext.Session.GetString("NombreCompleto")
            ?? HttpContext.Session.GetString("Username")
            ?? User?.Identity?.Name
            ?? "Usuario";

        private int? ObtenerEmpresaID()
        {
            var empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId.HasValue && empresaId.Value > 0) return empresaId.Value;
            var empresaTexto = HttpContext.Session.GetString("EmpresaId");
            if (int.TryParse(empresaTexto, out var empresaParseada) && empresaParseada > 0) return empresaParseada;
            empresaTexto = HttpContext.Session.GetString("EmpresaID");
            return int.TryParse(empresaTexto, out empresaParseada) && empresaParseada > 0 ? empresaParseada : null;
        }

        private async Task<SqlConnection> AbrirAsync(CancellationToken cancellationToken)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private async Task<IActionResult?> ValidarAccesoAsync(string urlSubMenu, string accion, CancellationToken cancellationToken)
        {
            if (!UsuarioID.HasValue || UsuarioID.Value <= 0)
                return RedirectToAction("Login", "Login");
            var empresaId = ObtenerEmpresaID();
            if (!empresaId.HasValue)
            {
                TempData["ErrorMessage"] = "Selecciona una empresa antes de ingresar a Coordinación.";
                return RedirectToAction("Index", "Menu");
            }
            await using var connection = await AbrirAsync(cancellationToken);
            if (!await UsuarioPerteneceEmpresaAsync(connection, UsuarioID.Value, empresaId.Value, cancellationToken))
                return Forbid();
            if (!await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaId.Value, urlSubMenu, accion, cancellationToken))
                return Forbid();
            return null;
        }

        private static async Task<bool> UsuarioPerteneceEmpresaAsync(SqlConnection connection, int usuarioId, int empresaId, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT CAST(CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.UsuariosEmpresas UE
    INNER JOIN dbo.Empresas E ON E.EmpresaID=UE.EmpresaID
    INNER JOIN dbo.Usuarios U ON U.UsuarioID=UE.UsuarioID
    WHERE UE.UsuarioID=@UsuarioID
      AND UE.EmpresaID=@EmpresaID
      AND UE.Activo=1
      AND E.Activa=1
      AND U.Activo=1
) THEN 1 ELSE 0 END AS bit);";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value != DBNull.Value && Convert.ToBoolean(value);
        }

        private static async Task<bool> TienePermisoAccionAsync(SqlConnection connection, int usuarioId, int empresaId, string urlSubMenu, string accion, CancellationToken cancellationToken)
        {
            const string sql = @"
DECLARE @SubMenuID INT;
DECLARE @RolID INT;
SELECT @RolID=RolID FROM dbo.Usuarios WHERE UsuarioID=@UsuarioID AND Activo=1;
SELECT TOP(1) @SubMenuID=SM.SubMenuID
FROM dbo.SubMenus SM
WHERE SM.Activo=1
  AND LOWER(REPLACE(LTRIM(RTRIM(ISNULL(SM.UrlEnlace,''))),'~',''))=LOWER(REPLACE(LTRIM(RTRIM(@UrlSubMenu)),'~',''));
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
        ) THEN 0
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
              AND LTRIM(RTRIM(A.Nombre)) COLLATE Latin1_General_CI_AI=LTrim(RTRIM(@Accion)) COLLATE Latin1_General_CI_AI
        ) THEN 1
        ELSE 0
    END AS bit
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
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
            var empresaId = ObtenerEmpresaID();
            if (!empresaId.HasValue)
            {
                TempData["ErrorMessage"] = "Selecciona una empresa antes de ingresar a Coordinación.";
                return RedirectToAction("Index", "Menu");
            }
            await using var connection = await AbrirAsync(cancellationToken);
            if (!await UsuarioPerteneceEmpresaAsync(connection, UsuarioID.Value, empresaId.Value, cancellationToken)) return Forbid();
            var opciones = new (string Url, string Accion)[]
            {
                (UrlProgramacion,nameof(ProgramacionSemanal)),
                (UrlPedidosDosis,nameof(PedidosDosis)),
                (UrlPedidosHembras,nameof(PedidosHembras)),
                (UrlPedidosSementales,nameof(PedidosSementales)),
                (UrlPedidosRastro,nameof(PedidosRastro)),
                (UrlPedidosFueraDeTiempo,nameof(PedidosFueraDeTiempo)),
                (UrlHistoricoPedidos,nameof(HistoricoPedidos))
            };
            foreach (var opcion in opciones)
            {
                if (await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaId.Value, opcion.Url, "Ver", cancellationToken)) return RedirectToAction(opcion.Accion);
            }
            return Forbid();
        }

        [HttpGet]
        public async Task<IActionResult> Pedidos([FromQuery] CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
            var empresaId = ObtenerEmpresaID();
            if (!empresaId.HasValue)
            {
                TempData["ErrorMessage"] = "Selecciona una empresa antes de ingresar a Coordinación.";
                return RedirectToAction("Index", "Menu");
            }
            filtros ??= new CoordinacionFiltroPedidosVm();
            await using var connection = await AbrirAsync(cancellationToken);
            if (!await UsuarioPerteneceEmpresaAsync(connection, UsuarioID.Value, empresaId.Value, cancellationToken)) return Forbid();
            var opciones = new (string Url, string Accion)[]
            {
                (UrlPedidosDosis,nameof(PedidosDosis)),
                (UrlPedidosHembras,nameof(PedidosHembras)),
                (UrlPedidosSementales,nameof(PedidosSementales)),
                (UrlPedidosRastro,nameof(PedidosRastro))
            };
            var routeValues = new
            {
                filtros.EstatusPedidoID,
                filtros.ClienteID,
                filtros.FechaDesde,
                filtros.FechaHasta,
                filtros.Buscar
            };
            foreach (var opcion in opciones)
            {
                if (await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaId.Value, opcion.Url, "Ver", cancellationToken)) return RedirectToAction(opcion.Accion, routeValues);
            }
            if (await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaId.Value, UrlPedidosFueraDeTiempo, "Ver", cancellationToken)) return RedirectToAction(nameof(PedidosFueraDeTiempo), routeValues);
            if (await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaId.Value, UrlHistoricoPedidos, "Ver", cancellationToken)) return RedirectToAction(nameof(HistoricoPedidos), routeValues);
            return Forbid();
        }

        [HttpGet]
        public Task<IActionResult> PedidosDosis([FromQuery] CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            return CargarBandejaTipoAsync("DOSIS", UrlPedidosDosis, filtros, cancellationToken);
        }

        [HttpGet]
        public Task<IActionResult> PedidosHembras([FromQuery] CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            return CargarBandejaTipoAsync("HEMBRAS", UrlPedidosHembras, filtros, cancellationToken);
        }

        [HttpGet]
        public Task<IActionResult> PedidosSementales([FromQuery] CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            return CargarBandejaTipoAsync("SEMENTALES", UrlPedidosSementales, filtros, cancellationToken);
        }

        [HttpGet]
        public Task<IActionResult> PedidosRastro([FromQuery] CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            return CargarBandejaTipoAsync("RASTRO", UrlPedidosRastro, filtros, cancellationToken);
        }

        [HttpGet]
        public async Task<IActionResult> PedidosFueraDeTiempo([FromQuery] CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(UrlPedidosFueraDeTiempo, "Ver", cancellationToken);
            if (acceso != null) return acceso;
            var empresaId = ObtenerEmpresaID()!.Value;
            filtros ??= new CoordinacionFiltroPedidosVm();
            filtros.SoloFueraDeTiempo = true;
            filtros.Buscar = string.IsNullOrWhiteSpace(filtros.Buscar) ? null : filtros.Buscar.Trim();
            var model = await CargarPaginaPedidosAsync(empresaId, filtros, cancellationToken);
            ViewData["ModoBandeja"] = "FueraDeTiempo";
            return View("Pedidos", model);
        }

        [HttpGet]
        public async Task<IActionResult> HistoricoPedidos([FromQuery] CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(UrlHistoricoPedidos, "Ver", cancellationToken);
            if (acceso != null) return acceso;
            var empresaId = ObtenerEmpresaID()!.Value;
            filtros ??= new CoordinacionFiltroPedidosVm();
            filtros.Buscar = string.IsNullOrWhiteSpace(filtros.Buscar) ? null : filtros.Buscar.Trim();
            var model = await CargarPaginaPedidosAsync(empresaId, filtros, cancellationToken);
            model.Pedidos = model.Pedidos
                .Where(x => string.Equals(x.EstatusCodigo, "CERRADO", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(x.EstatusCodigo, "CANCELADO", StringComparison.OrdinalIgnoreCase))
                .ToList();
            model.Resumen = new CoordinacionResumenPedidosVm { TotalPedidos = model.Pedidos.Count };
            ViewData["ModoBandeja"] = "Historico";
            return View("Pedidos", model);
        }

        [HttpGet]
        public async Task<IActionResult> NuevoPedido(int? tipoPedidoId = null, int? clienteId = null, CancellationToken cancellationToken = default)
        {
            if (!tipoPedidoId.HasValue || tipoPedidoId.Value <= 0)
                return BadRequest("Debes indicar el tipo de pedido.");
            var acceso = await ValidarAccesoTipoPedidoAsync(tipoPedidoId.Value, "Crear", cancellationToken);
            if (acceso != null) return acceso;
            var empresaId = ObtenerEmpresaID()!.Value;
            await using var connection = await AbrirAsync(cancellationToken);
            var catalogos = await ObtenerCatalogosPedidoAsync(connection, empresaId, cancellationToken);
            catalogos.Productos = await ObtenerProductosPorTipoPedidoAsync(connection, tipoPedidoId.Value, cancellationToken);
            if (clienteId.HasValue && clienteId.Value > 0)
                catalogos.UbicacionesCliente = await ObtenerUbicacionesClienteAsync(connection, clienteId.Value, cancellationToken);
            var model = new CoordinacionCrearPedidoVm
            {
                EmpresaID = empresaId,
                TipoPedidoID = tipoPedidoId.Value,
                ClienteID = clienteId,
                FechaSolicitud = DateTime.Now,
                OrigenPedidoID = catalogos.Origenes.FirstOrDefault(x => string.Equals(x.Codigo, "COORDINACION", StringComparison.OrdinalIgnoreCase))?.OrigenPedidoID ?? 0,
                PrioridadPedidoID = catalogos.Prioridades.FirstOrDefault(x => string.Equals(x.Codigo, "NORMAL", StringComparison.OrdinalIgnoreCase))?.PrioridadPedidoID,
                Catalogos = catalogos,
                Detalles = new List<CoordinacionPedidoRenglonCapturaVm> { new() { Renglon = 1 } }
            };
            ViewData["TipoPedidoCodigo"] = catalogos.TiposPedido.FirstOrDefault(x => x.TipoPedidoID == tipoPedidoId.Value)?.Codigo;
            ViewData["TipoPedidoNombre"] = catalogos.TiposPedido.FirstOrDefault(x => x.TipoPedidoID == tipoPedidoId.Value)?.Nombre;
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerCatalogosPedido(int tipoPedidoId, CancellationToken cancellationToken)
        {
            if (tipoPedidoId <= 0) return BadRequest(new { ok = false, mensaje = "El tipo de pedido no es válido." });
            var acceso = await ValidarAccesoTipoPedidoAsync(tipoPedidoId, "Crear", cancellationToken);
            if (acceso != null) return acceso;
            var empresaId = ObtenerEmpresaID()!.Value;
            await using var connection = await AbrirAsync(cancellationToken);
            var catalogos = await ObtenerCatalogosPedidoAsync(connection, empresaId, cancellationToken);
            catalogos.Productos = await ObtenerProductosPorTipoPedidoAsync(connection, tipoPedidoId, cancellationToken);
            return Json(new { ok = true, catalogos });
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerProductosPorTipoPedido(int tipoPedidoId, CancellationToken cancellationToken)
        {
            if (tipoPedidoId <= 0) return BadRequest(new { ok = false, mensaje = "El tipo de pedido no es válido." });
            var acceso = await ValidarAccesoTipoPedidoAsync(tipoPedidoId, "Crear", cancellationToken);
            if (acceso != null) return acceso;
            try
            {
                await using var connection = await AbrirAsync(cancellationToken);
                var productos = await ObtenerProductosPorTipoPedidoAsync(connection, tipoPedidoId, cancellationToken);
                return Json(new { ok = true, productos });
            }
            catch (SqlException ex)
            {
                return BadRequest(new { ok = false, mensaje = LimpiarMensajeSql(ex.Message) });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerUbicacionesCliente(int clienteId, int tipoPedidoId, CancellationToken cancellationToken)
        {
            if (tipoPedidoId <= 0) return BadRequest(new { ok = false, mensaje = "El tipo de pedido no es válido." });
            var acceso = await ValidarAccesoTipoPedidoAsync(tipoPedidoId, "Crear", cancellationToken);
            if (acceso != null) return acceso;
            if (clienteId <= 0) return Json(new { ok = true, ubicaciones = Array.Empty<object>() });
            await using var connection = await AbrirAsync(cancellationToken);
            var ubicaciones = await ObtenerUbicacionesClienteAsync(connection, clienteId, cancellationToken);
            return Json(new { ok = true, ubicaciones });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearPedido([FromBody] CoordinacionCrearPedidoVm model, CancellationToken cancellationToken)
        {
            if (model == null) return BadRequest(new { ok = false, mensaje = "No se recibió la información del pedido." });
            if (model.TipoPedidoID <= 0) return BadRequest(new { ok = false, mensaje = "El tipo de pedido no es válido." });
            var acceso = await ValidarAccesoTipoPedidoAsync(model.TipoPedidoID, "Crear", cancellationToken);
            if (acceso != null) return acceso;
            var usuarioId = UsuarioID!.Value;
            var empresaId = ObtenerEmpresaID()!.Value;
            model.EmpresaID = empresaId;
            NormalizarPedido(model);
            ValidarPedidoEnControlador(model);
            if (!ModelState.IsValid) return BadRequest(new { ok = false, mensaje = ObtenerErroresModelState() });
            try
            {
                await using var connection = await AbrirAsync(cancellationToken);
                var resultado = await CrearPedidoAsync(connection, model, usuarioId, UsuarioNombre, cancellationToken);
                return Json(new
                {
                    ok = true,
                    mensaje = model.GuardarComoBorrador ? "Pedido guardado como borrador." : "Pedido creado correctamente.",
                    pedido = resultado,
                    redirectUrl = Url.Action(nameof(DetallePedido), new { id = resultado.PedidoID })
                });
            }
            catch (SqlException ex)
            {
                return BadRequest(new { ok = false, mensaje = LimpiarMensajeSql(ex.Message) });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible crear el pedido.", detalle = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DetallePedido(int id, CancellationToken cancellationToken)
        {
            if (id <= 0) return NotFound();
            var acceso = await ValidarAccesoPedidoAsync(id, "Ver", cancellationToken);
            if (acceso != null) return acceso;
            try
            {
                await using var connection = await AbrirAsync(cancellationToken);
                var empresaPedidoId = await ObtenerEmpresaPedidoAsync(connection, id, cancellationToken);
                if (!empresaPedidoId.HasValue) return NotFound();
                var model = await ObtenerDetallePedidoAsync(connection, id, empresaPedidoId.Value, cancellationToken);
                if (model.Pedido.PedidoID <= 0) return NotFound();
                ViewData["TipoPedidoCodigo"] = model.Pedido.TipoPedidoCodigo;
                ViewData["TipoPedidoNombre"] = model.Pedido.TipoPedido;
                return View(model);
            }
            catch (SqlException ex) when (ex.Number == 53200)
            {
                return NotFound();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstatus([FromBody] CoordinacionCambiarEstatusPedidoVm model, CancellationToken cancellationToken)
        {
            if (model == null) return BadRequest(new { ok = false, mensaje = "No se recibió la información del cambio de estatus." });
            if (model.PedidoID <= 0) return BadRequest(new { ok = false, mensaje = "El pedido no es válido." });
            var acceso = await ValidarAccesoPedidoAsync(model.PedidoID, "Editar", cancellationToken);
            if (acceso != null) return acceso;
            model.NuevoEstatusCodigo = (model.NuevoEstatusCodigo ?? string.Empty).Trim().ToUpperInvariant();
            model.Observaciones = string.IsNullOrWhiteSpace(model.Observaciones) ? null : model.Observaciones.Trim();
            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, mensaje = ObtenerErroresModelState() });
            try
            {
                var empresaId = ObtenerEmpresaID()!.Value;
                await using var connection = await AbrirAsync(cancellationToken);
                var resultado = await CambiarEstatusPedidoAsync(connection, model, empresaId, UsuarioID!.Value, UsuarioNombre, cancellationToken);
                return Json(new { ok = true, mensaje = "Estatus actualizado correctamente.", pedido = resultado });
            }
            catch (SqlException ex)
            {
                return BadRequest(new { ok = false, mensaje = LimpiarMensajeSql(ex.Message) });
            }
        }

        private async Task<IActionResult?> ValidarAccesoTipoPedidoAsync(int tipoPedidoId, string accion, CancellationToken cancellationToken)
        {
            if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
            var empresaId = ObtenerEmpresaID();
            if (!empresaId.HasValue)
            {
                TempData["ErrorMessage"] = "Selecciona una empresa antes de ingresar a Coordinación.";
                return RedirectToAction("Index", "Menu");
            }
            await using var connection = await AbrirAsync(cancellationToken);
            if (!await UsuarioPerteneceEmpresaAsync(connection, UsuarioID.Value, empresaId.Value, cancellationToken)) return Forbid();
            var codigo = await ObtenerCodigoTipoPedidoAsync(connection, tipoPedidoId, cancellationToken);
            if (string.IsNullOrWhiteSpace(codigo)) return BadRequest(new { ok = false, mensaje = "El tipo de pedido no existe o está inactivo." });
            var url = ObtenerUrlSubMenuPorTipo(codigo);
            if (string.IsNullOrWhiteSpace(url)) return BadRequest(new { ok = false, mensaje = "El tipo de pedido no tiene un submenú configurado." });
            if (!await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaId.Value, url, accion, cancellationToken)) return Forbid();
            return null;
        }

        private async Task<IActionResult?> ValidarAccesoPedidoAsync(int pedidoId, string accion, CancellationToken cancellationToken)
        {
            if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
            var empresaSesionId = ObtenerEmpresaID();
            if (!empresaSesionId.HasValue)
            {
                TempData["ErrorMessage"] = "Selecciona una empresa antes de ingresar a Coordinación.";
                return RedirectToAction("Index", "Menu");
            }
            await using var connection = await AbrirAsync(cancellationToken);
            if (!await UsuarioPerteneceEmpresaAsync(connection, UsuarioID.Value, empresaSesionId.Value, cancellationToken)) return Forbid();
            var info = await ObtenerInfoAccesoPedidoAsync(connection, pedidoId, cancellationToken);
            if (!info.HasValue) return NotFound();
            var urlTipo = ObtenerUrlSubMenuPorTipo(info.Value.TipoPedidoCodigo);
            if (!string.IsNullOrWhiteSpace(urlTipo) && await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaSesionId.Value, urlTipo, accion, cancellationToken)) return null;
            if (info.Value.FueraDeTiempo && await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaSesionId.Value, UrlPedidosFueraDeTiempo, accion, cancellationToken)) return null;
            var esHistorico = string.Equals(info.Value.EstatusCodigo, "CERRADO", StringComparison.OrdinalIgnoreCase) || string.Equals(info.Value.EstatusCodigo, "CANCELADO", StringComparison.OrdinalIgnoreCase);
            if (esHistorico && string.Equals(accion, "Ver", StringComparison.OrdinalIgnoreCase) && await TienePermisoAccionAsync(connection, UsuarioID.Value, empresaSesionId.Value, UrlHistoricoPedidos, "Ver", cancellationToken)) return null;
            return Forbid();
        }

        private static async Task<int?> ObtenerEmpresaPedidoAsync(SqlConnection connection, int pedidoId, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1) EmpresaID
FROM dbo.Nutri_Pedidos
WHERE PedidoID=@PedidoID
  AND Activo=1;";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@PedidoID", SqlDbType.Int).Value = pedidoId;
            var resultado = await command.ExecuteScalarAsync(cancellationToken);
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToInt32(resultado);
        }

        private static async Task<string?> ObtenerCodigoTipoPedidoAsync(SqlConnection connection, int tipoPedidoId, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1) Codigo
FROM dbo.Nutri_TiposPedido
WHERE TipoPedidoID=@TipoPedidoID
  AND Activo=1;";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@TipoPedidoID", SqlDbType.Int).Value = tipoPedidoId;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value == null || value == DBNull.Value ? null : Convert.ToString(value)?.Trim().ToUpperInvariant();
        }

        private static async Task<(string TipoPedidoCodigo, bool FueraDeTiempo, string EstatusCodigo, int EmpresaID)?> ObtenerInfoAccesoPedidoAsync(SqlConnection connection, int pedidoId, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1)
    P.EmpresaID,
    TP.Codigo AS TipoPedidoCodigo,
    P.FueraDeTiempo,
    EP.Codigo AS EstatusCodigo
FROM dbo.Nutri_Pedidos P
INNER JOIN dbo.Nutri_TiposPedido TP ON TP.TipoPedidoID=P.TipoPedidoID
INNER JOIN dbo.Nutri_EstatusPedido EP ON EP.EstatusPedidoID=P.EstatusPedidoID
WHERE P.PedidoID=@PedidoID
  AND P.Activo=1;";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@PedidoID", SqlDbType.Int).Value = pedidoId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return
            (
                ReadString(reader, "TipoPedidoCodigo"),
                ReadBool(reader, "FueraDeTiempo"),
                ReadString(reader, "EstatusCodigo"),
                ReadInt(reader, "EmpresaID")
            );
        }

        private static string? ObtenerUrlSubMenuPorTipo(string? codigoTipo)
        {
            return codigoTipo?.Trim().ToUpperInvariant() switch
            {
                "DOSIS" => UrlPedidosDosis,
                "HEMBRAS" => UrlPedidosHembras,
                "SEMENTALES" => UrlPedidosSementales,
                "RASTRO" => UrlPedidosRastro,
                _ => null
            };
        }
        private async Task<CoordinacionPedidosIndexVm> CargarPaginaPedidosAsync(int empresaId, CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            var model = new CoordinacionPedidosIndexVm { Filtros = filtros };
            await using var connection = await AbrirAsync(cancellationToken);
            var catalogos = await ObtenerCatalogosPedidoAsync(connection, empresaId, cancellationToken);
            model.TiposPedido = catalogos.TiposPedido;
            model.Clientes = catalogos.Clientes;
            model.EstatusPedido = await ObtenerEstatusPedidoAsync(connection, cancellationToken);
            model.Resumen = await ObtenerResumenPedidosAsync(connection, empresaId, cancellationToken);
            if (filtros.FechaDesde.HasValue && filtros.FechaHasta.HasValue && filtros.FechaHasta.Value.Date < filtros.FechaDesde.Value.Date)
            {
                ViewData["ErrorFiltros"] = "La fecha final no puede ser anterior a la fecha inicial.";
                return model;
            }
            model.Pedidos = await ObtenerBandejaPedidosAsync(connection, empresaId, filtros, cancellationToken);
            return model;
        }

        private static async Task<CoordinacionResumenPedidosVm> ObtenerResumenPedidosAsync(SqlConnection connection, int empresaId, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("dbo.usp_Coordinacion_ObtenerResumenPedidos", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return new CoordinacionResumenPedidosVm();
            return new CoordinacionResumenPedidosVm
            {
                TotalPedidos = ReadInt(reader, "TotalPedidos"),
                Recibidos = ReadInt(reader, "Recibidos"),
                PendientesValidacion = ReadInt(reader, "PendientesValidacion"),
                PendientesProgramar = ReadInt(reader, "PendientesProgramar"),
                Programados = ReadInt(reader, "Programados"),
                FueraDeTiempo = ReadInt(reader, "FueraDeTiempo")
            };
        }

        private static async Task<List<CoordinacionPedidoBandejaVm>> ObtenerBandejaPedidosAsync(SqlConnection connection, int empresaId, CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            var resultado = new List<CoordinacionPedidoBandejaVm>();
            await using var command = new SqlCommand("dbo.usp_Coordinacion_ObtenerBandejaPedidos", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 90
            };
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            AddNullableInt(command, "@TipoPedidoID", filtros.TipoPedidoID);
            AddNullableInt(command, "@EstatusPedidoID", filtros.EstatusPedidoID);
            AddNullableInt(command, "@ClienteID", filtros.ClienteID);
            AddNullableDate(command, "@FechaDesde", filtros.FechaDesde);
            AddNullableDate(command, "@FechaHasta", filtros.FechaHasta);
            var pFuera = command.Parameters.Add("@SoloFueraDeTiempo", SqlDbType.Bit);
            pFuera.Value = filtros.SoloFueraDeTiempo.HasValue ? (object)filtros.SoloFueraDeTiempo.Value : DBNull.Value;
            var pBuscar = command.Parameters.Add("@Buscar", SqlDbType.NVarChar, 150);
            pBuscar.Value = string.IsNullOrWhiteSpace(filtros.Buscar) ? DBNull.Value : filtros.Buscar.Trim();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(new CoordinacionPedidoBandejaVm
                {
                    PedidoID = ReadInt(reader, "PedidoID"),
                    Folio = ReadString(reader, "Folio"),
                    EmpresaID = ReadInt(reader, "EmpresaID"),
                    TipoPedidoID = ReadInt(reader, "TipoPedidoID"),
                    TipoPedidoCodigo = ReadString(reader, "TipoPedidoCodigo"),
                    TipoPedido = ReadString(reader, "TipoPedido"),
                    ClienteID = ReadNullableInt(reader, "ClienteID"),
                    Cliente = ReadString(reader, "Cliente"),
                    ClienteUbicacionID = ReadNullableInt(reader, "ClienteUbicacionID"),
                    CentroDestinoID = ReadNullableInt(reader, "CentroDestinoID"),
                    Destino = ReadString(reader, "Destino"),
                    FechaSolicitud = ReadDateTime(reader, "FechaSolicitud"),
                    FechaRequerida = ReadNullableDateTime(reader, "FechaRequerida"),
                    PrioridadPedidoID = ReadInt(reader, "PrioridadPedidoID"),
                    PrioridadCodigo = ReadString(reader, "PrioridadCodigo"),
                    Prioridad = ReadString(reader, "Prioridad"),
                    EstatusPedidoID = ReadInt(reader, "EstatusPedidoID"),
                    EstatusCodigo = ReadString(reader, "EstatusCodigo"),
                    Estatus = ReadString(reader, "Estatus"),
                    ColorHex = ReadNullableString(reader, "ColorHex"),
                    OrigenCodigo = ReadString(reader, "OrigenCodigo"),
                    Origen = ReadString(reader, "Origen"),
                    ReferenciaExterna = ReadNullableString(reader, "ReferenciaExterna"),
                    FueraDeTiempo = ReadBool(reader, "FueraDeTiempo"),
                    MotivoFueraDeTiempo = ReadNullableString(reader, "MotivoFueraDeTiempo"),
                    Observaciones = ReadNullableString(reader, "Observaciones"),
                    TotalRenglones = ReadInt(reader, "TotalRenglones"),
                    CantidadTotal = ReadDecimal(reader, "CantidadTotal"),
                    TieneProgramacion = ReadBool(reader, "TieneProgramacion"),
                    ProgramacionID = ReadNullableInt(reader, "ProgramacionID"),
                    ProximaFechaProgramada = ReadNullableDateTime(reader, "ProximaFechaProgramada")
                });
            }
            return resultado;
        }

        private static async Task<CoordinacionCatalogosPedidoVm> ObtenerCatalogosPedidoAsync(SqlConnection connection, int empresaId, CancellationToken cancellationToken)
        {
            var model = new CoordinacionCatalogosPedidoVm();
            await using var command = new SqlCommand("dbo.usp_Nutri_ObtenerCatalogosPedido", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                model.TiposPedido.Add(new CoordinacionTipoPedidoVm
                {
                    TipoPedidoID = ReadInt(reader, "TipoPedidoID"),
                    Codigo = ReadString(reader, "Codigo"),
                    Nombre = ReadString(reader, "Nombre"),
                    TipoProductoID = ReadNullableInt(reader, "TipoProductoID"),
                    RequiereLineaGenetica = ReadBool(reader, "RequiereLineaGenetica")
                });
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.Prioridades.Add(new CoordinacionPrioridadPedidoVm
                    {
                        PrioridadPedidoID = ReadInt(reader, "PrioridadPedidoID"),
                        Codigo = ReadString(reader, "Codigo"),
                        Nombre = ReadString(reader, "Nombre"),
                        Orden = ReadInt(reader, "Orden")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.Origenes.Add(new CoordinacionOrigenPedidoVm
                    {
                        OrigenPedidoID = ReadInt(reader, "OrigenPedidoID"),
                        Codigo = ReadString(reader, "Codigo"),
                        Nombre = ReadString(reader, "Nombre")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.Clientes.Add(new CoordinacionClienteVm
                    {
                        ClienteID = ReadInt(reader, "ClienteID"),
                        CodigoCliente = ReadNullableString(reader, "CodigoCliente"),
                        NombreComercial = ReadString(reader, "NombreComercial"),
                        RazonSocial = ReadNullableString(reader, "RazonSocial"),
                        EsBioHypor = ReadBool(reader, "EsBioHypor")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.LineasGeneticas.Add(new CoordinacionLineaGeneticaVm
                    {
                        LineaGeneticaID = ReadInt(reader, "LineaGeneticaID"),
                        Codigo = ReadString(reader, "Codigo"),
                        Nombre = ReadString(reader, "Nombre")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.TiposHembra.Add(new CoordinacionTipoHembraVm
                    {
                        TipoHembraID = ReadInt(reader, "TipoHembraID"),
                        Codigo = ReadString(reader, "Codigo"),
                        Nombre = ReadString(reader, "Nombre"),
                        Descripcion = ReadNullableString(reader, "Descripcion")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.CentrosOperacion.Add(new CoordinacionCentroOperacionVm
                    {
                        CentroOperacionID = ReadInt(reader, "CentroOperacionID"),
                        EmpresaID = ReadInt(reader, "EmpresaID"),
                        TipoCentroOperacionID = ReadInt(reader, "TipoCentroOperacionID"),
                        TipoCentroCodigo = ReadString(reader, "TipoCentroCodigo"),
                        TipoCentro = ReadString(reader, "TipoCentro"),
                        Codigo = ReadNullableString(reader, "Codigo"),
                        Nombre = ReadString(reader, "Nombre"),
                        Municipio = ReadNullableString(reader, "Municipio"),
                        Estado = ReadNullableString(reader, "Estado")
                    });
                }
            }
            return model;
        }

        private static async Task<List<CoordinacionEstatusPedidoVm>> ObtenerEstatusPedidoAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT EstatusPedidoID,Codigo,Nombre,Orden,EsFinal,ColorHex
FROM dbo.Nutri_EstatusPedido
WHERE Activo=1
ORDER BY Orden,Nombre;";
            var resultado = new List<CoordinacionEstatusPedidoVm>();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(new CoordinacionEstatusPedidoVm
                {
                    EstatusPedidoID = ReadInt(reader, "EstatusPedidoID"),
                    Codigo = ReadString(reader, "Codigo"),
                    Nombre = ReadString(reader, "Nombre"),
                    Orden = ReadInt(reader, "Orden"),
                    EsFinal = ReadBool(reader, "EsFinal"),
                    ColorHex = ReadNullableString(reader, "ColorHex")
                });
            }
            return resultado;
        }

        private static async Task<List<CoordinacionProductoVm>> ObtenerProductosPorTipoPedidoAsync(SqlConnection connection, int tipoPedidoId, CancellationToken cancellationToken)
        {
            var resultado = new List<CoordinacionProductoVm>();
            await using var command = new SqlCommand("dbo.usp_Nutri_ObtenerProductosPorTipoPedido", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            command.Parameters.Add("@TipoPedidoID", SqlDbType.Int).Value = tipoPedidoId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(new CoordinacionProductoVm
                {
                    ProductoID = ReadInt(reader, "ProductoID"),
                    TipoProductoID = ReadInt(reader, "TipoProductoID"),
                    Codigo = ReadString(reader, "Codigo"),
                    Nombre = ReadString(reader, "Nombre"),
                    Descripcion = ReadNullableString(reader, "Descripcion"),
                    UnidadMedida = ReadNullableString(reader, "UnidadMedida"),
                    RequiereLineaGenetica = ReadBool(reader, "RequiereLineaGenetica")
                });
            }
            return resultado;
        }

        private static async Task<List<CoordinacionUbicacionClienteVm>> ObtenerUbicacionesClienteAsync(SqlConnection connection, int clienteId, CancellationToken cancellationToken)
        {
            var resultado = new List<CoordinacionUbicacionClienteVm>();
            await using var command = new SqlCommand("dbo.usp_Nutri_ObtenerUbicacionesCliente", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            command.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                resultado.Add(new CoordinacionUbicacionClienteVm
                {
                    ClienteUbicacionID = ReadInt(reader, "ClienteUbicacionID"),
                    ClienteID = ReadInt(reader, "ClienteID"),
                    TipoUbicacionClienteID = ReadNullableInt(reader, "TipoUbicacionClienteID"),
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
                    Telefono = ReadNullableString(reader, "Telefono")
                });
            }
            return resultado;
        }

        private static async Task<CoordinacionResultadoCrearPedidoVm> CrearPedidoAsync(SqlConnection connection, CoordinacionCrearPedidoVm model, int usuarioId, string usuarioNombre, CancellationToken cancellationToken)
        {
            var detallesJson = JsonSerializer.Serialize(model.Detalles.Select((x, index) => new
            {
                renglon = x.Renglon > 0 ? x.Renglon : index + 1,
                productoId = x.ProductoID,
                lineaGeneticaId = x.LineaGeneticaID,
                cantidadSolicitada = x.CantidadSolicitada,
                pesoMinimoKg = x.PesoMinimoKg,
                pesoMaximoKg = x.PesoMaximoKg,
                especificaciones = x.Especificaciones,
                observaciones = x.Observaciones,
                fechaProduccionSolicitada = x.FechaProduccionSolicitada?.ToString("yyyy-MM-dd"),
                especificacionesDosis = x.EspecificacionesDosis,
                tipoHembraId = x.TipoHembraID,
                referenciaProyecto = x.ReferenciaProyecto,
                caracteristicasHembras = x.CaracteristicasHembras,
                esImportacion = x.EsImportacion,
                paisOrigen = x.PaisOrigen,
                referenciaImportacion = x.ReferenciaImportacion,
                caracteristicasSemental = x.CaracteristicasSemental,
                kilogramosSolicitados = x.KilogramosSolicitados,
                pesoPromedioObjetivoKg = x.PesoPromedioObjetivoKg,
                especificacionesRastro = x.EspecificacionesRastro
            }));
            await using var command = new SqlCommand("dbo.usp_Nutri_CrearPedidoCompleto", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = model.EmpresaID;
            command.Parameters.Add("@TipoPedidoID", SqlDbType.Int).Value = model.TipoPedidoID;
            command.Parameters.Add("@OrigenPedidoID", SqlDbType.Int).Value = model.OrigenPedidoID;
            AddNullableInt(command, "@ClienteID", model.ClienteID);
            AddNullableInt(command, "@ClienteUbicacionID", model.ClienteUbicacionID);
            AddNullableInt(command, "@CentroDestinoID", model.CentroDestinoID);
            AddNullableInt(command, "@UsuarioSolicitanteID", model.UsuarioSolicitanteID);
            var pFechaSolicitud = command.Parameters.Add("@FechaSolicitud", SqlDbType.DateTime2);
            pFechaSolicitud.Value = model.FechaSolicitud.HasValue ? (object)model.FechaSolicitud.Value : DBNull.Value;
            AddNullableDate(command, "@FechaRequerida", model.FechaRequerida);
            AddNullableInt(command, "@PrioridadPedidoID", model.PrioridadPedidoID);
            AddNullableString(command, "@ReferenciaExterna", 100, model.ReferenciaExterna);
            command.Parameters.Add("@FueraDeTiempo", SqlDbType.Bit).Value = model.FueraDeTiempo;
            AddNullableString(command, "@MotivoFueraDeTiempo", 500, model.MotivoFueraDeTiempo);
            AddNullableString(command, "@Observaciones", 1000, model.Observaciones);
            AddNullableString(command, "@Folio", 30, model.Folio);
            command.Parameters.Add("@GuardarComoBorrador", SqlDbType.Bit).Value = model.GuardarComoBorrador;
            var pDetalles = command.Parameters.Add("@Detalles", SqlDbType.NVarChar, -1);
            pDetalles.Value = detallesJson;
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            AddNullableString(command, "@UsuarioNombre", 150, usuarioNombre);
            var resultado = new CoordinacionResultadoCrearPedidoVm();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                resultado.PedidoID = ReadInt(reader, "PedidoID");
                resultado.Folio = ReadString(reader, "Folio");
                resultado.TipoPedidoCodigo = ReadString(reader, "TipoPedidoCodigo");
                resultado.TipoPedido = ReadString(reader, "TipoPedido");
                resultado.EstatusCodigo = ReadString(reader, "EstatusCodigo");
                resultado.Estatus = ReadString(reader, "Estatus");
                resultado.FechaSolicitud = ReadDateTime(reader, "FechaSolicitud");
                resultado.FechaRequerida = ReadNullableDateTime(reader, "FechaRequerida");
                resultado.FueraDeTiempo = ReadBool(reader, "FueraDeTiempo");
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado.Detalles.Add(new CoordinacionResultadoCrearPedidoDetalleVm
                    {
                        PedidoDetalleID = ReadInt(reader, "PedidoDetalleID"),
                        Renglon = ReadInt(reader, "Renglon"),
                        ProductoID = ReadInt(reader, "ProductoID"),
                        Producto = ReadString(reader, "Producto"),
                        LineaGeneticaID = ReadNullableInt(reader, "LineaGeneticaID"),
                        LineaGenetica = ReadNullableString(reader, "LineaGenetica"),
                        CantidadSolicitada = ReadDecimal(reader, "CantidadSolicitada"),
                        UnidadMedidaSnapshot = ReadNullableString(reader, "UnidadMedidaSnapshot")
                    });
                }
            }
            return resultado;
        }

        private static async Task<CoordinacionPedidoDetallePaginaVm> ObtenerDetallePedidoAsync(SqlConnection connection, int pedidoId, int empresaId, CancellationToken cancellationToken)
        {
            var model = new CoordinacionPedidoDetallePaginaVm();
            await using var command = new SqlCommand("dbo.usp_Coordinacion_ObtenerDetallePedido", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 90
            };
            command.Parameters.Add("@PedidoID", SqlDbType.Int).Value = pedidoId;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                model.Pedido = new CoordinacionPedidoEncabezadoVm
                {
                    PedidoID = ReadInt(reader, "PedidoID"),
                    Folio = ReadString(reader, "Folio"),
                    EmpresaID = ReadInt(reader, "EmpresaID"),
                    Empresa = ReadString(reader, "Empresa"),
                    TipoPedidoID = ReadInt(reader, "TipoPedidoID"),
                    TipoPedidoCodigo = ReadString(reader, "TipoPedidoCodigo"),
                    TipoPedido = ReadString(reader, "TipoPedido"),
                    OrigenPedidoID = ReadInt(reader, "OrigenPedidoID"),
                    OrigenCodigo = ReadString(reader, "OrigenCodigo"),
                    Origen = ReadString(reader, "Origen"),
                    EstatusPedidoID = ReadInt(reader, "EstatusPedidoID"),
                    EstatusCodigo = ReadString(reader, "EstatusCodigo"),
                    Estatus = ReadString(reader, "Estatus"),
                    ColorHex = ReadNullableString(reader, "ColorHex"),
                    PrioridadPedidoID = ReadInt(reader, "PrioridadPedidoID"),
                    PrioridadCodigo = ReadString(reader, "PrioridadCodigo"),
                    Prioridad = ReadString(reader, "Prioridad"),
                    ClienteID = ReadNullableInt(reader, "ClienteID"),
                    Cliente = ReadNullableString(reader, "Cliente"),
                    RazonSocial = ReadNullableString(reader, "RazonSocial"),
                    EsBioHypor = ReadNullableBool(reader, "EsBioHypor"),
                    ClienteUbicacionID = ReadNullableInt(reader, "ClienteUbicacionID"),
                    CentroDestinoID = ReadNullableInt(reader, "CentroDestinoID"),
                    Destino = ReadNullableString(reader, "Destino"),
                    DireccionCliente = ReadNullableString(reader, "DireccionCliente"),
                    MunicipioCliente = ReadNullableString(reader, "MunicipioCliente"),
                    EstadoCliente = ReadNullableString(reader, "EstadoCliente"),
                    DireccionCentro = ReadNullableString(reader, "DireccionCentro"),
                    MunicipioCentro = ReadNullableString(reader, "MunicipioCentro"),
                    EstadoCentro = ReadNullableString(reader, "EstadoCentro"),
                    UsuarioSolicitanteID = ReadNullableInt(reader, "UsuarioSolicitanteID"),
                    FechaSolicitud = ReadDateTime(reader, "FechaSolicitud"),
                    FechaRequerida = ReadNullableDateTime(reader, "FechaRequerida"),
                    ReferenciaExterna = ReadNullableString(reader, "ReferenciaExterna"),
                    FueraDeTiempo = ReadBool(reader, "FueraDeTiempo"),
                    MotivoFueraDeTiempo = ReadNullableString(reader, "MotivoFueraDeTiempo"),
                    Observaciones = ReadNullableString(reader, "Observaciones"),
                    FechaCreacion = ReadDateTime(reader, "FechaCreacion"),
                    FechaModificacion = ReadNullableDateTime(reader, "FechaModificacion")
                };
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.Detalles.Add(new CoordinacionPedidoDetalleRenglonVm
                    {
                        PedidoDetalleID = ReadInt(reader, "PedidoDetalleID"),
                        Renglon = ReadInt(reader, "Renglon"),
                        ProductoID = ReadInt(reader, "ProductoID"),
                        Producto = ReadString(reader, "Producto"),
                        LineaGeneticaID = ReadNullableInt(reader, "LineaGeneticaID"),
                        LineaGenetica = ReadNullableString(reader, "LineaGenetica"),
                        CantidadSolicitada = ReadDecimal(reader, "CantidadSolicitada"),
                        UnidadMedidaSnapshot = ReadNullableString(reader, "UnidadMedidaSnapshot"),
                        PesoMinimoKg = ReadNullableDecimal(reader, "PesoMinimoKg"),
                        PesoMaximoKg = ReadNullableDecimal(reader, "PesoMaximoKg"),
                        Especificaciones = ReadNullableString(reader, "Especificaciones"),
                        Observaciones = ReadNullableString(reader, "Observaciones"),
                        FechaProduccionSolicitada = ReadNullableDateTime(reader, "FechaProduccionSolicitada"),
                        EspecificacionesDosis = ReadNullableString(reader, "EspecificacionesDosis"),
                        TipoHembraID = ReadNullableInt(reader, "TipoHembraID"),
                        TipoHembraCodigo = ReadNullableString(reader, "TipoHembraCodigo"),
                        TipoHembra = ReadNullableString(reader, "TipoHembra"),
                        ReferenciaProyecto = ReadNullableString(reader, "ReferenciaProyecto"),
                        CaracteristicasHembras = ReadNullableString(reader, "CaracteristicasHembras"),
                        EsImportacion = ReadNullableBool(reader, "EsImportacion"),
                        PaisOrigen = ReadNullableString(reader, "PaisOrigen"),
                        ReferenciaImportacion = ReadNullableString(reader, "ReferenciaImportacion"),
                        CaracteristicasSemental = ReadNullableString(reader, "CaracteristicasSemental"),
                        KilogramosSolicitados = ReadNullableDecimal(reader, "KilogramosSolicitados"),
                        PesoPromedioObjetivoKg = ReadNullableDecimal(reader, "PesoPromedioObjetivoKg"),
                        EspecificacionesRastro = ReadNullableString(reader, "EspecificacionesRastro"),
                        Activo = ReadBool(reader, "Activo")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.Historial.Add(new CoordinacionPedidoHistorialVm
                    {
                        PedidoHistorialID = ReadLong(reader, "PedidoHistorialID"),
                        PedidoDetalleID = ReadNullableInt(reader, "PedidoDetalleID"),
                        Evento = ReadString(reader, "Evento"),
                        EstatusAnteriorID = ReadNullableInt(reader, "EstatusAnteriorID"),
                        EstatusAnterior = ReadNullableString(reader, "EstatusAnterior"),
                        EstatusNuevoID = ReadNullableInt(reader, "EstatusNuevoID"),
                        EstatusNuevo = ReadNullableString(reader, "EstatusNuevo"),
                        Observaciones = ReadNullableString(reader, "Observaciones"),
                        DatosAdicionales = ReadNullableString(reader, "DatosAdicionales"),
                        UsuarioID = ReadNullableInt(reader, "UsuarioID"),
                        UsuarioNombreSnapshot = ReadNullableString(reader, "UsuarioNombreSnapshot"),
                        FechaEvento = ReadDateTime(reader, "FechaEvento")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.Programacion.Add(new CoordinacionPedidoProgramacionVm
                    {
                        ProgramacionID = ReadInt(reader, "ProgramacionID"),
                        Anio = ReadInt(reader, "Anio"),
                        Semana = ReadNullableInt(reader, "Semana"),
                        Version = ReadInt(reader, "Version"),
                        FechaInicio = ReadDateTime(reader, "FechaInicio"),
                        FechaFin = ReadDateTime(reader, "FechaFin"),
                        EstatusProgramacionCodigo = ReadString(reader, "EstatusProgramacionCodigo"),
                        EstatusProgramacion = ReadString(reader, "EstatusProgramacion"),
                        ProgramacionDetalleID = ReadInt(reader, "ProgramacionDetalleID"),
                        PedidoDetalleID = ReadInt(reader, "PedidoDetalleID"),
                        CentroOrigenID = ReadNullableInt(reader, "CentroOrigenID"),
                        CentroOrigen = ReadNullableString(reader, "CentroOrigen"),
                        FechaProduccion = ReadNullableDateTime(reader, "FechaProduccion"),
                        FechaEntregaProgramada = ReadDateTime(reader, "FechaEntregaProgramada"),
                        HoraEntregaProgramada = ReadNullableTimeSpan(reader, "HoraEntregaProgramada"),
                        CantidadProgramada = ReadDecimal(reader, "CantidadProgramada"),
                        SecuenciaEntrega = ReadNullableInt(reader, "SecuenciaEntrega"),
                        ReferenciaRemision = ReadNullableString(reader, "ReferenciaRemision"),
                        ProgramacionRutaID = ReadNullableInt(reader, "ProgramacionRutaID"),
                        RutaID = ReadNullableInt(reader, "RutaID"),
                        Ruta = ReadNullableString(reader, "Ruta"),
                        FechaRuta = ReadNullableDateTime(reader, "FechaRuta"),
                        HoraSalidaProgramada = ReadNullableTimeSpan(reader, "HoraSalidaProgramada"),
                        EstatusDetalle = ReadString(reader, "EstatusDetalle")
                    });
                }
            }
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    model.SolicitudesCambio.Add(new CoordinacionSolicitudCambioVm
                    {
                        SolicitudCambioID = ReadLong(reader, "SolicitudCambioID"),
                        PedidoDetalleID = ReadNullableInt(reader, "PedidoDetalleID"),
                        ProgramacionID = ReadNullableInt(reader, "ProgramacionID"),
                        ProgramacionDetalleID = ReadNullableInt(reader, "ProgramacionDetalleID"),
                        TipoCambio = ReadString(reader, "TipoCambio"),
                        EsFueraDeTiempo = ReadBool(reader, "EsFueraDeTiempo"),
                        Motivo = ReadString(reader, "Motivo"),
                        DatosAnteriores = ReadNullableString(reader, "DatosAnteriores"),
                        DatosNuevos = ReadNullableString(reader, "DatosNuevos"),
                        EstatusCodigo = ReadString(reader, "EstatusCodigo"),
                        Estatus = ReadString(reader, "Estatus"),
                        UsuarioSolicitaID = ReadInt(reader, "UsuarioSolicitaID"),
                        FechaSolicitud = ReadDateTime(reader, "FechaSolicitud"),
                        UsuarioResuelveID = ReadNullableInt(reader, "UsuarioResuelveID"),
                        FechaResolucion = ReadNullableDateTime(reader, "FechaResolucion"),
                        ObservacionesResolucion = ReadNullableString(reader, "ObservacionesResolucion"),
                        FechaAplicacion = ReadNullableDateTime(reader, "FechaAplicacion")
                    });
                }
            }
            return model;
        }

        private static async Task<object> CambiarEstatusPedidoAsync(SqlConnection connection, CoordinacionCambiarEstatusPedidoVm model, int empresaId, int usuarioId, string usuarioNombre, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("dbo.usp_Nutri_CambiarEstatusPedido", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            command.Parameters.Add("@PedidoID", SqlDbType.Int).Value = model.PedidoID;
            command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            command.Parameters.Add("@NuevoEstatusCodigo", SqlDbType.VarChar, 40).Value = model.NuevoEstatusCodigo;
            AddNullableString(command, "@Observaciones", 1000, model.Observaciones);
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            AddNullableString(command, "@UsuarioNombre", 150, usuarioNombre);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new { pedidoID = model.PedidoID, estatusCodigo = model.NuevoEstatusCodigo };
            return new
            {
                pedidoID = ReadInt(reader, "PedidoID"),
                folio = ReadString(reader, "Folio"),
                estatusCodigo = ReadString(reader, "EstatusCodigo"),
                estatus = ReadString(reader, "Estatus")
            };
        }

        private static void NormalizarPedido(CoordinacionCrearPedidoVm model)
        {
            model.Folio = Limpiar(model.Folio);
            model.ReferenciaExterna = Limpiar(model.ReferenciaExterna);
            model.MotivoFueraDeTiempo = Limpiar(model.MotivoFueraDeTiempo);
            model.Observaciones = Limpiar(model.Observaciones);
            model.Detalles ??= new List<CoordinacionPedidoRenglonCapturaVm>();
            for (var i = 0; i < model.Detalles.Count; i++)
            {
                var item = model.Detalles[i];
                if (item.Renglon <= 0) item.Renglon = i + 1;
                item.Especificaciones = Limpiar(item.Especificaciones);
                item.Observaciones = Limpiar(item.Observaciones);
                item.EspecificacionesDosis = Limpiar(item.EspecificacionesDosis);
                item.ReferenciaProyecto = Limpiar(item.ReferenciaProyecto);
                item.CaracteristicasHembras = Limpiar(item.CaracteristicasHembras);
                item.PaisOrigen = Limpiar(item.PaisOrigen);
                item.ReferenciaImportacion = Limpiar(item.ReferenciaImportacion);
                item.CaracteristicasSemental = Limpiar(item.CaracteristicasSemental);
                item.EspecificacionesRastro = Limpiar(item.EspecificacionesRastro);
            }
        }

        private static async Task<(int TipoPedidoID, string Nombre)?> ObtenerTipoPedidoPorCodigoAsync(SqlConnection connection, string codigo, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1) TipoPedidoID,Nombre
FROM dbo.Nutri_TiposPedido
WHERE Codigo=@Codigo
  AND Activo=1;";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Codigo", SqlDbType.VarChar, 30).Value = codigo;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return (ReadInt(reader, "TipoPedidoID"), ReadString(reader, "Nombre"));
        }

        private async Task<IActionResult> CargarBandejaTipoAsync(string codigoTipo, string urlSubMenu, CoordinacionFiltroPedidosVm filtros, CancellationToken cancellationToken)
        {
            var acceso = await ValidarAccesoAsync(urlSubMenu, "Ver", cancellationToken);
            if (acceso != null) return acceso;
            var empresaId = ObtenerEmpresaID()!.Value;
            filtros ??= new CoordinacionFiltroPedidosVm();
            filtros.Buscar = string.IsNullOrWhiteSpace(filtros.Buscar) ? null : filtros.Buscar.Trim();
            await using var connection = await AbrirAsync(cancellationToken);
            var tipo = await ObtenerTipoPedidoPorCodigoAsync(connection, codigoTipo, cancellationToken);
            if (!tipo.HasValue) return NotFound($"No existe un tipo de pedido activo con código {codigoTipo}.");
            filtros.TipoPedidoID = tipo.Value.TipoPedidoID;
            var model = await CargarPaginaPedidosAsync(empresaId, filtros, cancellationToken);
            ViewData["ModoBandeja"] = "Tipo";
            ViewData["TipoPedidoID"] = tipo.Value.TipoPedidoID;
            ViewData["TipoPedidoCodigo"] = codigoTipo;
            ViewData["TipoPedidoNombre"] = tipo.Value.Nombre;
            ViewData["UrlSubMenuActual"] = urlSubMenu;
            return View("Pedidos", model);
        }

        private void ValidarPedidoEnControlador(CoordinacionCrearPedidoVm model)
        {
            if (model.Detalles == null || model.Detalles.Count == 0)
                ModelState.AddModelError(nameof(model.Detalles), "Agrega al menos un renglón al pedido.");
            if (model.FueraDeTiempo && string.IsNullOrWhiteSpace(model.MotivoFueraDeTiempo))
                ModelState.AddModelError(nameof(model.MotivoFueraDeTiempo), "Debes indicar el motivo del pedido fuera de tiempo.");
            if (model.FechaSolicitud.HasValue && model.FechaRequerida.HasValue && model.FechaRequerida.Value.Date < model.FechaSolicitud.Value.Date)
                ModelState.AddModelError(nameof(model.FechaRequerida), "La fecha requerida no puede ser anterior a la fecha de solicitud.");
            if (model.ClienteUbicacionID.HasValue && model.CentroDestinoID.HasValue)
            {
                ModelState.AddModelError(nameof(model.ClienteUbicacionID), "Selecciona una ubicación de cliente o un centro interno, no ambos.");
                ModelState.AddModelError(nameof(model.CentroDestinoID), "Selecciona una ubicación de cliente o un centro interno, no ambos.");
            }
            if (model.Detalles != null)
            {
                var duplicados = model.Detalles.Where(x => x.Renglon > 0).GroupBy(x => x.Renglon).Any(g => g.Count() > 1);
                if (duplicados) ModelState.AddModelError(nameof(model.Detalles), "Existen números de renglón duplicados.");
                foreach (var item in model.Detalles)
                {
                    if (item.PesoMinimoKg.HasValue && item.PesoMaximoKg.HasValue && item.PesoMaximoKg.Value < item.PesoMinimoKg.Value)
                        ModelState.AddModelError(nameof(model.Detalles), $"El rango de peso del renglón {item.Renglon} no es válido.");
                }
            }
        }

        private string ObtenerErroresModelState()
        {
            var errores = ModelState.Values.SelectMany(x => x.Errors)
                .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) ? x.Exception?.Message : x.ErrorMessage)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
            return errores.Count > 0 ? string.Join(" ", errores) : "Revisa los datos capturados.";
        }

        private static string? Limpiar(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? Limpiar(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var texto = value.Trim();
            if (maxLength <= 0) return texto;
            return texto.Length <= maxLength ? texto : texto.Substring(0, maxLength);
        }

        private static string LimpiarMensajeSql(string mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje)) return "Ocurrió un error en la base de datos.";
            var partes = mensaje.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            return partes.FirstOrDefault(x => !x.StartsWith("The statement has been terminated", StringComparison.OrdinalIgnoreCase))?.Trim() ?? mensaje.Trim();
        }

        private static void AddNullableInt(SqlCommand command, string nombre, int? value)
        {
            var parameter = command.Parameters.Add(nombre, SqlDbType.Int);
            parameter.Value = value.HasValue && value.Value > 0 ? (object)value.Value : DBNull.Value;
        }

        private static void AddNullableDate(SqlCommand command, string nombre, DateTime? value)
        {
            var parameter = command.Parameters.Add(nombre, SqlDbType.Date);
            parameter.Value = value.HasValue ? (object)value.Value.Date : DBNull.Value;
        }

        private static void AddNullableString(SqlCommand command, string nombre, int size, string? value)
        {
            var parameter = command.Parameters.Add(nombre, SqlDbType.NVarChar, size);
            parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }

        private static int Ordinal(SqlDataReader reader, string nombre) => reader.GetOrdinal(nombre);
        private static bool IsNull(SqlDataReader reader, string nombre) => reader.IsDBNull(Ordinal(reader, nombre));
        private static string ReadString(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? string.Empty : Convert.ToString(reader[nombre]) ?? string.Empty;
        private static string? ReadNullableString(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? null : Convert.ToString(reader[nombre]);
        private static int ReadInt(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? 0 : Convert.ToInt32(reader[nombre]);
        private static int? ReadNullableInt(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? null : Convert.ToInt32(reader[nombre]);
        private static long ReadLong(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? 0L : Convert.ToInt64(reader[nombre]);
        private static decimal ReadDecimal(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? 0m : Convert.ToDecimal(reader[nombre]);
        private static decimal? ReadNullableDecimal(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? null : Convert.ToDecimal(reader[nombre]);
        private static bool ReadBool(SqlDataReader reader, string nombre) => !IsNull(reader, nombre) && Convert.ToBoolean(reader[nombre]);
        private static bool? ReadNullableBool(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? null : Convert.ToBoolean(reader[nombre]);
        private static DateTime ReadDateTime(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? DateTime.MinValue : Convert.ToDateTime(reader[nombre]);
        private static DateTime? ReadNullableDateTime(SqlDataReader reader, string nombre) => IsNull(reader, nombre) ? null : Convert.ToDateTime(reader[nombre]);
        private static TimeSpan? ReadNullableTimeSpan(SqlDataReader reader, string nombre)
        {
            if (IsNull(reader, nombre)) return null;
            var value = reader[nombre];
            if (value is TimeSpan time) return time;
            return TimeSpan.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
        }
    }
}
