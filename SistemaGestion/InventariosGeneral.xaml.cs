using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class InventariosGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private bool _iniciado = false;

        public InventariosGeneral()
        {
            InitializeComponent();
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; ConfigurarModo(); CargarInventarios(); };
        }

        // BtnEliminar queda visible para todos: además del administrador, el
        // usuario que creó el documento también puede eliminarlo/ocultarlo — el
        // permiso se valida por documento en BtnEliminar_Click.
        private void ConfigurarModo() { }

        // ─── Carga la lista ────────────────────────────────────────────────────
        public void CargarInventarios()
        {
            var lista = new List<InventarioFila>();
            int linea = 1;

            int uf = Sql.DocumentosIObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosIObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                var fechaObj = Sql.DocumentosIObj.ObtenerItem("fecha", id);
                DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;
                double cantidad = CalcularCantidad(id);

                lista.Add(new InventarioFila
                {
                    Linea         = linea++,
                    Id            = id,
                    Codigo        = Sql.DocumentosIObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                    Fecha         = fecha,
                    FechaStr      = fecha != default ? $"{fecha:d} {fecha:HH:mm:ss}" : "",
                    Referencia    = Sql.DocumentosIObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                    UsuarioCreador = Sql.DocumentosIObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                    CantidadTotal = cantidad
                });
            }

            Grid1.ItemsSource = lista;
            SeleccionarFilaMasReciente(lista);
        }

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
        // Las selecciones explícitas de guardar/editar/eliminar corren DESPUÉS de
        // CargarInventarios(), así que siguen ganando sobre ésta.
        private void SeleccionarFilaMasReciente(List<InventarioFila> lista)
        {
            if (lista.Count == 0) return;
            var fila = lista.Aggregate((a, b) => b.Fecha >= a.Fecha ? b : a);
            Grid1.SelectedItem = fila;
            Grid1.ScrollIntoView(fila);

            // Foco de teclado en la fila: seleccionarla solo la pinta. Mismo helper
            // que ya usan Nuevo/Editar/Eliminar.
            GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
        }

        // ─── Helpers de actualización incremental del Grid1 ───────────────────
        private List<InventarioFila> FilasGrid =>
            Grid1.ItemsSource as List<InventarioFila> ?? new List<InventarioFila>();

        private InventarioFila ConstruirFilaInventario(string id, int linea)
        {
            var fechaObj = Sql.DocumentosIObj.ObtenerItem("fecha", id);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;
            return new InventarioFila
            {
                Linea         = linea,
                Id            = id,
                Codigo        = Sql.DocumentosIObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                Fecha         = fecha,
                FechaStr      = fecha != default ? $"{fecha:d} {fecha:HH:mm:ss}" : "",
                Referencia    = Sql.DocumentosIObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                UsuarioCreador = Sql.DocumentosIObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                CantidadTotal = CalcularCantidad(id)
            };
        }

        private void Renumerar()
        {
            int n = 1;
            foreach (var f in FilasGrid) f.Linea = n++;
            Grid1.Items.Refresh();
        }

        // ─── Calcula la cantidad total de un documento de inventario ──────────
        private double CalcularCantidad(string documentoI)
        {
            double cant = 0;
            int uf = Sql.InventariosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.InventariosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (Sql.InventariosObj.ObtenerItem("documentoI", id)?.ToString() != documentoI)
                    continue;

                cant += Convert.ToDouble(Sql.InventariosObj.ObtenerItem("cantidad", id) ?? 0);
            }
            return cant;
        }

        // ─── Doble clic = editar ───────────────────────────────────────────────
        private void Grid1_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AbrirEditar();
        }

        // ─── Tecla Enter = editar ─────────────────────────────────────────────
        private void Grid1_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            AbrirEditar();
        }

        // ─── Botones ──────────────────────────────────────────────────────────
        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            AppState.EventoFormularioI = "nuevo";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            var detalle = new InventariosDetalle(this);
            detalle.Cerrando += () =>
            {
                consola.CerrarPestaña(detalle);
                if (detalle.ItemCreadoId == null) return;   // cancelado
                var nueva = ConstruirFilaInventario(detalle.ItemCreadoId, 0);
                FilasGrid.Add(nueva);
                Renumerar();
                Grid1.SelectedItem = nueva; Grid1.ScrollIntoView(nueva);
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña("Nuevo Inventario", detalle, "nuevo-inventario");
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
            => AbrirEditar();

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            CargarInventarios();
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (Grid1.SelectedItem is not InventarioFila fila) return;

            // Solo el administrador o el usuario que creó el documento puede eliminarlo.
            if (!AppState.EsAdmin && fila.UsuarioCreador != AppState.UsuarioActivo)
            {
                MessageBox.Show("Solo un administrador o el usuario que creó este documento puede eliminarlo.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verificación de conexión en 2 capas antes de persistir el borrado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            // Solo se puede eliminar la apertura más reciente
            if (fila.Id != AppState.AperturaIdActiva)
            {
                MessageBox.Show("Solo se puede eliminar la última apertura.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var res = MessageBox.Show("¿Eliminar este inventario de apertura?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                // Ocultar todos los inventarios de este documentoI
                int uf = Sql.InventariosObj.ContarFilas;
                var idsEliminar = new List<string>();
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.InventariosObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    if (Sql.InventariosObj.ObtenerItem("documentoI", id)?.ToString() == fila.Id)
                        idsEliminar.Add(id);
                }
                foreach (string id in idsEliminar)
                    Sql.InventariosObj.Ocultar(id);

                // Ocultar el documento de inventario
                Sql.DocumentosIObj.EstablecerItem("edicion",  fila.Id, DateTime.Now);
                Sql.DocumentosIObj.EstablecerItem("usuarioE", fila.Id, AppState.UsuarioActivo);
                Sql.DocumentosIObj.Ocultar(fila.Id);

                Sql.InventariosObj.OrdenarData(("documentoI", false));
                Sql.DocumentosIObj.OrdenarData(("id", false));

                int periodo = string.IsNullOrEmpty(AppState.PeriodoActivo)
                    ? DateTime.Now.Year
                    : int.Parse(AppState.PeriodoActivo);
                AppState.ActualizarBase(periodo);
                AppLoader.ConectarDocumentos(AppState.DataFechaInicio, AppState.DataFechaFinal);

                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0) lista.RemoveAt(idx);
                Renumerar();

                if (lista.Count > 0)
                {
                    var sel = lista[Math.Min(idx, lista.Count - 1)];
                    Grid1.SelectedItem = sel; Grid1.ScrollIntoView(sel);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── Helper ───────────────────────────────────────────────────────────
        // Abre InventariosDetalle para editar. Si no es la apertura más reciente,
        // el propio InventariosDetalle detecta esto (AppState.AperturaIdActiva) y se
        // muestra en modo solo lectura en vez de bloquear la apertura del documento.
        private void AbrirEditar()
        {
            if (Grid1.SelectedItem is not InventarioFila fila) return;

            string idSel = fila.Id;
            int    linea = fila.Linea;
            AppState.EventoFormularioI = "editar";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            var detalle = new InventariosDetalle(this, fila.Id);
            detalle.Cerrando += () =>
            {
                consola.CerrarPestaña(detalle);
                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0)
                {
                    var actualizada = ConstruirFilaInventario(idSel, linea);
                    lista[idx] = actualizada;
                    Renumerar();
                    Grid1.SelectedItem = actualizada; Grid1.ScrollIntoView(actualizada);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña($"Inventario {fila.Codigo}", detalle, $"inventario-{idSel}");
        }

        // articulos.estado ("mostrar"/"ocultar", cargada por el usuario en
        // ArticulosDetalle) filtra qué artículos entran en las plantillas Excel/PDF.
        // Solo se excluye si quedó explícitamente en "ocultar"; null/vacío/cualquier
        // otro valor (artículos guardados antes de que existiera esta columna) cuenta
        // como visible, para no vaciar de golpe las plantillas ya existentes.
        private static bool EsVisibleEnPlantillas(string artId)
        {
            string estadoV = Sql.ArticulosObj.ObtenerItem("estado", artId)?.ToString()?.Trim().ToLowerInvariant() ?? "";
            return estadoV != "ocultar";
        }

        // ─── Crear Plantilla: elige Excel o PDF, después guarda y abre el archivo ──
        // Mismo patrón que VisorEmpresa/PreciosGeneral.BtnCrearPlantilla_Click. Excel:
        // catálogo completo, sin agrupar, con columnas Producto y Familia (N° / Código /
        // Producto / Familia / Descripción / Cantidad) — el import (InventariosDetalle.
        // ImportarCantidadesDesdeExcel) busca Código y Cantidad por encabezado, no por
        // posición. PDF: catálogo completo agrupado por Producto & Familia (banda de
        // grupo en vez de columnas propias). En ambos casos Cantidad arranca en 0.
        private void BtnCrearPlantilla_Click(object sender, RoutedEventArgs e)
        {
            var dlgFormato = new SeleccionarFormatoWindow { Owner = Window.GetWindow(this) };
            if (dlgFormato.ShowDialog() != true) return;

            bool esExcel = dlgFormato.FormatoElegido == "excel";

            var dlg = new SaveFileDialog
            {
                Title            = "Guardar plantilla de inventario",
                FileName         = $"{DateTime.Now:yyyyMMdd HHmmss} plantilla inventario.{(esExcel ? "xlsx" : "pdf")}",
                DefaultExt       = esExcel ? ".xlsx" : ".pdf",
                Filter           = esExcel ? "Excel (*.xlsx)|*.xlsx" : "PDF (*.pdf)|*.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (esExcel) GenerarPlantillaExcel(dlg.FileName);
                else         GenerarPlantillaPdf(dlg.FileName);
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar la plantilla:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void GenerarPlantillaExcel(string filePath)
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Inventario");

            ws.Cell(1, 1).Value = "N°";
            ws.Cell(1, 2).Value = "Código";
            ws.Cell(1, 3).Value = "Producto";
            ws.Cell(1, 4).Value = "Familia";
            ws.Cell(1, 5).Value = "Descripción";
            ws.Cell(1, 6).Value = "Cantidad";

            // Sin reordenar: Sql.ArticulosObj ya viene cargado en cascada Producto →
            // Familia → Índice (ver AppLoader.cs), el mismo orden que usan
            // InventariosDetalle y ArticulosGeneral.
            int row = 2;
            int numero = 1;
            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string artId = idObj.ToString()!;
                if (!EsVisibleEnPlantillas(artId)) continue;

                string famId    = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto",    famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
                string codigo   = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString() ?? "";
                string desc     = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                ws.Cell(row, 1).Value = numero;
                ws.Cell(row, 2).Value = codigo;
                ws.Cell(row, 3).Value = prodDesc;
                ws.Cell(row, 4).Value = famDesc;
                ws.Cell(row, 5).Value = descCompleta;
                ws.Cell(row, 6).Value = 0; // Cantidad: arranca en 0 para que el usuario la complete.
                row++;
                numero++;
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        // Plantilla en PDF: catálogo completo agrupado por Producto & Familia (misma
        // banda celeste y estilo de tabla que GenerarPdfInventario más abajo), con
        // Cantidad en 0 para completar a mano. A diferencia del Excel, acá Producto/
        // Familia NO son columnas de datos: van en la banda que encabeza cada grupo.
        private void GenerarPlantillaPdf(string filePath)
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;

            var fontTitulo = new XFont("Arial", 14, XFontStyleEx.Bold);
            var fontHeader = new XFont("Arial", 9,  XFontStyleEx.Bold);
            var fontGrupo  = new XFont("Arial", 10, XFontStyleEx.Bold);
            var fontCuerpo = new XFont("Arial", 9,  XFontStyleEx.Regular);

            var brushCeleste = new XSolidBrush(XColor.FromArgb(191, 219, 254));
            var brushHeader  = new XSolidBrush(XColor.FromArgb(241, 245, 249));
            var penLinea     = new XPen(XColors.Black, 0.6);

            const double margen        = 40;
            const double altoHeader    = 20;
            const double altoGrupo     = 18;
            const double altoFila      = 16;
            const double anchoN        = 28;
            const double anchoCodigo   = 80;
            const double anchoCantidad = 75;

            var document = new PdfDocument();
            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;
            XGraphics gfx = XGraphics.FromPdfPage(page);

            double anchoTabla = page.Width - margen * 2;
            double anchoDesc  = anchoTabla - anchoN - anchoCodigo - anchoCantidad;
            double xN        = margen;
            double xCodigo   = xN + anchoN;
            double xDesc     = xCodigo + anchoCodigo;
            double xCantidad = xDesc + anchoDesc;

            double y = margen;

            void DibujarFilaDatos(string n, string codigo, string desc, string cantidad)
            {
                gfx.DrawRectangle(penLinea, xN, y, anchoTabla, altoFila);
                gfx.DrawLine(penLinea, xCodigo,   y, xCodigo,   y + altoFila);
                gfx.DrawLine(penLinea, xDesc,     y, xDesc,     y + altoFila);
                gfx.DrawLine(penLinea, xCantidad, y, xCantidad, y + altoFila);

                gfx.DrawString(n,        fontCuerpo, XBrushes.Black, new XRect(xN,           y, anchoN,          altoFila), XStringFormats.Center);
                gfx.DrawString(codigo,   fontCuerpo, XBrushes.Black, new XRect(xCodigo + 4,  y, anchoCodigo - 8, altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(desc,     fontCuerpo, XBrushes.Black, new XRect(xDesc + 4,    y, anchoDesc - 8,   altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(cantidad, fontCuerpo, XBrushes.Black, new XRect(xCantidad + 4, y, anchoCantidad - 8, altoFila), XStringFormats.CenterRight);

                y += altoFila;
            }

            void DibujarEncabezadoColumnas()
            {
                gfx.DrawRectangle(penLinea, brushHeader, xN, y, anchoTabla, altoHeader);
                gfx.DrawLine(penLinea, xCodigo,   y, xCodigo,   y + altoHeader);
                gfx.DrawLine(penLinea, xDesc,     y, xDesc,     y + altoHeader);
                gfx.DrawLine(penLinea, xCantidad, y, xCantidad, y + altoHeader);

                gfx.DrawString("N°",          fontHeader, XBrushes.Black, new XRect(xN,           y, anchoN,          altoHeader), XStringFormats.Center);
                gfx.DrawString("Código",      fontHeader, XBrushes.Black, new XRect(xCodigo + 4,  y, anchoCodigo - 8, altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Descripción", fontHeader, XBrushes.Black, new XRect(xDesc + 4,    y, anchoDesc - 8,   altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Cantidad",    fontHeader, XBrushes.Black, new XRect(xCantidad + 4, y, anchoCantidad - 8, altoHeader), XStringFormats.CenterRight);

                y += altoHeader;
            }

            void DibujarBandaGrupo(string texto)
            {
                gfx.DrawRectangle(penLinea, brushCeleste, xN, y, anchoTabla, altoGrupo);
                gfx.DrawString(texto, fontGrupo, XBrushes.Black, new XRect(xN + 6, y, anchoTabla - 12, altoGrupo), XStringFormats.CenterLeft);
                y += altoGrupo;
            }

            void NuevaPagina()
            {
                gfx.Dispose();
                page = document.AddPage();
                page.Size = PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = margen;
                DibujarEncabezadoColumnas();
            }

            void AsegurarEspacio(double alto)
            {
                if (y + alto > page.Height - margen) NuevaPagina();
            }

            gfx.DrawString("Plantilla de Inventario", fontTitulo, XBrushes.Black, new XPoint(margen, y + 12));
            y += 28;

            DibujarEncabezadoColumnas();

            var lineas = new List<(string prodDesc, string famDesc, string codigo, string desc)>();

            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string artId = idObj.ToString()!;
                if (!EsVisibleEnPlantillas(artId)) continue;

                string famId    = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto",    famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
                string codigo   = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString() ?? "";
                string desc     = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                lineas.Add((prodDesc, famDesc, codigo, descCompleta));
            }

            var grupos = lineas
                .GroupBy(l => (l.prodDesc, l.famDesc))
                .OrderBy(g => g.Key.prodDesc, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.famDesc, StringComparer.OrdinalIgnoreCase);

            int n = 0;
            foreach (var grupo in grupos)
            {
                AsegurarEspacio(altoGrupo + altoFila);
                DibujarBandaGrupo($"{grupo.Key.prodDesc} & {grupo.Key.famDesc}");

                foreach (var l in grupo)
                {
                    AsegurarEspacio(altoFila);
                    n++;
                    DibujarFilaDatos(n.ToString(), l.codigo, l.desc, ""); // Cantidad en blanco para completar a mano.
                }
            }

            // El último gfx de la generación de contenido sigue abierto: hay que
            // cerrarlo antes de poder volver a abrir un XGraphics sobre esa misma
            // página en la segunda pasada de abajo.
            gfx.Dispose();

            // ── Encabezado (empresa / fecha de emisión) y pie de página (páginas), en
            // todas las páginas. Se hace en una segunda pasada porque el total de
            // páginas recién se conoce una vez generado todo el contenido.
            string empresaDesc     = Sql.EmpresasObj.ObtenerItem("descripcion", AppState.EmpresaActiva)?.ToString() ?? "";
            string sucursalDesc    = Sql.SucursalesObj.ObtenerItem("descripcion", AppState.SucursalActiva)?.ToString() ?? "";
            string encabezadoIzq   = string.IsNullOrEmpty(sucursalDesc) ? empresaDesc : $"{empresaDesc} - {sucursalDesc}";
            DateTime fechaEmision  = DateTime.Now;
            string fechaEmisionStr = $"{fechaEmision:d} {fechaEmision:HH:mm:ss}";
            int totalPaginas       = document.PageCount;

            for (int p = 0; p < totalPaginas; p++)
            {
                var paginaActual = document.Pages[p];
                using var gfxPagina = XGraphics.FromPdfPage(paginaActual);
                double anchoMedio = (paginaActual.Width - margen * 2) / 2;

                gfxPagina.DrawString(encabezadoIzq, fontCuerpo, XBrushes.Black,
                    new XRect(margen, 16, anchoMedio, 16), XStringFormats.CenterLeft);
                gfxPagina.DrawString($"Fecha de emisión: {fechaEmisionStr}", fontCuerpo, XBrushes.Black,
                    new XRect(margen + anchoMedio, 16, anchoMedio, 16), XStringFormats.CenterRight);

                gfxPagina.DrawString($"Páginas {p + 1}-{totalPaginas}", fontCuerpo, XBrushes.Black,
                    new XRect(margen, paginaActual.Height - margen + 10, paginaActual.Width - margen * 2, 16), XStringFormats.CenterRight);
            }

            document.Save(filePath);
        }

        // ─── PDF del inventario (botón "Acciones" por fila, mismo formato que PreciosGeneral) ─
        private void BtnPdfFila_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not InventarioFila fila) return;

            var dlg = new SaveFileDialog
            {
                Title            = "Guardar inventario",
                FileName         = $"{DateTime.Now:yyyyMMdd HHmmss} inventario {fila.Codigo}.pdf",
                DefaultExt       = ".pdf",
                Filter           = "PDF (*.pdf)|*.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                GenerarPdfInventario(dlg.FileName, fila);

                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el PDF:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        // Agrupa las líneas del inventario por producto y familia (encabezado "Producto & Familia"),
        // igual estructura de tabla que GenerarPdfListaPrecios en PreciosGeneral.
        private void GenerarPdfInventario(string filePath, InventarioFila fila)
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;

            var fontTitulo = new XFont("Arial", 14, XFontStyleEx.Bold);
            var fontHeader = new XFont("Arial", 9,  XFontStyleEx.Bold);
            var fontGrupo  = new XFont("Arial", 10, XFontStyleEx.Bold);
            var fontCuerpo = new XFont("Arial", 9,  XFontStyleEx.Regular);

            var brushCeleste = new XSolidBrush(XColor.FromArgb(191, 219, 254));
            var brushHeader  = new XSolidBrush(XColor.FromArgb(241, 245, 249));
            var penLinea     = new XPen(XColors.Black, 0.6);

            const double margen        = 40;
            const double altoHeader    = 20;
            const double altoGrupo     = 18;
            const double altoFila      = 16;
            const double anchoN        = 28;
            const double anchoCodigo   = 80;
            const double anchoCantidad = 75;

            var document = new PdfDocument();
            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;
            XGraphics gfx = XGraphics.FromPdfPage(page);

            double anchoTabla = page.Width - margen * 2;
            double anchoDesc  = anchoTabla - anchoN - anchoCodigo - anchoCantidad;
            double xN        = margen;
            double xCodigo   = xN + anchoN;
            double xDesc     = xCodigo + anchoCodigo;
            double xCantidad = xDesc + anchoDesc;

            double y = margen;

            // Fila de datos: 4 columnas con sus líneas separadoras.
            void DibujarFilaDatos(string n, string codigo, string desc, string cantidad)
            {
                gfx.DrawRectangle(penLinea, xN, y, anchoTabla, altoFila);
                gfx.DrawLine(penLinea, xCodigo,   y, xCodigo,   y + altoFila);
                gfx.DrawLine(penLinea, xDesc,     y, xDesc,     y + altoFila);
                gfx.DrawLine(penLinea, xCantidad, y, xCantidad, y + altoFila);

                gfx.DrawString(n,        fontCuerpo, XBrushes.Black, new XRect(xN,           y, anchoN,          altoFila), XStringFormats.Center);
                gfx.DrawString(codigo,   fontCuerpo, XBrushes.Black, new XRect(xCodigo + 4,  y, anchoCodigo - 8, altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(desc,     fontCuerpo, XBrushes.Black, new XRect(xDesc + 4,    y, anchoDesc - 8,   altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(cantidad, fontCuerpo, XBrushes.Black, new XRect(xCantidad + 4, y, anchoCantidad - 8, altoFila), XStringFormats.CenterRight);

                y += altoFila;
            }

            // Encabezado de columnas con fondo gris claro.
            void DibujarEncabezadoColumnas()
            {
                gfx.DrawRectangle(penLinea, brushHeader, xN, y, anchoTabla, altoHeader);
                gfx.DrawLine(penLinea, xCodigo,   y, xCodigo,   y + altoHeader);
                gfx.DrawLine(penLinea, xDesc,     y, xDesc,     y + altoHeader);
                gfx.DrawLine(penLinea, xCantidad, y, xCantidad, y + altoHeader);

                gfx.DrawString("N°",          fontHeader, XBrushes.Black, new XRect(xN,           y, anchoN,          altoHeader), XStringFormats.Center);
                gfx.DrawString("Código",      fontHeader, XBrushes.Black, new XRect(xCodigo + 4,  y, anchoCodigo - 8, altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Descripción", fontHeader, XBrushes.Black, new XRect(xDesc + 4,    y, anchoDesc - 8,   altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Cantidad",    fontHeader, XBrushes.Black, new XRect(xCantidad + 4, y, anchoCantidad - 8, altoHeader), XStringFormats.CenterRight);

                y += altoHeader;
            }

            // Banda de agrupación: una sola celda celeste de ancho completo (sin separadores
            // internos), para que el subtítulo resalte como una franja y no como otra fila de la tabla.
            void DibujarBandaGrupo(string texto)
            {
                gfx.DrawRectangle(penLinea, brushCeleste, xN, y, anchoTabla, altoGrupo);
                gfx.DrawString(texto, fontGrupo, XBrushes.Black, new XRect(xN + 6, y, anchoTabla - 12, altoGrupo), XStringFormats.CenterLeft);
                y += altoGrupo;
            }

            // Tabla de totales por categoría: 2 columnas (Categoría / Cantidad), alineada
            // con el borde derecho de la columna Cantidad de la tabla de artículos.
            void DibujarEncabezadoCategorias()
            {
                double anchoCatCol = xCantidad - xN;
                gfx.DrawRectangle(penLinea, brushHeader, xN, y, anchoTabla, altoHeader);
                gfx.DrawLine(penLinea, xCantidad, y, xCantidad, y + altoHeader);
                gfx.DrawString("Categoría", fontHeader, XBrushes.Black, new XRect(xN + 4, y, anchoCatCol - 8, altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Cantidad",  fontHeader, XBrushes.Black, new XRect(xCantidad + 4, y, anchoCantidad - 8, altoHeader), XStringFormats.CenterRight);
                y += altoHeader;
            }

            void DibujarFilaCategoria(string categoria, string cantidad, bool destacado = false)
            {
                double anchoCatCol = xCantidad - xN;
                if (destacado) gfx.DrawRectangle(penLinea, brushCeleste, xN, y, anchoTabla, altoFila);
                else           gfx.DrawRectangle(penLinea, xN, y, anchoTabla, altoFila);
                gfx.DrawLine(penLinea, xCantidad, y, xCantidad, y + altoFila);

                var font = destacado ? fontGrupo : fontCuerpo;
                gfx.DrawString(categoria, font, XBrushes.Black, new XRect(xN + 4, y, anchoCatCol - 8, altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(cantidad,  font, XBrushes.Black, new XRect(xCantidad + 4, y, anchoCantidad - 8, altoFila), XStringFormats.CenterRight);

                y += altoFila;
            }

            // Espacio en blanco entre la lista de artículos y los totales por categoría
            // (sin línea dibujada, solo separación vertical).
            const double altoSeparador = 16;
            void DibujarSeparador()
            {
                y += altoSeparador;
            }

            // Encabezado a redibujar al saltar de página: la tabla de artículos usa
            // DibujarEncabezadoColumnas, la sección de totales por categoría pasa a usar
            // DibujarEncabezadoCategorias una vez que empieza (ver más abajo).
            Action dibujarEncabezadoPagina = DibujarEncabezadoColumnas;

            void NuevaPagina()
            {
                gfx.Dispose();
                page = document.AddPage();
                page.Size = PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = margen;
                dibujarEncabezadoPagina();
            }

            void AsegurarEspacio(double alto)
            {
                if (y + alto > page.Height - margen) NuevaPagina();
            }

            string fechaHora = fila.FechaStr;
            string tituloPrincipal = string.IsNullOrEmpty(fechaHora)
                ? $"Inventario — {fila.Codigo}"
                : $"Inventario — {fila.Codigo}   ({fechaHora})";
            gfx.DrawString(tituloPrincipal, fontTitulo, XBrushes.Black, new XPoint(margen, y + 12));
            y += 28;

            DibujarEncabezadoColumnas();

            var lineas = new List<(string prodDesc, string famDesc, string codigo, string desc, double cantidad, string catDesc)>();

            int uf = Sql.InventariosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.InventariosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.InventariosObj.ObtenerItem("documentoI", id)?.ToString() != fila.Id) continue;

                string artId  = Sql.InventariosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string famId  = Sql.ArticulosObj.ObtenerItem("familia", artId)?.ToString() ?? "";
                string prodId = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";

                string prodDesc     = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc      = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString()  ?? "";
                string codigo       = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString()  ?? "";
                string descArt  = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString()  ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString()  ?? "";
                double cantidad = Convert.ToDouble(Sql.InventariosObj.ObtenerItem("cantidad", id) ?? 0);
                if (cantidad <= 0) continue;

                string catId   = Sql.ArticulosObj.ObtenerItem("categoria", artId)?.ToString() ?? "";
                string catDesc = string.IsNullOrEmpty(catId) ? "" : (Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "");
                string descCompleta = FuncionesComunes.UnirVariables(descArt, famDesc, modelo);

                lineas.Add((prodDesc, famDesc, codigo, descCompleta, cantidad, catDesc));
            }

            var grupos = lineas
                .GroupBy(l => (l.prodDesc, l.famDesc))
                .OrderBy(g => g.Key.prodDesc, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.famDesc, StringComparer.OrdinalIgnoreCase);

            int n = 0;
            foreach (var grupo in grupos)
            {
                AsegurarEspacio(altoGrupo + altoFila);
                DibujarBandaGrupo($"{grupo.Key.prodDesc} & {grupo.Key.famDesc}");

                foreach (var l in grupo)
                {
                    AsegurarEspacio(altoFila);
                    n++;
                    DibujarFilaDatos(n.ToString(), l.codigo, l.desc, l.cantidad.ToString("#,##0.##"));
                }
            }

            // ── Totales por categoría + total general, al final de la carga ──────
            var totalesPorCategoria = lineas
                .GroupBy(l => string.IsNullOrEmpty(l.catDesc) ? "Otros" : l.catDesc)
                .Select(g => (categoria: g.Key, cantidad: g.Sum(x => x.cantidad)))
                .OrderBy(x => x.categoria, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (totalesPorCategoria.Count > 0)
            {
                dibujarEncabezadoPagina = () => { };
                AsegurarEspacio(altoSeparador + altoGrupo + altoHeader);
                DibujarSeparador();
                DibujarBandaGrupo("Totales por categoría");
                DibujarEncabezadoCategorias();
                dibujarEncabezadoPagina = DibujarEncabezadoCategorias;

                double totalGeneral = 0;
                foreach (var (categoria, cantidad) in totalesPorCategoria)
                {
                    AsegurarEspacio(altoFila);
                    DibujarFilaCategoria(categoria, cantidad.ToString("#,##0.##"));
                    totalGeneral += cantidad;
                }

                AsegurarEspacio(altoFila);
                DibujarFilaCategoria("Total general", totalGeneral.ToString("#,##0.##"), destacado: true);
            }

            // El último gfx de la generación de contenido sigue abierto: hay que
            // cerrarlo antes de poder volver a abrir un XGraphics sobre esa misma
            // página en la segunda pasada de abajo.
            gfx.Dispose();

            // ── Encabezado (empresa / fecha de emisión) y pie de página (páginas), en
            // todas las páginas. Se hace en una segunda pasada porque el total de
            // páginas recién se conoce una vez generado todo el contenido.
            string empresaDesc     = Sql.EmpresasObj.ObtenerItem("descripcion", AppState.EmpresaActiva)?.ToString() ?? "";
            string sucursalDesc    = Sql.SucursalesObj.ObtenerItem("descripcion", AppState.SucursalActiva)?.ToString() ?? "";
            string encabezadoIzq   = string.IsNullOrEmpty(sucursalDesc) ? empresaDesc : $"{empresaDesc} - {sucursalDesc}";
            DateTime fechaEmision  = DateTime.Now;
            string fechaEmisionStr = $"{fechaEmision:d} {fechaEmision:HH:mm:ss}";
            int totalPaginas       = document.PageCount;

            for (int p = 0; p < totalPaginas; p++)
            {
                var paginaActual = document.Pages[p];
                using var gfxPagina = XGraphics.FromPdfPage(paginaActual);
                double anchoMedio = (paginaActual.Width - margen * 2) / 2;

                gfxPagina.DrawString(encabezadoIzq, fontCuerpo, XBrushes.Black,
                    new XRect(margen, 16, anchoMedio, 16), XStringFormats.CenterLeft);
                gfxPagina.DrawString($"Fecha de emisión: {fechaEmisionStr}", fontCuerpo, XBrushes.Black,
                    new XRect(margen + anchoMedio, 16, anchoMedio, 16), XStringFormats.CenterRight);

                gfxPagina.DrawString($"Páginas {p + 1}-{totalPaginas}", fontCuerpo, XBrushes.Black,
                    new XRect(margen, paginaActual.Height - margen + 10, paginaActual.Width - margen * 2, 16), XStringFormats.CenterRight);
            }

            document.Save(filePath);
        }

        // ─── Excel del inventario (botón "Excel" por fila) ───────────────────
        private void BtnExcelFila_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not InventarioFila fila) return;

            var dlg = new SaveFileDialog
            {
                Title            = "Guardar inventario",
                FileName         = $"{DateTime.Now:yyyyMMdd HHmmss} inventario {fila.Codigo}.xlsx",
                DefaultExt       = ".xlsx",
                Filter           = "Excel (*.xlsx)|*.xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                GenerarExcelInventario(dlg.FileName, fila);

                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el Excel:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void GenerarExcelInventario(string filePath, InventarioFila fila)
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Inventario");

            ws.Cell(1, 1).Value = "N°";
            ws.Cell(1, 2).Value = "Código";
            ws.Cell(1, 3).Value = "Producto";
            ws.Cell(1, 4).Value = "Familia";
            ws.Cell(1, 5).Value = "Categoría";
            ws.Cell(1, 6).Value = "Descripción";
            ws.Cell(1, 7).Value = "Cantidad";

            int uf = Sql.InventariosObj.ContarFilas;
            var lineas = new List<(string prodDesc, string famDesc, string codigo, string desc, double cantidad, string catDesc)>();

            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.InventariosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.InventariosObj.ObtenerItem("documentoI", id)?.ToString() != fila.Id) continue;

                string artId    = Sql.InventariosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string famId    = Sql.ArticulosObj.ObtenerItem("familia", artId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString()  ?? "";
                string codigo   = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString()  ?? "";
                string descArt  = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString()  ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString()  ?? "";
                double cantidad = Convert.ToDouble(Sql.InventariosObj.ObtenerItem("cantidad", id) ?? 0);
                if (cantidad <= 0) continue;

                string catId   = Sql.ArticulosObj.ObtenerItem("categoria", artId)?.ToString() ?? "";
                string catDesc = string.IsNullOrEmpty(catId) ? "Otros" : (Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "Otros");
                string descCompleta = FuncionesComunes.UnirVariables(descArt, famDesc, modelo);

                lineas.Add((prodDesc, famDesc, codigo, descCompleta, cantidad, catDesc));
            }

            int row = 2;
            int n = 0;
            foreach (var l in lineas)
            {
                n++;
                ws.Cell(row, 1).Value = n;
                ws.Cell(row, 2).Value = l.codigo;
                ws.Cell(row, 3).Value = l.prodDesc;
                ws.Cell(row, 4).Value = l.famDesc;
                ws.Cell(row, 5).Value = l.catDesc;
                ws.Cell(row, 6).Value = l.desc;
                ws.Cell(row, 7).Value = l.cantidad;
                row++;
            }

            // ── Separador + totales por categoría (con fórmulas SUMIF/SUM) ────────
            int primerFilaDatos = 2;
            int ultimaFilaDatos = row - 1;

            if (ultimaFilaDatos >= primerFilaDatos)
            {
                row++; // fila en blanco: separa la lista de artículos de los totales por categoría

                ws.Cell(row, 1).Value = "Totales por categoría";
                ws.Range(row, 1, row, 7).Merge();
                ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(191, 219, 254);
                ws.Range(row, 1, row, 7).Style.Font.Bold = true;
                row++;

                ws.Range(row, 1, row, 6).Merge();
                ws.Cell(row, 1).Value = "Categoría";
                ws.Cell(row, 7).Value = "Cantidad";
                ws.Range(row, 1, row, 7).Style.Font.Bold = true;
                ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(241, 245, 249);
                row++;

                var categorias = lineas
                    .Select(l => l.catDesc)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

                foreach (var categoria in categorias)
                {
                    string catEscapada = categoria.Replace("\"", "\"\"");
                    ws.Range(row, 1, row, 6).Merge();
                    ws.Cell(row, 1).Value = categoria;
                    ws.Cell(row, 7).FormulaA1 =
                        $"=SUMIF(E{primerFilaDatos}:E{ultimaFilaDatos},\"{catEscapada}\",G{primerFilaDatos}:G{ultimaFilaDatos})";
                    row++;
                }

                ws.Range(row, 1, row, 6).Merge();
                ws.Cell(row, 1).Value = "Total general";
                ws.Cell(row, 7).FormulaA1 = $"=SUM(G{primerFilaDatos}:G{ultimaFilaDatos})";
                ws.Range(row, 1, row, 7).Style.Font.Bold = true;
                ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(191, 219, 254);
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }
    }

    // ─── Modelo de fila para el DataGrid ──────────────────────────────────────
    public class InventarioFila
    {
        public int      Linea         { get; set; }
        public string   Id            { get; set; } = "";
        public string   Codigo        { get; set; } = "";
        public DateTime Fecha         { get; set; }
        public string   FechaStr      { get; set; } = "";
        public string   Referencia    { get; set; } = "";
        public string   UsuarioCreador { get; set; } = "";
        public double   CantidadTotal { get; set; }
    }
}
