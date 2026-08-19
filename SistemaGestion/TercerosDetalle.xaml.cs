using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class TercerosDetalle : UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private readonly string _idEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle).
        private bool _guardando  = false;

        public event Action? Cerrando;
        public string? ItemCreadoId { get; private set; }

        private bool _iniciado = false;

        public TercerosDetalle(string idEditar = "")
        {
            InitializeComponent();
            FuncionesComunes.BloquearPegadoNoNumerico(Box_Codigo);
            _idEditar = idEditar;
            Loaded  += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial (equivalente a cargarUserform) ─────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            if (AppState.EventoFormularioL == "modificar")
            {
                LblTitulo.Text        = "Editar Tercero";
                Box_Codigo.IsEnabled  = false;
                CargarParaEditar();
            }
            else
            {
                LblTitulo.Text       = "Nuevo Tercero";
                Box_Codigo.IsEnabled = true;
                CargarParaNuevo();
            }

            _cargando    = false;
            _hayCambios  = false;
        }

        private void CargarParaEditar()
        {
            string id = _idEditar;
            Box_Codigo.Text      = Sql.TercerosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
            Box_Nit.Text         = Sql.TercerosObj.ObtenerItem("nit",         id)?.ToString() ?? "";
            Box_Descripcion.Text = Sql.TercerosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
            Box_Contacto.Text    = Sql.TercerosObj.ObtenerItem("contacto",    id)?.ToString() ?? "";
            Box_Telefono.Text    = Sql.TercerosObj.ObtenerItem("telefono",    id)?.ToString() ?? "";
            Box_Direccion.Text   = Sql.TercerosObj.ObtenerItem("direccion",   id)?.ToString() ?? "";
            Box_Contacto2.Text   = Sql.TercerosObj.ObtenerItem("contacto2",   id)?.ToString() ?? "";
            Box_Telefono2.Text   = Sql.TercerosObj.ObtenerItem("telefono2",   id)?.ToString() ?? "";
            Box_Observacion.Text = Sql.TercerosObj.ObtenerItem("observacion", id)?.ToString() ?? "";
        }

        private void CargarParaNuevo()
        {
            Box_Codigo.Text = Sql.TercerosObj.SiguienteCodigoInt().ToString();
        }

        // ─── Detectar cambios en cualquier campo ──────────────────────────────
        private void Campo_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Código: solo dígitos (columna codigo es int en la base) ─────────
        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // ─── Guardar (equivalente a guardarCambios) ───────────────────────────
        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;

                return AppState.EventoFormularioL == "modificar"
                    ? GuardarEditar()
                    : GuardarNuevo();
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
                Sql.TercerosObj.EstablecerItem("nit",         id, Box_Nit.Text);
                Sql.TercerosObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.TercerosObj.EstablecerItem("contacto",    id, Box_Contacto.Text);
                Sql.TercerosObj.EstablecerItem("telefono",    id, Box_Telefono.Text);
                Sql.TercerosObj.EstablecerItem("direccion",   id, Box_Direccion.Text);
                Sql.TercerosObj.EstablecerItem("contacto2",   id, Box_Contacto2.Text);
                Sql.TercerosObj.EstablecerItem("telefono2",   id, Box_Telefono2.Text);
                Sql.TercerosObj.EstablecerItem("observacion", id, Box_Observacion.Text);
                Sql.TercerosObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.TercerosObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);

                Sql.TercerosObj.OrdenarData(("codigo", false));
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
                MessageBox.Show("Debe asignar un código al tercero.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                if (Sql.TercerosObj.CodigoExiste(codigo))
                {
                    MessageBox.Show("El código ya existe", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Box_Codigo.Text = Sql.TercerosObj.SiguienteCodigoInt().ToString();
                    return false;
                }

                string id = Guid.NewGuid().ToString();
                Sql.TercerosObj.Nuevo(id);
                Sql.TercerosObj.EstablecerItem("codigo",      id, codigo);
                Sql.TercerosObj.EstablecerItem("nit",         id, Box_Nit.Text);
                Sql.TercerosObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.TercerosObj.EstablecerItem("contacto",    id, Box_Contacto.Text);
                Sql.TercerosObj.EstablecerItem("telefono",    id, Box_Telefono.Text);
                Sql.TercerosObj.EstablecerItem("direccion",   id, Box_Direccion.Text);
                Sql.TercerosObj.EstablecerItem("contacto2",   id, Box_Contacto2.Text);
                Sql.TercerosObj.EstablecerItem("telefono2",   id, Box_Telefono2.Text);
                Sql.TercerosObj.EstablecerItem("observacion", id, Box_Observacion.Text);
                Sql.TercerosObj.EstablecerItem("emision",     id, DateTime.Now);
                Sql.TercerosObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.TercerosObj.EstablecerItem("usuario",     id, AppState.UsuarioActivo);
                Sql.TercerosObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);
                Sql.TercerosObj.EstablecerItem("empresa",     id, AppState.EmpresaActiva);

                Sql.TercerosObj.OrdenarData(("codigo", false));
                ItemCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Validación de entrada (equivalente a KeyPress en VBA) ───────────
        private void Box_Nit_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        private void Box_Telefono_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);


        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (Guardar()) { _hayCambios = false; Cerrando?.Invoke(); }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        { _hayCambios = false; Cerrando?.Invoke(); }

        public void IntentarCerrar()
        {
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar Cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No)          Cerrando?.Invoke();
        }
    }
}
