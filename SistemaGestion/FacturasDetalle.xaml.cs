using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class FacturasDetalle : UserControl
    {
        public event Action? Cerrando;

        private static SqlData Sql => SqlData.Instance;

        // object en vez de FacturasGeneral: VisorEmpresa.FacturasGeneral (su grilla
        // de solo-lectura propia) no es del mismo tipo que el de la app principal, y
        // _padre no se usa dentro de esta clase — object evita que el visor deba
        // vincular también el FacturasGeneral completo de la app principal solo para
        // satisfacer este parámetro.
        private readonly object? _padre;
        private readonly string _idEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;
        private List<FacturaItemFila> _items = new();
        private List<TransaccionFItemFila> _cobros = new();
        // IDs de las líneas existentes al abrir para editar (para el guardado diferencial).
        private HashSet<string> _itemsOrig  = new();
        private HashSet<string> _cobrosOrig = new();

        // Estado de cuenta calculado a partir del importe de las líneas vs. lo cobrado
        // (transaccionesF). No es editable por el usuario, a diferencia de "estado".
        private string _estadoC = "pendiente";

        private bool _iniciado = false;
        private readonly string _tituloTab;
        private string _codigoDocF = "";
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle).
        private bool _guardando = false;

        // ─── Confirmar edición pendiente antes de que el click llegue a su destino ──
        // Bug conocido de WPF DataGrid: si una celda de GridItems/GridCobros está en
        // edición y se hace click en OTRO control (Guardar, un TextBox de cabecera),
        // el CommitEdit del grid recién se dispara al procesar ESE MISMO click — lo
        // que descarta el click en curso y obliga a hacer uno segundo para que el
        // foco realmente llegue al control de destino. Confirmando acá, en el túnel
        // (se ejecuta ANTES de que el click llegue al control real), se evita esa carrera.
        private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Registrar a qué grilla apunta el click ANTES de confirmar (ver
            // GridFocusHelper.RegistrarClick).
            GridFocusHelper.RegistrarClick(e.OriginalSource as DependencyObject);

            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            GridCobros.CommitEdit(DataGridEditingUnit.Row, true);
        }

        /// <summary>ID del documento de factura recién creado.</summary>
        public string? ItemCreadoId { get; private set; }

        // Factura nueva creada desde "Validar pedido" (FacturasGeneral): id del
        // documentosP a copiar y las líneas ya sumadas por categoría. Vacíos en
        // una factura nueva común o al editar.
        private readonly string _desdePedidoId;
        private readonly List<FacturaLineaValidada> _lineasDesdePedido;
        // Movimiento con el que nace una factura nueva ("ingreso"/"egreso"). Lo pasa
        // la sección de FacturasGeneral desde la que se creó; vacío = ingreso.
        private readonly string _movimientoInicial;
        // Movimiento del documento ("ingreso"/"egreso"). Ya no es un combo: al crear
        // lo fija la sección (Ingresos/Egresos) o el pedido validado, y al editar se
        // conserva el que tiene guardado el documento.
        private string _movimiento = "ingreso";

        // Factura vinculada a un pedido (documentosF.relacion): los cobros y el estado
        // de cuenta viven en el pedido, y los datos generales se toman de él.
        private bool _vinculadoAPedido = false;
        // Id del documentosP relacionado. Ya no hay caja de texto: se carga con el
        // botón "Validar" (o al abrir una factura que ya lo tenía guardado).
        private string _pedidoRelacionadoId = "";

        public FacturasDetalle(object? padre = null, string idEditar = "", string tituloTab = "",
                               string desdePedidoId = "",
                               List<FacturaLineaValidada>? lineasDesdePedido = null,
                               string movimientoInicial = "")
        {
            InitializeComponent();
            _padre       = padre;
            _idEditar    = idEditar;
            _tituloTab   = tituloTab;
            _desdePedidoId     = desdePedidoId;
            _lineasDesdePedido = lineasDesdePedido ?? new List<FacturaLineaValidada>();
            _movimientoInicial = (movimientoInicial ?? "").ToLower();
            Loaded      += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            // El título lo pone ActualizarBadges según el movimiento, una vez que
            // CargarParaEditar/CargarParaNuevo dejaron _movimiento con su valor.
            if (!string.IsNullOrEmpty(_idEditar)) CargarParaEditar();
            else                                  CargarParaNuevo();

            LblDocNum.Text = Box_DocumentoF.Text;
            AplicarVinculoPedido();
            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            string codigoDocEdit = Sql.DocumentosFObj.ObtenerItem("codigo", _idEditar)?.ToString() ?? "";
            Box_DocumentoF.Text = codigoDocEdit;

            var fechaObj = Sql.DocumentosFObj.ObtenerItem("fecha", _idEditar);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : DateTime.Now;
            Box_Fecha.SelectedDate = fecha;
            Box_Hora.Text          = fecha.ToString("HH:mm:ss");

            Box_Referencia.Text  = Sql.DocumentosFObj.ObtenerItem("referencia",  _idEditar)?.ToString() ?? "";
            Box_Observacion.Text = Sql.DocumentosFObj.ObtenerItem("observacion", _idEditar)?.ToString() ?? "";

            string terceroUuid = Sql.DocumentosFObj.ObtenerItem("tercero", _idEditar)?.ToString() ?? "";
            Box_Tercero_Identificador.Text = Sql.TercerosObj.ObtenerItem("codigo", terceroUuid)?.ToString() ?? "";
            ActualizarDescripcionTercero();

            _pedidoRelacionadoId = Sql.DocumentosFObj.ObtenerItem("relacion", _idEditar)?.ToString() ?? "";
            ActualizarDescripcionPedido();

            // Las facturas anteriores a este cambio guardaron "venta"/"compra": se
            // leen como su equivalente ingreso/egreso.
            _movimiento = EsEgreso(Sql.DocumentosFObj.ObtenerItem("movimiento", _idEditar)?.ToString()) ? "egreso" : "ingreso";

            string estadoVal = Sql.DocumentosFObj.ObtenerItem("estado", _idEditar)?.ToString() ?? "pendiente";
            SeleccionarEstado(estadoVal);
            _estadoC = Sql.DocumentosFObj.ObtenerItem("estadoC", _idEditar)?.ToString() ?? "pendiente";

            var emisionObj = Sql.DocumentosFObj.ObtenerItem("emision", _idEditar);
            var edicionObj = Sql.DocumentosFObj.ObtenerItem("edicion", _idEditar);
            Box_Emision.Text = emisionObj != null ? $"{Convert.ToDateTime(emisionObj):d} {Convert.ToDateTime(emisionObj):HH:mm:ss}" : "";
            Box_Edicion.Text = edicionObj != null ? $"{Convert.ToDateTime(edicionObj):d} {Convert.ToDateTime(edicionObj):HH:mm:ss}" : "";

            // Cargar líneas de la factura
            _items.Clear();
            int linea = 1;
            int uf    = Sql.FacturasObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.FacturasObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (Sql.FacturasObj.ObtenerItem("documentoF", id)?.ToString() != _idEditar) continue;

                _items.Add(new FacturaItemFila
                {
                    FacturaId   = id,
                    Linea       = linea++,
                    Concepto    = Sql.FacturasObj.ObtenerItem("concepto", id)?.ToString() ?? "",
                    CategoriaId = Sql.FacturasObj.ObtenerItem("categoria", id)?.ToString() ?? "",
                    Importe     = Convert.ToDouble(Sql.FacturasObj.ObtenerItem("importe", id) ?? 0),
                    Monto       = Convert.ToDouble(Sql.FacturasObj.ObtenerItem("monto",   id) ?? 0)
                });
            }
            _itemsOrig = new HashSet<string>(_items.Select(x => x.FacturaId));

            // Cargar cobros (transaccionesF)
            _cobros.Clear();
            int lineaC = 1;
            int ufC    = Sql.TransaccionesFObj.ContarFilas;
            for (int i = 1; i <= ufC; i++)
            {
                var idObj = Sql.TransaccionesFObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.TransaccionesFObj.ObtenerItem("documentoF", id)?.ToString() != _idEditar) continue;

                var fObj = Sql.TransaccionesFObj.ObtenerItem("fecha", id);
                DateTime fc = fObj != null ? Convert.ToDateTime(fObj) : DateTime.Now;
                _cobros.Add(new TransaccionFItemFila
                {
                    TransaccionId = id,
                    Linea         = lineaC++,
                    FechaStr      = fc.ToString("d"),
                    HoraStr       = fc.ToString("HH:mm:ss"),
                    Descripcion   = Sql.TransaccionesFObj.ObtenerItem("descripcion", id)?.ToString() ?? "",
                    Forma         = Sql.TransaccionesFObj.ObtenerItem("forma",       id)?.ToString() ?? "efectivo",
                    Importe       = Convert.ToDouble(Sql.TransaccionesFObj.ObtenerItem("importe", id) ?? 0)
                });
            }
            _cobrosOrig = new HashSet<string>(_cobros.Select(x => x.TransaccionId));

            RefrescarGrid();
            RefrescarGridCobros();
            ActualizarTotales();
        }

        private void CargarParaNuevo()
        {
            Box_Fecha.SelectedDate = DateTime.Today;
            Box_Hora.Text          = DateTime.Now.ToString("HH:mm:ss");
            var ahora = DateTime.Now;
            Box_Emision.Text = $"{ahora:d} {ahora:HH:mm:ss}";
            Box_Edicion.Text = $"{ahora:d} {ahora:HH:mm:ss}";

            Box_Tercero_Identificador.Text = "";
            Box_Tercero_Descripcion.Text   = "";
            _pedidoRelacionadoId            = "";
            Box_PedidoRelacionado_Desc.Text = "";
            _movimiento = _movimientoInicial == "egreso" ? "egreso" : "ingreso";

            // El correlativo va por movimiento, así que se calcula recién acá, con
            // _movimiento ya resuelto (no antes, cuando todavía era el default).
            _codigoDocF = CodigoDocumento.SiguienteFactura(AppState.EmpresaActiva, MovimientoSeleccionado);
            Box_DocumentoF.Text = _codigoDocF;

            SeleccionarEstado("pendiente");
            _estadoC = "pendiente";

            _items.Clear();
            _itemsOrig.Clear();
            _cobros.Clear();
            _cobrosOrig.Clear();

            if (!string.IsNullOrEmpty(_desdePedidoId)) CopiarDesdePedido();

            RefrescarGrid();
            RefrescarGridCobros();
            ActualizarTotales();
        }

        /// <summary>
        /// Factura nueva creada desde "Validar pedido": copia los datos generales
        /// del pedido (tercero, movimiento, referencia y observación) y carga las
        /// líneas ya sumadas por categoría. La fecha queda en hoy — la factura es
        /// un documento nuevo, no hereda la fecha del pedido.
        /// </summary>
        private void CopiarDesdePedido()
        {
            CopiarGeneralesDelPedido(_desdePedidoId);

            // documentosF.relacion: el pedido que se está facturando.
            _pedidoRelacionadoId = _desdePedidoId;
            ActualizarDescripcionPedido();

            foreach (var linea in _lineasDesdePedido)
                _items.Add(new FacturaItemFila
                {
                    FacturaId   = "",   // línea nueva, todavía sin guardar
                    Concepto    = linea.Concepto,
                    CategoriaId = string.IsNullOrEmpty(linea.CategoriaId) ? PrimeraCategoriaId() : linea.CategoriaId,
                    // La suma por categoría del pedido va a "monto" (lo facturado).
                    // "importe" (lo que se debe cobrar) queda en 0: se llena por otra vía.
                    Monto       = linea.Importe
                });
        }

        // ─── Estado (manual) ──────────────────────────────────────────────────
        private void SeleccionarEstado(string valor)
        {
            foreach (ComboBoxItem item in Box_Estado.Items)
            {
                if (string.Equals(item.Content?.ToString(), valor, StringComparison.OrdinalIgnoreCase))
                {
                    Box_Estado.SelectedItem = item;
                    return;
                }
            }
            if (Box_Estado.Items.Count > 0) Box_Estado.SelectedIndex = 0;
        }

        private void Box_Estado_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
            ActualizarBadges();
        }

        // ─── Tercero ──────────────────────────────────────────────────────────
        private string ResolverTerceroId()
        {
            string cod = Box_Tercero_Identificador.Text.Trim();
            return cod == "" ? "" : Sql.TercerosObj.BuscarIdentificador("codigo", cod);
        }

        private void ActualizarDescripcionTercero()
        {
            string id = ResolverTerceroId();
            Box_Tercero_Descripcion.Text = id == "" ? "" : Sql.TercerosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
        }

        private void Box_Tercero_Identificador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cargando) return;
            ActualizarDescripcionTercero();
            _hayCambios = true;
        }

        // ─── Pedido de origen (documentosF.relacion → documentosP) ────────────
        private string ResolverPedidoRelacionadoId() => _pedidoRelacionadoId;

        /// <summary>
        /// Muestra el pedido relacionado en el encabezado: código, cliente/proveedor
        /// y fecha completa.
        /// </summary>
        private void ActualizarDescripcionPedido()
        {
            string id = _pedidoRelacionadoId;
            if (id == "") { Box_PedidoRelacionado_Desc.Text = "—"; return; }

            string codigo    = Sql.DocumentosPObj.ObtenerItem("codigo", id)?.ToString() ?? "";
            string terceroId = Sql.DocumentosPObj.ObtenerItem("tercero", id)?.ToString() ?? "";
            string desc      = Sql.TercerosObj.ObtenerItem("descripcion", terceroId)?.ToString() ?? "";
            var    fechaObj  = Sql.DocumentosPObj.ObtenerItem("fecha", id);
            string fecha     = fechaObj != null
                               ? $"{Convert.ToDateTime(fechaObj):d} {Convert.ToDateTime(fechaObj):HH:mm:ss}"
                               : "";
            Box_PedidoRelacionado_Desc.Text = string.Join("  ·  ",
                new[] { codigo, desc, fecha }.Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        /// <summary>
        /// Ajusta el formulario según haya o no un pedido en el campo "Pedido".
        /// Con la factura vinculada a un pedido, los cobros y el estado de cuenta
        /// son los del pedido (transaccionesP): acá no se muestran ni se cargan. Y
        /// los datos generales (tercero, movimiento, referencia, observación) son
        /// los del pedido: se copian de él y no se editan desde la factura.
        /// </summary>
        private void AplicarVinculoPedido()
        {
            string pedidoId = ResolverPedidoRelacionadoId();
            _vinculadoAPedido = pedidoId != "";

            var ocultarCobros = _vinculadoAPedido ? Visibility.Collapsed : Visibility.Visible;
            TabCobros.Visibility   = ocultarCobros;
            CardCobrado.Visibility = ocultarCobros;
            CardSaldo.Visibility   = ocultarCobros;

            // Columna "Importe" (lo que se tiene que cobrar): oculta cuando se validó
            // un pedido (ahí el importe lo lleva el pedido), visible y editable (default
            // 0) en una factura manual.
            ColImporteFactura.Visibility = ocultarCobros;

            if (_vinculadoAPedido && ReferenceEquals(TabControl1.SelectedItem, TabCobros))
                TabControl1.SelectedIndex = 0;

            // "Validar" y "Actualizar" solo tienen sentido con un pedido detrás.
            var botonesPedido = _vinculadoAPedido ? Visibility.Visible : Visibility.Collapsed;
            BtnValidarPedido.Visibility    = botonesPedido;
            BtnActualizarPedido.Visibility = botonesPedido;
            LblPedidoRelacionado.Visibility        = botonesPedido;
            Box_PedidoRelacionado_Desc.Visibility  = botonesPedido;

            ActualizarTotales();
        }

        /// <summary>
        // ─── Botones "Validar" / "Actualizar" (solo con un pedido detrás) ─────

        /// <summary>
        /// Reabre la pestaña "Validar pedido" con el pedido actual ya seleccionado:
        /// se puede elegir otro pedido o volver a tildar las líneas de este. Lo que
        /// se valide reemplaza los datos generales y las líneas de la factura.
        /// </summary>
        private void BtnValidarPedido_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            ValidarPedido.PedidoValidado  = null;
            ValidarPedido.LineasValidadas = null;

            // El pedido es venta/compra y la factura ingreso/egreso: una compra
            // entra (ingreso) y una venta sale (egreso).
            string movPedido = MovimientoSeleccionado == "egreso" ? "venta" : "compra";

            ValidarPedido.OpenAsDialog(Window.GetWindow(this)!, contexto: _tituloTab, llamador: this,
                movimiento: movPedido, pedidoPreseleccionado: _pedidoRelacionadoId,
                onCerrado: () =>
                {
                    string pedidoId = ValidarPedido.PedidoValidado ?? "";
                    var lineas      = ValidarPedido.LineasValidadas;
                    if (pedidoId == "" || lineas == null || lineas.Count == 0) return;

                    RehacerDesdePedido(pedidoId, lineas);
                });
#endif
        }

        /// <summary>
        /// Rehace la factura con los datos actuales del pedido ya relacionado, sin
        /// pasar por la pestaña de validación: mismos datos generales y las líneas
        /// del pedido agrupadas por categoría.
        /// </summary>
        private void BtnActualizarPedido_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            if (_pedidoRelacionadoId == "") return;

            var res = MessageBox.Show(
                "Se van a reemplazar los datos generales y todas las líneas con lo que " +
                "tiene hoy el pedido relacionado.\n\n¿Continuar?",
                "Consola", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            RehacerDesdePedido(_pedidoRelacionadoId, ValidarPedido.LineasDePedido(_pedidoRelacionadoId));
#endif
        }

        /// <summary>
        /// Reemplaza documento y líneas con los del pedido indicado.
        /// </summary>
        private void RehacerDesdePedido(string pedidoId, List<FacturaLineaValidada> lineas)
        {
            _pedidoRelacionadoId = pedidoId;
            CopiarGeneralesDelPedido(pedidoId);
            ActualizarDescripcionPedido();

            _items.Clear();
            foreach (var linea in lineas)
                _items.Add(new FacturaItemFila
                {
                    FacturaId   = "",   // línea nueva, todavía sin guardar
                    Concepto    = linea.Concepto,
                    CategoriaId = string.IsNullOrEmpty(linea.CategoriaId) ? PrimeraCategoriaId() : linea.CategoriaId,
                    // La suma por categoría del pedido va a "monto" (lo facturado).
                    Monto       = linea.Importe
                });

            _hayCambios = true;
            AplicarVinculoPedido();
            RefrescarGrid();
            ActualizarTotales();
        }

        /// <summary>
        /// Copia al encabezado los datos generales del pedido relacionado: fecha y
        /// hora, tercero, movimiento, referencia y observación. El pedido es
        /// venta/compra y la factura ingreso/egreso, por la mercadería: una compra
        /// entra (ingreso) y una venta sale (egreso).
        /// </summary>
        private void CopiarGeneralesDelPedido(string pedidoId)
        {
            bool prev = _cargando;
            _cargando = true;   // no marcar cambios por lo que se copia solo

            var fechaObj = Sql.DocumentosPObj.ObtenerItem("fecha", pedidoId);
            if (fechaObj != null)
            {
                DateTime fechaPedido = Convert.ToDateTime(fechaObj);
                Box_Fecha.SelectedDate = fechaPedido.Date;
                Box_Hora.Text          = fechaPedido.ToString("HH:mm:ss");
            }

            string terceroId = Sql.DocumentosPObj.ObtenerItem("tercero", pedidoId)?.ToString() ?? "";
            Box_Tercero_Identificador.Text = terceroId == ""
                                             ? ""
                                             : Sql.TercerosObj.ObtenerItem("codigo", terceroId)?.ToString() ?? "";
            ActualizarDescripcionTercero();

            string movimiento = Sql.DocumentosPObj.ObtenerItem("movimiento", pedidoId)?.ToString() ?? "venta";
            _movimiento = string.Equals(movimiento, "compra", StringComparison.OrdinalIgnoreCase) ? "ingreso" : "egreso";

            Box_Referencia.Text  = Sql.DocumentosPObj.ObtenerItem("referencia",  pedidoId)?.ToString() ?? "";
            Box_Observacion.Text = Sql.DocumentosPObj.ObtenerItem("observacion", pedidoId)?.ToString() ?? "";

            _cargando = prev;
        }

        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // #if !VISOR: TercerosGeneral+TercerosDetalle no están vinculados en
        // VisorEmpresa.csproj — innecesario porque este botón ya queda
        // deshabilitado en modo solo lectura (AplicarModoSoloLectura).
        private void BtnBuscarTercero_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            TercerosGeneral.TerceroSeleccionado = null;
            TercerosGeneral.OpenAsDialog(Window.GetWindow(this)!, modoSelector: true, contexto: _tituloTab, llamador: this, onCerrado: () =>
            {
                if (!string.IsNullOrEmpty(TercerosGeneral.TerceroSeleccionado))
                    Box_Tercero_Identificador.Text = TercerosGeneral.TerceroSeleccionado;
            });
#endif
        }

        // ─── Categoría: primera encontrada (default de una línea nueva) ───────
        private static string PrimeraCategoriaId()
        {
            var idObj = Sql.CategoriasObj.Mover(1);
            return idObj?.ToString() ?? "";
        }

        // ─── Categoría: lista para el ComboBox del GridItems ──────────────────
        public static List<CategoriaComboItem> CategoriasCombo
        {
            get
            {
                var lista = new List<CategoriaComboItem>();
                int uf = Sql.CategoriasObj.ContarFilas;
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.CategoriasObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    lista.Add(new CategoriaComboItem
                    {
                        Id          = id,
                        Descripcion = Sql.CategoriasObj.ObtenerItem("descripcion", id)?.ToString() ?? id
                    });
                }
                return lista;
            }
        }

        // ─── Refrescar grids ──────────────────────────────────────────────────
        private void RefrescarGrid()
        {
            for (int i = 0; i < _items.Count; i++)
                _items[i].Linea = i + 1;

            var seleccionado = GridItems.SelectedItem as FacturaItemFila;

            GridItems.ItemsSource = null;
            GridItems.ItemsSource = _items;

            if (seleccionado != null && _items.Contains(seleccionado))
                GridItems.SelectedItem = seleccionado;
        }

        private void RefrescarGridCobros()
        {
            for (int i = 0; i < _cobros.Count; i++)
                _cobros[i].Linea = i + 1;

            var seleccionado = GridCobros.SelectedItem as TransaccionFItemFila;

            GridCobros.ItemsSource = null;
            GridCobros.ItemsSource = _cobros;

            if (seleccionado != null && _cobros.Contains(seleccionado))
                GridCobros.SelectedItem = seleccionado;
        }

        // ─── Totales + estado de cuenta ───────────────────────────────────────
        private void ActualizarTotales()
        {
            double importe = _items.Sum(x => x.Importe);
            double cobrado = _cobros.Sum(x => x.Importe);
            double saldo   = importe - cobrado;

            TxtTotalImporte.Text = importe.ToString("N2");
            TxtTotalCobrado.Text = cobrado.ToString("N2");
            TxtTotalSaldo.Text   = saldo.ToString("N2");

            // Con la factura vinculada a un pedido no hay cobros propios que mirar:
            // la cuenta la lleva el pedido, así que la factura queda cancelada.
            if (_vinculadoAPedido)
            {
                _estadoC = "cancelado";
            }
            else
            {
                if (importe > 0 && cobrado == 0)           _estadoC = "pendiente";
                else if (cobrado > 0 && cobrado < importe) _estadoC = "pendiente parcial";
                else if (cobrado >= importe)               _estadoC = "cancelado";
                else                                       _estadoC = "pendiente";
            }

            ActualizarBadges();
        }

        private void ActualizarBadges()
        {
            string estado = (Box_Estado.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLower() ?? "pendiente";
            (BadgeEstado.Background, TxtBadgeEstado.Foreground, TxtBadgeEstado.Text) = estado switch
            {
                "entregado" => (new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                                new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)), "Entregado"),
                _           => (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                                new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)), "Pendiente")
            };

            (BadgeEstadoC.Background, TxtBadgeEstadoC.Foreground, TxtBadgeEstadoC.Text) = _estadoC switch
            {
                "cancelado"         => (new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                                        new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)), "Cta: Cancelado"),
                "pendiente parcial" => (new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                                        new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)), "Cta: Pendiente parcial"),
                _                   => (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                                        new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)), "Cta: Pendiente")
            };

            // Título, ícono y color del encabezado según el movimiento.
            bool esEgreso = MovimientoSeleccionado == "egreso";
            LblTitulo.Text          = esEgreso ? "Egreso de Factura" : "Ingreso de Factura";
            LblIconoTipo.Text       = esEgreso ? "EG" : "IN";
            // Ingreso en verde, egreso en rojo.
            LblIconoTipo.Foreground = esEgreso
                ? new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B))
                : new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
            IconoBorde.Background   = esEgreso
                ? new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2))
                : new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));

            LblDocNum.Text = Box_DocumentoF.Text;
        }

        // ─── Detectar cambios ─────────────────────────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private void Campo_DateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private string MovimientoSeleccionado => _movimiento;

        /// <summary>
        /// true si el movimiento guardado es un egreso. Acepta el valor viejo
        /// ("venta") además del actual ("egreso"): la mercadería de una venta sale.
        /// </summary>
        private static bool EsEgreso(string? mov)
        {
            string v = (mov ?? "").ToLower();
            return v == "egreso" || v == "venta";
        }

        // ─── Nueva línea vacía (líneas de la factura) ─────────────────────────
        private void BtnNuevaLinea_Click(object sender, RoutedEventArgs e)
        {
            _items.Add(new FacturaItemFila { FacturaId = "", Concepto = "", CategoriaId = PrimeraCategoriaId(), Monto = 0 });
            _hayCambios = true;
            RefrescarGrid();
            ActualizarTotales();
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
            if (GridItems.SelectedItem is not FacturaItemFila fila) return;

            var copia = new FacturaItemFila
            {
                FacturaId = "", Concepto = fila.Concepto, CategoriaId = fila.CategoriaId,
                Importe = fila.Importe, Monto = fila.Monto
            };
            _items.Add(copia);
            _hayCambios = true;
            RefrescarGrid();
            ActualizarTotales();
            GridItems.SelectedItem = copia;
            GridItems.ScrollIntoView(copia);
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        // ─── Eliminar línea seleccionada ──────────────────────────────────────
        private void BtnEliminarLinea_Click(object sender, RoutedEventArgs e)
        {
            if (GridItems.SelectedItem is not FacturaItemFila fila) return;

            var res = MessageBox.Show("¿Eliminar esta línea?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                int idx = _items.IndexOf(fila);
                _items.Remove(fila);
                _hayCambios = true;
                RefrescarGrid();
                ActualizarTotales();
                if (_items.Count > 0)
                {
                    var siguiente = _items[Math.Min(idx, _items.Count - 1)];
                    GridItems.SelectedItem = siguiente;
                    GridItems.ScrollIntoView(siguiente);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
            }
        }

        // ─── Insertar línea en la posición seleccionada ───────────────────────
        private void BtnInsertar_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridItems.SelectedItem is FacturaItemFila sel
                      ? _items.IndexOf(sel)
                      : _items.Count;
            if (idx < 0) idx = _items.Count;

            var nueva = new FacturaItemFila { FacturaId = "", Concepto = "", CategoriaId = PrimeraCategoriaId(), Monto = 0 };
            _items.Insert(idx, nueva);
            _hayCambios = true;
            RefrescarGrid();
            ActualizarTotales();
            if (idx < GridItems.Items.Count)
            {
                GridItems.SelectedIndex = idx;
                GridItems.ScrollIntoView(GridItems.SelectedItem);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        // ─── Celda editada (líneas) ────────────────────────────────────────────
        private void GridItems_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit &&
                e.Row.Item is FacturaItemFila fila)
            {
                string col = e.Column.Header?.ToString() ?? "";

                if (col == "Monto" && e.EditingElement is TextBox tbImp)
                {
                    if (double.TryParse(tbImp.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double monto))
                        fila.Monto = monto;
                }
                else if (col == "Importe" && e.EditingElement is TextBox tbImporte)
                {
                    if (double.TryParse(tbImporte.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double importe))
                        fila.Importe = importe;
                }
                else if (col == "Concepto" && e.EditingElement is TextBox tbConcepto)
                {
                    fila.Concepto = tbConcepto.Text;
                }
            }

            _hayCambios = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefrescarGrid();
                ActualizarTotales();
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems, soloSiElFocoSigueEnLaGrilla: true);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ─── Seleccionar todo al entrar al campo Importe ──────────────────────
        private void GridItems_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
        {
            string col = e.Column.Header?.ToString() ?? "";
            if (col != "Monto" && col != "Importe") return;
            GridFocusHelper.SeleccionarTodoEnEdicion(e.EditingElement);
            if (e.EditingElement is TextBox tb)
                FuncionesComunes.RestringirACantidad(tb);
        }

        // ─── Botones Cobros ────────────────────────────────────────────────────
        private void BtnNuevaLineaCobro_Click(object sender, RoutedEventArgs e)
        {
            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            _cobros.Add(new TransaccionFItemFila
            {
                TransaccionId = "",
                Descripcion   = $"Cobro de documento {Box_DocumentoF.Text}",
                FechaStr      = fechaDoc.ToString("d"),
                HoraStr       = DateTime.Now.ToString("HH:mm:ss"),
                Forma         = "efectivo",
                Importe       = 0
            });
            _hayCambios = true;
            RefrescarGridCobros();
            ActualizarTotales();
        }

        private void BtnInsertarCobro_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridCobros.SelectedItem is TransaccionFItemFila sel
                      ? _cobros.IndexOf(sel) : _cobros.Count;
            if (idx < 0) idx = _cobros.Count;
            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            _cobros.Insert(idx, new TransaccionFItemFila
            {
                TransaccionId = "",
                Descripcion   = $"Cobro de documento {Box_DocumentoF.Text}",
                FechaStr      = fechaDoc.ToString("d"),
                HoraStr       = DateTime.Now.ToString("HH:mm:ss"),
                Forma         = "efectivo",
                Importe       = 0
            });
            _hayCambios = true;
            RefrescarGridCobros();
            ActualizarTotales();
        }

        private void BtnEliminarLineaCobro_Click(object sender, RoutedEventArgs e)
        {
            if (GridCobros.SelectedItem is not TransaccionFItemFila fila) return;
            int idx = _cobros.IndexOf(fila);
            _cobros.Remove(fila);
            _hayCambios = true;
            RefrescarGridCobros();
            ActualizarTotales();
            if (_cobros.Count > 0)
            {
                var siguiente = _cobros[Math.Min(idx, _cobros.Count - 1)];
                GridCobros.SelectedItem = siguiente;
                GridCobros.ScrollIntoView(siguiente);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridCobros);
        }

        private void BtnCobrarDocumento_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TxtTotalSaldo.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double saldo) || saldo <= 0)
            { MessageBox.Show("La cuenta ya se canceló.", "Consola", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            _cobros.Add(new TransaccionFItemFila
            {
                TransaccionId = "",
                Descripcion   = $"Cobro de documento {Box_DocumentoF.Text}",
                FechaStr      = fechaDoc.ToString("d"),
                HoraStr       = DateTime.Now.ToString("HH:mm:ss"),
                Forma         = "efectivo",
                Importe       = saldo
            });
            _hayCambios = true;
            RefrescarGridCobros();
            ActualizarTotales();
        }

        // ─── CellEditEnding – Cobros ───────────────────────────────────────────
        private void GridCobros_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not TransaccionFItemFila fila) return;

            if (e.Column.Header?.ToString() == "Importe" && e.EditingElement is TextBox tbImp)
            {
                if (double.TryParse(tbImp.Text.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out double imp))
                    fila.Importe = imp;
            }
            else if (e.Column.Header?.ToString() == "Descripción" && e.EditingElement is TextBox tbDesc)
            {
                fila.Descripcion = tbDesc.Text;
            }

            _hayCambios = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefrescarGridCobros();
                ActualizarTotales();
                GridFocusHelper.EnfocarCeldaSeleccionada(GridCobros, soloSiElFocoSigueEnLaGrilla: true);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void GridCobros_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
        {
            string col = e.Column.Header?.ToString() ?? "";
            GridFocusHelper.SeleccionarTodoEnEdicion(e.EditingElement);
            if (col == "Importe" && e.EditingElement is TextBox tb)
                FuncionesComunes.RestringirACantidad(tb);
        }

        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            GridCobros.CommitEdit(DataGridEditingUnit.Row, true);
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
            GridCobros.CommitEdit(DataGridEditingUnit.Row, true);
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

                return !string.IsNullOrEmpty(_idEditar)
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
            if (_items.Count == 0)
            {
                MessageBox.Show("Agregue al menos una línea.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        // ─── Revisión del código al guardar ───────────────────────────────────
        // El código se calculó al abrir el formulario: entre ese momento y el
        // guardado, otro usuario (u otra sucursal de la misma empresa) pudo haberse
        // quedado con el número. Si ya está tomado se avisa y se usa el siguiente
        // libre, en vez de guardar un código duplicado.
        private void VerificarCodigo()
        {
            string nuevo = CodigoDocumento.VerificarFactura(_codigoDocF, AppState.EmpresaActiva, MovimientoSeleccionado);
            if (nuevo != _codigoDocF)
            {
                MessageBox.Show(
                    $"El código {_codigoDocF} ya estaba en uso. El documento se guardará " +
                    $"con el código {nuevo}.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Information);
                _codigoDocF = nuevo;
                Box_DocumentoF.Text = _codigoDocF;
            }
        }

        private bool GuardarNuevo()
        {
            if (!ValidarCabecera()) return false;

            try
            {
                string docId = Guid.NewGuid().ToString();
                DateTime fecha = CombinarFechaHora(Box_Fecha.SelectedDate ?? DateTime.Today, Box_Hora.Text);
                string estado = (Box_Estado.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "pendiente";

                VerificarCodigo();

                Sql.DocumentosFObj.Nuevo(docId);
                Sql.DocumentosFObj.EstablecerItem("codigo",      docId, _codigoDocF);
                Sql.DocumentosFObj.EstablecerItem("fecha",       docId, fecha);
                Sql.DocumentosFObj.EstablecerItem("sucursal",    docId, AppState.SucursalActiva);
                Sql.DocumentosFObj.EstablecerItem("tercero",     docId, ResolverTerceroId());
                Sql.DocumentosFObj.EstablecerItem("relacion",    docId, ResolverPedidoRelacionadoId());
                Sql.DocumentosFObj.EstablecerItem("movimiento",  docId, MovimientoSeleccionado);
                Sql.DocumentosFObj.EstablecerItem("referencia",  docId, Box_Referencia.Text.Trim());
                Sql.DocumentosFObj.EstablecerItem("observacion", docId, Box_Observacion.Text.Trim());
                Sql.DocumentosFObj.EstablecerItem("estado",      docId, estado);
                Sql.DocumentosFObj.EstablecerItem("estadoC",     docId, _estadoC);
                Sql.DocumentosFObj.EstablecerItem("emision",     docId, DateTime.Now);
                Sql.DocumentosFObj.EstablecerItem("edicion",     docId, DateTime.Now);
                Sql.DocumentosFObj.EstablecerItem("usuario",     docId, AppState.UsuarioActivo);
                Sql.DocumentosFObj.EstablecerItem("usuarioE",    docId, AppState.UsuarioActivo);

                GuardarLineas(docId);
                GuardarLineasCobro(docId);
                OrdenarTablas();

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
                DateTime fecha = CombinarFechaHora(Box_Fecha.SelectedDate ?? DateTime.Today, Box_Hora.Text);
                string estado = (Box_Estado.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "pendiente";

                Sql.DocumentosFObj.EstablecerItem("fecha",       docId, fecha);
                Sql.DocumentosFObj.EstablecerItem("tercero",     docId, ResolverTerceroId());
                Sql.DocumentosFObj.EstablecerItem("relacion",    docId, ResolverPedidoRelacionadoId());
                Sql.DocumentosFObj.EstablecerItem("movimiento",  docId, MovimientoSeleccionado);
                Sql.DocumentosFObj.EstablecerItem("referencia",  docId, Box_Referencia.Text.Trim());
                Sql.DocumentosFObj.EstablecerItem("observacion", docId, Box_Observacion.Text.Trim());
                Sql.DocumentosFObj.EstablecerItem("estado",      docId, estado);
                Sql.DocumentosFObj.EstablecerItem("estadoC",     docId, _estadoC);
                Sql.DocumentosFObj.EstablecerItem("edicion",     docId, DateTime.Now);
                Sql.DocumentosFObj.EstablecerItem("usuarioE",    docId, AppState.UsuarioActivo);

                // Guardado diferencial de líneas (inserta/actualiza/oculta)
                GuardarLineas(docId);
                GuardarLineasCobro(docId);
                OrdenarTablas();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Guardado diferencial de líneas (inserta/actualiza/oculta) ──
        private void GuardarLineas(string docId)
        {
            var vigentes = new HashSet<string>(
                _items.Where(x => !string.IsNullOrEmpty(x.FacturaId)).Select(x => x.FacturaId));

            var reservados = Sql.FacturasObj.IndicesNoNormales("documentoF", docId);
            foreach (var idOrig in _itemsOrig)
                if (!vigentes.Contains(idOrig))
                {
                    var ix = Sql.FacturasObj.ObtenerItem("indice", idOrig);
                    if (ix != null && int.TryParse(ix.ToString(), out int n)) reservados.Add(n);
                    Sql.FacturasObj.Eliminar(idOrig);
                }

            int next = 1;
            foreach (var item in _items)
            {
                string id;
                if (string.IsNullOrEmpty(item.FacturaId))
                {
                    id = Guid.NewGuid().ToString();
                    Sql.FacturasObj.Nuevo(id);
                    Sql.FacturasObj.EstablecerItem("documentoF", id, docId);
                    item.FacturaId = id;
                }
                else id = item.FacturaId;

                while (reservados.Contains(next)) next++;
                Sql.FacturasObj.EstablecerItem("indice",    id, next);
                next++;
                Sql.FacturasObj.EstablecerItem("concepto",  id, item.Concepto);
                Sql.FacturasObj.EstablecerItem("categoria", id, item.CategoriaId);
                Sql.FacturasObj.EstablecerItem("importe",   id, item.Importe);
                Sql.FacturasObj.EstablecerItem("monto",     id, item.Monto);
            }
            _itemsOrig = new HashSet<string>(_items.Select(x => x.FacturaId));
        }

        // ─── Guardado diferencial de cobros (transaccionesF) ──
        private void GuardarLineasCobro(string docId)
        {
            var vigentes = new HashSet<string>(
                _cobros.Where(x => !string.IsNullOrEmpty(x.TransaccionId)).Select(x => x.TransaccionId));

            var reservados = Sql.TransaccionesFObj.IndicesNoNormales("documentoF", docId);
            foreach (var idOrig in _cobrosOrig)
                if (!vigentes.Contains(idOrig))
                {
                    var ix = Sql.TransaccionesFObj.ObtenerItem("indice", idOrig);
                    if (ix != null && int.TryParse(ix.ToString(), out int n)) reservados.Add(n);
                    Sql.TransaccionesFObj.Eliminar(idOrig);
                }

            int next = 1;
            foreach (var item in _cobros)
            {
                DateTime fechaC = CombinarFechaHora(
                    DateTime.TryParse(item.FechaStr, out var fd) ? fd : DateTime.Today,
                    item.HoraStr);
                string id;
                if (string.IsNullOrEmpty(item.TransaccionId))
                {
                    id = Guid.NewGuid().ToString();
                    Sql.TransaccionesFObj.Nuevo(id);
                    Sql.TransaccionesFObj.EstablecerItem("documentoF", id, docId);
                    item.TransaccionId = id;
                }
                else id = item.TransaccionId;

                while (reservados.Contains(next)) next++;
                Sql.TransaccionesFObj.EstablecerItem("indice",      id, next);
                next++;
                Sql.TransaccionesFObj.EstablecerItem("fecha",       id, fechaC);
                Sql.TransaccionesFObj.EstablecerItem("descripcion", id, item.Descripcion);
                Sql.TransaccionesFObj.EstablecerItem("importe",     id, item.Importe);
                Sql.TransaccionesFObj.EstablecerItem("forma",       id, item.Forma);
            }
            _cobrosOrig = new HashSet<string>(_cobros.Select(x => x.TransaccionId));
        }

        private static void OrdenarTablas()
        {
            Sql.DocumentosFObj.OrdenarData(("fecha", false));
            Sql.FacturasObj.OrdenarData(("documentoF", false), ("indice", false));
            Sql.TransaccionesFObj.OrdenarData(("documentoF", false), ("indice", false));
        }

        // ─── Helper: combinar una fecha y una hora (cabecera o línea de cobro) ─
        private static DateTime CombinarFechaHora(DateTime fecha, string horaTexto)
        {
            if (TimeSpan.TryParse(horaTexto, out var ts))
                return fecha.Date + ts;
            return fecha.Date;
        }
    }

    // ─── Modelo de ítem (líneas de la factura) ─────────────────────────────────
    public class FacturaItemFila
    {
        public string FacturaId   { get; set; } = ""; // vacío = nuevo sin guardar
        public int    Linea       { get; set; }
        public string Concepto    { get; set; } = "";
        public string CategoriaId { get; set; } = "";
        // Se resuelve en vivo contra la caché de Categorias (no se guarda en la fila).
        public string CategoriaDescripcion =>
            string.IsNullOrEmpty(CategoriaId)
                ? ""
                : SqlData.Instance.CategoriasObj.ObtenerItem("descripcion", CategoriaId)?.ToString() ?? "";
        // Importe = lo que se tiene que cobrar (se llena por otra vía, no en este grid).
        public double Importe { get; set; }
        // Monto = lo que se factura (columna visible/editable; al validar un pedido
        // se carga con la suma por categoría del pedido).
        public double Monto   { get; set; }
    }

    // ─── Ítem del ComboBox de categoría en GridItems ───────────────────────────
    public class CategoriaComboItem
    {
        public string Id          { get; set; } = "";
        public string Descripcion { get; set; } = "";
    }

    // ─── Modelo de ítem (cobros / transaccionesF) ──────────────────────────────
    public class TransaccionFItemFila
    {
        public string    TransaccionId { get; set; } = ""; // vacío = nuevo sin guardar
        public int        Linea         { get; set; }
        public string     FechaStr      { get; set; } = "";
        // Derivada de FechaStr para mantener sincronizado el DatePicker de edición.
        public DateTime? FechaDate
        {
            get => DateTime.TryParse(FechaStr, out var d) ? d : null;
            set { if (value.HasValue) FechaStr = value.Value.ToString("d"); }
        }
        public string HoraStr     { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Forma       { get; set; } = "efectivo";
        public double Importe     { get; set; }
    }
}
