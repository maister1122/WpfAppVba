# CONTEXT.md — Sistema de Gestión

## Objetivo General del Proyecto
Aplicación de escritorio Windows (WPF, .NET 8) para gestión empresarial: artículos, pedidos (ventas/compras), traspasos entre sucursales, correcciones de stock, terceros (clientes/proveedores), sucursales, inventarios y precios. Reemplaza un sistema VBA/Excel anterior.

## Stack Tecnológico
- **Framework**: .NET 8.0 Windows / WPF (XAML)
- **Lenguaje**: C# 12 con `Nullable enable` e `ImplicitUsings enable`
- **Base de datos**: SQL Server — acceso vía `Microsoft.Data.SqlClient 7.0.1`
- **Reportes Excel**: `ClosedXML 0.102.2`
- **IDE recomendado**: Visual Studio 2022 / VS Code + extensión C#
- **Proyecto**: `SistemaGestion/SistemaGestion.csproj` (renombrado desde `WpfAppVba/WpfAppVba.csproj` — el repo de GitHub sigue llamándose `maister1122/WpfAppVba`, solo cambió el proyecto/carpeta/namespace de adentro)
- **Tercer proyecto (desde sesión 2026-07-27)**: `ConexionBroker/ConexionBroker.csproj` — servicio ASP.NET Core Minimal API, `net8.0` (multiplataforma, NO `-windows`), desplegado aparte en un VPS Linux. Ver sección "ConexionBroker (servicio de autenticación)" más abajo.
- **Rama de desarrollo activa**: `master` (desde sesión 2026-06-18 en adelante se trabaja directo en `master`; las ramas `claude/*` no se pushean al remoto — ver CLAUDE.md)

## Lo Que Está Implementado y Funciona

### Infraestructura de UI (ConsolaMovimientos)
- Ventana única (`ConsolaMovimientos`) con sidebar de navegación y `TabControl` de pestañas dinámicas.
- Sistema de pestañas con deduplicación por clave (`AbrirPestaña`, `CerrarPestaña`, `CerrarPestañaPorClave`, `SeleccionarPestaña`).
- **Registro de pestañas por sección**: cada sección del sidebar conserva sus propias pestañas abiertas al navegar (`_pestañasPorSeccion`).
- **Memoria de pestaña activa por sección**: al volver a una sección, se restaura la última pestaña enfocada (`_pestañaSeleccionadaPorSeccion`).
- Tema visual dinámico (recursos `ThemeBgPrincipal`, `ThemeBtnSecBg`, etc.) configurable desde Configuración.

### Secciones del sidebar (orden visual)

> **Reestructurado en sesión 2026-07-28/29**: los 4 módulos de documentos se
> partieron en dos entradas cada uno, bajo un encabezado de sección
> (estilo `NavSeccion`). Cada entrada abre el MISMO panel `*General`, pero
> construido con su `tipoMovimiento` fijo, así que el filtro de movimiento
> dejó de ser un combo en pantalla.

| Botón | Panel fijo | Detalle en pestaña | Selector en pestaña |
|-------|-----------|-------------------|---------------------|
| 📊 Dashboard | `DashboardGeneral` | — | — |
| 📋 Artículos | `ArticulosGeneral` | `ArticulosDetalle` (pestaña) | — |
| *PEDIDOS* | | | |
| 🛒 Ventas | `PedidosGeneral("venta")` | `PedidosDetalle` (pestaña) | TercerosGeneral, ArticulosGeneral |
| 📥 Compras | `PedidosGeneral("compra")` | `PedidosDetalle` (pestaña) | TercerosGeneral, ArticulosGeneral |
| *TRASPASOS* | | | |
| 📦 Entradas | `TraspasosGeneral("entrada")` | `TraspasosDetalle` (pestaña) | SucursalesGeneral, ArticulosGeneral |
| 📤 Salidas | `TraspasosGeneral("salida")` | `TraspasosDetalle` (pestaña) | SucursalesGeneral, ArticulosGeneral |
| *CORRECCIONES* | | | |
| 🔧 Repuestas | `CorreccionesGeneral("repuesta")` | `CorreccionesDetalle` (pestaña) | ArticulosGeneral (pestaña) |
| 🔧 Retirados | `CorreccionesGeneral("retirado")` | `CorreccionesDetalle` (pestaña) | ArticulosGeneral (pestaña) |
| *FACTURAS* | | | |
| 🧾 Ingresos | `FacturasGeneral("ingreso")` | `FacturasDetalle` (pestaña) | TercerosGeneral (pestaña-selector) |
| 🧾 Egresos | `FacturasGeneral("egreso")` | `FacturasDetalle` (pestaña) | TercerosGeneral (pestaña-selector) |
| *(separador)* | | | |
| 👥 Terceros | `TercerosGeneral` | `TercerosDetalle` (pestaña) | — |
| 🏢 Sucursales | `SucursalesGeneral` | `SucursalesDetalle` (pestaña) | RegionesGeneral (pestaña-selector) |
| 🗂 Familias | `FamiliasGeneral` | `FamiliasDetalle` (pestaña) | — |
| 📦 Productos | `ProductosGeneral` | `ProductosDetalle` (pestaña) | — |
| 🏭 Industrias | `IndustriasGeneral` | `IndustriasDetalle` (pestaña) | — |
| 🏷 Categorías | `CategoriasGeneral` | `CategoriasDetalle` (pestaña) | — |
| 📊 Inventarios | `InventariosGeneral` | `InventariosDetalle` (pestaña) | ArticulosGeneral (pestaña) |
| 🌐 Regiones | `RegionesGeneral` | `RegionesDetalle` (pestaña) | — |
| 💲 Precios | `PreciosGeneral` | `PreciosDetalle` (pestaña) | RegionesGeneral (pestaña-selector) |
| 🏢 Empresas | `EmpresasGeneral` | `EmpresasDetalle` (pestaña) | — |
| 👤 Usuarios | `UsuariosGeneral` | `UsuariosDetalle` (pestaña) | — |
| ⚙ Configuración | `Configuracion` (embedded) | — | — |

### Paneles declarados en ConsolaMovimientos
```csharp
private readonly ArticulosGeneral    _panelArticulos     = new();
// Documentos: dos instancias por módulo, cada una fijada a su movimiento
// (sesión 2026-07-28/29 — antes era un panel único con combo "Movimiento").
private          PedidosGeneral      _panelVentas        = new("venta");
private          PedidosGeneral      _panelCompras       = new("compra");
private          TraspasosGeneral    _panelEntradas      = new("entrada");
private          TraspasosGeneral    _panelSalidas       = new("salida");
private          CorreccionesGeneral _panelRepuestas     = new("repuesta");
private          CorreccionesGeneral _panelRetirados     = new("retirado");
private          FacturasGeneral     _panelIngresos      = new("ingreso");
private          FacturasGeneral     _panelEgresos       = new("egreso");
private readonly TercerosGeneral     _panelTerceros      = new();
private readonly SucursalesGeneral   _panelSucursales    = new();
private readonly FamiliasGeneral     _panelFamilias      = new();
private readonly ProductosGeneral    _panelProductos     = new();
private readonly IndustriasGeneral   _panelIndustrias    = new();
private readonly CategoriasGeneral   _panelCategorias    = new();
private readonly InventariosGeneral  _panelInventarios   = new();
private readonly PreciosGeneral      _panelPrecios       = new();
private readonly RegionesGeneral     _panelRegiones      = new();
private readonly EmpresasGeneral     _panelEmpresas      = new();
private readonly Configuracion       _panelConfiguracion = new();
private          DashboardGeneral    _panelDashboard     = new(); // sección "dashboard"
private          UsuariosGeneral     _panelUsuarios      = new(); // solo admin; recreado en RecargarContexto
```

### Patrones de comportamiento implementados
- `_iniciado` flag en todos los paneles (evita recarga al cambiar de pestaña).
- `Cerrando` event + `IntentarCerrar()` en todos los detalles embebidos en pestañas.
- Selector tabs deduplicados por contexto: `seleccionar-tercero|{contexto}`, `seleccionar-sucursal|{contexto}`, `buscar-articulo|{contexto}`, `importar-articulos|{contexto}`, `seleccionar-region|{contexto}`.
- Botones de gestión (Nuevo/Editar/Eliminar) ocultos en modo selector; botón "Seleccionar" visible solo en modo selector; "Actualizar" siempre visible.
- Actualización incremental del grid tras crear/editar/eliminar (sin recargar todo).
- **Nombres de botones CRUD específicos por entidad**: todos los formularios General usan etiquetas explícitas ("Nuevo Artículo", "Editar Traspaso", "Eliminar Corrección", etc.).
- **Botones Guardar/Cancelar en detalles**: posición inferior izquierda, estilo uniforme — Guardar `#1A73E8`/blanco, Cancelar `ThemeBtnSecBg`/`ThemeBtnSecFg`, ambos con `Cursor="Hand"`.

## Decisiones de Arquitectura Importantes

### 1. Ventana única (Single-Window UI)
`ConsolaMovimientos` es la **única ventana OS**. Todo se embebe en pestañas. No hay ventanas secundarias de gestión principal. Todos los detalles están embebidos en pestañas; no hay ventanas secundarias de gestión principal.

### 2. Patrón `Cerrando` + `IntentarCerrar`
```csharp
// En el UserControl (detalle):
public event Action? Cerrando;
public void IntentarCerrar() {
    if (!_hayCambios) { Cerrando?.Invoke(); return; }
    var res = MessageBox.Show("¿Guardar cambios?", ...);
    if (res == Yes && Guardar()) Cerrando?.Invoke();
    else if (res == No) Cerrando?.Invoke();
}
// BtnGuardar: if (Guardar()) Cerrando?.Invoke();
// BtnCancelar: _hayCambios = false; Cerrando?.Invoke();
```
El tab X button debe llamar `IntentarCerrar()` en lugar de cerrar directamente (para proteger cambios no guardados). El `Cerrando +=` del abridor hace el cleanup.

### 3. Deduplicación de pestañas por clave
```csharp
consola.AbrirPestaña($"Pedido {docSel}", dlg, $"pedido-{docSel}");
// Si ya existe una pestaña con esa clave, solo la enfoca
```

### 4. Selector tabs con contexto (callback-based)
```csharp
// OpenAsTab con callback — no usa estado global, sino lambda:
RegionesGeneral.OpenAsTab(
    Window.GetWindow(this)!,
    id => { Box_Referido_Codigo.Text = id; },  // callback al seleccionar
    contexto: _tituloTab,
    llamador: this
);
```
Clave de deduplicación: `seleccionar-region|{_tituloTab}`. Un pedido abierto puede tener exactamente un "Seleccionar Tercero (Pedido 34)" y otro "Seleccionar Tercero (Pedido 50)".

### 5. `_iniciado` flag
```csharp
Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; CargarDatos(); ConfigurarModo(); };
```
WPF re-dispara `Loaded` cuando un UserControl se mueve entre pestañas del TabControl. Sin este guard, los datos se recargarían al cambiar de sección.

### 6. `OpenAsTab` (patrón selector)
```csharp
// Método estático en el General:
public static void OpenAsTab(Window ventana, Action<string>? callback, string contexto, UserControl llamador)
```
- Sin callback → modo normal (Nuevo/Editar/Eliminar visibles, Seleccionar oculto).
- Con callback → modo selector (Nuevo/Editar/Eliminar ocultos, Seleccionar visible en pie izquierdo).

### 7. Registro de pestañas por sección
```csharp
private readonly Dictionary<string, List<TabItem>> _pestañasPorSeccion = new()
{
    ["articulos"] = new(), ["pedidos"] = new(), ["traspasos"] = new(),
    ["correcciones"] = new(), ["facturas"] = new(), ["terceros"] = new(), ["sucursales"] = new(),
    ["familias"] = new(), ["productos"] = new(), ["industrias"] = new(),
    ["categorias"] = new(), ["inventarios"] = new(), ["precios"] = new(),
    ["regiones"] = new(), ["configuracion"] = new(),
};
```
Al cambiar sección: guardar pestañas actuales + pestaña activa → cambiar panel fijo → restaurar pestañas + pestaña activa de la nueva sección.

### 8. Convención de botones CRUD
Todos los `XxxGeneral.xaml` usan etiquetas que incluyen el nombre de la entidad:
- Nuevo/Nueva `{Entidad}` (azul `#1A73E8`)
- Insertar `{Entidad}` (azul, solo en Artículos)
- Editar `{Entidad}` (secundario `ThemeBtnSecBg`)
- Eliminar `{Entidad}` (rojo `#D93025`)
- Actualizar (secundario, siempre visible)
- Seleccionar (visible solo en modo selector)

## Códigos de documento: sin signo, por empresa y movimiento — desde sesión 2026-07-28/29

El `codigo` visible de un documento dejó de ser `signo + correlativo por sucursal`.
Ahora es el **correlativo pelado dentro de su ámbito**, y hay **tres piezas que
tienen que coincidir siempre** — si una se cambia sola, los códigos se mueven de
lugar en cuanto se regenera:

| Pieza | Archivo (copia propia en cada proyecto) | Qué hace |
|-------|------------------------------------------|----------|
| Cubos de movimiento | `MovimientoSql.cs` | Única fuente de verdad. Expone cada criterio **dos veces**: `Caso*(alias)` → expresión SQL `CASE` (para el servidor) y `Normalizar*(mov)` → el mismo criterio en C# (para la app). Normaliza además el vocabulario viejo al nuevo. |
| Documento nuevo | `CodigoDocumento.cs` | Calcula el código al crear (MAX + 1) y lo **vuelve a revisar al guardar**. |
| Renumerado masivo | `CodigoRegenerator.cs` | Botón "🔢 Regenerar códigos" de VisorEmpresa: renumera 1..N lo existente. |

**Ámbitos (idénticos en las tres piezas):**
- `documentosP` / `documentosC` / `documentosF` → correlativo por **(empresa activa, movimiento)**: venta/compra, repuesta/retirado, ingreso/egreso. Cada cubo lleva su propio 1..N.
- `documentosI` / `documentosT` / `documentosL` → correlativo por **empresa activa**, sin partir por movimiento.
- La empresa se resuelve por cascada: `sucursal → sucursales.empresa` (P/C/F/I), `emitido → sucursales.empresa` (T), o la columna `empresa` directa (L, que es `nvarchar` y guarda el id como texto).
- Las **tablas maestras** siguen con numeración global 1..N por `descripcion` — no se filtran por empresa.

**Detalles que no son obvios:**
- **Criterio de mercadería, no de plata**: en facturas una *compra* es **ingreso** (la mercadería entra) y una *venta* es **egreso** (sale). Corregido explícitamente por el usuario en esta sesión — es al revés de lo que sugiere el flujo de dinero.
- **Revisión al guardar**: el código se fija al abrir el formulario, así que entre ese momento y el guardado otro usuario (u otra sucursal de la misma empresa) puede haberse quedado con el número. Los 10 `GuardarNuevo` de documentos llaman a un `VerificarCodigo()` propio que, si el número ya está tomado, avisa y guarda con el siguiente libre.
- **En Correcciones y Facturas el código se calcula recién cuando `_movimiento` ya está resuelto**, no al principio de `CargarParaNuevo` — el correlativo depende del cubo, y al principio todavía tiene el valor default.
- **Tolerancia a códigos viejos**: `CodigoDocumento` lee los dígitos finales (`"A12"` → 12), así que el correlativo no se reinicia en 1 mientras queden códigos con signo sin regenerar.
- `empresas.signo`, `sucursales.signo` y `regiones.signo` **siguen existiendo y se siguen editando** en sus pantallas de mantenimiento; ya no intervienen en el código de ningún documento.
- En `DataConsulta` se eliminaron `SiguienteNumeroDoc`, `SiguienteNumeroDocPorEmpresa` y `SiguienteNumeroDocPorRegion` (reemplazados por `CodigoDocumento`).

## VisorEmpresa (app compañera)

App WPF separada (`VisorEmpresa/VisorEmpresa.csproj`, símbolo de compilación `VISOR`) que visualiza la **empresa completa** (todas las sucursales a la vez), a diferencia de la app principal que siempre trabaja filtrada por `AppState.SucursalActiva`. Login propio (`LoginVisorWindow`), ventana propia (`ConsolaVisor.xaml`), tema propio (`TemaVisor`).

Desde la sesión 2026-07-28/29 su sidebar espeja el de la app principal para documentos: Pedidos partido en **Ventas/Compras**, Correcciones en **Repuestas/Retirados**, Facturas en **Ingresos/Egresos**, y Traspasos en **Entradas/Salidas** (este último se hizo después, en un pedido aparte).

De solo lectura únicamente para los documentos transaccionales (**Pedidos, Traspasos, Correcciones, Facturas** — "ver documento" desde la grilla, sin Nuevo/Editar/Eliminar/Guardar). Todo el resto del catálogo permite Nuevo/Editar/Eliminar (gateado por `AppState.EsAdmin`, igual que la app principal): **Precios, Empresas, Sucursales, Usuarios, Regiones** (desde antes), y desde la sesión 2026-07-20 también **Artículos, Familias, Productos, Industrias, Categorías** — catálogo completo editable, ver detalle en el historial de esa sesión.

### Código 100% independiente de SistemaGestion (desde sesión 2026-07-12/13)
`VisorEmpresa.csproj` **ya no tiene ningún archivo vinculado** (`<Compile Link=.../>`). Los ~22 archivos que antes se compartían por link con `SistemaGestion/` tienen ahora copia física propia en `VisorEmpresa/`, con su namespace renombrado:
- `namespace SistemaGestion.Data` → `namespace VisorEmpresa.Data`: `DataConsulta`, `SqlData`, `AppLoader`, `AppState`, `StockCalculator`, `DatabaseConnection`, `EsquemaValidator`, `SqlRetry`, `PasswordHasher`, `CodigoRegenerator`, `PedidosPrecioActualizador`, `AppsheetsSync`.
- `namespace SistemaGestion` → `namespace VisorEmpresa`: `ConexionEstado`, `WindowHelper`, `WindowTheming`, `GridFocusHelper`, `FuncionesComunes`.
- Más los dos temas (`Themes/LightTheme.xaml`/`DarkTheme.xaml`), copiados sin cambios (son puro `ResourceDictionary`, sin código).

> **Actualizado en sesión 2026-07-27**: `ConexionConfig`, `ConfiguracionDbWindow` y `ConexionServidoresWindow` (listados en este bloque hasta la sesión 2026-07-12/13) **ya no existen** — se borraron por completo de los dos proyectos al migrar el login a `ConexionBroker` (ver sección propia más abajo). Ya no hay "elegir/configurar servidor" en ninguna de las dos apps.

Editar cualquiera de estos archivos ya **no** afecta a SistemaGestion, y viceversa — es la misma independencia física que ya tenían desde antes los módulos de catálogo (`PreciosGeneral`, `EmpresasGeneral`/`Detalle`, `SucursalesGeneral`/`Detalle`, `UsuariosGeneral`/`Detalle`, `RegionesGeneral`/`Detalle`) y los formularios `PedidosGeneral`/`TraspasosGeneral`/`CorreccionesGeneral`/`FacturasGeneral` (y sus `*Detalle`), que ya eran copias propias en el namespace `VisorEmpresa`.

**Puntos en común que quedan intencionalmente compartidos, y no son código:**
1. **`ConsolaVisor.xaml.cs`** sigue declarando `namespace SistemaGestion { public partial class ConsolaMovimientos : Window ... }` a propósito — no está vinculado, es la clase PROPIA del visor que imita ese nombre/namespace exacto para que los formularios propios de VisorEmpresa (`PedidosDetalle`, `SucursalesGeneral`, etc., todos en namespace `VisorEmpresa`) puedan hacer `Window.GetWindow(this) as ConsolaMovimientos` con un simple `using SistemaGestion;`, sin necesitar cualificar nada.
2. El ícono (`icono.ico`) también sigue siendo un `Resource Include` compartido — es un asset visual, no código, así que no entraba en el alcance de "independencia de código".
3. **`AuthBrokerClient.BrokerUrl`** (desde sesión 2026-07-27): cada proyecto tiene su PROPIA copia de `AuthBrokerClient.cs` (no vinculado), pero las dos apuntan a la misma constante `https://conexion.jhoelmaister.tech` — no es código compartido, es el mismo valor duplicado a propósito en dos archivos independientes. Ver sección "ConexionBroker" más abajo.

### `ConsultasEmpresa.cs` — consultas propias del visor, y su caché al loguear (sesión 2026-07-12/13)
Consultas SQL agregadas a nivel de EMPRESA (todas las sucursales), con conexión propia (no la global de `AppLoader`, para poder correr en paralelo sin choque de `SqlConnection`/`DataReader`). Todo lo que sigue se precarga en `LoginVisorWindow` (tras `AppLoader.ConectarUsuarios`/`ConectarProductos`) y se vuelve a precargar en `ConsolaVisor` al cambiar de empresa desde la top bar:
- **`ConectarCache{Pedidos,Traspasos,Correcciones,Facturas}(empresa, año)`**: puebla `Sql.DocumentosXObj`/`XObj` para TODA la empresa (sin filtro de sucursal — el parámetro se eliminó del todo, ya no existe) en una sola consulta por año. Memoizado por `(empresa, año)`: llamadas repetidas con la misma clave son un no-op. Las pantallas `PedidosGeneral`/`TraspasosGeneral`/`CorreccionesGeneral`/`FacturasGeneral` llaman a estos mismos métodos en su primer `Loaded` (por si se abren sin pasar por el login), pero gracias a la memoización eso ya no vuelve a tocar SQL — el combo de sucursal (u Origen/Destino en Traspasos) de cada pantalla filtra en memoria, no en SQL.
- **`ConectarCacheDashboardEmpresa(empresa)`**: independiente de lo anterior — carga pedidos/traspasos/correcciones/aperturas de TODA la empresa **sin límite de fecha** (a diferencia de la de arriba, que es por año) en listas propias (`PedidoCache`/`TraspasoCache`/`CorreccionCache`). Alimenta `ObtenerStockEmpresa`/`ObtenerStockEmpresaAlCierre`, `CargarMovimientos`, `CargarResumenPedidos` y `CargarTraspasosInternos` (usados por el Dashboard), que antes golpeaban SQL en cada llamada y ahora solo calculan en memoria.
- **`CargarSucursalesEmpresa(empresa)`**: ya NO consulta SQL — lee de `Sql.SucursalesObj`, el catálogo que `AppLoader.ConectarProductos` ya carga al loguear con el mismo filtro exacto (`estadof='normal' AND empresa=X`).
- Los 4 métodos `ConectarCachePedidos/Traspasos/Correcciones/Facturas` **año+sucursal-scoped** que usan las pantallas de navegación de documentos (mencionados arriba) son un mecanismo DISTINTO del de `ConectarCacheDashboardEmpresa` — se decidió explícitamente dejarlos así (año+sucursal) en vez de unificarlos, ante la duda de qué hacer con las pantallas que ya los usaban.
- **Pendiente, propuesto y no confirmado**: `CargarPedidos()`/`CargarTraspasos()`/`CargarCorrecciones()`/`CargarFacturas()` calculan cantidad/importe por documento con un helper (`CalcularCantidad`/`CalcularImporte`) que recorre TODA la tabla de líneas por cada documento — O(documentos × líneas), ahora sobre toda la empresa. Es la causa de que la PRIMERA apertura de cada pestaña siga siendo más lenta que las siguientes (que no reprocesan nada, `_iniciado` ya en `true`). Arreglo propuesto: agrupar las líneas por documento en un diccionario una sola vez al principio de cada `CargarXxx()`.

### ⚠️ Trampa recurrente: campos de `AppState` que nunca se pueblan en el visor
`AppState.SucursalActiva`, `AppState.AperturaActiva`, `AppState.RegionActiva`, `AppState.DataFechaInicio`/`DataFechaFinal` (ahora `VisorEmpresa.Data.AppState`, copia propia — ver arriba) **nunca se completan en VisorEmpresa** — los llena `AppState.ActualizarBase()`, exclusivo del flujo de login de la app principal (`LoginVisorWindow` deja `SucursalActiva`/`RegionActiva` explícitamente en `""`). Cualquier código que dependa de estos campos (p. ej. `StockCalculator.ContarStock`, que usa los tres primeros) da resultados vacíos o en cero cuando corre dentro del visor, sin ningún error visible — ya causó dos bugs reales en la sesión 2026-07-08. Antes de reusar uno de estos métodos dentro de un archivo propio de VisorEmpresa, revisar si depende de alguno de estos campos; si es así, hay que reemplazarlo por el equivalente de `ConsultasEmpresa`/`VisorState` (que sí tiene `EmpresaActiva`/`AnioActivo`/`UsuarioActivo`, sin sucursal ni apertura activa — el visor es multi-sucursal por diseño).

## ConexionBroker (servicio de autenticación) — desde sesión 2026-07-27

Servicio server-side mínimo (`ConexionBroker/`, ASP.NET Core Minimal API, `.NET 8`) que reemplazó por completo el flujo anterior de conexión a SQL Server. **Antes**: `SistemaGestion`/`VisorEmpresa` guardaban la contraseña real de SQL Server cifrada por PC (DPAPI, `%AppData%\SistemaGestion\conexion.dat` vía `ConexionConfig.cs`), con una pantalla para elegir/configurar servidor (`ConfiguracionDbWindow`, `ConexionServidoresWindow`) que había que cargar a mano en cada PC nueva. **Ahora**: la contraseña real de SQL Server vive ÚNICAMENTE en el broker, nunca en las PCs de los empleados.

- **Flujo**: el empleado pone su usuario/contraseña de siempre (tabla `usuarios`) en `LoginWindow`/`LoginVisorWindow` → la app se lo manda por HTTPS a `AuthBrokerClient.LoginAsync(...)` → el broker valida contra `usuarios` (mismo hash PBKDF2 de siempre) y, si es correcto, devuelve la cadena de conexión real de SQL Server — solo en memoria, solo para esa sesión de la app (`DatabaseConnection.Configurar(...)`). Nunca se vuelve a escribir a disco; al cerrar la app se pierde y hay que volver a loguear.
- **Sin pantalla de "elegir servidor"**: la URL del broker queda fija en el código, `AuthBrokerClient.BrokerUrl = "https://conexion.jhoelmaister.tech"` (copia propia en `SistemaGestion/AuthBrokerClient.cs` y `VisorEmpresa/AuthBrokerClient.cs`). Si el dominio/VPS cambia algún día, se edita esa constante en los dos archivos y se publica una versión nueva de la app.
- **Archivos borrados por completo** (los dos proyectos): `ConexionConfig.cs`, `ConfiguracionDbWindow.xaml(.cs)`, `ConexionServidoresWindow.xaml(.cs)`.
- **Infraestructura real ya desplegada y funcionando en producción**: VPS Ubuntu 24.04 (`179.197.71.81`, el mismo donde corre SQL Server 2022 para Linux), dominio `conexion.jhoelmaister.tech` (DNS en Hostinger), servicio `systemd` (`conexionbroker.service`, puerto local `5080`) detrás de `Caddy` (HTTPS automático vía Let's Encrypt). Documentación completa paso a paso, con los 3 bugs reales que aparecieron durante el despliegue (using faltante, archivo de config en la carpeta equivocada, `InvariantGlobalization` incompatible con `Microsoft.Data.SqlClient`) y cómo actualizar el broker cuando cambia el código: **`DESPLIEGUE-CONEXIONBROKER.md`** (raíz del repo) y `ConexionBroker/README.md` (guía genérica reusable).
- **Seguridad**: se descartó embeber la credencial real en el binario de la app (el repo es público, necesario para que Velopack lea releases sin token — se podría decompilar) y también mover el repo a privado (rompería ese mismo mecanismo). El broker resuelve esto sin ninguno de esos dos costados: la credencial real de SQL Server no viaja nunca a las PCs de los empleados, ni en disco ni en el binario — solo el broker (bajo control directo del dueño, en su propio VPS) la conoce.
- **El clon del VPS es un checkout normal del repo** (`~/WpfAppVba`, `origin` re-apuntado a `maister1122/WpfAppVba` en la sesión 2026-08-11/12): actualizar el broker es `git pull` + `dotnet publish -c Release -o /opt/conexionbroker` + `systemctl restart conexionbroker`. Ojo con lo que queda suelto en esa carpeta: los respaldos de editor del `appsettings.Production.json` tienen las credenciales en texto plano y el repo es público (ya cubiertos por `.gitignore` desde `742f56c`, pero conviene editar ese archivo fuera del repo).
- **Pendiente / mejora futura no implementada**: el broker no tiene todavía límite de intentos fallidos de login (todos los logins ya pasan por este único punto, sería el lugar natural para agregarlo).

## Lo Que Falta Por Hacer

### Alta prioridad
- [x] **CorreccionesDetalle → lógica de pestañas**: migrado a UserControl con `Cerrando` + `IntentarCerrar`; selectores usan `OpenAsTab`.
- [x] **ArticulosDetalle → lógica de pestañas**: migrado a UserControl con `Cerrando` + `IntentarCerrar`; selectores usan `OpenAsTab`.
- [x] ~~**Reinstalar el `Setup.exe` nuevo una vez en cada PC**~~ (releases `v1.0.8`/`visor-v1.0.5`) — hecho por el usuario, confirmado en sesión 2026-08-13. Las apps instaladas antes de la mudanza buscaban updates en `jhoelmaister/wpfappvba`, compilado adentro del binario e imposible de redirigir a distancia; la reinstalación era la única salida.
- [ ] **Publicar la `1.0.9` (visor: `1.0.6`) y confirmar que aparece el botón 🔄 en las PCs** — es la prueba de punta a punta de que la mudanza de cuenta quedó cerrada: recién ahí se comprueba que las apps reinstaladas leen el feed del repo nuevo. Sesión 2026-08-13.
- [ ] **Borrar el repo viejo `jhoelmaister/wpfappvba` en GitHub** — dejarlo unos días por si alguna PC quedó sin reinstalar. No rompe el login (el broker es aparte), pero esa PC perdería toda posibilidad de actualizarse. Sesión 2026-08-13.
- [ ] **Desinstalar la GitHub App de Claude de la cuenta vieja** (Settings → Applications) — higiene, sin apuro. Ya está instalada en `maister1122`. Sesión 2026-08-13.
- [ ] Verificar compilación y prueba completa del sistema (no hay herramientas de build en el entorno cloud — debe hacerse en máquina local).
- [ ] Verificar en máquina local el catálogo editable nuevo de VisorEmpresa (Familias/Productos/Industrias/Categorías + Articulos) — sesión 2026-07-20, sin compilar en la nube.
- [ ] Confirmar con el usuario el criterio de `estadoV` (opt-out vs opt-in) tras probarlo — sesión 2026-07-20.

### Media prioridad
- [x] Botón X de las pestañas dinámicas llama `IntentarCerrar()` si el contenido lo implementa (via reflexión), en vez de cerrar directamente.
- [ ] Exportación de datos en otras secciones (actualmente solo ArticulosGeneral tiene "Informe Excel"/"Informe PDF", con filtro Condición y resumen de categorías — sesión 2026-07-27).
- [ ] Replicar el guardado async (Task.Run + deshabilitar ventana) de VisorEmpresa/PreciosDetalle al resto de los ~34 archivos/81 llamados a `OrdenarData()` — piloto hecho en sesión 2026-07-20, rollout pendiente de confirmación.
- [x] ~~Migrar la conexión a SQL Server a Application Roles + token de sesión (en vez de credencial fija cifrada por DPAPI)~~ — dirección elegida en sesión 2026-07-20, pero en sesión 2026-07-27 se resolvió el mismo problema por otra vía: **`ConexionBroker`** (ver sección propia arriba). Ya no hay ninguna credencial de SQL Server guardada en las PCs (ni DPAPI ni ningún otro cifrado) — el broker la entrega en memoria tras loguear. Application Roles queda descartado, no hace falta.
- [ ] Verificar en máquina local (compilación real) los cambios de la sesión 2026-07-27: el flujo completo de `ConexionBroker`/`AuthBrokerClient` en ambas apps, el fix de `UsuariosDetalle`/`DataConsulta.EstablecerItem`, la reorganización del panel lateral de `ConsolaVisor` (VisorEmpresa), y el nuevo "Informe PDF" + filtro Condición + resumen de categorías de `ArticulosGeneral` (Excel/PDF, los 4 archivos) — nada de esto se compiló en el entorno cloud (sin SDK de .NET). Prestar especial atención a que las fórmulas `SUMIF`/`SUM` del Excel calculen bien al abrirlo.
- [ ] Agregar límite de intentos fallidos de login en `ConexionBroker` (ver sección propia arriba).
- [ ] **Verificar en máquina local los cambios de la sesión 2026-07-28/29** — es la sesión con más superficie sin compilar. Prioridad: (1) crear un documento nuevo de cada tipo y confirmar que el código sale sin signo y con el correlativo correcto por empresa+movimiento; (2) correr "Regenerar códigos" y confirmar que los cambios se ven al reingresar (fix de cachés); (3) "Validar pedido" de punta a punta; (4) las 8 entradas nuevas del sidebar en los dos proyectos.
- [ ] **Correr "🗑️ Eliminar ocultos" primero en una base de prueba** — hace `DELETE` físico irreversible sobre 26 tablas. Nunca se ejecutó todavía contra datos reales.
- [ ] Correr "Regenerar códigos" una vez tras desplegar, para pasar los códigos viejos con signo al esquema nuevo (mientras tanto conviven: `CodigoDocumento` los tolera leyendo los dígitos finales).

### Baja prioridad / Futuro
- [ ] Reportes adicionales en Excel
- [ ] Mejoras de rendimiento en carga de grids grandes

## Problemas Conocidos o Pendientes

- **Sin herramientas de build en el entorno cloud**: todos los cambios se aplican siguiendo patrones existentes, pero no pueden compilarse ni ejecutarse remotamente. Siempre verificar localmente antes de merge a producción.
- **`AppState.TipoPedido` y `AppState.TipoMovimiento` son globales**: en `PedidosGeneral.AbrirEditar` se leen del DB antes de abrir la pestaña para garantizar el valor correcto. Si se abrieran dos pestañas de edición simultáneas muy rápido, podría haber condición de carrera (actualmente no es un problema práctico). Desde la sesión 2026-07-28/29 esto pesa más, porque hay **dos paneles vivos a la vez** por módulo (Ventas y Compras, Entradas y Salidas, etc.) compartiendo esos mismos campos globales.
- **Las cachés de `ConsultasEmpresa` son `static` y sobreviven al cierre de sesión**: cualquier herramienta que reescriba datos en el servidor tiene que invalidarlas o los cambios no se ven al reingresar. Ya hay un `LimpiarCaches()` llamado desde `CerrarSesionInterno()` (sesión 2026-07-28/29) — si se agrega otra herramienta de este tipo, asegurarse de que pase por ahí.
- **Los tres lados del `codigo` tienen que moverse juntos**: `MovimientoSql` (cubos), `CodigoDocumento` (creación) y `CodigoRegenerator` (renumerado). Cambiar el ámbito en uno solo hace que los códigos se muevan de lugar al regenerar — fue exactamente el bug de esta sesión, ver la sección "Códigos de documento".
- **`CorreccionesDetalle` ya es pestaña**: usa `OpenAsTab` de ArticulosGeneral para buscar artículos, igual que Pedidos/Traspasos.
- **El feed de actualización es una constante compilada, no una configuración**: `ActualizadorApp.RepoUrl` (y `AuthBrokerClient.BrokerUrl`) viven dentro del `.exe`. Cambiarlos exige publicar una versión nueva Y que cada PC la reciba — y si lo que cambió es justamente la URL del feed, las apps ya instaladas no pueden auto-actualizarse a ella: hay que reinstalar a mano una vez (fue el caso de la mudanza de cuenta, sesión 2026-08-11/12).
- **El número de versión solo sube, y los releases no se migran con git**: al mover el repo viajaron los tags pero ninguna release (son artefactos de GitHub, no de git). Antes de publicar, mirar qué releases existen en el repo destino — no alcanza con mirar los tags.
- **Desde la sesión web NO se llega al VPS**: la política de red del contenedor solo habilita GitHub y los registries de paquetes. No hay cliente `ssh` instalado, no hay claves en `~/.ssh/`, el puerto 22 de `179.197.71.81` es inalcanzable y el proxy responde `403 to CONNECT` para `conexion.jhoelmaister.tech:443`. Todo chequeo del VPS (remote del clon, estado del servicio, `ping` del broker) lo tiene que correr el usuario por SSH y pegar la salida — verificado en sesión 2026-08-13.
- **`conexion.jhoelmaister.tech` NO es la cuenta de GitHub vieja**: es el dominio propio en Hostinger apuntando al VPS, independiente de GitHub. Que contenga la cadena `jhoelmaister` hace que aparezca en cualquier `git grep` de rastros de la cuenta vieja y da un falso positivo. **No tocar `AuthBrokerClient.BrokerUrl`**: cambiarlo deja a las dos apps sin poder loguear.

## Historial de Cambios por Sesión

### Sesión 2026-08-13 — Cierre y auditoría de la migración de cuenta: barrido completo de rastros de `jhoelmaister`, verificación de los dos clones (VPS y PC) y de los tokens; ningún cambio de código (rama `master`)

> Sesión de verificación pura, sin una sola línea de código tocada. El pedido inicial fue *"quiero saber qué pendientes tengo para terminar la migración"* y derivó en *"me preocupa que algo tenga alguna relación con mi anterior cuenta jhoelmaister"*. Se auditó todo el circuito y quedó confirmado que no queda ningún vínculo con la cuenta vieja. Único cambio del repo: esta actualización de `CONTEXT.md`.

#### Estado de la migración al arrancar la sesión: todo lo técnico ya estaba hecho
Verificado contra el estado real (no contra la documentación):
- Repo **público** (`visibility: public` por API) y **Actions habilitadas** (2 releases con conclusión `success`).
- `git grep -niI "jhoelmaister/wpfappvba" -- '*.cs' '*.yml'` → **vacío**. Los 4 archivos del checklist + `publicar.ps1` apuntan a `maister1122/WpfAppVba`.
- `origin/master` = `cecf3c1`, con los 3 commits de la mudanza adentro.
- Releases `v1.0.8` y `visor-v1.0.5` publicadas con sus `Setup.exe`, `.nupkg` y feeds.
- **El único pendiente real era la reinstalación manual en cada PC** — y el `download_count` de los dos `Setup.exe` estaba en **0**, o sea que todavía no se había bajado en ninguna máquina. El usuario la completó durante esta sesión.

#### Auditoría de rastros de la cuenta vieja (el pedido central)
Se barrió el repo entero, no solo los archivos del checklist:

| Qué se revisó | Resultado |
|---|---|
| `git grep -i "jhoelmaister"` en **todo** el repo | Solo el dominio del broker + docs históricas |
| `ActualizadorApp.cs` (×2), workflows (4 líneas), `publicar.ps1`, `git remote` | `maister1122/WpfAppVba` ✅ |
| Contenido de `.github/` | Solo los 2 workflows — sin `CODEOWNERS`, `FUNDING.yml` ni `dependabot.yml` |
| Metadata de los `.csproj` | Sin `Authors`/`Company`/`RepositoryUrl`/`PackageProjectUrl` — nada que arrastre la cuenta |
| `NuGet.config` / fuentes privadas | No existe |
| Tokens hardcodeados | Ninguno — `publicar.ps1` lee de `$env:GITHUB_TOKEN` |
| Feed real que leen las apps | `releases.win.json` descargado: JSON válido, apunta a `SistemaGestion-1.0.8-full.nupkg` |

Las únicas menciones a `github.com/jhoelmaister/...` que quedan están en `MIGRACION-CUENTA-NUEVA.md` y en el historial de este archivo: son documentación que **describe** la mudanza y tiene que nombrar la cuenta vieja para explicarse. No las lee ningún programa.

#### El falso positivo que motivó la consulta: dominio ≠ cuenta
`SistemaGestion/AuthBrokerClient.cs` y `VisorEmpresa/AuthBrokerClient.cs` siguen diciendo `https://conexion.jhoelmaister.tech`, y eso **está bien**. Es el dominio propio en Hostinger que apunta al VPS `179.197.71.81`, sin ninguna relación con GitHub: si mañana se borra la cuenta `jhoelmaister` de GitHub, el broker sigue funcionando igual. Cambiar esa constante dejaría a las dos apps sin poder loguear. Anotado también en "Problemas Conocidos" para que no vuelva a generar la duda.

#### Verificación del clon del VPS (corrida por el usuario, por SSH)
La sesión web no llega al VPS (ver "Problemas Conocidos"), así que se le pasó al usuario un bloque de comandos con veredicto automático. Resultado:
- **Remote**: `https://github.com/maister1122/WpfAppVba.git` ✅ — el re-apuntado de la sesión anterior quedó firme.
- **Estado**: `742f56c`, 1 commit atrás de `origin/master` (`cecf3c1`). Se verificó qué trae ese commit: `CONTEXT.md | 37 +++`, **un solo archivo de documentación**. No toca `ConexionBroker/`, así que **no hubo que republicar ni reiniciar el broker**.
- **Respaldos con credenciales**: ninguno, y `git status --porcelain` vacío — el `.gitignore` endurecido en `742f56c` está haciendo su trabajo.
- **Servicio**: `conexionbroker` y `caddy` en `enabled`/`active` los dos, y `curl http://127.0.0.1:5080/ping` → **200**.

#### Verificación del clon local de Windows (un hueco que el checklist no cubría)
Se había auditado el clon del VPS pero no el de la PC del usuario, que es donde compila en Visual Studio. Resultado en `C:\Users\jhoel\source\repos\maister1122\WpfAppVba`:
- **Remote**: `https://github.com/maister1122/WpfAppVba` ✅, árbol de trabajo limpio, `[behind 1]` (el mismo commit de docs).
- **Acá el `git pull` sí importa** (a diferencia del VPS): el commit que falta modifica `CONTEXT.md`, justo el archivo que el usuario edita a mano al cerrar cada sesión. Editarlo estando en `742f56c` y commitear habría producido un conflicto de merge.
- El usuario además **borró la carpeta del clon viejo** (`jhoelmaister/wpfappvba`) de su PC, para que Visual Studio dejara de listar dos repos. Nada que recuperar: todo el proyecto vive en `master` en GitHub.

#### Tokens: no había ninguno
`echo $env:GITHUB_TOKEN` en la PC del usuario → **vacío**. No existía ningún PAT de la cuenta vieja guardado. Igual era un riesgo teórico: `publicar.ps1` (el camino manual) es el único que necesita PAT, y el flujo real de publicación es **GitHub Actions**, que usa el `secrets.GITHUB_TOKEN` automático de cada corrida. Un PAT viejo tampoco sería un riesgo de seguridad sobre el repo nuevo — no tiene ningún permiso ahí; el único síntoma posible sería un 403.

#### Lo que queda abierto
- **Publicar la `1.0.9` / `visor-1.0.6`** — es la prueba de punta a punta: recién cuando aparezca el botón 🔄 en las PCs reinstaladas queda demostrado que leen el feed del repo nuevo. Ojo: `1.0.8` y `1.0.5` ya están consumidas como release en este repo, los `.csproj` siguen en esos números.
- **Borrar el repo viejo en GitHub** — esperar unos días por si alguna PC quedó sin reinstalar.
- **Desinstalar la GitHub App de Claude de la cuenta vieja** — higiene.

### Sesión 2026-08-11/12 — Migración del repo a la cuenta de GitHub `maister1122`: URL del feed de auto-actualización en código, workflows y `publicar.ps1`; primeras releases desde el repo nuevo; clon del VPS re-apuntado; `.gitignore` endurecido contra respaldos de credenciales (rama `master`)

> Sesión corta y operativa, sin un solo cambio de lógica de negocio: el repo se movió de la cuenta `jhoelmaister` a `maister1122` y había que dejar el circuito completo andando (auto-actualización, releases, VPS). El pedido fue *"analizá `MIGRACION-CUENTA-NUEVA.md` y seguí las instrucciones"* — ese checklist se había escrito en la sesión anterior justamente para esto. A mitad de camino la sesión quedó trabada sin poder pushear (ver más abajo), y el tramo final se resolvió por SSH contra el VPS.

#### La URL del repo, cambiada en 5 archivos funcionales (`fc5a17b`)
- La app busca sus actualizaciones en una URL **fija en el código**, así que mudar el repo la deja apuntando a la cuenta vieja. Se reemplazó `jhoelmaister/wpfappvba` por `https://github.com/maister1122/WpfAppVba` (el `origin` real) en: `SistemaGestion/ActualizadorApp.cs` y `VisorEmpresa/ActualizadorApp.cs` (`RepoUrl`), y `.github/workflows/release.yml` + `release-visor.yml` (2 líneas `--repoUrl` cada uno, en `vpk download` y `vpk upload`).
- **`publicar.ps1` no figuraba en el checklist**: su comando de verificación (`git grep ... -- '*.cs' '*.yml'`) no mira `.ps1`, así que el `$RepoUrl` del publish manual se le escapaba — habría subido la release al repo viejo. Se corrigió también.
- Docs actualizadas de paso: `CLAUDE.md`, `CONTEXT.md`, `CREAR-NUEVA-VERSION.md`, `PUBLICAR-ACTUALIZACIONES.md`, `REPLICAR-AUTOACTUALIZACION.md`, `ConexionBroker/README.md`. `MIGRACION-CUENTA-NUEVA.md` quedó intacto a propósito: cita la cuenta vieja para explicar la migración.
- **`AuthBrokerClient.BrokerUrl` NO se tocó** (`https://conexion.jhoelmaister.tech`): el VPS es independiente de GitHub y sigue siendo el mismo. El vínculo app↔VPS no requirió ningún paso — es una constante compilada en el `.exe`, no una configuración.

#### ⚠️ La sesión web quedó sin permiso de escritura hasta instalar la GitHub App en la cuenta nueva
- Al ir a pushear: `403` en `git-receive-pack` (antes de negociar refs, o sea no era un tema de rama), y lo mismo por API REST (*"GitHub access is not enabled for this session"*) y por las herramientas MCP (`Resource not accessible by integration`). **Lectura sí, escritura no, por ningún camino.**
- Causa: la **Claude GitHub App estaba instalada en la cuenta vieja**, no en `maister1122`. Es un paso de la mudanza que el checklist no contemplaba.
- Mientras tanto el commit se entregó como parche (`git format-patch`), porque el contenedor de la sesión es efímero y el trabajo se perdía. Se destrabó cuando el usuario instaló la app en la cuenta nueva (GitHub → Settings → Applications → Claude, con *Read and write access to code*): el push salió al primer intento. **Para futuras mudanzas: instalar la GitHub App en la cuenta nueva ANTES de empezar.**

#### Primeras releases desde el repo nuevo (las lanzó el usuario con Run workflow)
- `Sistema de Gestión v1.0.8` y `Visor Empresa v1.0.5`, las dos con conclusión `success`, compiladas desde `fc5a17b` — o sea que **esos `Setup.exe` ya llevan la URL nueva adentro**.
- Los tags `v1.0.8`/`visor-v1.0.5` ya existían en el repo nuevo (viajaron con el `git push` de la mudanza) pero **sin release asociada**: los releases NO se migran con git, son artefactos de GitHub. Por eso se pudo publicar con el mismo número sin choque de tags.
- **La próxima versión tiene que ser `1.0.9` (y `1.0.6` el visor)**: 1.0.8 y 1.0.5 ya están consumidas como release en este repo.

#### El VPS también tenía un clon apuntando al repo viejo
- `~/WpfAppVba` en el VPS (de donde se publica el broker) tenía `origin` en `jhoelmaister/WpfAppVba.git`, así que el `git pull` del procedimiento de actualización del broker habría traído código viejo. Se re-apuntó con `git remote set-url` y se puso al día (estaba 13 commits atrás).
- **No hubo que republicar el broker**: el pull no trajo ningún cambio de código de `ConexionBroker/` (solo su `README.md`). Verificado: `systemctl is-enabled conexionbroker caddy` → `enabled`/`enabled` (sobrevive un reboot, que el VPS tiene pendiente hace rato) y `curl http://127.0.0.1:5080/ping` → `200 OK`.

#### Hallazgo: un respaldo de nano con las credenciales, fuera del `.gitignore` (`742f56c`)
- El `git status` del VPS mostró `?? ConexionBroker/appsettings.Production.json.save` — el respaldo que deja **nano** al editar el archivo de credenciales. Tiene la contraseña real de SQL Server **en texto plano**, y este repo es **público**.
- El `.gitignore` no lo cubría: la regla era el nombre exacto `ConexionBroker/appsettings.Production.json`, que no matchea las variantes. Un `git add -A` desde el VPS las habría publicado.
- Se agregaron patrones para respaldos de editor dentro de `ConexionBroker/` (`appsettings.*.json.*`, `*.save`, `*.swp`, `*~`) y se borró el archivo del VPS.

#### Notas / pendientes de esta sesión
- **Nada se compiló** en el entorno remoto (sin SDK de .NET, como siempre), pero el cambio es un reemplazo de texto en una constante y en argumentos de línea de comandos, y las dos releases compilaron `success` en Actions — esa es la verificación real.
- **Falta reinstalar el `Setup.exe` nuevo una vez en cada PC**: las apps instaladas tienen la URL vieja compilada adentro y no se pueden redirigir a distancia. Es un corte manual, una sola vez; de ahí en más las actualizaciones vuelven a llegar solas.

### Sesión 2026-07-28/29 — Facturas restaurada como documento propio + "Validar pedido"; los 4 módulos de documentos partidos en dos entradas de sidebar (Ventas/Compras, Entradas/Salidas, Repuestas/Retirados, Ingresos/Egresos); códigos sin signo por empresa+movimiento en creación Y regeneración; botón "Eliminar ocultos" (rama `master`)

> Sesión muy larga, con pedidos numerados turno a turno ("problema1", "problema2", "problema3"). Dos correcciones importantes del usuario reorientaron el trabajo a mitad de camino (ver más abajo: el revert de Facturas y el criterio ingreso/egreso). El usuario además pusheó un commit propio (`a79b0bf`) mientras la sesión estaba en curso, que hubo que integrar por merge. 21 commits, todos a `master`.

#### Revert: Facturas vuelve a ser un documento propio (`4a260ff`)
- Arranque: el usuario pidió eliminar de Pedidos todo lo referido a facturas. Se hizo — pero acto seguido avisó que había sido un error suyo haber borrado `FacturasGeneral`/`FacturasDetalle`, `documentosF` y `transaccionesF` en una sesión anterior, y pidió revertir esas eliminaciones.
- Se restauraron las pantallas `FacturasGeneral`/`FacturasDetalle` (los dos proyectos) y las tablas `documentosF`/`transaccionesF`, con `facturas.documentoF` (había quedado como `facturas.documentoP`).
- **Columna nueva `documentosF.relacion`** (FK → `documentosP`): el conector pedido-factura. Se preguntó explícitamente si iba en `documentosP.relacion` o `documentosF.relacion` y el usuario eligió que apunte al pedido desde la factura.
- `transacciones` renombrada a `transaccionesP` (contraparte de `transaccionesF`).
- Pedidos quedó sin saber nada de facturas: se eliminó la pestaña "Facturas del pedido", el badge de estado de factura, la columna "Factura" y el filtro lateral por factura.

#### "Validar pedido" — armar una factura desde un pedido (`cc2cdca`, `92c2d07`)
- Pantalla nueva `ValidarPedido.xaml(.cs)`: buscador + grid de pedidos, grid de detalle debajo con checkboxes, y botones Validar/Cancelar. Búsqueda por código, cliente/proveedor y referencia (elegido por el usuario).
- **Las líneas NO se copian una por una**: se **suman agrupadas por categoría** (dos ítems de la misma categoría con importe 200 → una línea de 400). Pedido explícito del usuario. Las categorías que suman cero se saltean.
- El botón se movió de `FacturasDetalle` a `FacturasGeneral`: crea una factura NUEVA copiando fecha/hora/tercero/referencia del pedido. Dentro de `FacturasDetalle` quedaron botones "Validar"/"Actualizar" (visibles solo si la factura está vinculada a un pedido) para re-elegir o re-sincronizar.

#### Los 4 módulos de documentos partidos en dos entradas de sidebar
- Pedidos → **Ventas / Compras**; Traspasos → **Entradas / Salidas**; Correcciones → **Repuestas / Retirados**; Facturas → **Ingresos / Egresos**. Cada `*General` toma ahora un parámetro `tipoMovimiento` en el constructor y se instancia dos veces (ver "Paneles declarados" arriba).
- En consecuencia se eliminaron de TODOS los formularios correspondientes: la columna "Movimiento" de `Grid1` y los combos `Box_Movimiento`/`CboMovimiento`. El movimiento ya no se elige en pantalla: lo fija la sección del sidebar.
- **Espejado completo en VisorEmpresa** para Pedidos/Correcciones/Facturas (confirmado por el usuario). Traspasos quedó fuera en ese momento por decisión suya ("dejarlo como está"), y se hizo después en un pedido aparte.
- **Iteración de diseño del sidebar**: primero se agruparon las entradas bajo encabezados de sección; el usuario lo rechazó ("no me gusta, solo pon las pestañas... como estaban") y se aplanó. Más tarde el usuario pusheó él mismo el commit `a79b0bf` **volviendo a poner los encabezados** de sección — se integró por merge (conflicto en `ConsolaMovimientos.xaml`, resuelto conservando su estructura y aplicando encima el rename Descargas→Retirados). El estado final del sidebar es el de él, con encabezados.
- Renames pedidos en el camino: Albaranes → Remisiones → (revertido), Descargas → **Retirados**.

#### ⚠️ Corrección importante del usuario: ingreso/egreso va por mercadería, no por dinero (`82697a1`)
- La primera implementación mapeaba ingresos→ventas y egresos→compras. El usuario lo corrigió: **es al revés**. Una **compra** hace ENTRAR mercadería → **ingreso**; una **venta** la hace SALIR → **egreso**.
- Todos los helpers (`NormalizarMovimiento`/`EsEgreso`, y hoy `MovimientoSql`) implementan ese criterio, aceptando los valores viejos como equivalentes (`venta`→egreso, `compra`→ingreso) para no romper documentos ya guardados.

#### Códigos de documento sin signo (`6826b3b`, `ee4631b`)
- Se rehízo el esquema completo de `codigo` en **las dos puntas** — ver la sección propia "Códigos de documento" más arriba para el detalle de ámbitos y piezas.
- Primero (`6826b3b`) se cambió solo la **regeneración** (`CodigoRegenerator`): fuera los signos, correlativo por (empresa, movimiento) en P/C/F y por empresa en T/L/I, exigiendo empresa activa.
- Después (`ee4631b`) se descubrió que faltaba la otra punta: **la creación seguía poniendo signo y numerando por sucursal**, así que un documento nuevo nacía con el esquema viejo y los códigos se movían de lugar apenas se regeneraba. Se crearon `MovimientoSql.cs` (fuente de verdad única de los cubos, que `CodigoRegenerator` también pasó a usar) y `CodigoDocumento.cs` (cálculo al crear + **revisión al guardar**, que avisa y corrige si el número ya está tomado), conectados en los 10 `GuardarNuevo` de documentos de los dos proyectos.
- **`documentosI` cambió de ámbito, no solo de signo**: numeraba por sucursal y pasó a numerar por empresa, para coincidir con el regenerador (si no, dos sucursales podían generar el mismo número). Se avisó explícitamente al usuario porque va más allá de "solo sacale el signo".
- `documentosL` también cambió de cascada: al crear usaba `region → regiones.empresa` y pasó a la columna `empresa` directa, igual que el regenerador.

#### Botón "🗑️ Eliminar ocultos" — único DELETE físico de la app (`22452d1`)
- Nueva herramienta `RegistrosOcultosPurgador` (ambos proyectos), con botón solo en `VisorEmpresa/ConsolaVisor`, al lado de "Regenerar códigos"/"Recalcular precios".
- Borra **físicamente** (`DELETE` real, no `estadof`) las filas con `estadof` en `('ocultado','eliminado')` de la **empresa activa** (alcance elegido por el usuario ante la pregunta), en las 26 tablas: líneas de documentos primero, cabeceras después, maestras al final.
- Luego **renumera 1..N sin huecos** la columna `indice` de las tablas que la tienen (`articulos` por familia; `correcciones`/`entregas`/`facturas`/`pedidos`/`traspasos`/`transaccionesF`/`transaccionesP` por su documento dueño).
- Todo en una única transacción con rollback. Doble confirmación antes de ejecutar; cierra la sesión al terminar. **Es la única excepción a la regla de "la app nunca hace DELETE físico"** — documentado como tal en `ESTRUCTURA-BASE-DE-DATOS.md`.

#### Bug real encontrado: "Regenerar códigos no cambia nada" era caché, no SQL (`7b276b5`)
- El usuario reportó que el botón no cambiaba los códigos. El dato que destrabó el diagnóstico lo dio él: *"luego de cerrar sesión yo mismo y volver a entrar, noto los cambios"* — o sea el `UPDATE` **sí** se aplicaba.
- **Causa raíz**: las cachés de `ConsultasEmpresa` son `static` y sobreviven al cierre de sesión. Al reingresar con la misma empresa+año, las claves memoizadas (`_pedidosCacheCargada` y compañía) coincidían, `ConectarCache*` salía por su early-return y se seguía mostrando lo que había quedado en memoria en vez de releer SQL.
- **Fix**: nuevo `ConsultasEmpresa.LimpiarCaches()` (descarta dashboard, stock y las 4 memoizaciones empresa+año), llamado desde `ConsolaVisor.CerrarSesionInterno()` — cubre el cierre manual y también el forzado de "Regenerar códigos" y "Eliminar ocultos".

#### Otros arreglos puntuales
- `PedidosGeneral` (ambos proyectos): la columna Artículo de `Lista2` mostraba el `id` (GUID) en vez del `codigo` del artículo (`ec7a207`).
- `VisorEmpresa/TraspasosDetalle`: fuera el combo "Movimiento"; `LblTitulo` fijo en "Traspasos de Producto"; ícono fijo "TR" con relleno blanco (`6826b3b`). El `Foreground` había quedado en `ThemeTexto`, que en tema oscuro es `#E8EAED` sobre relleno blanco → la "TR" quedaba **invisible**; se pasó a negro fijo (`7b276b5`).
- Correcciones de títulos, incluida una autocorrección del usuario: el "sacar la palabra Nueva" no era de Factura sino de `CorreccionesDetalle` — se restauró "Nueva Factura" y se cambió el de Correcciones (`6464667`).
- **Cambios revertidos a pedido**: se habían hecho ajustes de tamaño del instalador al explicar un error de disco de Velopack (captura de WhatsApp). El usuario aclaró *"no quiero cambios que dañaran mi aplicación, solo quería saber el motivo"* → se revirtieron, dejando solo la explicación en documentación (`65a92c8`).

#### Notas / pendientes de esta sesión
- **Sin SDK de .NET en el entorno remoto** (igual que siempre): todo se verificó por lectura de código, grep, scripts de conteo de llaves/paréntesis fuera de strings y comentarios, validación de XAML bien formado, y cross-checks de que cada handler de XAML tenga método en el `.cs` y cada `x:Name` usado exista. **Nada se compiló.** Es la sesión con más superficie de cambio sin compilar de todas — conviene probar en Visual Studio antes de publicar versión.
- Los 4 pares de archivos compartidos tocados (`CodigoRegenerator`, `CodigoDocumento`, `MovimientoSql`, `DataConsulta`) se verificaron con `diff` para confirmar que difieren **solo** en la línea del `namespace`.

### Sesión 2026-07-27 — ConexionBroker: nueva arquitectura de login + despliegue real en producción; VisorEmpresa: fix de usuario nuevo que no llegaba a SQL Server y panel lateral separado en "nivel empresa"/"superiores"; ArticulosGeneral: nuevo Informe PDF, filtro Condición y resumen de categorías (Excel/PDF) ajustado a diseño real de InventariosGeneral (rama `master`)

> Sesión larga con tres bloques. Primero (el más largo): a partir de un pedido de seguridad ("que la app no guarde las credenciales en la PC"), se diseñó, implementó y desplegó EN VIVO, en la infraestructura real del usuario (VPS, dominio, SQL Server de producción), un servicio nuevo (`ConexionBroker`) que reemplaza el login directo a SQL Server. Segundo: varios "problema1/problema2" numerados sobre bugs y pedidos puntuales de VisorEmpresa (usuario nuevo que no persistía, panel lateral, columna `usuarios.temaC`). Tercero: pedidos turno a turno sobre el nuevo "Informe PDF" de `ArticulosGeneral` (ambos proyectos), afinados dos veces contra archivos PDF/Excel reales que el usuario mandó como referencia (generados por `InventariosGeneral`).

#### Arquitectura nueva: `ConexionBroker` (servicio de autenticación centralizado)
- Punto de partida: el usuario quería dejar de guardar la contraseña real de SQL Server cifrada en cada PC (`ConexionConfig.cs`/DPAPI) y dejar de tener que cargarla a mano en cada instalación nueva. Se evaluaron alternativas (embeber la credencial en el binario, mover el repo a privado) y se descartaron por romper el pipeline de releases de Velopack (que necesita el repo público para leer sin token) o no resolver nada. Dirección elegida y aprobada por el usuario: un **servicio broker** propio, servidor-side, que es el único que conoce la contraseña real de SQL Server.
- Proyecto nuevo `ConexionBroker/` (ASP.NET Core Minimal API, `net8.0` sin `-windows`): `/login` valida usuario/contraseña contra la tabla `usuarios` (mismo `PasswordHasher` PBKDF2 que ya usaba `AppLoader.ValidarLogin`, con la misma migración automática de contraseñas viejas en texto plano) y devuelve la cadena de conexión real de SQL Server; `/ping` para healthcheck. Sin `InvariantGlobalization` en el `.csproj` (rompe `Microsoft.Data.SqlClient` si se activa — anotado explícitamente para no repetir el error).
- `AuthBrokerClient.cs` (copia propia en `SistemaGestion/` y `VisorEmpresa/`): `PingAsync`/`LoginAsync` contra el broker. `LoginWindow`/`LoginVisorWindow` ahora pingean y loguean contra el broker en vez de contra SQL Server directo, y llaman a `DatabaseConnection.Configurar(...)` con la respuesta (credenciales solo en memoria del proceso, nunca a disco).
- **Se eliminaron por completo** `ConexionConfig.cs`, `ConfiguracionDbWindow.xaml(.cs)`, `ConexionServidoresWindow.xaml(.cs)` (los dos proyectos) — ya no existe pantalla de "elegir/configurar servidor". `DatabaseConnection.CargarDesdeConfiguracion()` también se eliminó; se agregó `ObtenerCadenaConexion()` para las clases que abren conexiones sueltas propias (p. ej. `VisorEmpresa/ConsultasEmpresa.CadenaConexion()`, que antes armaba su propia cadena vía `ConexionConfig`).
- **Cambio de alcance pedido a mitad de despliegue**: la versión inicial mantenía una pantalla para elegir el servidor del broker. El usuario quedó atascado en producción con la pantalla de login colgada en "Reintentando…" (con "Configurar conexión" también deshabilitado, sin salida) por un `conexion.dat` viejo con datos pre-migración — pidió explícitamente sacar del todo esa función y arrancar siempre contra `https://conexion.jhoelmaister.tech` fijo en el código (`AuthBrokerClient.BrokerUrl`). Se aplicó como el alcance final, no como parche puntual.
- Detalle completo de todos los archivos tocados/borrados: ver la sección "ConexionBroker (servicio de autenticación)" más arriba.

#### Despliegue real en producción (VPS del usuario, en vivo, guiado paso a paso)
- Infraestructura: VPS `179.197.71.81` (Hostinger, Ubuntu 24.04.4 LTS, mismo servidor donde ya corre SQL Server 2022 para Linux). Dominio `jhoelmaister.tech` (Hostinger/hPanel) — se agregó un registro DNS `A` nuevo (`conexion` → la misma IP) sin tocar los registros existentes. `.NET 8 SDK` instalado en el VPS, código clonado a `~/WpfAppVba`, publicado a `/opt/conexionbroker`, corriendo como servicio `systemd` (`conexionbroker.service`, `http://127.0.0.1:5080`, solo local) detrás de `Caddy` (HTTPS/Let's Encrypt automático, `ufw` inactivo en este VPS).
- El usuario (no técnico) ejecutó cada comando de terminal guiado en tiempo real; se documentaron y corrigieron 3 bugs reales encontrados durante el despliegue en vivo: `CS0103` por falta de `using ConexionBroker;` en `Program.cs`; `appsettings.Production.json` creado por error en la carpeta equivocada (sesión SSH que se reconectó en otro directorio); y el más importante, `System.NotSupportedException: Globalization Invariant Mode is not supported` causado por `<InvariantGlobalization>true</InvariantGlobalization>` en el `.csproj` (se agregó originalmente como optimización de tamaño, sin saber que `Microsoft.Data.SqlClient` lo necesita desactivado) — este fue el que más costó diagnosticar, porque `/ping` devolvía `503` sin ningún log útil hasta que se agregó logging explícito de la excepción en el propio `Program.cs`.
- Verificado funcionando extremo a extremo: `curl https://conexion.jhoelmaister.tech/ping` → `200 OK` desde afuera, y login real desde Visual Studio (SistemaGestion) contra el broker en producción.
- Documentado en **`DESPLIEGUE-CONEXIONBROKER.md`** (nuevo, raíz del repo) a pedido explícito del usuario: arquitectura, infraestructura exacta, cómo verificar que está andando, cómo actualizar el broker cuando cambia el código, los 3 bugs reales con su fix, y qué hacer si el dominio o el VPS cambian alguna vez.

#### Problema 1 (primer pedido) — VisorEmpresa/UsuariosDetalle: no se podía poner contraseña a un usuario nuevo
- Al crear un usuario nuevo en VisorEmpresa no había forma de asignarle contraseña, así que ese usuario nunca iba a poder loguear. Se agregó un campo CONTRASEÑA (con botón Ver/Ocultar, mismo patrón que `CambiarContrasena.xaml`) al card "ACCESO" de `UsuariosDetalle.xaml`. Obligatorio al crear (bloquea el guardado si está vacío); opcional al editar (vacío = no se toca la contraseña actual). Guarda con `PasswordHasher.Hashear(...)` en la columna `llave`.

#### Problema 1 (segundo pedido) — VisorEmpresa/UsuariosGeneral: al guardar un usuario nuevo, quedaba en caché pero nunca llegaba a SQL Server
- **Causa raíz encontrada por lectura de código** (`DataConsulta.EstablecerItem`, duplicado en los dos proyectos): el método sobreescribe `row[columna] = valor` ANTES de leer el estado (`estadof`) anterior de la fila para decidir si la marca como `"editado"`. `UsuariosDetalle.GuardarNuevo()` llamaba `EstablecerItem("estadof", id, "normal")` justo después de crear el usuario — como la columna que se está tocando ES `"estadof"`, el método leía su propio valor recién sobreescrito (`"normal"`), concluía que la fila no estaba en `"nuevo"`, y la degradaba a `"editado"`. Al exportar, esa fila se mandaba por `UPDATE ... WHERE id=@id` (0 filas afectadas — el id todavía no existía en SQL Server) en vez de por `INSERT`, sin ningún error visible: el `UPDATE` "exitoso" con 0 filas no lanza excepción, la transacción hacía commit, y en memoria la fila quedaba marcada `"normal"` (visible en la grilla) pese a no haberse guardado nunca en la base.
- Fix de dos capas: (1) se quitó la línea `EstablecerItem("estadof", id, "normal")` de `GuardarNuevo()` — sobraba, porque `Nuevo(id)` ya deja `"nuevo"` y el propio `ExportarItemsInterno()` pasa la fila a `"normal"` justo antes de insertarla; (2) se blindó `EstablecerItem()` en los dos proyectos (`SistemaGestion/DataConsulta.cs` y `VisorEmpresa/DataConsulta.cs`) para leer el estado anterior ANTES de sobreescribir la columna, y para no volver a marcar `"editado"` cuando la columna tocada es la propia `"estadof"` — cierra la clase de bug de raíz, no solo el caso puntual.
- **Auditoría del resto de la app** (pedido explícito: "busca otros que sufren del mismo problema"): se confirmó por grep que `UsuariosDetalle` era el ÚNICO lugar de los dos proyectos que llamaba `EstablecerItem("estadof", ...)` directo; se revisaron los otros 22 formularios que crean registros nuevos (todos usan `Nuevo()` + `EstablecerItem()` por campo + `OrdenarData()`, que internamente llama a `ExportarItems()` — patrón correcto en todos); y se revisaron las dos únicas llamadas a `EstablecerItem` con nombre de columna dinámico (`TraspasosDetalle`, `campOtro` = `"origen"`/`"destino"`, nunca `"estadof"` — sin riesgo). No se encontraron otros casos a corregir; el fix en `EstablecerItem` ya protege a toda la app, incluido código futuro.

#### Problema 2 — VisorEmpresa/ConsolaVisor: separar en el panel lateral lo de "nivel empresa" de lo "superior" (ejemplo: Usuarios y Empresas)
- El grupo "Catálogos de empresa" del sidebar (`Precios`, `Empresas`, `Sucursales`, `Regiones`, `Usuarios`) mezclaba catálogos propios de la empresa filtrada (nivel empresa) con administración por encima de una empresa puntual (Empresas = lista de TODAS las empresas; Usuarios = cuentas de acceso al sistema, no de una empresa sola). Se separó en dos grupos con encabezado visible (nuevo estilo `NavSeccion`, texto uppercase 10px `ThemeTextoMuted`): **NIVEL EMPRESA** (Precios, Sucursales, Regiones) y **SUPERIORES** (Empresas, Usuarios). Sin tocar nombres de botones/handlers (`BtnNav_Xxx_Click`), solo reordenados dentro del `StackPanel` del sidebar.

#### Problema — `usuarios.temaC` en desuso, pero seguía bloqueando el login si se borraba de la base
- El usuario pidió sacar la columna `usuarios.temaC` ("no es necesario"). Se confirmó que ningún código la lee/escribe desde hace varias sesiones (el tema visual es 100% local, `ThemeManager`/`TemaVisor`, `theme.txt` por PC) — pero seguía listada como obligatoria en el manifiesto de `EsquemaValidator.cs` (los dos proyectos). Si el usuario la hubiera borrado de SQL Server sin este fix, `EsquemaValidator.Validar()` (que corre en cada login, después del broker) habría reportado "falta la columna" y bloqueado el login de **todos** los usuarios con un cartel de "Estructura incompatible".
- Se sacó `temaC` del manifiesto en `SistemaGestion/EsquemaValidator.cs` y `VisorEmpresa/EsquemaValidator.cs`, se corrigió un comentario desactualizado en `VisorEmpresa/ConsolaVisor.xaml.cs` que decía que la app principal todavía la usaba, y se documentó en `ESTRUCTURA-BASE-DE-DATOS.md` con el `ALTER TABLE usuarios DROP COLUMN temaC;` exacto para que el usuario la borre de la base cuando quiera (no ejecutable desde este entorno — sin conexión directa a SQL Server).

#### ArticulosGeneral — nuevo botón "Informe PDF" (SistemaGestion y VisorEmpresa)
- Pedido: un botón al lado de "Informe Excel" que genere un PDF del catálogo agrupado por Producto & Familia. Se creó `InformePdfArticulos.xaml(.cs)` en los dos proyectos (copia del patrón de `InformeExcelArticulos`: mismo diálogo con nombre + fecha/hora de corte), con el PDF armado a mano vía `PdfSharp` (banda celeste de grupo, encabezado de columnas gris, igual estilo que `InventariosGeneral.GenerarPlantillaPdf`). VisorEmpresa usa `ConsultasEmpresa.ObtenerStockEmpresa` (stock de toda la empresa) en vez de `StockCalculator.ContarStock`, mismo motivo que el resto de los informes del visor (`AppState.SucursalActiva` nunca se puebla ahí).

#### ArticulosGeneral — filtro "Condición" y resumen de categorías, en los 4 informes (Excel/PDF × SistemaGestion/VisorEmpresa)
- Nuevo combo **"Condición"** en los diálogos de informe (Todos los artículos / con stock negativo / con stock / con stock 0) que filtra qué artículos entran al informe.
- El título pasó a mostrar nombre del informe + fecha de corte completa (fecha y hora) + la Condición aplicada, en vez de un título fijo.
- Se agregó una tabla de resumen por categoría después de la tabla de artículos (separada por una fila en blanco), calculada sobre el mismo conjunto ya filtrado por Condición.
- **Ajustada dos veces contra archivos reales que mandó el usuario como referencia** (generados por `InventariosGeneral`, no por este informe nuevo):
  1. Un PDF real de `GenerarPdfInventario` mostró que la tabla principal del PDF de referencia no repite "Categoría" como columna por fila (solo aparece en el resumen final) y que el resumen va en una caja con banda celeste + encabezado + fila "Total general" destacada, con paginación propia usando un delegado intercambiable (`dibujarEncabezadoPagina`) en vez de un mecanismo de página aparte. El PDF de Artículos se reescribió para copiar esa estructura exacta (tabla principal de 4 columnas: N°/Código/Descripción/Stock).
  2. Un Excel real de `GenerarExcelInventario` mostró que el resumen de categorías en Excel usa celdas combinadas + colores (`XLColor.FromArgb(191,219,254)` banda / `(241,245,249)` encabezado) + fórmulas `SUMIF`/`SUM` reales (no valores ya calculados en C#) — y que solo tiene una columna de valor (ahí "Cantidad", acá "Stock total"), no dos. Se reescribió el Excel de Artículos para igualar ese diseño exacto, y se sacó la columna "Cantidad de artículos"/"Cant. artículos" que había quedado de más en el primer intento (Excel y PDF, los 4 archivos). Para que el `SUMIF` encuentre las filas sin categoría, `catDesc` ahora se normaliza a `"(sin categoría)"` en el momento de leer cada artículo (no solo al agrupar) — antes esas filas quedaban con la celda vacía y el `SUMIF` nunca las contaba.
- **Pendiente de verificar en Visual Studio**: que las fórmulas `SUMIF`/`SUM` calculen bien al abrir el Excel (ClosedXML no las evalúa al guardar, se recalculan recién al abrir en Excel real) y que el diseño del PDF/Excel coincida ahora sí con los archivos de referencia — no hay forma de generar/abrir estos archivos en este entorno para comparar pixel a pixel.

#### Notas / pendientes de esta sesión
- **Sin SDK de .NET en el entorno remoto**: todo se verificó por lectura de código, grep, conteo de llaves y validación de XML bien formado — nada se compiló acá. El broker sí se compiló y corrió de verdad, pero en el VPS del usuario (`dotnet publish`/`dotnet run` reales), no en este entorno. Los cambios de `SistemaGestion`/`VisorEmpresa` de esta sesión quedan pendientes de compilar en Visual Studio local (ver "Lo Que Falta Por Hacer").
- **Falso positivo repetido del stop-hook de git**: cada commit de la sesión se marcó como "Unverified" por el hook local (`noreply@anthropic.com` no coincide con `allowedSignersFile`, que no existe en este sandbox) — verificado en una sesión anterior que GitHub sí atribuye los commits correctamente; no se amendó/rebaseó ningún commit ya pusheado a `master`.
- El usuario todavía tiene pendiente lanzar manualmente "Run workflow" en GitHub Actions para publicar la próxima versión (`SistemaGestion.csproj` sigue en `1.0.5`, subido por el usuario directo en GitHub durante esta sesión, commit `edd497d`) — ver `CLAUDE.md`, sección "Publicar versiones".

### Sesión 2026-07-20 — VisorEmpresa: import inteligente de Excel, guardado async (piloto), "Crear Plantilla" Excel/PDF en Precios e Inventarios, columna articulos.estadoV, y catálogo completo editable (Familias/Productos/Industrias/Categorías); SistemaGestion: fixes de pestañas e Índice automático (rama `master`)

> Sesión muy larga, pedidos numerados turno a turno ("problema1", "problema2"...) sobre VisorEmpresa y SistemaGestion. Se documenta de una sola vez, agrupada por bloque temático en el orden en que se pidió.

#### VisorEmpresa/PreciosDetalle — Importar Excel: detección inteligente de columnas
- El import ya no depende de la posición fija de columnas (antes: Código=col.1, Precio=col.5). Ahora `DetectarColumnas` busca en la fila de encabezados el texto "código"/"precio" (sin tildes, sin importar mayúsculas, por coincidencia parcial — "Cód. Artículo" o "Precio Venta" también matchean) vía `NormalizarEncabezado` (quita diacríticos con `NormalizationForm.FormD`).
- Decisión de comportamiento (pedida explícitamente, revertida de una idea intermedia): el import **reemplaza el precio de TODO el catálogo**, no solo de las filas presentes en el Excel — el artículo cuyo código aparece con un valor numérico toma ese valor (incluido 0 explícito); cualquier otro artículo (ausente del Excel, o con la celda de precio en blanco) queda en 0.

#### VisorEmpresa/PreciosGeneral y SistemaGestion/InventariosGeneral — botón "Crear Plantilla" (Excel/PDF)
- El botón "Plantilla Excel" de Precios se renombra a **"Crear Plantilla"**; al hacer clic abre un diálogo nuevo (`SeleccionarFormatoWindow`, copia propia por proyecto) para elegir Excel o PDF.
  - Excel: catálogo completo sin agrupar, con columnas Producto y Familia (sin cambios de fondo).
  - PDF (nuevo `GenerarPlantillaPdf`): catálogo completo agrupado por Producto & Familia (banda celeste, mismo estilo que `GenerarPdfListaPrecios`) — Producto/Familia van en la banda de grupo, no como columnas de datos. Precio/Cantidad arranca en blanco en el PDF (no "0" — pedido explícito), pero sigue en 0 en el Excel (sin cambios, tal como se pidió).
- Mismo patrón replicado en **SistemaGestion/InventariosGeneral** (nuevo botón "Crear Plantilla", Excel con Cantidad, PDF agrupado) y nuevo botón **"Importar Excel"** en **SistemaGestion/InventariosDetalle**, mismo mecanismo de detección inteligente de columnas (Código/Cantidad) que Precios.

#### Fix: Descripción incompleta en 5 exportaciones Excel/PDF
- Auditoría (agente Explore) encontró que `VisorEmpresa/PreciosGeneral` (`GenerarPlantillaExcel`, `GenerarPdfListaPrecios`, `GenerarExcelListaPrecios`) y `SistemaGestion/InventariosGeneral` (`GenerarPdfInventario`, `GenerarExcelInventario`) escribían la Descripción cruda del artículo (sin familia ni modelo) en vez de la descripción completa. Las 5 corregidas para usar `FuncionesComunes.UnirVariables(desc, famDesc, modelo)`, igual que ya hacían correctamente `InformeExcelArticulos`/`ArqueoExcelArticulos`.

#### Nueva columna `articulos.estadoV` ("mostrar"/"ocultar") — filtro para las plantillas
- El usuario agregó la columna en SQL Server (`SELECT a.*` en `AppLoader` ya la trae sola, sin cambios de query). Se agregó un combo **"VISIBLE EN PLANTILLAS"** en `SistemaGestion/ArticulosDetalle` (Mostrar/Ocultar), que se carga y guarda junto al resto del artículo.
- Decisión de diseño explícita (comunicada al usuario, no confirmada aún): el filtro es "opt-out", no "opt-in" — se excluye SOLO lo explícitamente en "ocultar"; `NULL`/vacío/"mostrar" cuentan como visible, para que agregar la columna no vacíe de golpe las plantillas de artículos ya cargados. Si el usuario prefiere lo contrario (arrancar todo oculto hasta marcar "Mostrar" a mano), es un cambio de una línea en `EsVisibleEnPlantillas` (duplicado en `VisorEmpresa/PreciosGeneral.xaml.cs` y `SistemaGestion/InventariosGeneral.xaml.cs`).
- El filtro se aplica solo en las 4 generadoras de "Crear Plantilla" (no en Informe Excel/Arqueo/exports de un documento ya guardado), por pedido explícito ("al crear una plantilla").

#### VisorEmpresa — instancia única + auto-actualización
- `SistemaGestion/App.xaml.cs` y `VisorEmpresa/App.xaml.cs`: `Mutex` nombrado por app (`SistemaGestion.InstanciaUnica`/`VisorEmpresa.InstanciaUnica`) — si ya hay una ventana abierta de la misma app en la misma sesión de Windows, la segunda instancia avisa y se cierra sola. Las dos apps entre sí siguen pudiendo correr juntas (son ejecutables distintos).
- VisorEmpresa nunca había tenido código de auto-actualización (a diferencia de SistemaGestion), pese a que su release ya se publica en el canal Velopack "visor" del mismo repo. Se agregó `VisorEmpresa/ActualizadorApp.cs` (mismo patrón que `SistemaGestion/ActualizadorApp.cs`) y se conectó a `LoginVisorWindow` (bloqueo de login si hay versión nueva) y `ConsolaVisor` (botón "🔄 Actualizar" en la top bar).

#### VisorEmpresa/PreciosDetalle — guardado async (piloto, no rollout completo)
- Diagnóstico: `Guardar()`/`GuardarNuevo()`/`GuardarEditar()` corrían síncrono en el hilo de UI, y el único tramo que habla con SQL Server (`DataConsulta.OrdenarData()` → `ExportarItems()`) congelaba la ventana durante el round-trip de red.
- Fix aplicado (piloto, solo este archivo): los 3 métodos pasan a `async`, el tramo de red se manda a `Task.Run`, y mientras dura se deshabilita TODA la ventana (no solo la pestaña) — `_tabla` de `DataConsulta` es compartida por toda la app y no es thread-safe, así que ninguna otra pestaña puede tocarla en paralelo durante el guardado.
- **Pendiente, explícitamente no resuelto**: el mismo cuello de botella se repite en **~34 archivos / 81 llamados** a `OrdenarData()` en ambos proyectos (todos los módulos con botón Guardar). Replicar el patrón al resto queda pendiente de confirmación del usuario, dado el tamaño del cambio y que no se puede compilar en este entorno para verificarlo masivamente.

#### Fixes chicos en SistemaGestion
- **Pedidos/Traspasos/CorreccionesDetalle**: "Importar Artículos" y "Buscar Artículo" podían quedar abiertos los dos a la vez (dos pestañas selectoras distintas para el mismo documento). Fix en el único método compartido, `ArticulosGeneral.OpenAsTab`: ahora también cierra la variante contraria para el mismo contexto antes de abrir la nueva, así queda como mucho una abierta.
- **ArticulosDetalle**: el campo Índice pasa de editable a solo lectura (`ReadOnlyInput`) — siempre se calcula solo (`RecalcularIndicePorFamilia` al elegir/cambiar familia, o heredado del artículo de referencia en modo insertar), ya no se puede tipear a mano.

#### VisorEmpresa — catálogo completo editable (Familias/Productos/Industrias/Categorías + Artículos)
- Hasta esta sesión, VisorEmpresa era de solo lectura para todo el catálogo de artículos: Familias/Productos/Industrias/Categorías no existían ni como pantallas propias, y `ArticulosDetalle` era un visor de 73 líneas sin Guardar. Ahora:
  - 4 módulos nuevos, copias editables de sus equivalentes en SistemaGestion, adaptadas al namespace/using de VisorEmpresa (mismo patrón que ya usaban Precios/Empresas/Sucursales/Usuarios/Regiones): `FamiliasGeneral`/`Detalle`, `IndustriasGeneral`/`Detalle`, `ProductosGeneral`/`Detalle`, `CategoriasGeneral`/`Detalle`. Cada uno con su propio `OpenAsTab` (selector en pestaña) y dedup key propia (`seleccionar-familia|...`, etc.). Sin dependencia de `AppState.SucursalActiva`/`AperturaActiva`/`RegionActiva` (confirmado en la auditoría previa — estos 4 módulos no los tocan).
  - `VisorEmpresa/ArticulosGeneral`: se agregan Nuevo/Insertar/Editar/Eliminar (antes de solo lectura, doble clic abría un detalle no editable). Se agregan los helpers estáticos `RenumerarFamilia`/`SincronizarAppsheetsTrasCambio` (ya existían en `SistemaGestion.ArticulosGeneral`, necesarios para Guardar). "Arqueo Excel" queda explícitamente fuera de alcance (no existe `ArqueoExcelArticulos` en VisorEmpresa, y el usuario confirmó no portarlo).
  - `VisorEmpresa/ArticulosDetalle`: reconstruido de cero como formulario editable completo (antes solo mostraba datos), con selectores de Familia/Industria/Categoría apuntando a los 4 módulos nuevos, y el combo `CboEstadoV` incluido. **Sin "Ver Movimientos"**: depende de `MovimientosGeneral`, que usa `AppState.SucursalActiva`/`AperturaActiva` — nunca pobladas en VisorEmpresa (mismo problema ya documentado con `StockCalculator`); el usuario confirmó explícitamente no portarlo en vez de portarlo mal.
  - `ConsolaVisor`: 4 secciones nuevas en la barra lateral (agrupadas junto a Artículos), con sus paneles fijos, entradas en `_pestañasPorSeccion`/`_pestañaSeleccionadaPorSeccion`, `AsignarPanelFijo` y `BtnNav_Xxx_Click`. `RecargarPaneles()` (se dispara al cambiar de Empresa en la top bar) también recrea los 4 paneles nuevos — si no, cambiar de empresa dejaría datos de la empresa anterior en pantalla (riesgo real señalado por la auditoría previa, en el mismo espíritu que la trampa de `SucursalActiva`).
  - Un agente en background (para reconstruir `ArticulosDetalle`) falló por límite de uso de la sesión a mitad de tarea; se rehizo a mano con el mismo material fuente, sin pérdida de trabajo.

#### Discusión de seguridad: credenciales de SQL Server (sin cambios de código)
- El usuario preguntó (vía `/critico` y `/ideas`) si convenía embeber/automatizar la conexión a SQL Server del VPS nuevo en vez del flujo actual (config manual cifrada con DPAPI por PC/usuario, `ConexionConfig.cs`). Se descartó embeber la credencial en el binario (la app se distribuye desde un repo público de GitHub, necesario para que Velopack lea releases sin token — cualquiera podría decompilar el .exe y sacarla) y también se descartó mover el repo al VPS propio (no resuelve nada: el binario compilado igual llega a cada PC, y rompería el pipeline de releases que depende del repo público).
- Dirección elegida para una sesión futura: **SQL Server Application Roles** (`sp_setapprole`) — conexión inicial con login de privilegios mínimos (puede ser compartido), login real de la app (tabla `usuarios`) activa el rol con permisos reales, usando un token de sesión de corta duración en vez de una contraseña fija. Sin cambios de código todavía; se preparó un prompt de arranque para la próxima sesión (pide investigar el flujo actual antes de tocar nada, y proponer un plan con el script SQL exacto — la creación del Application Role en el VPS es un paso manual del usuario, no ejecutable desde este entorno).

### Sesión 2026-07-12/13 — Rename WpfAppVba→SistemaGestion, funciones Excel (Plantilla/Importar Precios, Arqueo Excel), y VisorEmpresa: caché completa al loguear + independencia total de código (rama `master`)

> Sesión muy larga con varios bloques de trabajo bien diferenciados, documentados de una sola vez. Los primeros bloques (versión, rename, features de Excel) fueron pedidos concretos turno a turno; el bloque final (caché de VisorEmpresa) partió de una pregunta del usuario sobre por qué el visor volvía a consultar SQL al loguear, y terminó en una serie de fixes encadenados hasta lograr que el login precargue todo lo que las pantallas necesitan.

#### Rename del proyecto principal: WpfAppVba → SistemaGestion
- La carpeta/csproj/sln/namespace de la app principal pasaron de `WpfAppVba` a `SistemaGestion` (el repo de GitHub sigue llamándose `maister1122/WpfAppVba`, solo cambió el proyecto de adentro). El ejecutable resultante es `SistemaGestion.exe`.
- `SistemaGestion.sln` renombrado a `Solucion.sln` (el nombre casi idéntico a la carpeta `SistemaGestion/` confundía en el listado de archivos de GitHub).
- Versiones (`<Version>`) de `SistemaGestion.csproj` y `VisorEmpresa.csproj` reiniciadas a `1.0.1` tras borrar manualmente los releases viejos (`v1.0.0`–`v1.1.4`) del repo renombrado, ya que no se había publicado ninguna versión nueva todavía bajo el nombre nuevo.

#### VisorEmpresa/PreciosGeneral + PreciosDetalle — Plantilla e Importación de precios por Excel
- Nuevo botón **"Plantilla Excel"** en `PreciosGeneral` (izquierda de "Nueva Lista de Precios"): exporta el catálogo completo de artículos (Código/Producto/Familia/Descripción/Precio) con Precio en 0, en el mismo orden "cascada" (Producto→Familia→Índice) que usa `PreciosDetalle`/`ArticulosGeneral` — no alfabético por código (bug reportado y corregido).
- Nuevo botón **"Importar Excel"** en `PreciosDetalle`: lee esa plantilla ya editada y actualiza `Precio` por artículo (columna Código + columna Precio). Ajustado dos veces a pedido del usuario: (1) mensaje amigable si el archivo sigue abierto en Excel (`IOException`) en vez de un error crudo; (2) decisión final explícita — **reemplaza el precio de todo artículo presente en el Excel, incluso si el valor es 0** (se descartó la idea intermedia de "0 = no tocar", que el usuario pidió revertir).

#### SistemaGestion/ArticulosGeneral — nuevo botón "Arqueo Excel"
- Nuevo botón a la derecha de "Informe Excel", que abre un diálogo propio (`ArqueoExcelArticulos`, mismo patrón que `InformeExcelArticulos`: nombre + fecha/hora de corte) y genera un Excel de conteo físico vs. sistema, replicando fielmente un excel de referencia que subió el usuario:
  - Bloque resumen (FECHA con fecha+hora completas, INFORME, ESTADO, NO REVISADOS, ERRORES vía fórmulas `COUNTIFS`).
  - Bloque de totales por categoría, como tabla propia de Excel (**TablaCategorias**) con fila de totales activada (suma en sistema/inventario).
  - Tabla principal de artículos (**Tabla1**): columnas `linea`(secuencial)/`articulo`(código real)/`categoria`/`familia`/`descripcion`/`sistema`(stock actual)/`revicion`(en blanco, para completar a mano)/`inventario`/`estado`/`hoja`/`referecia`/`observacion`/`diferencia`/`cantidad` (fórmulas), con fila de totales activada y formato condicional (ERROR en rojo negrita, NO REVISADO en violeta) restringido solo a la columna `estado`.
  - Cuadrícula de bordes finos en todos los bloques de resumen, igual que la plantilla de referencia.
  - Fix de orden de escritura: activar la fila de totales de una tabla de Excel internamente hace un `InsertRowsBelow(1)` — hubo que crear/activar `TablaCategorias` ANTES de escribir el resto del contenido para no desalinear filas ya escritas.

#### Asesoría sobre despliegue a un VPS (Hostinger KVM 1) — sin cambios de código
- Conversación extensa (sin tocar código) sobre migrar la base SQL Server a un VPS Linux de 1 vCPU/4GB: se confirmó que SQL Server sí corre en Linux, se revisó que el manejo de conexión de la app ya es resiliente (`SqlRetry`, reconexión en `DatabaseConnection`), y se explicaron los topes duros de SQL Server Express en Linux (buffer pool ~1.4GB, 10GB por base, sin relación con el hardware del VPS) y las implicancias de latencia alta (~150ms) sobre una app que guarda de forma síncrona en el hilo de UI.

#### VisorEmpresa — fix de resiliencia real en producción
- Un `SqlException` real de producción (timeout de handshake) en `ConsultasEmpresa.EjecutarConsulta`/`ValidarLogin` reveló que, a diferencia de `DataConsulta` (app principal), `ConsultasEmpresa.cs` nunca había usado `SqlRetry` — un microcorte de red no se reintentaba solo. Fix: las dos rutas ahora pasan por `SqlRetry.Ejecutar`.

#### VisorEmpresa — caché de Dashboard/Stock a nivel de empresa completa (sin sucursal ni período)
- Pedido explícito: "que todo se guarde en caché al loguear, igual que SistemaGestion" — matizado tras explicar que SistemaGestion tampoco cachea *todo* sin límites (los documentos siempre están acotados a una sucursal + un período activo; solo los catálogos son ilimitados) — el usuario confirmó el alcance real: VisorEmpresa debía cachear **solo por empresa** (sin sucursal, sin período), igual que ya hacen `documentosL`/`precios`.
- Se agregó `ConsultasEmpresa.ConectarCacheDashboardEmpresa(empresa)`: carga en listas propias (`PedidoCache`/`TraspasoCache`/`CorreccionCache`, y diccionarios de apertura por sucursal) los pedidos/traspasos/correcciones de TODA la empresa sin límite de fecha — independiente de los `ConectarCachePedidos/Traspasos/Correcciones/Facturas` ya existentes (año+sucursal, para las pantallas de navegación de documentos), que el usuario decidió explícitamente **dejar como están** ante la duda de qué hacer con ellos.
- `ObtenerStockEmpresa`, `CargarMovimientos`, `CargarResumenPedidos` y `CargarTraspasosInternos` reescritos para leer de esa caché en memoria en vez de golpear SQL en cada llamada. Se precalienta al loguear (`LoginVisorWindow`, tras `AppLoader.ConectarProductos`) y al cambiar de empresa desde la top bar (`ConsolaVisor`).

#### VisorEmpresa — independencia TOTAL de código respecto a SistemaGestion
- El usuario preguntó si tocar código del visor también tocaba SistemaGestion — respuesta: sí, para los ~22 archivos que estaban vinculados por `<Compile Link=.../>` en `VisorEmpresa.csproj` (capa de datos, conexión/configuración, utilidades, herramientas admin, ventanas de configuración de servidor, más los 2 temas). El usuario pidió eliminar ese riesgo por completo.
- Se copiaron físicamente los 22 archivos a `VisorEmpresa/` y se renombró su namespace: `SistemaGestion.Data` → `VisorEmpresa.Data`, `SistemaGestion` → `VisorEmpresa` (el usuario eligió explícitamente el rename completo de namespace en vez de solo romper el vínculo físico, pese al mayor esfuerzo/riesgo). Se actualizaron los ~27 archivos consumidores (`using SistemaGestion.Data;` → `using VisorEmpresa.Data;`) y se limpió `VisorEmpresa.csproj` quitando las 6 secciones de `<Compile Link>`/`<Page Link>`.
- Se preservó a propósito, sin tocar: el truco de `ConsolaVisor.xaml.cs` (declara `namespace SistemaGestion { class ConsolaMovimientos }` — no es código compartido, solo imita el nombre para que los formularios propios del visor puedan hacer `Window.GetWindow(this) as ConsolaMovimientos` vía `using SistemaGestion;`), el ícono compartido (asset, no código) y la carpeta `%AppData%\SistemaGestion\conexion.dat` (convención de almacenamiento en disco para que un servidor configurado en una app aparezca también en la otra).
- Ver la sección "VisorEmpresa (app compañera)" más arriba, actualizada con el detalle completo de esta nueva arquitectura.

#### VisorEmpresa — precarga de Pedidos/Traspasos/Correcciones/Facturas al loguear (sin filtro de sucursal)
- Pedido explícito: que `ConectarCachePedidos/Traspasos/Correcciones/Facturas` se carguen al loguear, sin filtro de sucursal (toda la empresa en una sola consulta), ya que las 4 pantallas (`PedidosGeneral`, etc.) iban a filtrar directamente de la caché para armar sus grillas.
- Se agregó memoización por `(empresa, año)` a los 4 métodos (llamadas repetidas con la misma clave son un no-op) y se precargan al loguear y al cambiar de empresa. Las 4 pantallas ya no vuelven a consultar SQL al cambiar su combo de sucursal (o Origen/Destino en Traspasos) — filtran en memoria (`CargarMeses`/`CargarXxx` ahora aplican el filtro ellos mismos).
- Simplificación de seguimiento: como ningún llamador pasaba ya un valor puntual de sucursal, se quitó por completo el parámetro `sucursalId`/`origenId`/`destinoId` de los 4 métodos (y el `filtroSuc`/`condLado` que armaban en el SQL) — quedan solo `(empresa, año)`, consulta siempre a nivel de toda la empresa.
- **Causa real de la demora restante al abrir cada pestaña por primera vez**: `CargarSucursalesEmpresa` (usada por las 4 pantallas + Articulos + Dashboard para poblar el combo de sucursales) seguía siendo una consulta SQL en vivo, no cacheada, repetida por separado en cada pantalla. Se reescribió para leer de `Sql.SucursalesObj` (catálogo ya cargado por `AppLoader.ConectarProductos` con el mismo filtro) en vez de golpear SQL.
- **Pendiente, propuesto y no confirmado todavía**: `CargarPedidos()`/`CargarTraspasos()`/`CargarCorrecciones()`/`CargarFacturas()` calculan cantidad/importe por documento con `CalcularCantidad()`/`CalcularImporte()`, que recorren TODA la tabla de líneas por cada documento (O(documentos × líneas) — ahora sobre toda la empresa, no una sucursal). Detectado al explicar por qué la primera apertura de cada pestaña sigue siendo más lenta que las siguientes; el arreglo (agrupar líneas por documento en un diccionario una sola vez) fue presentado al usuario pero todavía no confirmado.

#### Notas / pendientes de esta sesión
- **Sin SDK de .NET en el entorno remoto**: todos los cambios de código se verificaron por lectura/grep exhaustivos (namespaces, `using`, `x:Class`, nombres duplicados), pero no se compilaron. Compilar en máquina local antes de dar por cerrada la sesión, en especial el rename completo de namespace de VisorEmpresa (22 archivos + ~27 consumidores) y el Arqueo Excel (fórmulas/tablas de ClosedXML).
- **Pendiente de decisión del usuario**: si aplicar la optimización de `CalcularCantidad`/`CalcularImporte` (ver detalle en la sección "VisorEmpresa (app compañera)" arriba).

### Sesión 2026-07-08 — Fix de lógica de traspasos (`documentosT`), revert a `1d78abf`, y VisorEmpresa independiente en lógica de negocio (Etapa 1: Precios, Etapa 2: 4 formularios Detalle) (rama `master`)

> Sesión larga con dos partes claramente separadas por un revert en el medio. Primera parte: fixes de lógica sobre el esquema `sucursal`/`movimiento`/`sucursalR` de `documentosT` (post-migración). Luego el usuario pidió volver al commit `1d78abf71c673c05ede6dc4ae6243e1f3651763a` (esquema previo `origen`/`destino`/`emitido`), confirmado explícitamente pese a saber que es incompatible con la base de datos actual. Segunda parte: los mismos tres pedidos rehechos sobre el esquema viejo, más el arranque de independencia arquitectónica de VisorEmpresa. Se documenta de una sola vez.

#### Parte 1 (esquema `sucursal`/`movimiento`/`sucursalR`, luego revertida)
- Aclarada la semántica correcta: `documentosT.sucursal` = quien emite el documento; `documentosT.movimiento` = "entrada"/"salida" relativo a `sucursal`; `documentosT.sucursalR` = contraparte (destino si `movimiento="salida"`, origen si `movimiento="entrada"`). Se corrigió en toda la app un bug de lectura `emitido` (columna que ya no existía en este esquema) en `TraspasosGeneral` (3 puntos: `CargarTraspasos`, `ConstruirFilaTraspaso`, `BtnEntregarTodos_Click`) y `MovimientosGeneral` (1 punto) — debían leer `sucursal`.
- `TraspasosGeneral` Grid1: columna `SucursalR` renombrada a "Origen/Destino"; luego corregido un bug donde mostraba la sucursal propia activa en vez de la contraparte real, con la fórmula `contraparte = sucursal == AppState.SucursalActiva ? sucursalR : sucursal`.
- `TraspasosDetalle.LblSucursalTipo`: lógica de "sucursal destino"/"sucursal origen" ajustada a la fórmula bidireccional pedida por el usuario.
- Columna **Referencia** agregada a Grid1 en Pedidos/Traspasos/Correcciones/Inventarios/Precios General, a la derecha de Cliente/Proveedor, Origen/Destino, Motivo, Fecha, Región.
- Todo este trabajo quedó sin efecto al revertir al commit `1d78abf` (ver abajo) — se documenta igual porque el mismo pedido se rehizo sobre el esquema viejo en la Parte 2.

#### Revert no destructivo a `1d78abf71c673c05ede6dc4ae6243e1f3651763a`
- El usuario pidió volver ese commit exacto (esquema `documentosT.origen`/`destino`/`emitido`, previo a la migración a `sucursal`/`movimiento`/`sucursalR`). Confirmado explícitamente vía pregunta directa, a sabiendas de que es incompatible con la base de datos ya migrada.
- Aplicado con `git commit-tree <1d78abf>^{tree} -p <HEAD> -m "..."` (contenido del commit viejo, pero con un padre que preserva toda la historia intermedia — no reescribe nada ya publicado), luego `git update-ref` + push normal a `master`. Mismo patrón ya usado como precedente en el commit `ae35f03`. `CONTEXT.md` también quedó con el contenido de esa fecha (por eso no tenía ninguna mención de VisorEmpresa hasta esta edición).

#### Parte 2 (esquema `origen`/`destino`/`emitido`, estado actual)
- **`WpfAppVba/TraspasosGeneral`**: columnas separadas "Origen"/"Destino" fusionadas en una sola "Origen/Destino" (`ContraparteDesc`, reemplaza `OrigenDesc`/`DestinoDesc`); corregido el mismo bug de "muestra la sucursal propia" con `contraparteId = origen == AppState.SucursalActiva ? destino : origen`. Explícitamente sin tocar VisorEmpresa (su propio `TraspasosGeneral` es un archivo separado, no vinculado).
- Columna **Referencia** rehecha en Pedidos/Traspasos/Correcciones/Inventarios/Precios General (mismo pedido que en la Parte 1, sobre el esquema restaurado).
- **`VisorEmpresa/TraspasosGeneral`**: eliminado por completo el filtro/columna de "movimiento" (`CboTipoMovimiento`, `ActualizarDisponibilidadMovimiento`, `ObtenerFiltroTipo`, el bloque de filtro en `CargarTraspasos`, el handler de selección) — el filtro de sucursal (`CmbSucursal`) no se tocó.
- **`VisorEmpresa/TraspasosDetalle`**: agregado un panel explícito con controles de Origen y Destino (antes solo mostraba "destino").

#### Arquitectura: VisorEmpresa independiente en lógica de negocio
- El usuario preguntó por qué VisorEmpresa mostraba `TraspasosDetalle` sin tener "su propio" formulario — respondida sin cambios de código: en ese momento era un archivo vinculado compartido 1:1 con WpfAppVba.
- Pedido explícito de hacer VisorEmpresa independiente en su lógica de consulta/caché, con foco inicial en el problema de Precios (hacía una consulta SQL redundante en vez de usar caché propia del visor). El usuario eligió, vía pregunta directa: alcance = **independiente en lógica de negocio** (la infraestructura genérica se mantiene compartida/vinculada; el código con lógica de negocio pasa a copia propia), enfoque = **por etapas, empezando por Precios**.
- **Etapa 1 (Precios)**: `PreciosGeneral` se mantuvo vinculado (su caché, poblada por `AppLoader.ConectarProductos()`, ya era correcta a nivel de empresa — no necesitaba duplicarse). Se creó `VisorEmpresa/PreciosDetalle` (copia propia) cuyo cálculo de Stock/Disponible por empresa pasó a usar una nueva `ConsultasEmpresa.ObtenerStockEmpresa(empresa, hasta, forzarRecarga)` — 5 consultas SQL agregadas a nivel de empresa, cacheadas en memoria (`Dictionary<(Empresa, Hasta), StockEmpresaResultado>`), reemplazando el cálculo local que antes repetía la consulta cada vez. Los 3 call sites de `PreciosGeneral` que abrían el detalle se separaron con `#if VISOR ... #else ... #endif`.
- **Etapa 2 (4 formularios Detalle)**: se crearon copias propias de `PedidosDetalle`/`TraspasosDetalle`/`CorreccionesDetalle`/`FacturasDetalle` en `VisorEmpresa/` (namespace `VisorEmpresa`), simplificadas a siempre-solo-lectura (se quitó el parámetro/campo `_soloLectura` y el método `AplicarModoSoloLectura()` condicional — ahora se aplica incondicionalmente). Los 4 `*General` correspondientes de VisorEmpresa actualizaron su único call site (`new XxxDetalle(...)`) quitando `soloLectura: true`; por resolución de tipos de C# (mismo namespace gana sobre `using`), esas llamadas ya apuntaban automáticamente a las nuevas clases `VisorEmpresa.XxxDetalle` sin cambios adicionales. `VisorEmpresa.csproj`: quitadas las entradas de archivo vinculado para estos 5 formularios (ahora se recogen como archivos propios vía globbing implícito del SDK).
- **Bug encontrado y corregido durante la Etapa 2**: `StockCalculator.ContarStock` (usado por los 3 Detail forms recién duplicados) dependía de `AppState.SucursalActiva`/`AperturaActiva`/`DataFechaFinal`, que **nunca se pueblan en VisorEmpresa** (solo los llena `AppState.ActualizarBase()`, exclusivo del login de la app principal; confirmado por grep que nunca se llama bajo `VisorEmpresa/`) — daba stock 0 o incompleto sin ningún error visible. Fix: se generalizó `ConsultasEmpresa.ObtenerStockEmpresa` con parámetro `hasta` y se agregó `ObtenerStockEmpresaAlCierre(empresa, anio, forzarRecarga)`; los 3 formularios se reengancharon a estos métodos en vez de `StockCalculator`.
- **Bug encontrado y corregido en el mismo audit**: `PedidosDetalle.CargarStockYPrecios()` filtraba el historial de precios por `AppState.RegionActiva`, que también es siempre `""` en VisorEmpresa (fijado explícitamente así en `LoginVisorWindow`) — el panel de precios históricos quedaba siempre vacío. Fix: se quitó el filtro de región (el visor no tiene concepto de región activa única).
- **Compile error CS0136 reportado por el usuario** (Visual Studio, `VisorEmpresa/ConsultasEmpresa.cs:467`): la variable `clave` (clave de caché `(empresa, fechaHasta.Date)`, declarada en el ámbito del método `ObtenerStockEmpresa`) colisionaba con dos declaraciones internas del mismo nombre — la del local function `Obtener(sucursal, articulo)` y la del `foreach (var (clave, acumulado) in porSucursal)`. Ambas renombradas a `claveSucArt`.
- **Bug encontrado y corregido en un segundo audit** (tras confirmar la compilación): `VisorEmpresa/TraspasosDetalle` calculaba `esLocal = (emitido == AppState.SucursalActiva)` — siempre `false` (`SucursalActiva` vacía en el visor), lo que reetiquetaba todo documento "pendiente" como "pendiente revisar", contradiciendo la decisión ya tomada en `TraspasosGeneral` de mostrar el estado crudo sin reinterpretar en el visor. Fix: se quitó la rama `esLocal` y se muestra directamente `estadoDB`, confiando en `AplicarModoSoloLectura()` (ya incondicional) para el resto del bloqueo de edición.

#### Notas / pendientes de esta sesión
- **Sin build en el entorno cloud**: todos los cambios se verificaron por revisión manual y balance de llaves (sin SDK de .NET disponible); compilar y probar en máquina local antes de publicar, en especial el revert de esquema (`origen`/`destino`/`emitido` debe coincidir con las columnas reales de la base actual — el usuario confirmó que sí) y los nuevos cálculos de stock por empresa en `ConsultasEmpresa`.
- **Queda intencionalmente compartido/vinculado** (no se tocó ni se planea duplicar salvo que aparezca una necesidad concreta): `DatabaseConnection`, `SqlData`/`DataConsulta`, `AppLoader`, `AppState`, `StockCalculator`, `PreciosGeneral`, `EmpresasGeneral`/`Detalle`, `SucursalesGeneral`/`Detalle`, `UsuariosGeneral`/`Detalle`, `RegionesGeneral`/`Detalle` — ver la nueva sección "VisorEmpresa (app compañera)" arriba para el detalle completo.
- **Pendiente sin decidir**: la idea original de "AppLoader/SqlData independiente" como próxima etapa se dejó de lado a favor de dos rondas de auditoría de bugs concretos (evidencia real > refactor arquitectónico especulativo); si se retoma, priorizar evidencia de un problema concreto antes de duplicar más infraestructura genérica.

### Sesión 2026-07-02 — Nuevo módulo Facturas (documentosF/facturas/transaccionesF) completo + fixes de guardado en grids y refresco de Tree1 al eliminar, en Facturas/Pedidos/Traspasos/Correcciones (rama de trabajo local `claude/invoices-form-integration-rmvoq8`, todo confirmado se pasó a `master`)

> Sesión larga construida a pedido, turno a turno, sobre las tablas `documentosF`/`facturas` que el usuario ya había creado en SQL Server. El usuario probó cada entrega manualmente y reportó bugs numerados ("problema1", "problema2"...) turno a turno. Se documenta de una sola vez.

#### Módulo Facturas — creación desde cero
- Nuevas pantallas **`FacturasGeneral`**/**`FacturasDetalle`**, integrando las tablas `documentosF` (encabezado) y `facturas` (líneas), replicando el patrón de Pedidos/Traspasos/Correcciones.
- `documentosF.codigo` usa el prefijo `sucursal.signo` igual que los demás documentos; la carga inicial filtra por sucursal activa (`AppLoader.ConectarDocumentos`).
- Nueva sección propia en el sidebar, **"🧾 Facturas"**, ubicada justo después de "🔧 Correcciones" (a pedido explícito del usuario) y antes del separador que da paso a Terceros.
- Infraestructura agregada: `SqlData.DocumentosFObj`/`FacturasObj`/`TransaccionesFObj` (`DataConsulta`); `AppLoader.ConectarDocumentos` con los 3 `Conectar(...)` (documentosF, facturas con JOIN a documentosF, transaccionesF con JOIN a documentosF), todos filtrados por sucursal activa + rango de fechas; `CodigoRegenerator` con `("documentosF", "sucursal")`; `ConsolaMovimientos` con `_panelFacturas`, entrada en `_pestañasPorSeccion["facturas"]`, case en el switch de secciones y `BtnNav_Facturas_Click`.

#### `estado`/`estadoC` + tabla `transaccionesF` (Cobros/Pagos)
- El usuario agregó a `documentosF` las columnas `estado` (`pendiente`/`entregado`) y `estadoC` (`pendiente`/`pendiente parcial`/`cancelado`), y creó `transaccionesF` como sub-ledger de cobros, mismo rol que `transacciones` en Pedidos.
- A diferencia de Pedidos (donde `estado` se calcula automáticamente), en Facturas **`estado` es editable manualmente** por el usuario (ComboBox en el encabezado); `estadoC` sigue siendo calculado automáticamente comparando `importe` de las líneas contra lo cobrado en `transaccionesF` (mismo criterio que Pedidos con `transacciones`).
- `FacturasDetalle` ganó una segunda pestaña **"Cobros"** (`GridCobros`) con Lín./Fecha/Hora/Descripción/Forma (mismo combo `PedidosDetalle.FormasTrasaccion`)/Importe, y guardado diferencial propio (`GuardarLineasCobro`) contra `TransaccionesFObj`.

#### `tercero` (FK a `terceros`) en `documentosF`
- Nueva columna `tercero` (`uniqueidentifier`) aplicada en `FacturasGeneral`/`FacturasDetalle`: resolución por código con búsqueda en vivo (`ResolverTerceroId`/`ActualizarDescripcionTercero`) y selector modal reutilizando `TercerosGeneral.OpenAsDialog`, igual que en Pedidos.

#### `movimiento` (venta/compra), contadores de pendientes y categoría por defecto
- Nueva columna `movimiento` en `documentosF`; filtro ComboBox (Todos/Ventas/Compras) en `FacturasGeneral`, aplicado también a la numeración/listado igual que en los demás módulos con movimiento.
- Nuevos contadores en `FacturasGeneral`: **"Estados pendientes"** (documentos con `estado = pendiente`) y **"Cuentas pendientes"** (documentos con `estadoC = pendiente` o `pendiente parcial`).
- `FacturasDetalle.GridItems`: al insertar/agregar una línea nueva, la columna Categoría se precarga con la primera categoría encontrada en caché (`PrimeraCategoriaId()`, vía `Sql.CategoriasObj.Mover(1)`).

#### Fixes de UX reportados por el usuario en `FacturasDetalle`
- **Categoría (ComboBox) en `GridItems`**: reemplazó a las columnas de texto Categoría/Descripción; el combo mostraba de más un ítem "(sin categoría)" — filtrado para listar solo categorías reales.
- **Columna Importe**: formato alineado al de `PedidosDetalle` (`#\,##0.##`) y fix de que la edición no se guardaba al desenfocar la celda (agregado el parseo manual del `TextBox` en `CellEditEnding`, convención ya usada en el resto de la app — ver más abajo el hallazgo de que faltaba también en Concepto/Descripción).
- **Aviso de pestañas relacionadas al cerrar**: Facturas no tenía el guard `SinPestañasRelacionadas()`/`ConfirmarCierrePestañasRelacionadas` que sí tienen Pedidos/Traspasos/Correcciones/Articulos/Sucursales — agregado.
- **`Box_Observacion`**: no ocupaba el espacio sobrante (quedaba una franja alrededor); ajustadas las `RowDefinition` del layout (`Auto`/`3*`/`*`/`Auto`) para que ocupe todo el espacio disponible.

#### Fix: Tree1 (árbol de meses) no se actualizaba al guardar/editar un documento
- `FacturasGeneral.CargarMeses()` reescrito para preservar el mes/nodo activo (`_mesActivo`, función local `SeleccionarMes`, captura de `tagPrevio` antes de reconstruir el árbol) y se agregaron las llamadas a `CargarMeses()` en `BtnNuevo_Click`/`AbrirEditar` (antes solo se recargaba la grilla, no el árbol).

#### Fix sistémico: numeración de documentos reutilizada tras ocultar/eliminar
- `DataConsulta.SiguienteNumeroDoc`/`SiguienteNumeroDocPorEmpresa`/`SiguienteNumeroDocPorRegion` filtraban `estadof = 'normal'` al calcular el próximo número — un documento oculto/eliminado liberaba su número, que podía reasignarse a otro documento nuevo. Se quitó ese filtro: ahora se considera **todo** registro histórico (sin importar `estadof`) al calcular el siguiente número, igual que ya ocurre con `articulos`/`familias` (índices no reutilizables). Beneficia a Pedidos/Traspasos/Correcciones/Facturas/Precios por igual (helper compartido).

#### Investigado y descartado: "el tercero no se guarda" en `FacturasDetalle`
- Investigación exhaustiva (comparación línea por línea contra el flujo equivalente de `PedidosDetalle`) sin encontrar ninguna divergencia de código. El usuario confirmó luego que fue un error propio de prueba ("era error mío") — no se aplicó ningún cambio.

#### Investigado y NO aplicado (decisión explícita del usuario): `Ocultar` vs `Eliminar` real al borrar documentos
- El usuario notó que borrar un `documentoF` (o cualquier documento) lo deja en `estadof = "oculto"` en vez de `"eliminado"`. Se confirmó que es una convención **universal** en los 12+ módulos del sistema (borrado lógico, nunca `DELETE` físico — ver "Decisiones de Arquitectura"), no un bug específico de Facturas. Al ser un cambio estructural que afectaría la semántica histórica de todos los módulos, se presentaron opciones con `AskUserQuestion`; el usuario eligió **dejarlo como está**. Sin cambios de código.

#### Fix: columnas de texto libre en grids no guardaban el valor al desenfocar la celda
- Se confirmó la convención ya existente en el codebase: **toda** columna editable de un `DataGrid` (numérica o de texto) se parsea manualmente desde el `TextBox` del `EditingElement` dentro de `CellEditEnding` y se asigna al modelo — no alcanza con el binding automático de `DataGridTextColumn`.
- **`FacturasDetalle.GridItems`**: agregado el manejo de la columna **Concepto** (antes solo se parseaba Importe).
- **`FacturasDetalle.GridCobros`**: agregado el manejo de la columna **Descripción** (antes solo se parseaba Importe) — mismo problema, columna editable sin `IsReadOnly`.
- **`PedidosDetalle.GridTrasacciones`** (pestaña Cobros/Pagos): mismo problema encontrado y corregido en su columna **Descripción**.
- Revisadas todas las demás columnas de texto editables de la app (Traspasos/Correcciones/Precios/Inventarios y los grids `*General` de mantenimiento): o bien ya tenían su manejo manual (columna "Código"), o están marcadas `IsReadOnly="True"` a nivel de grid/columna — sin el mismo problema.

#### Fix: Tree1 no se actualizaba al **eliminar** un documento (Facturas/Pedidos/Traspasos/Correcciones)
- `BtnEliminar_Click` en los 4 módulos hacía una actualización incremental de la grilla en memoria (`lista.RemoveAt(idx)`) sin tocar el árbol de meses — si el documento eliminado era el último de su mes, el nodo del mes quedaba "fantasma" en el árbol.
- Cambiado a recarga completa (`CargarMeses(); CargarXxx();`) igual que ya hacían Nuevo/Editar, reseleccionando la fila más cercana por índice capturado antes de la recarga (no por referencia de objeto, que queda inválida tras el reload).
- **Hallazgo colateral**: `PedidosGeneral`/`TraspasosGeneral`/`CorreccionesGeneral.CargarMeses()` todavía tenían la versión antigua (sin preservar el mes activo, solo "seleccionar mes actual" a ciegas) — a diferencia de `FacturasGeneral`, que ya se había arreglado antes en esta misma sesión. Si se llamaba `CargarMeses()` desde `BtnEliminar_Click` sin ese fix, el árbol saltaba al mes calendario actual en vez de conservar la vista del usuario. Se hizo backport de la lógica de preservación (`tagPrevio`/`SeleccionarMes`) de `FacturasGeneral` a los 3 módulos.
- `RenumerarYTotales()` quedó sin usos en `FacturasGeneral` tras el cambio (su único llamador era el `BtnEliminar_Click` reemplazado) y se eliminó; en Pedidos/Traspasos/Correcciones el método sigue usándose en otros dos puntos cada uno, así que se conservó.
- **`ArticulosGeneral` investigado y descartado**: su `Tree1` es un árbol estático de catálogo Producto→Familia (`ConstruirArbolDeseado`, basado en `ProductosObj`/`FamiliasObj`), no depende de cuántos artículos activos tiene cada nodo — eliminar un artículo nunca dejaría un nodo "fantasma" ahí. No presenta el mismo problema; no se tocó.

#### Notas / pendientes de esta sesión
- **Sin build en el entorno cloud**: ninguno de estos cambios se compiló (sin SDK de .NET disponible en este contenedor); toda la verificación fue por revisión manual de diffs. Probar en máquina local, en especial el guardado diferencial de `transaccionesF`/Cobros y los refrescos de Tree1 al eliminar.
- **Propuesta discutida y NO implementada**: el usuario preguntó por una aplicación nueva, extensión de esta, para visualizar la empresa "en general" (agregado entre todas las sucursales — el `DashboardGeneral` actual filtra por `AppState.SucursalActiva`). Se propusieron dos caminos: (a) dashboard web liviano (ASP.NET Core/Blazor) leyendo la misma base SQL sin filtro de sucursal, accesible desde cualquier navegador/celular, con el costo de sumar una pieza nueva a hostear (no se distribuye con Velopack); o (b) otra app de escritorio WPF reutilizando `DataConsulta`/`SqlData` tal cual, más rápida de construir pero atada a una PC Windows. El usuario no había decidido el camino al momento de pedir esta actualización de `CONTEXT.md`.

### Sesión 2026-06-30 — Limpieza de `estadoU`, layout en 2 columnas, validaciones de guardado obligatorias, fixes de regresión en Traspasos/Correcciones y aviso de pestañas vinculadas (rama `master`)

> Sesión larga con varios pedidos encadenados del usuario, todos en `master` (commits `19c57bf`…`17543ca`). Se documenta de una sola vez.

#### Limpieza de `estadoU`, reordenamiento de columnas PDF/Excel, BtnConfigurarConexion e insertar sobre seleccionado (`19c57bf`)
- **Eliminada toda referencia a la columna `estadoU`** (tabla `usuarios`, ya borrada en SQL por el usuario): `UsuariosGeneral`, `UsuariosDetalle` (XAML y code-behind), `ConsolaMovimientos`, `LoginWindow`. Nota: esto reemplaza/corrige el flujo descrito en la sesión 2026-06-15 (`MarcarInactivo`/`estadoU="activo"/"inactivo"` al login/logout), que ya no aplica.
- **PDF y Excel de Precios e Inventarios**: columnas `Producto` y `Familia` movidas a la izquierda de `Descripción` (orden final: N°|Código|Producto|Familia|Descripción|Precio o Cantidad).
- **LoginWindow**: `BtnConfigurarConexion` ahora se deshabilita junto con el resto de controles mientras se procesa el inicio de sesión (antes quedaba activo y permitía abrir la configuración de conexión en medio del login).
- **`ArticulosGeneral.BtnNuevo_Click`**: el artículo recién creado se inserta encima (`lista.Insert(idx, nueva)`) del ítem que estaba seleccionado antes de abrir el diálogo, en vez de agregarse al final.

#### Validaciones de guardado obligatorias en formularios Detalle (`78dd99b`)
- **Código obligatorio**: todos los formularios `*Detalle` con código editable por el usuario (Articulos, Categorias, Empresas, Familias, Industrias, Productos, Regiones, Sucursales, Terceros) rechazan guardar en modo nuevo si `Box_Codigo` está vacío (`MessageBox` de advertencia + `return false`). No aplica a los formularios de documentos (Pedidos/Traspasos/Correcciones/Inventarios/Precios), cuyo código se autogenera y el campo está deshabilitado.
- **`ArticulosDetalle.Guardar()`**: además de la familia (ya exigida en sesión previa), ahora también exige **categoría** resuelta (`ResolverCategoriaId()` no vacío) en los tres modos (nuevo/insertar/editar).
- **`TraspasosDetalle`**: `GuardarNuevo()` y `GuardarEditar()` rechazan guardar si no se resolvió la sucursal destino/origen (`ResolverSucursalId()` vacío) — mensaje dinámico según `tipo` ("salida"→destino, "entrada"→origen).

#### ArticulosDetalle — esquinas redondeadas en todos los controles (`78dd99b`)
- Estilo `ModernInput`: `BorderThickness` de `"0,0,0,2"` (solo borde inferior) a `"1"` completo, con `ControlTemplate`/`CornerRadius="6"`.
- Estilo `SelectorBtn`: agregado `ControlTemplate` con `CornerRadius="6"` + triggers hover/pressed/disabled.
- Nuevos estilos `BtnBase`/`BtnPrimario`/`BtnSecundario` (mismo patrón que TercerosDetalle/SucursalesDetalle) aplicados a Cancelar/Guardar/Ver Movimientos.
- El TextBox de OBSERVACIONES se envolvió en un `Border CornerRadius="6"`.

#### Layout en 2 columnas: ArticulosDetalle, TercerosDetalle, SucursalesDetalle (`2adb468`)
- A pedido explícito del usuario ("lado a lado para evitar scroll"), el contenido de cada formulario pasó de `StackPanel` vertical a un `Grid` de 2 columnas (`*`, gap 12, `*`) dentro del `ScrollViewer`:
  - **ArticulosDetalle**: IDENTIFICACIÓN (col izq, fila 1) · CLASIFICACIÓN (col der, `RowSpan` filas 1-2) · DETALLES (col izq, fila 2) · OBSERVACIONES (`ColumnSpan` completo, fila 3).
  - **TercerosDetalle**: IDENTIFICACIÓN (col izq, fila 1) · CONTACTO (col der, `RowSpan` filas 1-2) · INFORMACIÓN (col izq, fila 2) · OBSERVACIONES (ancho completo, fila 3).
  - **SucursalesDetalle**: IDENTIFICACIÓN —con SIGNO reubicado ahí, junto a Tipo— (col izq, fila 1) · INFORMACIÓN (col der, `RowSpan` filas 1-2) · REGIÓN (col izq, fila 2) · OBSERVACIONES (ancho completo, fila 3).
  - Todas las cards usan `Margin="0"` (el espaciado lo dan las filas/columnas gap de 12px, no el margen propio de `CardBorder`).

#### Fix: `BtnInsertar_Click` insertaba debajo en vez de arriba (`cd1001e`)
- El pedido de "insertar sobre el seleccionado" de `19c57bf` solo se había aplicado a `BtnNuevo_Click`. `ArticulosGeneral.BtnInsertar_Click` seguía usando `lista.Insert(idx + 1, nueva)` (debajo de la fila de referencia). Cambiado a `lista.Insert(idx, nueva)` para que también quede arriba.

#### MaxLength en Box_Codigo + regresión de altura en Traspasos/Correcciones (`1f8605c`)
- **`ArticulosDetalle.Box_Codigo`**: `MaxLength="20"` (columna SQL `nvarchar(20)`; antes no había límite en la UI y se podía escribir más de lo que la base acepta).
- **Regresión detectada**: el commit `2427e3e` ("Habilita scroll táctil...") había envuelto `TraspasosDetalle` y `CorreccionesDetalle` en un `ScrollViewer` extra y cambiado la fila de `GridItems` de `Height="*"` a `Height="Auto"` — el grid dejó de ocupar el espacio disponible. Revertido: se quitó el `ScrollViewer` envolvente y se restauró `Height="*"` en ambos.

#### Segunda vuelta de la misma regresión: `MinHeight`/`MaxHeight` + Emisión/Edición faltante en Correcciones (`c9358e0`)
- El usuario notó que aún quedaban "franjas arriba y abajo" del grid — causa: el mismo commit `2427e3e` también había agregado `MinHeight="200" MaxHeight="450"` al `Grid` contenedor de `GridItems` (en ambos formularios), sin relación con el fix anterior. Eliminado en `TraspasosDetalle` y `CorreccionesDetalle`.
- **`CorreccionesDetalle` no mostraba Emisión/Edición** (sí las tenía `PedidosDetalle`): agregados `Box_Emision`/`Box_Edicion` (TextBlock readonly) en el encabezado, debajo de la fila de campos — mismo patrón visual que `PedidosDetalle`. Code-behind: se cargan desde `DocumentosCObj` en `CargarParaEditar` y se fijan a `DateTime.Now` en `CargarParaNuevo`.

#### Aviso y cierre automático de pestañas vinculadas al guardar/cerrar (`17543ca`)
- Problema: un formulario Detalle (p. ej. un Pedido) puede tener sub-pestañas abiertas vinculadas a él (buscador de artículos, selector de tercero/sucursal/región — todas con clave `"algo|{contexto}"` donde `contexto` es el `_tituloTab` del formulario padre). Guardar o cerrar el padre sin cerrar esas sub-pestañas las dejaba huérfanas.
- **`ConsolaMovimientos.ConfirmarCierrePestañasRelacionadas(contexto)`** (nuevo método público): busca en `TabContenido.Items` las pestañas cuya clave (`Tag`) termine en `|{contexto}`; si no hay ninguna, continúa sin interrumpir; si hay, muestra un `MessageBox` (`OKCancel`) listando sus títulos — **Aceptar**: las cierra todas y permite continuar; **Cancelar**: no hace nada y bloquea la acción.
- Aplicado como guard (`SinPestañasRelacionadas()`, helper de instancia que llama al método de arriba con `_tituloTab`) al inicio de `Guardar()`, `BtnCancelar_Click` e `IntentarCerrar()` en los 5 formularios que abren sub-pestañas: **PedidosDetalle, TraspasosDetalle, CorreccionesDetalle, ArticulosDetalle, SucursalesDetalle**.

#### Notas / pendientes de esta sesión
- **Sin build en el entorno cloud**: todos los cambios siguen patrones ya existentes en el codebase, pero no se compilaron (no hay SDK de .NET en este entorno). Verificar localmente antes de publicar, en especial el aviso de pestañas vinculadas (`ConfirmarCierrePestañasRelacionadas`) y los fixes de layout de Traspasos/Correcciones.
- **Regresión recurrente**: el commit `2427e3e` (sesión previa, scroll táctil) introdujo dos problemas distintos en el mismo archivo (`Height="Auto"` y luego `MinHeight`/`MaxHeight`) que requirieron dos rondas de fix en esta sesión porque no se habían notado juntos la primera vez. Si se vuelve a tocar scroll táctil en formularios con `DataGrid` interno, revisar que la fila del grid conserve `Height="*"` y que no se le agregue `MinHeight`/`MaxHeight` fijo al contenedor.

### Sesión 2026-06-23/29 — InventariosDetalle rediseñado, Stock/Disponible de PreciosDetalle a nivel de empresa, aviso de precio no vigente en Pedidos, recálculo masivo de importes, PDF de lista de precios como tabla, fix de duplicado al editar y control de acceso por tipo de usuario (rama `claude/blissful-mendel-vbilla`)

> Sesión larga con varios sub-temas encadenados, todos en la misma rama. Se documenta de una sola vez (commits `868b57b`…`fab244f`).

#### PreciosDetalle — refuerzo del fix de crash al refrescar
- El crash `CollectionView.Refresh()` durante transacción `AddNew`/`EditItem` ya se había corregido (sesión anterior) quitando `Grid1.Items.Refresh()` de `ActualizarTotales()`. Quedaba un riesgo análogo en `RefrescarGrid()`: reasignar `Grid1.ItemsSource` mientras una celda Precio seguía en edición (p. ej. al clickear el árbol o buscar sin confirmar la celda) podía disparar la misma excepción. Fix: `Grid1.CommitEdit(DataGridEditingUnit.Row, true)` al inicio de `RefrescarGrid()`, mismo patrón ya usado en `BtnGuardar_Click`/`IntentarCerrar`.

#### InventariosDetalle — mismo rediseño que PreciosDetalle: árbol + buscador + catálogo completo
- Reemplaza `GridItems` + "TOOLBAR ARTÍCULOS" por el patrón de `PreciosDetalle`/`ArticulosGeneral`: `Tree1` (productos/familias) + buscador + botón Actualizar + `Grid1` con el catálogo completo de artículos (no solo líneas ya registradas).
- Columna **Cantidad** reemplaza a Stock/Disponible, valor por defecto 0.
- Regla de persistencia (igual criterio que `PreciosDetalle.CrearLineas`): artículo sin registro previo en `inventarios` solo se guarda si la cantidad editada es mayor a cero; artículo con registro previo siempre se actualiza, incluso a cantidad cero (queda `normal`, nunca se oculta desde aquí).
- Se limpiaron referencias a la columna `indice` de `inventarios` (ya eliminada en SQL) en `InventariosDetalle`, `InventariosGeneral` y `AppLoader.ConectarBases`.
- **Columna Categoría** agregada en `Grid1`, entre Código y Descripción (`articulos.categoria` → `categorias.descripcion`, mismo patrón que `ObtenerDescripcionArticulo`).
- **Fix de buscador angosto**: el `Border` de `TxtBuscar` tenía `MaxWidth="320"` + `HorizontalAlignment="Left"` heredados por error al adaptar el patrón de `ArticulosGeneral` (que no los tiene), en **PreciosDetalle e InventariosDetalle**. Se reemplazó por `Margin` para igualar el comportamiento original (estirado al ancho disponible).

#### PreciosDetalle — Stock/Disponible a nivel de empresa (no de sucursal)
- Las listas de precios se manejan por empresa, pero Stock/Disponible se calculaban solo con datos de la sucursal activa.
- Primer paso: recorrer todas las sucursales de la empresa (reutilizando la secuencia de recarga de `Configuracion.xaml.cs`) acumulando totales por artículo; se agregó un panel lateral **"Sucursales"** con el desglose (descripción, disponible, stock) del artículo seleccionado.
- Segundo paso (performance): ese recorrido recargaba la caché completa N veces (~8 consultas cada una). Se reemplazó por **5 consultas SQL fijas a nivel de empresa** que reproducen la fórmula de `StockCalculator.ContarStock`/`ContarStock2`, acumulando en memoria por sucursal+artículo — sin tocar `AppState` ni las cachés de `SqlData`. Reduce drásticamente el tiempo de carga.

#### TraspasosGeneral — "Entregar todos" habilitado en cualquier sucursal
- Se quitó el gating por tipo de sucursal `'central'` (visibilidad del botón y defensa interna del handler) y se eliminó `EsSucursalCentral()` (sin más usos).

#### PedidosDetalle — aviso cuando el precio aplicado no es de la lista más reciente
- `ObtenerPrecioArticulo` ahora expone la fecha del `documentoL` usado; nuevo `ObtenerFechaMaximaListaPrecios` calcula la fecha de la lista de precios vigente más reciente aplicable al documento.
- Nuevo helper `ObtenerPreciosConAviso`: compara ambas fechas por artículo; si alguno no tiene precio en la lista más nueva, junta los casos en un solo aviso y pregunta si aplicar el precio anterior encontrado — si la respuesta es "No", el artículo queda con precio 0.
- Conectado en los 4 puntos donde se fijaba el precio automáticamente: edición de Código en la grilla, importar artículos, buscar artículo y "Actualizar precios".

#### Configuración — recálculo masivo de importes de pedidos automáticos
- Nuevo botón **"Recalcular precios"** junto a "Regenerar códigos".
- Nueva clase `PedidosPrecioActualizador.cs`: recorre toda la tabla `pedidos` (solo tipo `automatico`) y recalcula `importe = precio_vigente * cantidad` usando la lista de precios (`documentosL`/`precios`) con fecha más reciente que no supere la fecha del `documentoP` de cada pedido (mismo criterio que `ObtenerPrecioArticulo`). Los pedidos tipo `manual` no se tocan.
- Ajuste posterior: los pedidos **sin ninguna lista de precios aplicable** ahora quedan con `importe = 0` (`OUTER APPLY` + `ISNULL`) en vez de no tocarse — mismo criterio que cuando el usuario rechaza aplicar un precio anterior en `PedidosDetalle`.

#### PreciosGeneral — PDF de lista de precios como tabla real
- Se quitó la columna **"Valor"** de `Grid1`.
- `GenerarPdfListaPrecios` reescrito con funciones locales (`DibujarFilaDatos`/`DibujarEncabezadoColumnas`/`DibujarBandaGrupo`/`NuevaPagina`/`AsegurarEspacio`): genera una tabla real con bordes, bandas celestes por grupo, columnas N°/Código, y título con fecha+hora.

#### PreciosDetalle — fix bug "editar y guardar crea una lista nueva"
- Causa: `Guardar()`/`CargarUserform()` dependían del flag global `AppState.EventoFormularioL` (compartido con el módulo de Terceros, sin relación) — podía pisarse entre abrir "Editar" y clickear "Guardar", haciendo que corriera `GuardarNuevo()` en vez de `GuardarEditar()` y creando un documento duplicado.
- Fix: se reemplazó por el campo `_idEditar` de instancia (`!string.IsNullOrEmpty(_idEditar)`), inmune a interferencia de otros formularios abiertos en paralelo.

#### Formato de Precio unificado + eliminación de "Valor total" + control de acceso por tipo de usuario
- **PreciosDetalle `Grid1`**: columna Precio usa el mismo `StringFormat` que `PedidosDetalle.GridItems` (`#\,##0.##`, recorta decimales en cero — antes `#\,##0.00`, forzaba siempre 2 decimales).
- **PreciosDetalle**: se eliminó por completo el cálculo y la tarjeta de **"Valor total"** (`TxtValorTotal` y su lógica en `ActualizarTotales`) — a pedido explícito del usuario, que ya no lo necesita.
- **ConsolaMovimientos**: usuarios de tipo `"user"` (`!AppState.EsAdmin`) ya no ven en el panel lateral las pestañas Regiones, Precios, Sucursales y Empresas (Usuarios ya estaba oculta por defecto).
- **Configuración**: usuarios de tipo `"user"` ya no ven los botones Regenerar códigos, Recalcular precios y Sincronizar AppSheets (antes este último no tenía ningún gating por rol).

#### Investigado y descartado: error MC3010 reportado por el usuario en `PedidosDetalle.xaml:377`
- El usuario reportó en su Visual Studio local: `MC3010 — el valor de la propiedad Name '  ' no es válido` en la línea 377. Se revisó exhaustivamente el archivo de este repo (todo atributo `Name`/`x:Name`, `Setter Property="Name"`, atributos partidos en dos líneas, caracteres no-ASCII/invisibles, finales de línea) sin encontrar ningún `Name` vacío o inválido — la línea 377 actual (`<DataGrid x:Name="GridItems" ...>`) es válida. El usuario confirmó luego que ya se había resuelto de su lado (probablemente Error List desactualizado en VS); no se aplicó ningún cambio de código para esto.

#### Notas / pendientes de esta sesión
- **Sin build en el entorno cloud**: ninguno de estos cambios se compiló en este entorno (sin SDK de .NET disponible); verificar en máquina local antes de publicar, en especial el recálculo masivo de importes (`PedidosPrecioActualizador`) y las consultas agregadas de Stock/Disponible por empresa en `PreciosDetalle`.
- **Firma de commits "Unverified"**: recordatorio recurrente del stop-hook por falta de firma GPG/SSH en este contenedor (el email/nombre de autor ya están correctos, `noreply@anthropic.com`/`Claude`; no hay clave de firma disponible). No se reescribió historia de `master` para "solucionarlo" — requiere autorización explícita cada vez por tratarse de un rewrite de historia ya publicada.

### Sesión 2026-06-23 — PreciosDetalle: árbol de familias + buscador + catálogo completo; exclusión de precios en cero; columna `indice` eliminada de `precios` (rama `claude/stoic-einstein-0wqg94`)

> Contexto: a diferencia del análisis de la sesión anterior (`claude/wizardly-faraday-bdvna8`, ver abajo) — que evaluó eliminar `PreciosDetalle` por completo y mover todo a edición inline en `GridPrecios`, y concluyó que NO era viable — esta sesión tomó un camino distinto: **conservar** `PreciosDetalle` como pantalla en pestaña, pero rediseñar su contenido para que liste **todo el catálogo de artículos** (no solo líneas ya agregadas a la lista de precios) con precio editable por fila, igual que `ArticulosGeneral`.

#### Rediseño de `PreciosDetalle`: de `GridItems`+toolbar a Tree1+buscador+Grid1
- Se **eliminó** `GridItems` y la "TOOLBAR ARTÍCULOS" (Importar artículos, Buscar artículo, Insertar, Nueva línea, Eliminar línea) — los métodos `BtnBuscarArticulos_Click`, `BtnBuscarArticulo_Click`, `BtnNuevaLinea_Click`, `BtnEliminarLinea_Click`, `BtnInsertar_Click` se borraron por completo.
- En su lugar, mismo patrón visual que `ArticulosGeneral`: sidebar con `Tree1` (árbol Producto→Familia, `Tag` = `"todos"`/`"producto:{id}"`/`"familia:{id}"`), buscador (`TxtBuscar`+`BtnBuscar`) y `Grid1`, que ahora carga **todos** los artículos de `Sql.ArticulosObj` (antes solo mostraba líneas ya creadas en `precios`).
- **Simplificación deliberada vs. `ArticulosGeneral`**: `CargarArbol()` reconstruye el árbol desde cero cada vez (sin la reconciliación incremental por `Tag`/`NodoDeseado` que tiene `ArticulosGeneral.BtnActualizar_Click`) — la estructura de familias rara vez cambia en medio de una edición de lista de precios, y evita ~80 líneas de infraestructura no pedida.
- Nuevo botón **"Actualizar"** (reemplaza la lógica de la toolbar eliminada): recarga el catálogo desde SQL (`AppState.ActualizarProductos()`) conservando los precios ya editados en memoria por artículo (no se pierde edición en curso al refrescar).
- Columnas de `Grid1`: Código, Descripción (read-only), Disponible, Stock (read-only, vía `StockCalculator.ContarStock`/`ContarStock2`), Precio (editable, **0.00 por defecto** para todo artículo sin precio registrado en esta lista). Se quitó la columna "Línea" (no pedida; sin sentido ahora que `Grid1` no son líneas de documento sino el catálogo completo).
- Modelo `PrecioItemFila`: `PrecioId` (vacío = artículo sin registro en `precios` para este documento), `ArticuloId`, `Codigo`, `Descripcion`, `Stock`, `Disponible`, `Precio`.

#### Reglas de persistencia nuevas (`CrearLineas`, reemplaza la lógica diferencial anterior)
- Artículo **sin** registro previo en `precios`: solo se inserta una fila nueva si el precio editado es **mayor a cero**. Si quedó en 0.00 (sin editar), no genera fila.
- Artículo **con** registro previo: se actualiza siempre, **incluso si el precio se edita a 0** — el registro permanece `estadof = "normal"` (nunca se oculta/elimina); solo deja de **mostrarse** en el PDF y en `PedidosDetalle`.
- Esto reemplaza el guardado diferencial anterior (que comparaba `_items` contra `_itemsOrig` para detectar altas/bajas de líneas) — ya no aplica el concepto de "línea eliminada" en `precios`, porque ahora todo el catálogo está siempre presente en pantalla.

#### Exclusión de precios en cero (PDF + GridPrecios)
- **`PreciosGeneral.GenerarPdfListaPrecios`**: `if (precio <= 0) continue;` antes de agregar la línea — un artículo con precio 0 en una lista no aparece en el PDF generado.
- **`PedidosDetalle.CargarStockYPrecios`** (`GridPrecios`, historial de precios de un artículo): mismo guard — un precio en 0 no aparece en el historial mostrado al usuario.

#### Columna `indice` eliminada de `precios`
- A pedido explícito del usuario ("ya no la necesitaré"), se quitó **toda referencia en código** a `precios.indice`: `AppLoader.ConectarProductos` (ya no ordena por `indice`, solo por `documentoL`), `PreciosGeneral.BtnEliminar_Click` y `PreciosDetalle.GuardarNuevo`/`GuardarEditar` (`OrdenarData` sin el segundo criterio `indice`). `PreciosDetalle.CrearLineas` ya no reserva ni asigna índices (no usa `IndicesNoNormales` en esta tabla).
- **Pendiente del lado del usuario**: ejecutar manualmente en SQL Server (Claude no ejecuta DDL contra la base en vivo):
  ```sql
  ALTER TABLE precios DROP COLUMN indice;
  ```

#### Bug post-deploy corregido en la misma sesión: crash al editar precio
- El usuario reportó en producción: `System.InvalidOperationException: No se permite 'Refresh' durante una transacción AddNew o EditItem` en `ActualizarTotales()` línea 288, al editar una celda de Precio.
- Causa: `Grid1.Items.Refresh()` se llamaba (via `Dispatcher.BeginInvoke` disparado desde `Grid1_CellEditEnding`) mientras la fila del DataGrid seguía en transacción `EditItem` (WPF no libera esa transacción solo porque una celda terminó de editarse; sigue abierta a nivel de fila hasta que se navega a otra fila o se hace `CommitEdit` de fila).
- Fix: se **eliminó** la llamada a `Grid1.Items.Refresh()` en `ActualizarTotales()` — no hacía falta: la celda Precio ya se refresca sola por su binding, y `RefrescarGrid()` (el otro llamador de `ActualizarTotales`) ya reemplaza `Grid1.ItemsSource` por completo, lo cual refresca la grilla igual.

#### Notas / pendientes de esta sesión
- **Sin build en el entorno cloud**: se verificó manualmente balance de llaves/paréntesis (script Python) en los `.cs` tocados y buena formación XML en el `.xaml`, pero no hubo compilación real — el crash de `Items.Refresh()` reportado por el usuario es evidencia de que esta verificación manual no sustituye una prueba en ejecución real. Verificar en la app real (no solo sintaxis) antes de futuros cambios similares.
- Workflow de git usado (como en sesiones anteriores): commit en `claude/stoic-einstein-0wqg94` → checkout `master` → pull → cherry-pick → push `master` → checkout de vuelta. Dos rondas en esta sesión: el rediseño completo (`ffc6c1c`/`d31993e`) y el fix del crash (`642c428`/`4ffe5e4`).

### Sesión 2026-06-19/20 — PreciosGeneral: columna Precio + Hora y CRUD inline en GridPrecios (implementado y luego revertido a pedido del usuario); análisis de eliminar PreciosDetalle (rama `claude/wizardly-faraday-bdvna8`)

> Contexto: continuación de un reordenamiento de layout y PDF de lista de precios de `PreciosGeneral` ya cerrado en una sesión previa (commit `17e9a0b`, en `master`). Esta sesión agregó columnas y CRUD inline en `GridPrecios`, pero el usuario revirtió ese cambio al final — queda documentado en detalle para no repetir el mismo intento sin releer esto primero.

#### Implementado y luego revertido: columna Precio (Grid1), columna Hora (GridPrecios) y menú contextual CRUD
- **Grid1**: nueva columna "Precio" a la derecha de "Estado"; mostraba el último precio vigente del artículo en la región activa (`ObtenerPrecioVigente(articuloId, regionId)`, 0 si no había registro).
- **GridPrecios**: nueva columna "Hora" a la derecha de "Fecha" (`HoraStr`, formato `HH:mm:ss`, separada de `FechaStr`).
- **GridPrecios**: `DataGrid.ContextMenu` (clic derecho) con "Nuevo Precio"/"Editar Precio"/"Eliminar Precio" — primer uso de `ContextMenu`/`MenuItem` sobre un DataGrid en todo el proyecto. Wiring nuevo: `GridPrecios_PreviewMouseRightButtonDown` selecciona la fila bajo el cursor antes de abrir el menú (mismo patrón `VisualTreeHelper.GetParent` que ya usaba `Tree1_PreviewMouseLeftButtonDown`).
- Se quitó el botón `BtnEditarPrecio` de la barra inferior (a pedido del usuario); el método `BtnEditarPrecio_Click` se conservó porque seguía siendo invocado por doble clic, Enter y el ítem de menú contextual.
- `ConfigurarModo()`: para no-admin, además de ocultar Nuevo/Eliminar, también ponía `GridPrecios.ContextMenu = null`.
- **Commit `e8660f7`** (pusheado a `master`) → **revertido en `74408b9`** (también pusheado a `master`) a pedido explícito del usuario. Se usó `git revert` (no reset/force-push) porque el commit ya estaba en `master` compartido. Estado final: `master` quedó byte-a-byte igual que `17e9a0b` en `PreciosGeneral.xaml`/`.xaml.cs` — es decir, **sin** columna Precio en Grid1, **sin** columna Hora ni menú contextual en GridPrecios, y **con** el botón `BtnEditarPrecio` de vuelta.

#### Análisis (NO implementado): eliminar `PreciosDetalle` y reemplazar por edición inline real en `GridPrecios`
> El usuario planteó esto con `/critico` (modo crítico adversarial), antes de revertir el punto anterior. Veredicto entregado: **No aceptable** tal como estaba planteado. Se deja documentado para no repetir la misma investigación si se retoma la idea más adelante.
- **Precedente arquitectónico**: hay 15 clases `*Detalle` (Articulos, Categorias, Correcciones, Empresas, Familias, Industrias, Inventarios, Pedidos, Productos, Regiones, Sucursales, Terceros, Traspasos, Usuarios, Precios) — todas siguen el mismo patrón formulario-en-pestaña (`ConsolaMovimientos.AbrirPestaña`/`IntentarCerrar`). Confirmado por grep: **cero** DataGrids con `IsReadOnly="False"` o edición inline real en todo el proyecto. Eliminar `PreciosDetalle` para hacer edición inline solo ahí convertiría a Precios en la única pantalla de 16 con un patrón de edición distinto.
- **Riesgo concreto de pérdida de datos**: `GridPrecios.ItemsSource` se reemplaza completo al cambiar de artículo, cambiar de región (`CmbRegion_SelectionChanged`) o clickear "Actualizar". Hoy es inofensivo porque la grilla es de solo lectura, pero destruiría sin aviso cualquier edición de celda sin confirmar si la grilla fuera editable.
- **`PreciosDetalle` no es trivial de replicar en una celda de grilla**: genera código correlativo por región (`SiguienteNumeroDoc`) solo al guardar con éxito (para no quemar números si se cancela); parsea decimales multi-cultura; restringe input con `PreviewTextInput`; formatea precio a 2 decimales en `LostFocus`; persiste 9 campos al crear vs. 5 al editar. Reconstruir todo eso en un DataGrid no tiene ningún precedente en el codebase.
- **Modelo de fila incompleto para región**: `PrecioHistFila.Region` es un string descriptivo, no guarda el `regionId` (UUID). Hoy no importa porque la región del precio viene fija desde `CmbRegion` (ni siquiera se edita dentro de `PreciosDetalle`, que la muestra deshabilitada) — dato a favor de la propuesta, pero confirma que la región nunca sería editable por fila si se hiciera inline.
- El usuario, después de leer el veredicto, optó por revertir el cambio de columnas/menú contextual (ver arriba) en vez de seguir con la edición inline. Quedó esbozada (sin implementar ni confirmar) la idea de "fila en blanco al final (`CanUserAddRows`) + Fecha vía `DatePicker` inline + guardado automático al desenfocar, sin botón Guardar"; preguntas abiertas si se retoma: UX exacta de "Agregar" (fila en blanco vs. botón que inserta fila editable) y si "Hora" también sería editable inline.

### Sesión 2026-06-18 (parte 3) — ConsolaMovimientos: resaltado de Ítems de navegación, login limpia/enfoca contraseña al fallar; TimePicker probado y revertido (rama `claude/quirky-bardeen-al3g9g`)

#### Resaltado de Ítems de navegación en `ConsolaMovimientos` (tema claro)
- El tema oscuro no se tocó en ningún momento de esta sesión (confirmado por el usuario: "en tema oscuro esta bien").
- Se agregaron tres claves de tema **dedicadas** al sidebar de navegación (en vez de reusar `ThemeTabSelBg`/`ThemeTabHoverBg`, que también pinta tabs en otras partes de `ConsolaMovimientos.xaml` y `App.xaml` — para no generar regresiones visuales en esos otros usos):
  - `ThemeNavActivoBg`: fondo de la pestaña de navegación **activa/seleccionada**. Tema claro `#4A90E2` (azul), tema oscuro `#2A4A7A`.
  - `ThemeNavHoverBg`: fondo al pasar el puntero sobre una pestaña de navegación (no activa). Tema claro `#CFE4FF` (azul claro — el mismo valor que ya tenía antes de esta sesión, no cambió en la práctica), tema oscuro `#2D3152`.
  - `ThemeNavActivoFg`: color de letra de la pestaña activa. Tema claro `#000000` (negro), tema oscuro `#E8EAED`.
  - Archivos: `WpfAppVba/Themes/LightTheme.xaml`, `WpfAppVba/Themes/DarkTheme.xaml`.
- **`ConsolaMovimientos.xaml`** (estilo `NavBtn`, `ControlTemplate.Triggers`): el trigger `IsMouseOver` pinta `Borde.Background` con `ThemeNavHoverBg`; el trigger `IsPressed`/estado activo pinta con `ThemeNavActivoBg`.
- **`ConsolaMovimientos.xaml.cs` (`MarcarActivo(Button btn)`)**: al activar una pestaña, se le aplica `SetResourceReference(BackgroundProperty, "ThemeNavActivoBg")` y `SetResourceReference(ForegroundProperty, "ThemeNavActivoFg")` (en vez de un color fijo), para que reaccione en vivo a un cambio de tema. Al desactivar la pestaña previa, vuelve a fondo transparente y a `ThemeTextoSec`.
- **Iteración con varios cambios de opinión del usuario durante la sesión** (se documenta para no repetir el mismo error): se probó azul claro para el activo, luego "mismo color que el hover", luego "plomo oscuro y que el hover de la pestaña activa no cambie" (en este paso se cometió un error: al implementarlo se modificó también `ThemeNavHoverBg` general, lo cual rompió el hover de las pestañas NO activas, que el usuario dijo que "estaba bien como estaba" — quedó claro que "activo" y "hover" deben tratarse como estados independientes salvo que se pida unificarlos explícitamente). Estado final pedido y aplicado: **activo = fondo azul `#4A90E2` con letras negras; hover = azul claro `#CFE4FF` sin cambios**.

#### Login: limpia y enfoca el campo de contraseña si las credenciales son incorrectas
- **`LoginWindow.xaml.cs` (`BtnIngresar_Click`, rama de credenciales incorrectas)**: además de mostrar el mensaje de error, ahora limpia `TxtContrasena` y `TxtContrasenaVisible` y enfoca el que esté visible (`TxtContrasena` si está oculto el de texto plano, si no `TxtContrasenaVisible`), para que el usuario pueda volver a escribir sin borrar manualmente.

#### Probado y revertido: TimePicker de Extended WPF Toolkit
- A pedido del usuario se implementó `xctk:TimePicker` (Extended WPF Toolkit / Xceed, paquete `Extended.Wpf.Toolkit`) en los 6 formularios que registran hora (`TraspasosDetalle`, `SucursalesDetalle`, `PreciosDetalle`, `PedidosDetalle`, `InventariosDetalle`, `CorreccionesDetalle`).
- El usuario pidió revertir todo el cambio (`git reset --hard 5b4218abf88d15146dac663d8dc709e4e21f09ce` + force-push sobre `claude/quirky-bardeen-al3g9g`). **Estado actual: NO hay TimePicker ni dependencia de Extended Wpf Toolkit en el repo** (confirmado sin referencias a `xctk` en ningún archivo); los 6 formularios siguen usando `Box_Hora` como `TextBox` con `TimeSpan.TryParse`. Se deja documentado para que una futura sesión sepa que ya se intentó y se descartó (no por un problema técnico, sino por decisión del usuario en ese momento).

#### Notas / pendientes de esta sesión
- Sin build en el entorno cloud (no hay SDK de .NET disponible); verificar localmente antes de dar por cerrado.

### Sesión 2026-06-18 (parte 2) — Fix de paquetes delta, bloqueo por actualización pendiente y auditoría de seguridad (login + contraseñas) (rama `master`)

> Esta sesión trabajó directamente en `master` (continuación de una sesión previa con autorización ya otorgada). Cuatro commits: `863f0c8`, `ce27f98`, `021c623`, `4518260`, `6027c12`.

#### Fix de paquetes delta en releases (Velopack)
- **Problema**: cada corrida de GitHub Actions arranca de un checkout limpio, así que `vpk pack` nunca encontraba el `.nupkg` de la versión previa y solo generaba el paquete **full** (descarga completa aunque no hubiera cambios).
- **`.github/workflows/release.yml`**: se agrega `vpk download github` (trae la última release publicada) **antes** de `vpk pack`, para que Velopack genere correctamente el delta.
- **`ActualizadorApp.cs` (`TamañoDescargaMB`)**: antes siempre leía `TargetFullRelease.Size` (mostraba el peso de la app entera en la barra de progreso aunque se estuviera descargando un delta mucho más chico). Ahora suma `DeltasToTarget` cuando hay deltas disponibles, y solo cae al tamaño full si no los hay.

#### Bloqueo de la app mientras hay una actualización pendiente
- **Login (`LoginWindow`)**: reutiliza el patrón de actualización de `ConsolaMovimientos`. Al cargar, si `_actualizador.HayActualizacionAsync()` detecta versión nueva, se oculta el formulario de login y se muestra un bloque de actualización obligatoria (descargar + reiniciar) hasta que el usuario actualice — no se puede iniciar sesión con una versión vieja.
- **`AppState.VersionPendiente`** (nuevo, `string?`): bandera global que se setea en `ConsolaMovimientos.BuscarActualizacionesAsync()` cuando hay una versión nueva disponible.
- **`FuncionesComunes`**: nuevo `HayActualizacionPendienteOAvisa(owner)`, integrado en `VerificarConexionParaGuardar` **y** `VerificarConexionParaActualizar` (decisión explícita del usuario: bloquear también los botones "Actualizar" de refresco de cada grid, no solo Guardar/Eliminar). Como estos dos guards ya se usaban en los 16 módulos + Configuración + CambiarContraseña, el bloqueo quedó centralizado sin tocar cada panel. La navegación entre secciones/pestañas sigue funcionando con normalidad; solo se bloquean escritura y refresco de datos.

#### Auditoría de seguridad + hashing de contraseñas + login sin volcado completo de `usuarios`
> A pedido del usuario se hizo una auditoría de seguridad de la app. Hallazgos principales: contraseñas en texto plano, descarga de toda la tabla `usuarios` (con contraseñas de todas las cuentas) antes de autenticar, `TrustServerCertificate=True` en `DatabaseConnection` (pendiente, no se tocó), sin protección anti fuerza bruta (pendiente), releases sin firmar (pendiente). Se implementaron los dos primeros puntos (prioridad alta):

- **`PasswordHasher.cs`** (nuevo, `WpfAppVba.Data`): `Hashear(contrasena)` / `Verificar(contrasena, valorAlmacenado)` con PBKDF2-SHA256, 100.000 iteraciones, salt aleatorio de 16 bytes, formato almacenado `"{iteraciones}.{saltBase64}.{hashBase64}"`. Comparación en tiempo constante (`CryptographicOperations.FixedTimeEquals`). `EsHash(valor)` distingue un hash de una contraseña vieja en texto plano.
- **`AppLoader.ValidarLogin(cuenta, contrasena)`** (nuevo): consulta puntual y parametrizada (`SELECT id, llave FROM usuarios WHERE cuenta = @cuenta AND estadof = 'normal'`) — ya **no** se compara en el cliente contra el volcado completo de la tabla `usuarios` (que traía las contraseñas de TODAS las cuentas a memoria antes de que el usuario se autentique). Si la contraseña almacenada es un hash, verifica con `PasswordHasher.Verificar`; si es texto plano y coincide, **migra automáticamente** (la re-hashea con un `UPDATE` directo) y deja loguear.
- **`AppLoader.ConectarUsuarios()`**: ya no se llama antes de loguear. Ahora se llama **después** de `ValidarLogin` exitoso (su doc-comment se actualizó para reflejar esto). Antes de loguear, `LoginWindow.ConectarBaseDatosAsync()` solo hace una sonda de conectividad (`DatabaseConnection.ConexionEstaActiva()`, un `SELECT 1`) en vez de cargar `usuarios`/`empresas` completas.
- **`LoginWindow.BtnIngresar_Click`**: el loop que recorría `Sql.UsuariosObj` comparando `cuenta`/`llave` en texto plano se reemplazó por `AppLoader.ValidarLogin(...)`; si autentica, recién ahí se llama `AppLoader.ConectarUsuarios()` para poblar la caché que usan las pantallas de administración de usuarios/empresas.
- **`CambiarContrasena.BtnGuardar_Click`**: la verificación de la contraseña actual usa `PasswordHasher.Verificar` (con fallback a comparación en texto plano para cuentas aún no migradas); la nueva contraseña se guarda siempre hasheada (`PasswordHasher.Hashear`).
- **Pendiente de la auditoría (NO implementado esta sesión)**: `TrustServerCertificate=True` en `DatabaseConnection.cs` (riesgo MITM), protección anti fuerza bruta en el login, firma de releases de Velopack, y mover los privilegios del usuario de SQL Server de la app a un rol mínimo (solo `SELECT/INSERT/UPDATE`, sin `DELETE` — la app nunca hace `DELETE FROM`, ver borrado lógico) y restringir el acceso remoto por IP/VPN en el panel del hosting (el usuario usa SmarterASP.NET, hosting compartido — no hay firewall de servidor propio, hay que buscar la opción de "Remote MSSQL Access"/IPs permitidas en su panel).

#### Notas / pendientes de esta sesión
- **Sin build en el entorno cloud**: ninguno de estos cambios se compiló (no hay SDK de .NET en este entorno). Antes del próximo release, verificar en una máquina local: compilación, login con una cuenta existente (su contraseña en texto plano debe migrarse sola al hash en el primer login exitoso) y el flujo de "Cambiar Contraseña".

### Sesión 2026-06-18 — Selección/edición de grids, filtros multi-empresa en cascada, login con carga diferida y reubicación de Categorías (rama `claude/optimistic-dirac-5arp05`)

> Contexto: el usuario **eliminó las columnas `empresa` de `articulos`, `familias` y `precios`** en SQL Server; el filtrado de esas tablas por empresa pasa a ser por **cascada de relaciones** (no por columna propia). Además, varios ajustes de UX en grids editables, login y cierre de la consola.

#### ArticulosGeneral — grid de artículos
- **Color de selección propio**: nuevo brush `ThemeFilaSelGrid` (claro `#D7E5FF`, oscuro `#33426B`) en ambos temas; `Grid1.RowStyle` lo aplica con `Trigger IsSelected=True` (antes dependía del resaltado del sistema, que cambiaba de tono al perder el foco). El verde `ThemeFilaSeleccionada` (checkbox marcado en modo importar) gana sobre el azul.
- **Alto de fila fijo + sin negrita**: `Setter Height="34"` en el estilo de fila y se **quitó** `FontWeight=SemiBold` del trigger de `Seleccionado`. Ese cambio de FontWeight forzaba a WPF a re-medir la fila y, con la virtualización, cambiaba el alto de las filas al seleccionar/marcar (visible en modo importar: scroll + seleccionar + doble clic, más notorio en modo oscuro).
- **Nueva columna "Categoría"** entre Código y Descripción Completa (`ArticuloFila.Categoria` ← `articulos.Categoria` → `Categorias.descripcion`). El buscador (`TxtBuscar`) ahora también coincide y **filtra por la categoría**.
- **Actualizar incremental** (`BtnActualizar_Click`): recarga la caché y refresca **sin reconstruir todo**, preservando selección/foco/scroll y la expansión del árbol. `Tree1` se reconcilia por `Tag` (alta/baja/edición de nodos); `Grid1` hace diff por `Id` (actualiza en sitio, agrega, quita) reusando instancias. Helpers nuevos: `ConstruirListaArticulos`, `ConstruirArbolDeseado`/`CrearNodo`/`ReconciliarNodos`/`RestaurarSeleccionArbol`/`BuscarNodoPorTag`, `RefrescarGridIncremental`/`RestaurarSeleccionGrid`. Flag `_suspenderEventosArbol` para que la reconciliación del árbol no dispare recargas.
- **Se eliminó el nodo "Sin Clasificar"** del árbol y su filtro (todo artículo debe tener familia).

#### Edición de celdas — seleccionar todo + solo números
- **`GridFocusHelper.SeleccionarTodoEnEdicion(FrameworkElement?)`**: al entrar a editar, selecciona todo el texto; busca el `TextBox` dentro del `EditingElement` (sirve para `DataGridTextColumn` **y** `DataGridTemplateColumn`) y re-despacha `SelectAll` en `DispatcherPriority.Input` para que el clic del ratón no deshaga la selección. Aplicado en los `PreparingCellForEdit` de Inventarios, Correcciones, Traspasos y Pedidos (líneas + entregas).
- **Columna Cantidad unificada**: en `InventariosDetalle` y `CorreccionesDetalle` pasó de `DataGridTemplateColumn` a `DataGridTextColumn` con `CeldaNumero`/`CeldaNumeroEditar` (mismo aspecto que Pedidos/Traspasos).
- **`FuncionesComunes.RestringirACantidad(TextBox)`**: bloquea letras/símbolos al escribir **y al pegar** (`DataObject` pasting), permitiendo solo dígitos y separadores `,`/`.` (sin depender de la cultura). Aplicado a Cantidad en los 4 formularios y, además, a **Precio, Importe y Contable** en `PedidosDetalle` (GridItems).

#### Reubicación de Categorías a la derecha del grid
- En `InventariosDetalle`, `CorreccionesDetalle` y `TraspasosDetalle`: el panel **Categorías** se movió a una columna (220px) a la derecha de `GridItems` (se quitó de la barra de totales inferior, que conserva Total unidades / Diferentes).
- En `PedidosDetalle`: **Categorías** a la derecha de `GridItems` (pestaña Artículos, `GridCategorias`) y de `GridEntregas` (pestaña Entregas, `GridCategoriasE`); se quitaron ambas tarjetas de la barra de totales. La lógica `CargarTotalesCategoria*` ya existía; solo se reubicó el XAML.

#### ArticulosDetalle / FamiliasDetalle — validaciones y estado
- **Artículo: familia obligatoria y existente** — `ArticulosDetalle.Guardar()` no guarda si `ResolverFamiliaId()` es vacío (campo vacío o código inexistente). Cubre nuevo/insertar/editar.
- **Familia: producto obligatorio y existente** — `FamiliasDetalle.Guardar()` no guarda si `ResolverProductoId()` es vacío.
- **Estado por defecto** — `ArticulosDetalle.GuardarInsertar` ahora fija `estado="mostrar"` (igual que `GuardarNuevo`).
- **ID interno oculto** — el badge "ID" de `ArticulosDetalle` queda `Visibility="Collapsed"`; `Box_Identificador` se conserva (oculto) porque el code-behind guarda el GUID ahí.

#### Multi-empresa — filtros de carga de caché (sin columna `empresa` en articulos/familias/precios)
- `AppLoader.ConectarProductos`: productos, industrias, terceros, regiones, sucursales y categorías filtran **directo** por `empresa = empresa activa`. **familias** filtran por `producto IN (productos de la empresa)`, **artículos** por `familia IN (familias de la empresa)` (cascada de 2 niveles), y **precios** por `region IN (regiones de la empresa)` — sin tocar la columna `empresa` de esas tablas hijas.
- **empresa por defecto al crear** (`GuardarNuevo`): Productos, Industrias, Terceros, Regiones, Sucursales y Categorías establecen `empresa = AppState.EmpresaActiva`.
- **`AppsheetsSync`**: dejó de usar `articulos.empresa`; filtra los artículos de la empresa por cascada `familia → producto → empresa` (en INSERT y en UPDATE de marcado eliminado).

#### Login (LoginWindow) — carga diferida, robustez y UX
- **`AppLoader.ConectarUsuarios()`** (nuevo): antes de iniciar sesión solo carga `usuarios` (+`empresas`), lo mínimo para validar el login. El resto de catálogos se carga **tras loguear** con `ConectarProductos` (ya filtrado por empresa). `AppState.RegionActiva` se calcula **después** de `ConectarProductos` (cuando `sucursales` ya está en caché).
- **Bloqueo total de controles**: `HabilitarControles(bool)` habilita/deshabilita juntos usuario, contraseña (`PasswordBox`), caja de texto visible y **botón del ojo** (antes el ojo seguía activo y reactivaba el campo al alternar).
- **Caída de conexión post-login**: la carga de caché va en `try/catch`; si se cae la red, avisa en el label, cancela el inicio de sesión (limpia `SesionActiva`/`UsuarioActivo`) y **desbloquea todo** para reintentar, en vez de cerrarse.
- **Cursor del ojo**: al alternar mostrar/ocultar, el cursor del `PasswordBox` se reposiciona en el índice que tenía la caja visible (vía el método interno `PasswordBox.Select` por reflexión); antes saltaba al inicio.

#### ConsolaMovimientos — cierre con cambios sin guardar
- Al cerrar la ventana (o al cerrar sesión), si hay **cambios sin guardar** en alguna pestaña de detalle (nuevo/editar), pide confirmación **Sí/No listando exactamente los títulos** de las pestañas afectadas. Detecta cambios por reflexión: propiedad `HayCambios` (PedidosDetalle) o campo `_hayCambios` (resto de detalles); los paneles General/selectores cuentan como "sin cambios". Si no hay cambios, cierra sin preguntar. Flag `_cierreConfirmado` para no preguntar dos veces en el flujo de logout. Helpers: `PestañasConCambios`, `TituloPestaña`, `TieneCambiosSinGuardar`, `ConfirmarPerderCambios`.

#### Notas / pendientes de esta sesión
- **Sin build en el entorno cloud**: todos los cambios siguen patrones existentes pero **no se compilaron**; verificar localmente (especialmente: alto de fila fijo en modo importar/oscuro, select-all en columnas Cantidad de plantilla, restricción numérica, y el aviso de cierre con cambios).
- **Orden de despliegue para borrar columnas**: como `ConectarProductos` usa `SELECT *`, conviene desplegar primero esta versión y **luego** borrar en SQL las columnas `empresa` de `articulos`/`familias`/`precios` (una versión vieja corriendo sí fallaría si se borran antes).

### Sesión 2026-06-15 — Resiliencia ante red inestable (Bolivia), guards de conexión y rediseño/escalado del Dashboard (rama `claude/eloquent-hopper-gi9nlv`)

> Contexto: la conexión a SQL Server en Bolivia es inestable. El objetivo de la sesión fue evitar congelamientos, guardados corruptos y registros "fantasma" cuando la red se cae, más mejoras de UI en el Dashboard. **No se introdujo SQLite ni caché en disco** (se evaluó y descartó por riesgo de duplicar correlativos entre usuarios y romper la generación de códigos, que debe seguir siendo en vivo contra SQL Server).

#### Resiliencia de escritura (`DataConsulta.ExportarItems` + `DatabaseConnection`)
- **Transacción atómica por tabla**: `ExportarItems` envuelve sus INSERT + UPDATE en una sola `SqlTransaction` (todo-o-nada). En rollback restaura el `estadof` original en memoria para conservar los cambios pendientes y poder reintentar. ⚠️ La atomicidad es **por tabla**, NO por documento completo (un pedido se guarda en 4 tablas vía `OrdenarTablas` → `DocumentosPObj`, `PedidosObj`, `TrasaccionesObj`, `EntregasObj`, cada una con su propia transacción). La atomicidad multi-tabla quedó **pendiente** (limitación conocida).
- **Inserts idempotentes**: el INSERT pasó de `VALUES (...)` a `INSERT ... SELECT ... FROM (VALUES …) AS v(cols) WHERE NOT EXISTS (SELECT 1 FROM tabla WHERE id = v.id)`, para que un reintento tras un ACK perdido no duplique filas por `id`. Los UPDATE ya son idempotentes.
- **Descarte de inserts no persistidos (anti-fantasma)**: si `ExportarItems` falla definitivamente (tras reintentos), `DescartarInsertsNoPersistidos()` quita de la caché en memoria las filas en estado `"nuevo"` que no llegaron a SQL, para no dejar registros que se ven en la app pero no existen en la base. (Las ediciones que fallan quedan pendientes, no se revierten — limitación conocida.)
- **`SqlRetry`** (nueva clase, `WpfAppVba.Data`): `SqlRetry.Ejecutar(Func/Action, intentos=3, baseMs=400)` reintenta con backoff exponencial **solo** ante errores SQL transitorios (números `-2, 53, 64, 121, 233, 1205, 10053/4/60/1, 11001, 40197, 40501, 40613`). Es **síncrono** (`Thread.Sleep`), así que un reintento añade una pausa breve en el hilo de UI. Envuelve: `ObtenerDatos` (lecturas), `ExportarItems` (la transacción completa) y los helpers en vivo (`CodigoExiste`, `SiguienteCodigoInt`, `SiguienteNumeroDoc`, `SiguienteNumeroDocPorEmpresa`, `MaxFecha`, `Maximo`, `VerificarId`, `IndicesNoNormales`).
- **`DatabaseConnection.ConnectionString`**: se agregó `Connect Retry Count=3;Connect Retry Interval=10;Pooling=true;` (resiliencia de conexiones idle ante microcortes).
- **`DatabaseConnection.Sondear(int timeoutSeg=2)`** (nuevo): sonda rápida con una conexión **propia y desechable** (timeout corto), que NO toca la conexión compartida; la usa el timer de estado de conexión.

#### Estado de conexión (label en top bar) + guards de guardado/actualización
- **`ConexionEstado`** (nueva clase estática, `WpfAppVba`): solo mantiene estado, sin UI. `EnLinea` (bool, optimista en `true`), evento `Cambio`, `Intervalo` (15 s). `Iniciar(Dispatcher)` arranca un `DispatcherTimer` que sondea en segundo plano (`DatabaseConnection.Sondear(2)` vía `Task.Run`) y marshaliza el cambio al hilo de UI. `Revisar()` fuerza una sonda inmediata.
- **Label en la top bar** de `ConsolaMovimientos`: pill `PillConexion` + `LblConexion` ("●  En línea" verde `#2E7D32` / "●  Sin conexión" rojo `#C62828`), en la columna central. Se suscribe a `ConexionEstado.Cambio` en el ctor (`ConexionEstado.Iniciar(Dispatcher)`) y se da de baja en `ConsolaMovimientos_Closing`.
- **`FuncionesComunes`**: `HayConexionOAvisa(owner, mensaje)` (capa 1: lee `ConexionEstado.EnLinea` instantáneo; capa 2 con cortocircuito `&&`: `TieneConexion()` real / `SELECT 1`); `VerificarConexionParaGuardar(owner)` ("Sin conexión. No se pueden guardar los cambios.") y `VerificarConexionParaActualizar(owner)` ("…no se pueden actualizar los datos.").
- **Guard al GUARDAR** (`if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return [false];`) al inicio de `Guardar()` de los 14 `*Detalle` (Articulos, Categorias, Correcciones, Empresas, Familias, Industrias, Inventarios, Pedidos, Precios, Productos, Regiones, Sucursales, Terceros, Usuarios), `Configuracion.BtnGuardar_Click`/`BtnGuardarTema_Click` y `CambiarContrasena.BtnGuardar_Click`. (Se convirtieron a cuerpo de bloque los `Guardar()` que eran expresión.)
- **Guard al ELIMINAR/cambiar estado**: en los `BtnEliminar_Click` de los 15 paneles `*General` y en `PreciosGeneral.BtnCambiarEstado_Click`. **NO** se guarda `ConsolaMovimientos.MarcarInactivo()` (presencia best-effort en logout, con su propio try/catch).
- **Guard al ACTUALIZAR**: en los 16 `BtnActualizar_Click` (paneles General + Dashboard), con el mensaje de actualizar.

#### Offline: arreglo del congelamiento de Configuración
- `Configuracion.PoblarSucursales` / `ActualizarPeriodos` / `ActualizarFechaInicio`: si `ConexionEstado.EnLinea == false` usan el **caché en memoria** (`Sql.SucursalesObj` filtrado por empresa; `MaxFecha` solo se intenta online) en vez de consultar SQL Server. Antes, abrir la pestaña Configuración sin conexión consultaba SQL en el hilo de UI → con `SqlRetry` reintentaba y reventaba con `SqlException` (congelamiento ~10 s). Ahora abre al instante offline.

#### AppSheets — solo "sincronizar todo"
- Se **eliminó** `SincronizarAppsheetsDialog.xaml/.cs` y el método `AppsheetsSync.SincronizarSucursalActiva()` (y la lógica `suc != null` de los helpers `InsertarFaltantes`/`MarcarEliminados`, que ahora siempre cubren todas las sucursales de la empresa vía `CROSS JOIN`).
- `Configuracion.BtnSincronizarAppsheets_Click` ahora llama **directamente** a `AppsheetsSync.SincronizarTodasLasSucursales()` (sin diálogo). `ArticulosGeneral` ya usaba esa función.

#### Dashboard — rediseño de gráficos + escalado tipo zoom
- **Reorganización a grilla 2 columnas × 3 filas** (`DashboardGeneral.xaml`): Col 1 = "Cantidad por mes y categoría" (filas 1-2, `RowSpan`) + "Suma por artículo" (fila 3); Col 2 = "Total por categoría" (fila 1) + "Total por familia" (filas 2-3, `RowSpan`).
- **Escalado global tipo zoom**: TODO el contenido (segmentadores + KPIs + 3 gráficos) está envuelto en un **`Viewbox` (`Stretch="Uniform"`)** sobre un tamaño de diseño fijo `<Grid Width="1600" Height="860">`. Al minimizar/maximizar el conjunto se escala proporcionalmente; nada se recorta. (`Uniform` = sin deformar, puede dejar franja; alternativa `Fill` = llena sin franja pero deforma. Se evaluó un slider de zoom manual con `ScaleTransform`+`ScrollViewer` pero el usuario lo **revirtió**; quedó solo el escalado automático del Viewbox.)
- **Scrolls internos selectivos** (respaldo si los datos exceden el diseño): "por mes" tiene **scroll horizontal** (su `PanelMeses` pasó de `UniformGrid` a `StackPanel Orientation="Horizontal"` para poder desbordar; el eje Y queda fijo fuera del scroll); "por categoría" y "por familia" tienen **scroll vertical**; la tabla "por artículo" conserva el scroll interno del `DataGrid`.

#### Limitaciones conocidas (pendientes, NO implementadas)
- **Atomicidad de documentos multi-tabla**: cabecera + líneas de un pedido/traspaso se guardan en transacciones separadas; un corte entre tablas puede dejar un documento incompleto en SQL. Requiere una transacción ambiental compartida en `DatabaseConnection` (cambio mayor en el acceso a datos).
- **Revertir ediciones al fallar**: el anti-fantasma solo descarta INSERTs nuevos; una edición que falla deja la caché mostrando el valor nuevo aunque SQL tenga el viejo (hasta el próximo guardado/Actualizar). Requiere snapshot del valor previo.
- **`SqlRetry` es síncrono**: los reintentos pausan brevemente el hilo de UI. Eliminarlo requeriría pasar el acceso a datos a async.

### Sesión 2026-06-15 — Dashboard bars, sección Usuarios y ícono global (rama `claude/confident-curie-t2etpj`)

#### Dashboard — barras de familia proporcionales al ancho
- **`DashboardGeneral.xaml`**: `PanelFamilias` ScrollViewer cambiado a `HorizontalScrollBarVisibility="Disabled"` para que las columnas star tengan ancho finito.
- **`DashboardGeneral.xaml.cs`** `RenderFamilias()`: las barras ahora usan un `innerGrid` con 2 columnas star (`total` | `maxTotal - total`); la primera columna contiene un `segsGrid` con los segmentos por categoría (star proporcional a su valor). El `track` Border usa `HorizontalAlignment=Stretch` para ocupar todo el ancho del card. El total numérico queda a la derecha pegado a la barra (`barRow` con col 1 = Auto).

#### Nueva sección Usuarios (solo admin)
- **`UsuariosGeneral.xaml/.cs`** (nuevo UserControl):
  - Lista con DataGrid: Línea / Código / Cuenta / Nombres / Apellidos / Tipo / Estado.
  - Buscador por cuenta/nombres/apellidos; botones Nuevo/Editar/Eliminar/Actualizar.
  - Guarda contra eliminar al usuario activo (`fila.Id == AppState.UsuarioActivo`).
  - `UsuarioFila`: Linea, Id, Codigo, Cuenta, Nombres, Apellidos, Tipo, EstadoU.
- **`UsuariosDetalle.xaml/.cs`** (nuevo UserControl):
  - Cards: IDENTIFICACIÓN (Código read-only + Cuenta), DATOS PERSONALES (Nombres + Apellidos), ACCESO (CmbTipo [admin/user] + TxtEstadoU read-only TextBlock), EMPRESA Y SUCURSAL (CmbEmpresa + CmbSucursal dependiente).
  - Sin campo contraseña ni tema (eliminados del formulario).
  - `TxtEstadoU` es un `TextBlock` dentro de `Border` con `ThemeBgReadOnly` — no editable.
  - `EmpresaItem` / `SucursalItem` como clases internas privadas.
  - `PoblarSucursales(empresaId)` hace consulta directa filtrada por empresa.
  - `GuardarNuevo`: setea `estadoU = "inactivo"`, `estadof = "normal"`.
  - `GuardarEditar`: NO modifica `estadoU` (gestionado automáticamente).
- **`ConsolaMovimientos.xaml`**: botón `👤 Usuarios` (`BtnNav_Usuarios`) con `Visibility="Collapsed"` antes de Configuración; `MinWidth="960"` `MinHeight="620"`; `Closing="ConsolaMovimientos_Closing"`.
- **`ConsolaMovimientos.xaml.cs`**:
  - En constructor: `if (AppState.EsAdmin) BtnNav_Usuarios.Visibility = Visibility.Visible;`
  - `_panelUsuarios` (mutable, recreado en `RecargarContexto()`); sección `"usuarios"` en ambos diccionarios.
  - `MarcarInactivo()`: guarda `estadoU = "inactivo"` para `AppState.UsuarioActivo`; con guard `if (string.IsNullOrEmpty(...)) return;` para evitar doble ejecución.
  - `ConsolaMovimientos_Closing`: llama `MarcarInactivo()`.
  - `BtnCerrarSesion_Click`: llama `MarcarInactivo()` antes de limpiar `AppState.UsuarioActivo` (doble llamada en Closing es segura por el guard).
- **`LoginWindow.xaml.cs`**: al autenticar, setea `estadoU = "activo"` y llama `ExportarItems()` antes de abrir `ConsolaMovimientos`.

#### Ícono de aplicación en todas las ventanas
- **`WpfAppVba.csproj`**: `<ApplicationIcon>icono.ico</ApplicationIcon>` + `<Resource Include="icono.ico"/>`.
- **`ConsolaMovimientos.xaml`**: `Icon="icono.ico"` en el elemento `Window`.
- **`App.xaml.cs`**: en el constructor, se crea `BitmapFrame.Create(new Uri("pack://application:,,,/icono.ico", UriKind.Absolute))` y se asigna a cada ventana vía el `EventManager.RegisterClassHandler` existente de `Window.Loaded` (mismo handler que aplica el modo oscuro). Esto garantiza que el ícono aparece en la barra de tareas y título de TODAS las ventanas (LoginWindow, ConsolaMovimientos, diálogos).
  - Nota: la aproximación anterior con `<Style TargetType="Window">` en `App.xaml` no funciona porque WPF no aplica estilos implícitos de Application.Resources a subclases de Window.

#### Distribución / Instalador
- Publicación self-contained con archivo único:
  ```
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none
  ```
  Resultado: 7 archivos en `bin\Release\net8.0-windows\win-x64\publish\` (1 exe ~162 MB + 6 DLLs nativas de WPF inevitables).
- Instalador creado con **Inno Setup 6.7.3** (asistente Script Wizard): idiomas Inglés + Español; modo administrativo; acceso directo en escritorio y menú Inicio; output `Setup_SistemaGestion.exe`. Script `.iss` guardado para futuras actualizaciones (recompilar con F9 tras nuevo publish).

---

### Sesión 2026-06-14 — AppSheets (sincronización), orden por `secuencia` y panel Dashboard (rama `claude/affectionate-albattani-r2uilg`)

#### Tabla `appsheets` + sincronización desde Configuración
- La tabla `appsheets` la creó el usuario en SQL Server. Estructura final: `secuencia` INT IDENTITY, `estadof` NVARCHAR(255), `emision` DATETIME, `id` UNIQUEIDENTIFIER DEFAULT NEWID(), `sucursal`, `articulo`, `usuario`, `empresa` (todas UNIQUEIDENTIFIER). **La columna `indice` se eliminó** (no tenía función).
- Nueva clase **`AppsheetsSync`** (estilo `CodigoRegenerator`, SQL directo en transacción). Métodos:
  - `SincronizarSucursalActiva()` — solo la sucursal activa.
  - `SincronizarTodasLasSucursales()` — todas las sucursales `estadof='normal'` de la empresa activa (INSERT vía `CROSS JOIN sucursales`).
  - Reglas por sucursal: **INSERT** una fila por cada artículo activo (`estadof='normal'`, empresa activa) que aún no tenga fila `'normal'` en `appsheets` (rellena `articulo`, `sucursal`, `empresa`, `usuario`, `emision=GETDATE()`, `estadof='normal'`); **UPDATE `estadof='eliminado'`** las filas `'normal'` cuyo artículo ya no esté activo.
- **Configuración**: botón **`🧾 Sincronizar AppSheets`** a la izquierda de "Cambiar Contraseña". Abre `SincronizarAppsheetsDialog` (Window) con 3 opciones: *Sincronizar todo* (todas las sucursales) / *Sincronizar sucursal activa* / *Cancelar*.
- **Disparo automático**: al **agregar** (`ArticulosDetalle.GuardarNuevo`/`GuardarInsertar`) o **eliminar** (`ArticulosGeneral.BtnEliminar_Click`) un artículo se ejecuta `SincronizarTodasLasSucursales()` vía el helper estático `ArticulosGeneral.SincronizarAppsheetsTrasCambio()` (si falla solo avisa, no revierte el cambio del artículo). Editar un artículo NO dispara sync.

#### Orden de recarga de caché por `secuencia`
- `AppLoader.ConectarProductos`: las consultas que ordenaban `ORDER BY id ASC` ahora usan `ORDER BY secuencia ASC` (empresas, usuarios, familias, productos, Categorias, industrias, terceros, sucursales, regiones). `articulos` sigue por `familia, indice`; `precios` por `fecha`.

#### Nuevo panel **Dashboard** (sidebar)
- Nuevo `DashboardGeneral` (UserControl), sección `"dashboard"`, botón `📊 Dashboard` al tope del sidebar de `ConsolaMovimientos` (registrado en diccionarios, `MostrarPanel`, `RecargarContexto`, `BtnNav_Dashboard_Click`, `_panelDashboard`).
- Lee del caché ya filtrado (`PedidosObj`/`TraspasosObj`) por sucursal + período activos. Venta/compra por `documentosP.movimiento`; entrada/salida por `origen`/`destino` vs sucursal activa en `documentosT`; categoría/familia/descripción/código vía `articulos` → `CategoriasObj`/`FamiliasObj`. Todo en **cantidad** (unidades). Botón **Actualizar**.
- **Segmentador de selección única** (RadioButton, GroupName): Ventas/Compras/Entradas/Salidas; cada chip muestra su cantidad y filtra el resto; por defecto Ventas.
- **KPIs** (a la derecha de los segmentadores, misma fila): Total unidades (tipo activo), Movimientos, Artículos distintos.
- **Gráfico por mes**: barras **agrupadas por categoría** (una barra por categoría con valor encima), solo meses con datos, eje Y con escala (redondeo "lindo" `NiceCeil`) + cuadrícula horizontal; el total del mes va debajo del nombre del mes. Leyenda de colores por categoría. **Altura dinámica** según el alto disponible (`PanelPlot_SizeChanged`, cache `_porMesCache`).
- **Por categoría**: barras horizontales que llenan el ancho (proporción star), eje X + cuadrícula, color propio por categoría (paleta estable `_colorCat`).
- **Por familia**: una barra **apilada por categoría** por familia, con los totales por categoría entre el título y la barra; longitud proporcional al total de la familia (ancho fijo `AnchoBarra`).
- **Lista de artículos**: DataGrid Código · Familia · Descripción · Cantidad · % + fila Total.
- **Layout sin scroll general**: el contenido es un `Grid` con filas proporcionales (`Auto` / `1*` / `1.4*`); el gráfico y las tablas se estiran por resolución y solo las listas internas tienen scroll. Disposición inferior: col 1 = categoría (arriba) + artículos (abajo); col 2 = familia (ambas filas).
- Gráficos dibujados con primitivas WPF (sin dependencias NuGet nuevas).

### Sesión 2026-06-12/13 — Multi-empresa, borrado lógico, lógica de índices, períodos y limpieza (rama `claude/cool-hopper-vo3mxo`)

#### Multi-empresa: formularios y empresa activa
- Nueva sección **Empresas** en el sidebar, entre Precios y Configuración (botón `🏢 Empresas`).
- `EmpresasGeneral` (UserControl): lista Línea/Código/Descripción/Signo; CRUD "Nueva/Editar/Eliminar Empresa" + Actualizar; patrón estándar (`_iniciado`, `Cerrando`/`IntentarCerrar`, actualización incremental, `OpenAsTab` selector clave `seleccionar-empresa|{contexto}`). Usa `Sql.EmpresasObj`.
- `EmpresasDetalle` (UserControl): Código (int, `SiguienteCodigoInt()`, read-only al editar), Descripción, Signo (NVARCHAR(4) mayúscula), Observación (TextBox plano en `Border`, texto al tope como `TercerosDetalle`). Valida `CodigoExiste()`. Persiste `codigo, descripcion, signo, observacion, fecha, emision, edicion, usuario, usuarioE`. La columna `codigo` (INT) de `empresas` la agregó el usuario en SQL.
- `ConsolaMovimientos`: panel `_panelEmpresas` (+ sección `"empresas"` en los diccionarios, caso en `MostrarPanel`, `BtnNav_Empresas_Click`). Los paneles General pasaron de `readonly` a mutables.
- **Configuración**: nuevo `CmbEmpresa` ("EMPRESA ACTIVA") a la izquierda de `CmbSucursal`. `CmbSucursal` es **dependiente** de la empresa (se repuebla por consulta directa `_sucursalesEmpresa`, ya que el caché está filtrado por la empresa activa). `ActualizarFechaInicio`/`ActualizarPeriodos` leen de `_sucursalesEmpresa`.
- **Guardar en Configuración ya NO cierra sesión**: si cambia empresa/sucursal/periodo recarga cachés (`ConectarProductos` si cambió empresa, `ConectarBases`, `ActualizarBase`, `ConectarDocumentos`) y llama `ConsolaMovimientos.RecargarContexto()` (cierra las pestañas dinámicas y **recrea los paneles General**), manteniendo el foco en Configuración; luego `CargarDatos()` repuebla sus combos. La empresa se persiste en `usuarios.empresa`. Se eliminó `CerrarSesionYReabrirLogin`.
- **No se puede guardar sin sucursal**: si `CmbSucursal` está vacío (empresa sin sucursales) → `MessageBox` de advertencia y `return` (reemplaza un comportamiento intermedio que dejaba `usuarios.sucursal` en NULL).
- **TOP BAR**: nuevo `LblEmpresa` ("Empresa: {desc}") a la derecha de `LblSucursal`, seteado en `ActualizarInfoUsuario()`.

#### Borrado lógico global (sin DELETE físico)
- `DataConsulta.ExportarItems` ya NO ejecuta `DELETE FROM`. Las filas con `estadof` = `"eliminado"`/`"ocultado"` se persisten con `UPDATE` del `estadof` (se filtran al recargar, que solo trae `estadof='normal'`). Se quitó el bloque DELETE y `deleteIds`. Afecta a todos los `.Eliminar()`/`.Ocultar()`. Nota: las tablas acumulan filas con estadof≠normal (no se borran nunca).

#### Lógica de índices: no reutilizar índices de filas eliminadas
- **Guardado diferencial de líneas** de documentos (antes borraban TODAS las líneas y las recreaban): nueva (id de fila vacío) → insertar; existente → `EstablecerItem` sobre su mismo id (UPDATE); quitada (estaba al abrir y ya no está) → `Eliminar()` (estadof "eliminado"). Cada detalle captura los ids originales (`_xxxOrig`) al abrir para editar y los limpia en modo nuevo. Aplicado en **PedidosDetalle** (pedidos/transacciones/entregas), **TraspasosDetalle**, **CorreccionesDetalle**, **InventariosDetalle**. Se quitaron `EliminarLineas`/loops `idsEliminar`.
- **Índice por posición sin reutilizar eliminados**: cada línea visible se numera por su posición en la grilla, **saltando los índices reservados** por filas eliminadas (que conservan su índice). Reservados = filas `estadof <> 'normal'` en SQL (`DataConsulta.IndicesNoNormales(filtroColumna, filtroValor)`) + las que se eliminan en este guardado. Ej.: A(índice 1, eliminado) + B(índice 2, normal), insertar C sobre B → A=1 (eliminado), C=2, B=3. (Reemplaza un intento previo `SiguienteIndice` = MAX+1, ya retirado.)
- **`articulos`** (su `indice` es la posición dentro de la familia): Eliminar (`ArticulosGeneral`) ya NO corre los índices, solo oculta (índice reservado, puede quedar hueco). Insertar (`ArticulosDetalle.GuardarInsertar`) usa el nuevo helper estático `ArticulosGeneral.RenumerarFamilia(famId)` (renumera activos por orden saltando reservados). El índice sugerido (`RecalcularIndicePorFamilia`) considera también los reservados.
- Tablas con columna `indice` cubiertas por la regla: pedidos, transacciones, entregas, traspasos, correcciones, inventarios y articulos. (Modo editar de un artículo: mantiene su índice; reposicionar manualmente ahí aún no aplica el salto-de-reservados — pendiente menor.)

#### Apertura / períodos
- **Períodos desde la máxima fecha de inventario**: `Configuracion.ActualizarPeriodos` calcula el año inicial de `CmbPeriodo` como el año de la MÁXIMA fecha de inventario de la sucursal (`DocumentosIObj.MaxFecha("sucursal", id)`, consulta directa a SQL); si la sucursal no tiene inventarios usa el año de `sucursal.fecha`. Nuevo helper `DataConsulta.MaxFecha`. Evita elegir un período anterior al inventario (que recargaba documentos previos a esa fecha vía `ActualizarBase`). Ej.: sucursal con `fecha`=01/01/2024 e inventario 19/02/2026 → el desplegable solo muestra 2026.
- **Fix `uniqueidentifier`**: `AppLoader.ConectarBases`/`ConectarDocumentos` usan el GUID nulo `00000000-0000-0000-0000-000000000000` cuando `SucursalActiva` está vacía (antes `sucursal = ''` contra una columna `uniqueidentifier` lanzaba *"Conversion failed when converting from a character string to uniqueidentifier"* al guardar sin sucursal y también en el login siguiente).

#### Eliminación de la tabla `stocks`
- El usuario eliminó la tabla `stocks` en SQL Server. Se quitó `SqlData.StocksObj`, su `Conectar("stocks", ...)` en `AppLoader.ConectarProductos`, y `AppState.ActualizarStocks()` + sus 3 llamadas. **No** se tocó el cálculo de stock (`StockCalculator.ContarStock/ContarStock2`, `GridStock`, columnas "Stock", avisos de stock insuficiente), que usa apertura + documentos y no dependía de la tabla.

#### Otros (UI y limpieza)
- **MovimientosGeneral**: la columna "Movimiento" muestra `código-tipo` del documento (de `Documentos[P/T/C]Obj.ObtenerItem("codigo", id)`) en vez del `id(UUID)`. Se **eliminó** `MovimientosWindow.xaml/.cs` (Window legacy sin referencias); las clases `MovimientoDato`/`MovimientoFila` se movieron a `MovimientosGeneral.xaml.cs`.
- **Diseño esquinas redondeadas**: `EmpresasGeneral` y `RegionesGeneral` al estilo unificado (botones `ControlTemplate CornerRadius=6`, `SearchInput` redondeado, `DataGrid` en `<Border CornerRadius="6">`).
- **Signo en maestros**: `RegionesDetalle` y `SucursalesDetalle` tienen `Box_Signo` (SIGNO, MaxLength 4, mayúscula) a la derecha de DESCRIPCIÓN; cargan/guardan `regiones.signo`/`sucursales.signo`.

#### Helpers nuevos en `DataConsulta`
- `IndicesNoNormales(filtroColumna, filtroValor)` → índices de filas `estadof <> 'normal'` del documento (para no reutilizarlos).
- `MaxFecha(filtroColumna, filtroValor)` → máxima fecha de filas en estado normal (consulta directa).
- (`SiguienteIndice` se introdujo y luego se retiró; reemplazado por la regla de posición + `IndicesNoNormales`.)

### Sesión 2026-06-12 — Empresas, regeneración de códigos y UI de conexión (rama `claude/brave-albattani-03ox62`)

#### Nueva entidad: Empresa (multi-empresa)
- Cambios de esquema hechos por el usuario en SQL Server:
  - Nueva tabla `empresas` (`id` UNIQUEIDENTIFIER, `secuencia` INT IDENTITY, `descripcion`, `signo` NVARCHAR(4), `observacion`, `fecha`, `emision`, `edicion`, `usuario`, `usuarioE`, `estadof`).
  - Nueva columna `empresa` (UNIQUEIDENTIFIER) en: `usuarios, sucursales, articulos, categorias, familias, industrias, productos, regiones, stocks, terceros`.
  - Todas las tablas tienen `id` con `DEFAULT NEWID()` y una columna `secuencia` IDENTITY (para que la herramienta SQL ordene sin tocar el `id`).
- `AppState.EmpresaActiva` (string); se setea al iniciar sesión desde `usuarios.empresa` y se limpia en logout (`ConsolaMovimientos`, `Configuracion`).
- `SqlData.EmpresasObj`; `AppLoader.ConectarProductos` carga la tabla `empresas`.
- **Filtro por empresa en la carga de caché** (`ConectarProductos`, solo cuando hay empresa activa):
  - Filtro directo `empresa = '{guid}'`: `stocks, articulos, familias, productos, Categorias, industrias, terceros, sucursales, regiones`.
  - `usuarios`: SIN filtro (necesario para el login).
  - `precios` (sin columna empresa): cascada `region IN (SELECT id FROM regiones WHERE empresa = '{guid}')`.
  - Tras autenticar se vuelve a llamar `ConectarProductos()` ya filtrado por la empresa del usuario.
- **Documentos**: se evaluó cargarlos por todas las sucursales de la empresa (cascada empresa→sucursales) pero **se revirtió**; siguen filtrándose por **sucursal activa + rango de fechas** (commit revert `4eeb572`). `ConectarBases`/`ConectarDocumentos` quedaron como antes.

#### Conexión SQL Server: formulario propio (extraído de Configuración)
- Nuevo `ConexionServidoresWindow` (Window): lista/agregar/editar/eliminar/conectar servidores (antes era el CARD "CONEXIÓN SQL SERVER" embebido en `Configuracion`).
- Se **eliminó** ese card de `Configuracion.xaml`/`.cs` (y el estilo `SelectorBtn` y todo el code-behind de servidores); el grid de Configuración quedó a una sola columna (GENERAL + MI CUENTA).
- `LoginWindow.BtnConfigurarConexion_Click` abre `ConexionServidoresWindow` (en vez de `ConfiguracionDbWindow`); al cerrar recarga `DatabaseConnection.CargarDesdeConfiguracion()` y reconecta. `ConfiguracionDbWindow` se conserva (diálogo de un solo servidor usado por Agregar/Editar).
- **Conectar** no cierra el formulario: marca el servidor activo, reconfigura `DatabaseConnection` y refresca la lista (queda abierto; se cierra con "Cerrar").
- Fix de layout: el `DataGrid` usa `MinHeight` y ocupa el espacio (`Grid` con filas), para que los botones Conectar/Editar/Eliminar queden siempre visibles.

#### Regeneración de códigos (`CodigoRegenerator` + botón en `ConexionServidoresWindow`)
- Botón "🔢 Regenerar códigos" (a la derecha de Eliminar) que ejecuta `CodigoRegenerator.RegenerarTodos()` en una transacción sobre el servidor activo. Reescribe **todas** las filas (sin filtro `estadof`).
- Maestras (`usuarios, familias, productos, Categorias, industrias, terceros, sucursales, regiones`): `codigo = 1..N` (`ROW_NUMBER() OVER (ORDER BY id)`).
- `documentosI/P/C`: `codigo = signo_sucursal + correlativo por sucursal`.
- `documentosT` (traspasos): `codigo = signo_empresa + correlativo por empresa`, vía cascada `emitido → sucursales.empresa → empresas.signo` (documentosT NO tiene columna `sucursal`; tiene `origen/destino/emitido`).
- `precios`: `codigo = signo_region + correlativo por región`.
- Los signos siempre en **MAYÚSCULA** (`UPPER(...)`).

#### Códigos: signo en mayúscula y visualización del código completo
- Al construir el código en los detalles (`Pedidos/Traspasos/Inventarios/Correcciones/Precios`) el signo va en mayúscula (`signo.ToUpper()`).
- **Traspasos**: el signo del código sale de `empresas.signo` (no `sucursales.signo`); nuevo método `DataConsulta.SiguienteNumeroDocPorEmpresa(signo, empresaId)` (cascada `emitido→sucursales.empresa`) para numerar el traspaso nuevo por empresa. Corrige el error "Invalid column name 'sucursal'" al crear un traspaso.
- `Box_Documento*` (Pedidos/Traspasos/Correcciones/Inventarios) muestra el **código completo** (signo+número) en modo nuevo y editar (antes mostraba solo el número).
- Grids "General": la columna "Documento" muestra el **código** del documento (propiedad `Codigo` agregada a `PedidoFila/TraspasoFila/CorreccionFila/InventarioFila`); la clave interna (`DocumentoP/DocumentoT/Id`) no cambia.
- Cabecera del panel de detalle (Pedidos/Traspasos/Correcciones) muestra el código del documento.
- Título de la pestaña de edición usa el **código** (no el id UUID); la clave de deduplicación sigue usando el id.

#### ConsolaMovimientos — top bar
- `LblUsuario` muestra el **nombre** del usuario (`usuarios.nombres`) en vez del id; conserva el Período.
- Nuevo `LblSucursal` a la derecha con `Sucursal: {descripción}` de la sucursal activa.

#### Persistencia — columna IDENTITY `secuencia`
- `DataConsulta.ExportarItems` **excluye** la columna autogenerada `secuencia` (helper `EsAutogenerada` + `ColumnasPersistibles`) de INSERT y UPDATE, para evitar "Cannot insert/update identity column 'secuencia'". El `id` se sigue insertando explícitamente (convive con `DEFAULT NEWID()`).
- Pendiente conocido: si se agrega un alta de `empresas` desde el app, habría que asegurarse de que su `secuencia` IDENTITY también quede excluida (ya lo está por nombre).

### Sesión anterior (antes de compactación)
- Precios migrado a panel sidebar + pestañas (`PreciosGeneral`, `PreciosDetalle`).
- Regiones: nueva sección en sidebar entre Inventarios y Precios (`RegionesGeneral`, `RegionesDetalle`).
- Sucursales: selector de región vía `RegionesGeneral.OpenAsTab` con callback (sin estado global).
- Inventarios migrado a panel sidebar + pestañas (`InventariosGeneral`, `InventariosDetalle`); selección de artículos vía `ArticulosGeneral.OpenAsTab`.
- Configuración: ya tenía soporte embedded en `BtnGuardar_Click`; no requirió cambios de código.

### Sesión anterior (antes de compactación)
- **SucursalesDetalle**: botones Guardar/Cancelar movidos a inferior izquierda con estilo uniforme (`#1A73E8`, `ThemeBtnSecBg`/`ThemeBtnSecFg`, `Cursor="Hand"`), igual a TercerosDetalle.
- **Todos los formularios General**: botones CRUD renombrados para incluir el nombre de la entidad (e.g., "Nuevo Traspaso", "Editar Corrección", "Eliminar Artículo").

### Sesión anterior (antes de compactación)
- **Botón X de pestañas dinámicas**: ahora llama `IntentarCerrar()` vía reflexión si el contenido lo implementa, protegiendo cambios no guardados antes de cerrar.

### Sesión 2026-06-11 — Refactor id/codigo (commit c5002d6)
Separación completa del campo `id` interno (UUID) del campo `codigo` visible al usuario en todos los formularios. **41 archivos modificados.**

#### Infraestructura
- **`AppState.cs`**: `SucursalActiva`, `RegionActiva`, `UsuarioActivo`, `AperturaIdActiva` cambiados de `long` a `string`.
- **`DataConsulta.cs`**:
  - `BuscarIdentificador()` ahora devuelve `string` en vez de `long`.
  - Nuevo método `SiguienteCodigoInt()`: `MAX(CAST(codigo AS INT)) + 1` para tablas maestras.
  - Nuevo método `SiguienteNumeroDoc(signo, filtroColumna, filtroValor)`: siguiente número de documento filtrado por sucursal o región activa; devuelve solo el número (sin el signo).
  - Nuevo método `CodigoExiste(codigo, idActual?)`: verifica duplicados antes de guardar.
  - Fix DELETE: ids UUID se citan correctamente en SQL (`'uuid'` con comillas).
- **`AppLoader.cs`, `StockCalculator.cs`, `LoginWindow.xaml.cs`, `Configuracion.xaml.cs`, `ConsolaMovimientos.xaml.cs`**: actualizados para usar `AppState` con campos `string`.

#### Formularios maestros (Productos, Industrias, Categorías, Regiones, Terceros, Familias, Sucursales, Artículos)
- Modo nuevo: `id = Guid.NewGuid().ToString()`; `codigo` = entero autoincremental sugerido via `SiguienteCodigoInt()`, editable por el usuario.
- Modo editar: usa `_idEditar` (UUID) como clave interna; `Box_Codigo` muestra el `codigo` legible.
- Validación: `CodigoExiste()` antes de guardar nuevo; si ya existe, sugiere el siguiente disponible.
- `OrdenarData(("codigo", false))` en todos (antes `("id", false)`).
- Campos FK en formularios: muestran `codigo` del registro referenciado (no el UUID). Al guardar, se resuelve el UUID internamente con `BuscarIdentificador("codigo", cod)`.
- Helpers `ResolverXxxId()` agregados en los formularios con FK (Familias→Producto, Sucursales→Región, Artículos→Familia/Industria/Categoría).

#### Formularios de documentos (Pedidos, Traspasos, Correcciones, Inventarios, Precios)
- `codigo` del documento = `signo_sucursal + numero` (ej. `"A5"`); al usuario solo se muestra el número (`"5"`); el campo Box_DocumentoX queda deshabilitado.
- `id` del documento = `Guid.NewGuid().ToString()` (nunca visible).
- Precios: usa `signo_region` (de `AppState.RegionActiva`) en vez de signo de sucursal.
- Sub-registros de líneas (pedidos, trasacciones, entregas, corrección-líneas, inventario-líneas): también usan `Guid.NewGuid().ToString()` como id (antes `$"{docP}{i:D3}"`).
- FK de tercero: `Box_Tercero_Identificador` muestra `codigo` del tercero; se resuelve UUID con `ResolverTerceroId()` al guardar.
- Eliminado `VerificarId()` para documentos; la generación automática de UUID garantiza unicidad.

#### Formularios General (todas las secciones)
- `Seleccionar()` devuelve `fila.Codigo` (antes `fila.Id`).
- Títulos de pestañas usan `fila.Codigo` (ej. `"Artículo A123"` en vez de UUID).
- Columnas de DataGrid vinculadas a `Binding="{Binding Codigo}"` donde antes usaban `Id`.
- `ConstruirFila()`: poblada con `Codigo = Sql.XxxObj.ObtenerItem("codigo", id)?.ToString() ?? ""`.

#### Limpieza
- Eliminadas todas las llamadas redundantes `.ToString()` sobre propiedades `string` de `AppState` en: `PedidosGeneral`, `TraspasosGeneral`, `CorreccionesGeneral`, `MovimientosGeneral`, `MovimientosWindow`, `InventariosGeneral`.

### Sesión 2026-06-10 — rama `master`

#### Accesos rápidos en top bar (ConsolaMovimientos)
- Añadidos 4 botones en la barra superior (derecha del usuario): **Venta** (rojo `#E53935`), **Compra** (azul `#1E88E5`), **Salida** (naranja `#FB8C00`), **Entrada** (verde `#43A047`).
- Estilo `QuickBtn` con `CornerRadius="6"`, `Height="32"`, separador visual antes de `LblUsuario`.
- Venta/Compra abren `PedidosDetalle` (tipo rápido) forzando `TipoMovimiento`; Salida/Entrada abren `TraspasosDetalle`.
- **Si ya existe pestaña rápida** (`"nuevo-pedido"` / `"nuevo-traspaso"`): en lugar de abrir una nueva, se llama `CambiarTipoMovimiento(tipo)` en el detalle existente y se enfoca esa pestaña.
- `PedidosDetalle.CambiarTipoMovimiento(string tipo)`: sets `AppState.TipoMovimiento` + `CboMovimiento.SelectedIndex`.
- `TraspasosDetalle.CambiarTipoMovimiento(string tipo)`: sets `AppState.TipoMovimiento` + `CboMovimiento.SelectedIndex`.
- `PedidosGeneral.AbrirNuevoPedido` made public, acepta `tipoMovimiento` opcional.
- `TraspasosGeneral.AbrirNuevoTraspaso` made public, acepta `tipoMovimiento` opcional.

#### Configuracion — reorganización de layout y botones
- Grid 2 columnas: izquierda (GENERAL fila 0 + MI CUENTA fila 1), derecha (CONEXIÓN SQL SERVER RowSpan=2).
- **Botón "Guardar Cambios"** movido al card MI CUENTA; guarda solo nombres, apellidos, sucursal y periodo.
- **Botón "Cambiar Contraseña"** movido a la barra de acciones inferior.
- Si se cambia la **sucursal activa** al guardar → cierra sesión y reabre `LoginWindow`.
- `BtnConectarServidor`: seleccionar servidor diferente al activo → confirma, `EstablecerActivo`, luego `CerrarSesionYReabrirLogin()`.
- Editar servidor activo después de guardar → `CerrarSesionYReabrirLogin()`.
- Agregar/editar servidor no-activo → solo registra, no conecta.

#### PreciosGeneral — columna Código y título de pestaña
- Añadida columna "Código" en `GridPrecios` (entre Línea y Fecha).
- La columna muestra el **ID del registro de precio** (de la tabla precios), no el código del artículo.
- Título de pestaña de edición: `"Precio {id}"` en lugar del código del artículo.

#### InventariosGeneral — botón Actualizar
- Añadido botón **Actualizar** a la derecha de "Eliminar Inventario".
- Llama `CargarInventarios()` para refrescar la lista.

#### TraspasosGeneral — métricas de estado
- Métrica **"Estado pendientes"**: ahora cuenta solo `estado == "pendiente"` (antes incluía "pendiente revisar").
- Métrica **"Pendientes revisar"** (renombrada de "Entregados"): cuenta `estado == "pendiente revisar"`.

#### Detalles de formularios — esquinas redondeadas y layout
- **ProductosDetalle, RegionesDetalle, CategoriasDetalle, IndustriasDetalle**: código + descripción lado a lado (`Grid 160|16|*`).
- **SucursalesDetalle, TercerosDetalle, PreciosDetalle, FamiliasDetalle, ProductosDetalle, RegionesDetalle, CategoriasDetalle, IndustriasDetalle**: `ModernInput` con `ControlTemplate CornerRadius="6"`; estilos `BtnBase`/`BtnPrimario`/`BtnSecundario`.
- **ConfiguracionDbWindow**: trigger `IsEnabled=False` → `Opacity=0.45` en `BtnBase`.

#### Rama de trabajo
- Esta sesión trabajó directamente en `master` (por instrucción del usuario).
- Los commits son verificados (`user.email noreply@anthropic.com`).

---

### Sesión 2026-06-09 (parte 1) — rama `master`
> Nota: esta sesión trabajó directamente en `master` por instrucción del usuario.

#### Rediseño general de vistas (esquinas redondeadas)
- **TercerosGeneral, SucursalesGeneral, FamiliasGeneral, ProductosGeneral, IndustriasGeneral, CategoriasGeneral, InventariosGeneral, PreciosGeneral**: aplicado estilo unificado de esquinas redondeadas.
  - Estilo `Btn` con `ControlTemplate` + `CornerRadius="6"` + triggers hover/pressed.
  - Estilo `SearchInput` (TextBox) con `CornerRadius="6"`.
  - DataGrids envueltos en `<Border CornerRadius="6">`.
- **PreciosDetalle**: rediseñado siguiendo el estilo de TercerosDetalle.
- **ConfiguracionDbWindow**: UX "test before save" — botón "Probar conexión" antes de guardar, mensaje de estado inline.
- **LoginWindow**: abre diálogo de configuración en modo edición cuando ya existe un servidor configurado.
- **DatabaseConnection.cs**: suprimido warning IDE0130 con directiva `#pragma warning`.

#### PedidosDetalle — rediseño completo
> El primer rediseño fue revertido por el usuario al commit `0f302660`. Luego se aplicó el diseño final aprobado.

**Estructura XAML (3 filas):**
- **Row 0 — Encabezado** (`ThemeBgSurface`, borde inferior 1px, Padding 14,10):
  - Fila superior: ícono `LblIconoTipo` (V/C), `LblTitulo`, `LblDocBadge`, `BadgeEstado`, `BadgeCuenta`, botones Cancelar + Guardar.
  - Fila de campos: Doc. Pedido, Fecha, Hora, Tercero (id + botón `···` + descripción read-only), Referencia, ComboBox Tipo.
  - Fila secundaria: Emisión (`Box_Emision`), Edición (`Box_Edicion`), `Box_Estado` y `Box_Cuenta` como `Visibility="Collapsed"` (usados por code-behind, visualmente en badges).
- **Row 1 — TabControl** (3 tabs: Artículos, Cobros/Pagos, Entregas):
  - **Tab Artículos**: toolbar + `GridItems` + panel inferior de 7 columnas (`260/1/240/1/160/1/*`):
    - Col 0: `GridPrecios` (Historial de precios)
    - Col 2: `GridStock` (Stock actual)
    - Col 4: `GridCategorias` (lista de categorías — añadida en esta sesión)
    - Col 6: `Box_Observaciones`
  - **Tab Cobros/Pagos**: toolbar + `GridTrasacciones`.
  - **Tab Entregas**: toolbar + `GridEntregas`.
- **Row 2 — Barra de totales** (Grid con dos grupos):
  - **Izquierda**: `TxtTotalUnidades` (Total unidades) + `TxtUnidadesDiferentes` (Diferentes) — números alineados a la derecha dentro de cada tarjeta `MetricCard`.
  - **Derecha**: `TxtTotalImporte` (verde `#065F46`) + `TxtTotalCuenta` (azul `#1E40AF`) + `TxtTotalSaldo` (rojo `#991B1B`) — números alineados a la derecha.

**Code-behind destacado:**
- `ActualizarBadges()`: colorea dinámicamente `BadgeEstado`/`BadgeCuenta` con `SolidColorBrush(Color.FromRgb(...))` según valores de `Box_Estado`/`Box_Cuenta`. Actualiza `LblIconoTipo` (V/C) y `LblDocBadge`.
- `CargarTotalesCategoria()`: lee todas las categorías de `Sql.CategoriasObj`; suma cantidades de `_pedidos` por categoría (campo `"categoria"` del artículo); líneas sin `ArticuloId` o sin categoría reconocida → "Otros". Asigna a `GridCategorias.ItemsSource`.
- `ActualizarTotales()` llama: `CargarTotales()`, `CargarTotalesDivisas()`, `CargarEstadosCuenta()`, `CargarTotalesCategoria()`, y `CargarEstados()` si `tipoPedido=="normal"`.
- `CategoriaCantFila`: clase pública compartida por PedidosDetalle, CorreccionesDetalle, TraspasosDetalle e InventariosDetalle para filas de `GridCategorias`.

**Correcciones realizadas:**
- CS0101: `CategoriaFila` duplicada con `CategoriasGeneral` → renombrada a `CategoriaCantFila` en PedidosDetalle.
- Normalización `.ToLower()` en `PedidosGeneral.xaml.cs` al leer `estado`/`estadoC` desde DB (previene badges negros por case mismatch).
- `GridEntregas_CellEditEnding` y `GridTrasacciones_CellEditEnding`: leen manualmente de TextBox antes de que WPF haga commit (patrón para `DataGridTextColumn` numérico + `RefrescarGrid` via `Dispatcher.BeginInvoke`).
- `FechaDate` como propiedad calculada en `TrasaccionItemFila` y `EntregaItemFila` para que el DatePicker siempre tenga valor al editar.
- `"entrega parcial"` → `"pendiente parcial"` en todo PedidosDetalle.
- Badge colors: pendiente=rojo, pendiente parcial=amarillo, entregado/cancelado=verde.
- `"Pend. parcial"` → `"Pendiente parcial"` en botón de filtro de PedidosGeneral.

### Sesión 2026-06-09 (parte 3) — rama `master`

#### CorreccionesDetalle — Stock actual al lado de Observación
- Row 3 ampliado a 140 px; panel horizontal: `GridStock (260px) | separador 1px | Observación (*)`.
- `GridItems_SelectionChanged` → `CargarStock()` usa `StockCalculator.ContarStock` igual que TraspasosDetalle.
- Nueva clase `CorreccionStockFila`.

#### TraspasosDetalle — GridCategorias siempre cargado
- `CargarTotales()` itera primero `Sql.CategoriasObj` para crear todas las filas con cantidad 0, luego acumula desde `_items`. Así el grid muestra todas las categorías desde el inicio aunque no haya artículos.

#### Movimientos — convertido a UserControl + sección en sidebar
- `MovimientosGeneral` (nuevo UserControl): mismo contenido que `MovimientosWindow` pero embebible en pestañas.
  - `OpenAsTab(Window, codigoArticulo)` abre como pestaña con clave `movimientos-{codigo}`.
  - Si `codigoFijo != ""` (invocado desde ArticulosDetalle): `TxtCodigo.IsEnabled = false`, `BtnBuscar.IsEnabled = false`.
- `ConsolaMovimientos`: nuevo botón `📈 Movimientos` en sidebar entre Correcciones y el separador; sección `"movimientos"` registrada en los diccionarios de pestañas.
- `ArticulosDetalle.BtnVerMovimientos_Click`: reemplaza `new MovimientosWindow(...).ShowDialog()` → `MovimientosGeneral.OpenAsTab(...)`.
- `MovimientosWindow` permanece en el proyecto (no se eliminó) para no romper referencias pendientes.

### Sesión 2026-06-09 (parte 2) — rama `master`

#### TraspasosDetalle — rediseño completo + mejoras
**Estructura XAML (5 filas):**
- **Row 0 — Encabezado** (`ThemeBgSurface`, borde inferior): ícono dinámico (SA/EN) + título + `LblDocNum` + `BadgeEstado` + Guardar/Cancelar en fila superior; todos los campos en línea en fila inferior: Doc. Traspaso · Fecha · Hora · Sucursal (código + botón `···` + descripción readonly) · Referencia · Estado · Tipo; emisión/edición al pie.
- **Row 1 — Toolbar** (`ThemeBgSurface`, borde inferior): label "Artículos del traspaso" + botones (Importar artículos / Buscar artículo / Insertar / Nueva línea / Eliminar línea).
- **Row 2 — GridItems**: DataGrid a todo el ancho, sin borde exterior, `RowHeight=32`, `ColumnHeaderStyle` unificado.
- **Row 3 — Stock + Observaciones** (altura 140): panel horizontal — Stock (260px) | separador 1px | Observaciones (*); ambos con `Border CornerRadius="6"`.
- **Row 4 — Barra de totales** (`ThemeBgSurface`, borde superior): `MetricCard` Total unidades + `MetricCard` Diferentes + `MetricCard` Categorías (DataGrid `GridCategorias` 170×78px).

**Estilos en `UserControl.Resources`:**
- `BtnBase` con `ControlTemplate` + `CornerRadius="6"` + triggers hover/pressed.
- `ModernInput` (TextBox): ControlTemplate con `CornerRadius="6"`, `PART_ContentHost`.
- `MetricCard` (Border): `CornerRadius="8"`, `Padding="10,8"`.
- `LblMuted`, `SectionHeader`: igual al patrón de PedidosDetalle.

**Code-behind destacado:**
- `ActualizarBadgeEstado()`: badge de estado (pendiente=amarillo, pendiente revisar=rojo, entregado=verde); ícono dinámico (SA=naranja, EN=verde); `LblSucursalTipo` dinámico ("Sucursal destino"/"Sucursal origen"); `LblDocNum` actualizado. Llamado desde `CargarParaEditar`, `CargarParaNuevo`, `Campo_SelectionChanged` (when `Box_Estado`), `CboMovimiento_SelectionChanged`.
- `CargarTotales()`: ya no usa categorías fijas (Peq/Med/Gra/Otros); agrupa por categoría real de la DB y asigna `CategoriaCantFila` a `GridCategorias.ItemsSource`.
- **Validación sucursal activa**: `Box_Sucursal_Identificador_TextChanged` y `BtnBuscarSucursal_Click` bloquean seleccionar `AppState.SucursalActiva` como destino/origen (muestra advertencia y limpia el campo).

#### CorreccionesDetalle — rediseño completo
**Estructura XAML (5 filas):**
- **Row 0 — Encabezado**: ícono dinámico (EG/IN) + título + `LblDocNum` + `BadgeEstado` + Guardar/Cancelar; campos en línea: Doc. Corrección · Fecha · Hora · Movimiento · Motivo · Referencia.
- **Row 1 — Toolbar**: label + botones artículos.
- **Row 2 — GridItems**: DataGrid a todo el ancho.
- **Row 3 — Observación** (altura 90): `Box_Observacion` en borde redondeado.
- **Row 4 — Barra de totales**: MetricCard Unidades + Diferentes + GridCategorias.

**Code-behind:**
- `ActualizarBadge()`: ingreso=verde/"IN", egreso=rojo/"EG"; actualiza ícono, badge y `LblDocNum`. Llamado al cargar y en `Box_Movimiento_SelectionChanged`.
- `LblDocNum` se actualiza en vivo en `Campo_TextChanged` cuando `sender == Box_DocumentoC`.

#### InventariosDetalle — rediseño completo
**Estructura XAML (5 filas):**
- **Row 0 — Encabezado**: ícono fijo "IV" (azul `#1E40AF` / fondo `#DBEAFE`) + título + `LblDocNum` + badge fijo "Inventario físico" (azul) + Guardar/Cancelar; campos en línea: Doc. Inventario · Fecha · Hora.
- **Row 1 — Toolbar**: label + botones artículos.
- **Row 2 — GridItems**: DataGrid a todo el ancho.
- **Row 3 — Observaciones** (altura 90): `Box_Observacion` en borde redondeado.
- **Row 4 — Barra de totales**: MetricCard Unidades + **Diferentes** (nuevo) + **GridCategorias** (nuevo).

**Code-behind:**
- Nuevo `CargarTotalesCategoria()`: mismo patrón que CorreccionesDetalle — itera `Sql.CategoriasObj`, acumula por artículo, añade fila "Otros", asigna a `GridCategorias.ItemsSource`.
- `RefrescarGrid()` ahora también calcula `TxtUnidadesDiferentes` y llama `CargarTotalesCategoria()`.
- `LblDocNum` actualizado en `CargarUserform()` y en `Campo_TextChanged` cuando `sender == Box_DocumentoI`.

#### Sistema de estilos compartido (TraspasosDetalle → Correcciones → Inventarios)
Todos comparten el mismo modelo visual:
```
Encabezado surface   [ícono][título + doc]  [badge]  [Cancelar][Guardar]
                     [campo1][campo2]...[campoN]
─────────────────────────────────────────────────────────────────────────
Toolbar surface      Artículos de...         [btn][btn][btn][btn][peligro]
─────────────────────────────────────────────────────────────────────────
GridItems (*)        Lín. | Código | Descripción | Cantidad
─────────────────────────────────────────────────────────────────────────
Panel inferior       Observación / Stock + Observaciones
─────────────────────────────────────────────────────────────────────────
Totales surface      [MetricCard Unidades] [MetricCard Diferentes] [MetricCard Categorías]
```
## Comandos Importantes

### Clonar y abrir el proyecto
```bash
git clone <repo-url>
cd WpfAppVba
git checkout sistemaControl_1.5
# Abrir WpfAppVba/WpfAppVba.csproj en Visual Studio 2022
```

### Compilar
```bash
cd WpfAppVba
dotnet build WpfAppVba.csproj
```

### Ejecutar
```bash
cd WpfAppVba
dotnet run --project WpfAppVba.csproj
# O desde Visual Studio: F5 / Ctrl+F5
```

### Git (rama activa)
```bash
git checkout claude/confident-curie-t2etpj
git pull origin claude/confident-curie-t2etpj
git push -u origin claude/confident-curie-t2etpj
```

### Restaurar paquetes NuGet (si es necesario)
```bash
cd WpfAppVba
dotnet restore
```
