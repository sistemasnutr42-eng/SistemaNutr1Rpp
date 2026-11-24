using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaNutri.Controllers
{
    /*
     * SEGURIDAD de Líneas genéticas.
     * Copia exacta de UsuarioPerteneceEmpresaAsync y TienePermisoAccionAsync de ClientesController,
     * para que las dos pantallas apliquen la misma regla de acceso.
     * Usa la constante RolAdministradorId definida en LineasGeneticasController.cs.
     */
    public partial class LineasGeneticasController
    {
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
    }
}
