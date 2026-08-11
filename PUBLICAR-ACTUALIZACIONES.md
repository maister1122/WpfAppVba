# Publicar actualizaciones (Velopack)

Guía del flujo de release de la app con **Velopack**. Reemplaza al paso de Inno Setup
para la distribución con auto-actualización.

## ⭐ Forma recomendada: publicar desde cualquier sesión (GitHub Actions)

GitHub compila la app en un **Windows en la nube** (workflow `.github/workflows/release.yml`),
así que **no necesitas tu PC ni un token manual**. Funciona incluso desde Claude en la web.

### Procedimiento oficial (2 pasos)

> ⚠️ Clave: el número de versión debe estar en **`master`** ANTES de lanzar el workflow.
> El bump de versión que se quede solo en una rama de trabajo NO cuenta.

**Paso 1 — El cambio de versión llega a `master`.**
Sube `<Version>` en `SistemaGestion/SistemaGestion.csproj` (ej. `1.0.0` → `1.0.1`) y haz que ese
cambio esté en `master` (commit directo o vía PR mergeado). Si trabajas con Claude:
*"sube la versión a 1.0.1 en master"*.

**Paso 2 — Lanzar el workflow (lo haces tú con un clic).**
GitHub no permite que Claude (web) dispare releases, así que este paso es manual:

1. Abre <https://github.com/maister1122/WpfAppVba/actions/workflows/release.yml>
2. Botón **Run workflow**.
3. En *version* escribe la versión (ej. `1.0.1`) o déjalo vacío para usar la del csproj.
4. **Run workflow** (verde).

GitHub compila, empaqueta y publica la Release. Las apps instaladas verán **🔄 Actualizar**
la próxima vez que se abran.

> **Resumen del reparto:** Claude sube la versión a `master` → tú das **Run workflow**.

> También se puede disparar empujando un tag `vX.Y.Z` desde una PC con git
> (`git tag v1.0.1 && git push origin v1.0.1`). Útil si publicas desde tu máquina.

---

Lo de abajo es el camino **manual** (publicar desde tu propia PC Windows), por si lo
necesitas. Con GitHub Actions normalmente no hace falta.

## Requisitos (una sola vez)

```powershell
# Instalar la CLI de Velopack como herramienta global de .NET
dotnet tool install -g vpk
```

El feed de actualización es el repo **público** `https://github.com/maister1122/WpfAppVba`
(configurado en `SistemaGestion/ActualizadorApp.cs`). Para **subir** releases necesitas un
token de GitHub con permiso sobre ese repo.

### Crear el token de GitHub (una sola vez)

Dos tipos de token sirven. El **Fine-grained** es el recomendado (permisos mínimos).

**Opción A — Fine-grained token (recomendado):**

1. Entra a <https://github.com/settings/tokens?type=beta> (o: foto de perfil →
   *Settings* → *Developer settings* → *Personal access tokens* → *Fine-grained tokens*).
2. *Generate new token*.
3. **Token name**: algo como `publicar-wpfappvba`.
4. **Expiration**: elige una fecha (ej. 90 días o *No expiration* si lo prefieres).
5. **Repository access** → *Only select repositories* → marca **`wpfappvba`**.
6. **Permissions** → *Repository permissions* → busca **Contents** → ponlo en
   **Read and write**. (Eso basta para crear releases y subir los archivos.)
7. *Generate token* y **copia el valor** (empieza por `github_pat_…`). Solo se muestra
   una vez.

**Opción B — Token clásico (más simple, más permisos):**

1. Entra a <https://github.com/settings/tokens> → *Generate new token (classic)*.
2. **Note**: `publicar-wpfappvba`; elige *Expiration*.
3. Marca el scope **`repo`** (completo).
4. *Generate token* y copia el valor (empieza por `ghp_…`).

### Usar el token

Pásalo como variable de entorno (no lo escribas en sitios compartidos ni lo subas al repo):

```powershell
$env:GITHUB_TOKEN = "PEGA_AQUÍ_TU_TOKEN"
```

> 🔒 **Seguridad:** trata el token como una contraseña. Si se filtra (lo pegas en un
> chat, captura, commit…), revócalo en la misma página de *Settings → tokens* y crea
> otro. El token **no** va dentro de la app ni del repo: solo lo usas tú al publicar.

## Publicar una versión nueva — forma rápida (un solo comando)

Usa el script `publicar.ps1` (raíz del repo). Lee la versión sola del csproj y hace
los 3 pasos (publish → pack → upload):

```powershell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxx"
# 1) Sube <Version> en SistemaGestion\SistemaGestion.csproj (ej. 1.0.0 -> 1.0.1)
# 2) Ejecuta:
.\publicar.ps1
```

Si quieres forzar una versión sin tocar el csproj: `.\publicar.ps1 -Version 1.2.0`.

El resto de esta guía explica los mismos 3 pasos a mano (por si quieres entenderlos
o ajustarlos).

## Publicar una versión nueva — paso a paso (manual)

1. **Sube el número de versión** en `SistemaGestion/SistemaGestion.csproj` (`<Version>`).
   Ej.: `1.0.0` → `1.0.1`. Velopack solo ofrece la actualización si la release es mayor
   que la instalada.

2. **Publica** la app como ya lo haces (self-contained single-file):

   ```powershell
   dotnet publish SistemaGestion/SistemaGestion.csproj -c Release -r win-x64 --self-contained true `
       -p:PublishSingleFile=true -p:DebugType=none `
       -o .\publish
   ```

3. **Empaqueta** con Velopack (genera Setup.exe + .nupkg full + delta + RELEASES):

   ```powershell
   vpk pack `
       --packId SistemaGestion `
       --packVersion 1.0.1 `
       --packDir .\publish `
       --mainExe SistemaGestion.exe `
       --packTitle "Sistema de Gestión" `
       --icon SistemaGestion\icono.ico
   ```

   > `--packVersion` debe coincidir con `<Version>` del csproj.
   > El resultado queda en `.\Releases`.

4. **Sube** las releases al repo público de GitHub:

   ```powershell
   vpk upload github `
       --repoUrl https://github.com/maister1122/WpfAppVba `
       --publish `
       --releaseName "v1.0.1" `
       --tag v1.0.1 `
       --token $env:GITHUB_TOKEN
   ```

Listo. Los usuarios con la app instalada verán el botón **🔄 Actualizar** en la barra
superior la próxima vez que la abran.

## Primera distribución (instalador inicial)

La **primera vez**, el usuario instala con el `Setup.exe` que genera `vpk pack`
(en `.\Releases\SistemaGestion-win-Setup.exe`). Se instala **por-usuario** en
`%LocalAppData%\SistemaGestion` (sin permisos de administrador, sin UAC).
A partir de ahí, todas las actualizaciones son in-app.

## Cómo se ve para el usuario

1. Al abrir la app, en segundo plano se consulta si hay versión nueva (no molesta).
2. Si la hay, aparece **🔄 Actualizar** en la barra superior. Es **opcional**.
3. Al pulsarlo, descarga en segundo plano con **barra de progreso** (sigue usando la app).
4. Al terminar, el botón cambia a **✅ Reiniciar para actualizar**.
5. El usuario reinicia **cuando quiera** y la app vuelve a abrir ya actualizada.

## Problemas al instalar en otra PC

### "Sistema de Gestión requires at least NNN MB disk space to be installed. There is only NNN MB available."

No es un error de la app: el `Setup.exe` de Velopack comprueba el espacio libre
**antes** de instalar y esa PC no lo tiene. La cuenta que hace Velopack es:

```
espacio exigido = tamaño del paquete comprimido + tamaño ya extraído + 50 MB de margen
```

y lo mide en la unidad donde va a instalar (por defecto `C:`, en
`%LocalAppData%\SistemaGestion`).

**Soluciones, en orden:**

1. **Liberar espacio en la PC** (es la causa real). Conviene dejar **1 GB o más
   libre**, no lo justo: cada actualización necesita espacio para descargar el
   paquete nuevo y extraerlo mientras la versión vieja todavía está en disco.
   Rápido: Papelera, `Configuración > Sistema > Almacenamiento > Archivos
   temporales`, y `%LocalAppData%\Temp`.
2. **Instalar en otra unidad** que sí tenga espacio (ej. un disco `D:`), desde
   `cmd` en la carpeta del instalador:

   ```
   SistemaGestion-win-Setup.exe --installto D:\SistemaGestion
   ```

   (`--silent` además instala sin diálogos.)
3. Si ya está instalada y el disco quedó corto, borrar paquetes viejos de
   `%LocalAppData%\SistemaGestion\packages` (se vuelven a descargar si hicieran falta).

La app es self-contained (lleva el runtime de .NET adentro), así que el
instalador siempre va a pedir unos cuantos cientos de MB. Se puede achicar
(comprimir el single-file al publicar, recortar idiomas de las librerías), pero
eso hay que probarlo en Windows antes de publicarlo y de todas formas no
reemplaza tener disco libre.

## Notas

- En **desarrollo** (ejecutando desde Visual Studio o `bin\`), el botón nunca aparece:
  Velopack solo actúa cuando la app está instalada vía su `Setup.exe`.
- Las actualizaciones son **delta** (solo se descarga lo que cambió entre versiones),
  por eso son rápidas aunque el ejecutable completo pese ~160 MB.
- Firmar el `Setup.exe` con un certificado de código es **opcional** pero recomendable
  para evitar el aviso de SmartScreen de Windows (`vpk pack --signParams ...`).
