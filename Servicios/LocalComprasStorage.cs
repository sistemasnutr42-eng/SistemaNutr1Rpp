// Optional private local file storage for Compras.
// In IIS, set Compras:StorageMode=Local and an explicit Compras:StorageRoot.
// Files are stored OUTSIDE wwwroot to avoid unauthenticated direct downloads.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

public sealed class LocalComprasStorage : ISftpStorage
{
    private readonly string _root;

    public LocalComprasStorage(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredRoot = configuration["Compras:StorageRoot"];
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Configura Compras:StorageRoot para el almacenamiento local de IIS.");
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
                throw new InvalidOperationException("No se encontro el directorio local del usuario.");
            configuredRoot = Path.Combine(localData, "SistemaNutri", "ComprasLocal");
        }
        if (!Path.IsPathFullyQualified(configuredRoot) ||
            (OperatingSystem.IsWindows() && configuredRoot.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new InvalidOperationException("Compras:StorageRoot debe ser una ruta absoluta de disco local.");
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
        var contentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ContentRootPath));
        if (_root.Equals(Path.GetPathRoot(_root), StringComparison.OrdinalIgnoreCase) ||
            DentroDe(_root, contentRoot) || DentroDe(contentRoot, _root))
            throw new InvalidOperationException("El almacenamiento de Compras debe estar en una carpeta privada independiente del sitio.");
        VerificarSinEnlaces(_root);
        Directory.CreateDirectory(_root);
        VerificarSinEnlaces(_root);
    }

    private static bool DentroDe(string ruta, string raiz) =>
        ruta.Equals(raiz, StringComparison.OrdinalIgnoreCase) ||
        ruta.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void VerificarSinEnlaces(string ruta)
    {
        for (string? actual = ruta; !string.IsNullOrEmpty(actual); actual = Path.GetDirectoryName(actual))
        {
            try
            {
                if ((File.GetAttributes(actual) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("No se permiten enlaces ni puntos de reanalisis en el almacenamiento de Compras.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private string Resolver(string? rutaRelativa)
    {
        var segmentos = (rutaRelativa ?? string.Empty).Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segmentos.Any(s => s == "." || s == ".." || s.Contains(':') ||
            s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            (OperatingSystem.IsWindows() && (s.EndsWith('.') || s.EndsWith(' ')))))
            throw new InvalidOperationException("La ruta de almacenamiento no es valida.");

        var destino = _root;
        foreach (var segmento in segmentos)
            destino = Path.Combine(destino, segmento);

        destino = Path.GetFullPath(destino);
        if (!destino.Equals(_root, StringComparison.OrdinalIgnoreCase) &&
            !destino.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta fuera del almacenamiento local.");

        VerificarSinEnlaces(destino);
        return destino;
    }

    public void AsegurarDirectorio(string rutaRelativaDirectorio)
        => Directory.CreateDirectory(Resolver(rutaRelativaDirectorio));

    public void SubirStream(Stream origen, string rutaRelativa)
    {
        var ruta = Resolver(rutaRelativa);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var destino = new FileStream(ruta, FileMode.Create, FileAccess.Write, FileShare.None);
        origen.CopyTo(destino);
    }

    public void SubirTexto(string rutaRelativa, string texto)
    {
        var ruta = Resolver(rutaRelativa);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, texto, Encoding.UTF8);
    }

    public string DescargarTexto(string rutaRelativa)
        => File.ReadAllText(Resolver(rutaRelativa), Encoding.UTF8);

    public byte[] DescargarBytes(string rutaRelativa)
        => File.ReadAllBytes(Resolver(rutaRelativa));

    public IEnumerable<(string Nombre, bool EsCarpeta, long Tamano, DateTime UltimaModUtc)>
        Listar(string rutaRelativaDirectorio)
    {
        var dir = Resolver(rutaRelativaDirectorio);
        if (!Directory.Exists(dir))
            yield break;

        foreach (var ruta in Directory.EnumerateFileSystemEntries(dir))
        {
            var esCarpeta = Directory.Exists(ruta);
            var tamano = esCarpeta ? 0L : new FileInfo(ruta).Length;
            var ultima = esCarpeta ? Directory.GetLastWriteTimeUtc(ruta) : File.GetLastWriteTimeUtc(ruta);
            yield return (Path.GetFileName(ruta), esCarpeta, tamano, ultima);
        }
    }

    public void EliminarRecursivo(string rutaRelativa)
    {
        var ruta = Resolver(rutaRelativa);
        if (ruta.Equals(_root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("No se permite borrar la carpeta raiz.");

        if (Directory.Exists(ruta)) Directory.Delete(ruta, recursive: true);
        else if (File.Exists(ruta)) File.Delete(ruta);
    }
}
