using System;
using System.Linq;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace VisorEmpresa
{
    /// <summary>
    /// Encapsula la auto-actualización con Velopack para Visor Empresa. Mismo flujo
    /// manual/opt-in que SistemaGestion.ActualizadorApp:
    ///   1. HayActualizacionAsync()  → consulta el feed al arrancar.
    ///   2. DescargarAsync(progreso) → solo cuando el usuario pulsa "Actualizar".
    ///   3. AplicarYReiniciar()      → solo cuando el usuario pulsa "Reiniciar".
    /// Usa el mismo repo público que la app principal: Velopack detecta el canal
    /// "visor" solo, porque el Setup instalado quedó empaquetado con --channel visor
    /// (ver .github/workflows/release-visor.yml), así que no hace falta indicarlo acá.
    /// </summary>
    public class ActualizadorApp
    {
        private const string RepoUrl = "https://github.com/maister1122/WpfAppVba";

        private readonly UpdateManager _mgr;
        private UpdateInfo? _update;

        public ActualizadorApp()
        {
            _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }

        /// <summary>Versión nueva detectada (null si no hay ninguna pendiente).</summary>
        public string? VersionNueva => _update?.TargetFullRelease.Version.ToString();

        /// <summary>
        /// Tamaño real a descargar, en MB (0 si no hay update). Si hay deltas disponibles
        /// (caso normal), Velopack descarga esos en vez del paquete completo.
        /// </summary>
        public double TamañoDescargaMB
        {
            get
            {
                if (_update == null) return 0;
                long bytes = _update.DeltasToTarget.Length > 0
                    ? _update.DeltasToTarget.Sum(d => d.Size)
                    : _update.TargetFullRelease.Size;
                return bytes / 1024.0 / 1024.0;
            }
        }

        /// <summary>
        /// Consulta el feed. Devuelve true si hay una versión más nueva que la instalada.
        /// En desarrollo (app no instalada vía Velopack) devuelve false sin tocar la red.
        /// </summary>
        public async Task<bool> HayActualizacionAsync()
        {
            if (!_mgr.IsInstalled) return false;   // corriendo desde bin/ o VS: no hay updates
            _update = await _mgr.CheckForUpdatesAsync();
            return _update != null;
        }

        /// <summary>
        /// Descarga la actualización en segundo plano informando el progreso (0–100).
        /// La app sigue usándose mientras descarga.
        /// </summary>
        public async Task DescargarAsync(IProgress<int> progreso)
        {
            if (_update == null) return;
            await _mgr.DownloadUpdatesAsync(_update, p => progreso.Report(p));
        }

        /// <summary>Aplica lo descargado y reinicia la app ya en la nueva versión.</summary>
        public void AplicarYReiniciar()
        {
            if (_update == null) return;
            _mgr.ApplyUpdatesAndRestart(_update.TargetFullRelease);
        }
    }
}
