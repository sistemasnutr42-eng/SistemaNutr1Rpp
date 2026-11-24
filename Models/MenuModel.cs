
namespace SistemaNutri.Models
{
    
    public class MenuModel
    {
        public int MenuID { get; set; }
        public string Nombre { get; set; }
        public string? Icono { get; set; } 
        public string? Url { get; set; }
        public int Orden { get; set; }
        public int? MenuPadreID { get; set; }
        public List<MenuModel> SubMenus { get; set; } = new List<MenuModel>();
        public string Descripcion { get; set; } = string.Empty;
    }

}
