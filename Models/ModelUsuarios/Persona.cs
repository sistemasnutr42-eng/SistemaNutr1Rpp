using System.ComponentModel.DataAnnotations;

namespace SistemaNutri.Models
{
    public class Persona
    {
        [Key]
        public int PersonaID { get; set; }

        public string? Nombre { get; set; }

        public string? ApellidoPaterno { get; set; }

        public string? ApellidoMaterno { get; set; }

        public string? Correo { get; set; }

        public string? Telefono { get; set; }

        public DateTime? FechaNacimiento { get; set; }

        public string? NumeroEmpleado { get; set; }

        public string? ClaveEmpleadoNomina { get; set; }

        public DateTime? FechaIngreso { get; set; }

        public string? Puesto { get; set; }

        public int? JefeInmediatoPersonaID { get; set; }

        public string? FotoRuta { get; set; }
    }
}