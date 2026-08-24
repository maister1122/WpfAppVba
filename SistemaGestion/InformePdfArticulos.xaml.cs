using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class InformePdfArticulos : Window
    {
        private static SqlData Sql => SqlData.Instance;

        public InformePdfArticulos()
        {
            InitializeComponent();
            var ahora = DateTime.Now;
            TxtFecha.Text = ahora.ToString("dd/MM/yyyy");
            TxtHora.Text  = ahora.ToString("HH:mm:ss");
            ActualizarNombreArchivo();
        }

        // ─── Actualiza la preview del nombre de archivo ───────────────────────
        private void ActualizarNombreArchivo()
        {
            string nombre = TxtNombre.Text.Trim();
            string prefijoFecha = ObtenerPrefijoFecha();
            string nombreBase   = string.IsNullOrEmpty(nombre)
                ? $"{prefijoFecha} informe"
                : $"{prefijoFecha} informe {nombre}";

            TxtNombrePreview.Text = $"Nombre de archivo: {SanitizarNombre(nombreBase)}.pdf";
        }

        private string ObtenerPrefijoFecha()
        {
            string fechaStr = TxtFecha.Text.Trim();
            string horaStr  = TxtHora.Text.Trim();

            if (DateTime.TryParseExact(fechaStr,
                    new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime fecha))
            {
                if (TimeSpan.TryParse(horaStr, out TimeSpan hora))
                {
                    var dt = fecha.Date + hora;
                    return dt.ToString("yyyyMMdd HHmmss");
                }
                return fecha.ToString("yyyyMMdd") + " 000000";
            }
            return DateTime.Now.ToString("yyyyMMdd HHmmss");
        }

        private static string SanitizarNombre(string nombre)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                nombre = nombre.Replace(c, '_');
            return nombre;
        }

        // ─── Auto-numeración si el archivo ya existe ──────────────────────────
        private static string ResolverRutaUnica(string directorio, string nombreBase)
        {
            string ruta = Path.Combine(directorio, $"{nombreBase}.pdf");
            if (!File.Exists(ruta)) return ruta;

            int n = 1;
            while (true)
            {
                ruta = Path.Combine(directorio, $"{nombreBase} ({n}).pdf");
                if (!File.Exists(ruta)) return ruta;
                n++;
            }
        }

        // ─── Condición (filtro de artículos incluidos en el informe) ──────────
        private string CondicionSeleccionada()
            => (CmbCondicion.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Todos los artículos";

        private static bool CumpleCondicion(double stock, string condicion) => condicion switch
        {
            "Artículos con stock negativo" => stock < 0,
            "Artículos con stock"          => stock > 0,
            "Artículos con stock 0"        => stock == 0,
            _                               => true, // "Todos los artículos"
        };

        // ─── Handlers de cambio ───────────────────────────────────────────────
        private void TxtNombre_TextChanged(object sender, TextChangedEventArgs e)
            => ActualizarNombreArchivo();

        private void TxtFechaHora_TextChanged(object sender, TextChangedEventArgs e)
            => ActualizarNombreArchivo();

        // ─── Botón Crear ──────────────────────────────────────────────────────
        private void BtnCrearInforme_Click(object sender, RoutedEventArgs e)
        {
            string nombre = TxtNombre.Text.Trim();

            // ── Validar fecha ─────────────────────────────────────────────
            if (!DateTime.TryParseExact(TxtFecha.Text.Trim(),
                    new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime fechaBase))
            {
                MessageBox.Show("Fecha inválida. Use el formato dd/mm/aaaa.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtFecha.Focus();
                return;
            }

            // ── Combinar fecha + hora de corte ────────────────────────────
            DateTime fechaCorte = fechaBase.Date;
            if (TimeSpan.TryParse(TxtHora.Text.Trim(), out TimeSpan hora))
                fechaCorte = fechaBase.Date + hora;

            // ── Construir nombre base del archivo ─────────────────────────
            string prefijoFecha = ObtenerPrefijoFecha();
            string nombreBase   = string.IsNullOrEmpty(nombre)
                ? $"{prefijoFecha} informe"
                : $"{prefijoFecha} informe {nombre}";
            nombreBase = SanitizarNombre(nombreBase);

            // ── Explorador de guardado ────────────────────────────────────
            var dlg = new SaveFileDialog
            {
                Title            = "Guardar informe PDF",
                FileName         = nombreBase,
                DefaultExt       = ".pdf",
                Filter           = "PDF (*.pdf)|*.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog(this) != true) return;

            // ── Auto-numeración si el archivo ya existe ───────────────────
            string directorio   = Path.GetDirectoryName(dlg.FileName) ?? "";
            string sinExt       = Path.GetFileNameWithoutExtension(dlg.FileName);
            string rutaFinal    = ResolverRutaUnica(directorio, sinExt);

            try
            {
                BtnCrearInforme.IsEnabled = false;
                BtnCrearInforme.Content   = "Generando…";

                GenerarPdf(rutaFinal, fechaCorte);

                Close();
                Process.Start(new ProcessStartInfo(rutaFinal) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el informe:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCrearInforme.IsEnabled = true;
                BtnCrearInforme.Content   = "Crear Informe";
            }
        }

        // ─── Generación del PDF: catálogo agrupado por Producto & Familia, con
        // resumen de categorías al final — mismo estilo/estructura exacta que
        // InventariosGeneral.GenerarPdfInventario (banda celeste de grupo,
        // encabezado gris, tabla de totales por categoría alineada al borde
        // derecho de la columna Stock, fila "Total general" destacada). ────────
        private void GenerarPdf(string filePath, DateTime fechaCorte)
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
            const double anchoStock    = 75;

            var document = new PdfDocument();
            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;
            XGraphics gfx = XGraphics.FromPdfPage(page);

            double anchoTabla = page.Width - margen * 2;
            double anchoDesc  = anchoTabla - anchoN - anchoCodigo - anchoStock;
            double xN         = margen;
            double xCodigo    = xN + anchoN;
            double xDesc      = xCodigo + anchoCodigo;
            double xStock     = xDesc + anchoDesc;

            double y = margen;

            // Fila de datos: 4 columnas con sus líneas separadoras.
            void DibujarFilaDatos(string n, string codigo, string desc, string stock)
            {
                gfx.DrawRectangle(penLinea, xN, y, anchoTabla, altoFila);
                gfx.DrawLine(penLinea, xCodigo, y, xCodigo, y + altoFila);
                gfx.DrawLine(penLinea, xDesc,   y, xDesc,   y + altoFila);
                gfx.DrawLine(penLinea, xStock,  y, xStock,  y + altoFila);

                gfx.DrawString(n,      fontCuerpo, XBrushes.Black, new XRect(xN,          y, anchoN,          altoFila), XStringFormats.Center);
                gfx.DrawString(codigo, fontCuerpo, XBrushes.Black, new XRect(xCodigo + 4, y, anchoCodigo - 8, altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(desc,   fontCuerpo, XBrushes.Black, new XRect(xDesc + 4,   y, anchoDesc - 8,   altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(stock,  fontCuerpo, XBrushes.Black, new XRect(xStock + 4,  y, anchoStock - 8,  altoFila), XStringFormats.CenterRight);

                y += altoFila;
            }

            // Encabezado de columnas con fondo gris claro.
            void DibujarEncabezadoColumnas()
            {
                gfx.DrawRectangle(penLinea, brushHeader, xN, y, anchoTabla, altoHeader);
                gfx.DrawLine(penLinea, xCodigo, y, xCodigo, y + altoHeader);
                gfx.DrawLine(penLinea, xDesc,   y, xDesc,   y + altoHeader);
                gfx.DrawLine(penLinea, xStock,  y, xStock,  y + altoHeader);

                gfx.DrawString("N°",          fontHeader, XBrushes.Black, new XRect(xN,          y, anchoN,          altoHeader), XStringFormats.Center);
                gfx.DrawString("Código",      fontHeader, XBrushes.Black, new XRect(xCodigo + 4, y, anchoCodigo - 8, altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Descripción", fontHeader, XBrushes.Black, new XRect(xDesc + 4,   y, anchoDesc - 8,   altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Stock",       fontHeader, XBrushes.Black, new XRect(xStock + 4,  y, anchoStock - 8,  altoHeader), XStringFormats.CenterRight);

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

            // Tabla de resumen por categoría: 2 columnas (Categoría / Stock total),
            // con la segunda alineada al borde derecho de la columna Stock de la
            // tabla de artículos.
            double xStockResumen   = xStock;
            double anchoCatResumen = xStockResumen - xN;

            void DibujarEncabezadoResumen()
            {
                gfx.DrawRectangle(penLinea, brushHeader, xN, y, anchoTabla, altoHeader);
                gfx.DrawLine(penLinea, xStockResumen, y, xStockResumen, y + altoHeader);
                gfx.DrawString("Categoría",   fontHeader, XBrushes.Black, new XRect(xN + 4,            y, anchoCatResumen - 8, altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Stock total", fontHeader, XBrushes.Black, new XRect(xStockResumen + 4, y, anchoStock - 8,      altoHeader), XStringFormats.CenterRight);
                y += altoHeader;
            }

            void DibujarFilaResumen(string categoria, string stock, bool destacado = false)
            {
                if (destacado) gfx.DrawRectangle(penLinea, brushCeleste, xN, y, anchoTabla, altoFila);
                else           gfx.DrawRectangle(penLinea, xN, y, anchoTabla, altoFila);
                gfx.DrawLine(penLinea, xStockResumen, y, xStockResumen, y + altoFila);

                var font = destacado ? fontGrupo : fontCuerpo;
                gfx.DrawString(categoria, font, XBrushes.Black, new XRect(xN + 4,            y, anchoCatResumen - 8, altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(stock,     font, XBrushes.Black, new XRect(xStockResumen + 4, y, anchoStock - 8,      altoFila), XStringFormats.CenterRight);

                y += altoFila;
            }

            // Espacio en blanco entre la lista de artículos y el resumen de categorías
            // (sin línea dibujada, solo separación vertical).
            const double altoSeparador = 16;
            void DibujarSeparador() { y += altoSeparador; }

            // Encabezado a redibujar al saltar de página: la tabla de artículos usa
            // DibujarEncabezadoColumnas, la sección de resumen pasa a usar
            // DibujarEncabezadoResumen una vez que empieza (ver más abajo).
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

            string nombreInforme = TxtNombre.Text.Trim();
            string tituloInforme = string.IsNullOrEmpty(nombreInforme) ? "Informe de Artículos" : nombreInforme;
            string condicion     = CondicionSeleccionada();

            gfx.DrawString(tituloInforme, fontTitulo, XBrushes.Black, new XPoint(margen, y + 12));
            y += 20;
            gfx.DrawString($"Fecha de corte: {fechaCorte:dd/MM/yyyy HH:mm:ss}    Condición: {condicion}",
                fontCuerpo, XBrushes.Black, new XPoint(margen, y + 10));
            y += 22;

            DibujarEncabezadoColumnas();

            // ── Recolectar datos (según la Condición elegida) ─────────────
            var lineas = new List<(string prodDesc, string famDesc, string codigo, string catDesc, string desc, double stock, int indice)>();

            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string codigo = Sql.ArticulosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
                string desc   = Sql.ArticulosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                string modelo = Sql.ArticulosObj.ObtenerItem("modelo",      id)?.ToString() ?? "";
                string famId  = Sql.ArticulosObj.ObtenerItem("familia",     id)?.ToString() ?? "";
                string catId  = Sql.ArticulosObj.ObtenerItem("Categoria",   id)?.ToString() ?? "";

                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",   famId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto",      famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion",  prodId)?.ToString() ?? "";
                string catDesc  = Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "";

                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);
                double stock        = StockCalculator.ContarStock(id, fechaCorte);

                if (!CumpleCondicion(stock, condicion)) continue;

                // Índice del artículo dentro de su familia: ordena las filas dentro de
                // cada grupo Producto+Familia (antes salían en orden de lectura).
                int indice = int.TryParse(Sql.ArticulosObj.ObtenerItem("indice", id)?.ToString(), out int ix) ? ix : 0;

                lineas.Add((prodDesc, famDesc, codigo, catDesc, descCompleta, stock, indice));
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

                // Dentro del grupo Producto+Familia, por índice dentro de la familia
                // (antes salían en el orden en que se leyeron de la base).
                foreach (var l in grupo.OrderBy(x => x.indice))
                {
                    AsegurarEspacio(altoFila);
                    n++;
                    DibujarFilaDatos(n.ToString(), l.codigo, l.desc, l.stock.ToString("0.##"));
                }
            }

            // ── Resumen por categoría + total general, al final del informe ──────
            var resumenCategorias = lineas
                .GroupBy(l => string.IsNullOrEmpty(l.catDesc) ? "(sin categoría)" : l.catDesc)
                .Select(g => (categoria: g.Key, stock: g.Sum(x => x.stock)))
                .OrderBy(x => x.categoria, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (resumenCategorias.Count > 0)
            {
                dibujarEncabezadoPagina = () => { };
                AsegurarEspacio(altoSeparador + altoGrupo + altoHeader);
                DibujarSeparador();
                DibujarBandaGrupo("Resumen por categoría");
                DibujarEncabezadoResumen();
                dibujarEncabezadoPagina = DibujarEncabezadoResumen;

                double totalStock = 0;
                foreach (var (categoria, stock) in resumenCategorias)
                {
                    AsegurarEspacio(altoFila);
                    DibujarFilaResumen(categoria, stock.ToString("0.##"));
                    totalStock += stock;
                }

                AsegurarEspacio(altoFila);
                DibujarFilaResumen("Total general", totalStock.ToString("0.##"), destacado: true);
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
    }
}
