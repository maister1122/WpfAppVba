using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisorEmpresa.Data;

namespace VisorEmpresa
{
    public partial class ProductosDetalle : UserControl
    {
        public event Action? Cerrando;

        private static SqlData Sql => SqlData.Instance;

        private readonly string _idEditar;
        private readonly bool   _modoEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle en SistemaGestion).
        private bool _guardando  = false;

        private bool _iniciado = false;
        private readonly string _tituloTab;

        /// <summary>ID del producto recién creado (solo modo "nuevo").</summary>
        public string? ItemCreadoId { get; private set; }

        public ProductosDetalle(string idEditar = "", string tituloTab = "")
        {
            InitializeComponent();
            FuncionesComunes.BloquearPegadoNoNumerico(Box_Codigo);
            _idEditar   = idEditar;
            _modoEditar = !string.IsNullOrEmpty(idEditar);
            _tituloTab  = tituloTab;
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            if (_modoEditar)
            {
                LblTitulo.Text       = "Editar Producto";
                Box_Codigo.IsEnabled = false;
                CargarParaEditar();
            }
            else
            {
                LblTitulo.Text       = "Nuevo Producto";
                Box_Codigo.IsEnabled = true;
                CargarParaNuevo();
            }

            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            string id = _idEditar;
            Box_Codigo.Text      = Sql.ProductosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
            Box_Descripcion.Text = Sql.ProductosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
        }

        private void CargarParaNuevo()
        {
            Box_Codigo.Text = Sql.ProductosObj.SiguienteCodigoInt().ToString();
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
                Sql.ProductosObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.ProductosObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.ProductosObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);

                Sql.ProductosObj.OrdenarData(("codigo", false));
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
                MessageBox.Show("Debe asignar un código a la producto.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                if (Sql.ProductosObj.CodigoExiste(codigo))
                {
                    MessageBox.Show("El código ya existe", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Box_Codigo.Text = Sql.ProductosObj.SiguienteCodigoInt().ToString();
                    return false;
                }

                string id = Guid.NewGuid().ToString();
                Sql.ProductosObj.Nuevo(id);
                Sql.ProductosObj.EstablecerItem("codigo",      id, codigo);
                Sql.ProductosObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.ProductosObj.EstablecerItem("emision",     id, DateTime.Now);
                Sql.ProductosObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.ProductosObj.EstablecerItem("usuario",     id, AppState.UsuarioActivo);
                Sql.ProductosObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);
                Sql.ProductosObj.EstablecerItem("empresa",     id, AppState.EmpresaActiva);

                Sql.ProductosObj.OrdenarData(("codigo", false));
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

        // ─── Llamado por el botón X de la pestaña para verificar cambios ──────
        public void IntentarCerrar()
        {
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No) Cerrando?.Invoke();
        }
    }
}
