using System.Collections.Generic;

namespace SistemaNutri.Models
{
    public class AdministracionMenusVm
    {
        public List<AdministracionMenuGrupoVm> Grupos { get; set; } = new();
    }

    public class AdministracionMenuGrupoVm
    {
        public int MenuGrupoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string IconoCss { get; set; } = string.Empty;
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public List<AdministracionMenuVm> Menus { get; set; } = new();
    }

    public class AdministracionMenuVm
    {
        public int MenuID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public List<AdministracionSubMenuVm> SubMenus { get; set; } = new();
    }

    public class AdministracionSubMenuVm
    {
        public int SubMenuID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string UrlEnlace { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public List<AdministracionAccionVm> Acciones { get; set; } = new();
    }

    public class AdministracionAccionVm
    {
        public int AccionID { get; set; }
        public int SubMenuAccionID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool PermisoAdministrador { get; set; }
    }

    public class RolMenuAdminVm
    {
        public int RolID { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public class CrearMenuGrupoRequest
    {
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string IconoCss { get; set; } = string.Empty;
        public int Orden { get; set; }
    }

    public class EditarMenuGrupoRequest : CrearMenuGrupoRequest
    {
        public int MenuGrupoID { get; set; }
    }

    public class CrearMenuRequest
    {
        public int MenuGrupoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public class EditarMenuRequest : CrearMenuRequest
    {
        public int MenuID { get; set; }
    }

    public class CrearSubMenuRequest
    {
        public int MenuID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string UrlEnlace { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public bool CrearAcciones { get; set; } = true;
        public bool Ver { get; set; } = true;
        public bool Crear { get; set; } = true;
        public bool Editar { get; set; } = true;
        public bool Eliminar { get; set; } = true;
        public bool AsignarAdministrador { get; set; } = true;
    }

    public class EditarSubMenuRequest
    {
        public int SubMenuID { get; set; }
        public int MenuID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string UrlEnlace { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
    }

    public class GuardarAccionesSubMenuRequest
    {
        public int SubMenuID { get; set; }
        public bool Ver { get; set; }
        public bool Crear { get; set; }
        public bool Editar { get; set; }
        public bool Eliminar { get; set; }
        public bool AsignarAdministrador { get; set; } = true;
    }
}
