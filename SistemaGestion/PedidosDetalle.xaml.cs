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
    public partial class PedidosDetalle : UserControl
    {
        public event Action? Cerrando;
        private static SqlData Sql => SqlData.Instance;
        // object en vez de PedidosGeneral: VisorEmpresa.PedidosGeneral (su grilla de
        // solo-lectura propia) no es del mismo tipo que el de la app principal, y
        // _padre no se usa dentro de esta clase — object evita que el visor deba
        // vincular también el PedidosGeneral completo de la app principal solo para
        // satisfacer este parámetro.
        private readonly object? _padre;
        private readonly string _idEditar;

        private bool _cargando         = true;
        private bool _cambioDocumento  = false;
        private bool _cambioPedido     = false;
        private bool _cambioTrasaccion = false;
        private bool _cambioEntrega    = false;

        private List<PedidoItemFila>     _pedidos       = new();
        private List<TrasaccionItemFila> _trasacciones  = new();
        private List<EntregaItemFila>    _entregas      = new();

        // IDs de las líneas existentes al abrir para editar; sirven para detectar
        // bajas (lo que estaba y ya no está) y aplicar un guardado diferencial.
        private HashSet<string> _pedidosOrig      = new();
        private HashSet<string> _trasaccionesOrig = new();
        private HashSet<string> _entregasOrig     = new();

        private readonly HashSet<string> _articulosAlertados = new();
        private string                   _observaciones = "";
        private string                   _codigoDocP    = "";
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle).
        private bool _guardando = false;

        public string? DocumentoCreadoId { get; private set; }

        // Listas estáticas para ComboBox dentro de DataGrid
        public static List<string> FormasTrasaccion = new() { "cheque", "efectivo", "transferencia", "pago Qr" };

        private bool HayCambios => _cambioDocumento || _cambioPedido || _cambioTrasaccion || _cambioEntrega;

        private bool _iniciado = false;
        private readonly string _tituloTab;

        public PedidosDetalle(object? padre = null, string idEditar = "", string tituloTab = "")
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

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            string tipo = AppState.TipoMovimiento.ToLower();
            LblTitulo.Text = tipo == "venta" ? "Venta de Productos" : "Compra de Productos";

            // Etiquetas dinámicas según tipo
            if (TabCobros != null)
                TabCobros.Header = tipo == "venta" ? "Cobros" : "Pagos";
            BtnCobrarDocumento.Content = tipo == "venta" ? "Cobrar Documento" : "Pagar Documento";

            // tipoPedido = "rapido" → ocultar tabs de cobros y entregas
            string tipoPedido = AppState.TipoPedido.ToLower();
            if (tipoPedido == "rapido")
            {
                if (TabCobros              != null) TabCobros.Visibility              = Visibility.Collapsed;
                if (TabEntregas            != null) TabEntregas.Visibility            = Visibility.Collapsed;
                if (GrupoTotalesEntregas   != null) GrupoTotalesEntregas.Visibility   = Visibility.Collapsed;
                if (SeparadorTotales       != null) SeparadorTotales.Visibility       = Visibility.Collapsed;
            }

            if (AppState.EventoFormularioM == "editar")
                CargarParaEditar();
            else
                CargarParaNuevo();

            _cargando        = false;
            _cambioDocumento = false;
            _cambioPedido    = false;
            _cambioTrasaccion= false;
            _cambioEntrega   = false;
        }

        // ─── Modo nuevo ───────────────────────────────────────────────────────
        private void CargarParaNuevo()
        {
            _codigoDocP = CodigoDocumento.SiguientePedido(AppState.EmpresaActiva, AppState.TipoMovimiento);
            Box_DocumentoP.Text      = _codigoDocP;
            Box_DocumentoP.IsEnabled = false;

            string tipoPedido = AppState.TipoPedido.ToLower();
            DateTime ahora = DateTime.Now;
            bool mismoAnio = int.TryParse(AppState.PeriodoActivo, out int periodo) && periodo == ahora.Year;

            Box_Fecha.SelectedDate = mismoAnio ? ahora.Date : AppState.DataFechaFinal.Date;
            Box_Hora.Text          = mismoAnio ? ahora.ToString("HH:mm:ss") : "23:59:59";

            // Pedido nuevo sin líneas ni entregas -> nada pendiente -> estado "entregado".
            string cuentaInicial = tipoPedido == "rapido" ? "cancelado" : "pendiente";
            SeleccionarEstado("entregado");
            Box_Cuenta.Text  = cuentaInicial;
            Box_Emision.Text = $"{ahora:d} {ahora:HH:mm:ss}";
            Box_Edicion.Text = $"{ahora:d} {ahora:HH:mm:ss}";

            _observaciones = "";
            Box_Observaciones.Text = "";

            _pedidos.Clear();
            _trasacciones.Clear();
            _entregas.Clear();
            _pedidosOrig.Clear();
            _trasaccionesOrig.Clear();
            _entregasOrig.Clear();
            RefrescarGridPedidos();
            RefrescarGridTrasacciones();
            RefrescarGridEntregas();
            ActualizarTotales();
            ActualizarBadges();
        }

        // ─── Modo editar ──────────────────────────────────────────────────────
        private void CargarParaEditar()
        {
            Box_DocumentoP.IsEnabled = false;
            string codigoDocEdit = Sql.DocumentosPObj.ObtenerItem("codigo", _idEditar)?.ToString() ?? "";
            Box_DocumentoP.Text = codigoDocEdit;

            var fechaObj = Sql.DocumentosPObj.ObtenerItem("fecha", _idEditar);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : DateTime.Now;
            Box_Fecha.SelectedDate = fecha.Date;
            Box_Hora.Text = fecha.ToString("HH:mm:ss");

            string terceroUuid = Sql.DocumentosPObj.ObtenerItem("tercero", _idEditar)?.ToString() ?? "";
            Box_Tercero_Identificador.Text = Sql.TercerosObj.ObtenerItem("codigo", terceroUuid)?.ToString() ?? "";
            ActualizarDescripcionTercero();

            string estadoVal = Sql.DocumentosPObj.ObtenerItem("estado", _idEditar)?.ToString() ?? "pendiente";
            SeleccionarEstado(estadoVal);

            Box_Referencia.Text  = Sql.DocumentosPObj.ObtenerItem("referencia",  _idEditar)?.ToString() ?? "";
            _observaciones = Sql.DocumentosPObj.ObtenerItem("observacion", _idEditar)?.ToString() ?? "";
            Box_Observaciones.Text = _observaciones;
            Box_Cuenta.Text      = Sql.DocumentosPObj.ObtenerItem("estadoC",     _idEditar)?.ToString() ?? "";

            var emisionObj = Sql.DocumentosPObj.ObtenerItem("emision", _idEditar);
            var edicionObj = Sql.DocumentosPObj.ObtenerItem("edicion", _idEditar);
            Box_Emision.Text = emisionObj != null ? $"{Convert.ToDateTime(emisionObj):d} {Convert.ToDateTime(emisionObj):HH:mm:ss}" : "";
            Box_Edicion.Text = edicionObj != null ? $"{Convert.ToDateTime(edicionObj):d} {Convert.ToDateTime(edicionObj):HH:mm:ss}" : "";

            AppState.TipoPedido = Sql.DocumentosPObj.ObtenerItem("tipo", _idEditar)?.ToString() ?? "rapido";

            // Cargar pedidos
            _pedidos.Clear();
            int linea = 1;
            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() != _idEditar) continue;

                string artId   = Sql.PedidosObj.ObtenerItem("articulo",  id)?.ToString() ?? "";
                string codigo  = Sql.ArticulosObj.ObtenerItem("codigo",   artId)?.ToString() ?? "";
                double cant    = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("cantidad",  id) ?? 0);
                double importe = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("importe",   id) ?? 0);
                string tipo    = Sql.PedidosObj.ObtenerItem("tipo",      id)?.ToString() ?? "automatico";
                double precio  = cant > 0 ? importe / cant : 0;

                _pedidos.Add(new PedidoItemFila
                {
                    PedidoId    = id,
                    Linea       = linea++,
                    ArticuloId  = artId,
                    Codigo      = codigo,
                    Descripcion = ObtenerDescripcionArticulo(artId),
                    Cantidad    = cant,
                    Precio      = precio,
                    Importe     = importe,
                    Tipo        = tipo
                });
            }

            // Cargar trasacciones
            _trasacciones.Clear();
            int ufT = Sql.TrasaccionesObj.ContarFilas;
            int lineaT = 1;
            for (int i = 1; i <= ufT; i++)
            {
                var idObj = Sql.TrasaccionesObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.TrasaccionesObj.ObtenerItem("documentoP", id)?.ToString() != _idEditar) continue;

                var fObj = Sql.TrasaccionesObj.ObtenerItem("fecha", id);
                DateTime ft = fObj != null ? Convert.ToDateTime(fObj) : DateTime.Now;
                _trasacciones.Add(new TrasaccionItemFila
                {
                    TrasaccionId = id,
                    Linea        = lineaT++,
                    FechaStr     = ft.ToString("d"),
                    FechaDate    = ft.Date,
                    HoraStr      = ft.ToString("HH:mm:ss"),
                    Descripcion  = Sql.TrasaccionesObj.ObtenerItem("descripcion", id)?.ToString() ?? "",
                    Forma        = Sql.TrasaccionesObj.ObtenerItem("forma",       id)?.ToString() ?? "efectivo",
                    Importe      = Convert.ToDouble(Sql.TrasaccionesObj.ObtenerItem("importe", id) ?? 0)
                });
            }

            // Cargar entregas
            _entregas.Clear();
            int ufE = Sql.EntregasObj.ContarFilas;
            int lineaE = 1;
            for (int i = 1; i <= ufE; i++)
            {
                var idObj = Sql.EntregasObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.EntregasObj.ObtenerItem("documentoP", id)?.ToString() != _idEditar) continue;

                string artId  = Sql.EntregasObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string codigo = Sql.ArticulosObj.ObtenerItem("codigo",  artId)?.ToString() ?? "";
                var fObj = Sql.EntregasObj.ObtenerItem("fecha", id);
                DateTime fe = fObj != null ? Convert.ToDateTime(fObj) : DateTime.Now;
                _entregas.Add(new EntregaItemFila
                {
                    EntregaId   = id,
                    Linea       = lineaE++,
                    ArticuloId  = artId,
                    Codigo      = codigo,
                    Descripcion = ObtenerDescripcionArticulo(artId),
                    Cantidad    = Convert.ToDouble(Sql.EntregasObj.ObtenerItem("cantidad", id) ?? 0),
                    FechaStr    = fe.ToString("d"),
                    FechaDate   = fe.Date,
                    HoraStr     = fe.ToString("HH:mm:ss")
                });
            }

            // Snapshot de los ids existentes para el guardado diferencial
            _pedidosOrig      = new HashSet<string>(_pedidos.Select(p => p.PedidoId));
            _trasaccionesOrig = new HashSet<string>(_trasacciones.Select(t => t.TrasaccionId));
            _entregasOrig     = new HashSet<string>(_entregas.Select(e => e.EntregaId));

            RefrescarGridPedidos();
            RefrescarGridTrasacciones();
            RefrescarGridEntregas();
            ActualizarTotales();
            ActualizarBadges();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────
        private void SeleccionarEstado(string valor) => Box_Estado.Text = valor;

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

        private static string ObtenerDescripcionArticulo(string artId)
        {
            string desc    = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
            string famId   = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
            string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
            string modelo  = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
            return FuncionesComunes.UnirVariables(desc, famDesc, modelo);
        }

        private double ObtenerPrecioArticulo(string artId, out DateTime? fechaUsada)
        {
            double precio = 0;
            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            DateTime? mejorFecha = null;
            int uf = Sql.PreciosObj.ContarFilas;
            for (int p = 1; p <= uf; p++)
            {
                var pid = Sql.PreciosObj.Mover(p)?.ToString();
                if (pid == null) continue;
                if (Sql.PreciosObj.ObtenerItem("articulo", pid)?.ToString() != artId) continue;
                string docLId = Sql.PreciosObj.ObtenerItem("documentoL", pid)?.ToString() ?? "";
                if (docLId == "") continue;
                if (Sql.DocumentosLObj.ObtenerItem("estado", docLId)?.ToString() == "pendiente") continue;
                var fp = Sql.DocumentosLObj.ObtenerItem("fecha", docLId);
                if (fp == null) continue;
                DateTime fecha = Convert.ToDateTime(fp);
                if (fecha > fechaDoc) continue;
                if (mejorFecha == null || fecha > mejorFecha)
                {
                    mejorFecha = fecha;
                    precio = Convert.ToDouble(Sql.PreciosObj.ObtenerItem("precio", pid) ?? 0);
                }
            }
            fechaUsada = mejorFecha;
            return precio;
        }

        // ─── Precio de cada artículo recibido ──────────────────────────────────
        // Busca el precio más reciente aplicable de cada artículo (sin avisar
        // si la lista de precios usada no es la más nueva).
        private Dictionary<string, double> ObtenerPrecios(IEnumerable<(string ArticuloId, string Descripcion)> articulos)
        {
            var unicos = articulos
                .Where(a => !string.IsNullOrEmpty(a.ArticuloId))
                .GroupBy(a => a.ArticuloId)
                .Select(g => g.First())
                .ToList();

            var resultado = new Dictionary<string, double>();
            foreach (var (id, _) in unicos)
                resultado[id] = ObtenerPrecioArticulo(id, out _);

            return resultado;
        }

        private static DateTime CombinarFechaHora(DateTime fecha, string horaTexto)
        {
            if (TimeSpan.TryParse(horaTexto, out var ts))
                return fecha.Date + ts;
            return fecha.Date;
        }

        // ─── Refrescar grids (preserva selección) ────────────────────────────
        private void RefrescarGridPedidos()
        {
            for (int i = 0; i < _pedidos.Count; i++) _pedidos[i].Linea = i + 1;
            var sel = GridItems.SelectedItem as PedidoItemFila;
            GridItems.ItemsSource = null;
            GridItems.ItemsSource = _pedidos;
            if (sel != null && _pedidos.Contains(sel)) GridItems.SelectedItem = sel;
            CargarStockYPrecios(GridItems.SelectedItem as PedidoItemFila);
        }

        private void RefrescarGridTrasacciones()
        {
            for (int i = 0; i < _trasacciones.Count; i++) _trasacciones[i].Linea = i + 1;
            var sel = GridTrasacciones.SelectedItem as TrasaccionItemFila;
            GridTrasacciones.ItemsSource = null;
            GridTrasacciones.ItemsSource = _trasacciones;
            if (sel != null && _trasacciones.Contains(sel)) GridTrasacciones.SelectedItem = sel;
        }

        private void RefrescarGridEntregas()
        {
            for (int i = 0; i < _entregas.Count; i++) _entregas[i].Linea = i + 1;
            var sel = GridEntregas.SelectedItem as EntregaItemFila;
            GridEntregas.ItemsSource = null;
            GridEntregas.ItemsSource = _entregas;
            if (sel != null && _entregas.Contains(sel)) GridEntregas.SelectedItem = sel;
        }

        // ─── Totales ──────────────────────────────────────────────────────────
        private void ActualizarTotales()
        {
            CargarTotales();
            CargarTotalesEntregas();
            CargarTotalesDivisas();
            CargarEstadosCuenta();
            CargarTotalesCategoria();
            CargarTotalesCategoriaEntregas();
            if (AppState.TipoPedido.ToLower() == "normal")
                CargarEstados();
        }

        private void CargarTotales()
        {
            double totalUnid = 0;
            var articulosUnicos = new HashSet<string>();

            foreach (var p in _pedidos)
            {
                if (!string.IsNullOrEmpty(p.ArticuloId)) articulosUnicos.Add(p.ArticuloId);
                totalUnid += p.Cantidad;
            }

            TxtTotalUnidades.Text      = totalUnid.ToString("N0");
            TxtUnidadesDiferentes.Text = articulosUnicos.Count.ToString();
        }

        private void CargarTotalesEntregas()
        {
            double totalUnid = 0;
            var articulosUnicos = new HashSet<string>();

            foreach (var e in _entregas)
            {
                if (!string.IsNullOrEmpty(e.ArticuloId)) articulosUnicos.Add(e.ArticuloId);
                totalUnid += e.Cantidad;
            }

            TxtTotalUnidadesE.Text      = totalUnid.ToString("N0");
            TxtUnidadesDiferentesE.Text = articulosUnicos.Count.ToString();
        }

        private void CargarTotalesDivisas()
        {
            double totalImporte = _pedidos.Sum(p => p.Importe);
            double totalCuenta;

            if (AppState.TipoPedido.ToLower() == "rapido")
                totalCuenta = totalImporte;
            else
                totalCuenta = _trasacciones.Sum(t => t.Importe);

            double saldo = totalImporte - totalCuenta;

            TxtTotalImporte.Text   = totalImporte.ToString("N2");
            TxtTotalCuenta.Text    = totalCuenta.ToString("N2");
            TxtTotalSaldo.Text     = saldo.ToString("N2");
        }

        private void CargarEstadosCuenta()
        {
            if (!double.TryParse(TxtTotalImporte.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double importe)) return;
            if (!double.TryParse(TxtTotalCuenta.Text,  NumberStyles.Any, CultureInfo.CurrentCulture, out double cuenta))  return;

            string nuevaCuenta;
            if (importe > 0 && cuenta == 0)         nuevaCuenta = "pendiente";
            else if (cuenta > 0 && cuenta < importe) nuevaCuenta = "pendiente parcial";
            else if (cuenta >= importe)              nuevaCuenta = "cancelado";
            else                                     nuevaCuenta = "pendiente";

            bool prev = _cargando;
            _cargando = true;
            Box_Cuenta.Text = nuevaCuenta;
            _cargando = prev;
            ActualizarBadges();
        }

        private void ActualizarBadges()
        {
            // Badge Estado
            string estado = Box_Estado.Text.ToLower();
            (BadgeEstado.Background, TxtBadgeEstado.Foreground, TxtBadgeEstado.Text) = estado switch
            {
                "entregado"        => (new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                                       new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)),
                                       "Entregado"),
                "pendiente parcial" => (new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                                        new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                                        "Pendiente parcial"),
                _                  => (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                                       new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)),
                                       "Pendiente")
            };

            // Badge Cuenta
            string cuenta = Box_Cuenta.Text.ToLower();
            (BadgeCuenta.Background, TxtBadgeCuenta.Foreground, TxtBadgeCuenta.Text) = cuenta switch
            {
                "cancelado"         => (new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                                        new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)),
                                        "Cta: Cancelado"),
                "pendiente parcial" => (new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                                        new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                                        "Cta: Pendiente parcial"),
                _                   => (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                                        new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)),
                                        "Cta: Pendiente")
            };

            // Ícono y color del encabezado según tipo de movimiento
            string tipo = AppState.TipoMovimiento.ToLower();
            bool esCompra = tipo == "compra";
            LblIconoTipo.Text       = esCompra ? "CO" : "VE";
            LblIconoTipo.Foreground = esCompra
                ? new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46))
                : new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
            IconoBorde.Background   = esCompra
                ? new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5))
                : new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));

            // Sincronizar badge del número de documento
            LblDocBadge.Text = Box_DocumentoP.Text;
        }

        private void CargarTotalesCategoria()
            => CargarTotalesCategoriaEn(_pedidos.Select(p => (p.ArticuloId, p.Cantidad)), GridCategorias);

        private void CargarTotalesCategoriaEntregas()
            => CargarTotalesCategoriaEn(_entregas.Select(e => (e.ArticuloId, e.Cantidad)), GridCategoriasE);

        private void CargarTotalesCategoriaEn(IEnumerable<(string ArticuloId, double Cantidad)> lineas, DataGrid destino)
        {
            int ufCat = Sql.CategoriasObj.ContarFilas;
            var categoriaIds   = new List<string>();
            var categoriaDescs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cantPorCategoria = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i <= ufCat; i++)
            {
                var idObj = Sql.CategoriasObj.Mover(i);
                if (idObj == null) continue;
                string catId   = idObj.ToString()!;
                string catDesc = Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? catId;
                categoriaIds.Add(catId);
                categoriaDescs[catId]    = catDesc;
                cantPorCategoria[catId]  = 0;
            }

            double cantOtros = 0;
            foreach (var linea in lineas)
            {
                if (string.IsNullOrEmpty(linea.ArticuloId))
                { cantOtros += linea.Cantidad; continue; }

                string catId = Sql.ArticulosObj.ObtenerItem("categoria", linea.ArticuloId)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(catId) && cantPorCategoria.ContainsKey(catId))
                    cantPorCategoria[catId] += linea.Cantidad;
                else
                    cantOtros += linea.Cantidad;
            }

            var filas = categoriaIds
                .Select(id => new CategoriaCantFila
                {
                    Categoria = categoriaDescs[id],
                    Cantidad  = cantPorCategoria[id].ToString("N0")
                })
                .ToList();

            filas.Add(new CategoriaCantFila { Categoria = "Otros", Cantidad = cantOtros.ToString("N0") });
            destino.ItemsSource = filas;
        }

        private void CargarEstados()
        {
            // Solo aplica en modo "normal"
            if (AppState.TipoPedido.ToLower() != "normal") return;
            if (_pedidos.Count == 0) return;

            // Agrupar cantidades de pedidos por artículo
            var cantPedido = _pedidos
                .Where(p => !string.IsNullOrEmpty(p.ArticuloId))
                .GroupBy(p => p.ArticuloId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

            // Agrupar cantidades de entregas por artículo
            var cantEntrega = _entregas
                .Where(e => !string.IsNullOrEmpty(e.ArticuloId))
                .GroupBy(e => e.ArticuloId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

            string estado = "pendiente";
            bool hayEntregas = _entregas.Count > 0;

            foreach (var kv in cantPedido)
            {
                double entregado = cantEntrega.TryGetValue(kv.Key, out double val) ? val : 0;
                if (entregado < kv.Value && hayEntregas)
                { estado = "pendiente parcial"; break; }
                if (entregado >= kv.Value)
                    estado = "entregado";
            }

            bool prev = _cargando;
            _cargando = true;
            SeleccionarEstado(estado);
            _cargando = prev;
            ActualizarBadges();
        }

        // ─── Stock y precios del artículo seleccionado ────────────────────────
        private void CargarStockYPrecios(PedidoItemFila? fila)
        {
            GridPrecios.ItemsSource = null;
            GridStock.ItemsSource   = null;

            if (fila == null || string.IsNullOrEmpty(fila.ArticuloId)) return;

            // Precios
            var precios = new List<PrecioFila>();
            int uf = Sql.PreciosObj.ContarFilas;
            for (int i = uf; i >= 1; i--)
            {
                var pid = Sql.PreciosObj.Mover(i)?.ToString();
                if (pid == null) continue;
                if (Sql.PreciosObj.ObtenerItem("articulo", pid)?.ToString() != fila.ArticuloId) continue;
                string docLId = Sql.PreciosObj.ObtenerItem("documentoL", pid)?.ToString() ?? "";
                if (docLId == "") continue;
                if (Sql.DocumentosLObj.ObtenerItem("region", docLId)?.ToString() != AppState.RegionActiva) continue;
                if (Sql.DocumentosLObj.ObtenerItem("estado", docLId)?.ToString() == "pendiente") continue;
                double precioVal = Convert.ToDouble(Sql.PreciosObj.ObtenerItem("precio", pid) ?? 0);
                if (precioVal <= 0) continue;
                var fp = Sql.DocumentosLObj.ObtenerItem("fecha", docLId);
                precios.Add(new PrecioFila
                {
                    Codigo = fila.Codigo,
                    Fecha  = fp != null ? Convert.ToDateTime(fp).ToString("d") : "",
                    Precio = precioVal.ToString("N2")
                });
            }
            GridPrecios.ItemsSource = precios;

            // Stock
            double stockTotal = StockCalculator.ContarStock(fila.ArticuloId, AppState.DataFechaFinal);
            double stockDisp  = StockCalculator.ContarStock(fila.ArticuloId, DateTime.Now);
            GridStock.ItemsSource = new List<StockFila>
            {
                new() { Codigo = fila.Codigo, Disponible = stockDisp.ToString("N0"), Stock = stockTotal.ToString("N0") }
            };
        }

        // ─── Notificación de stock insuficiente (solo ventas, modo nuevo) ─────
        private void VerificarStockVenta()
        {
            if (AppState.TipoMovimiento.ToLower() != "venta") return;
            if (AppState.EventoFormularioM != "nuevo") return;

            var faltantes = new List<string>();

            foreach (var item in _pedidos)
            {
                if (string.IsNullOrEmpty(item.ArticuloId)) continue;
                if (_articulosAlertados.Contains(item.ArticuloId)) continue;

                double totalCant = _pedidos.Where(x => x.ArticuloId == item.ArticuloId)
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

        // ─── Eventos de campos del encabezado ─────────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _cambioDocumento = true;
        }

        private void Campo_DateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _cambioDocumento = true;
        }

        private void Campo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _cambioDocumento = true;
        }

        private void Box_Tercero_Identificador_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando)
            {
                ActualizarDescripcionTercero();
                _cambioDocumento = true;
            }
        }

        private void Box_Observaciones_TextChanged(object sender, TextChangedEventArgs e)
        {
            _observaciones = Box_Observaciones.Text;
            if (!_cargando) _cambioDocumento = true;
        }

        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // ─── Buscar tercero ───────────────────────────────────────────────────
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

        // ─── Selección en grid de pedidos ─────────────────────────────────────
        private void GridItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CargarStockYPrecios(GridItems.SelectedItem as PedidoItemFila);
        }

        // ─── Doble clic en GridPrecios → cargar precio en fila seleccionada ──
        private void GridPrecios_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridPrecios.SelectedItem is not PrecioFila precioFila) return;
            if (GridItems.SelectedItem  is not PedidoItemFila itemFila) return;
            if (!double.TryParse(precioFila.Precio, NumberStyles.Any, CultureInfo.CurrentCulture, out double precio)) return;
            itemFila.Precio  = precio;
            itemFila.Importe = Math.Round(itemFila.Cantidad * precio, 2);
            _cambioPedido = true;
            Dispatcher.BeginInvoke(() => { RefrescarGridPedidos(); ActualizarTotales(); });
        }

        // ─── CellEditEnding – Pedidos ─────────────────────────────────────────
        private void GridItems_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not PedidoItemFila fila) return;

            string col = e.Column.Header?.ToString() ?? "";

            if (col == "Código" && e.EditingElement is TextBox tbCod)
            {
                string codigo = tbCod.Text.Trim();
                string artId = Sql.ArticulosObj.BuscarIdentificador("codigo", codigo);
                if (!string.IsNullOrEmpty(artId))
                {
                    fila.ArticuloId  = artId;
                    fila.Codigo      = codigo;
                    fila.Descripcion = ObtenerDescripcionArticulo(artId);
                    double precio    = ObtenerPrecios(new[] { (artId, fila.Descripcion) })[artId];
                    fila.Precio      = precio;
                    fila.Importe     = precio * fila.Cantidad;
                    fila.Tipo        = "automatico";
                }
                else
                {
                    fila.ArticuloId  = "";
                    fila.Codigo      = codigo;
                    fila.Descripcion = string.IsNullOrEmpty(codigo) ? "" : "⚠ Artículo no encontrado";
                    fila.Precio      = 0;
                    fila.Importe     = 0;
                }
            }
            else if (col == "Cantidad" && e.EditingElement is TextBox tbCant)
            {
                if (double.TryParse(tbCant.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double cant))
                {
                    fila.Cantidad = cant;
                    fila.Importe  = cant * fila.Precio;
                }
            }
            else if (col == "Precio" && e.EditingElement is TextBox tbPrecio)
            {
                if (double.TryParse(tbPrecio.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double precio))
                {
                    fila.Precio  = precio;
                    fila.Importe = fila.Cantidad * precio;
                    fila.Tipo    = "manual";
                }
            }
            else if (col == "Importe" && e.EditingElement is TextBox tbImp)
            {
                if (double.TryParse(tbImp.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double importe))
                {
                    fila.Importe = importe;
                    fila.Precio  = fila.Cantidad > 0 ? importe / fila.Cantidad : 0;
                    fila.Tipo    = "manual";
                }
            }
            _cambioPedido = true;
            string colNombre = col;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefrescarGridPedidos();
                ActualizarTotales();
                if (colNombre == "Código" || colNombre == "Cantidad")
                    VerificarStockVenta();
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void GridItems_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            GridFocusHelper.SeleccionarTodoEnEdicion(e.EditingElement);
            if (e.Column.Header?.ToString() is "Cantidad" or "Precio" or "Importe"
                && e.EditingElement is TextBox tb)
                FuncionesComunes.RestringirACantidad(tb);
        }

        // ─── Botones Pedidos ──────────────────────────────────────────────────
        // #if !VISOR: ArticulosGeneral (y su cadena Familias/Productos/
        // Categorías/Industrias/Movimientos/informe Excel, ~28 archivos) no está
        // vinculada en VisorEmpresa.csproj — innecesario porque este botón ya
        // queda deshabilitado en modo solo lectura (AplicarModoSoloLectura).
        private void BtnImportarArticulos_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, arts =>
            {
                var precios = ObtenerPrecios(arts.Select(a => (a.Id, a.Descripcion)));
                foreach (var art in arts)
                {
                    double precio = precios[art.Id];
                    _pedidos.Add(new PedidoItemFila
                    {
                        PedidoId    = "", ArticuloId  = art.Id,
                        Codigo      = art.Codigo, Descripcion = art.Descripcion,
                        Cantidad    = 1,
                        Precio      = precio, Importe = precio, Tipo = "automatico"
                    });
                }
                _cambioPedido = true;
                RefrescarGridPedidos();
                ActualizarTotales();
                VerificarStockVenta();
                if (_pedidos.Count > 0)
                {
                    var ultimo = _pedidos[_pedidos.Count - 1];
                    GridItems.SelectedItem = ultimo;
                    GridItems.ScrollIntoView(ultimo);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
                return true;
            }, null, contexto: _tituloTab, llamador: this);
#endif
        }

        // ─── Buscar artículo individual (doble clic en ArticulosGeneral) ────────
        private void BtnBuscarArticulo_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            // Guardar referencia a la fila actualmente seleccionada
            var filaActual = GridItems.SelectedItem as PedidoItemFila;

            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, null, art =>
            {
                double precio = ObtenerPrecios(new[] { (art.Id, art.Descripcion) })[art.Id];
                PedidoItemFila filaEnfocar;

                if (filaActual != null && _pedidos.Contains(filaActual))
                {
                    // Llenar la fila seleccionada con el artículo buscado
                    filaActual.ArticuloId  = art.Id;
                    filaActual.Codigo      = art.Codigo;
                    filaActual.Descripcion = art.Descripcion;
                    filaActual.Precio      = precio;
                    filaActual.Importe     = precio * filaActual.Cantidad;
                    filaActual.Tipo        = "automatico";
                    filaEnfocar = filaActual;
                }
                else
                {
                    // Sin fila seleccionada → agregar nueva línea
                    var nueva = new PedidoItemFila
                    {
                        PedidoId    = "", ArticuloId  = art.Id,
                        Codigo      = art.Codigo, Descripcion = art.Descripcion,
                        Cantidad    = 1,
                        Precio      = precio, Importe = precio, Tipo = "automatico"
                    };
                    _pedidos.Add(nueva);
                    filaEnfocar = nueva;
                }
                _cambioPedido = true;
                RefrescarGridPedidos();
                ActualizarTotales();
                VerificarStockVenta();

                // Enfocar columna Cantidad de la fila e iniciar edición
                EnfocarColumnaCantidad(filaEnfocar);
                return true;
            }, contexto: _tituloTab, llamador: this);
#endif
        }

        // Posiciona el cursor en la celda Cantidad de la fila indicada e inicia edición
        private void EnfocarColumnaCantidad(PedidoItemFila fila)
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

        private void BtnNuevaLinea_Click(object sender, RoutedEventArgs e)
        {
            _pedidos.Add(new PedidoItemFila
            {
                PedidoId = "", ArticuloId = "", Codigo = "", Descripcion = "",
                Cantidad = 1, Precio = 0, Importe = 0
            });
            _cambioPedido = true;
            RefrescarGridPedidos();
            ActualizarTotales();
            int lastIdx = GridItems.Items.Count - 1;
            if (lastIdx >= 0)
            {
                GridItems.SelectedIndex = lastIdx;
                GridItems.ScrollIntoView(GridItems.SelectedItem);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        private void BtnDuplicarLinea_Click(object sender, RoutedEventArgs e)
        {
            if (GridItems.SelectedItem is not PedidoItemFila fila) return;

            var copia = new PedidoItemFila
            {
                PedidoId = "", ArticuloId = fila.ArticuloId, Codigo = fila.Codigo,
                Descripcion = fila.Descripcion, Cantidad = fila.Cantidad,
                Precio = fila.Precio, Importe = fila.Importe, Tipo = fila.Tipo
            };
            _pedidos.Add(copia);
            _cambioPedido = true;
            RefrescarGridPedidos();
            ActualizarTotales();
            VerificarStockVenta();
            GridItems.SelectedItem = copia;
            GridItems.ScrollIntoView(copia);
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        private void BtnInsertar_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridItems.SelectedItem is PedidoItemFila sel
                      ? _pedidos.IndexOf(sel) : _pedidos.Count;
            if (idx < 0) idx = _pedidos.Count;
            _pedidos.Insert(idx, new PedidoItemFila
            {
                PedidoId = "", ArticuloId = "", Codigo = "", Descripcion = "",
                Cantidad = 1, Precio = 0, Importe = 0
            });
            _cambioPedido = true;
            RefrescarGridPedidos();
            if (idx < GridItems.Items.Count)
            { GridItems.SelectedIndex = idx; GridItems.ScrollIntoView(GridItems.SelectedItem); }
            ActualizarTotales();
            GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
        }

        private void BtnEliminarLinea_Click(object sender, RoutedEventArgs e)
        {
            if (GridItems.SelectedItem is not PedidoItemFila fila) return;

            var res = MessageBox.Show("¿Eliminar esta línea?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                int idx = _pedidos.IndexOf(fila);
                _pedidos.Remove(fila);
                _cambioPedido = true;
                RefrescarGridPedidos();
                ActualizarTotales();
                if (_pedidos.Count > 0)
                {
                    var siguiente = _pedidos[Math.Min(idx, _pedidos.Count - 1)];
                    GridItems.SelectedItem = siguiente;
                    GridItems.ScrollIntoView(siguiente);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(GridItems);
            }
        }

        private void BtnActualizarPrecios_Click(object sender, RoutedEventArgs e)
        {
            var precios = ObtenerPrecios(_pedidos
                .Where(p => !string.IsNullOrEmpty(p.ArticuloId))
                .Select(p => (p.ArticuloId, p.Descripcion)));

            foreach (var p in _pedidos)
            {
                if (string.IsNullOrEmpty(p.ArticuloId)) continue;
                double precio = precios[p.ArticuloId];
                p.Precio  = precio;
                p.Importe = precio * p.Cantidad;
                p.Tipo    = "automatico";
            }
            _cambioPedido = true;
            RefrescarGridPedidos();
            ActualizarTotales();
        }

        // ─── CellEditEnding – Trasacciones ────────────────────────────────────
        private void GridTrasacciones_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not TrasaccionItemFila fila) return;

            if (e.Column.Header?.ToString() == "Fecha")
                fila.FechaStr = fila.FechaDate?.ToString("d") ?? fila.FechaStr;

            if (e.Column.Header?.ToString() == "Importe" && e.EditingElement is TextBox tbImp)
            {
                if (double.TryParse(tbImp.Text.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out double imp))
                    fila.Importe = imp;
            }

            if (e.Column.Header?.ToString() == "Descripción" && e.EditingElement is TextBox tbDesc)
            {
                fila.Descripcion = tbDesc.Text;
            }

            _cambioTrasaccion = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefrescarGridTrasacciones();
                CargarTotalesDivisas();
                CargarEstadosCuenta();
                GridFocusHelper.EnfocarCeldaSeleccionada(GridTrasacciones);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ─── Botones Cobros/Pagos ─────────────────────────────────────────────
        private void BtnNuevaLineaTrasaccion_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TxtTotalSaldo.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double saldo) || saldo <= 0)
            { MessageBox.Show("La cuenta ya se canceló.", "Consola", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            string tipo = AppState.TipoMovimiento.ToLower();
            _trasacciones.Add(new TrasaccionItemFila
            {
                TrasaccionId = "", Descripcion = $"{(tipo == "venta" ? "Cobro" : "Pago")} de documento {Box_DocumentoP.Text}",
                FechaStr = fechaDoc.ToString("d"), HoraStr = DateTime.Now.ToString("HH:mm:ss"),
                Forma = "efectivo", Importe = 0
            });
            _cambioTrasaccion = true;
            RefrescarGridTrasacciones();
        }

        private void BtnInsertarTrasaccion_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TxtTotalSaldo.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double saldo) || saldo <= 0)
            { MessageBox.Show("La cuenta ya se canceló.", "Consola", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            int idx = GridTrasacciones.SelectedItem is TrasaccionItemFila sel
                      ? _trasacciones.IndexOf(sel) : _trasacciones.Count;
            if (idx < 0) idx = _trasacciones.Count;
            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            string tipo = AppState.TipoMovimiento.ToLower();
            _trasacciones.Insert(idx, new TrasaccionItemFila
            {
                TrasaccionId = "", Descripcion = $"{(tipo == "venta" ? "Cobro" : "Pago")} de documento {Box_DocumentoP.Text}",
                FechaStr = fechaDoc.ToString("d"), HoraStr = DateTime.Now.ToString("HH:mm:ss"),
                Forma = "efectivo", Importe = 0
            });
            _cambioTrasaccion = true;
            RefrescarGridTrasacciones();
        }

        private void BtnEliminarLineaTrasaccion_Click(object sender, RoutedEventArgs e)
        {
            if (GridTrasacciones.SelectedItem is not TrasaccionItemFila fila) return;
            int idx = _trasacciones.IndexOf(fila);
            _trasacciones.Remove(fila);
            _cambioTrasaccion = true;
            RefrescarGridTrasacciones();
            CargarTotalesDivisas();
            CargarEstadosCuenta();
            if (_trasacciones.Count > 0)
            {
                var siguiente = _trasacciones[Math.Min(idx, _trasacciones.Count - 1)];
                GridTrasacciones.SelectedItem = siguiente;
                GridTrasacciones.ScrollIntoView(siguiente);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridTrasacciones);
        }

        private void BtnCobrarDocumento_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TxtTotalSaldo.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double saldo) || saldo <= 0)
            { MessageBox.Show("La cuenta ya se canceló.", "Consola", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            string tipo = AppState.TipoMovimiento.ToLower();
            _trasacciones.Add(new TrasaccionItemFila
            {
                TrasaccionId = "",
                Descripcion  = $"{(tipo == "venta" ? "Cobro" : "Pago")} de documento {Box_DocumentoP.Text}",
                FechaStr     = fechaDoc.ToString("d"),
                HoraStr      = DateTime.Now.ToString("HH:mm:ss"),
                Forma        = "efectivo",
                Importe      = saldo
            });
            _cambioTrasaccion = true;
            RefrescarGridTrasacciones();
            CargarTotalesDivisas();
            CargarEstadosCuenta();
        }

        // ─── CellEditEnding – Entregas ────────────────────────────────────────
        private void GridEntregas_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not EntregaItemFila fila) return;

            if (e.Column.Header?.ToString() == "Fecha")
                fila.FechaStr = fila.FechaDate?.ToString("d") ?? fila.FechaStr;

            if (e.Column.Header?.ToString() == "Cantidad" && e.EditingElement is TextBox tbCant)
            {
                if (double.TryParse(tbCant.Text.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out double cant))
                    fila.Cantidad = cant;
            }

            if (e.Column.Header?.ToString() == "Código" && e.EditingElement is TextBox tbCod)
            {
                string codigo = tbCod.Text.Trim();
                string artId = Sql.ArticulosObj.BuscarIdentificador("codigo", codigo);
                if (!string.IsNullOrEmpty(artId))
                {
                    fila.ArticuloId  = artId;
                    fila.Codigo      = codigo;
                    fila.Descripcion = ObtenerDescripcionArticulo(fila.ArticuloId);
                }
                else
                {
                    fila.ArticuloId  = "";
                    fila.Codigo      = codigo;
                    fila.Descripcion = string.IsNullOrEmpty(codigo) ? "" : "⚠ Artículo no encontrado";
                }
            }

            _cambioEntrega = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefrescarGridEntregas();
                if (AppState.TipoPedido.ToLower() == "normal") CargarEstados();
                GridFocusHelper.EnfocarCeldaSeleccionada(GridEntregas);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void GridEntregas_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            GridFocusHelper.SeleccionarTodoEnEdicion(e.EditingElement);
            if (e.Column.Header?.ToString() == "Cantidad" && e.EditingElement is TextBox tb)
                FuncionesComunes.RestringirACantidad(tb);
        }

        // ─── Botones Entregas ─────────────────────────────────────────────────
        // #if !VISOR: ArticulosGeneral (y su cadena Familias/Productos/
        // Categorías/Industrias/Movimientos/informe Excel, ~28 archivos) no está
        // vinculada en VisorEmpresa.csproj — innecesario porque este botón ya
        // queda deshabilitado en modo solo lectura (AplicarModoSoloLectura).
        private void BtnImportarArticulosEntregas_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, arts =>
            {
                DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
                foreach (var art in arts)
                {
                    _entregas.Add(new EntregaItemFila
                    {
                        EntregaId = "", ArticuloId = art.Id,
                        Codigo = art.Codigo, Descripcion = art.Descripcion,
                        Cantidad = 1,
                        FechaStr = fechaDoc.ToString("d"),
                        HoraStr  = DateTime.Now.ToString("HH:mm:ss")
                    });
                }
                _cambioEntrega = true;
                RefrescarGridEntregas();
                if (AppState.TipoPedido.ToLower() == "normal") CargarEstados();
                if (_entregas.Count > 0)
                {
                    var ultimo = _entregas[_entregas.Count - 1];
                    GridEntregas.SelectedItem = ultimo;
                    GridEntregas.ScrollIntoView(ultimo);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(GridEntregas);
                return true;
            }, null, contexto: _tituloTab, llamador: this);
#endif
        }

        private void BtnBuscarArticuloEntregas_Click(object sender, RoutedEventArgs e)
        {
#if !VISOR
            var filaActual = GridEntregas.SelectedItem as EntregaItemFila;
            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;

            ArticulosGeneral.OpenAsTab(Window.GetWindow(this)!, null, art =>
            {
                EntregaItemFila filaEnfocar;

                if (filaActual != null && _entregas.Contains(filaActual))
                {
                    filaActual.ArticuloId  = art.Id;
                    filaActual.Codigo      = art.Codigo;
                    filaActual.Descripcion = art.Descripcion;
                    filaEnfocar = filaActual;
                }
                else
                {
                    var nueva = new EntregaItemFila
                    {
                        EntregaId = "", ArticuloId = art.Id,
                        Codigo = art.Codigo, Descripcion = art.Descripcion,
                        Cantidad = 1,
                        FechaStr = fechaDoc.ToString("d"),
                        FechaDate = fechaDoc.Date,
                        HoraStr  = DateTime.Now.ToString("HH:mm:ss")
                    };
                    _entregas.Add(nueva);
                    filaEnfocar = nueva;
                }
                _cambioEntrega = true;
                RefrescarGridEntregas();
                if (AppState.TipoPedido.ToLower() == "normal") CargarEstados();

                var colCantidad = GridEntregas.Columns
                    .FirstOrDefault(c => c.Header?.ToString() == "Cantidad");
                if (colCantidad != null)
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        GridEntregas.SelectedItem = filaEnfocar;
                        GridEntregas.CurrentCell  = new DataGridCellInfo(filaEnfocar, colCantidad);
                        GridEntregas.ScrollIntoView(filaEnfocar, colCantidad);
                        GridEntregas.Focus();
                        GridEntregas.BeginEdit();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                return true;
            }, contexto: _tituloTab, llamador: this);
#endif
        }

        private void BtnNuevaLineaEntrega_Click(object sender, RoutedEventArgs e)
        {
            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            _entregas.Add(new EntregaItemFila
            {
                EntregaId = "", ArticuloId = "", Codigo = "", Descripcion = "",
                Cantidad = 1,
                FechaStr = fechaDoc.ToString("d"),
                HoraStr  = DateTime.Now.ToString("HH:mm:ss")
            });
            _cambioEntrega = true;
            RefrescarGridEntregas();
            if (_entregas.Count > 0)
            {
                var ultimo = _entregas[_entregas.Count - 1];
                GridEntregas.SelectedItem = ultimo;
                GridEntregas.ScrollIntoView(ultimo);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridEntregas);
        }

        private void BtnInsertarEntrega_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridEntregas.SelectedItem is EntregaItemFila sel
                      ? _entregas.IndexOf(sel) : _entregas.Count;
            if (idx < 0) idx = _entregas.Count;
            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;
            _entregas.Insert(idx, new EntregaItemFila
            {
                EntregaId = "", ArticuloId = "", Codigo = "", Descripcion = "",
                Cantidad = 1,
                FechaStr = fechaDoc.ToString("d"),
                HoraStr  = DateTime.Now.ToString("HH:mm:ss")
            });
            _cambioEntrega = true;
            RefrescarGridEntregas();
            if (idx < GridEntregas.Items.Count)
            {
                GridEntregas.SelectedIndex = idx;
                GridEntregas.ScrollIntoView(GridEntregas.SelectedItem);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridEntregas);
        }

        private void BtnEliminarLineaEntrega_Click(object sender, RoutedEventArgs e)
        {
            if (GridEntregas.SelectedItem is not EntregaItemFila fila) return;
            int idx = _entregas.IndexOf(fila);
            _entregas.Remove(fila);
            _cambioEntrega = true;
            RefrescarGridEntregas();
            if (AppState.TipoPedido.ToLower() == "normal") CargarEstados();
            if (_entregas.Count > 0)
            {
                var siguiente = _entregas[Math.Min(idx, _entregas.Count - 1)];
                GridEntregas.SelectedItem = siguiente;
                GridEntregas.ScrollIntoView(siguiente);
            }
            GridFocusHelper.EnfocarCeldaSeleccionada(GridEntregas);
        }

        private void BtnRegistrarEntregas_Click(object sender, RoutedEventArgs e)
        {
            // Solo modo normal: auto-llena entregas pendientes
            var cantPedido = _pedidos
                .Where(p => !string.IsNullOrEmpty(p.ArticuloId))
                .GroupBy(p => p.ArticuloId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

            var cantEntrega = _entregas
                .Where(e2 => !string.IsNullOrEmpty(e2.ArticuloId))
                .GroupBy(e2 => e2.ArticuloId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

            DateTime fechaDoc = Box_Fecha.SelectedDate ?? DateTime.Today;

            foreach (var kv in cantPedido)
            {
                double entregado = cantEntrega.TryGetValue(kv.Key, out double val) ? val : 0;
                double pendiente = kv.Value - entregado;
                if (pendiente <= 0) continue;

                string codigo = Sql.ArticulosObj.ObtenerItem("codigo", kv.Key)?.ToString() ?? "";
                _entregas.Add(new EntregaItemFila
                {
                    EntregaId   = "", ArticuloId = kv.Key, Codigo = codigo,
                    Descripcion = ObtenerDescripcionArticulo(kv.Key),
                    Cantidad    = pendiente,
                    FechaStr    = fechaDoc.ToString("d"),
                    HoraStr     = DateTime.Now.ToString("HH:mm:ss")
                });
            }

            _cambioEntrega = true;
            RefrescarGridEntregas();
            CargarEstados();
        }

        // ─── Guardar ──────────────────────────────────────────────────────────
        private bool SinPestañasRelacionadas()
        {
            var c = Window.GetWindow(this) as ConsolaMovimientos;
            return c == null || c.ConfirmarCierrePestañasRelacionadas(_tituloTab);
        }

        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!SinPestañasRelacionadas()) return false;
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;

                return AppState.EventoFormularioM == "editar"
                    ? GuardarEditar()
                    : GuardarNuevo();
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
        private string VerificarCodigo()
        {
            string nuevo = CodigoDocumento.VerificarPedido(_codigoDocP, AppState.EmpresaActiva, AppState.TipoMovimiento);
            if (nuevo != _codigoDocP)
            {
                MessageBox.Show(
                    $"El código {_codigoDocP} ya estaba en uso. El documento se guardará " +
                    $"con el código {nuevo}.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Information);
                _codigoDocP = nuevo;
                Box_DocumentoP.Text = _codigoDocP;
            }
            return _codigoDocP;
        }

        private bool GuardarNuevo()
        {
            try
            {
                string id     = Guid.NewGuid().ToString();
                string codigo = VerificarCodigo();

                DateTime fechaFinal = CombinarFechaHora(Box_Fecha.SelectedDate ?? DateTime.Today, Box_Hora.Text);
                string estado  = Box_Estado.Text;
                string cuenta  = Box_Cuenta.Text;
                string tipoPed = AppState.TipoPedido.ToLower();

                Sql.DocumentosPObj.Nuevo(id);
                Sql.DocumentosPObj.EstablecerItem("codigo",      id, codigo);
                Sql.DocumentosPObj.EstablecerItem("sucursal",    id, AppState.SucursalActiva);
                Sql.DocumentosPObj.EstablecerItem("tercero",     id, ResolverTerceroId());
                Sql.DocumentosPObj.EstablecerItem("movimiento",  id, AppState.TipoMovimiento);
                Sql.DocumentosPObj.EstablecerItem("estado",      id, estado);
                Sql.DocumentosPObj.EstablecerItem("fecha",       id, fechaFinal);
                Sql.DocumentosPObj.EstablecerItem("referencia",  id, Box_Referencia.Text.Trim());
                Sql.DocumentosPObj.EstablecerItem("observacion", id, _observaciones);
                Sql.DocumentosPObj.EstablecerItem("estadoC",     id, cuenta);
                Sql.DocumentosPObj.EstablecerItem("tipo",        id, tipoPed);
                Sql.DocumentosPObj.EstablecerItem("emision",     id, DateTime.Now);
                Sql.DocumentosPObj.EstablecerItem("edicion",     id, DateTime.Now);
                Sql.DocumentosPObj.EstablecerItem("usuario",     id, AppState.UsuarioActivo);
                Sql.DocumentosPObj.EstablecerItem("usuarioE",    id, AppState.UsuarioActivo);

                GuardarLineasPedido(id);
                GuardarLineasTrasaccion(id);
                GuardarLineasEntrega(id);
                OrdenarTablas();

                _cambioDocumento = _cambioPedido = _cambioTrasaccion = _cambioEntrega = false;
                DocumentoCreadoId = id;
                return true;
            }
            catch (Exception ex)
            { MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        }

        private bool GuardarEditar()
        {
            string docP = _idEditar;
            try
            {
                DateTime fechaFinal = CombinarFechaHora(Box_Fecha.SelectedDate ?? DateTime.Today, Box_Hora.Text);
                string estado  = Box_Estado.Text;
                string cuenta  = Box_Cuenta.Text;

                Sql.DocumentosPObj.EstablecerItem("tercero",     docP, ResolverTerceroId());
                Sql.DocumentosPObj.EstablecerItem("fecha",       docP, fechaFinal);
                Sql.DocumentosPObj.EstablecerItem("estado",      docP, estado);
                Sql.DocumentosPObj.EstablecerItem("referencia",  docP, Box_Referencia.Text.Trim());
                Sql.DocumentosPObj.EstablecerItem("observacion", docP, _observaciones);
                Sql.DocumentosPObj.EstablecerItem("estadoC",     docP, cuenta);
                Sql.DocumentosPObj.EstablecerItem("edicion",     docP, DateTime.Now);
                Sql.DocumentosPObj.EstablecerItem("usuarioE",    docP, AppState.UsuarioActivo);

                // Guardado diferencial: solo inserta/actualiza/oculta lo que cambió.
                if (_cambioPedido)     GuardarLineasPedido(docP);
                if (_cambioTrasaccion) GuardarLineasTrasaccion(docP);
                if (_cambioEntrega)    GuardarLineasEntrega(docP);
                OrdenarTablas();

                _cambioDocumento = _cambioPedido = _cambioTrasaccion = _cambioEntrega = false;
                return true;
            }
            catch (Exception ex)
            { MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        }

        // Guardado diferencial: las líneas nuevas (id vacío) se insertan, las
        // existentes se actualizan sobre su mismo id, y las que se quitaron de la
        // grilla se ocultan (estadof). Ya no se borran y recrean todas.
        private void GuardarLineasPedido(string docP)
        {
            var vigentes = new HashSet<string>(
                _pedidos.Where(p => !string.IsNullOrEmpty(p.PedidoId)).Select(p => p.PedidoId));

            // Índices reservados por filas eliminadas (no se reutilizan): las ya
            // eliminadas en SQL + las que se eliminan en este guardado.
            var reservados = Sql.PedidosObj.IndicesNoNormales("documentoP", docP);
            foreach (var idOrig in _pedidosOrig)
                if (!vigentes.Contains(idOrig))
                {
                    var ix = Sql.PedidosObj.ObtenerItem("indice", idOrig);
                    if (ix != null && int.TryParse(ix.ToString(), out int n)) reservados.Add(n);
                    Sql.PedidosObj.Eliminar(idOrig);
                }

            int next = 1;
            foreach (var item in _pedidos)
            {
                string id;
                if (string.IsNullOrEmpty(item.PedidoId))
                {
                    id = Guid.NewGuid().ToString();
                    Sql.PedidosObj.Nuevo(id);
                    Sql.PedidosObj.EstablecerItem("documentoP", id, docP);
                    item.PedidoId = id;
                }
                else id = item.PedidoId;

                while (reservados.Contains(next)) next++;
                Sql.PedidosObj.EstablecerItem("indice",   id, next);
                next++;
                Sql.PedidosObj.EstablecerItem("articulo", id, item.ArticuloId);
                Sql.PedidosObj.EstablecerItem("cantidad", id, item.Cantidad);
                Sql.PedidosObj.EstablecerItem("importe",  id, item.Importe);
                Sql.PedidosObj.EstablecerItem("tipo",     id, item.Tipo);
            }
            _pedidosOrig = new HashSet<string>(_pedidos.Select(p => p.PedidoId));
        }

        private void GuardarLineasTrasaccion(string docP)
        {
            var vigentes = new HashSet<string>(
                _trasacciones.Where(t => !string.IsNullOrEmpty(t.TrasaccionId)).Select(t => t.TrasaccionId));

            var reservados = Sql.TrasaccionesObj.IndicesNoNormales("documentoP", docP);
            foreach (var idOrig in _trasaccionesOrig)
                if (!vigentes.Contains(idOrig))
                {
                    var ix = Sql.TrasaccionesObj.ObtenerItem("indice", idOrig);
                    if (ix != null && int.TryParse(ix.ToString(), out int n)) reservados.Add(n);
                    Sql.TrasaccionesObj.Eliminar(idOrig);
                }

            int next = 1;
            foreach (var item in _trasacciones)
            {
                DateTime fechaT = CombinarFechaHora(
                    DateTime.TryParse(item.FechaStr, out var fd) ? fd : DateTime.Today,
                    item.HoraStr);
                string id;
                if (string.IsNullOrEmpty(item.TrasaccionId))
                {
                    id = Guid.NewGuid().ToString();
                    Sql.TrasaccionesObj.Nuevo(id);
                    Sql.TrasaccionesObj.EstablecerItem("documentoP", id, docP);
                    item.TrasaccionId = id;
                }
                else id = item.TrasaccionId;

                while (reservados.Contains(next)) next++;
                Sql.TrasaccionesObj.EstablecerItem("indice",      id, next);
                next++;
                Sql.TrasaccionesObj.EstablecerItem("fecha",       id, fechaT);
                Sql.TrasaccionesObj.EstablecerItem("descripcion", id, item.Descripcion);
                Sql.TrasaccionesObj.EstablecerItem("importe",     id, item.Importe);
                Sql.TrasaccionesObj.EstablecerItem("forma",       id, item.Forma);
            }
            _trasaccionesOrig = new HashSet<string>(_trasacciones.Select(t => t.TrasaccionId));
        }

        private void GuardarLineasEntrega(string docP)
        {
            var vigentes = new HashSet<string>(
                _entregas.Where(e => !string.IsNullOrEmpty(e.EntregaId)).Select(e => e.EntregaId));

            var reservados = Sql.EntregasObj.IndicesNoNormales("documentoP", docP);
            foreach (var idOrig in _entregasOrig)
                if (!vigentes.Contains(idOrig))
                {
                    var ix = Sql.EntregasObj.ObtenerItem("indice", idOrig);
                    if (ix != null && int.TryParse(ix.ToString(), out int n)) reservados.Add(n);
                    Sql.EntregasObj.Eliminar(idOrig);
                }

            int next = 1;
            foreach (var item in _entregas)
            {
                DateTime fechaE = CombinarFechaHora(
                    DateTime.TryParse(item.FechaStr, out var fd) ? fd : DateTime.Today,
                    item.HoraStr);
                string id;
                if (string.IsNullOrEmpty(item.EntregaId))
                {
                    id = Guid.NewGuid().ToString();
                    Sql.EntregasObj.Nuevo(id);
                    Sql.EntregasObj.EstablecerItem("documentoP", id, docP);
                    item.EntregaId = id;
                }
                else id = item.EntregaId;

                while (reservados.Contains(next)) next++;
                Sql.EntregasObj.EstablecerItem("indice",   id, next);
                next++;
                Sql.EntregasObj.EstablecerItem("articulo", id, item.ArticuloId);
                Sql.EntregasObj.EstablecerItem("cantidad", id, item.Cantidad);
                Sql.EntregasObj.EstablecerItem("fecha",    id, fechaE);
            }
            _entregasOrig = new HashSet<string>(_entregas.Select(e => e.EntregaId));
        }

        private static void OrdenarTablas()
        {
            Sql.DocumentosPObj.OrdenarData(("fecha", false));
            Sql.PedidosObj.OrdenarData(("documentoP", false), ("indice", false));
            Sql.TrasaccionesObj.OrdenarData(("documentoP", false), ("indice", false));
            Sql.EntregasObj.OrdenarData(("documentoP", false), ("indice", false));
        }

        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            CommitEdicionesPendientes();
            if (Guardar()) Cerrando?.Invoke();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (!SinPestañasRelacionadas()) return;
            _cambioDocumento = _cambioPedido = _cambioTrasaccion = _cambioEntrega = false;
            Cerrando?.Invoke();
        }

        // Confirma cualquier celda en edición en los grids editables
        private void CommitEdicionesPendientes()
        {
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            GridTrasacciones.CommitEdit(DataGridEditingUnit.Row, true);
            GridEntregas.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // ─── Confirmar edición pendiente antes de que el click llegue a su destino ──
        // Bug conocido de WPF DataGrid: si una celda de un grid editable está en
        // edición y se hace click en OTRO control (Guardar, un TextBox de cabecera),
        // el CommitEdit del grid recién se dispara al procesar ESE MISMO click — lo
        // que descarta el click en curso y obliga a hacer uno segundo para que el
        // foco realmente llegue al control de destino. Confirmando acá, en el túnel
        // (se ejecuta ANTES de que el click llegue al control real), se evita esa carrera.
        private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => CommitEdicionesPendientes();

        // Llamado por el botón X del overlay para verificar cambios antes de cerrar
        public void IntentarCerrar()
        {
            CommitEdicionesPendientes();
            if (!SinPestañasRelacionadas()) return;
            if (!HayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No) Cerrando?.Invoke();
        }
    }

    // ─── Modelos de fila ──────────────────────────────────────────────────────
    public class PedidoItemFila
    {
        public string PedidoId    { get; set; } = "";
        public int    Linea       { get; set; }
        public string ArticuloId  { get; set; } = "";
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public double Cantidad    { get; set; }
        public double Precio      { get; set; }
        public double Importe     { get; set; }
        public string Tipo        { get; set; } = "automatico";
    }

    public class TrasaccionItemFila
    {
        public string    TrasaccionId { get; set; } = "";
        public int       Linea        { get; set; }
        public string    FechaStr     { get; set; } = "";
        // Derivada de FechaStr para mantener sincronizado el DatePicker de edición.
        public DateTime? FechaDate
        {
            get => DateTime.TryParse(FechaStr, out var d) ? d : null;
            set { if (value.HasValue) FechaStr = value.Value.ToString("d"); }
        }
        public string    HoraStr      { get; set; } = "";
        public string    Descripcion  { get; set; } = "";
        public string    Forma        { get; set; } = "efectivo";
        public double    Importe      { get; set; }
    }

    public class EntregaItemFila
    {
        public string    EntregaId   { get; set; } = "";
        public int       Linea       { get; set; }
        public string    ArticuloId  { get; set; } = "";
        public string    Codigo      { get; set; } = "";
        public string    Descripcion { get; set; } = "";
        public double    Cantidad    { get; set; }
        public string    FechaStr    { get; set; } = "";
        // Derivada de FechaStr para mantener sincronizado el DatePicker de edición.
        public DateTime? FechaDate
        {
            get => DateTime.TryParse(FechaStr, out var d) ? d : null;
            set { if (value.HasValue) FechaStr = value.Value.ToString("d"); }
        }
        public string    HoraStr     { get; set; } = "";
    }

    public class PrecioFila
    {
        public string Codigo { get; set; } = "";
        public string Fecha  { get; set; } = "";
        public string Precio { get; set; } = "";
    }

    public class StockFila
    {
        public string Codigo     { get; set; } = "";
        public string Disponible { get; set; } = "";
        public string Stock      { get; set; } = "";
    }

    public class CategoriaCantFila
    {
        public string Categoria { get; set; } = "";
        public string Cantidad  { get; set; } = "";
    }
}
