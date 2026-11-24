# SistemaNutri - desarrollo local e IIS

Código consolidado de los ZIP de Adrian y Arleth del 24/11/2026. Incluye las implementaciones de Litzi que estaban en la copia de Adrian; no se contrastó una rama independiente de Litzi.

## Desarrollo local

1. Instalar el SDK .NET 10.
2. Abrir PowerShell en la carpeta con `SistemaNutri.csproj`.
3. Configurar `ConnectionStrings:DefaultConnection` para una base de pruebas autorizada mediante .NET User Secrets o variables de entorno. El archivo `.csproj` ya contiene UserSecretsId. No guardar credenciales en el perfil ni en Git.
4. Ejecutar `dotnet run` (o `dotnet run --launch-profile SistemaNutri`). En Visual Studio seleccionar el perfil SistemaNutri. Abrir http://localhost:5053/Compras/Index después de iniciar sesión.

El perfil compartido `Properties/launchSettings.json` configura Development y almacenamiento Local. No contiene credenciales ni activa compradores de demostración. La carpeta local de documentos predeterminada es `%LOCALAPPDATA%\SistemaNutri\ComprasLocal`. No usar `--no-launch-profile` salvo que se proporcionen las variables equivalentes.

## IIS

En producción conservar `ASPNETCORE_ENVIRONMENT=Production`, la configuración SQL y correo ya autorizada, y las dos variables de IIS `Compras__StorageMode=Local` y `Compras__StorageRoot=C:\ProgramData\SistemaNutri\Compras`. El Application Pool debe tener permisos de lectura/escritura sobre esa carpeta privada, fuera del sitio web. Si no se selecciona Local, se conserva el modo SFTP. El perfil de desarrollo no configura IIS ni se aplica a una ejecución de la publicación.

Para publicar: `dotnet publish .\SistemaNutri.csproj -c Release --self-contained false -o C:\Temp\SistemaNutri_Publicacion`.

No ejecutar migraciones SQL por este cambio. Los documentos existentes no se trasladan automáticamente entre PCs y el servidor; conservar las rutas relativas y usar datos de pruebas para desarrollo.

## Git

Conservar `Properties/launchSettings.json` al empaquetar. `.gitignore` permite específicamente ese archivo, pero continúa ignorando los demás JSON según la política original de Arleth. Antes de subir, revisar las credenciales y archivos de datos de todo el repositorio. No incluir documentos generados, configuraciones locales sensibles ni respaldos.

Paquete generado a partir de la fusión previamente revisada y del FIX de Compras IIS, no de la carpeta actual de una PC. No incluye cambios posteriores que no se hayan compartido. Validación estática del contenido; la compilación y las pruebas de ejecución deben efectuarse en Windows.
