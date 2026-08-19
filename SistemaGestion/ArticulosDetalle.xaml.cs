using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class ArticulosDetalle : UserControl
    {
        public event Action? Cerrando;

        private static SqlData Sql => SqlData.Instance;

        private readonly ArticulosGeneral? _padre;
        private readonly string _idEditar;
        private bool _hayCambios = false;
        private bool _cargando   = true;

        private bool _iniciado = false;
        private readonly string _tituloTab;
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle).
        private bool _guardando = false;

        /// <summary>ID del artículo recién creado (nuevo o insertado).</summary>
        public string? ItemCreadoId { get; private set; }

        public ArticulosDetalle(ArticulosGeneral? padre = null, string idEditar = "", string tituloTab = "")
        {
            InitializeComponent();
            _padre     = padre;
            _idEditar  = idEditar;
            _tituloTab = tituloTab;
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;
            Box_Identificador.IsEnabled = false;

            if (AppState.EventoFormularioA == "modificar")
            {
                LblTitulo.Text                = "Editar Artículo";
                Box_Codigo.IsEnabled          = false;
                BtnVerMovimientos.Visibility  = Visibility.Visible;
                CargarParaEditar();
            }
            else if (AppState.EventoFormularioA == "insertar")
            {
                LblTitulo.Text                = "Insertar Artículo";
                Box_Codigo.IsEnabled          = true;
                BtnVerMovimientos.Visibility  = Visibility.Collapsed;
                CargarParaInsertar();
            }
            else
            {
                LblTitulo.Text                = "Nuevo Artículo";
                Box_Codigo.IsEnabled          = true;
                BtnVerMovimientos.Visibility  = Visibility.Collapsed;
                CargarParaNuevo();
            }

            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            string id = _idEditar;
            Box_Identificador.Text = id;
            Box_Codigo.Text        = Sql.ArticulosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
            Box_Indice.Text        = Sql.ArticulosObj.ObtenerItem("indice",      id)?.ToString() ?? "";

            string famId = Sql.ArticulosObj.ObtenerItem("familia", id)?.ToString() ?? "";
            Box_Identificador_Familia.Text = Sql.FamiliasObj.ObtenerItem("codigo", famId)?.ToString() ?? "";
            ActualizarDescripcionFamilia();

            string indId = Sql.ArticulosObj.ObtenerItem("industria", id)?.ToString() ?? "";
            Box_Identificador_Industria.Text = Sql.IndustriasObj.ObtenerItem("codigo", indId)?.ToString() ?? "";
            ActualizarDescripcionIndustria();

            string catId = Sql.ArticulosObj.ObtenerItem("Categoria", id)?.ToString() ?? "";
            Box_Identificador_Categoria.Text = Sql.CategoriasObj.ObtenerItem("codigo", catId)?.ToString() ?? "";
            ActualizarDescripcionCategoria();

            Box_Descripcion.Text = Sql.ArticulosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
            Box_Modelo.Text      = Sql.ArticulosObj.ObtenerItem("modelo",      id)?.ToString() ?? "";
            Box_Observacion.Text = Sql.ArticulosObj.ObtenerItem("observacion", id)?.ToString() ?? "";

            // "Ocultar" solo si quedó explícitamente así; cualquier otro valor (incluido
            // null, de artículos guardados antes de que existiera esta columna) cuenta
            // como "Mostrar" — para que agregar esta columna no vacíe de golpe las
            // plantillas de artículos ya cargados.
            string estadoV = Sql.ArticulosObj.ObtenerItem("estado", id)?.ToString()?.Trim().ToLowerInvariant() ?? "";
            CboEstadoV.SelectedIndex = estadoV == "ocultar" ? 1 : 0;
        }

        private void CargarParaNuevo()
        {
            Box_Identificador.Text = Guid.NewGuid().ToString();
            Box_Indice.Text        = "1";
            CboEstadoV.SelectedIndex = 0; // Mostrar, por defecto.
        }

        private void CargarParaInsertar()
        {
            Box_Identificador.Text = Guid.NewGuid().ToString();

            // Tomar familia (código) e indice del artículo de referencia
            string famRefId = Sql.ArticulosObj.ObtenerItem("familia", _idEditar)?.ToString() ?? "";
            string indRef   = Sql.ArticulosObj.ObtenerItem("indice",  _idEditar)?.ToString() ?? "1";

            Box_Identificador_Familia.Text = Sql.FamiliasObj.ObtenerItem("codigo", famRefId)?.ToString() ?? "";
            Box_Indice.Text                = indRef;
            ActualizarDescripcionFamilia();
            CboEstadoV.SelectedIndex = 0; // Mostrar, por defecto.

            // Bloquear campos que no se deben cambiar en modo insertar
            Box_Identificador_Familia.IsEnabled = false;
            Box_Indice.IsEnabled                = false;
            BtnVerFamilias.IsEnabled            = false;
        }

        // ─── Resolver id (UUID) de los referidos a partir del código digitado ─
        private string ResolverFamiliaId()
        {
            string c = Box_Identificador_Familia.Text.Trim();
            return c == "" ? "" : Sql.FamiliasObj.BuscarIdentificador("codigo", c);
        }

        private string ResolverIndustriaId()
        {
            string c = Box_Identificador_Industria.Text.Trim();
            return c == "" ? "" : Sql.IndustriasObj.BuscarIdentificador("codigo", c);
        }

        private string ResolverCategoriaId()
        {
            string c = Box_Identificador_Categoria.Text.Trim();
            return c == "" ? "" : Sql.CategoriasObj.BuscarIdentificador("codigo", c);
        }

        // ─── Cuando cambia la familia en modo "nuevo": indice = max(familia) + 1 ───
        private void RecalcularIndicePorFamilia()
        {
            string famId = ResolverFamiliaId();
            if (string.IsNullOrEmpty(famId)) return;

            int indiceMax = 0;
            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string fam = Sql.ArticulosObj.ObtenerItem("familia", id)?.ToString() ?? "";
                if (fam != famId) continue;

                int ind = Convert.ToInt32(Sql.ArticulosObj.ObtenerItem("indice", id) ?? 0);
                if (ind > indiceMax) indiceMax = ind;
            }

            // También considerar los índices reservados por artículos eliminados, para
            // sugerir un índice que NO reutilice uno reservado (queda más allá de todos).
            foreach (int r in Sql.ArticulosObj.IndicesNoNormales("familia", famId))
                if (r > indiceMax) indiceMax = r;

            Box_Indice.Text = (indiceMax + 1).ToString();
        }

        // ─── Actualizar descripciones de referidos ────────────────────────────
        private void ActualizarDescripcionFamilia()
        {
            string famId = ResolverFamiliaId();
            Box_Familia_Descripcion.Text = string.IsNullOrEmpty(famId)
                ? ""
                : Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? "";
        }

        private void ActualizarDescripcionIndustria()
        {
            string id = ResolverIndustriaId();
            Box_Industria_Descripcion.Text = string.IsNullOrEmpty(id)
                ? ""
                : Sql.IndustriasObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
        }

        private void ActualizarDescripcionCategoria()
        {
            string id = ResolverCategoriaId();
            Box_Categoria_Descripcion.Text = string.IsNullOrEmpty(id)
                ? ""
                : Sql.CategoriasObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
        }

        // ─── Detectar cambios ─────────────────────────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        private void Box_Familia_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cargando) return;
            _hayCambios = true;
            ActualizarDescripcionFamilia();

            // En modo "nuevo" y "modificar": auto-sugerir indice = max(familia) + 1
            if (AppState.EventoFormularioA is "nuevo" or "modificar")
                RecalcularIndicePorFamilia();
        }

        private void Box_Industria_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cargando) return;
            _hayCambios = true;
            ActualizarDescripcionIndustria();
        }

        private void Box_Categoria_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cargando) return;
            _hayCambios = true;
            ActualizarDescripcionCategoria();
        }

        private void CboEstadoV_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Validación de entrada ────────────────────────────────────────────
        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // ─── Ver familias (modo selector, en pestaña) ─────────────────────────
        private void BtnVerFamilias_Click(object sender, RoutedEventArgs e)
        {
            FamiliasGeneral.OpenAsTab(Window.GetWindow(this)!, id =>
            {
                // Asignar el ID — el TextChanged handler llama ActualizarDescripcionFamilia()
                if (!string.IsNullOrEmpty(id)) Box_Identificador_Familia.Text = id;
            }, contexto: _tituloTab, llamador: this);
        }

        // ─── Ver industrias (modo selector, en pestaña) ───────────────────────
        private void BtnVerIndustrias_Click(object sender, RoutedEventArgs e)
        {
            IndustriasGeneral.OpenAsTab(Window.GetWindow(this)!, id =>
            {
                if (!string.IsNullOrEmpty(id)) Box_Identificador_Industria.Text = id;
            }, contexto: _tituloTab, llamador: this);
        }

        // ─── Ver categorías (modo selector, en pestaña) ───────────────────────
        private void BtnVerCategorias_Click(object sender, RoutedEventArgs e)
        {
            CategoriasGeneral.OpenAsTab(Window.GetWindow(this)!, id =>
            {
                if (!string.IsNullOrEmpty(id)) Box_Identificador_Categoria.Text = id;
            }, contexto: _tituloTab, llamador: this);
        }

        // ─── Ver movimientos del artículo ─────────────────────────────────────
        private void BtnVerMovimientos_Click(object sender, RoutedEventArgs e)
        {
            MovimientosGeneral.OpenAsTab(Window.GetWindow(this)!, Box_Codigo.Text.Trim());
        }

        private bool SinPestañasRelacionadas()
        {
            var c = Window.GetWindow(this) as ConsolaMovimientos;
            return c == null || c.ConfirmarCierrePestañasRelacionadas(_tituloTab);
        }

        // ─── Guardar ─────────────────────────────────────────────────────────
        // "Mostrar"/"Ocultar" (CboEstadoV) -> "mostrar"/"ocultar" para la columna
        // articulos.estado: filtra qué artículos entran en las plantillas Excel/PDF
        // (VisorEmpresa/PreciosGeneral y SistemaGestion/InventariosGeneral).
        private string EstadoVSeleccionado => CboEstadoV.SelectedIndex == 1 ? "ocultar" : "mostrar";

        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!SinPestañasRelacionadas()) return false;
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;

                if (string.IsNullOrEmpty(ResolverFamiliaId()))
                {
                    MessageBox.Show("Debe asignar una familia existente al artículo.", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (string.IsNullOrEmpty(ResolverCategoriaId()))
                {
                    MessageBox.Show("Debe asignar una categoría existente al artículo.", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                return AppState.EventoFormularioA switch
                {
                    "modificar" => GuardarEditar(),
                    "insertar"  => GuardarInsertar(),
                    _            => GuardarNuevo()
                };
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
                Sql.ArticulosObj.EstablecerItem("indice",     id, Box_Indice.Text);
                Sql.ArticulosObj.EstablecerItem("familia",    id, ResolverFamiliaId());
                Sql.ArticulosObj.EstablecerItem("industria",  id, ResolverIndustriaId());
                Sql.ArticulosObj.EstablecerItem("Categoria",  id, ResolverCategoriaId());
                Sql.ArticulosObj.EstablecerItem("descripcion",id, Box_Descripcion.Text);
                Sql.ArticulosObj.EstablecerItem("modelo",     id, Box_Modelo.Text);
                Sql.ArticulosObj.EstablecerItem("estado",    id, EstadoVSeleccionado);
                Sql.ArticulosObj.EstablecerItem("observacion",id, Box_Observacion.Text);
                Sql.ArticulosObj.EstablecerItem("edicion",    id, DateTime.Now);
                Sql.ArticulosObj.EstablecerItem("usuarioE",   id, AppState.UsuarioActivo);

                Sql.ArticulosObj.OrdenarData(("familia", false), ("indice", false));
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
                MessageBox.Show("Debe asignar un código al artículo.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                if (Sql.ArticulosObj.CodigoExiste(codigo))
                {
                    MessageBox.Show("El código ya existe", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                string id = Box_Identificador.Text.Trim();
                Sql.ArticulosObj.Nuevo(id);
                Sql.ArticulosObj.EstablecerItem("codigo",     id, codigo);
                Sql.ArticulosObj.EstablecerItem("indice",     id, Box_Indice.Text);
                Sql.ArticulosObj.EstablecerItem("familia",    id, ResolverFamiliaId());
                Sql.ArticulosObj.EstablecerItem("industria",  id, ResolverIndustriaId());
                Sql.ArticulosObj.EstablecerItem("Categoria",  id, ResolverCategoriaId());
                Sql.ArticulosObj.EstablecerItem("descripcion",id, Box_Descripcion.Text);
                Sql.ArticulosObj.EstablecerItem("modelo",     id, Box_Modelo.Text);
                Sql.ArticulosObj.EstablecerItem("estado",    id, EstadoVSeleccionado);
                Sql.ArticulosObj.EstablecerItem("observacion",id, Box_Observacion.Text);
                Sql.ArticulosObj.EstablecerItem("emision",    id, DateTime.Now);
                Sql.ArticulosObj.EstablecerItem("edicion",    id, DateTime.Now);
                Sql.ArticulosObj.EstablecerItem("usuario",    id, AppState.UsuarioActivo);
                Sql.ArticulosObj.EstablecerItem("usuarioE",   id, AppState.UsuarioActivo);

                Sql.ArticulosObj.OrdenarData(("familia", false), ("indice", false));

                // Artículo nuevo → sincronizar AppSheets (todas las sucursales).
                ArticulosGeneral.SincronizarAppsheetsTrasCambio();

                ItemCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool GuardarInsertar()
        {
            string codigo = Box_Codigo.Text.Trim();
            if (string.IsNullOrEmpty(codigo))
            {
                MessageBox.Show("Debe asignar un código al artículo.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                if (Sql.ArticulosObj.CodigoExiste(codigo))
                {
                    MessageBox.Show("El código ya existe", "Consola",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                string famNuevaId = ResolverFamiliaId();
                int    indNuevo   = Convert.ToInt32(Box_Indice.Text);

                // Bump: subir 1 a todos los indices >= indNuevo dentro de la misma familia
                int uf = Sql.ArticulosObj.ContarFilas;
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.ArticulosObj.Mover(i);
                    if (idObj == null) continue;
                    string idIt = idObj.ToString()!;

                    string fam = Sql.ArticulosObj.ObtenerItem("familia", idIt)?.ToString() ?? "";
                    if (fam != famNuevaId) continue;

                    int ind = Convert.ToInt32(Sql.ArticulosObj.ObtenerItem("indice", idIt) ?? 0);
                    if (ind >= indNuevo)
                        Sql.ArticulosObj.EstablecerItem("indice", idIt, ind + 1);
                }

                // Crear el nuevo artículo en la posición indNuevo
                string id = Box_Identificador.Text.Trim();
                Sql.ArticulosObj.Nuevo(id);
                Sql.ArticulosObj.EstablecerItem("codigo",     id, codigo);
                Sql.ArticulosObj.EstablecerItem("indice",     id, indNuevo);
                Sql.ArticulosObj.EstablecerItem("familia",    id, famNuevaId);
                Sql.ArticulosObj.EstablecerItem("industria",  id, ResolverIndustriaId());
                Sql.ArticulosObj.EstablecerItem("Categoria",  id, ResolverCategoriaId());
                Sql.ArticulosObj.EstablecerItem("descripcion",id, Box_Descripcion.Text);
                Sql.ArticulosObj.EstablecerItem("modelo",     id, Box_Modelo.Text);
                Sql.ArticulosObj.EstablecerItem("estado",    id, EstadoVSeleccionado);
                Sql.ArticulosObj.EstablecerItem("observacion",id, Box_Observacion.Text);
                Sql.ArticulosObj.EstablecerItem("emision",    id, DateTime.Now);
                Sql.ArticulosObj.EstablecerItem("edicion",    id, DateTime.Now);
                Sql.ArticulosObj.EstablecerItem("usuario",    id, AppState.UsuarioActivo);
                Sql.ArticulosObj.EstablecerItem("usuarioE",   id, AppState.UsuarioActivo);

                // Reasignar índices de la familia por su orden, saltando los reservados
                // por artículos eliminados (sin reutilizarlos).
                ArticulosGeneral.RenumerarFamilia(famNuevaId);

                Sql.ArticulosObj.OrdenarData(("familia", false), ("indice", false));

                // Artículo insertado → sincronizar AppSheets (todas las sucursales).
                ArticulosGeneral.SincronizarAppsheetsTrasCambio();

                ItemCreadoId = id;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (Guardar()) { _hayCambios = false; Cerrando?.Invoke(); }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (!SinPestañasRelacionadas()) return;
            _hayCambios = false; Cerrando?.Invoke();
        }

        // ─── Llamado por el botón X de la pestaña para verificar cambios ──────
        public void IntentarCerrar()
        {
            if (!SinPestañasRelacionadas()) return;
            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No) Cerrando?.Invoke();
        }
    }
}
