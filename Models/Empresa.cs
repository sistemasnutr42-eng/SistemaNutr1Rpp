using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SistemaNutri.Models
{
    public class Empresa
    {
        [Key]
        public int EmpresaID { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public bool Activa { get; set; }

      
    }
}