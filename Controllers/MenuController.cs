using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SistemaNutri.Models;
using System.Data;
using System.Text.Json;

namespace SistemaNutri.Controllers
{
    public class MenuController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public MenuController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");
        }

        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var usuarioId = ObtenerUsuarioIdSesion();
            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            var empresaId = ObtenerEmpresaIdSesion();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await ActualizarPerfilSesionAsync(conn, usuarioId.Value);

            var sidebar = await ObtenerSidebarUsuarioAsync(
                conn,
                usuarioId.Value,
                empresaId);

            GuardarSidebarEnSesion(sidebar);

            ViewBag.MenuSidebar = sidebar;
            ViewBag.MostrarBienvenida =
                (TempData["MostrarBienvenida"] as string) == "true";

            return View("Index");
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ObtenerSidebar()
        {
            var usuarioId = ObtenerUsuarioIdSesion();

            if (!usuarioId.HasValue)
            {
                return Unauthorized(new
                {
                    ok = false,
                    mensaje = "La sesión ha expirado."
                });
            }

            var empresaId = ObtenerEmpresaIdSesion();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sidebar = await ObtenerSidebarUsuarioAsync(
                conn,
                usuarioId.Value,
                empresaId);

            GuardarSidebarEnSesion(sidebar);

            return Json(new
            {
                ok = true,
                grupos = sidebar
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefrescarSidebar()
        {
            var usuarioId = ObtenerUsuarioIdSesion();

            if (!usuarioId.HasValue)
            {
                return Unauthorized(new
                {
                    ok = false,
                    mensaje = "La sesión ha expirado."
                });
            }

            var empresaId = ObtenerEmpresaIdSesion();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sidebar = await ObtenerSidebarUsuarioAsync(
                conn,
                usuarioId.Value,
                empresaId);

            GuardarSidebarEnSesion(sidebar);

            return Json(new
            {
                ok = true,
                grupos = sidebar
            });
        }

        [HttpGet]
        public async Task<IActionResult> Grupo(int id)
        {
            var usuarioId = ObtenerUsuarioIdSesion();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            var empresaId = ObtenerEmpresaIdSesion();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var ruta = await ObtenerPrimeraRutaPermitidaGrupoAsync(
                conn,
                usuarioId.Value,
                empresaId,
                id);

            if (!string.IsNullOrWhiteSpace(ruta))
                return LocalRedirect(NormalizarRutaLocal(ruta));

            TempData["InfoMessage"] =
                "Este módulo todavía no tiene opciones disponibles.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Logout()
        {
            return RedirectToAction("Logout", "Login");
        }

        private int? ObtenerUsuarioIdSesion()
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            if (usuarioId.HasValue && usuarioId.Value > 0)
                return usuarioId;

            var claimUsuario = User.FindFirst("UsuarioID")?.Value;

            if (int.TryParse(claimUsuario, out var usuarioClaimId) &&
                usuarioClaimId > 0)
            {
                return usuarioClaimId;
            }

            return null;
        }

        private int? ObtenerEmpresaIdSesion()
        {
            var empresaIdInt =
                HttpContext.Session.GetInt32("EmpresaID");

            if (empresaIdInt.HasValue &&
                empresaIdInt.Value > 0)
            {
                return empresaIdInt;
            }

            var empresaIdTexto =
                HttpContext.Session.GetString("EmpresaId");

            if (int.TryParse(
                empresaIdTexto,
                out var empresaId) &&
                empresaId > 0)
            {
                return empresaId;
            }

            var claimEmpresa =
                User.FindFirst("EmpresaID")?.Value
                ?? User.FindFirst("EmpresaId")?.Value;

            if (int.TryParse(
                claimEmpresa,
                out empresaId) &&
                empresaId > 0)
            {
                return empresaId;
            }

            return null;
        }

        private int ObtenerRolIdSesion()
        {
            var rolId =
                HttpContext.Session.GetInt32("RolID");

            if (rolId.HasValue)
                return rolId.Value;

            var claimRol =
                User.FindFirst("RolID")?.Value;

            if (int.TryParse(
                claimRol,
                out var rolClaimId))
            {
                return rolClaimId;
            }

            return 0;
        }

        private async Task<List<MenuSidebarGrupoModel>>
            ObtenerSidebarUsuarioAsync(
                SqlConnection conn,
                int usuarioId,
                int? empresaId)
        {
            var rolId = ObtenerRolIdSesion();
            var esAdministrador = rolId == 1;

            const string sql = @"
WITH Permisos AS
(
    SELECT
        SubMenuID
    FROM dbo.fn_PermisosEfectivosUsuario
    (
        @UsuarioID,
        @EmpresaID
    )
    WHERE TienePermiso = 1
)
SELECT
    G.MenuGrupoID,
    G.Nombre AS GrupoNombre,
    G.Descripcion AS GrupoDescripcion,
    G.IconoCss,
    G.Orden AS GrupoOrden,

    M.MenuID,
    M.Nombre AS MenuNombre,

    SM.SubMenuID,
    SM.Nombre AS SubMenuNombre,
    SM.UrlEnlace,
    SM.Descripcion AS SubMenuDescripcion,

    CAST
    (
        CASE
            WHEN P.SubMenuID IS NOT NULL
                THEN 1
            ELSE 0
        END
        AS bit
    ) AS TienePermiso

FROM dbo.MenuGrupo G

LEFT JOIN dbo.Menus M
    ON M.MenuGrupoID = G.MenuGrupoID

LEFT JOIN dbo.SubMenus SM
    ON SM.MenuID = M.MenuID
   AND SM.Activo = 1

LEFT JOIN Permisos P
    ON P.SubMenuID = SM.SubMenuID

WHERE G.Activo = 1

ORDER BY
    G.Orden,
    G.MenuGrupoID,
    M.MenuID,
    SM.SubMenuID;";

            await using var cmd =
                new SqlCommand(sql, conn);

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            var parametroEmpresa =
                cmd.Parameters.Add(
                    "@EmpresaID",
                    SqlDbType.Int);

            parametroEmpresa.Value =
                (object?)empresaId
                ?? DBNull.Value;

            var grupos =
                new Dictionary<int, MenuSidebarGrupoModel>();

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var grupoId =
                    rd.GetInt32(
                        rd.GetOrdinal("MenuGrupoID"));

                var tieneSubMenu =
                    !rd.IsDBNull(
                        rd.GetOrdinal("SubMenuID"));

                var tienePermiso =
                    !rd.IsDBNull(
                        rd.GetOrdinal("TienePermiso"))
                    &&
                    rd.GetBoolean(
                        rd.GetOrdinal("TienePermiso"));

                /*
                 * Para usuarios normales solamente se agregan
                 * grupos que tengan por lo menos un SubMenu
                 * permitido.
                 *
                 * Para RolID = 1 también conservamos grupos sin
                 * contenido todavía (Almacén, Logística, Compras).
                 */
                if (!esAdministrador &&
                    (!tieneSubMenu || !tienePermiso))
                {
                    continue;
                }

                if (!grupos.TryGetValue(
                    grupoId,
                    out var grupo))
                {
                    grupo =
                        new MenuSidebarGrupoModel
                        {
                            MenuGrupoID = grupoId,

                            Nombre =
                                rd["GrupoNombre"]?.ToString()
                                ?? string.Empty,

                            Descripcion =
                                rd["GrupoDescripcion"]?.ToString()
                                ?? string.Empty,

                            IconoCss =
                                NormalizarIconoGrupo(
                                    rd["GrupoNombre"]?.ToString(),
                                    rd["IconoCss"]?.ToString()),

                            Orden =
                                Convert.ToInt32(
                                    rd["GrupoOrden"]),

                            Menus =
                                new List<MenuSidebarMenuModel>()
                        };

                    grupos.Add(
                        grupoId,
                        grupo);
                }

                if (!tieneSubMenu ||
                    !tienePermiso)
                {
                    continue;
                }

                if (rd.IsDBNull(
                    rd.GetOrdinal("MenuID")))
                {
                    continue;
                }

                var menuId =
                    rd.GetInt32(
                        rd.GetOrdinal("MenuID"));

                var menu =
                    grupo.Menus
                        .FirstOrDefault(
                            x => x.MenuID == menuId);

                if (menu == null)
                {
                    var nombreMenu =
                        rd["MenuNombre"]?.ToString()
                        ?? string.Empty;

                    menu =
                        new MenuSidebarMenuModel
                        {
                            MenuID = menuId,

                            Nombre = nombreMenu,

                            IconoCss =
                                ObtenerIconoMenu(
                                    nombreMenu),

                            SubMenus =
                                new List<MenuSidebarSubMenuModel>()
                        };

                    grupo.Menus.Add(menu);
                }

                var subMenuId =
                    rd.GetInt32(
                        rd.GetOrdinal("SubMenuID"));

                if (menu.SubMenus.Any(
                    x => x.SubMenuID == subMenuId))
                {
                    continue;
                }

                menu.SubMenus.Add(
                    new MenuSidebarSubMenuModel
                    {
                        SubMenuID = subMenuId,

                        Nombre =
                            rd["SubMenuNombre"]?.ToString()
                            ?? string.Empty,

                        UrlEnlace =
                            NormalizarRutaMenu(
                                rd["UrlEnlace"]?.ToString()),

                        Descripcion =
                            rd["SubMenuDescripcion"]?.ToString()
                            ?? string.Empty
                    });
            }

            var resultado =
                grupos.Values
                    .OrderBy(x => x.Orden)
                    .ThenBy(x => x.Nombre)
                    .ToList();

            foreach (var grupo in resultado)
            {
                grupo.Menus =
                    grupo.Menus
                        .OrderBy(x => x.MenuID)
                        .ToList();

                foreach (var menu in grupo.Menus)
                {
                    menu.SubMenus =
                        menu.SubMenus
                            .OrderBy(x => x.SubMenuID)
                            .ToList();
                }

                grupo.TieneContenido =
                    grupo.Menus.Any(
                        m => m.SubMenus.Any());
            }

            return resultado;
        }

        private async Task<string?>
            ObtenerPrimeraRutaPermitidaGrupoAsync(
                SqlConnection conn,
                int usuarioId,
                int? empresaId,
                int menuGrupoId)
        {
            const string sql = @"
WITH Permisos AS
(
    SELECT
        SubMenuID
    FROM dbo.fn_PermisosEfectivosUsuario
    (
        @UsuarioID,
        @EmpresaID
    )
    WHERE TienePermiso = 1
)
SELECT TOP 1
    SM.UrlEnlace

FROM dbo.MenuGrupo G

INNER JOIN dbo.Menus M
    ON M.MenuGrupoID = G.MenuGrupoID

INNER JOIN dbo.SubMenus SM
    ON SM.MenuID = M.MenuID

INNER JOIN Permisos P
    ON P.SubMenuID = SM.SubMenuID

WHERE G.MenuGrupoID = @MenuGrupoID
  AND G.Activo = 1
  AND SM.Activo = 1
  AND SM.UrlEnlace IS NOT NULL
  AND LTRIM(RTRIM(SM.UrlEnlace)) <> ''

ORDER BY
    M.MenuID,
    SM.SubMenuID;";

            await using var cmd =
                new SqlCommand(sql, conn);

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            var parametroEmpresa =
                cmd.Parameters.Add(
                    "@EmpresaID",
                    SqlDbType.Int);

            parametroEmpresa.Value =
                (object?)empresaId
                ?? DBNull.Value;

            cmd.Parameters.Add(
                "@MenuGrupoID",
                SqlDbType.Int).Value =
                    menuGrupoId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            return resultado == null ||
                   resultado == DBNull.Value
                ? null
                : resultado.ToString();
        }

        private async Task ActualizarPerfilSesionAsync(
            SqlConnection conn,
            int usuarioId)
        {
            const string sql = @"
SELECT TOP 1
    ISNULL(P.FotoRuta, '') AS FotoRuta,
    ISNULL(P.Puesto, '') AS Puesto,
    ISNULL(DEP.NombreDepartamento, '') AS Departamento

FROM dbo.Usuarios U

INNER JOIN dbo.Persona P
    ON P.PersonaID = U.PersonaID

OUTER APPLY
(
    SELECT TOP 1
        D.NombreDepartamento

    FROM dbo.EmpleadoDepartamentos ED

    INNER JOIN dbo.Departamentos D
        ON D.DepartamentoID = ED.DepartamentoID
       AND D.Activo = 1

    WHERE ED.UsuarioID = U.UsuarioID
      AND ED.Activo = 1

    ORDER BY
        ED.FechaAsignacion DESC,
        ED.EmpleadoDepartamentoID DESC
) DEP

WHERE U.UsuarioID = @UsuarioID
  AND U.Activo = 1;";

            await using var cmd =
                new SqlCommand(sql, conn);

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                    usuarioId;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return;

            var fotoRuta =
                rd["FotoRuta"]?.ToString()?.Trim()
                ?? string.Empty;

            var puesto =
                rd["Puesto"]?.ToString()?.Trim()
                ?? string.Empty;

            var departamento =
                rd["Departamento"]?.ToString()?.Trim()
                ?? string.Empty;

            HttpContext.Session.SetString(
                "FotoRuta",
                fotoRuta);

            HttpContext.Session.SetString(
                "Puesto",
                puesto);

            HttpContext.Session.SetString(
                "Departamento",
                departamento);

            ViewBag.FotoPerfil =
                fotoRuta;

            ViewBag.PuestoUsuario =
                puesto;

            ViewBag.DepartamentoUsuario =
                departamento;
        }

        private void GuardarSidebarEnSesion(
            List<MenuSidebarGrupoModel> sidebar)
        {
            var json =
                JsonSerializer.Serialize(
                    sidebar,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = null,
                        WriteIndented = false
                    });

            HttpContext.Session.SetString(
                "MenuSidebar",
                json);
        }

        private static string NormalizarRutaMenu(
            string? ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta))
                return "#";

            ruta = ruta.Trim();

            if (ruta == "#")
                return "#";

            if (ruta.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) ||
                ruta.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ruta;
            }

            ruta =
                ruta.Replace(
                    "~",
                    string.Empty);

            if (!ruta.StartsWith("/"))
                ruta = "/" + ruta;

            return ruta;
        }

        private static string NormalizarRutaLocal(
            string ruta)
        {
            ruta =
                NormalizarRutaMenu(ruta);

            if (ruta == "#")
                return "/Menu/Index";

            if (ruta.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) ||
                ruta.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase))
            {
                return "/Menu/Index";
            }

            return ruta;
        }

        private static string NormalizarIconoGrupo(
            string? nombre,
            string? iconoBd)
        {
            if (!string.IsNullOrWhiteSpace(iconoBd))
                return iconoBd.Trim();

            var valor =
                QuitarAcentos(
                    nombre ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            return valor switch
            {
                var x when
                    x.Contains("coordinacion")
                    => "fa-solid fa-calendar-check",

                var x when
                    x.Contains("comercial")
                    => "fa-solid fa-handshake",

                var x when
                    x.Contains("almacen")
                    => "fa-solid fa-boxes-stacked",

                var x when
                    x.Contains("logistica")
                    => "fa-solid fa-truck",

                var x when
                    x.Contains("compras")
                    => "fa-solid fa-cart-shopping",

                _ => "fa-solid fa-layer-group"
            };
        }

        private static string ObtenerIconoMenu(
            string? nombre)
        {
            var valor =
                QuitarAcentos(
                    nombre ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            return valor switch
            {
                var x when
                    x.Contains("programacion")
                    => "fa-solid fa-calendar-days",

                var x when
                    x.Contains("pedido")
                    => "fa-solid fa-clipboard-list",

                var x when
                    x.Contains("disponibilidad")
                    => "fa-solid fa-chart-pie",

                var x when
                    x.Contains("cliente")
                    => "fa-solid fa-users",

                var x when
                    x.Contains("cotizacion")
                    => "fa-solid fa-file-invoice-dollar",

                var x when
                    x.Contains("inventario")
                    => "fa-solid fa-boxes-stacked",

                var x when
                    x.Contains("ruta")
                    => "fa-solid fa-route",

                var x when
                    x.Contains("proveedor")
                    => "fa-solid fa-truck-field",

                _ => "fa-solid fa-folder"
            };
        }

        private static string QuitarAcentos(
            string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return texto;

            var normalizado =
                texto.Normalize(
                    System.Text.NormalizationForm.FormD);

            var resultado =
                new System.Text.StringBuilder();

            foreach (var caracter in normalizado)
            {
                var categoria =
                    System.Globalization.CharUnicodeInfo
                        .GetUnicodeCategory(caracter);

                if (categoria !=
                    System.Globalization.UnicodeCategory
                        .NonSpacingMark)
                {
                    resultado.Append(caracter);
                }
            }

            return resultado
                .ToString()
                .Normalize(
                    System.Text.NormalizationForm.FormC);
        }
    }
}

namespace SistemaNutri.Models
{
    public class MenuSidebarGrupoModel
    {
        public int MenuGrupoID { get; set; }

        public string Nombre { get; set; } =
            string.Empty;

        public string Descripcion { get; set; } =
            string.Empty;

        public string IconoCss { get; set; } =
            "fa-solid fa-layer-group";

        public int Orden { get; set; }

        public bool TieneContenido { get; set; }

        public List<MenuSidebarMenuModel> Menus
        {
            get;
            set;
        } = new();
    }

    public class MenuSidebarMenuModel
    {
        public int MenuID { get; set; }

        public string Nombre { get; set; } =
            string.Empty;

        public string IconoCss { get; set; } =
            "fa-solid fa-folder";

        public List<MenuSidebarSubMenuModel> SubMenus
        {
            get;
            set;
        } = new();
    }

    public class MenuSidebarSubMenuModel
    {
        public int SubMenuID { get; set; }

        public string Nombre { get; set; } =
            string.Empty;

        public string UrlEnlace { get; set; } =
            "#";

        public string Descripcion { get; set; } =
            string.Empty;
    }
}