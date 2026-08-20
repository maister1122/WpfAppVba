using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class TraspasosDetalle : System.Windows.Controls.UserControl
    {
        public event Action? Cerrando;
        private static SqlData Sql => SqlData.Instance;
        // object en vez de TraspasosGeneral: VisorEmpresa.TraspasosGeneral (su grilla
        // de solo-lectura propia) no es del mismo tipo que el de la app principal, y
        // _padre no se usa dentro de esta clase — object evita que el visor deba
        // vincular también el TraspasosGeneral completo de la app principal solo
        // para satisfacer este parámetro.
        private readonly object? _padre;
        private readonly string _idEditar;
        private bool _hayCambios      = false;
        private bool _cargando        = true;
        private bool _editarFormulario = false;
        private List<TraspasoItemFila> _items = new();
        // IDs de las líneas existentes al abrir para editar (para el guardado diferencial).
        private HashSet<string> _itemsOrig = new();

        private readonly HashSet<string> _articulosAlertados = new();

        private bool _iniciado = false;
        private readonly string _tituloTab;
        private string _codigoDocT = "";
        // Evita guardados duplicados si el usuario hace varios clicks seguidos en
        // Guardar: Guardar() puede mostrar MessageBox (VerificarConexionParaGuardar,
        // SinPestañasRelacionadas) cuyo loop de mensajes anidado podría procesar un
        // segundo click sobre BtnGuardar antes de que el primer guardado termine.
        private bool _guardando = false;

        /// <summary>
        /// ID del documento recién creado (solo en modo "nuevo").
        /// El padre lo lee en el evento Cerrando para enfocar la fila.
        /// </summary>
        public string? DocumentoCreadoId { get; private set; }

        public TraspasosDetalle(object? padre = null, string idEditar = "", string tituloTab = "")
        {
            InitializeComponent();
            _padre       = padre;
            _idEditar    = idEditar;
            _tituloTab   = tituloTab;
            Loaded      += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Cambiar tipo de movimiento en pestaña existente ─────────────────
        public void CambiarTipoMovimiento(string tipo)
        {
            AppState.TipoMovimiento = tipo.ToLower();
        }

        // ─── Confirmar edición pendiente antes de que el click llegue a su destino ──
        // Bug conocido de WPF DataGrid: si una celda de GridItems está en edición y se
        // hace click en OTRO control (Guardar, un TextBox de cabecera), el CommitEdit
        // de la grilla recién se dispara al procesar ESE MISMO click — lo que descarta
        // el click en curso y obliga a hacer uno segundo para que el foco realmente
        // llegue al control de destino. Confirmando acá, en el túnel (se ejecuta ANTES
        // de que el click llegue al control real), se evita esa carrera.
        private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (GridItems.IsKeyboardFocusWithin)
                GridItems.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            string tipo = AppState.TipoMovimiento.ToLower();
            LblTitulo.Text = tipo == "salida"
                ? "Salida de Productos"
                : "Entrada de Productos";

            CargarComboSucursales();

            if (AppState.EventoFormularioM == "editar")
                CargarParaEditar();
            else
                CargarParaNuevo();

            _cargando   = false;
            _hayCambios = false;
        }

        // ─── Modo editar ──────────────────────────────────────────────────────
        private void CargarParaEditar()
        {
            Box_DocumentoT.IsEnabled = false;
            string codigoDocEdit = Sql.DocumentosTObj.ObtenerItem("codigo", _idEditar)?.ToString() ?? "";
            Box_DocumentoT.Text = codigoDocEdit;

            var fechaObj = Sql.DocumentosTObj.ObtenerItem("fecha", _idEditar);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : DateTime.Now;
            Box_Fecha.SelectedDate = fecha.Date;
            Box_Hora.Text = fecha.ToString("HH:mm:ss");

            // Sucursal opuesta
            string tipo    = AppState.TipoMovimiento.ToLower();
            string campOtro = tipo == "salida" ? "destino" : "origen";
            string otroUuid  = Sql.DocumentosTObj.ObtenerItem(campOtro, _idEditar)?.ToString() ?? "";
            CboSucursal.SelectedValue = otroUuid;

            // Emisión / Edición
            var emisionObj = Sql.DocumentosTObj.ObtenerItem("emision", _idEditar);
            var edicionObj = Sql.DocumentosTObj.ObtenerItem("edicion", _idEditar);
            TxtEmision.Text = emisionObj != null ? $"{Convert.ToDateTime(emisionObj):d} {Convert.ToDateTime(emisionObj):HH:mm:ss}" : "";
            TxtEdicion.Text = edicionObj != null ? $"{Convert.ToDateTime(edicionObj):d} {Convert.ToDateTime(edicionObj):HH:mm:ss}" : "";

            Box_Referencia.Text    = Sql.DocumentosTObj.ObtenerItem("referencia",  _idEditar)?.ToString() ?? "";
            Box_Observaciones.Text = Sql.DocumentosTObj.ObtenerItem("observacion", _idEditar)?.ToString() ?? "";

            // Permisos según emitido ─────────────────────────────────────────
            string emitido  = Sql.DocumentosTObj.ObtenerItem("emitido", _idEditar)?.ToString() ?? "";
            string estadoDB = Sql.DocumentosTObj.ObtenerItem("estado",  _idEditar)?.ToString() ?? "pendiente";
            bool esLocal    = (emitido == AppState.SucursalActiva);

            if (esLocal)
            {
                if (estadoDB.ToLower() == "entregado")
                {
                    // Solo lectura total: no se puede editar nada, ni siquiera tipo de
                    // movimiento, duplicar línea o guardar.
                    DeshabilitarControlesCabecera();
                    BtnGuardar.IsEnabled    = false;
                    _editarFormulario = false;
                    GridItems.IsEnabled = false;
                    ActualizarBotonesGrid();
                }
                else
                {
                    // Puede editar artículos pero no el estado
                    Box_Estado.IsEnabled = false;
                    _editarFormulario    = true;
                }
                SeleccionarEstado(estadoDB);
            }
            else
            {
                // De otra sucursal: no se puede editar nada excepto el estado (se permite
                // guardar para poder persistir ese cambio). Los artículos solo se ven, no
                // se modifican, y el tipo de movimiento tampoco se puede cambiar.
                DeshabilitarControlesCabecera();

                if (estadoDB.ToLower() == "pendiente")
                    estadoDB = "pendiente revisar";

                Box_Estado.Items.Clear();
                Box_Estado.Items.Add(new ComboBoxItem { Content = "pendiente revisar" });
                Box_Estado.Items.Add(new ComboBoxItem { Content = "entregado" });
                Box_Estado.IsEnabled = true;
                _editarFormulario = false;
                GridItems.IsEnabled = false;
                ActualizarBotonesGrid();
                SeleccionarEstado(estadoDB);
            }

            // Cargar artículos
            ActualizarBadgeEstado();

            _items.Clear();
            int linea = 1;
            int uf = Sql.TraspasosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.TraspasosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.TraspasosObj.ObtenerItem("documentoT", id)?.ToString() != _idEditar) continue;

                string artId  = Sql.TraspasosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string codigo = Sql.ArticulosObj.ObtenerItem("codigo",   artId)?.ToString() ?? "";
                double cant   = Convert.ToDouble(Sql.TraspasosObj.ObtenerItem("cantidad", id) ?? 0);
                string desc   = ObtenerDescripcionArticulo(artId);

                _items.Add(new TraspasoItemFila
                {
                    TraspasoId  = id,
                    Linea       = linea++,
                    ArticuloId  = artId,
                    Codigo      = codigo,
                    Descripcion = desc,
                    Cantidad    = cant
                });
            }
            _itemsOrig = new HashSet<string>(_items.Select(x => x.TraspasoId));
            RefrescarGrid();
        }

        // ─── Modo nuevo ───────────────────────────────────────────────────────
        private void CargarParaNuevo()
        {
            _editarFormulario = true;
            Box_DocumentoT.IsEnabled = false;
            _codigoDocT = CodigoDocumento.SiguienteTraspaso(AppState.EmpresaActiva);
            Box_DocumentoT.Text      = _codigoDocT;

            DateTime ahora = DateTime.Now;
            Box_Fecha.SelectedDate = ahora.Date;
            Box_Hora.Text = ahora.ToString("HH:mm:ss");
            SeleccionarEstado("pendiente");
            Box_Estado.IsEnabled = false;

            TxtEmision.Text = $"{ahora:d} {ahora:HH:mm:ss}";
            TxtEdicion.Text = $"{ahora:d} {ahora:HH:mm:ss}";

            ActualizarBadgeEstado();

            _items.Clear();
            _itemsOrig.Clear();
            RefrescarGrid();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────
        private void SeleccionarEstado(string valor)
        {
            foreach (ComboBoxItem item in Box_Estado.Items)
            {
                if (item.Content?.ToString() == valor)
                {
                    Box_Estado.SelectedItem = item;
                    return;
                }
            }
            if (Box_Estado.Items.Count > 0) Box_Estado.SelectedIndex = 0;
        }

        private void ActualizarBadgeEstado()
        {
            string estado = ((Box_Estado.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "").ToLower();
            (BadgeEstado.Background, TxtBadgeEstado.Foreground, TxtBadgeEstado.Text) = estado switch
            {
                "entregado"         => (new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                                        new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)), "Entregado"),
                "pendiente revisar" => (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                                        new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)), "Pendiente revisar"),
                _                   => (new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                                        new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)), "Pendiente")
            };

            // Ícono y color según tipo (entrada/salida)
            string tipo    = AppState.TipoMovimiento.ToLower();
            bool esSalida  = tipo == "salida";
            LblIconoTipo.Text       = esSalida ? "SA" : "EN";
            IconoBorde.Background   = esSalida
                ? new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7))
                : new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
            LblIconoTipo.Foreground = esSalida
                ? new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E))
                : new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));

            LblDocNum.Text       = Box_DocumentoT.Text;
            LblSucursalTipo.Text = esSalida ? "Sucursal destino" : "Sucursal origen";
        }

        // ─── Combo de sucursal contraparte: todas menos la activa ─────────────
        private void CargarComboSucursales()
        {
            var lista = new List<SucursalOpcion>();
            int uf = Sql.SucursalesObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.SucursalesObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (id == AppState.SucursalActiva) continue;

                string codigo = Sql.SucursalesObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
                string desc   = Sql.SucursalesObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                lista.Add(new SucursalOpcion { Id = id, Descripcion = $"{codigo} - {desc}" });
            }

            CboSucursal.DisplayMemberPath = "Descripcion";
            CboSucursal.SelectedValuePath = "Id";
            CboSucursal.ItemsSource       = lista;
        }

        private string ResolverSucursalId() => CboSucursal.SelectedValue?.ToString() ?? "";

        private static string ObtenerDescripcionArticulo(string artId)
        {
            string desc    = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
            string famId   = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
            string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
            string modelo  = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
            return FuncionesComunes.UnirVariables(desc, famDesc, modelo);
        }

        private void DeshabilitarControlesCabecera()
        {
            Box_DocumentoT.IsEnabled            = false;
            Box_Fecha.IsEnabled                  = false;
            Box_Hora.IsEnabled                   = false;
            CboSucursal.IsEnabled                = false;
            Box_Estado.IsEnabled                 = false;
            Box_Referencia.IsEnabled             = false;
            Box_Observaciones.IsEnabled          = false;
        }

        private void ActualizarBotonesGrid()
        {
            BtnImportarArticulos.IsEnabled = _editarFormulario;
            BtnBuscarArticulo.IsEnabled    = _editarFormulario;
            BtnInsertar.IsEnabled          = _editarFormulario;
            BtnNuevaLinea.IsEnabled        = _editarFormulario;
            BtnEliminarLinea.IsEnabled     = _editarFormulario;
            BtnDuplicarLinea.IsEnabled     = _editarFormulario;
        }

        // ─── Refrescar grid y totales ─────────────────────────────────────────
        private void RefrescarGrid()
        {
            for (int i = 0; i < _items.Count; i++) _items[i].Linea = i + 1;

            var seleccionado = GridItems.SelectedItem as TraspasoItemFila;
            GridItems.ItemsSource = null;
            GridItems.ItemsSource = _items;
            if (seleccionado != null && _items.Contains(seleccionado))
                GridItems.SelectedItem = seleccionado;

            CargarTotales();
        }

        // ─── Totales por categoría ────────────────────────────────────────────
        private void CargarTotales()
        {
            double totalUnidades = 0;
            var distintos = new HashSet<string>();

            // Inicializar todas las categorías existentes con cantidad 0
            var categoriaIds   = new List<string>();
            var categoriaDescs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var porCategoria   = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            int ufCat = Sql.CategoriasObj.ContarFilas;
            for (int i = 1; i <= ufCat; i++)
            {
                var idObj = Sql.CategoriasObj.Mover(i);
                if (idObj == null) continue;
                string catId   = idObj.ToString()!;
                string catDesc = Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? catId;
                categoriaIds.Add(catId);
                categoriaDescs[catId] = catDesc;
                porCategoria[catDesc] = 0;
            }

            double cantOtros = 0;
            foreach (var item in _items)
            {
                totalUnidades += item.Cantidad;
                if (!string.IsNullOrEmpty(item.ArticuloId))
                    distintos.Add(item.ArticuloId);

                string catId   = Sql.ArticulosObj.ObtenerItem("categoria", item.ArticuloId)?.ToString() ?? "";
                string catDesc = (!string.IsNullOrEmpty(catId) && categoriaDescs.ContainsKey(catId))
                                 ? categoriaDescs[catId] : "";
                if (!string.IsNullOrEmpty(catDesc))
                    porCategoria[catDesc] += item.Cantidad;
                else
                    cantOtros += item.Cantidad;
            }

            TxtTotalUnidades.Text      = totalUnidades.ToString("N0");
            TxtUnidadesDiferentes.Text = distintos.Count.ToString();

            var filas = categoriaIds
                .Select(id => new CategoriaCantFila
                {
                    Categoria = categoriaDescs[id],
                    Cantidad  = porCategoria[categoriaDescs[id]].ToString("N0")
                })
                .ToList();
            filas.Add(new CategoriaCantFila { Categoria = "Otros", Cantidad = cantOtros.ToString("N0") });

            GridFocusHelper.ReasignarItemsSource(GridCategorias, filas);
        }

        // ─── Stock del artículo seleccionado (GridStock / Lista3) ─────────────
        private void CargarStock(TraspasoItemFila? fila)
        {
            if (fila == null || string.IsNullOrEmpty(fila.ArticuloId))
            {
                GridFocusHelper.ReasignarItemsSource(GridStock, null);
                return;
            }

            double stock = StockCalculator.ContarStock(fila.ArticuloId, AppState.DataFechaFinal);

            GridFocusHelper.ReasignarItemsSource(GridStock, new List<TraspasoStockFila>
            {
                new TraspasoStockFila
                {
                    Codigo     = fila.Codigo,
                    Disponible = stock.ToString("N0"),
                    Stock      = stock.ToString("N0")
                }
            });
        }

        // ─── Notificación de stock insuficiente (solo salidas, modo nuevo) ────
        private void NotificarStockInsuficiente(TraspasoItemFila? filaModificada = null)
        {
            if (AppState.TipoMovimiento.ToLower() != "salida") return;
            if (AppState.EventoFormularioM != "nuevo") return;

            // Un solo aviso con todos los artículos sin stock (mismo criterio que
            // PedidosDetalle.VerificarStockVenta): al importar varios artículos de una
            // vez, avisar uno por uno obligaba a cerrar un cartel por cada línea.
            var faltantes = new List<string>();

            foreach (var item in _items)
            {
                if (string.IsNullOrEmpty(item.ArticuloId)) continue;
                if (_articulosAlertados.Contains(item.ArticuloId)) continue;

                double totalCant = _items.Where(x => x.ArticuloId == item.ArticuloId)
                                          .Sum(x => x.Cantidad);
                double stock = StockCalculator.ContarStock(item.ArticuloId, AppState.DataFechaFinal);

                if (stock < totalCant)
                {
                    _articulosAlertados.Add(item.ArticuloId);
                    faltantes.Add($"{item.Descripcion}: disponible {stock:F0}, solicitado {totalCant:F0}");
                }
            }

            if (faltantes.Count > 0)
            {
                MessageBox.Show("Stock insuficiente:\n\n" + string.Join("\n", faltantes),
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ─── Eventos de campos ────────────────────────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
            if (sender == Box_DocumentoT) LblDocNum.Text = Box_DocumentoT.Text;
        }

        private void Campo_DateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private void Campo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
            if (sender == Box_Estado) ActualizarBadgeEstado();
        }

        private void CboSucursal_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // ─── Selección en GridItems → mostrar stock ───────────────────────────
        private void GridItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => CargarStock(GridItems.SelectedItem as TraspasoItemFila);

        // ─── Buscar artículo (single-select) ─────────────────────────────────
        // #if !VISOR ya no es necesario en la práctica (TraspasosDetalle no está
        // vinculado en VisorEmpresa.csproj — tiene su propia copia), pero se deja
        // por si en el futuro algo más vuelve a vincular este archivo.
        private void BtnBuscarArticulo_Click(object sender, RoutedEventArgs e)
        {
            if (!_editarFormulario) return;
#if !VISOR

            var filaActual = GridItems.SelectedItem as TraspasoItemFila;
            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, null, art =>
            {
                TraspasoItemFila filaEnfocar;

                if (filaActual != null && _items.Contains(filaActual))
                {
                    filaActual.ArticuloId  = art.Id;
                    filaActual.Codigo      = art.Codigo;
                    filaActual.Descripcion = art.Descripcion;
                    filaEnfocar = filaActual;
                }
                else
                {
                    var nueva = new TraspasoItemFila
                    {
                        TraspasoId  = "",
                        ArticuloId  = art.Id,
                        Codigo      = art.Codigo,
                        Descripcion = art.Descripcion,
                        Cantidad    = 1
                    };
                    _items.Add(nueva);
                    filaEnfocar = nueva;
                }
                _hayCambios = true;
                RefrescarGrid();
                NotificarStockInsuficiente();
                EnfocarColumnaCantidad(filaEnfocar);
                return true;
            }, contexto: _tituloTab, llamador: this);
#endif
        }

        // Posiciona el cursor en la celda Cantidad de la fila indicada e inicia edición
        private void EnfocarColumnaCantidad(TraspasoItemFila fila)
        {
            var colCantidad = GridItems.Columns
                .FirstOrDefault(c => c.Header?.ToString() == "Cantidad");
            if (colCantidad == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                GridItems.SelectedItem = fila;
                GridItems.CurrentCell  = new DataGridCellInfo(fila, colCantidad);
                GridItems.ScrollIntoView(fila, colCantidad);
                GridItems.Focus();
                GridItems.BeginEdit();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ─── Importar artículos ───────────────────────────────────────────────
        private void BtnImportarArticulos_Click(object sender, RoutedEventArgs e)
        {
            if (!_editarFormulario) return;
#if !VISOR

            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, arts =>
            {
                foreach (var art in arts)
                {
                    _items.Add(new TraspasoItemFila
                    {
                        TraspasoId  = "",
                        ArticuloId  = art.Id,
                        Codigo      = art.Codigo,
                        Descripcion = art.Descripcion,
                        Cantidad    = 1
                    });
                }
                _hayCambios = true;
                RefrescarGrid();
                NotificarStockInsuficiente();
                if (_items.Count > 0)
                {
                    var ultimo = _items[_items.Count - 1];
                    GridItems.SelectedItem = ultimo;
                    GridItems.ScrollIntoView(ultimo);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
                return true;
            }, null, contexto: _tituloTab, llamador: this);
#endif
        }

        // ─── Nueva línea vacía ────────────────────────────────────────────────
        private void BtnNuevaLinea_Click(object sender, RoutedEventArgs e)
        {
            if (!_editarFormulario) return;

            _items.Add(new TraspasoItemFila
            {
                TraspasoId = "", ArticuloId = "", Codigo = "",
                Descripcion = "", Cantidad = 1
            });
            _hayCambios = true;
            RefrescarGrid();
            int lastIdx = GridItems.Items.Count - 1;
            if (lastIdx >= 0)
            {
                GridItems.SelectedIndex = lastIdx;
                GridItems.ScrollIntoView(GridItems.SelectedItem);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        // ─── Duplicar línea seleccionada ──────────────────────────────────────
        private void BtnDuplicarLinea_Click(object sender, RoutedEventArgs e)
        {
            if (!_editarFormulario) return;
            if (GridItems.SelectedItem is not TraspasoItemFila fila) return;

            var copia = new TraspasoItemFila
            {
                TraspasoId = "", ArticuloId = fila.ArticuloId, Codigo = fila.Codigo,
                Descripcion = fila.Descripcion, Cantidad = fila.Cantidad
            };
            _items.Add(copia);
            _hayCambios = true;
            RefrescarGrid();
            NotificarStockInsuficiente();
            GridItems.SelectedItem = copia;
            GridItems.ScrollIntoView(copia);
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        // ─── Eliminar línea seleccionada ──────────────────────────────────────
        private void BtnEliminarLinea_Click(object sender, RoutedEventArgs e)
        {
            if (!_editarFormulario) return;
            if (GridItems.SelectedItem is not TraspasoItemFila fila) return;

            var res = MessageBox.Show("¿Eliminar esta línea?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                int idx = _items.IndexOf(fila);
                _items.Remove(fila);
                _hayCambios = true;
                RefrescarGrid();
                if (_items.Count > 0)
                {
                    var siguiente = _items[Math.Min(idx, _items.Count - 1)];
                    GridItems.SelectedItem = siguiente;
                    GridItems.ScrollIntoView(siguiente);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
            }
        }

        // ─── Edición de celda ─────────────────────────────────────────────────
        private void GridItems_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            _hayCambios = true;

            if (e.EditAction == DataGridEditAction.Commit &&
                e.Column.Header?.ToString() == "Código" &&
                e.Row.Item is TraspasoItemFila fila &&
                e.EditingElement is TextBox tb)
            {
                string codigo  = tb.Text.Trim();
                string artId   = Sql.ArticulosObj.BuscarIdentificador("codigo", codigo);
                if (!string.IsNullOrEmpty(artId))
                {
                    fila.ArticuloId  = artId;
                    fila.Codigo      = codigo;
                    fila.Descripcion = ObtenerDescripcionArticulo(artId);
                }
                else
                {
                    fila.ArticuloId  = "";
                    fila.Codigo      = codigo;
                    fila.Descripcion = string.IsNullOrEmpty(codigo) ? "" : "⚠ Artículo no encontrado";
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefrescarGrid();
                    NotificarStockInsuficiente(fila);
                    GridFocusHelper.EnfocarCeldaSeleccionada(GridItems, soloSiElFocoSigueEnLaGrilla: true);
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            if (e.EditAction == DataGridEditAction.Commit &&
                e.Column.Header?.ToString() == "Cantidad" &&
                e.Row.Item is TraspasoItemFila filaCant &&
                e.EditingElement is TextBox tbCant)
            {
                if (double.TryParse(tbCant.Text,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    out double nuevaCant))
                {
                    filaCant.Cantidad = nuevaCant;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefrescarGrid();
                    NotificarStockInsuficiente(filaCant);
                    GridFocusHelper.EnfocarCeldaSeleccionada(GridItems, soloSiElFocoSigueEnLaGrilla: true);
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefrescarGrid();
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems, soloSiElFocoSigueEnLaGrilla: true);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ─── Seleccionar todo al entrar al campo Código ───────────────────────
        private void GridItems_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            string col = e.Column.Header?.ToString() ?? "";
            if (col is "Código" or "Cantidad")
                GridFocusHelper.SeleccionarTodoEnEdicion(e.EditingElement);
            if (col == "Cantidad" && e.EditingElement is TextBox tb)
                FuncionesComunes.RestringirACantidad(tb);
        }

        // ─── Insertar línea en la posición seleccionada ───────────────────────
        private void BtnInsertar_Click(object sender, RoutedEventArgs e)
        {
            if (!_editarFormulario) return;

            int idx = GridItems.SelectedItem is TraspasoItemFila sel
                      ? _items.IndexOf(sel)
                      : _items.Count;
            if (idx < 0) idx = _items.Count;

            var nueva = new TraspasoItemFila
            {
                TraspasoId = "", ArticuloId = "", Codigo = "",
                Descripcion = "", Cantidad = 1
            };
            _items.Insert(idx, nueva);
            _hayCambios = true;
            RefrescarGrid();
            if (idx < GridItems.Items.Count)
            {
                GridItems.SelectedIndex = idx;
                GridItems.ScrollIntoView(GridItems.SelectedItem);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
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
                return AppState.EventoFormularioM == "editar" ? GuardarEditar() : GuardarNuevo();
            }
            finally
            {
                _guardando = false;
                BtnGuardar.IsEnabled = true;
            }
        }

        // ─── Revisión del código al guardar ───────────────────────────────────
        // El código se calculó al abrir el formulario: entre ese momento y el
        // guardado, otro usuario (u otra sucursal de la misma empresa) pudo haberse
        // quedado con el número. Si ya está tomado se avisa y se usa el siguiente
        // libre, en vez de guardar un código duplicado.
        private void VerificarCodigo()
        {
            string nuevo = CodigoDocumento.VerificarTraspaso(_codigoDocT, AppState.EmpresaActiva);
            if (nuevo != _codigoDocT)
            {
                MessageBox.Show(
                    $"El código {_codigoDocT} ya estaba en uso. El documento se guardará " +
                    $"con el código {nuevo}.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Information);
                _codigoDocT = nuevo;
                Box_DocumentoT.Text = _codigoDocT;
            }
        }

        private bool GuardarNuevo()
        {
            try
            {
                DateTime fechaBase  = Box_Fecha.SelectedDate ?? DateTime.Today;
                DateTime fechaFinal = CombinarFechaHora(fechaBase, Box_Hora.Text);

                string tipo      = AppState.TipoMovimiento.ToLower();
                string otroUuid  = ResolverSucursalId();
                if (string.IsNullOrEmpty(otroUuid))
                {
                    string campo = tipo == "salida" ? "sucursal destino" : "sucursal origen";
                    MessageBox.Show($"Debe asignar una {campo} al documento.", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                string origenId  = tipo == "salida"  ? AppState.SucursalActiva : otroUuid;
                string destinoId = tipo == "entrada" ? AppState.SucursalActiva : otroUuid;
                string estado    = (Box_Estado.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "pendiente";

                string id = Guid.NewGuid().ToString();

                VerificarCodigo();

                Sql.DocumentosTObj.Nuevo(id);
                Sql.DocumentosTObj.EstablecerItem("codigo",      id, _codigoDocT);
                Sql.DocumentosTObj.EstablecerItem("origen",      id, origenId);
                Sql.DocumentosTObj.EstablecerItem("destino",     id, destinoId);
                Sql.DocumentosTObj.EstablecerItem("fecha",       id, fechaFinal);
                Sql.DocumentosTObj.EstablecerItem("estado",      id, estado);
                Sql.DocumentosTObj.EstablecerItem("referencia",  id, Box_Referencia.Text.Trim());
                Sql.DocumentosTObj.EstablecerItem("observacion", id, Box_Observaciones.Text.Trim());
                Sql.DocumentosTObj.EstablecerItem("emision",     id, DateTime.Now);
                Sql.DocumentosTObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.DocumentosTObj.EstablecerItem("usuario",     id, AppState.UsuarioActivo);
                Sql.DocumentosTObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);
                Sql.DocumentosTObj.EstablecerItem("emitido",     id, AppState.SucursalActiva);

                // ── Crear líneas (diferencial: documento nuevo → todas se insertan) ──
                GuardarLineasTraspaso(id);

                Sql.DocumentosTObj.OrdenarData(("fecha", false));
                Sql.TraspasosObj.OrdenarData(("documentoT", false), ("indice", false));

                _hayCambios = false;
                DocumentoCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool GuardarEditar()
        {
            string docT = _idEditar;
            try
            {
                DateTime fechaBase  = Box_Fecha.SelectedDate ?? DateTime.Today;
                DateTime fechaFinal = CombinarFechaHora(fechaBase, Box_Hora.Text);

                string tipo     = AppState.TipoMovimiento.ToLower();
                string campOtro = tipo == "salida" ? "destino" : "origen";
                string estado   = (Box_Estado.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "pendiente";

                // Si es "pendiente revisar" guardar como "pendiente" en la DB (igual VBA)
                if (estado == "pendiente revisar") estado = "pendiente";

                string otroUuidE = ResolverSucursalId();
                if (_editarFormulario && string.IsNullOrEmpty(otroUuidE))
                {
                    string campo = tipo == "salida" ? "sucursal destino" : "sucursal origen";
                    MessageBox.Show($"Debe asignar una {campo} al documento.", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                Sql.DocumentosTObj.EstablecerItem("fecha",       docT, fechaFinal);
                Sql.DocumentosTObj.EstablecerItem(campOtro,      docT, otroUuidE);
                Sql.DocumentosTObj.EstablecerItem("estado",      docT, estado);
                Sql.DocumentosTObj.EstablecerItem("referencia",  docT, Box_Referencia.Text.Trim());
                Sql.DocumentosTObj.EstablecerItem("observacion", docT, Box_Observaciones.Text.Trim());
                Sql.DocumentosTObj.EstablecerItem("edicion",     docT, DateTime.Now);
                Sql.DocumentosTObj.EstablecerItem("usuarioE",    docT, AppState.UsuarioActivo);

                // ── Guardado diferencial de líneas (inserta/actualiza/oculta) ──
                GuardarLineasTraspaso(docT);

                Sql.DocumentosTObj.OrdenarData(("fecha", false));
                Sql.TraspasosObj.OrdenarData(("documentoT", false), ("indice", false));

                _hayCambios = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Guardado diferencial de líneas (inserta/actualiza/oculta) ────────
        private void GuardarLineasTraspaso(string docT)
        {
            var vigentes = new HashSet<string>(
                _items.Where(x => !string.IsNullOrEmpty(x.TraspasoId)).Select(x => x.TraspasoId));

            var reservados = Sql.TraspasosObj.IndicesNoNormales("documentoT", docT);
            foreach (var idOrig in _itemsOrig)
                if (!vigentes.Contains(idOrig))
                {
                    var ix = Sql.TraspasosObj.ObtenerItem("indice", idOrig);
                    if (ix != null && int.TryParse(ix.ToString(), out int n)) reservados.Add(n);
                    Sql.TraspasosObj.Eliminar(idOrig);
                }

            int next = 1;
            foreach (var item in _items)
            {
                string id;
                if (string.IsNullOrEmpty(item.TraspasoId))
                {
                    id = Guid.NewGuid().ToString();
                    Sql.TraspasosObj.Nuevo(id);
                    Sql.TraspasosObj.EstablecerItem("documentoT", id, docT);
                    item.TraspasoId = id;
                }
                else id = item.TraspasoId;

                while (reservados.Contains(next)) next++;
                Sql.TraspasosObj.EstablecerItem("indice",   id, next);
                next++;
                Sql.TraspasosObj.EstablecerItem("articulo", id, item.ArticuloId);
                Sql.TraspasosObj.EstablecerItem("cantidad", id, item.Cantidad);
            }
            _itemsOrig = new HashSet<string>(_items.Select(x => x.TraspasoId));
        }

        // ─── Combinar fecha + hora ────────────────────────────────────────────
        private static DateTime CombinarFechaHora(DateTime fecha, string horaTexto)
        {
            if (TimeSpan.TryParse(horaTexto, out var ts))
                return fecha.Date + ts;
            return fecha.Date;
        }

        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            if (Guardar()) Cerrando?.Invoke();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (!SinPestañasRelacionadas()) return;
            _hayCambios = false; Cerrando?.Invoke();
        }

        // ─── Al cerrar (llamado por el botón X de la pestaña) ──────────────────
        public void IntentarCerrar()
        {
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            if (!SinPestañasRelacionadas()) return;
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No)          Cerrando?.Invoke();
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class TraspasoItemFila
    {
        public string TraspasoId  { get; set; } = "";
        public int    Linea       { get; set; }
        public string ArticuloId  { get; set; } = "";
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public double Cantidad    { get; set; }
    }

    public class TraspasoStockFila
    {
        public string Codigo     { get; set; } = "";
        public string Disponible { get; set; } = "";
        public string Stock      { get; set; } = "";
    }

    public class SucursalOpcion
    {
        public string Id          { get; set; } = "";
        public string Descripcion { get; set; } = "";
    }
}
