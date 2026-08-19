using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion;
using VisorEmpresa.Data;

namespace VisorEmpresa
{
    public partial class SucursalesDetalle : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private readonly SucursalesGeneral? _padre;
        private readonly string _idEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        private bool _iniciado   = false;
        private string _tituloTab = "";
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle en SistemaGestion).
        private bool _guardando  = false;

        public event Action? Cerrando;

        /// <summary>ID de la sucursal recién creada (solo modo "nuevo").</summary>
        public string? ItemCreadoId { get; private set; }

        public SucursalesDetalle(SucursalesGeneral? padre = null, string idEditar = "")
        {
            InitializeComponent();
            FuncionesComunes.BloquearPegadoNoNumerico(Box_Codigo);
            _padre     = padre;
            _idEditar  = idEditar;
            _tituloTab = string.IsNullOrEmpty(idEditar) ? "nueva-sucursal" : $"sucursal-{idEditar}";
            Loaded    += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            if (AppState.EventoFormularioI == "modificar")
            {
                LblTitulo.Text       = "Editar Sucursal";
                Box_Codigo.IsEnabled = false;
                CargarParaEditar();
            }
            else
            {
                LblTitulo.Text       = "Nueva Sucursal";
                Box_Codigo.IsEnabled = true;
                CargarParaNuevo();
            }

            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            string id = _idEditar;
            Box_Codigo.Text      = Sql.SucursalesObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
            Box_Nit.Text         = Sql.SucursalesObj.ObtenerItem("nit",         id)?.ToString() ?? "";
            Box_Descripcion.Text = Sql.SucursalesObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
            Box_Signo.Text       = Sql.SucursalesObj.ObtenerItem("signo",       id)?.ToString() ?? "";
            SeleccionarTipo(Sql.SucursalesObj.ObtenerItem("tipo", id)?.ToString() ?? "");
            Box_Telefono.Text    = Sql.SucursalesObj.ObtenerItem("telefono",    id)?.ToString() ?? "";
            Box_Direccion.Text   = Sql.SucursalesObj.ObtenerItem("direccion",   id)?.ToString() ?? "";
            Box_Observacion.Text = Sql.SucursalesObj.ObtenerItem("observacion", id)?.ToString() ?? "";

            string regionId = Sql.SucursalesObj.ObtenerItem("region", id)?.ToString() ?? "";
            Box_Referido_Codigo.Text = Sql.RegionesObj.ObtenerItem("codigo", regionId)?.ToString() ?? "";
            Box_Referido_Descripcion.Text = Sql.RegionesObj.ObtenerItem("descripcion", regionId)?.ToString() ?? "";

            var fechaObj = Sql.SucursalesObj.ObtenerItem("fecha", id);
            if (fechaObj != null && DateTime.TryParse(fechaObj.ToString(), out DateTime fecha))
            {
                Box_Fecha.SelectedDate = fecha.Date;
                Box_Hora.Text          = fecha.ToString("HH:mm:ss");
            }
            else
            {
                Box_Fecha.SelectedDate = DateTime.Today;
                Box_Hora.Text          = "00:00:00";
            }
        }

        private void CargarParaNuevo()
        {
            Box_Codigo.Text        = Sql.SucursalesObj.SiguienteCodigoInt().ToString();
            SeleccionarTipo("sucursal"); // valor por defecto
            var ahora = DateTime.Now;
            Box_Fecha.SelectedDate = ahora.Date;
            Box_Hora.Text          = ahora.ToString("HH:mm:ss");
        }

        // ─── Selecciona el ítem del combo TIPO según el valor guardado ─────────
        private void SeleccionarTipo(string tipo)
        {
            tipo = tipo.Trim().ToLower();
            if (tipo == "") tipo = "sucursal"; // por defecto si la fila no tiene tipo aún
            foreach (var obj in Cmb_Tipo.Items)
                if (obj is ComboBoxItem cbi &&
                    string.Equals(cbi.Content?.ToString(), tipo, StringComparison.OrdinalIgnoreCase))
                {
                    Cmb_Tipo.SelectedItem = cbi;
                    return;
                }
            Cmb_Tipo.SelectedIndex = 1; // "sucursal"
        }

        // ─── Tipo seleccionado en el combo (siempre "central" o "sucursal") ────
        private string TipoSeleccionado()
            => (Cmb_Tipo.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim().ToLower() ?? "sucursal";

        // ─── Resolver el id (UUID) de la región a partir del código digitado ──
        private string ResolverRegionId()
        {
            string cod = Box_Referido_Codigo.Text.Trim();
            return cod == "" ? "" : Sql.RegionesObj.BuscarIdentificador("codigo", cod);
        }

        // ─── Combina la fecha (DatePicker) con la hora (TextBox) ───────────────
        private DateTime ObtenerFechaHora()
        {
            DateTime fecha = Box_Fecha.SelectedDate ?? DateTime.Today;
            if (TimeSpan.TryParse(Box_Hora.Text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var ts))
                return fecha.Date + ts;
            return fecha.Date;
        }

        // ─── Actualizar descripción de la región referida ─────────────────────
        private void Box_Referido_Codigo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cargando) return;
            _hayCambios = true;
            string regionId = ResolverRegionId();
            Box_Referido_Descripcion.Text = regionId == ""
                ? ""
                : Sql.RegionesObj.ObtenerItem("descripcion", regionId)?.ToString() ?? "";
        }

        // ─── Ver regiones (modo selector) ─────────────────────────────────────
        private void BtnVerRegiones_Click(object sender, RoutedEventArgs e)
        {
            RegionesGeneral.OpenAsTab(Window.GetWindow(this)!,
                id => { Box_Referido_Codigo.Text = id; },
                _tituloTab, this);
        }

        // ─── Detectar cambios en cualquier campo ──────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Código: solo dígitos (columna codigo es int en la base) ─────────
        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        private void Box_Fecha_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private void Cmb_Tipo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private bool SinPestañasRelacionadas()
        {
            var c = Window.GetWindow(this) as ConsolaMovimientos;
            return c == null || c.ConfirmarCierrePestañasRelacionadas(_tituloTab);
        }

        // ─── Guardar ──────────────────────────────────────────────────────────
        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!SinPestañasRelacionadas()) return false;
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;

                return AppState.EventoFormularioI == "modificar"
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
                Sql.SucursalesObj.EstablecerItem("nit",         id, Box_Nit.Text);
                Sql.SucursalesObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.SucursalesObj.EstablecerItem("signo",       id, Box_Signo.Text.Trim().ToUpper());
                Sql.SucursalesObj.EstablecerItem("tipo",        id, TipoSeleccionado());
                Sql.SucursalesObj.EstablecerItem("telefono",    id, Box_Telefono.Text);
                Sql.SucursalesObj.EstablecerItem("region",      id, ResolverRegionId());
                Sql.SucursalesObj.EstablecerItem("direccion",   id, Box_Direccion.Text);
                Sql.SucursalesObj.EstablecerItem("observacion", id, Box_Observacion.Text);
                Sql.SucursalesObj.EstablecerItem("fecha",       id, ObtenerFechaHora());
                Sql.SucursalesObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.SucursalesObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);

                Sql.SucursalesObj.OrdenarData(("codigo", false));
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
                MessageBox.Show("Debe asignar un código a la sucursal.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                if (Sql.SucursalesObj.CodigoExiste(codigo))
                {
                    MessageBox.Show("El código ya existe", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Box_Codigo.Text = Sql.SucursalesObj.SiguienteCodigoInt().ToString();
                    return false;
                }

                string id = Guid.NewGuid().ToString();
                Sql.SucursalesObj.Nuevo(id);
                Sql.SucursalesObj.EstablecerItem("codigo",      id, codigo);
                Sql.SucursalesObj.EstablecerItem("nit",         id, Box_Nit.Text);
                Sql.SucursalesObj.EstablecerItem("descripcion", id, Box_Descripcion.Text);
                Sql.SucursalesObj.EstablecerItem("signo",       id, Box_Signo.Text.Trim().ToUpper());
                Sql.SucursalesObj.EstablecerItem("tipo",        id, TipoSeleccionado());
                Sql.SucursalesObj.EstablecerItem("telefono",    id, Box_Telefono.Text);
                Sql.SucursalesObj.EstablecerItem("region",      id, ResolverRegionId());
                Sql.SucursalesObj.EstablecerItem("direccion",   id, Box_Direccion.Text);
                Sql.SucursalesObj.EstablecerItem("observacion", id, Box_Observacion.Text);
                Sql.SucursalesObj.EstablecerItem("fecha",       id, ObtenerFechaHora());
                Sql.SucursalesObj.EstablecerItem("emision",     id, DateTime.Now);
                Sql.SucursalesObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.SucursalesObj.EstablecerItem("usuario",     id, AppState.UsuarioActivo);
                Sql.SucursalesObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);
                Sql.SucursalesObj.EstablecerItem("empresa",     id, AppState.EmpresaActiva);

                Sql.SucursalesObj.OrdenarData(("codigo", false));
                ItemCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Validación de entrada ────────────────────────────────────────────
        private void Box_Nit_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        private void Box_Referido_Codigo_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        private void Box_Telefono_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);


        // ─── Botones ──────────────────────────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        { if (Guardar()) { _hayCambios = false; Cerrando?.Invoke(); } }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (!SinPestañasRelacionadas()) return;
            _hayCambios = false; Cerrando?.Invoke();
        }

        // ─── Al cerrar: preguntar si hay cambios ──────────────────────────────
        public void IntentarCerrar()
        {
            if (!SinPestañasRelacionadas()) return;
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar Cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No)          Cerrando?.Invoke();
        }
    }
}
