using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class FamiliasDetalle : UserControl
    {
        public event Action? Cerrando;

        private static SqlData Sql => SqlData.Instance;

        private readonly FamiliasGeneral? _padre;
        private readonly string _idEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle).
        private bool _guardando  = false;

        private bool _iniciado = false;
        private readonly string _tituloTab;

        /// <summary>ID de la familia recién creada (solo modo "nuevo").</summary>
        public string? ItemCreadoId { get; private set; }

        public FamiliasDetalle(FamiliasGeneral? padre = null, string idEditar = "", string tituloTab = "")
        {
            InitializeComponent();
            FuncionesComunes.BloquearPegadoNoNumerico(Box_Codigo);
            _padre     = padre;
            _idEditar  = idEditar;
            _tituloTab = tituloTab;
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            if (AppState.EventoFormularioF == "modificar")
            {
                LblTitulo.Text       = "Editar Familia";
                Box_Codigo.IsEnabled = false;
                CargarParaEditar();
            }
            else
            {
                LblTitulo.Text       = "Nueva Familia";
                Box_Codigo.IsEnabled = true;
                CargarParaNuevo();
            }

            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            string id = _idEditar;
            Box_Codigo.Text = Sql.FamiliasObj.ObtenerItem("codigo", id)?.ToString() ?? "";
            Box_Descripcion.Text = Sql.FamiliasObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
            string productoId = Sql.FamiliasObj.ObtenerItem("producto", id)?.ToString() ?? "";
            Box_Referido_Codigo.Text = Sql.ProductosObj.ObtenerItem("codigo", productoId)?.ToString() ?? "";
            Box_Referido_Descripcion.Text = Sql.ProductosObj.ObtenerItem("descripcion", productoId)?.ToString() ?? "";
            Box_Observacion.Text = Sql.FamiliasObj.ObtenerItem("observacion", id)?.ToString() ?? "";
        }

        private void CargarParaNuevo()
        {
            Box_Codigo.Text = Sql.FamiliasObj.SiguienteCodigoInt().ToString();
        }

        // ─── Resolver el id (UUID) del producto a partir del código digitado ──
        private string ResolverProductoId()
        {
            string cod = Box_Referido_Codigo.Text.Trim();
            return cod == "" ? "" : Sql.ProductosObj.BuscarIdentificador("codigo", cod);
        }

        // ─── Actualizar descripción del producto referido ─────────────────────
        private void Box_Referido_Codigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cargando) return;
            _hayCambios = true;
            string productoId = ResolverProductoId();
            Box_Referido_Descripcion.Text = productoId == ""
                ? ""
                : Sql.ProductosObj.ObtenerItem("descripcion", productoId)?.ToString() ?? "";
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

                // La familia debe tener asignado un producto EXISTENTE (ResolverProductoId
                // devuelve "" tanto si está vacío como si el código no existe).
                if (string.IsNullOrEmpty(ResolverProductoId()))
                {
                    MessageBox.Show("Debe asignar un producto existente a la familia.", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                return AppState.EventoFormularioF == "modificar"
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
                Sql.FamiliasObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.FamiliasObj.EstablecerItem("producto",    id, ResolverProductoId());
                Sql.FamiliasObj.EstablecerItem("observacion", id, Box_Observacion.Text);
                Sql.FamiliasObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.FamiliasObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);

                Sql.FamiliasObj.OrdenarData(("codigo", false));
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
                MessageBox.Show("Debe asignar un código a la familia.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                if (Sql.FamiliasObj.CodigoExiste(codigo))
                {
                    MessageBox.Show("El código ya existe", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Box_Codigo.Text = Sql.FamiliasObj.SiguienteCodigoInt().ToString();
                    return false;
                }

                string id = Guid.NewGuid().ToString();
                Sql.FamiliasObj.Nuevo(id);
                Sql.FamiliasObj.EstablecerItem("codigo",      id, codigo);
                Sql.FamiliasObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.FamiliasObj.EstablecerItem("producto",    id, ResolverProductoId());
                Sql.FamiliasObj.EstablecerItem("observacion", id, Box_Observacion.Text);
                Sql.FamiliasObj.EstablecerItem("emision",     id, DateTime.Now);
                Sql.FamiliasObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.FamiliasObj.EstablecerItem("usuario",     id, AppState.UsuarioActivo);
                Sql.FamiliasObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);

                Sql.FamiliasObj.OrdenarData(("codigo", false));
                ItemCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Ver productos (modo selector, en pestaña) ────────────────────────
        private void BtnVerProductos_Click(object sender, RoutedEventArgs e)
        {
            ProductosGeneral.OpenAsTab(Window.GetWindow(this)!, id =>
            {
                if (!string.IsNullOrEmpty(id)) Box_Referido_Codigo.Text = id;
            }, contexto: _tituloTab, llamador: this);
        }

        // ─── Validación de entrada ────────────────────────────────────────────
        private void Box_Referido_Codigo_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

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
