using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SistemaGestion.Data;

namespace SistemaGestion
{
    /// <summary>
    /// Panel Dashboard: totales en CANTIDAD (unidades) de la sucursal y período
    /// activos. Lee del caché ya filtrado (PedidosObj / TraspasosObj). El
    /// segmentador (Ventas / Compras / Entradas / Salidas) es de selección única
    /// y filtra los gráficos, la tabla y la tarjeta de total; las cantidades de
    /// cada tipo se muestran siempre en sus chips.
    /// El gráfico por mes es apilado por categoría (cada categoría con su color).
    /// </summary>
    public partial class DashboardGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private bool _iniciado;

        private readonly ObservableCollection<ArticuloCantFila> _articulos = new();

        // Cache artículo → (categoría desc, familia desc, descripción, código) para no repetir lookups.
        private readonly Dictionary<string, (string cat, string fam, string desc, string cod)> _articuloMeta = new();

        // Color asignado a cada categoría en el render actual.
        private readonly Dictionary<string, Brush> _colorCat = new();

        private double _alturaGrafico = 280;        // px de la zona de barras (meses); dinámico por resolución
        private const double AnchoBarra    = 300;   // px de la barra apilada de familia (fijo)
        private const double ReservaEtiquetaY = 16; // px reservados arriba de la barra más alta para su etiqueta de valor

        // Cache del último cálculo del gráfico por mes (para re-render al cambiar el alto disponible).
        private double[]? _porMesCache;
        private Dictionary<int, Dictionary<string, double>>? _porMesCatCache;

        // Paleta de colores para categorías (se cicla si hay más categorías que colores).
        private static readonly Color[] Paleta =
        {
            Color.FromRgb(0x4A,0x6F,0xE3), Color.FromRgb(0x2E,0x7D,0x32),
            Color.FromRgb(0xFB,0x8C,0x00), Color.FromRgb(0xE5,0x39,0x35),
            Color.FromRgb(0x8E,0x24,0xAA), Color.FromRgb(0x00,0xAC,0xC1),
            Color.FromRgb(0xFD,0xD8,0x35), Color.FromRgb(0x6D,0x4C,0x41),
            Color.FromRgb(0xEC,0x40,0x7A), Color.FromRgb(0x7C,0xB3,0x42),
            Color.FromRgb(0x5C,0x6B,0xC0), Color.FromRgb(0x26,0xA6,0x9A),
        };

        public DashboardGeneral()
        {
            InitializeComponent();
            GridArticulos.ItemsSource = _articulos;
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; Recalcular(); };
        }

        // ─── Eventos ──────────────────────────────────────────────────────────
        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            AppState.ActualizarProductos();
            _articuloMeta.Clear();
            Recalcular();
        }

        private void Segmentador_Changed(object sender, RoutedEventArgs e)
        {
            // RadioButton dispara Checked al aplicar IsChecked por defecto antes de
            // que el panel esté listo; protegemos con _iniciado.
            if (!_iniciado) return;
            Recalcular();
        }

        // ─── Cálculo principal ────────────────────────────────────────────────
        private void Recalcular()
        {
            bool incVenta   = TglVentas.IsChecked == true;
            bool incCompra  = TglCompras.IsChecked == true;
            bool incEntrada = TglEntradas.IsChecked == true;
            bool incSalida  = TglSalidas.IsChecked == true;

            string suc = AppState.SucursalActiva;

            // Totales por tipo (siempre, independientes del segmentador).
            double totVenta = 0, totCompra = 0, totEntrada = 0, totSalida = 0;

            // Estructuras filtradas por el segmentador activo.
            var porMes       = new double[13];                          // total del mes (1..12)
            var porMesCat    = new Dictionary<int, Dictionary<string, double>>();
            var porCategoria = new Dictionary<string, double>();
            var porFamiliaCat= new Dictionary<string, Dictionary<string, double>>();
            var porArticulo  = new Dictionary<string, double>();
            var articulosSet = new HashSet<string>();
            var documentos   = new HashSet<string>();
            double totalActivos = 0;

            void Acumular(bool activo, double cant, DateTime? fecha, string artId, string docId)
            {
                if (!activo || cant == 0) return;
                totalActivos += cant;

                var (cat, fam, _, _) = MetaArticulo(artId);

                if (fecha.HasValue && fecha.Value.Month is >= 1 and <= 12)
                {
                    int mm = fecha.Value.Month;
                    porMes[mm] += cant;
                    if (!porMesCat.TryGetValue(mm, out var d)) { d = new(); porMesCat[mm] = d; }
                    d[cat] = d.GetValueOrDefault(cat) + cant;
                }

                porCategoria[cat] = porCategoria.GetValueOrDefault(cat) + cant;

                if (!porFamiliaCat.TryGetValue(fam, out var fc)) { fc = new(); porFamiliaCat[fam] = fc; }
                fc[cat] = fc.GetValueOrDefault(cat) + cant;

                if (!string.IsNullOrEmpty(artId))
                {
                    articulosSet.Add(artId);
                    porArticulo[artId] = porArticulo.GetValueOrDefault(artId) + cant;
                }
                if (!string.IsNullOrEmpty(docId)) documentos.Add(docId);
            }

            // ── Pedidos (ventas / compras) ───────────────────────────────────
            int ufP = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= ufP; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string docP = Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() ?? "";
                if (docP == "") continue;
                string mov  = (Sql.DocumentosPObj.ObtenerItem("movimiento", docP)?.ToString() ?? "")
                              .Trim().ToLowerInvariant();
                double cant = ConvertirCantidad(Sql.PedidosObj.ObtenerItem("cantidad", id));
                string art  = Sql.PedidosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                DateTime? fecha = ConvertirFecha(Sql.DocumentosPObj.ObtenerItem("fecha", docP));

                if (mov == "venta")
                {
                    totVenta += cant;
                    Acumular(incVenta, cant, fecha, art, docP);
                }
                else if (mov == "compra")
                {
                    totCompra += cant;
                    Acumular(incCompra, cant, fecha, art, docP);
                }
            }

            // ── Traspasos (entradas / salidas) ───────────────────────────────
            int ufT = Sql.TraspasosObj.ContarFilas;
            for (int i = 1; i <= ufT; i++)
            {
                var idObj = Sql.TraspasosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string docT = Sql.TraspasosObj.ObtenerItem("documentoT", id)?.ToString() ?? "";
                if (docT == "") continue;
                string origen  = Sql.DocumentosTObj.ObtenerItem("origen",  docT)?.ToString() ?? "";
                string destino = Sql.DocumentosTObj.ObtenerItem("destino", docT)?.ToString() ?? "";
                double cant = ConvertirCantidad(Sql.TraspasosObj.ObtenerItem("cantidad", id));
                string art  = Sql.TraspasosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                DateTime? fecha = ConvertirFecha(Sql.DocumentosTObj.ObtenerItem("fecha", docT));

                // Salida: la sucursal activa es el origen. Entrada: es el destino.
                if (origen == suc)
                {
                    totSalida += cant;
                    Acumular(incSalida, cant, fecha, art, docT);
                }
                else if (destino == suc)
                {
                    totEntrada += cant;
                    Acumular(incEntrada, cant, fecha, art, docT);
                }
            }

            // ── Asignar colores por categoría (orden estable por cantidad) ────
            AsignarColores(porCategoria);

            // ── Actualizar UI ────────────────────────────────────────────────
            LblVentasVal.Text   = Fmt(totVenta);
            LblComprasVal.Text  = Fmt(totCompra);
            LblEntradasVal.Text = Fmt(totEntrada);
            LblSalidasVal.Text  = Fmt(totSalida);

            LblMovimientos.Text  = Fmt(documentos.Count);
            LblArticulos.Text    = Fmt(articulosSet.Count);

            _porMesCache    = porMes;
            _porMesCatCache = porMesCat;

            RenderLeyenda(porCategoria);
            RenderMeses(porMes, porMesCat);
            RenderCategorias(porCategoria);
            RenderFamilias(porFamiliaCat);
            RenderArticulos(porArticulo, totalActivos);
        }

        // ─── Colores por categoría ────────────────────────────────────────────
        private void AsignarColores(Dictionary<string, double> porCategoria)
        {
            _colorCat.Clear();
            var cats = porCategoria.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            for (int i = 0; i < cats.Count; i++)
                _colorCat[cats[i]] = new SolidColorBrush(Paleta[i % Paleta.Length]);
        }

        private Brush ColorDe(string cat) =>
            _colorCat.TryGetValue(cat, out var b) ? b : new SolidColorBrush(Colors.Gray);

        // ─── Leyenda de categorías ────────────────────────────────────────────
        private void RenderLeyenda(Dictionary<string, double> porCategoria)
        {
            PanelLeyenda.Children.Clear();
            foreach (var cat in porCategoria.Where(kv => kv.Value > 0)
                                            .OrderByDescending(kv => kv.Value)
                                            .Select(kv => kv.Key))
            {
                var chip = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 14, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                chip.Children.Add(new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                    Background = ColorDe(cat),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                chip.Children.Add(new TextBlock
                {
                    Text = cat, FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Tema("ThemeTexto", Color.FromRgb(0x20, 0x20, 0x20))
                });
                PanelLeyenda.Children.Add(chip);
            }
        }

        // ─── Gráfico de barras agrupadas por mes (una barra por categoría) ──
        // Guardamos los datos del último render para redibujar la cuadrícula
        // cuando cambia el tamaño del área de plot.
        private double _niceMax;

        private void RenderMeses(double[] porMes, Dictionary<int, Dictionary<string, double>> porMesCat)
        {
            PanelMeses.Children.Clear();
            PanelEjeY.Children.Clear();
            PanelGrid.Children.Clear();

            // Escala = máximo valor (categoría, mes), ya que las barras son por categoría.
            double max = porMesCat.Values.SelectMany(d => d.Values).DefaultIfEmpty(0).Max();

            if (max <= 0)
            {
                LblMesesVacio.Visibility = Visibility.Visible;
                ZonaMeses.Visibility     = Visibility.Collapsed;
                return;
            }
            LblMesesVacio.Visibility = Visibility.Collapsed;
            ZonaMeses.Visibility     = Visibility.Visible;

            _niceMax = NiceCeil(max);
            PanelEjeY.Height = _alturaGrafico + ReservaEtiquetaY;
            RenderEjeY();

            // Orden estable de categorías (por total) para agrupar igual en cada mes.
            var ordenCats = _colorCat.Keys.ToList();

            for (int m = 1; m <= 12; m++)
            {
                double totalMes = porMes[m];
                if (totalMes <= 0) continue;   // solo meses con registros
                porMesCat.TryGetValue(m, out var catsMes);

                var columna = new StackPanel
                {
                    Margin = new Thickness(6, 0, 6, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };

                // Zona de altura fija → barras alineadas al mismo eje base. Se reserva
                // espacio extra arriba (ReservaEtiquetaY) para que la etiqueta de valor
                // de la barra más alta no quede recortada por el ScrollViewer horizontal
                // (que tiene el scroll vertical deshabilitado).
                var zona  = new Grid { Height = _alturaGrafico + ReservaEtiquetaY };
                var grupo = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                if (catsMes != null)
                {
                    foreach (var cat in ordenCats)
                    {
                        double val = catsMes.GetValueOrDefault(cat);
                        if (val <= 0) continue;

                        // Columna [valor encima][barra], alineada al fondo: el valor
                        // queda pegado justo arriba de la barra y sube/baja con ella.
                        var barCol = new StackPanel
                        {
                            Margin = new Thickness(2, 0, 2, 0),
                            VerticalAlignment = VerticalAlignment.Bottom,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        barCol.Children.Add(new TextBlock
                        {
                            Text = Fmt(val),
                            FontSize = 9, FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Foreground = Tema("ThemeTexto", Color.FromRgb(0x20, 0x20, 0x20)),
                            Margin = new Thickness(0, 0, 0, 2)
                        });
                        barCol.Children.Add(new Border
                        {
                            Width = 20,
                            Height = Math.Max(2, val / _niceMax * _alturaGrafico),
                            Background = ColorDe(cat),
                            CornerRadius = new CornerRadius(3, 3, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        grupo.Children.Add(barCol);
                    }
                }
                zona.Children.Add(grupo);
                columna.Children.Add(zona);

                // Título del mes
                columna.Children.Add(new TextBlock
                {
                    Text = NombreMes(m),
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = Tema("ThemeTexto", Color.FromRgb(0x20, 0x20, 0x20)),
                    Margin = new Thickness(0, 4, 0, 0)
                });

                // Total del mes, DEBAJO del título del mes
                columna.Children.Add(new TextBlock
                {
                    Text = totalMes > 0 ? Fmt(totalMes) : "",
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = Tema("ThemeTextoSec", Color.FromRgb(0x7A, 0x7A, 0x7A))
                });

                PanelMeses.Children.Add(columna);
            }

            // Redibujar la cuadrícula con el ancho real una vez completado el layout.
            Dispatcher.BeginInvoke(new Action(DibujarCuadricula),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Eje Y: 5 marcas (0, ¼, ½, ¾, máx) alineadas con la zona de barras.
        private void RenderEjeY()
        {
            var brush = Tema("ThemeTextoSec", Color.FromRgb(0x7A, 0x7A, 0x7A));
            for (int i = 0; i <= 4; i++)
            {
                double frac = i / 4.0;
                double y    = _alturaGrafico + ReservaEtiquetaY - frac * _alturaGrafico;
                var lbl = new TextBlock
                {
                    Text = Fmt(_niceMax * frac),
                    FontSize = 10,
                    Foreground = brush,
                    Width = 42,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(lbl, 0);
                Canvas.SetTop(lbl, y - 7);
                PanelEjeY.Children.Add(lbl);
            }
        }

        // Cuadrícula horizontal detrás de las barras (se redibuja al cambiar el ancho).
        private void PanelPlot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Al minimizar la ventana el alto llega a ~0; ignorarlo para no borrar el
            // gráfico (se re-renderiza al restaurar con un alto válido).
            if (e.NewSize.Height <= 0) return;

            // Alto disponible para las barras (reservando ~46 px para el nombre del
            // mes y su total debajo, más ReservaEtiquetaY para la etiqueta de valor
            // arriba de la barra más alta). Re-renderiza el gráfico por mes si cambió.
            double nueva = Math.Max(120, e.NewSize.Height - 46 - ReservaEtiquetaY);
            if (_porMesCache != null && Math.Abs(nueva - _alturaGrafico) > 1)
            {
                _alturaGrafico = nueva;
                RenderMeses(_porMesCache, _porMesCatCache!);
            }
            else
            {
                DibujarCuadricula();
            }
        }

        private void DibujarCuadricula()
        {
            PanelGrid.Children.Clear();
            if (_niceMax <= 0) return;
            double w = PanelMeses.ActualWidth > 0 ? PanelMeses.ActualWidth : PanelPlot.ActualWidth;
            if (w <= 0) return;

            var brush = Tema("ThemeBorde", Color.FromRgb(0xDD, 0xDD, 0xDD));
            for (int i = 0; i <= 4; i++)
            {
                double y = _alturaGrafico + ReservaEtiquetaY - (i / 4.0) * _alturaGrafico;
                var linea = new System.Windows.Shapes.Rectangle
                {
                    Width = w,
                    Height = 1,
                    Fill = brush,
                    Opacity = 0.6
                };
                Canvas.SetLeft(linea, 0);
                Canvas.SetTop(linea, y);
                PanelGrid.Children.Add(linea);
            }
        }

        // Redondea hacia arriba a un número "lindo" (1/2/2.5/5 × 10^k) para el eje.
        private static double NiceCeil(double v)
        {
            if (v <= 0) return 0;
            double exp  = Math.Floor(Math.Log10(v));
            double pot  = Math.Pow(10, exp);
            double frac = v / pot;
            double nice = frac <= 1 ? 1 : frac <= 2 ? 2 : frac <= 2.5 ? 2.5 : frac <= 5 ? 5 : 10;
            return nice * pot;
        }

        // ─── Barras horizontales por categoría (color propio por categoría) ──
        private void RenderCategorias(Dictionary<string, double> porCategoria)
        {
            var orden = porCategoria.Where(kv => kv.Value > 0)
                                    .OrderByDescending(kv => kv.Value)
                                    .ToList();
            if (orden.Count == 0)
            {
                LblCategoriasVacio.Visibility = Visibility.Visible;
                PanelCategorias.Children.Clear();
                return;
            }
            LblCategoriasVacio.Visibility = Visibility.Collapsed;

            var datos = orden.Select(kv => (kv.Key, kv.Value, ColorDe(kv.Key))).ToList();
            RenderBarrasH(PanelCategorias, datos);
        }

        // ─── Una barra apilada por familia, segmentada por categoría ─────────
        // Por cada familia: título, totales por categoría y una sola barra
        // apilada (cada segmento = categoría, con su color). La longitud total de
        // la barra es proporcional al total de la familia (máxima familia = 100%).
        private void RenderFamilias(Dictionary<string, Dictionary<string, double>> porFamiliaCat)
        {
            PanelFamilias.Children.Clear();

            var familias = porFamiliaCat
                .Select(kv => (fam: kv.Key, cats: kv.Value, total: kv.Value.Values.Sum()))
                .Where(x => x.total > 0)
                .OrderByDescending(x => x.total)
                .ToList();

            if (familias.Count == 0)
            {
                LblFamiliasVacio.Visibility = Visibility.Visible;
                return;
            }
            LblFamiliasVacio.Visibility = Visibility.Collapsed;

            double maxTotal = familias[0].total;
            var ordenCats   = _colorCat.Keys.ToList();   // orden estable de categorías
            var textBrush   = Tema("ThemeTexto", Color.FromRgb(0x20, 0x20, 0x20));
            var trackBrush  = Tema("ThemeBgReadOnly", Color.FromRgb(0xEE, 0xEE, 0xEE));

            foreach (var (fam, cats, total) in familias)
            {
                var card = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

                // Título de la familia
                var tFam = new TextBlock
                {
                    Text = fam, FontSize = 12, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = textBrush, Margin = new Thickness(0, 0, 0, 0)
                };
                card.Children.Add(tFam);

                // Totales por categoría (entre el título y la barra)
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
                foreach (var cat in ordenCats)
                {
                    double val = cats.GetValueOrDefault(cat);
                    if (val <= 0) continue;
                    var chip = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 12, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    chip.Children.Add(new Border
                    {
                        Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                        Background = ColorDe(cat),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0)
                    });
                    chip.Children.Add(new TextBlock
                    {
                        Text = $"{cat}: {Fmt(val)}", FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = textBrush
                    });
                    chips.Children.Add(chip);
                }
                card.Children.Add(chips);

                // Track que llena todo el ancho. Dentro: col0 coloreada (proporcional
                // al total de esta familia vs la máxima), col1 vacía (fondo del track).
                var innerGrid = new Grid();
                innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(total, GridUnitType.Star) });
                innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, maxTotal - total), GridUnitType.Star) });

                var segsGrid = new Grid();
                int colIdx = 0;
                foreach (var cat in ordenCats)
                {
                    double val = cats.GetValueOrDefault(cat);
                    if (val <= 0) continue;
                    segsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(val, GridUnitType.Star) });
                    var seg = new Border { Background = ColorDe(cat) };
                    Grid.SetColumn(seg, colIdx++);
                    segsGrid.Children.Add(seg);
                }
                Grid.SetColumn(segsGrid, 0);
                innerGrid.Children.Add(segsGrid);

                var track = new Border
                {
                    Height = 20, CornerRadius = new CornerRadius(4),
                    Background = trackBrush, ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = innerGrid
                };

                // Grid: col0 star = track ancho completo, col1 auto = etiqueta del total
                var barRow = new Grid { VerticalAlignment = VerticalAlignment.Center };
                barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(track, 0);
                barRow.Children.Add(track);
                var tTotBar = new TextBlock
                {
                    Text = Fmt(total), FontSize = 12, FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = textBrush
                };
                Grid.SetColumn(tTotBar, 1);
                barRow.Children.Add(tTotBar);
                card.Children.Add(barRow);

                PanelFamilias.Children.Add(card);
            }
        }

        // ─── Helper: barras horizontales que LLENAN el ancho disponible ──────
        // Usa columnas en proporción (star): la barra más larga = 100% del ancho
        // de la zona de barras. El contenedor debe estar en un ScrollViewer con
        // scroll horizontal deshabilitado (para que el ancho sea finito).
        private const double LabelW = 130;

        private void RenderBarrasH(Panel destino, List<(string label, double val, Brush color)> datos)
        {
            destino.Children.Clear();
            if (datos.Count == 0) return;

            double max = NiceCeil(datos.Max(d => d.val));
            if (max <= 0) return;

            var lineBrush  = Tema("ThemeBorde", Color.FromRgb(0xDD, 0xDD, 0xDD));
            var textBrush  = Tema("ThemeTexto", Color.FromRgb(0x20, 0x20, 0x20));
            var mutedBrush = Tema("ThemeTextoSec", Color.FromRgb(0x7A, 0x7A, 0x7A));

            // Contenedor: cuadrícula (detrás) + filas (delante)
            var cont = new Grid();

            // Cuadrícula vertical: 4 columnas iguales con línea a la derecha
            var gl = new Grid { Margin = new Thickness(LabelW, 0, 0, 0) };
            for (int i = 0; i < 4; i++)
            {
                gl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var line = new Border
                {
                    BorderBrush = lineBrush,
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Opacity = 0.6
                };
                Grid.SetColumn(line, i);
                gl.Children.Add(line);
            }
            cont.Children.Add(gl);

            // Filas (etiqueta + barra proporcional + valor)
            var filas = new StackPanel();
            foreach (var (label, val, color) in datos)
            {
                var fila = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelW) });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lbl = new TextBlock
                {
                    Text = label, FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Foreground = textBrush
                };
                Grid.SetColumn(lbl, 0);
                fila.Children.Add(lbl);

                // 3 columnas: col0=val Stars (barra), col1=Auto (etiqueta justo
                // después), col2=(max-val) Stars (espacio restante). La etiqueta
                // aparece pegada al borde derecho de su barra, no al del contenedor.
                var bz = new Grid();
                bz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, val), GridUnitType.Star) });
                bz.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, max - val), GridUnitType.Star) });

                var bar = new Border
                {
                    Height = 24,
                    Background = color,
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(bar, 0);
                bz.Children.Add(bar);

                var v = new TextBlock
                {
                    Text = Fmt(val), FontSize = 11, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    Foreground = textBrush
                };
                Grid.SetColumn(v, 1);
                bz.Children.Add(v);

                Grid.SetColumn(bz, 1);
                fila.Children.Add(bz);

                filas.Children.Add(fila);
            }
            cont.Children.Add(filas);
            destino.Children.Add(cont);

            // Eje X: 0 a la izquierda + marcas 25/50/75/100% del máximo
            var eje = new Grid { Margin = new Thickness(LabelW, 4, 0, 0) };
            for (int i = 0; i < 4; i++)
            {
                eje.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var t = new TextBlock
                {
                    Text = Fmt(max * (i + 1) / 4.0),
                    FontSize = 10, TextAlignment = TextAlignment.Right,
                    Foreground = mutedBrush
                };
                Grid.SetColumn(t, i);
                eje.Children.Add(t);
            }
            var cero = new TextBlock
            {
                Text = "0", FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = mutedBrush
            };
            Grid.SetColumn(cero, 0);
            eje.Children.Add(cero);
            destino.Children.Add(eje);
        }

        // ─── Lista de artículos (Familia · Descripción · Cantidad) ───────────
        private void RenderArticulos(Dictionary<string, double> porArticulo, double total)
        {
            _articulos.Clear();
            foreach (var (artId, val) in porArticulo.Where(kv => kv.Value > 0)
                                                    .OrderByDescending(kv => kv.Value))
            {
                var (_, fam, desc, cod) = MetaArticulo(artId);
                _articulos.Add(new ArticuloCantFila
                {
                    Codigo          = cod,
                    Familia         = fam,
                    Descripcion     = desc,
                    Cantidad        = val,
                    PorcentajeTexto = total > 0 ? (val / total).ToString("P1", CultureInfo.CurrentCulture) : "0%"
                });
            }
            LblArticulosTotal.Text = Fmt(total);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────
        private (string cat, string fam, string desc, string cod) MetaArticulo(string artId)
        {
            if (string.IsNullOrEmpty(artId)) return ("Sin categoría", "Sin familia", "(sin artículo)", "");
            if (_articuloMeta.TryGetValue(artId, out var meta)) return meta;

            string catId = Sql.ArticulosObj.ObtenerItem("categoria", artId)?.ToString() ?? "";
            string famId = Sql.ArticulosObj.ObtenerItem("familia",   artId)?.ToString() ?? "";
            string cat = string.IsNullOrEmpty(catId)
                ? "Sin categoría"
                : Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "Sin categoría";
            string fam = string.IsNullOrEmpty(famId)
                ? "Sin familia"
                : Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? "Sin familia";
            string desc = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
            string cod  = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? "";

            meta = (cat, fam, desc, cod);
            _articuloMeta[artId] = meta;
            return meta;
        }

        private static double ConvertirCantidad(object? v)
        {
            if (v is null or DBNull) return 0;
            return double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out double d)
                || double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)
                ? d : 0;
        }

        private static DateTime? ConvertirFecha(object? v)
        {
            if (v is null or DBNull) return null;
            if (v is DateTime dt) return dt;
            return DateTime.TryParse(v.ToString(), out DateTime f) ? f : (DateTime?)null;
        }

        private static string Fmt(double v) => v.ToString("N0", CultureInfo.CurrentCulture);

        // Brush del tema con fallback (evita excepción si la clave no resuelve).
        private Brush Tema(string clave, Color fallback) =>
            TryFindResource(clave) as Brush ?? new SolidColorBrush(fallback);

        private static string NombreMes(int m)
        {
            string[] meses = { "", "Ene", "Feb", "Mar", "Abr", "May", "Jun",
                               "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
            return (m >= 1 && m <= 12) ? meses[m] : "";
        }

        // ─── Filas de tablas ──────────────────────────────────────────────────
        public class ArticuloCantFila
        {
            public string Codigo          { get; set; } = "";
            public string Familia         { get; set; } = "";
            public string Descripcion     { get; set; } = "";
            public double Cantidad        { get; set; }
            public string CantidadTexto   => Cantidad.ToString("N0", CultureInfo.CurrentCulture);
            public string PorcentajeTexto { get; set; } = "";
        }
    }
}
