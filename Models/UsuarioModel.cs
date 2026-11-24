using SistemaNutri.Models.ModelUsuarios;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SistemaNutri.Models
{
    public class UsuarioModel
    {
        [Key]
        public int UsuarioID { get; set; }
        public int PersonaID { get; set; }
        public int EmpresaID { get; set; }
        public string Username { get; set; }



      
        public string Password { get; set; } = string.Empty;

        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;

       
        public string NombreEmpresa { get; set; } = string.Empty;
        public string ColorPrimario { get; set; } = string.Empty;
        public string Logo { get; set; } = string.Empty;

        
        public string Rol { get; set; } = string.Empty;

       
        public List<EmpresaModel> Empresas { get; set; } = new List<EmpresaModel>();
    }

    
    public class EmpresaModel
    {
        [Key]
        public int EmpresaID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Logo { get; set; } = string.Empty;
        public string ColorPrimario { get; set; } = string.Empty;
    }

   
    public class UsuarioPerfilViewModel
    {
        [Key]
        public string NombreUsuario { get; set; } = "";
        public string Username { get; set; } = "";
        public string? Correo { get; set; }
        public string? Telefono { get; set; }
        public string? NombreRol { get; set; }
        public string? DescripcionRol { get; set; }
    }
}


