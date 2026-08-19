using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion;
using VisorEmpresa.Data;

namespace VisorEmpresa
{
    public partial class RegionesDetalle : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private readonly string _idEditar;
        private readonly bool   _modoEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        private bool _iniciado   = false;
        private string _tituloTab = "";
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle en SistemaGestion).
        private bool _guardando  = false;

        public event Action? Cerrando;

        /// <summary>ID de la región recién creada (solo modo "nuevo").</summary>
        public string? ItemCreadoId { get; private set; }

        public RegionesDetalle(string idEditar = "")
        {
            InitializeComponent();
            FuncionesComunes.BloquearPegadoNoNumerico(Box_Codigo);
            _idEditar   = idEditar;
            _modoEditar = !string.IsNullOrEmpty(idEditar);
            _tituloTab  = string.IsNullOrEmpty(idEditar) ? "nueva-region" : $"region-{idEditar}";
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        public void IntentarCerrar()
        {
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar Cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No)          Cerrando?.Invoke();
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            if (_modoEditar)
            {
                LblTitulo.Text       = "Editar Región";
                Box_Codigo.IsEnabled = false;
                Box_Codigo.Background = (System.Windows.Media.Brush)FindResource("ThemeBgReadOnly");
                Box_Codigo.Foreground = (System.Windows.Media.Brush)FindResource("ThemeTextoMuted");
                CargarParaEditar();
            }
            else
            {
                LblTitulo.Text       = "Nueva Región";
                Box_Codigo.IsEnabled = true;
                CargarParaNuevo();
            }

            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            string id = _idEditar;
            Box_Codigo.Text      = Sql.RegionesObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
            Box_Descripcion.Text = Sql.RegionesObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
        }

        private void CargarParaNuevo()
        {
            Box_Codigo.Text = Sql.RegionesObj.SiguienteCodigoInt().ToString();
        }

        // ─── Detectar cambios en cualquier campo ──────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Código: solo dígitos (columna codigo es int en la base) ─────────
        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // ─── Guardar ─────────────────────────────────────────────────────────
        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;
                return _modoEditar ? GuardarEditar() : GuardarNuevo();
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
            try
            {
                Sql.RegionesObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.RegionesObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.RegionesObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);

                Sql.RegionesObj.OrdenarData(("codigo", false));
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool GuardarNuevo()
        {
            string codigo = Box_Codigo.Text.Trim();
            if (string.IsNullOrEmpty(codigo))
            {
                MessageBox.Show("Debe asignar un código a la región.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                if (Sql.RegionesObj.CodigoExiste(codigo))
                {
                    MessageBox.Show("El código ya existe", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Box_Codigo.Text = Sql.RegionesObj.SiguienteCodigoInt().ToString();
                    return false;
                }

                string id = Guid.NewGuid().ToString();
                Sql.RegionesObj.Nuevo(id);
                Sql.RegionesObj.EstablecerItem("codigo",      id, codigo);
                Sql.RegionesObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.RegionesObj.EstablecerItem("emision",     id, DateTime.Now);
                Sql.RegionesObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.RegionesObj.EstablecerItem("usuario",     id, AppState.UsuarioActivo);
                Sql.RegionesObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);
                Sql.RegionesObj.EstablecerItem("empresa",     id, AppState.EmpresaActiva);

                Sql.RegionesObj.OrdenarData(("codigo", false));
                ItemCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (Guardar()) { _hayCambios = false; Cerrando?.Invoke(); }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        { _hayCambios = false; Cerrando?.Invoke(); }
    }
}
