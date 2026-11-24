
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SistemaNutri.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SistemaNutri.Controllers
{

    public class AdministracionMenusController : Controller
    {
        private readonly string _connectionString;

        private const int RolAdministradorId = 1;

        private static readonly string[] AccionesBase =
        {
            "Ver",
            "Crear",
            "Editar",
            "Eliminar"
        };

        public AdministracionMenusController(
            IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No se encontró la cadena de conexión DefaultConnection.");
        }


        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!EsAdministrador())
                return Forbid();

            var model =
                await ObtenerEstructuraCompletaAsync();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerEstructura()
        {
            if (!EsAdministrador())
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        mensaje = "No tienes permisos para administrar los menús."
                    });
            }

            var estructura =
                await ObtenerEstructuraCompletaAsync();

            return Json(new
            {
                ok = true,
                grupos = estructura.Grupos
            });
        }

    

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearGrupo(
            CrearMenuGrupoRequest model)
        {
            if (!EsAdministrador())
                return Forbid();

            model.Nombre =
                (model.Nombre ?? string.Empty).Trim();

            model.Descripcion =
                (model.Descripcion ?? string.Empty).Trim();

            model.IconoCss =
                (model.IconoCss ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del grupo es obligatorio."
                });
            }

            if (model.Nombre.Length > 100)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del grupo no puede exceder 100 caracteres."
                });
            }

            if (model.Descripcion.Length > 250)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "La descripción no puede exceder 250 caracteres."
                });
            }

            if (model.IconoCss.Length > 50)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El icono no puede exceder 50 caracteres."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string validar = @"
SELECT COUNT(1)
FROM dbo.MenuGrupo
WHERE LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI;";

            await using (var command =
                new SqlCommand(validar, connection))
            {
                command.Parameters.AddWithValue(
                    "@Nombre",
                    model.Nombre);

                var existe =
                    Convert.ToInt32(
                        await command.ExecuteScalarAsync()) > 0;

                if (existe)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "Ya existe un grupo con ese nombre."
                    });
                }
            }

            const string insertar = @"
INSERT INTO dbo.MenuGrupo
(
    Nombre,
    Descripcion,
    IconoCss,
    Orden,
    Activo
)
VALUES
(
    @Nombre,
    @Descripcion,
    @IconoCss,
    @Orden,
    1
);

SELECT CAST(SCOPE_IDENTITY() AS int);";

            await using var cmd =
                new SqlCommand(insertar, connection);

            cmd.Parameters.AddWithValue(
                "@Nombre",
                model.Nombre);

            cmd.Parameters.AddWithValue(
                "@Descripcion",
                string.IsNullOrWhiteSpace(model.Descripcion)
                    ? DBNull.Value
                    : model.Descripcion);

            cmd.Parameters.AddWithValue(
                "@IconoCss",
                string.IsNullOrWhiteSpace(model.IconoCss)
                    ? DBNull.Value
                    : model.IconoCss);

            cmd.Parameters.AddWithValue(
                "@Orden",
                model.Orden <= 0
                    ? 100
                    : model.Orden);

            var id =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            return Json(new
            {
                ok = true,
                mensaje = "Grupo creado correctamente.",
                menuGrupoId = id
            });
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosSubMenuRol(int subMenuId, int rolId)
        {
            if (!EsAdministrador()) return Forbid();
            if (subMenuId <= 0 || rolId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El submenú o el rol no son válidos."
                });
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sqlValidar = @"
SELECT
    (SELECT COUNT(1) FROM dbo.SubMenus WHERE SubMenuID = @SubMenuID) AS ExisteSubMenu,
    (SELECT COUNT(1) FROM dbo.Roles WHERE RolID = @RolID) AS ExisteRol;";

            await using (var cmdValidar = new SqlCommand(sqlValidar, connection))
            {
                cmdValidar.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuId;
                cmdValidar.Parameters.Add("@RolID", SqlDbType.Int).Value = rolId;

                await using var rdValidar = await cmdValidar.ExecuteReaderAsync();

                if (!await rdValidar.ReadAsync()
                    || Convert.ToInt32(rdValidar["ExisteSubMenu"]) <= 0
                    || Convert.ToInt32(rdValidar["ExisteRol"]) <= 0)
                {
                    return NotFound(new
                    {
                        ok = false,
                        mensaje = "No se encontró el submenú o el rol seleccionado."
                    });
                }
            }

            const string sql = @"
SELECT
    A.Nombre,
    SMA.Activo AS AccionDisponible,
    CAST(CASE
        WHEN SMA.Activo = 1
         AND ISNULL(PR.Activo, 0) = 1
        THEN 1
        ELSE 0
    END AS bit) AS Permitido
FROM dbo.SubMenuAcciones SMA
INNER JOIN dbo.Acciones A
    ON A.AccionID = SMA.AccionID
LEFT JOIN dbo.PermisosPorRol PR
    ON PR.SubMenuAccionID = SMA.SubMenuAccionID
   AND PR.RolID = @RolID
WHERE SMA.SubMenuID = @SubMenuID
ORDER BY CASE A.Nombre
    WHEN N'Ver' THEN 1
    WHEN N'Crear' THEN 2
    WHEN N'Editar' THEN 3
    WHEN N'Eliminar' THEN 4
    ELSE 99
END,
A.Nombre;";

            bool ver = false;
            bool crear = false;
            bool editar = false;
            bool eliminar = false;
            bool disponibleVer = false;
            bool disponibleCrear = false;
            bool disponibleEditar = false;
            bool disponibleEliminar = false;

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuId;
            cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = rolId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty;
                var disponible = Convert.ToBoolean(rd["AccionDisponible"]);
                var permitido = Convert.ToBoolean(rd["Permitido"]);

                switch (nombre.ToLowerInvariant())
                {
                    case "ver":
                        disponibleVer = disponible;
                        ver = permitido;
                        break;
                    case "crear":
                        disponibleCrear = disponible;
                        crear = permitido;
                        break;
                    case "editar":
                        disponibleEditar = disponible;
                        editar = permitido;
                        break;
                    case "eliminar":
                        disponibleEliminar = disponible;
                        eliminar = permitido;
                        break;
                }
            }

            return Json(new
            {
                ok = true,
                subMenuId,
                rolId,
                permisos = new
                {
                    ver,
                    crear,
                    editar,
                    eliminar
                },
                disponibles = new
                {
                    ver = disponibleVer,
                    crear = disponibleCrear,
                    editar = disponibleEditar,
                    eliminar = disponibleEliminar
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarGrupo(
            EditarMenuGrupoRequest model)
        {
            if (!EsAdministrador())
                return Forbid();

            if (model.MenuGrupoID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El grupo seleccionado no es válido."
                });
            }

            model.Nombre =
                (model.Nombre ?? string.Empty).Trim();

            model.Descripcion =
                (model.Descripcion ?? string.Empty).Trim();

            model.IconoCss =
                (model.IconoCss ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del grupo es obligatorio."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string validar = @"
SELECT COUNT(1)
FROM dbo.MenuGrupo
WHERE MenuGrupoID <> @MenuGrupoID
  AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI;";

            await using (var command =
                new SqlCommand(validar, connection))
            {
                command.Parameters.AddWithValue(
                    "@MenuGrupoID",
                    model.MenuGrupoID);

                command.Parameters.AddWithValue(
                    "@Nombre",
                    model.Nombre);

                var existe =
                    Convert.ToInt32(
                        await command.ExecuteScalarAsync()) > 0;

                if (existe)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "Ya existe otro grupo con ese nombre."
                    });
                }
            }

            const string actualizar = @"
UPDATE dbo.MenuGrupo
SET
    Nombre = @Nombre,
    Descripcion = @Descripcion,
    IconoCss = @IconoCss,
    Orden = @Orden
WHERE MenuGrupoID = @MenuGrupoID;

SELECT @@ROWCOUNT;";

            await using var cmd =
                new SqlCommand(actualizar, connection);

            cmd.Parameters.AddWithValue(
                "@MenuGrupoID",
                model.MenuGrupoID);

            cmd.Parameters.AddWithValue(
                "@Nombre",
                model.Nombre);

            cmd.Parameters.AddWithValue(
                "@Descripcion",
                string.IsNullOrWhiteSpace(model.Descripcion)
                    ? DBNull.Value
                    : model.Descripcion);

            cmd.Parameters.AddWithValue(
                "@IconoCss",
                string.IsNullOrWhiteSpace(model.IconoCss)
                    ? DBNull.Value
                    : model.IconoCss);

            cmd.Parameters.AddWithValue(
                "@Orden",
                model.Orden <= 0
                    ? 100
                    : model.Orden);

            var filas =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            if (filas <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se encontró el grupo."
                });
            }

            return Json(new
            {
                ok = true,
                mensaje = "Grupo actualizado correctamente."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoGrupo(int menuGrupoId, bool activo)
        {
            if (!EsAdministrador())
                return AccesoAdministrativoDenegado();

            if (menuGrupoId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El grupo seleccionado no es válido."
                });
            }

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
UPDATE dbo.MenuGrupo
SET Activo = @Activo
WHERE MenuGrupoID = @MenuGrupoID;

SELECT @@ROWCOUNT;";

                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = activo;
                cmd.Parameters.Add("@MenuGrupoID", SqlDbType.Int).Value = menuGrupoId;

                var filas = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (filas <= 0)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró el grupo indicado."
                    });
                }

                await ActualizarSidebarSesionAsync(connection);

                return Json(new
                {
                    ok = true,
                    activo,
                    menuGrupoId,
                    mensaje = activo
                        ? "El grupo fue activado correctamente."
                        : "El grupo fue desactivado y ocultado del menú lateral."
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        ok = false,
                        mensaje = "Ocurrió un error de base de datos al cambiar el estado del grupo.",
                        detalle = ex.Message
                    });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        ok = false,
                        mensaje = "No fue posible cambiar el estado del grupo.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearMenu(
            CrearMenuRequest model)
        {
            if (!EsAdministrador())
                return Forbid();

            model.Nombre =
                (model.Nombre ?? string.Empty).Trim();

            if (model.MenuGrupoID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Debes seleccionar un grupo."
                });
            }

            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del menú es obligatorio."
                });
            }

            if (model.Nombre.Length > 100)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del menú no puede exceder 100 caracteres."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string validarGrupo = @"
SELECT COUNT(1)
FROM dbo.MenuGrupo
WHERE MenuGrupoID = @MenuGrupoID
  AND Activo = 1;";

            await using (var cmdGrupo =
                new SqlCommand(validarGrupo, connection))
            {
                cmdGrupo.Parameters.AddWithValue(
                    "@MenuGrupoID",
                    model.MenuGrupoID);

                var grupoExiste =
                    Convert.ToInt32(
                        await cmdGrupo.ExecuteScalarAsync()) > 0;

                if (!grupoExiste)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "El grupo seleccionado no existe o está inactivo."
                    });
                }
            }

            const string validarMenu = @"
SELECT COUNT(1)
FROM dbo.Menus
WHERE MenuGrupoID = @MenuGrupoID
  AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI;";

            await using (var cmdValidar =
                new SqlCommand(validarMenu, connection))
            {
                cmdValidar.Parameters.AddWithValue(
                    "@MenuGrupoID",
                    model.MenuGrupoID);

                cmdValidar.Parameters.AddWithValue(
                    "@Nombre",
                    model.Nombre);

                var existe =
                    Convert.ToInt32(
                        await cmdValidar.ExecuteScalarAsync()) > 0;

                if (existe)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "Ya existe un menú con ese nombre dentro del grupo."
                    });
                }
            }

            const string insertar = @"
INSERT INTO dbo.Menus
(
    Nombre,
    MenuGrupoID
)
VALUES
(
    @Nombre,
    @MenuGrupoID
);

SELECT CAST(SCOPE_IDENTITY() AS int);";

            await using var cmd =
                new SqlCommand(insertar, connection);

            cmd.Parameters.AddWithValue(
                "@Nombre",
                model.Nombre);

            cmd.Parameters.AddWithValue(
                "@MenuGrupoID",
                model.MenuGrupoID);

            var id =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            return Json(new
            {
                ok = true,
                mensaje = "Menú creado correctamente.",
                menuId = id
            });
        }

    

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarMenu(
            EditarMenuRequest model)
        {
            if (!EsAdministrador())
                return Forbid();

            model.Nombre =
                (model.Nombre ?? string.Empty).Trim();

            if (model.MenuID <= 0 ||
                model.MenuGrupoID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Los datos del menú no son válidos."
                });
            }

            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del menú es obligatorio."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string validar = @"
SELECT COUNT(1)
FROM dbo.Menus
WHERE MenuID <> @MenuID
  AND MenuGrupoID = @MenuGrupoID
  AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI;";

            await using (var command =
                new SqlCommand(validar, connection))
            {
                command.Parameters.AddWithValue(
                    "@MenuID",
                    model.MenuID);

                command.Parameters.AddWithValue(
                    "@MenuGrupoID",
                    model.MenuGrupoID);

                command.Parameters.AddWithValue(
                    "@Nombre",
                    model.Nombre);

                var existe =
                    Convert.ToInt32(
                        await command.ExecuteScalarAsync()) > 0;

                if (existe)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "Ya existe otro menú con ese nombre dentro del grupo."
                    });
                }
            }

            const string actualizar = @"
UPDATE dbo.Menus
SET
    Nombre = @Nombre,
    MenuGrupoID = @MenuGrupoID
WHERE MenuID = @MenuID;

SELECT @@ROWCOUNT;";

            await using var cmd =
                new SqlCommand(actualizar, connection);

            cmd.Parameters.AddWithValue(
                "@MenuID",
                model.MenuID);

            cmd.Parameters.AddWithValue(
                "@MenuGrupoID",
                model.MenuGrupoID);

            cmd.Parameters.AddWithValue(
                "@Nombre",
                model.Nombre);

            var filas =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            return Json(new
            {
                ok = filas > 0,
                mensaje = filas > 0
                    ? "Menú actualizado correctamente."
                    : "No se encontró el menú."
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarMenu(
            int menuId)
        {
            if (!EsAdministrador())
                return Forbid();

            if (menuId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El menú seleccionado no es válido."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string validar = @"
SELECT COUNT(1)
FROM dbo.SubMenus
WHERE MenuID = @MenuID;";

            await using (var cmdValidar =
                new SqlCommand(validar, connection))
            {
                cmdValidar.Parameters.AddWithValue(
                    "@MenuID",
                    menuId);

                var cantidadSubMenus =
                    Convert.ToInt32(
                        await cmdValidar.ExecuteScalarAsync());

                if (cantidadSubMenus > 0)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No puedes eliminar este menú porque tiene submenús asociados."
                    });
                }
            }

            const string eliminar = @"
DELETE FROM dbo.Menus
WHERE MenuID = @MenuID;

SELECT @@ROWCOUNT;";

            await using var cmd =
                new SqlCommand(eliminar, connection);

            cmd.Parameters.AddWithValue(
                "@MenuID",
                menuId);

            var filas =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            return Json(new
            {
                ok = filas > 0,
                mensaje = filas > 0
                    ? "Menú eliminado correctamente."
                    : "No se encontró el menú."
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSubMenu(
            CrearSubMenuRequest model,
            [FromForm] List<int>? RolIDs)
        {
            if (!EsAdministrador())
                return Forbid();

            var rolesSeleccionados = (RolIDs ?? new List<int>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!rolesSeleccionados.Contains(RolAdministradorId))
                rolesSeleccionados.Add(RolAdministradorId);

            model.Nombre =
                (model.Nombre ?? string.Empty).Trim();

            model.UrlEnlace =
                (model.UrlEnlace ?? string.Empty).Trim();

            model.Descripcion =
                (model.Descripcion ?? string.Empty).Trim();

            if (model.MenuID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Debes seleccionar un menú."
                });
            }

            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del submenú es obligatorio."
                });
            }

            if (model.Nombre.Length > 100)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del submenú no puede exceder 100 caracteres."
                });
            }

            if (model.UrlEnlace.Length > 500)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "La URL no puede exceder 500 caracteres."
                });
            }

            if (model.Descripcion.Length > 255)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "La descripción no puede exceder 255 caracteres."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                const string validarMenu = @"
SELECT COUNT(1)
FROM dbo.Menus
WHERE MenuID = @MenuID;";

                await using (var cmdMenu =
                    new SqlCommand(
                        validarMenu,
                        connection,
                        transaction))
                {
                    cmdMenu.Parameters.AddWithValue(
                        "@MenuID",
                        model.MenuID);

                    var menuExiste =
                        Convert.ToInt32(
                            await cmdMenu.ExecuteScalarAsync()) > 0;

                    if (!menuExiste)
                    {
                        await transaction.RollbackAsync();

                        return Json(new
                        {
                            ok = false,
                            mensaje = "El menú seleccionado no existe."
                        });
                    }
                }

                const string validarDuplicado = @"
SELECT COUNT(1)
FROM dbo.SubMenus
WHERE MenuID = @MenuID
  AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI;";

                await using (var cmdValidar =
                    new SqlCommand(
                        validarDuplicado,
                        connection,
                        transaction))
                {
                    cmdValidar.Parameters.AddWithValue(
                        "@MenuID",
                        model.MenuID);

                    cmdValidar.Parameters.AddWithValue(
                        "@Nombre",
                        model.Nombre);

                    var existe =
                        Convert.ToInt32(
                            await cmdValidar.ExecuteScalarAsync()) > 0;

                    if (existe)
                    {
                        await transaction.RollbackAsync();

                        return Json(new
                        {
                            ok = false,
                            mensaje = "Ya existe un submenú con ese nombre dentro del menú."
                        });
                    }
                }

                const string insertar = @"
INSERT INTO dbo.SubMenus
(
    MenuID,
    Nombre,
    UrlEnlace,
    Descripcion,
    Activo,
    FechaCreacion
)
VALUES
(
    @MenuID,
    @Nombre,
    @UrlEnlace,
    @Descripcion,
    1,
    GETDATE()
);

SELECT CAST(SCOPE_IDENTITY() AS int);";

                int subMenuId;

                await using (var cmd =
                    new SqlCommand(
                        insertar,
                        connection,
                        transaction))
                {
                    cmd.Parameters.AddWithValue(
                        "@MenuID",
                        model.MenuID);

                    cmd.Parameters.AddWithValue(
                        "@Nombre",
                        model.Nombre);

                    cmd.Parameters.AddWithValue(
                        "@UrlEnlace",
                        string.IsNullOrWhiteSpace(model.UrlEnlace)
                            ? DBNull.Value
                            : model.UrlEnlace);

                    cmd.Parameters.AddWithValue(
                        "@Descripcion",
                        string.IsNullOrWhiteSpace(model.Descripcion)
                            ? DBNull.Value
                            : model.Descripcion);

                    subMenuId =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());
                }

                if (model.CrearAcciones)
                {
                    await AsegurarAccionesBaseAsync(
                        connection,
                        transaction);

                    await CrearAccionesSubMenuAsync(
                        connection,
                        transaction,
                        subMenuId,
                        model.Ver,
                        model.Crear,
                        model.Editar,
                        model.Eliminar);

                    foreach (var rolId in rolesSeleccionados)
                    {
                        const string validarRolSql = @"
SELECT COUNT(1)
FROM dbo.Roles
WHERE RolID = @RolID;";

                        await using var validarRolCmd = new SqlCommand(
                            validarRolSql,
                            connection,
                            transaction);

                        validarRolCmd.Parameters.Add(
                            "@RolID",
                            SqlDbType.Int).Value = rolId;

                        var rolExiste = Convert.ToInt32(
                            await validarRolCmd.ExecuteScalarAsync()) > 0;

                        if (!rolExiste)
                            throw new InvalidOperationException(
                                $"El rol con ID {rolId} no existe.");

                        await AsignarAccionesSubMenuAlRolAsync(
                            connection,
                            transaction,
                            subMenuId,
                            rolId);
                    }
                }

                await transaction.CommitAsync();
                await ActualizarSidebarSesionAsync(connection);

                return Json(new
                {
                    ok = true,
                    mensaje = "Submenú creado correctamente.",
                    subMenuId
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        ok = false,
                        mensaje = "No fue posible crear el submenú.",
                        detalle = ex.Message
                    });
            }
        }

     

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarSubMenu(
            EditarSubMenuRequest model)
        {
            if (!EsAdministrador())
                return Forbid();

            model.Nombre =
                (model.Nombre ?? string.Empty).Trim();

            model.UrlEnlace =
                (model.UrlEnlace ?? string.Empty).Trim();

            model.Descripcion =
                (model.Descripcion ?? string.Empty).Trim();

            if (model.SubMenuID <= 0 ||
                model.MenuID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Los datos del submenú no son válidos."
                });
            }

            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El nombre del submenú es obligatorio."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string validar = @"
SELECT COUNT(1)
FROM dbo.SubMenus
WHERE SubMenuID <> @SubMenuID
  AND MenuID = @MenuID
  AND LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI;";

            await using (var cmdValidar =
                new SqlCommand(validar, connection))
            {
                cmdValidar.Parameters.AddWithValue(
                    "@SubMenuID",
                    model.SubMenuID);

                cmdValidar.Parameters.AddWithValue(
                    "@MenuID",
                    model.MenuID);

                cmdValidar.Parameters.AddWithValue(
                    "@Nombre",
                    model.Nombre);

                var existe =
                    Convert.ToInt32(
                        await cmdValidar.ExecuteScalarAsync()) > 0;

                if (existe)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "Ya existe otro submenú con ese nombre."
                    });
                }
            }

            const string actualizar = @"
UPDATE dbo.SubMenus
SET
    MenuID = @MenuID,
    Nombre = @Nombre,
    UrlEnlace = @UrlEnlace,
    Descripcion = @Descripcion,
    FechaModificacion = GETDATE()
WHERE SubMenuID = @SubMenuID;

SELECT @@ROWCOUNT;";

            await using var cmd =
                new SqlCommand(actualizar, connection);

            cmd.Parameters.AddWithValue(
                "@SubMenuID",
                model.SubMenuID);

            cmd.Parameters.AddWithValue(
                "@MenuID",
                model.MenuID);

            cmd.Parameters.AddWithValue(
                "@Nombre",
                model.Nombre);

            cmd.Parameters.AddWithValue(
                "@UrlEnlace",
                string.IsNullOrWhiteSpace(model.UrlEnlace)
                    ? DBNull.Value
                    : model.UrlEnlace);

            cmd.Parameters.AddWithValue(
                "@Descripcion",
                string.IsNullOrWhiteSpace(model.Descripcion)
                    ? DBNull.Value
                    : model.Descripcion);

            var filas =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            if (filas > 0)
                await ActualizarSidebarSesionAsync(connection);

            return Json(new
            {
                ok = filas > 0,
                mensaje = filas > 0
                    ? "Submenú actualizado correctamente."
                    : "No se encontró el submenú."
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoSubMenu(
            int subMenuId,
            bool activo)
        {
            if (!EsAdministrador())
                return Forbid();

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string sql = @"
UPDATE dbo.SubMenus
SET
    Activo = @Activo,
    FechaModificacion = GETDATE()
WHERE SubMenuID = @SubMenuID;

SELECT @@ROWCOUNT;";

            await using var cmd =
                new SqlCommand(sql, connection);

            cmd.Parameters.Add(
                "@Activo",
                SqlDbType.Bit).Value = activo;

            cmd.Parameters.AddWithValue(
                "@SubMenuID",
                subMenuId);

            var filas =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            if (filas > 0)
                await ActualizarSidebarSesionAsync(connection);

            return Json(new
            {
                ok = filas > 0,
                mensaje = filas > 0
                    ? activo
                        ? "Submenú activado correctamente."
                        : "Submenú desactivado correctamente."
                    : "No se encontró el submenú."
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarAccionesSubMenu(
            GuardarAccionesSubMenuRequest model)
        {
            if (!EsAdministrador())
                return Forbid();

            if (model.SubMenuID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El submenú no es válido."
                });
            }

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                await AsegurarAccionesBaseAsync(
                    connection,
                    transaction);

                await CrearAccionesSubMenuAsync(
                    connection,
                    transaction,
                    model.SubMenuID,
                    model.Ver,
                    model.Crear,
                    model.Editar,
                    model.Eliminar);

                await ActualizarEstadoAccionesSubMenuAsync(
                    connection,
                    transaction,
                    model);

                if (model.AsignarAdministrador)
                {
                    await AsignarAccionesSubMenuAlRolAsync(
                        connection,
                        transaction,
                        model.SubMenuID,
                        RolAdministradorId);
                }

                await transaction.CommitAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = "Acciones actualizadas correctamente."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        ok = false,
                        mensaje = "No fue posible actualizar las acciones.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPermisosSubMenuRol(int subMenuId, int rolId, bool ver, bool crear, bool editar, bool eliminar)
        {
            if (!EsAdministrador()) return Forbid();

            if (subMenuId <= 0 || rolId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El submenú o el rol no son válidos."
                });
            }

            if (rolId == RolAdministradorId)
                ver = crear = editar = eliminar = true;
            else if (crear || editar || eliminar)
                ver = true;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                const string sqlValidar = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SubMenus
    WHERE SubMenuID = @SubMenuID
)
    THROW 51001, 'El submenú seleccionado no existe.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Roles
    WHERE RolID = @RolID
)
    THROW 51002, 'El rol seleccionado no existe.', 1;";

                await using (var cmdValidar = new SqlCommand(sqlValidar, connection, transaction))
                {
                    cmdValidar.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuId;
                    cmdValidar.Parameters.Add("@RolID", SqlDbType.Int).Value = rolId;
                    await cmdValidar.ExecuteNonQueryAsync();
                }

                var permisos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Ver"] = ver,
                    ["Crear"] = crear,
                    ["Editar"] = editar,
                    ["Eliminar"] = eliminar
                };

                foreach (var permiso in permisos)
                {
                    const string sql = @"
DECLARE @SubMenuAccionID int;
DECLARE @AccionActiva bit;

SELECT TOP 1
    @SubMenuAccionID = SMA.SubMenuAccionID,
    @AccionActiva = SMA.Activo
FROM dbo.SubMenuAcciones SMA
INNER JOIN dbo.Acciones A
    ON A.AccionID = SMA.AccionID
WHERE SMA.SubMenuID = @SubMenuID
  AND LTRIM(RTRIM(A.Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@NombreAccion)) COLLATE Latin1_General_CI_AI;

IF @SubMenuAccionID IS NOT NULL
BEGIN
    DECLARE @Permitir bit =
        CASE
            WHEN ISNULL(@AccionActiva, 0) = 1 AND @Solicitado = 1 THEN 1
            ELSE 0
        END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.PermisosPorRol
        WHERE RolID = @RolID
          AND SubMenuAccionID = @SubMenuAccionID
    )
    BEGIN
        UPDATE dbo.PermisosPorRol
        SET Activo = @Permitir,
            FechaModificacion = GETDATE()
        WHERE RolID = @RolID
          AND SubMenuAccionID = @SubMenuAccionID;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.PermisosPorRol
        (
            RolID,
            SubMenuAccionID,
            Activo,
            FechaCreacion
        )
        VALUES
        (
            @RolID,
            @SubMenuAccionID,
            @Permitir,
            GETDATE()
        );
    END
END;";

                    await using var cmd = new SqlCommand(sql, connection, transaction);
                    cmd.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuId;
                    cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = rolId;
                    cmd.Parameters.Add("@NombreAccion", SqlDbType.NVarChar, 50).Value = permiso.Key;
                    cmd.Parameters.Add("@Solicitado", SqlDbType.Bit).Value = permiso.Value;
                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                await ActualizarSidebarSesionAsync(connection);

                return Json(new
                {
                    ok = true,
                    mensaje = "Los permisos del rol fueron actualizados correctamente.",
                    subMenuId,
                    rolId,
                    permisos = new
                    {
                        ver,
                        crear,
                        editar,
                        eliminar
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        ok = false,
                        mensaje = "No fue posible guardar los permisos del rol.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerResumenRolesSubMenu(int subMenuId)
        {
            if (!EsAdministrador()) return Forbid();

            if (subMenuId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El submenú seleccionado no es válido."
                });
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
SELECT
    R.RolID,
    R.NombreRol,
    CAST(MAX(CASE WHEN A.Nombre = N'Ver' AND SMA.Activo = 1 AND ISNULL(PR.Activo, 0) = 1 THEN 1 ELSE 0 END) AS bit) AS Ver,
    CAST(MAX(CASE WHEN A.Nombre = N'Crear' AND SMA.Activo = 1 AND ISNULL(PR.Activo, 0) = 1 THEN 1 ELSE 0 END) AS bit) AS Crear,
    CAST(MAX(CASE WHEN A.Nombre = N'Editar' AND SMA.Activo = 1 AND ISNULL(PR.Activo, 0) = 1 THEN 1 ELSE 0 END) AS bit) AS Editar,
    CAST(MAX(CASE WHEN A.Nombre = N'Eliminar' AND SMA.Activo = 1 AND ISNULL(PR.Activo, 0) = 1 THEN 1 ELSE 0 END) AS bit) AS Eliminar
FROM dbo.Roles R
CROSS JOIN dbo.SubMenus SM
LEFT JOIN dbo.SubMenuAcciones SMA
    ON SMA.SubMenuID = SM.SubMenuID
LEFT JOIN dbo.Acciones A
    ON A.AccionID = SMA.AccionID
LEFT JOIN dbo.PermisosPorRol PR
    ON PR.RolID = R.RolID
   AND PR.SubMenuAccionID = SMA.SubMenuAccionID
WHERE SM.SubMenuID = @SubMenuID
GROUP BY
    R.RolID,
    R.NombreRol
ORDER BY
    CASE WHEN R.RolID = @RolAdministradorID THEN 0 ELSE 1 END,
    R.NombreRol;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuId;
            cmd.Parameters.Add("@RolAdministradorID", SqlDbType.Int).Value = RolAdministradorId;

            await using var rd = await cmd.ExecuteReaderAsync();

            var roles = new List<object>();

            while (await rd.ReadAsync())
            {
                var ver = Convert.ToBoolean(rd["Ver"]);
                var crear = Convert.ToBoolean(rd["Crear"]);
                var editar = Convert.ToBoolean(rd["Editar"]);
                var eliminar = Convert.ToBoolean(rd["Eliminar"]);

                roles.Add(new
                {
                    rolId = Convert.ToInt32(rd["RolID"]),
                    nombre = rd["NombreRol"]?.ToString() ?? string.Empty,
                    ver,
                    crear,
                    editar,
                    eliminar,
                    tieneAcceso = ver || crear || editar || eliminar
                });
            }

            return Json(new
            {
                ok = true,
                subMenuId,
                roles
            });
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerRoles()
        {
            if (!EsAdministrador())
                return Forbid();

            var roles =
                new List<RolMenuAdminVm>();

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string sql = @"
SELECT
    RolID,
    NombreRol
FROM dbo.Roles
ORDER BY NombreRol;";

            await using var cmd =
                new SqlCommand(sql, connection);

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                roles.Add(
                    new RolMenuAdminVm
                    {
                        RolID =
                            rd.GetInt32(
                                rd.GetOrdinal("RolID")),

                        Nombre =
                            rd["NombreRol"]?.ToString()
                            ?? string.Empty
                    });
            }

            return Json(new
            {
                ok = true,
                roles = roles.Select(x => new
                {
                    rolId = x.RolID,
                    nombre = x.Nombre,
                    esAdministrador = x.RolID == RolAdministradorId
                }).ToList()
            });
        }

   

        private async Task<AdministracionMenusVm>
            ObtenerEstructuraCompletaAsync()
        {
            var model =
                new AdministracionMenusVm();

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string sql = @"
SELECT
    G.MenuGrupoID,
    G.Nombre AS GrupoNombre,
    G.Descripcion AS GrupoDescripcion,
    G.IconoCss,
    G.Orden,
    G.Activo AS GrupoActivo,

    M.MenuID,
    M.Nombre AS MenuNombre,

    SM.SubMenuID,
    SM.Nombre AS SubMenuNombre,
    SM.UrlEnlace,
    SM.Descripcion AS SubMenuDescripcion,
    SM.Activo AS SubMenuActivo,

    SMA.SubMenuAccionID,

    A.AccionID,
    A.Nombre AS AccionNombre,

    ISNULL(PR.PermisoRolID, 0) AS PermisoRolID,
    CAST(CASE WHEN ISNULL(SMA.Activo,0)=1 AND ISNULL(PR.Activo,0)=1 THEN 1 ELSE 0 END AS bit) AS PermisoAdminActivo

FROM dbo.MenuGrupo G

LEFT JOIN dbo.Menus M
    ON M.MenuGrupoID = G.MenuGrupoID

LEFT JOIN dbo.SubMenus SM
    ON SM.MenuID = M.MenuID

LEFT JOIN dbo.SubMenuAcciones SMA
    ON SMA.SubMenuID = SM.SubMenuID

LEFT JOIN dbo.Acciones A
    ON A.AccionID = SMA.AccionID

LEFT JOIN dbo.PermisosPorRol PR
    ON PR.SubMenuAccionID = SMA.SubMenuAccionID
   AND PR.RolID = @RolAdminID

ORDER BY
    G.Orden,
    G.MenuGrupoID,
    M.MenuID,
    SM.SubMenuID,
    A.AccionID;";

            await using var cmd =
                new SqlCommand(sql, connection);

            cmd.Parameters.AddWithValue(
                "@RolAdminID",
                RolAdministradorId);

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var grupoId =
                    Convert.ToInt32(
                        rd["MenuGrupoID"]);

                var grupo =
                    model.Grupos.FirstOrDefault(
                        x =>
                            x.MenuGrupoID ==
                            grupoId);

                if (grupo == null)
                {
                    grupo =
                        new AdministracionMenuGrupoVm
                        {
                            MenuGrupoID =
                                grupoId,

                            Nombre =
                                rd["GrupoNombre"]?.ToString()
                                ?? string.Empty,

                            Descripcion =
                                rd["GrupoDescripcion"]?.ToString()
                                ?? string.Empty,

                            IconoCss =
                                rd["IconoCss"]?.ToString()
                                ?? string.Empty,

                            Orden =
                                Convert.ToInt32(
                                    rd["Orden"]),

                            Activo =
                                Convert.ToBoolean(
                                    rd["GrupoActivo"])
                        };

                    model.Grupos.Add(grupo);
                }

                if (rd["MenuID"] == DBNull.Value)
                    continue;

                var menuId =
                    Convert.ToInt32(
                        rd["MenuID"]);

                var menu =
                    grupo.Menus.FirstOrDefault(
                        x =>
                            x.MenuID ==
                            menuId);

                if (menu == null)
                {
                    menu =
                        new AdministracionMenuVm
                        {
                            MenuID =
                                menuId,

                            Nombre =
                                rd["MenuNombre"]?.ToString()
                                ?? string.Empty
                        };

                    grupo.Menus.Add(menu);
                }

                if (rd["SubMenuID"] == DBNull.Value)
                    continue;

                var subMenuId =
                    Convert.ToInt32(
                        rd["SubMenuID"]);

                var subMenu =
                    menu.SubMenus.FirstOrDefault(
                        x =>
                            x.SubMenuID ==
                            subMenuId);

                if (subMenu == null)
                {
                    subMenu =
                        new AdministracionSubMenuVm
                        {
                            SubMenuID =
                                subMenuId,

                            Nombre =
                                rd["SubMenuNombre"]?.ToString()
                                ?? string.Empty,

                            UrlEnlace =
                                rd["UrlEnlace"]?.ToString()
                                ?? string.Empty,

                            Descripcion =
                                rd["SubMenuDescripcion"]?.ToString()
                                ?? string.Empty,

                            Activo =
                                Convert.ToBoolean(
                                    rd["SubMenuActivo"])
                        };

                    menu.SubMenus.Add(subMenu);
                }

                if (rd["AccionID"] == DBNull.Value)
                    continue;

                var accionId =
                    Convert.ToInt32(
                        rd["AccionID"]);

                if (subMenu.Acciones.Any(
                    x =>
                        x.AccionID ==
                        accionId))
                {
                    continue;
                }

                subMenu.Acciones.Add(
                    new AdministracionAccionVm
                    {
                        AccionID =
                            accionId,

                        SubMenuAccionID =
                            rd["SubMenuAccionID"] ==
                            DBNull.Value
                                ? 0
                                : Convert.ToInt32(
                                    rd["SubMenuAccionID"]),

                        Nombre =
                            rd["AccionNombre"]?.ToString()
                            ?? string.Empty,

                        PermisoAdministrador =
                            Convert.ToBoolean(
                                rd["PermisoAdminActivo"])
                    });
            }

            return model;
        }

        private async Task ActualizarSidebarSesionAsync(SqlConnection connection)
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
            {
                HttpContext.Session.Remove("MenuSidebar");
                return;
            }

            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");

            if (!empresaId.HasValue)
            {
                var empresaTexto = HttpContext.Session.GetString("EmpresaId");

                if (int.TryParse(empresaTexto, out var empresaParseada))
                    empresaId = empresaParseada;
            }

            var rolId = HttpContext.Session.GetInt32("RolID") ?? 0;
            var esAdministrador = rolId == RolAdministradorId;

            const string sql = @"
WITH Permisos AS
(
    SELECT SubMenuID
    FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID, @EmpresaID)
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
            WHEN P.SubMenuID IS NOT NULL THEN 1
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

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;

            var pEmpresa = cmd.Parameters.Add("@EmpresaID", SqlDbType.Int);
            pEmpresa.Value = (object?)empresaId ?? DBNull.Value;

            var grupos = new Dictionary<int, MenuSidebarGrupoModel>();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var grupoId = rd.GetInt32(rd.GetOrdinal("MenuGrupoID"));
                var tieneSubMenu = !rd.IsDBNull(rd.GetOrdinal("SubMenuID"));
                var tienePermiso = !rd.IsDBNull(rd.GetOrdinal("TienePermiso")) &&
                                   rd.GetBoolean(rd.GetOrdinal("TienePermiso"));

                if (!esAdministrador && (!tieneSubMenu || !tienePermiso))
                    continue;

                if (!grupos.TryGetValue(grupoId, out var grupo))
                {
                    grupo = new MenuSidebarGrupoModel
                    {
                        MenuGrupoID = grupoId,
                        Nombre = rd["GrupoNombre"]?.ToString() ?? string.Empty,
                        Descripcion = rd["GrupoDescripcion"]?.ToString() ?? string.Empty,
                        IconoCss = string.IsNullOrWhiteSpace(rd["IconoCss"]?.ToString())
                            ? "fa-solid fa-layer-group"
                            : rd["IconoCss"]!.ToString()!,
                        Orden = Convert.ToInt32(rd["GrupoOrden"]),
                        Menus = new List<MenuSidebarMenuModel>()
                    };

                    grupos.Add(grupoId, grupo);
                }

                if (!tieneSubMenu || rd["MenuID"] == DBNull.Value)
                    continue;

                if (!esAdministrador && !tienePermiso)
                    continue;

                var menuId = Convert.ToInt32(rd["MenuID"]);

                var menu = grupo.Menus.FirstOrDefault(x => x.MenuID == menuId);

                if (menu == null)
                {
                    menu = new MenuSidebarMenuModel
                    {
                        MenuID = menuId,
                        Nombre = rd["MenuNombre"]?.ToString() ?? string.Empty,
                        IconoCss = ObtenerIconoMenuSidebar(rd["MenuNombre"]?.ToString()),
                        SubMenus = new List<MenuSidebarSubMenuModel>()
                    };

                    grupo.Menus.Add(menu);
                }

                var subMenuId = Convert.ToInt32(rd["SubMenuID"]);

                if (menu.SubMenus.Any(x => x.SubMenuID == subMenuId))
                    continue;

                menu.SubMenus.Add(new MenuSidebarSubMenuModel
                {
                    SubMenuID = subMenuId,
                    Nombre = rd["SubMenuNombre"]?.ToString() ?? string.Empty,
                    UrlEnlace = NormalizarRutaSidebar(rd["UrlEnlace"]?.ToString()),
                    Descripcion = rd["SubMenuDescripcion"]?.ToString() ?? string.Empty
                });
            }

            var sidebar = grupos.Values
                .OrderBy(x => x.Orden)
                .ThenBy(x => x.Nombre)
                .ToList();

            foreach (var grupo in sidebar)
            {
                grupo.Menus = grupo.Menus
                    .OrderBy(x => x.MenuID)
                    .ToList();

                foreach (var menu in grupo.Menus)
                {
                    menu.SubMenus = menu.SubMenus
                        .OrderBy(x => x.SubMenuID)
                        .ToList();
                }

                grupo.TieneContenido = grupo.Menus.Any(x => x.SubMenus.Any());
            }

            var json = System.Text.Json.JsonSerializer.Serialize(
                sidebar,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    WriteIndented = false
                });

            HttpContext.Session.SetString("MenuSidebar", json);
        }

        private static string NormalizarRutaSidebar(string? ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta))
                return "#";

            ruta = ruta.Trim();

            if (ruta == "#")
                return "#";

            if (ruta.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                ruta.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return ruta;

            ruta = ruta.Replace("~", string.Empty);

            if (!ruta.StartsWith("/"))
                ruta = "/" + ruta;

            return ruta;
        }

        private static string ObtenerIconoMenuSidebar(string? nombre)
        {
            var valor = QuitarAcentosMenuSidebar(nombre ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            return valor switch
            {
                var x when x.Contains("programacion") => "fa-solid fa-calendar-days",
                var x when x.Contains("pedido") => "fa-solid fa-clipboard-list",
                var x when x.Contains("disponibilidad") => "fa-solid fa-chart-pie",
                var x when x.Contains("cliente") => "fa-solid fa-users",
                var x when x.Contains("cotizacion") => "fa-solid fa-file-invoice-dollar",
                var x when x.Contains("configuracion") => "fa-solid fa-gears",
                var x when x.Contains("inventario") => "fa-solid fa-boxes-stacked",
                var x when x.Contains("ruta") => "fa-solid fa-route",
                var x when x.Contains("proveedor") => "fa-solid fa-truck-field",
                _ => "fa-solid fa-folder"
            };
        }

        private static string QuitarAcentosMenuSidebar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return texto;

            var normalizado = texto.Normalize(System.Text.NormalizationForm.FormD);
            var resultado = new System.Text.StringBuilder();

            foreach (var caracter in normalizado)
            {
                var categoria = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(caracter);

                if (categoria != System.Globalization.UnicodeCategory.NonSpacingMark)
                    resultado.Append(caracter);
            }

            return resultado
                .ToString()
                .Normalize(System.Text.NormalizationForm.FormC);
        }


        private static async Task
            AsegurarAccionesBaseAsync(
                SqlConnection connection,
                SqlTransaction transaction)
        {
            foreach (var nombre in AccionesBase)
            {
                const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Acciones
    WHERE LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
          = LTRIM(RTRIM(@Nombre)) COLLATE Latin1_General_CI_AI
)
BEGIN
    INSERT INTO dbo.Acciones
    (
        Nombre
    )
    VALUES
    (
        @Nombre
    );
END;";

                await using var cmd =
                    new SqlCommand(
                        sql,
                        connection,
                        transaction);

                cmd.Parameters.AddWithValue(
                    "@Nombre",
                    nombre);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task
            CrearAccionesSubMenuAsync(
                SqlConnection connection,
                SqlTransaction transaction,
                int subMenuId,
                bool ver,
                bool crear,
                bool editar,
                bool eliminar)
        {
            var acciones =
                new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["Ver"] = ver,
                    ["Crear"] = crear,
                    ["Editar"] = editar,
                    ["Eliminar"] = eliminar
                };

            foreach (var accion in acciones)
            {
                if (!accion.Value)
                    continue;

                const string sql = @"
DECLARE @AccionID int;

SELECT TOP 1
    @AccionID = AccionID
FROM dbo.Acciones
WHERE LTRIM(RTRIM(Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@NombreAccion)) COLLATE Latin1_General_CI_AI;

IF @AccionID IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM dbo.SubMenuAcciones
        WHERE SubMenuID = @SubMenuID
          AND AccionID = @AccionID
    )
    BEGIN
        UPDATE dbo.SubMenuAcciones
        SET
            Activo = 1,
            FechaModificacion = GETDATE()
        WHERE SubMenuID = @SubMenuID
          AND AccionID = @AccionID;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.SubMenuAcciones
        (
            SubMenuID,
            AccionID,
            Activo,
            FechaCreacion
        )
        VALUES
        (
            @SubMenuID,
            @AccionID,
            1,
            GETDATE()
        );
    END
END;";

                await using var cmd =
                    new SqlCommand(
                        sql,
                        connection,
                        transaction);

                cmd.Parameters.AddWithValue(
                    "@SubMenuID",
                    subMenuId);

                cmd.Parameters.AddWithValue(
                    "@NombreAccion",
                    accion.Key);

                await cmd.ExecuteNonQueryAsync();
            }
        }
        private IActionResult AccesoAdministrativoDenegado()
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    ok = false,
                    mensaje = "No tienes permisos para realizar esta acción."
                });
        }

        private static async Task
            ActualizarEstadoAccionesSubMenuAsync(
                SqlConnection connection,
                SqlTransaction transaction,
                GuardarAccionesSubMenuRequest model)
        {
            var acciones =
                new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["Ver"] = model.Ver,
                    ["Crear"] = model.Crear,
                    ["Editar"] = model.Editar,
                    ["Eliminar"] = model.Eliminar
                };

            foreach (var accion in acciones)
            {
                const string sql = @"
UPDATE SMA
SET
    SMA.Activo = @Activo,
    SMA.FechaModificacion = GETDATE()
FROM dbo.SubMenuAcciones SMA
INNER JOIN dbo.Acciones A
    ON A.AccionID = SMA.AccionID
WHERE SMA.SubMenuID = @SubMenuID
  AND LTRIM(RTRIM(A.Nombre)) COLLATE Latin1_General_CI_AI
      = LTRIM(RTRIM(@NombreAccion)) COLLATE Latin1_General_CI_AI;";

                await using var cmd =
                    new SqlCommand(
                        sql,
                        connection,
                        transaction);

                cmd.Parameters.AddWithValue(
                    "@SubMenuID",
                    model.SubMenuID);

                cmd.Parameters.AddWithValue(
                    "@NombreAccion",
                    accion.Key);

                cmd.Parameters.Add(
                    "@Activo",
                    SqlDbType.Bit).Value =
                    accion.Value;

                await cmd.ExecuteNonQueryAsync();
            }
        }

       

        private static async Task
            AsignarAccionesSubMenuAlRolAsync(
                SqlConnection connection,
                SqlTransaction transaction,
                int subMenuId,
                int rolId)
        {
            const string sql = @"
UPDATE PR
SET
    PR.Activo = 1,
    PR.FechaModificacion = GETDATE()
FROM dbo.PermisosPorRol PR
INNER JOIN dbo.SubMenuAcciones SMA
    ON SMA.SubMenuAccionID = PR.SubMenuAccionID
WHERE PR.RolID = @RolID
  AND SMA.SubMenuID = @SubMenuID
  AND SMA.Activo = 1;

INSERT INTO dbo.PermisosPorRol
(
    RolID,
    SubMenuAccionID,
    Activo,
    FechaCreacion
)
SELECT
    @RolID,
    SMA.SubMenuAccionID,
    1,
    GETDATE()
FROM dbo.SubMenuAcciones SMA
WHERE SMA.SubMenuID = @SubMenuID
  AND SMA.Activo = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.PermisosPorRol PR
      WHERE PR.RolID = @RolID
        AND PR.SubMenuAccionID = SMA.SubMenuAccionID
  );";

            await using var cmd =
                new SqlCommand(
                    sql,
                    connection,
                    transaction);

            cmd.Parameters.AddWithValue(
                "@RolID",
                rolId);

            cmd.Parameters.AddWithValue(
                "@SubMenuID",
                subMenuId);

            await cmd.ExecuteNonQueryAsync();
        }

     

        private bool EsAdministrador()
        {
            var rolId =
                HttpContext.Session.GetInt32("RolID");

            if (rolId.HasValue)
                return rolId.Value == RolAdministradorId;

            var claimRol =
                User.FindFirst("RolID")?.Value;

            return int.TryParse(
                       claimRol,
                       out var rolClaimId)
                   &&
                   rolClaimId ==
                   RolAdministradorId;
        }
    }
}
