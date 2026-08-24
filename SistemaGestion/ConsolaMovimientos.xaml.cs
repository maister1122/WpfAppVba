using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class ConsolaMovimientos : Window
    {
        private Button? _btnActivo;

        // Auto-actualización (Velopack). Flujo manual: aviso → descarga → reiniciar.
        private readonly ActualizadorApp _actualizador = new();

        // Paneles "General": mutables para poder recrearlos tras un cambio de contexto
        // (empresa/sucursal/periodo) y que relean los cachés recién cargados.
        private ArticulosGeneral    _panelArticulos    = new();
        private PedidosGeneral      _panelVentas  = new("venta");
        private PedidosGeneral      _panelCompras = new("compra");
        private TraspasosGeneral    _panelEntradas = new("entrada");
        private TraspasosGeneral    _panelSalidas  = new("salida");
        private CorreccionesGeneral _panelRepuestas   = new("repuesta");
        private CorreccionesGeneral _panelRetirados   = new("retirado");
        private FacturasGeneral     _panelIngresos     = new("ingreso");
        private FacturasGeneral     _panelEgresos      = new("egreso");
        private TercerosGeneral     _panelTerceros     = new();
        private FamiliasGeneral     _panelFamilias     = new();
        private ProductosGeneral    _panelProductos    = new();
        private IndustriasGeneral   _panelIndustrias   = new();
        private CategoriasGeneral   _panelCategorias   = new();
        private InventariosGeneral  _panelInventarios  = new();
        private readonly Configuracion       _panelConfiguracion= new();
        private MovimientosGeneral  _panelMovimientos  = new();
        private DashboardGeneral    _panelDashboard    = new();

        // Cada sección del menú lateral conserva su propio juego de pestañas dinámicas.
        private string _seccionActiva = "articulos";
        private readonly Dictionary<string, List<TabItem>> _pestañasPorSeccion = new()
        {
            ["articulos"]    = new List<TabItem>(),
            ["ventas"]  = new List<TabItem>(),
            ["compras"] = new List<TabItem>(),
            ["entradas"] = new List<TabItem>(),
            ["salidas"]  = new List<TabItem>(),
            ["repuestas"]    = new List<TabItem>(),
            ["retirados"]    = new List<TabItem>(),
            ["ingresos"]     = new List<TabItem>(),
            ["egresos"]      = new List<TabItem>(),
            ["terceros"]     = new List<TabItem>(),
            ["familias"]     = new List<TabItem>(),
            ["productos"]    = new List<TabItem>(),
            ["industrias"]   = new List<TabItem>(),
            ["categorias"]   = new List<TabItem>(),
            ["inventarios"]  = new List<TabItem>(),
            ["configuracion"]= new List<TabItem>(),
            ["movimientos"]  = new List<TabItem>(),
            ["dashboard"]    = new List<TabItem>(),
        };
        private readonly Dictionary<string, TabItem?> _pestañaSeleccionadaPorSeccion = new()
        {
            ["articulos"]    = null,
            ["ventas"]  = null,
            ["compras"] = null,
            ["entradas"] = null,
            ["salidas"]  = null,
            ["repuestas"]    = null,
            ["retirados"]    = null,
            ["ingresos"]     = null,
            ["egresos"]      = null,
            ["terceros"]     = null,
            ["familias"]     = null,
            ["productos"]    = null,
            ["industrias"]   = null,
            ["categorias"]   = null,
            ["inventarios"]  = null,
            ["configuracion"]= null,
            ["movimientos"]  = null,
            ["dashboard"]    = null,
        };

        public ConsolaMovimientos()
        {
            InitializeComponent();
            TabFijoContenido.Content = _panelArticulos;
            MostrarVersion();
            ActualizarInfoUsuario();
            ActualizarIconoTema();
            MarcarActivo(BtnNav_Articulos);

            // Estado de conexión: pintar el estado actual y escuchar cambios.
            ActualizarLabelConexion(ConexionEstado.EnLinea);
            ConexionEstado.Cambio += OnConexionCambio;
            ConexionEstado.Iniciar(Dispatcher);

            // Buscar actualizaciones en segundo plano (no bloquea el arranque).
            // Si hay una nueva versión, aparece el botón "🔄 Actualizar" en la top bar.
            _ = BuscarActualizacionesAsync();
        }

        // ─── Versión de la app (barra de título de la ventana) ────────────────
        private void MostrarVersion()
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            // Title de la ventana → lo muestra Windows en la barra de título del SO.
            Title = v == null
                ? "Sistema de Gestión"
                : $"Sistema de Gestión  v{v.Major}.{v.Minor}.{v.Build}";
        }

        // ─── Auto-actualización (Velopack) ────────────────────────────────────
        private async Task BuscarActualizacionesAsync()
        {
            try
            {
                if (await _actualizador.HayActualizacionAsync())
                {
                    AppState.VersionPendiente   = _actualizador.VersionNueva;
                    BloqueActualizar.Visibility = Visibility.Visible;
                    BtnActualizar.Visibility    = Visibility.Visible;
                    BtnActualizar.ToolTip       = $"Nueva versión disponible: {_actualizador.VersionNueva}";
                }
            }
            catch
            {
                // Sin red o sin feed accesible: silencioso. Se reintenta al próximo arranque.
            }
        }

        // Estado A → B: el usuario pulsa "Actualizar". Descarga en segundo plano con barra.
        private async void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            BtnActualizar.Visibility = Visibility.Collapsed;
            PanelDescarga.Visibility = Visibility.Visible;
            LblDescarga.Text         = "Descargando…";
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
                // Estado B → C: lista. El usuario decide cuándo reiniciar.
                PanelDescarga.Visibility = Visibility.Collapsed;
                BtnReiniciar.Visibility  = Visibility.Visible;
            }
            catch
            {
                // Falló la descarga: volver al estado A para poder reintentar.
                PanelDescarga.Visibility = Visibility.Collapsed;
                BtnActualizar.Visibility = Visibility.Visible;
                MessageBox.Show(
                    "No se pudo descargar la actualización. Revisa tu conexión e inténtalo de nuevo.",
                    "Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Estado C: aplica lo descargado y reinicia la app ya actualizada.
        private void BtnReiniciar_Click(object sender, RoutedEventArgs e)
        {
            _actualizador.AplicarYReiniciar();
        }

        // ─── Estado de conexión (top bar) ─────────────────────────────────────
        private void OnConexionCambio(bool enLinea) => ActualizarLabelConexion(enLinea);

        private void ActualizarLabelConexion(bool enLinea)
        {
            if (enLinea)
            {
                LblConexion.Text = "●  En línea";
                PillConexion.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // verde
            }
            else
            {
                LblConexion.Text = "●  Sin conexión";
                PillConexion.Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)); // rojo
            }
        }

        // ─── Info usuario ─────────────────────────────────────────────────────
        public void ActualizarInfoUsuario()
        {
            var sql = SqlData.Instance;
            string nombres = sql.UsuariosObj.ObtenerItem("nombres", AppState.UsuarioActivo)?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(nombres)) nombres = AppState.UsuarioActivo;
            string sucursalDesc = sql.SucursalesObj.ObtenerItem("descripcion", AppState.SucursalActiva)?.ToString() ?? "";
            string empresaDesc  = sql.EmpresasObj.ObtenerItem("descripcion", AppState.EmpresaActiva)?.ToString() ?? "";

            LblUsuario.Text  = $"Usuario: {nombres}  |  Período: {AppState.PeriodoActivo}";
            LblSucursal.Text = $"Sucursal: {sucursalDesc}";
            LblEmpresa.Text  = $"Empresa: {empresaDesc}";
        }

        // ─── Tema claro / oscuro (antes vivía en Configuración; ahora es un
        //     toggle rápido en la top bar, igual que en VisorEmpresa). 100% local
        //     (theme.txt): usuarios.temaC ya no existe (columna eliminada). ──────
        private void BtnTema_Click(object sender, RoutedEventArgs e)
        {
            string nuevo = ThemeManager.EsOscuroActivo ? ThemeManager.TemaClaro : ThemeManager.TemaOscuro;
            ThemeManager.AplicarTema(nuevo);
            AppState.TemaActivo = nuevo;
            ActualizarIconoTema();
        }

        private void ActualizarIconoTema()
        {
            BtnTema.Content = ThemeManager.EsOscuroActivo ? "☀" : "🌙";
        }

        /// <summary>
        /// Refresca toda la consola tras un cambio de contexto (empresa/sucursal/periodo)
        /// hecho desde Configuración, sin cerrar sesión y manteniendo el enfoque en
        /// Configuración. Recrea los paneles "General" para que relean los cachés
        /// recién cargados (como si recién se hubiera iniciado sesión).
        /// </summary>
        public void RecargarContexto()
        {
            // 1. Cerrar todas las pestañas dinámicas (estado "recién iniciado")
            for (int i = TabContenido.Items.Count - 1; i >= 0; i--)
                if (TabContenido.Items[i] is TabItem t && t != TabFijo)
                    TabContenido.Items.RemoveAt(i);
            foreach (var clave in _pestañasPorSeccion.Keys.ToList())
                _pestañasPorSeccion[clave].Clear();
            foreach (var clave in _pestañaSeleccionadaPorSeccion.Keys.ToList())
                _pestañaSeleccionadaPorSeccion[clave] = null;

            // 2. Recrear los paneles "General" (no Configuración: se mantiene el enfoque)
            _panelArticulos    = new();
            _panelVentas  = new("venta");
            _panelCompras = new("compra");
            _panelEntradas = new("entrada");
            _panelSalidas  = new("salida");
            _panelRepuestas   = new("repuesta");
            _panelRetirados   = new("retirado");
            _panelIngresos     = new("ingreso");
            _panelEgresos      = new("egreso");
            _panelTerceros     = new();
            _panelFamilias     = new();
            _panelProductos    = new();
            _panelIndustrias   = new();
            _panelCategorias   = new();
            _panelInventarios  = new();
            _panelMovimientos  = new();
            _panelDashboard    = new();

            // 3. Mantener Configuración como panel fijo enfocado
            _seccionActiva = "configuracion";
            TabFijoContenido.Content = _panelConfiguracion;
            TabFijoTitulo.Text = "Configuración";
            TabContenido.SelectedItem = TabFijo;
            MarcarActivo(BtnNav_Configuracion);

            // 4. Refrescar la barra superior
            ActualizarInfoUsuario();
        }

        // ─── Navegación por pestañas ──────────────────────────────────────────
        private void MostrarPanel(string nombre)
        {
            if (nombre == _seccionActiva)
            {
                TabContenido.SelectedItem = TabFijo;
                return;
            }

            // 1. Guardar la pestaña activa y las pestañas dinámicas de la sección actual
            _pestañaSeleccionadaPorSeccion[_seccionActiva] = TabContenido.SelectedItem as TabItem;
            var guardadas = _pestañasPorSeccion[_seccionActiva];
            guardadas.Clear();
            for (int i = TabContenido.Items.Count - 1; i >= 0; i--)
            {
                if (TabContenido.Items[i] is TabItem t && t != TabFijo)
                {
                    guardadas.Insert(0, t);
                    TabContenido.Items.RemoveAt(i);
                }
            }

            // 2. Cambiar el contenido y título de la pestaña fija
            switch (nombre)
            {
                case "articulos":    TabFijoContenido.Content = _panelArticulos;    TabFijoTitulo.Text = "Artículos";    break;
                case "ventas":       TabFijoContenido.Content = _panelVentas;       TabFijoTitulo.Text = "Ventas";       break;
                case "compras":      TabFijoContenido.Content = _panelCompras;      TabFijoTitulo.Text = "Compras";      break;
                case "entradas":     TabFijoContenido.Content = _panelEntradas;     TabFijoTitulo.Text = "Entradas";     break;
                case "salidas":      TabFijoContenido.Content = _panelSalidas;      TabFijoTitulo.Text = "Salidas";      break;
                case "repuestas":    TabFijoContenido.Content = _panelRepuestas;    TabFijoTitulo.Text = "Repuestas";    break;
                case "retirados":    TabFijoContenido.Content = _panelRetirados;    TabFijoTitulo.Text = "Retirados";    break;
                case "ingresos":     TabFijoContenido.Content = _panelIngresos;     TabFijoTitulo.Text = "Ingresos";     break;
                case "egresos":      TabFijoContenido.Content = _panelEgresos;      TabFijoTitulo.Text = "Egresos";      break;
                case "terceros":     TabFijoContenido.Content = _panelTerceros;     TabFijoTitulo.Text = "Terceros";     break;
                case "familias":     TabFijoContenido.Content = _panelFamilias;     TabFijoTitulo.Text = "Familias";     break;
                case "productos":    TabFijoContenido.Content = _panelProductos;    TabFijoTitulo.Text = "Productos";    break;
                case "industrias":   TabFijoContenido.Content = _panelIndustrias;   TabFijoTitulo.Text = "Industrias";   break;
                case "categorias":   TabFijoContenido.Content = _panelCategorias;   TabFijoTitulo.Text = "Categorías";   break;
                case "inventarios":  TabFijoContenido.Content = _panelInventarios;  TabFijoTitulo.Text = "Inventarios";  break;
                case "configuracion":TabFijoContenido.Content = _panelConfiguracion;TabFijoTitulo.Text = "Configuración";break;
                case "movimientos":  TabFijoContenido.Content = _panelMovimientos;  TabFijoTitulo.Text = "Movimientos";  break;
                case "dashboard":    TabFijoContenido.Content = _panelDashboard;    TabFijoTitulo.Text = "Dashboard";    break;
            }

            // 3. Restaurar las pestañas propias de la nueva sección
            _seccionActiva = nombre;
            var restaurar = _pestañasPorSeccion[nombre];
            foreach (var t in restaurar)
                TabContenido.Items.Add(t);
            restaurar.Clear();

            // 4. Restaurar la pestaña que estaba activa al salir de esta sección
            var selAnterior = _pestañaSeleccionadaPorSeccion[nombre];
            TabContenido.SelectedItem = (selAnterior != null && TabContenido.Items.Contains(selAnterior))
                ? selAnterior
                : TabFijo;
        }

        public void AbrirPestaña(string titulo, UIElement contenido, string? clave = null)
        {
            foreach (TabItem t in TabContenido.Items)
            {
                if (clave != null && t.Tag as string == clave) { TabContenido.SelectedItem = t; return; }
                if (clave == null && t.Content == contenido)   { TabContenido.SelectedItem = t; return; }
            }

            var lblTitulo = new TextBlock
            {
                Text = titulo,
                VerticalAlignment = VerticalAlignment.Center
            };
            var btnCerrar = new Button
            {
                Content           = "✕",
                Background        = Brushes.Transparent,
                BorderThickness   = new Thickness(0),
                Padding           = new Thickness(6, 0, 0, 0),
                FontSize          = 10,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB8)),
                VerticalAlignment = VerticalAlignment.Center,
                FocusVisualStyle  = null,
                Cursor            = Cursors.Hand
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(lblTitulo);
            header.Children.Add(btnCerrar);

            var tab = new TabItem { Header = header, Content = contenido, Tag = clave };
            btnCerrar.Click += (s, e) =>
            {
                e.Handled = true;
                var intentar = contenido.GetType().GetMethod("IntentarCerrar", Type.EmptyTypes);
                if (intentar != null) intentar.Invoke(contenido, null);
                else CerrarPestaña(contenido);
            };

            TabContenido.Items.Add(tab);
            TabContenido.SelectedItem = tab;
            MostrarPestañaEnBarra(tab);
        }

        // Trae la pestaña al área visible de la barra. Con muchas abiertas, las más
        // antiguas quedan fuera de vista (a la izquierda) y solo se llega a ellas con
        // el scroll horizontal: sin esto, abrir o elegir una pestaña la dejaría
        // seleccionada pero fuera de la vista.
        private void MostrarPestañaEnBarra(TabItem tab)
            => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(tab.BringIntoView));

        public void CerrarPestaña(UIElement contenido)
        {
            TabItem? target = null;
            foreach (TabItem t in TabContenido.Items)
                if (t.Content == contenido) { target = t; break; }
            if (target == null) return;
            int idx = TabContenido.Items.IndexOf(target);
            TabContenido.Items.Remove(target);
            if (TabContenido.Items.Count > 0)
                TabContenido.SelectedIndex = Math.Max(0, idx - 1);
        }

        public void SeleccionarPestaña(UIElement? contenido)
        {
            if (contenido == null) return;
            foreach (TabItem t in TabContenido.Items)
                if (t.Content == contenido) { TabContenido.SelectedItem = t; return; }
        }

        public void CerrarPestañaPorClave(string clave)
        {
            TabItem? target = null;
            foreach (TabItem t in TabContenido.Items)
                if (t.Tag as string == clave) { target = t; break; }
            if (target == null) return;
            int idx = TabContenido.Items.IndexOf(target);
            TabContenido.Items.Remove(target);
            if (TabContenido.Items.Count > 0)
                TabContenido.SelectedIndex = Math.Max(0, idx - 1);
        }

        // ─── Lista completa de pestañas (botón "▾" de la barra) ───────────────
        // La barra es una sola fila con scroll horizontal: si no entran todas, las
        // más antiguas quedan fuera de vista. Este menú las lista TODAS y salta a la
        // elegida trayéndola a la vista, igual que el botón de pestañas de Chrome.
        private void BtnPestanasTodas_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var menu = new ContextMenu
            {
                PlacementTarget = btn,
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Style           = (Style)FindResource("TabListaMenu")
            };

            var estiloItem = (Style)FindResource("TabListaItem");
            foreach (TabItem t in TabContenido.Items)
            {
                bool actual = ReferenceEquals(t, TabContenido.SelectedItem);
                var item = new MenuItem
                {
                    Header = FilaListaPestaña(TituloPestaña(t), actual),
                    Style  = estiloItem
                };
                var destino = t;
                item.Click += (_, _) =>
                {
                    TabContenido.SelectedItem = destino;
                    MostrarPestañaEnBarra(destino);
                };
                menu.Items.Add(item);
            }

            btn.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // La barra de pestañas no tiene scroll vertical, así que la rueda del mouse
        // sobre ella se usa para moverla a lo ancho.
        private void BarraPestanas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer barra) return;
            barra.ScrollToHorizontalOffset(barra.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        // Fila de la lista: un punto azul (el acento de la app) delante de la pestaña
        // que se está viendo, y el título al lado. El hueco del punto se reserva
        // siempre para que todos los títulos queden alineados.
        private static StackPanel FilaListaPestaña(string titulo, bool actual)
        {
            var fila = new StackPanel { Orientation = Orientation.Horizontal };
            fila.Children.Add(new TextBlock
            {
                Text              = actual ? "●" : "",
                Width             = 14,
                FontSize          = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xE3))
            });
            fila.Children.Add(new TextBlock
            {
                Text              = titulo,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight        = actual ? FontWeights.Bold : FontWeights.Normal
            });
            return fila;
        }

        // ─── Verificar y cerrar pestañas vinculadas antes de guardar/cerrar ─────
        // Devuelve true si se puede continuar (no hay pestañas o el usuario aceptó cerrarlas).
        public bool ConfirmarCierrePestañasRelacionadas(string contexto)
        {
            if (string.IsNullOrEmpty(contexto)) return true;
            var relacionadas = new List<TabItem>();
            foreach (TabItem t in TabContenido.Items)
                if (t.Tag is string clave && clave.EndsWith($"|{contexto}"))
                    relacionadas.Add(t);
            if (relacionadas.Count == 0) return true;

            string lista = string.Join("\n• ", relacionadas.Select(t =>
                t.Header is StackPanel sp
                    ? sp.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "pestaña"
                    : t.Tag?.ToString() ?? "pestaña"));

            var res = MessageBox.Show(
                $"Tiene pestaña(s) vinculada(s) aún abierta(s):\n• {lista}\n\nAceptar: cerrarlas y continuar.\nCancelar: volver sin cerrar nada.",
                "Pestañas relacionadas abiertas",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.OK) return false;
            foreach (var t in relacionadas)
                TabContenido.Items.Remove(t);
            return true;
        }

        // ─── Resaltar ítem activo en la barra lateral ─────────────────────────
        private void MarcarActivo(Button btn)
        {
            if (_btnActivo != null)
            {
                _btnActivo.Background  = Brushes.Transparent;
                _btnActivo.BorderBrush = Brushes.Transparent;
                _btnActivo.SetResourceReference(Control.ForegroundProperty, "ThemeTextoSec");
            }
            _btnActivo = btn;
            // SetResourceReference (en vez de asignar un Brush fijo) para que el
            // resaltado del ítem activo siga el tema actual incluso si el usuario
            // cambia de tema sin volver a hacer clic en el ítem.
            btn.SetResourceReference(Control.BackgroundProperty, "ThemeNavActivoBg");
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xE3));
            btn.SetResourceReference(Control.ForegroundProperty, "ThemeNavActivoFg");
        }

        // ─── Navegación lateral ───────────────────────────────────────────────

        private void BtnNav_Articulos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("articulos");
            MarcarActivo(BtnNav_Articulos);
        }

        private void BtnNav_Ventas_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("ventas");
            MarcarActivo(BtnNav_Ventas);
        }

        private void BtnNav_Compras_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("compras");
            MarcarActivo(BtnNav_Compras);
        }

        private void BtnNav_Entradas_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("entradas");
            MarcarActivo(BtnNav_Entradas);
        }

        private void BtnNav_Salidas_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("salidas");
            MarcarActivo(BtnNav_Salidas);
        }

        private void BtnNav_Repuestas_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("repuestas");
            MarcarActivo(BtnNav_Repuestas);
        }

        private void BtnNav_Retirados_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("retirados");
            MarcarActivo(BtnNav_Retirados);
        }

        private void BtnNav_Ingresos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("ingresos");
            MarcarActivo(BtnNav_Ingresos);
        }

        private void BtnNav_Egresos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("egresos");
            MarcarActivo(BtnNav_Egresos);
        }

        private void BtnNav_Terceros_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("terceros");
            MarcarActivo(BtnNav_Terceros);
        }

        private void BtnNav_Familias_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("familias");
            MarcarActivo(BtnNav_Familias);
        }

        private void BtnNav_Productos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("productos");
            MarcarActivo(BtnNav_Productos);
        }

        private void BtnNav_Industrias_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("industrias");
            MarcarActivo(BtnNav_Industrias);
        }

        private void BtnNav_Categorias_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("categorias");
            MarcarActivo(BtnNav_Categorias);
        }

        private void BtnNav_Inventarios_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("inventarios");
            MarcarActivo(BtnNav_Inventarios);
        }

        private void BtnNav_Configuracion_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("configuracion");
            MarcarActivo(BtnNav_Configuracion);
        }

        // ─── Cerrar sesión ────────────────────────────────────────────────────

        private void BtnNav_Movimientos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("movimientos");
            MarcarActivo(BtnNav_Movimientos);
        }

        private void BtnNav_Dashboard_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("dashboard");
            MarcarActivo(BtnNav_Dashboard);
        }

        // ─── Accesos rápidos del top bar ──────────────────────────────────────
        // Ventas y Compras tienen ahora su propia pestaña "nuevo rápido" (clave
        // "nuevo-pedido-{movimiento}-{tipo}"), así que la búsqueda es por movimiento.
        // Antes compartían la clave "nuevo-pedido": había una sola pestaña y el
        // acceso rápido del otro movimiento la reutilizaba dándola vuelta con
        // CambiarTipoMovimiento, lo que dejaba el título de la pestaña desactualizado.
        private PedidosDetalle? BuscarTabPedidoRapido(string movimiento)
        {
            foreach (TabItem t in TabContenido.Items)
                if (t.Tag as string == $"nuevo-pedido-{movimiento}-rapido" && t.Content is PedidosDetalle pd) return pd;
            return null;
        }

        // Entradas y Salidas tienen ahora su propia pestaña "nuevo" (clave
        // "nuevo-traspaso-{tipo}"), así que la búsqueda es por tipo. Antes las dos
        // compartían la clave "nuevo-traspaso": había una sola pestaña y el acceso
        // rápido del otro tipo la reutilizaba dándola vuelta con
        // CambiarTipoMovimiento, lo que dejaba el título de la pestaña desactualizado.
        private TraspasosDetalle? BuscarTabTraspasoRapido(string tipo)
        {
            foreach (TabItem t in TabContenido.Items)
                if (t.Tag as string == $"nuevo-traspaso-{tipo}" && t.Content is TraspasosDetalle td) return td;
            return null;
        }

        private void BtnQuick_Venta_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("ventas");
            MarcarActivo(BtnNav_Ventas);
            // La pestaña encontrada ya es de este movimiento (la clave lo incluye):
            // solo hay que seleccionarla, no darla vuelta.
            var existing = BuscarTabPedidoRapido("venta");
            if (existing != null) { foreach (TabItem t in TabContenido.Items) if (t.Content == existing) { TabContenido.SelectedItem = t; break; } }
            else _panelVentas.AbrirNuevoPedido("rapido", "venta");
        }

        private void BtnQuick_Compra_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("compras");
            MarcarActivo(BtnNav_Compras);
            // La pestaña encontrada ya es de este movimiento (la clave lo incluye):
            // solo hay que seleccionarla, no darla vuelta.
            var existing = BuscarTabPedidoRapido("compra");
            if (existing != null) { foreach (TabItem t in TabContenido.Items) if (t.Content == existing) { TabContenido.SelectedItem = t; break; } }
            else _panelCompras.AbrirNuevoPedido("rapido", "compra");
        }

        private void BtnQuick_Salida_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("salidas");
            MarcarActivo(BtnNav_Salidas);
            // La pestaña encontrada ya es de este tipo (la clave lo incluye): solo
            // hay que seleccionarla, no darla vuelta.
            var existing = BuscarTabTraspasoRapido("salida");
            if (existing != null) { foreach (TabItem t in TabContenido.Items) if (t.Content == existing) { TabContenido.SelectedItem = t; break; } }
            else _panelSalidas.AbrirNuevoTraspaso("salida");
        }

        private void BtnQuick_Entrada_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("entradas");
            MarcarActivo(BtnNav_Entradas);
            // La pestaña encontrada ya es de este tipo (la clave lo incluye): solo
            // hay que seleccionarla, no darla vuelta.
            var existing = BuscarTabTraspasoRapido("entrada");
            if (existing != null) { foreach (TabItem t in TabContenido.Items) if (t.Content == existing) { TabContenido.SelectedItem = t; break; } }
            else _panelEntradas.AbrirNuevoTraspaso("entrada");
        }

        private void MarcarInactivo() { }

        // Marcado cuando el cierre ya fue confirmado (p. ej. desde Cerrar sesión) para
        // que el evento Closing no vuelva a preguntar.
        private bool _cierreConfirmado = false;

        // Devuelve los títulos de las pestañas (nuevo/editar) con cambios sin guardar,
        // tanto en la sección actual como en las demás secciones.
        private List<string> PestañasConCambios()
        {
            var res = new List<string>();
            foreach (TabItem t in TabContenido.Items)
                if (t != TabFijo && TieneCambiosSinGuardar(t.Content)) res.Add(TituloPestaña(t));
            foreach (var lista in _pestañasPorSeccion.Values)
                foreach (var t in lista)
                    if (TieneCambiosSinGuardar(t.Content)) res.Add(TituloPestaña(t));
            return res;
        }

        // Extrae el texto del título de una pestaña: en las dinámicas el Header es un
        // StackPanel con un TextBlock de título seguido del botón de cierre; en la
        // pestaña fija es un TextBlock suelto (TabFijoTitulo).
        private static string TituloPestaña(TabItem t)
        {
            if (t.Header is System.Windows.Controls.StackPanel sp)
                foreach (var hijo in sp.Children)
                    if (hijo is System.Windows.Controls.TextBlock tb) return tb.Text;
            if (t.Header is System.Windows.Controls.TextBlock titulo) return titulo.Text;
            return t.Header?.ToString() ?? "(pestaña)";
        }

        // Lee por reflexión el estado de cambios del detalle: la propiedad "HayCambios"
        // (PedidosDetalle) o el campo "_hayCambios" (resto de detalles). Los paneles que
        // no tengan ninguno (General/selectores) cuentan como "sin cambios".
        private static bool TieneCambiosSinGuardar(object? content)
        {
            if (content == null) return false;
            var tipo = content.GetType();

            var prop = tipo.GetProperty("HayCambios",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.PropertyType == typeof(bool))
                return prop.GetValue(content) is bool pb && pb;

            var field = tipo.GetField("_hayCambios",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
                return field.GetValue(content) is bool fb && fb;

            return false;
        }

        // Pide confirmación si hay cambios sin guardar, indicando EXACTAMENTE en qué
        // pestañas. Devuelve true si se puede cerrar (no hay cambios o el usuario aceptó
        // perderlos). Pública: la usa también Configuración antes de refrescar el
        // contexto (cambio de empresa/sucursal/periodo), que cierra todas las pestañas
        // igual que cerrar sesión.
        public bool ConfirmarPerderCambios()
        {
            var conCambios = PestañasConCambios();
            if (conCambios.Count == 0) return true;

            string detalle = string.Join("\n", conCambios.ConvertAll(t => "   •  " + t));
            var res = MessageBox.Show(
                "Hay cambios sin guardar en:\n\n" + detalle +
                "\n\nSi cierras se perderán esos cambios.\n¿Seguro que deseas cerrar?",
                "Cerrar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return res == MessageBoxResult.Yes;
        }

        private void ConsolaMovimientos_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_cierreConfirmado && !ConfirmarPerderCambios())
            {
                e.Cancel = true;   // el usuario decidió no cerrar
                return;
            }
            ConexionEstado.Cambio -= OnConexionCambio;
            MarcarInactivo();
        }

        private void BtnCerrarSesion_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmarPerderCambios()) return;
            CerrarSesionInterno();
        }

        // Cierra sesión sin volver a pedir confirmación de cambios sin guardar: la usa
        // un llamador que ya avisó al usuario de antemano (p. ej. Regenerar códigos).
        public void CerrarSesionForzada() => CerrarSesionInterno();

        private void CerrarSesionInterno()
        {
            _cierreConfirmado = true;   // ya confirmado: Closing no vuelve a preguntar

            MarcarInactivo();
            AppState.SesionActiva  = false;
            AppState.UsuarioActivo = "";
            AppState.EmpresaActiva = "";
            DatabaseConnection.CerrarConexion();

            var login = new LoginWindow();
            login.Show();
            Close();
        }
    }
}
