using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion;
using VisorEmpresa.Data;

namespace VisorEmpresa
{
    public partial class UsuariosDetalle : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private readonly UsuariosGeneral? _padre;
        private readonly string _idEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        private bool _iniciado   = false;
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle en SistemaGestion).
        private bool _guardando  = false;

        public event Action? Cerrando;

        /// <summary>ID del usuario recién creado (solo modo "nuevo").</summary>
        public string? ItemCreadoId { get; private set; }

        private class EmpresaItem
        {
            public string Id          { get; set; } = "";
            public string Descripcion { get; set; } = "";
        }

        private class SucursalItem
        {
            public string Id          { get; set; } = "";
            public string Descripcion { get; set; } = "";
        }

        private DataConsulta? _sucursalesEmpresa;

        public UsuariosDetalle(UsuariosGeneral? padre = null, string idEditar = "")
        {
            InitializeComponent();
            FuncionesComunes.BloquearPegadoNoNumerico(Box_Codigo);
            _padre    = padre;
            _idEditar = idEditar;
            Loaded   += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;
            CargarCombosEmpresa();

            if (AppState.EventoFormularioI == "modificar")
            {
                LblTitulo.Text = "Editar Usuario";
                LblHintContrasena.Text = "Deja este campo en blanco para no cambiarla.";
                CargarParaEditar();
            }
            else
            {
                LblTitulo.Text = "Nuevo Usuario";
                LblHintContrasena.Text = "Obligatoria: sin esto el usuario no va a poder loguear.";
                CargarParaNuevo();
            }

            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarCombosEmpresa()
        {
            CmbEmpresa.Items.Clear();
            int total = Sql.EmpresasObj.ContarFilas;
            for (int i = 1; i <= total; i++)
            {
                var idObj = Sql.EmpresasObj.Mover(i);
                if (idObj == null) continue;
                string id   = idObj.ToString()!;
                string desc = Sql.EmpresasObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                CmbEmpresa.Items.Add(new EmpresaItem { Id = id, Descripcion = desc });
            }
        }

        private void PoblarSucursales(string empresaId)
        {
            CmbSucursal.Items.Clear();
            _sucursalesEmpresa = null;
            if (string.IsNullOrEmpty(empresaId)) return;

            var suc = new DataConsulta();
            suc.Conectar("sucursales",
                $"SELECT * FROM sucursales WHERE estadof = 'normal' AND empresa = '{empresaId}' ORDER BY id ASC");
            _sucursalesEmpresa = suc;

            int total = suc.ContarFilas;
            for (int i = 1; i <= total; i++)
            {
                var idObj = suc.Mover(i);
                if (idObj == null) continue;
                string id   = idObj.ToString()!;
                string desc = suc.ObtenerItem("descripcion", id)?.ToString() ?? "";
                CmbSucursal.Items.Add(new SucursalItem { Id = id, Descripcion = desc });
            }
        }

        private void CargarParaEditar()
        {
            string id = _idEditar;
            Box_Codigo.Text    = Sql.UsuariosObj.ObtenerItem("codigo",    id)?.ToString() ?? "";
            Box_Cuenta.Text    = Sql.UsuariosObj.ObtenerItem("cuenta",    id)?.ToString() ?? "";
            Box_Nombres.Text   = Sql.UsuariosObj.ObtenerItem("nombres",   id)?.ToString() ?? "";
            Box_Apellidos.Text = Sql.UsuariosObj.ObtenerItem("apellidos", id)?.ToString() ?? "";

            SeleccionarComboBoxItem(CmbTipo,
                Sql.UsuariosObj.ObtenerItem("tipo",    id)?.ToString() ?? "user");

            // Empresa → dispara PoblarSucursales
            string empId = Sql.UsuariosObj.ObtenerItem("empresa", id)?.ToString() ?? "";
            var empItem  = CmbEmpresa.Items.OfType<EmpresaItem>().FirstOrDefault(x => x.Id == empId);
            if (empItem != null) CmbEmpresa.SelectedItem = empItem;
            else if (CmbEmpresa.Items.Count > 0) CmbEmpresa.SelectedIndex = 0;

            // Sucursal (CmbSucursal ya fue poblado al seleccionar empresa)
            string sucId = Sql.UsuariosObj.ObtenerItem("sucursal", id)?.ToString() ?? "";
            var sucItem  = CmbSucursal.Items.OfType<SucursalItem>().FirstOrDefault(x => x.Id == sucId);
            if (sucItem != null) CmbSucursal.SelectedItem = sucItem;
            else if (CmbSucursal.Items.Count > 0) CmbSucursal.SelectedIndex = 0;

        }

        private void CargarParaNuevo()
        {
            Box_Codigo.Text = Sql.UsuariosObj.SiguienteCodigoInt().ToString();

            if (CmbTipo.Items.Count > 1) CmbTipo.SelectedIndex = 1; // "user"

            var activeEmp = CmbEmpresa.Items.OfType<EmpresaItem>()
                            .FirstOrDefault(x => x.Id == AppState.EmpresaActiva);
            if (activeEmp != null) CmbEmpresa.SelectedItem = activeEmp;
            else if (CmbEmpresa.Items.Count > 0) CmbEmpresa.SelectedIndex = 0;

            var activeSuc = CmbSucursal.Items.OfType<SucursalItem>()
                            .FirstOrDefault(x => x.Id == AppState.SucursalActiva);
            if (activeSuc != null) CmbSucursal.SelectedItem = activeSuc;
            else if (CmbSucursal.Items.Count > 0) CmbSucursal.SelectedIndex = 0;
        }

        private void SeleccionarComboBoxItem(ComboBox cmb, string valor)
        {
            foreach (ComboBoxItem item in cmb.Items)
            {
                if (item.Content?.ToString() == valor)
                { cmb.SelectedItem = item; return; }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        // ─── Eventos ─────────────────────────────────────────────────────────
        private void CmbEmpresa_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string empId = (CmbEmpresa.SelectedItem as EmpresaItem)?.Id ?? "";
            PoblarSucursales(empId);
            if (CmbSucursal.Items.Count > 0) CmbSucursal.SelectedIndex = 0;
            if (!_cargando) _hayCambios = true;
        }

        private void Campo_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private void Campo_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Mostrar / ocultar contraseña (mismo patrón que CambiarContrasena) ──
        private string ValorContrasena() =>
            TxtContrasenaVisible.Visibility == Visibility.Visible
                ? TxtContrasenaVisible.Text
                : PwdContrasena.Password;

        private void BtnVerContrasena_Click(object sender, RoutedEventArgs e)
        {
            if (PwdContrasena.Visibility == Visibility.Visible)
            {
                TxtContrasenaVisible.Text       = PwdContrasena.Password;
                PwdContrasena.Visibility        = Visibility.Collapsed;
                TxtContrasenaVisible.Visibility = Visibility.Visible;
                BtnVerContrasena.Content        = "Ocultar";
            }
            else
            {
                PwdContrasena.Password          = TxtContrasenaVisible.Text;
                TxtContrasenaVisible.Visibility = Visibility.Collapsed;
                PwdContrasena.Visibility        = Visibility.Visible;
                BtnVerContrasena.Content         = "Ver";
            }
        }

        // ─── Código: solo dígitos (columna codigo es int en la base) ─────────
        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        private void Campo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Guardar ──────────────────────────────────────────────────────────
        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;
                return AppState.EventoFormularioI == "modificar" ? GuardarEditar() : GuardarNuevo();
            }
            finally
            {
                _guardando = false;
                BtnGuardar.IsEnabled = true;
            }
        }

        private bool GuardarEditar()
        {
            string id = _idEditar;
            string cuenta = Box_Cuenta.Text.Trim();
            if (string.IsNullOrEmpty(cuenta))
            {
                MessageBox.Show("La cuenta no puede estar vacía.", "Usuarios",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                Sql.UsuariosObj.EstablecerItem("cuenta",    id, cuenta);
                Sql.UsuariosObj.EstablecerItem("nombres",   id, Box_Nombres.Text.Trim());
                Sql.UsuariosObj.EstablecerItem("apellidos", id, Box_Apellidos.Text.Trim());
                Sql.UsuariosObj.EstablecerItem("tipo",     id, ObtenerComboValor(CmbTipo));
                Sql.UsuariosObj.EstablecerItem("empresa",  id, (CmbEmpresa.SelectedItem  as EmpresaItem)?.Id  ?? "");
                Sql.UsuariosObj.EstablecerItem("sucursal", id, (CmbSucursal.SelectedItem as SucursalItem)?.Id ?? "");
                Sql.UsuariosObj.EstablecerItem("edicion",  id, DateTime.Now);

                // Contraseña: en blanco = no se toca (queda la actual).
                string contrasena = ValorContrasena();
                if (!string.IsNullOrEmpty(contrasena))
                    Sql.UsuariosObj.EstablecerItem("llave", id, PasswordHasher.Hashear(contrasena));

                Sql.UsuariosObj.OrdenarData(("codigo", false));
                Sql.UsuariosObj.ExportarItems();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Usuarios", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool GuardarNuevo()
        {
            string cuenta = Box_Cuenta.Text.Trim();
            if (string.IsNullOrEmpty(cuenta))
            {
                MessageBox.Show("La cuenta no puede estar vacía.", "Usuarios",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string contrasena = ValorContrasena();
            if (string.IsNullOrEmpty(contrasena))
            {
                MessageBox.Show("La contraseña no puede estar vacía: sin ella, este usuario no va a poder loguear.",
                    "Usuarios", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                string id = Guid.NewGuid().ToString();
                Sql.UsuariosObj.Nuevo(id);
                Sql.UsuariosObj.EstablecerItem("codigo",    id, Box_Codigo.Text);
                Sql.UsuariosObj.EstablecerItem("cuenta",    id, cuenta);
                Sql.UsuariosObj.EstablecerItem("nombres",   id, Box_Nombres.Text.Trim());
                Sql.UsuariosObj.EstablecerItem("apellidos", id, Box_Apellidos.Text.Trim());
                Sql.UsuariosObj.EstablecerItem("tipo",     id, ObtenerComboValor(CmbTipo));
                Sql.UsuariosObj.EstablecerItem("empresa",  id, (CmbEmpresa.SelectedItem  as EmpresaItem)?.Id  ?? "");
                Sql.UsuariosObj.EstablecerItem("sucursal", id, (CmbSucursal.SelectedItem as SucursalItem)?.Id ?? "");
                Sql.UsuariosObj.EstablecerItem("llave",    id, PasswordHasher.Hashear(contrasena));
                // No tocar "estadof" acá: Nuevo(id) ya lo dejó en "nuevo", y
                // ExportarItemsInterno() lo pasa a "normal" recién al insertar. Si se
                // llama EstablecerItem("estadof", ...) el propio método lo reescribe y
                // lo termina marcando "editado", con lo que este usuario se manda por
                // UPDATE (0 filas afectadas: el id todavía no existe en SQL Server) en
                // vez de por INSERT — queda en la caché pero nunca llega a la base.
                Sql.UsuariosObj.EstablecerItem("emision",   id, DateTime.Now);
                Sql.UsuariosObj.EstablecerItem("edicion",   id, DateTime.Now);

                Sql.UsuariosObj.OrdenarData(("codigo", false));
                Sql.UsuariosObj.ExportarItems();
                ItemCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Usuarios", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private string ObtenerComboValor(ComboBox cmb)
            => (cmb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        // ─── Botones ──────────────────────────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        { if (Guardar()) { _hayCambios = false; Cerrando?.Invoke(); } }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        { _hayCambios = false; Cerrando?.Invoke(); }

        // ─── Al cerrar: preguntar si hay cambios ──────────────────────────────
        public void IntentarCerrar()
        {
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar Cambios?", "Usuarios",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No)          Cerrando?.Invoke();
        }
    }
}
