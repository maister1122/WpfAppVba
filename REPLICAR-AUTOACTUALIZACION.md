# Guía: Replicar el sistema de auto-actualización por versiones

Documento de **respaldo y referencia**. Explica, paso a paso y con todo el código,
cómo está construido el sistema de actualización automática de esta app, para poder
**reproducirlo desde cero** en este u otro proyecto WPF (.NET).

> Tecnologías: **WPF / .NET 8**, **[Velopack](https://velopack.io)** (motor de updates)
> y **GitHub Actions** (compila y publica las releases).

---

## 1. Cómo funciona (resumen)

1. La app se instala con un `Setup.exe` que genera Velopack (instalación **por-usuario**
   en `%LocalAppData%`, sin admin).
2. Al arrancar, la app consulta las **releases de GitHub** (el "feed") y, si hay una
   versión más nueva, muestra un botón **🔄 Actualizar** (manual / opt-in).
3. El usuario pulsa → descarga en segundo plano con barra de progreso → al terminar,
   botón **✅ Reiniciar** → la app se reabre ya actualizada.
4. Publicar una versión nueva = subir `<Version>` + lanzar el workflow de GitHub Actions.

**Requisito clave:** el repositorio debe ser **público** (para que la app lea las
releases sin token).

---

## 2. Piezas del sistema (mapa de archivos)

| Archivo | Rol |
|---|---|
| `SistemaGestion/SistemaGestion.csproj` | Paquete `Velopack` + `<Version>` |
| `SistemaGestion/App.xaml.cs` | `VelopackApp.Build().Run()` al arrancar |
| `SistemaGestion/ActualizadorApp.cs` | Servicio que envuelve Velopack (buscar/descargar/reiniciar) |
| `SistemaGestion/ConsolaMovimientos.xaml` | Botón + barra de progreso en la top bar |
| `SistemaGestion/ConsolaMovimientos.xaml.cs` | Lógica del flujo de actualización |
| `.github/workflows/release.yml` | Compila en Windows y publica la release |
| `publicar.ps1` | (Alternativa) publica desde una PC Windows en un comando |

---

## 3. Paso a paso para replicarlo

### Paso 1 — Añadir Velopack y la versión (`.csproj`)

```xml
<PropertyGroup>
  <!-- ...resto de tu config... -->
  <!-- Velopack la usa para decidir si una release es "más nueva". -->
  <Version>1.0.0</Version>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Velopack" Version="1.2.0" />
</ItemGroup>
```

### Paso 2 — Inicializar Velopack al arrancar (`App.xaml.cs`)

`VelopackApp.Build().Run()` DEBE ser de lo primero que se ejecuta. Gestiona los hooks
de instalación/actualización (el instalador invoca la app con argumentos especiales).

```csharp
using Velopack;

protected override void OnStartup(StartupEventArgs e)
{
    // Velopack: antes que cualquier otra cosa de la app.
    VelopackApp.Build().Run();

    // ...resto de tu arranque...
    base.OnStartup(e);
}
```

### Paso 3 — Servicio de actualización (`ActualizadorApp.cs`)

Encapsula toda la lógica de Velopack. **Cambia `RepoUrl` por tu repo.**

```csharp
using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace SistemaGestion
{
    /// <summary>
    /// Auto-actualización con Velopack. Flujo manual/opt-in:
    ///   1. HayActualizacionAsync()  → consulta el feed al arrancar.
    ///   2. DescargarAsync(progreso) → cuando el usuario pulsa "Actualizar".
    ///   3. AplicarYReiniciar()      → cuando el usuario pulsa "Reiniciar".
    /// </summary>
    public class ActualizadorApp
    {
        // Repo PÚBLICO que aloja las releases (Setup.exe + *.nupkg + RELEASES).
        private const string RepoUrl = "https://github.com/maister1122/WpfAppVba";

        private readonly UpdateManager _mgr;
        private UpdateInfo? _update;

        public ActualizadorApp()
        {
            // accessToken null => repo público; prerelease false => solo estables.
            _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }

        public string? VersionNueva => _update?.TargetFullRelease.Version.ToString();

        /// <summary>Tamaño del paquete de la versión nueva, en MB.</summary>
        public double TamañoDescargaMB => (_update?.TargetFullRelease.Size ?? 0) / 1024.0 / 1024.0;

        /// <summary>True si hay una versión más nueva que la instalada.</summary>
        public async Task<bool> HayActualizacionAsync()
        {
            if (!_mgr.IsInstalled) return false;   // en dev (bin/VS) no hay updates
            _update = await _mgr.CheckForUpdatesAsync();
            return _update != null;
        }

        /// <summary>Descarga en segundo plano informando el progreso (0–100).</summary>
        public async Task DescargarAsync(IProgress<int> progreso)
        {
            if (_update == null) return;
            await _mgr.DownloadUpdatesAsync(_update, p => progreso.Report(p));
        }

        /// <summary>Aplica lo descargado y reinicia ya en la nueva versión.</summary>
        public void AplicarYReiniciar()
        {
            if (_update == null) return;
            _mgr.ApplyUpdatesAndRestart(_update.TargetFullRelease); // Velopack 1.x: VelopackAsset
        }
    }
}
```

### Paso 4 — UI en la barra superior (`ConsolaMovimientos.xaml`)

Bloque con 3 estados: botón Actualizar / barra de progreso / botón Reiniciar.
Colócalo donde quieras dentro de tu top bar.

```xml
<!-- Actualización (manual/opt-in). Oculto si no hay versión nueva. -->
<Border x:Name="BloqueActualizar" Visibility="Collapsed" VerticalAlignment="Center" Margin="0,0,10,0">
    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">

        <!-- Estado A: hay versión nueva -->
        <Button x:Name="BtnActualizar" Visibility="Collapsed" Content="🔄  Actualizar"
                Background="#6C4AE3" Click="BtnActualizar_Click"/>

        <!-- Estado B: descargando -->
        <StackPanel x:Name="PanelDescarga" Orientation="Horizontal" Visibility="Collapsed"
                    VerticalAlignment="Center">
            <TextBlock x:Name="LblDescarga" Text="Descargando…" Foreground="#9AA3B8"
                       VerticalAlignment="Center" Margin="0,0,8,0"/>
            <ProgressBar x:Name="BarraDescarga" Width="120" Height="8" Minimum="0" Maximum="100"
                         Value="0" VerticalAlignment="Center"/>
        </StackPanel>

        <!-- Estado C: lista para reiniciar -->
        <Button x:Name="BtnReiniciar" Visibility="Collapsed" Content="✅  Reiniciar para actualizar"
                Background="#2E7D32" Click="BtnReiniciar_Click"/>
    </StackPanel>
</Border>
```

### Paso 5 — Lógica del flujo (`ConsolaMovimientos.xaml.cs`)

```csharp
// Campo de la clase:
private readonly ActualizadorApp _actualizador = new();

// En el constructor (tras InitializeComponent), lanzar el chequeo en segundo plano:
_ = BuscarActualizacionesAsync();

// ── Métodos ──────────────────────────────────────────────────────────
private async Task BuscarActualizacionesAsync()
{
    try
    {
        if (await _actualizador.HayActualizacionAsync())
        {
            BloqueActualizar.Visibility = Visibility.Visible;
            BtnActualizar.Visibility    = Visibility.Visible;
            BtnActualizar.ToolTip       = $"Nueva versión disponible: {_actualizador.VersionNueva}";
        }
    }
    catch { /* sin red/feed: silencioso, se reintenta al próximo arranque */ }
}

private async void BtnActualizar_Click(object sender, RoutedEventArgs e)
{
    BtnActualizar.Visibility = Visibility.Collapsed;
    PanelDescarga.Visibility = Visibility.Visible;
    BarraDescarga.Value      = 0;

    double totalMB = _actualizador.TamañoDescargaMB;
    var progreso = new Progress<int>(p =>
    {
        BarraDescarga.Value = p;
        double bajadoMB = totalMB * p / 100.0;
        LblDescarga.Text = totalMB > 0
            ? $"Descargando… {bajadoMB:0.0} / {totalMB:0.0} MB ({p}%)"
            : $"Descargando… {p}%";
    });

    try
    {
        await _actualizador.DescargarAsync(progreso);
        PanelDescarga.Visibility = Visibility.Collapsed;
        BtnReiniciar.Visibility  = Visibility.Visible;
    }
    catch
    {
        PanelDescarga.Visibility = Visibility.Collapsed;
        BtnActualizar.Visibility = Visibility.Visible;
        MessageBox.Show("No se pudo descargar la actualización. Revisa tu conexión e inténtalo de nuevo.",
                        "Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

private void BtnReiniciar_Click(object sender, RoutedEventArgs e)
    => _actualizador.AplicarYReiniciar();
```

### Paso 6 — Workflow de publicación (`.github/workflows/release.yml`)

Compila la app WPF en un Windows de la nube y publica la release. Usa el `GITHUB_TOKEN`
automático de Actions (sin token manual). **Cambia el repo y el packId por los tuyos.**

```yaml
name: Publicar release (Velopack)

on:
  push:
    tags: ['v*']
  workflow_dispatch:
    inputs:
      version:
        description: 'Versión a publicar (ej. 1.0.1). Vacío = usar la del csproj.'
        required: false

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '8.0.x'
      - run: dotnet tool install -g vpk

      - name: Determinar versión
        id: ver
        shell: pwsh
        run: |
          $v = "${{ github.event.inputs.version }}"
          if ([string]::IsNullOrWhiteSpace($v)) {
            if ("${{ github.ref_type }}" -eq "tag") { $v = "${{ github.ref_name }}".TrimStart("v") }
            else {
              $m = Select-String -Path SistemaGestion/SistemaGestion.csproj -Pattern '<Version>\s*([^<]+?)\s*</Version>' | Select-Object -First 1
              $v = $m.Matches[0].Groups[1].Value.Trim()
            }
          }
          "version=$v" >> $env:GITHUB_OUTPUT

      - name: Publish
        run: dotnet publish SistemaGestion/SistemaGestion.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o ./publish

      - name: Pack
        run: vpk pack --packId SistemaGestion --packVersion ${{ steps.ver.outputs.version }} --packDir ./publish --mainExe SistemaGestion.exe --packTitle "Sistema de Gestión" --icon SistemaGestion/icono.ico

      - name: Upload
        run: vpk upload github --repoUrl https://github.com/maister1122/WpfAppVba --publish --releaseName "v${{ steps.ver.outputs.version }}" --tag "v${{ steps.ver.outputs.version }}" --token ${{ secrets.GITHUB_TOKEN }}
```

### Paso 7 — Hacer público el repositorio

Settings → Danger Zone → **Change repository visibility** → **Public**.
Sin esto, la app (sin token) no puede leer las releases y el botón nunca aparece.

---

## 4. Cómo publicar una versión nueva (uso diario)

1. Sube `<Version>` en `SistemaGestion/SistemaGestion.csproj` (ej. `1.0.1` → `1.0.2`) y llévalo a `master`.
2. Lanza el workflow: pestaña **Actions** → *Publicar release* → **Run workflow** → escribe la versión.
   - (Alternativa desde PC Windows: `git tag v1.0.2 && git push origin v1.0.2`.)
3. GitHub compila y publica. Las apps instaladas verán **🔄 Actualizar** al reabrir.

> Reglas del número de versión: siempre **mayor** que la actual, formato `X.Y.Z`,
> sin saltos raros (1.0.1 → 1.0.2, no 1.0.1 → 1.1).

---

## 5. Primera instalación / nuevos usuarios

Comparte el `Setup.exe` de la última release. Enlace permanente que siempre apunta a la
más reciente:

```
https://github.com/maister1122/WpfAppVba/releases/latest/download/SistemaGestion-win-Setup.exe
```

El usuario lo instala una vez; de ahí en adelante se actualiza solo dentro de la app.

---

## 6. Notas y resolución de problemas

- **El botón 🔄 no aparece:** casi siempre es que el **repo no es público**, o que no hay
  una versión más nueva que la instalada. En desarrollo (corriendo desde VS/`bin`) nunca
  aparece (es a propósito: `_mgr.IsInstalled` es false).
- **Updates delta:** Velopack descarga solo las diferencias entre versiones, no el `.exe`
  completo. Por eso son rápidas aunque el ejecutable pese mucho.
- **Si cambias de repositorio:** actualiza la URL en `ActualizadorApp.cs` (`RepoUrl`),
  en `release.yml` (`--repoUrl`) y en `publicar.ps1`.
- **Corte de internet a media descarga:** la descarga se cancela limpiamente (se captura el
  error y reaparece el botón); la versión instalada sigue intacta. El usuario reintenta con un clic.
- **Firma de código (opcional):** sin firma, Windows muestra SmartScreen ("Más información"
  → "Ejecutar de todas formas"). Se elimina con un certificado de firma (`vpk pack --signParams ...`).
- Detalles del flujo de publicación: ver `PUBLICAR-ACTUALIZACIONES.md`.
