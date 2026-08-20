using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class CorreccionesDetalle : UserControl
    {
        public event Action? Cerrando;

        private static SqlData Sql => SqlData.Instance;

        // object en vez de CorreccionesGeneral: VisorEmpresa.CorreccionesGeneral (su
        // grilla de solo-lectura propia) no es del mismo tipo que el de la app
        // principal, y _padre no se usa dentro de esta clase — object evita que el
        // visor deba vincular también el CorreccionesGeneral completo de la app
        // principal solo para satisfacer este parámetro.
        private readonly object? _padre;
        private readonly string _idEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        private List<CorreccionItemFila> _items = new();
        // IDs de las líneas existentes al abrir para editar (para el guardado diferencial).
        private HashSet<string> _itemsOrig = new();

        private bool _iniciado = false;
        private readonly string _tituloTab;
        private string _codigoDocC = "";
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle).
        private bool _guardando = false;

        // ─── Confirmar edición pendiente antes de que el click llegue a su destino ──
        // Bug conocido de WPF DataGrid: si una celda de GridItems está en edición y se
        // hace click en OTRO control (Guardar, un TextBox de cabecera), el CommitEdit
        // del grid recién se dispara al procesar ESE MISMO click — lo que descarta
        // el click en curso y obliga a hacer uno segundo para que el foco realmente
        // llegue al control de destino. Confirmando acá, en el túnel (se ejecuta ANTES
        // de que el click llegue al control real), se evita esa carrera.
        private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (GridItems.IsKeyboardFocusWithin)
                GridItems.CommitEdit(DataGridEditingUnit.Row, true);
        }

        /// <summary>ID del documento de corrección recién creado.</summary>
        public string? ItemCreadoId { get; private set; }

        public CorreccionesDetalle(object? padre = null, string idEditar = "", string tituloTab = "")
        {
            InitializeComponent();
            _padre       = padre;
            _idEditar    = idEditar;
            _tituloTab   = tituloTab;
            Loaded      += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            if (AppState.EventoFormularioC == "editar")
            {
                _movimiento      = NormalizarMovimiento(Sql.DocumentosCObj.ObtenerItem("movimiento", _idEditar)?.ToString());
                string tipoLabel = _movimiento == "repuesta" ? "Repuesta" : "Retirado";
                LblTitulo.Text   = $"Editar Corrección de {tipoLabel}";
                CargarParaEditar();
            }
            else
            {
                _movimiento      = NormalizarMovimiento(AppState.TipoCorreccion);
                string tipoLabel = _movimiento == "repuesta" ? "Repuesta" : "Retirado";
                LblTitulo.Text   = $"Corrección de {tipoLabel}";
                CargarParaNuevo();
            }

            ActualizarBadge();
            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            Box_DocumentoC.IsEnabled = false;
            string codigoDocEdit = Sql.DocumentosCObj.ObtenerItem("codigo", _idEditar)?.ToString() ?? "";
            Box_DocumentoC.Text = codigoDocEdit;

            var fechaObj = Sql.DocumentosCObj.ObtenerItem("fecha", _idEditar);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : DateTime.Now;
            Box_Fecha.SelectedDate = fecha;
            Box_Hora.Text          = fecha.ToString("HH:mm:ss");

            string movimiento = NormalizarMovimiento(Sql.DocumentosCObj.ObtenerItem("movimiento", _idEditar)?.ToString());
            string motivo     = Sql.DocumentosCObj.ObtenerItem("motivo",      _idEditar)?.ToString() ?? "";
            SeleccionarMovimiento(movimiento);
            ActualizarMotivos(motivo);

            Box_Referencia.Text  = Sql.DocumentosCObj.ObtenerItem("referencia",  _idEditar)?.ToString() ?? "";
            Box_Observacion.Text = Sql.DocumentosCObj.ObtenerItem("observacion", _idEditar)?.ToString() ?? "";

            var emisionObj = Sql.DocumentosCObj.ObtenerItem("emision", _idEditar);
            var edicionObj = Sql.DocumentosCObj.ObtenerItem("edicion", _idEditar);
            Box_Emision.Text = emisionObj != null ? $"{Convert.ToDateTime(emisionObj):d} {Convert.ToDateTime(emisionObj):HH:mm:ss}" : "";
            Box_Edicion.Text = edicionObj != null ? $"{Convert.ToDateTime(edicionObj):d} {Convert.ToDateTime(edicionObj):HH:mm:ss}" : "";

            // Cargar líneas de la corrección
            _items.Clear();
            int linea = 1;
            int uf    = Sql.CorreccionesObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.CorreccionesObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (Sql.CorreccionesObj.ObtenerItem("documentoC", id)?.ToString() != _idEditar) continue;

                string articuloId = Sql.CorreccionesObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string codigo     = Sql.ArticulosObj.ObtenerItem("codigo", articuloId)?.ToString() ?? "";
                string desc       = ObtenerDescripcionArticulo(articuloId);
                double cantidad   = Convert.ToDouble(Sql.CorreccionesObj.ObtenerItem("cantidad", id) ?? 0);

                _items.Add(new CorreccionItemFila
                {
                    CorreccionId = id,
                    Linea        = linea++,
                    ArticuloId   = articuloId,
                    Codigo       = codigo,
                    Descripcion  = desc,
                    Cantidad     = cantidad
                });
            }

            _itemsOrig = new HashSet<string>(_items.Select(x => x.CorreccionId));
            RefrescarGrid();
        }

        private void CargarParaNuevo()
        {
            Box_DocumentoC.IsEnabled = false;
            Box_Fecha.SelectedDate = DateTime.Today;
            Box_Hora.Text          = DateTime.Now.ToString("HH:mm:ss");
            var ahora = DateTime.Now;
            Box_Emision.Text = $"{ahora:d} {ahora:HH:mm:ss}";
            Box_Edicion.Text = $"{ahora:d} {ahora:HH:mm:ss}";

            // Preseleccionar el movimiento según la sección (Repuestas / Retirados)
            string tipo = string.IsNullOrEmpty(AppState.TipoCorreccion) ? "retirado" : AppState.TipoCorreccion;
            SeleccionarMovimiento(tipo);   // dispara ActualizarMotivos

            // El correlativo va por movimiento, así que se calcula recién acá, con
            // el movimiento ya resuelto (no antes, cuando todavía era el default).
            _codigoDocC = CodigoDocumento.SiguienteCorreccion(AppState.EmpresaActiva, MovimientoSeleccionado);
            Box_DocumentoC.Text = _codigoDocC;

            _items.Clear();
            _itemsOrig.Clear();
            RefrescarGrid();
        }

        // ─── Movimiento / Motivo ──────────────────────────────────────────────

        /// <summary>
        /// Movimiento del documento ("repuesta"/"retirado"). Ya no es un combo en
        /// pantalla: al crear lo fija la sección (Repuestas/Retirados) y al editar
        /// se conserva el que tiene guardado el documento.
        /// </summary>
        private string _movimiento = "retirado";

        /// <summary>
        /// Movimiento de una corrección, normalizado a "repuesta"/"retirado". Las
        /// correcciones cargadas antes del cambio de vocabulario guardaron
        /// "ingreso"/"egreso": se leen como su equivalente.
        /// </summary>
        private static string NormalizarMovimiento(string? mov) =>
            (mov ?? "").ToLower() switch
            {
                "repuesta" or "ingreso" => "repuesta",
                _                       => "retirado"
            };

        // ─── Badge + ícono según movimiento ───────────────────────────────────
        private void ActualizarBadge()
        {
            bool esIngreso = _movimiento == "repuesta";

            (BadgeEstado.Background, TxtBadgeEstado.Foreground, TxtBadgeEstado.Text) = esIngreso
                ? (new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                   new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)), "Repuesta")
                : (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                   new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)), "Retirado");

            LblIconoTipo.Text       = esIngreso ? "RE" : "RT";
            IconoBorde.Background   = esIngreso
                ? new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5))
                : new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            LblIconoTipo.Foreground = esIngreso
                ? new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46))
                : new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

            LblDocNum.Text = Box_DocumentoC.Text;
        }

        /// <summary>Repuebla la lista de motivos según el movimiento seleccionado.</summary>
        private void ActualizarMotivos(string? seleccionar = null)
        {
            if (Box_Motivo == null) return;

            string[] motivos = _movimiento == "repuesta"
                ? new[] { "error de registro", "registros omitidos" }
                : new[] { "pérdida", "merma", "hurto", "consumo interno" };

            Box_Motivo.Items.Clear();
            foreach (var m in motivos)
                Box_Motivo.Items.Add(new ComboBoxItem { Content = m });

            if (!string.IsNullOrEmpty(seleccionar))
                SeleccionarMotivo(seleccionar);
            else if (Box_Motivo.Items.Count > 0)
                Box_Motivo.SelectedIndex = 0;
        }

        private void SeleccionarMovimiento(string valor)
        {
            _movimiento = NormalizarMovimiento(valor);
            ActualizarMotivos();
        }

        private void SeleccionarMotivo(string valor)
        {
            foreach (ComboBoxItem item in Box_Motivo.Items)
            {
                if (string.Equals(item.Content?.ToString(), valor, StringComparison.OrdinalIgnoreCase))
                {
                    Box_Motivo.SelectedItem = item;
                    return;
                }
            }
            if (Box_Motivo.Items.Count > 0) Box_Motivo.SelectedIndex = 0;
        }

        // ─── Descripción de artículo ──────────────────────────────────────────
        private static string ObtenerDescripcionArticulo(string artId)
        {
            if (string.IsNullOrEmpty(artId)) return "";
            string desc    = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
            string famId   = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
            string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
            string modelo  = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
            return FuncionesComunes.UnirVariables(desc, famDesc, modelo);
        }

        // ─── Refrescar grid ───────────────────────────────────────────────────
        private void RefrescarGrid()
        {
            for (int i = 0; i < _items.Count; i++)
                _items[i].Linea = i + 1;

            var seleccionado = GridItems.SelectedItem as CorreccionItemFila;

            GridItems.ItemsSource = null;
            GridItems.ItemsSource = _items;

            if (seleccionado != null && _items.Contains(seleccionado))
                GridItems.SelectedItem = seleccionado;

            TxtTotalUnidades.Text = _items.Sum(x => x.Cantidad).ToString("N2");
            TxtUnidadesDiferentes.Text = _items
                .Where(x => !string.IsNullOrEmpty(x.ArticuloId))
                .Select(x => x.ArticuloId)
                .Distinct()
                .Count()
                .ToString();
            CargarTotalesCategoria();
        }

        // ─── Totales por categoría ────────────────────────────────────────────
        private void CargarTotalesCategoria()
        {
            int ufCat = Sql.CategoriasObj.ContarFilas;
            var categoriaIds     = new List<string>();
            var categoriaDescs   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cantPorCategoria = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i <= ufCat; i++)
            {
                var idObj = Sql.CategoriasObj.Mover(i);
                if (idObj == null) continue;
                string catId   = idObj.ToString()!;
                string catDesc = Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? catId;
                categoriaIds.Add(catId);
                categoriaDescs[catId]   = catDesc;
                cantPorCategoria[catId] = 0;
            }

            double cantOtros = 0;
            foreach (var item in _items)
            {
                if (string.IsNullOrEmpty(item.ArticuloId))
                { cantOtros += item.Cantidad; continue; }

                string catId = Sql.ArticulosObj.ObtenerItem("categoria", item.ArticuloId)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(catId) && cantPorCategoria.ContainsKey(catId))
                    cantPorCategoria[catId] += item.Cantidad;
                else
                    cantOtros += item.Cantidad;
            }

            var filas = categoriaIds
                .Select(id => new CategoriaCantFila
                {
                    Categoria = categoriaDescs[id],
                    Cantidad  = cantPorCategoria[id].ToString("N0")
                })
                .ToList();

            filas.Add(new CategoriaCantFila { Categoria = "Otros", Cantidad = cantOtros.ToString("N0") });

            // Reasignar ItemsSource reconstruye las celdas de GridCategorias y, si el
            // usuario tiene el foco puesto ahí (lo clickeó después de editar una
            // línea), se lo destruye sin restaurarlo — se preserva selección + foco
            // igual que se hace con GridItems.
            bool teniaFoco = GridCategorias.IsKeyboardFocusWithin;
            string? catSeleccionada = (GridCategorias.SelectedItem as CategoriaCantFila)?.Categoria;
            GridCategorias.ItemsSource = filas;
            var restaurar = filas.FirstOrDefault(f => f.Categoria == catSeleccionada);
            if (restaurar != null) GridCategorias.SelectedItem = restaurar;
            if (teniaFoco) GridFocusHelper.EnfocarCeldaSeleccionada(GridCategorias);
        }

        // ─── Detectar cambios ─────────────────────────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
            if (sender == Box_DocumentoC) LblDocNum.Text = Box_DocumentoC.Text;
        }

        private void Campo_DateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private void Campo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Validación de entrada ────────────────────────────────────────────
        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // ─── Buscar artículos (multi-select) ──────────────────────────────────
        // #if !VISOR: ArticulosGeneral (y su cadena Familias/Productos/
        // Categorías/Industrias/Movimientos/informe Excel, ~28 archivos) no está
        // vinculada en VisorEmpresa.csproj — innecesario porque este botón ya
        // queda deshabilitado en modo solo lectura (AplicarModoSoloLectura).
        private void BtnBuscarArticulos_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, arts =>
            {
                foreach (var art in arts)
                {
                    if (_items.Any(x => x.ArticuloId == art.Id)) continue;

                    _items.Add(new CorreccionItemFila
                    {
                        CorreccionId = "",
                        Linea        = _items.Count + 1,
                        ArticuloId   = art.Id,
                        Codigo       = art.Codigo,
                        Descripcion  = art.Descripcion,
                        Cantidad     = 1
                    });
                }
                _hayCambios = true;
                RefrescarGrid();
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

        // ─── Buscar artículo (single-select) ─────────────────────────────────
        private void BtnBuscarArticulo_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            var filaActual = GridItems.SelectedItem as CorreccionItemFila;
            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, null, art =>
            {
                CorreccionItemFila filaEnfocar;

                if (filaActual != null && _items.Contains(filaActual))
                {
                    filaActual.ArticuloId  = art.Id;
                    filaActual.Codigo      = art.Codigo;
                    filaActual.Descripcion = art.Descripcion;
                    filaEnfocar = filaActual;
                }
                else
                {
                    var nueva = new CorreccionItemFila
                    {
                        CorreccionId = "",
                        ArticuloId   = art.Id,
                        Codigo       = art.Codigo,
                        Descripcion  = art.Descripcion,
                        Cantidad     = 1
                    };
                    _items.Add(nueva);
                    filaEnfocar = nueva;
                }
                _hayCambios = true;
                RefrescarGrid();
                EnfocarColumnaCantidad(filaEnfocar);
                return true;
            }, contexto: _tituloTab, llamador: this);
#endif
        }

        // Posiciona el cursor en la celda Cantidad de la fila indicada e inicia edición
        private void EnfocarColumnaCantidad(CorreccionItemFila fila)
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

        // ─── Nueva línea vacía ────────────────────────────────────────────────
        private void BtnNuevaLinea_Click(object sender, RoutedEventArgs e)
        {
            _items.Add(new CorreccionItemFila
            {
                CorreccionId = "",
                Linea        = _items.Count + 1,
                ArticuloId   = "",
                Codigo       = "",
                Descripcion  = "",
                Cantidad     = 1
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
            if (GridItems.SelectedItem is not CorreccionItemFila fila) return;

            var copia = new CorreccionItemFila
            {
                CorreccionId = "", ArticuloId = fila.ArticuloId, Codigo = fila.Codigo,
                Descripcion = fila.Descripcion, Cantidad = fila.Cantidad
            };
            _items.Add(copia);
            _hayCambios = true;
            RefrescarGrid();
            GridItems.SelectedItem = copia;
            GridItems.ScrollIntoView(copia);
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        // ─── Eliminar línea seleccionada ──────────────────────────────────────
        private void BtnEliminarLinea_Click(object sender, RoutedEventArgs e)
        {
            if (GridItems.SelectedItem is not CorreccionItemFila fila) return;

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

        // ─── Celda editada ────────────────────────────────────────────────────
        private void GridItems_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            _hayCambios = true;

            // Cuando se confirma la edición de la columna Código → buscar artículo
            if (e.EditAction == DataGridEditAction.Commit &&
                e.Column.Header?.ToString() == "Código" &&
                e.Row.Item is CorreccionItemFila fila &&
                e.EditingElement is TextBox tb)
            {
                string codigo = tb.Text.Trim();
                string artId  = Sql.ArticulosObj.BuscarIdentificador("codigo", codigo);
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
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefrescarGrid();
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems, soloSiElFocoSigueEnLaGrilla: true);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ─── Selección en GridItems → mostrar stock ───────────────────────────
        private void GridItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => CargarStock(GridItems.SelectedItem as CorreccionItemFila);

        private void CargarStock(CorreccionItemFila? fila)
        {
            if (fila == null || string.IsNullOrEmpty(fila.ArticuloId))
            {
                GridStock.ItemsSource = null;
                return;
            }

            double stock = StockCalculator.ContarStock(fila.ArticuloId, AppState.DataFechaFinal);

            GridStock.ItemsSource = new List<CorreccionStockFila>
            {
                new CorreccionStockFila
                {
                    Codigo     = fila.Codigo,
                    Disponible = stock.ToString("N0"),
                    Stock      = stock.ToString("N0")
                }
            };
        }

        // ─── Seleccionar todo al entrar al campo Código / Cantidad ────────────
        private void GridItems_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
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
            int idx = GridItems.SelectedItem is CorreccionItemFila sel
                      ? _items.IndexOf(sel)
                      : _items.Count;
            if (idx < 0) idx = _items.Count;

            var nueva = new CorreccionItemFila
            {
                CorreccionId = "", ArticuloId = "", Codigo = "",
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

        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            bool ok = Guardar();
            if (ok) { _hayCambios = false; Cerrando?.Invoke(); }
        }

        private bool SinPestañasRelacionadas()
        {
            var c = Window.GetWindow(this) as ConsolaMovimientos;
            return c == null || c.ConfirmarCierrePestañasRelacionadas(_tituloTab);
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (!SinPestañasRelacionadas()) return;
            _hayCambios = false; Cerrando?.Invoke();
        }

        public void IntentarCerrar()
        {
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            if (!SinPestañasRelacionadas()) return;
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No) Cerrando?.Invoke();
        }

        // ─── Guardar ─────────────────────────────────────────────────────────
        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!SinPestañasRelacionadas()) return false;
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;

                return AppState.EventoFormularioC == "editar"
                    ? GuardarEditar()
                    : GuardarNuevo();
            }
            finally
            {
                _guardando = false;
                BtnGuardar.IsEnabled = true;
            }
        }

        private bool ValidarCabecera()
        {
            if (Box_Motivo.SelectedItem == null)
            {
                MessageBox.Show("Seleccione un motivo.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (_items.Count == 0)
            {
                MessageBox.Show("Agregue al menos un artículo.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private string MovimientoSeleccionado => _movimiento;

        private string MotivoSeleccionado =>
            (Box_Motivo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        // ─── Revisión del código al guardar ───────────────────────────────────
        // El código se calculó al abrir el formulario: entre ese momento y el
        // guardado, otro usuario (u otra sucursal de la misma empresa) pudo haberse
        // quedado con el número. Si ya está tomado se avisa y se usa el siguiente
        // libre, en vez de guardar un código duplicado.
        private void VerificarCodigo()
        {
            string nuevo = CodigoDocumento.VerificarCorreccion(_codigoDocC, AppState.EmpresaActiva, MovimientoSeleccionado);
            if (nuevo != _codigoDocC)
            {
                MessageBox.Show(
                    $"El código {_codigoDocC} ya estaba en uso. El documento se guardará " +
                    $"con el código {nuevo}.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Information);
                _codigoDocC = nuevo;
                Box_DocumentoC.Text = _codigoDocC;
            }
        }

        private bool GuardarNuevo()
        {
            if (!ValidarCabecera()) return false;

            try
            {
                string docId = Guid.NewGuid().ToString();
                DateTime fecha = CombinarFechaHora();

                VerificarCodigo();

                Sql.DocumentosCObj.Nuevo(docId);
                Sql.DocumentosCObj.EstablecerItem("codigo",      docId, _codigoDocC);
                Sql.DocumentosCObj.EstablecerItem("fecha",       docId, fecha);
                Sql.DocumentosCObj.EstablecerItem("sucursal",    docId, AppState.SucursalActiva);
                Sql.DocumentosCObj.EstablecerItem("movimiento",  docId, MovimientoSeleccionado);
                Sql.DocumentosCObj.EstablecerItem("motivo",      docId, MotivoSeleccionado);
                Sql.DocumentosCObj.EstablecerItem("referencia",  docId, Box_Referencia.Text.Trim());
                Sql.DocumentosCObj.EstablecerItem("observacion", docId, Box_Observacion.Text.Trim());
                Sql.DocumentosCObj.EstablecerItem("emision",     docId, DateTime.Now);
                Sql.DocumentosCObj.EstablecerItem("edicion",     docId, DateTime.Now);
                Sql.DocumentosCObj.EstablecerItem("usuario",     docId, AppState.UsuarioActivo);
                Sql.DocumentosCObj.EstablecerItem("usuarioE",    docId, AppState.UsuarioActivo);

                CrearLineas(docId);

                Sql.CorreccionesObj.OrdenarData(("documentoC", false), ("indice", false));
                Sql.DocumentosCObj.OrdenarData(("fecha", false));

                ItemCreadoId = docId;
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
            if (!ValidarCabecera()) return false;

            string docId = _idEditar;
            try
            {
                DateTime fecha = CombinarFechaHora();

                Sql.DocumentosCObj.EstablecerItem("fecha",       docId, fecha);
                Sql.DocumentosCObj.EstablecerItem("movimiento",  docId, MovimientoSeleccionado);
                Sql.DocumentosCObj.EstablecerItem("motivo",      docId, MotivoSeleccionado);
                Sql.DocumentosCObj.EstablecerItem("referencia",  docId, Box_Referencia.Text.Trim());
                Sql.DocumentosCObj.EstablecerItem("observacion", docId, Box_Observacion.Text.Trim());
                Sql.DocumentosCObj.EstablecerItem("edicion",     docId, DateTime.Now);
                Sql.DocumentosCObj.EstablecerItem("usuarioE",    docId, AppState.UsuarioActivo);

                // Guardado diferencial de líneas (inserta/actualiza/oculta)
                CrearLineas(docId);

                Sql.CorreccionesObj.OrdenarData(("documentoC", false), ("indice", false));
                Sql.DocumentosCObj.OrdenarData(("fecha", false));

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Guardado diferencial de líneas (inserta/actualiza/oculta) ──
        private void CrearLineas(string docId)
        {
            var vigentes = new HashSet<string>(
                _items.Where(x => !string.IsNullOrEmpty(x.CorreccionId)).Select(x => x.CorreccionId));

            var reservados = Sql.CorreccionesObj.IndicesNoNormales("documentoC", docId);
            foreach (var idOrig in _itemsOrig)
                if (!vigentes.Contains(idOrig))
                {
                    var ix = Sql.CorreccionesObj.ObtenerItem("indice", idOrig);
                    if (ix != null && int.TryParse(ix.ToString(), out int n)) reservados.Add(n);
                    Sql.CorreccionesObj.Eliminar(idOrig);
                }

            int next = 1;
            foreach (var item in _items)
            {
                string id;
                if (string.IsNullOrEmpty(item.CorreccionId))
                {
                    id = Guid.NewGuid().ToString();
                    Sql.CorreccionesObj.Nuevo(id);
                    Sql.CorreccionesObj.EstablecerItem("documentoC", id, docId);
                    item.CorreccionId = id;
                }
                else id = item.CorreccionId;

                while (reservados.Contains(next)) next++;
                Sql.CorreccionesObj.EstablecerItem("indice",   id, next);
                next++;
                Sql.CorreccionesObj.EstablecerItem("articulo", id, item.ArticuloId);
                Sql.CorreccionesObj.EstablecerItem("cantidad", id, item.Cantidad);
            }
            _itemsOrig = new HashSet<string>(_items.Select(x => x.CorreccionId));
        }

        // ─── Helper: combinar fecha del DatePicker y hora del TextBox ─────────
        private DateTime CombinarFechaHora()
        {
            DateTime fecha = Box_Fecha.SelectedDate ?? DateTime.Today;

            if (TimeSpan.TryParse(Box_Hora.Text, out TimeSpan hora))
                return fecha.Date + hora;

            return fecha.Date + DateTime.Now.TimeOfDay;
        }

    }

    public class CorreccionStockFila
    {
        public string Codigo     { get; set; } = "";
        public string Disponible { get; set; } = "";
        public string Stock      { get; set; } = "";
    }

    // ─── Modelo de ítem ───────────────────────────────────────────────────────
    public class CorreccionItemFila
    {
        public string CorreccionId { get; set; } = ""; // vacío = nuevo sin guardar
        public int    Linea        { get; set; }
        public string ArticuloId   { get; set; } = "";
        public string Codigo       { get; set; } = "";
        public string Descripcion  { get; set; } = "";
        public double Cantidad     { get; set; }
    }
}
