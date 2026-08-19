using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion;
using VisorEmpresa.Data;

namespace VisorEmpresa
{
    public partial class SucursalesGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private readonly bool _modoSelector;
        private bool _iniciado = false;

        public event Action? Cerrando;

        /// <summary>
        /// En modo selector (llamado desde TraspasosDetalle), el doble clic
        /// establece SucursalSeleccionada y cierra la pestaña.
        /// </summary>
        public static string? SucursalSeleccionada = null;

        public SucursalesGeneral() : this(false) { }

        public SucursalesGeneral(bool modoSelector = false)
        {
            InitializeComponent();
            _modoSelector = modoSelector;
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; ConfigurarModo(); CargarSucursales(); };
        }

        public void IntentarCerrar() => Cerrando?.Invoke();

        private void ConfigurarModo()
        {
            if (_modoSelector)
            {
                BtnNuevo.Visibility       = Visibility.Collapsed;
                BtnEditar.Visibility      = Visibility.Collapsed;
                BtnEliminar.Visibility    = Visibility.Collapsed;
                BtnSeleccionar.Visibility = Visibility.Visible;
            }
            else if (!AppState.EsAdmin)
            {
                BtnNuevo.Visibility    = Visibility.Collapsed;
                BtnEditar.Visibility   = Visibility.Collapsed;
                BtnEliminar.Visibility = Visibility.Collapsed;
            }
        }

        public static void OpenAsDialog(Window owner, bool modoSelector = false, string contexto = "", Action? onCerrado = null, UIElement? llamador = null)
        {
            var consola = owner as ConsolaMovimientos;
            if (consola == null) return;
            var ctrl = new SucursalesGeneral(modoSelector);
            ctrl.Cerrando += () => { consola.CerrarPestaña(ctrl); onCerrado?.Invoke(); consola.SeleccionarPestaña(llamador); };
            string titulo = !modoSelector ? "Sucursales"
                : string.IsNullOrEmpty(contexto) ? "Seleccionar Sucursal"
                : $"Seleccionar Sucursal ({contexto})";
            if (modoSelector)
            {
                // Una sola pestaña selector por llamador: clave única según el contexto.
                string clave = string.IsNullOrEmpty(contexto)
                    ? "seleccionar-sucursal"
                    : $"seleccionar-sucursal|{contexto}";
                consola.CerrarPestañaPorClave(clave);
                consola.AbrirPestaña(titulo, ctrl, clave);
            }
            else
            {
                consola.AbrirPestaña(titulo, ctrl);
            }
        }

        // ─── Verificación de administrador ─────────────────────────────────────
        private bool EsAdmin()
            => Sql.UsuariosObj.ObtenerItem("tipo", AppState.UsuarioActivo.ToString())?.ToString() == "admin";

        // ─── Carga la lista completa ───────────────────────────────────────────
        public void CargarSucursales()
        {
            string busqueda = TxtBuscar.Text.Trim().ToLower();
            var filas = new List<SucursalFila>();
            int linea = 1;

            int uf = Sql.SucursalesObj.ContarFilas;
            for (int ciclo = 1; ciclo <= uf; ciclo++)
            {
                var idObj = Sql.SucursalesObj.Mover(ciclo);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string desc   = Sql.SucursalesObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                string codigo = Sql.SucursalesObj.ObtenerItem("codigo",      id)?.ToString() ?? "";

                if (busqueda == "" ||
                    desc.ToLower().Contains(busqueda) ||
                    codigo.ToLower().Contains(busqueda))
                {
                    filas.Add(new SucursalFila
                    {
                        Linea       = linea++,
                        Id          = id,
                        Codigo      = codigo,
                        Descripcion = desc,
                        Tipo        = Sql.SucursalesObj.ObtenerItem("tipo", id)?.ToString() ?? "",
                        Fecha       = ObtenerFecha(id),
                        FechaStr    = FormatearFecha(id)
                    });
                }
            }

            Grid1.ItemsSource = filas;
        }

        // ─── Helpers de actualización incremental ──────────────────────────────
        private List<SucursalFila> FilasGrid =>
            Grid1.ItemsSource as List<SucursalFila> ?? new List<SucursalFila>();

        private SucursalFila ConstruirFilaSucursal(string id, int linea)
        {
            return new SucursalFila
            {
                Linea       = linea,
                Id          = id,
                Codigo      = Sql.SucursalesObj.ObtenerItem("codigo",      id)?.ToString() ?? "",
                Descripcion = Sql.SucursalesObj.ObtenerItem("descripcion", id)?.ToString() ?? "",
                Tipo        = Sql.SucursalesObj.ObtenerItem("tipo",        id)?.ToString() ?? "",
                Fecha       = ObtenerFecha(id),
                FechaStr    = FormatearFecha(id)
            };
        }

        // ─── Fecha (sin formatear) de una sucursal ─────────────────────────────
        private DateTime ObtenerFecha(string id)
        {
            var fechaObj = Sql.SucursalesObj.ObtenerItem("fecha", id);
            return fechaObj != null && DateTime.TryParse(fechaObj.ToString(), out DateTime fecha) ? fecha : default;
        }

        // ─── Formatea la fecha completa (fecha + hora) de una sucursal ─────────
        private string FormatearFecha(string id)
        {
            DateTime fecha = ObtenerFecha(id);
            return fecha != default ? $"{fecha:d} {fecha:HH:mm:ss}" : "";
        }

        private void Renumerar()
        {
            int n = 1;
            foreach (var f in FilasGrid) f.Linea = n++;
            Grid1.Items.Refresh();
        }

        // ─── Búsqueda ─────────────────────────────────────────────────────────
        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
            => CargarSucursales();

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
            => CargarSucursales();

        // ─── Doble clic ────────────────────────────────────────────────────────
        private void Grid1_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_modoSelector) Seleccionar();
            else               AbrirEditar();
        }

        // ─── Tecla Enter ───────────────────────────────────────────────────────
        private void Grid1_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (_modoSelector) Seleccionar();
            else               AbrirEditar();
        }

        // ─── Modo selector ─────────────────────────────────────────────────────
        private void Seleccionar()
        {
            if (Grid1.SelectedItem is not SucursalFila fila) return;
            SucursalSeleccionada = fila.Codigo;
            Cerrando?.Invoke();
        }

        private void BtnSeleccionar_Click(object sender, RoutedEventArgs e)
            => Seleccionar();

        // ─── Botones ───────────────────────────────────────────────────────────

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            if (!EsAdmin())
            {
                MessageBox.Show("Se requieren permisos de administrador.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppState.EventoFormularioI = "nuevo";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            var detalle = new SucursalesDetalle(this);
            detalle.Cerrando += () =>
            {
                consola.CerrarPestaña(detalle);
                if (detalle.ItemCreadoId == null) return;
                var nueva = ConstruirFilaSucursal(detalle.ItemCreadoId, 0);
                FilasGrid.Add(nueva);
                Renumerar();
                Grid1.SelectedItem = nueva; Grid1.ScrollIntoView(nueva);
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña("Nueva Sucursal", detalle, "nueva-sucursal");
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
            => AbrirEditar();

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            // Verificación de conexión en 2 capas antes de persistir el borrado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            if (!EsAdmin())
            {
                MessageBox.Show("Se requieren permisos de administrador.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Grid1.SelectedItem is not SucursalFila fila) return;

            var res = MessageBox.Show("¿Eliminar esta sucursal?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                Sql.SucursalesObj.EstablecerItem("edicion",  fila.Id, DateTime.Now);
                Sql.SucursalesObj.EstablecerItem("usuarioE", fila.Id, AppState.UsuarioActivo);
                Sql.SucursalesObj.Ocultar(fila.Id);
                Sql.SucursalesObj.OrdenarData(("id", false));

                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0) lista.RemoveAt(idx);
                Renumerar();

                if (lista.Count > 0)
                {
                    var sel = lista[System.Math.Min(idx, lista.Count - 1)];
                    Grid1.SelectedItem = sel; Grid1.ScrollIntoView(sel);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            }
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.SucursalesObj.Actualizar();
            CargarSucursales();
        }

        private void AbrirEditar()
        {
            if (!EsAdmin())
            {
                MessageBox.Show("Se requieren permisos de administrador.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Grid1.SelectedItem is not SucursalFila fila) return;
            string idSel = fila.Id;
            int    linea = fila.Linea;
            AppState.EventoFormularioI = "modificar";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            var detalle = new SucursalesDetalle(this, fila.Id);
            detalle.Cerrando += () =>
            {
                consola.CerrarPestaña(detalle);
                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0)
                {
                    var actualizada = ConstruirFilaSucursal(idSel, linea);
                    lista[idx] = actualizada;
                    Renumerar();
                    Grid1.SelectedItem = actualizada; Grid1.ScrollIntoView(actualizada);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña($"Sucursal {fila.Codigo}", detalle, $"sucursal-{idSel}");
        }
    }

    // ─── Modelo de fila para el DataGrid ──────────────────────────────────────
    public class SucursalFila
    {
        public int    Linea       { get; set; }
        public string Id          { get; set; } = "";
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Tipo        { get; set; } = "";
        public DateTime Fecha     { get; set; }
        public string FechaStr    { get; set; } = "";
    }
}
