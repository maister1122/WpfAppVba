# Estructura de la base de datos — `edberBase`

> Generado a partir del script SQL Server provisto por el usuario (`script.sql`,
> fecha de script 13/08/2026). Refleja el estado real de la base, no una
> suposición del código — ante cualquier diferencia, este archivo debe
> actualizarse con el próximo `script.sql` que se comparta, junto con
> `SistemaGestion/EsquemaValidator.cs` y `VisorEmpresa/EsquemaValidator.cs`
> (que validan esta misma estructura al conectar un servidor).

## Convenciones generales

- **Motor**: SQL Server (compatibility level 160). Cada tabla es `PRIMARY KEY`
  sobre `id`, la mayoría `NONCLUSTERED` (cuatro — `documentosF`, `documentosL`,
  `facturas`, `transaccionesF` — son `CLUSTERED`).
- **`id`** — `uniqueidentifier NOT NULL`, en todas las tablas. Default `newid()`
  vía constraint `DF_<tabla>_id`, en las 26 sin excepción. Dos constraints
  conservan el nombre de la tabla vieja: `appsheets` usa `DF_stocks_id` y
  `transaccionesP` usa `DF_transacciones_id`.
- **`estadof`** — `nvarchar(100) NULL`, en todas las tablas. Estado lógico
  interno de la fila: `normal` / `nuevo` / `editado` / `ocultado` / `eliminado`.
  La app **nunca hace `DELETE` físico** en el flujo normal — todo borrado es
  una actualización de `estadof` (ver `DataConsulta.ExportarItemsInterno`).
  La única excepción es la herramienta de administración
  `RegistrosOcultosPurgador.PurgarTodos()` (botón "🗑️ Eliminar ocultos" en
  VisorEmpresa), que sí hace `DELETE` físico e irreversible de las filas
  `ocultado`/`eliminado` de la empresa activa — ver más abajo.
- **`codigo`** — presente en la mayoría de las tablas maestras y de documentos.
  Es el número/código visible para el usuario. Desde la sesión 2026-07-29
  **ningún documento usa signo**: el código es el correlativo pelado dentro de
  su ámbito, y hay dos lados que deben coincidir siempre:
  - `CodigoDocumento` — calcula el código de un documento **nuevo** (MAX + 1) y
    lo vuelve a revisar al guardar (si otro usuario tomó el número mientras se
    editaba, avisa y usa el siguiente libre).
  - `CodigoRegenerator.RegenerarTodos()` — **renumera** lo existente 1..N.
    Exige una **empresa activa** (`AppState.EmpresaActiva`).
  Los cubos de movimiento con los que se agrupan los correlativos viven en
  `MovimientoSql` (única fuente de verdad, compartida por los dos lados).
  Ámbitos:
  - Tablas maestras (`usuarios`, `familias`, `productos`, `categorias`,
    `industrias`, `terceros`, `sucursales`, `regiones`, `empresas`): `int`,
    numeración 1..N ordenada por `descripcion` (excepto `usuarios`, que no
    tiene esa columna y se ordena por `apellidos, nombres`). Son catálogos
    globales de toda la base: no se filtran por empresa.
  - `documentosP`/`documentosC`/`documentosF`: `int`, correlativo 1..N
    **partido por tipo de movimiento** (`documentosP`: venta/compra;
    `documentosC`: repuesta/retirado; `documentosF`: ingreso/egreso —
    normalizando vocabulario viejo al nuevo, mismo criterio que
    `NormalizarMovimiento` en cada `*General`), ordenado por fecha, filtrado
    a la empresa activa vía cascada `sucursal` → `sucursales.empresa`.
  - `documentosI`: `int`, correlativo 1..N sin partición, ordenado por
    fecha, filtrado a la empresa activa vía `sucursal` → `sucursales.empresa`.
  - `documentosT`: `int`, correlativo 1..N sin partición, ordenado por
    fecha, filtrado a la empresa activa vía cascada `emitido` → `sucursales` →
    `empresas`.
  - `documentosL`: `int`, correlativo 1..N sin partición, ordenado por
    fecha, filtrado a la empresa activa (columna `empresa` directa,
    comparación de texto contra el id).
  - **`articulos.codigo` es la única excepción**: sigue siendo `nvarchar(100)`
    porque lo escribe el usuario a mano y admite letras. El regenerador no lo
    toca.
- **Columnas `uniqueidentifier` que no son `id`** son claves foráneas
  (apuntan al `id` de otra tabla) — no hay `FOREIGN KEY` declaradas en el
  script, la integridad se maneja desde la app.
- **`appsheets`** es la única tabla que **no** pasa por la caché genérica de
  `SqlData`/`DataConsulta` (`ObtenerItem`/`EstablecerItem`/`OrdenarData`) — se
  accede con SQL directo desde `AppsheetsSync.cs`. Por eso no forma parte del
  manifiesto de `EsquemaValidator` (que valida solo las tablas cacheadas).

## Cambios respecto a la versión anterior de este documento (sesión 2026-07-24)

- **`secuencia`**: eliminada de **todas** las tablas (ya no existe en ningún
  `CREATE TABLE` del script). El código ya no depende de ella para nada — el
  orden de las tablas maestras pasó a `descripcion` (`apellidos, nombres` en
  `usuarios`).
- **`documentosP.emitido`**: eliminada. Era idéntica a `documentosP.sucursal`
  (ambas se seteaban con `AppState.SucursalActiva` al crear el pedido) — el
  código ya solo usa `sucursal`.
- **`articulos.estadoV`**: eliminada, consolidada en `articulos.estado`
  ("mostrar"/"ocultar" — filtro de visibilidad en las plantillas Excel/PDF de
  Precios/Inventarios). El código que antes leía/escribía `estadoV` ahora usa
  `estado`.
- **`usuarios.emision` / `usuarios.edicion`**: agregadas (antes `usuarios` no
  las tenía). `VisorEmpresa/UsuariosDetalle.xaml.cs` ya las escribía al crear
  un usuario nuevo — con esto la columna finalmente existe en la base.
- **`pedidos.forma` / `pedidos.contable`**: eliminadas, sin reemplazo — ya no
  se usan en ningún lado.
- **`documentosF` y `transaccionesF` eliminadas por completo.** Facturas dejó
  de ser un documento propio (con cabecera, tercero, fecha, estado, etc.) y
  pasó a ser una línea más colgada directamente de `documentosP`, igual que
  `pedidos`/`transacciones`/`entregas`. `facturas.documentoF` se renombró a
  `facturas.documentoP`. Las pantallas `FacturasGeneral`/`FacturasDetalle`
  (ambos proyectos) se eliminaron; ahora hay una pestaña "Facturas del
  pedido" dentro de `PedidosDetalle`, ubicada a la derecha de "Artículos del
  pedido".
- **`facturas.forma` renombrada a `facturas.estado`** (`nvarchar(100)`),
  valores "con deuda"/"sin deuda". Una factura "con deuda" suma su importe al
  Saldo del pedido; "sin deuda" no. Nuevo botón **"Facturar pedido"** en esa
  pestaña: agrupa las líneas de artículos del pedido por categoría y genera
  una línea de factura por categoría (concepto = descripción de la categoría,
  importe = suma de esa categoría) con estado "sin deuda" — se puede volver a
  presionar para recalcular; no toca las líneas "con deuda" agregadas a mano.
- **`documentosP.estadoA`**: agregada (`nvarchar(100)`, "sin factura"/"con
  factura"). Es un estado más para el pedido, igual que `estado` (entrega) y
  `estadoC` (cuenta): se recalcula solo, sin intervención manual, según si el
  importe total facturado (`Importe facturado`, suma de las líneas de la
  pestaña "Facturas del pedido") es mayor que cero. Se muestra como badge
  "Estado de factura" en el encabezado de `PedidosDetalle`. En
  `PedidosGeneral`, reemplaza a la columna "Referencia" en `Grid1` (badge
  "Factura") y suma un nuevo filtro lateral ("Todos"/"Con factura"/"Sin
  factura").
- **Permiso de eliminar en `documentosP`/`documentosT`/`documentosI`/
  `documentosC`**: además del administrador, ahora también puede
  eliminar/ocultar un documento el usuario que figura en su columna
  `usuario` (el creador). Para que esto sea confiable se corrigió un bug en
  Pedidos e Inventarios: su `GuardarEditar` sobrescribía `usuario` en cada
  edición (perdiendo el creador original) — ahora, igual que ya hacían
  Traspasos y Correcciones, solo se actualiza `usuarioE` al editar y
  `usuario` queda fijo desde que se crea el documento.
- **`usuarios.temaC`** (sesión 2026-07-27): pedido explícito del usuario de
  sacarla, ya que ninguna de las dos apps la usa — el tema visual pasó a
  persistirse 100% local (`ThemeManager`/`TemaVisor`, `theme.txt` por PC) hace
  varias sesiones, dejando esta columna sin lectura ni escritura desde el
  código. Se sacó del manifiesto de `EsquemaValidator.cs` (los dos proyectos)
  para que dejar de tenerla no rompa el login. **La columna en sí todavía
  existe en la base** (no hay forma de correr DDL contra SQL Server desde este
  entorno) — para borrarla de verdad, correr en el SQL Server real:
  ```sql
  ALTER TABLE usuarios DROP COLUMN temaC;
  ```
  Se puede hacer en cualquier momento sin coordinar con un release de la app
  (ya no la lee ni la escribe ningún código en producción).

## Cambios respecto a la versión anterior de este documento (sesión 2026-07-28)

Se revirtió el cambio de la sesión 2026-07-24 que había disuelto Facturas dentro
de Pedidos. Las facturas vuelven a ser un documento propio.

- **`documentosF` y `transaccionesF`: vuelven a existir**, con las mismas
  columnas que antes. Vuelven también las pantallas `FacturasGeneral` /
  `FacturasDetalle` (los dos proyectos) y la entrada "🧾 Facturas" del panel
  lateral.
- **`documentosF.relacion`** (`uniqueidentifier`): columna **nueva**, no existía
  en la versión anterior de la tabla. Apunta al `documentosP` (pedido) que la
  factura factura. En `FacturasDetalle` se carga con el campo "Pedido"
  (se escribe el código del pedido y se resuelve a su `id`, igual que el campo
  "Tercero"), y `FacturasGeneral` la muestra en la columna "Pedido".
- **`facturas.documentoP` vuelve a ser `facturas.documentoF`**: las líneas de
  concepto/importe vuelven a colgar de la cabecera de la factura, no del pedido.
- **`transacciones` renombrada a `transaccionesP`** (mismas columnas). Es el
  contraparte de `transaccionesF`: cobros/pagos de `documentosP`.
- **Pedidos ya no sabe nada de facturas**: se eliminaron la pestaña "Facturas
  del pedido" de `PedidosDetalle` (con su botón "Facturar pedido" y el contador
  "Importe facturado"), el badge "Estado de factura", y en `PedidosGeneral` la
  columna "Factura" y el filtro lateral por factura. El saldo del pedido vuelve
  a ser `importe total − cobros`, sin sumarle facturas.
- **`documentosP.estadoA`**: sigue existiendo en la base pero **la app ya no la
  lee ni la escribe** (era el estado "sin factura"/"con factura" que calculaba
  la pestaña eliminada). Se sacó del manifiesto de `EsquemaValidator`. Se puede
  borrar cuando se quiera, sin coordinar con un release:
  ```sql
  ALTER TABLE documentosP DROP COLUMN estadoA;
  ```
- **`facturas.estado`** (`nvarchar(100)`, "con deuda"/"sin deuda"): queda en la
  base pero sin uso — era de la pestaña eliminada. `FacturasDetalle` no la
  lee ni la escribe.
- **`pedidos.forma` / `pedidos.contable`**: el script las sigue trayendo (nunca
  se llegaron a borrar en el SQL Server real). La app no las usa desde la sesión
  2026-07-24; borrarlas es opcional.
- **`usuarios.temaC`**: ya no está en el script — el `DROP COLUMN` pendiente de
  la sesión anterior efectivamente se corrió.

## Cambios respecto a la versión anterior de este documento (sesión 2026-07-29)

- **`CodigoRegenerator.RegenerarTodos()` — se eliminaron todos los signos y se
  agregó el filtro por empresa activa.** Antes `documentosI/P/C` usaban signo
  de sucursal + correlativo por sucursal, `documentosT`/`documentosL` signo de
  empresa + correlativo por empresa, y ninguno distinguía tipo de movimiento.
  Ahora, dentro de una única transacción y exigiendo
  `AppState.EmpresaActiva` no vacía (si no hay empresa activa, lanza
  `InvalidOperationException` antes de tocar la conexión):
  - `documentosP`/`documentosC`/`documentosF` — correlativo 1..N **partido por
    movimiento** (venta/compra, repuesta/retirado, ingreso/egreso
    respectivamente), filtrado a la empresa activa.
  - `documentosI`/`documentosT`/`documentosL` — correlativo 1..N sin
    partición, filtrado a la empresa activa. No se toca el resto de la base
    (otras empresas conservan su numeración).
  - Ver el bloque de comentarios en `CodigoRegenerator.cs` (ambos proyectos)
    para el detalle exacto de cada tabla. El texto de confirmación del botón
    en `VisorEmpresa/ConsolaVisor.xaml.cs` (`BtnRegenerarCodigos_Click`) se
    actualizó para reflejar esto y para mencionar `documentosF` (antes faltaba
    en la lista, aunque el código ya la procesaba).
- **`VisorEmpresa/TraspasosDetalle`**: se sacó el combo "Movimiento" del
  encabezado (entrada/salida ahora se elige solo desde la sección del panel
  lateral, como en el resto de módulos ya divididos). `LblTitulo` pasa a decir
  siempre "Traspasos de Producto" y el ícono de cabecera queda fijo en "TR"
  con relleno blanco, en vez de cambiar de texto/color según el tipo.
- **Nueva herramienta `RegistrosOcultosPurgador.PurgarTodos()`** (ambos
  proyectos; botón "🗑️ Eliminar ocultos" solo en `VisorEmpresa/ConsolaVisor`,
  junto a "Regenerar códigos"/"Recalcular precios"). Primera y única
  operación de la app que hace `DELETE` físico:
  - Borra, de la **empresa activa** únicamente, todas las filas con
    `estadof` en `('ocultado', 'eliminado')` en las 26 tablas de la base
    (líneas de documentos primero, luego cabeceras, luego maestras).
  - Después renumera 1..N sin huecos la columna `indice` de las tablas que
    la tienen (`articulos` por familia; `correcciones`/`entregas`/
    `facturas`/`pedidos`/`traspasos`/`transaccionesF`/`transaccionesP` por
    su documento dueño).
  - Todo en una única transacción (rollback completo ante cualquier error).
    Exige empresa activa, igual que `CodigoRegenerator`. Al terminar cierra
    la sesión (los `indice` cacheados en memoria podrían haber cambiado).
  - **Irreversible**: no hay forma de recuperar lo borrado. Doble
    confirmación en el botón antes de ejecutar.
- **La creación de documentos también dejó de usar signos** (antes solo se
  había cambiado la regeneración, así que un documento nuevo volvía a nacer
  con signo y numerado por sucursal). Nuevos archivos `MovimientoSql.cs` y
  `CodigoDocumento.cs` (ambos proyectos):
  - `MovimientoSql` es la única fuente de verdad de los cubos de movimiento:
    expone cada criterio dos veces (expresión SQL `CASE` para el servidor y
    normalizador C# para la app), y `CodigoRegenerator` pasó a usarlo en vez
    de repetir los `CASE` inline. Así creación y regeneración no pueden
    divergir.
  - `CodigoDocumento` calcula el código de un documento nuevo con el **mismo
    ámbito** que usa el regenerador, y lo **revisa de nuevo al guardar**: si
    el número ya está tomado avisa al usuario y guarda con el siguiente
    libre (antes el código se fijaba al abrir el formulario y se guardaba a
    ciegas). Se llama desde los 10 `GuardarNuevo` de documentos.
  - `documentosP/C/F` pasan a numerar por (empresa, movimiento);
    `documentosT/L/I` por empresa, sin signo. **`documentosI` cambia de
    ámbito**: antes numeraba por sucursal, ahora por empresa, para coincidir
    con el regenerador.
  - En `DataConsulta` se eliminaron `SiguienteNumeroDoc`,
    `SiguienteNumeroDocPorEmpresa` y `SiguienteNumeroDocPorRegion` (sin uso).
  - `empresas.signo`, `sucursales.signo` y `regiones.signo` **siguen en la
    base y se siguen editando** en sus pantallas de mantenimiento; ya no
    intervienen en el código de ningún documento.

## Cambios respecto a la versión anterior de este documento (sesión 2026-08-13)

Cotejo columna por columna del script del 13/08/2026 contra este documento y
contra el manifiesto de `EsquemaValidator`. **La base es compatible con la app**:
las 25 tablas del manifiesto existen con todas sus columnas (0 problemas). Lo
que cambió es la documentación, no el esquema:

- **`facturas.monto`** (`float`): existe en la base desde `ab4844d` y el código
  la usa en los dos proyectos (`FacturasDetalle`, `FacturasGeneral`), pero no
  estaba ni en este documento ni en el manifiesto. **Se agregó al manifiesto de
  `EsquemaValidator` en ambos proyectos**: sin eso, una base sin esa columna
  pasaba la validación y después fallaba en silencio (`ObtenerItem` devuelve
  `null` y `EstablecerItem` es un no-op cuando la columna no existe), mostrando
  el monto facturado en 0 y sin guardarlo.
- **`facturas.estado`**: este documento la listaba como "sin uso"; **ya no
  existe en la base**. Ningún código la referencia.
- **Default de `id`**: las 26 tablas tienen su `DEFAULT (newid())`. La excepción
  que este documento anotaba para `usuarios` ya no corre.
- **PK `CLUSTERED`**: son cuatro (`documentosF`, `documentosL`, `facturas`,
  `transaccionesF`), no dos.
- **`documentosC.movimiento` y `documentosP.movimiento`**: son `nvarchar(100)`,
  no `nvarchar(255)` como decía este documento. Sin impacto (los valores son
  "venta"/"compra", "repuesta"/"retirado").
- **`documentosP.estadoA`, `pedidos.forma` y `pedidos.contable`** siguen en la
  base y siguen sin uso en el código — se pueden `DROP COLUMN` cuando se quiera.
  El `DROP` de `pedidos.forma`/`contable` que este documento daba por hecho en
  la sesión 2026-07-24 nunca llegó a correrse.

- **`codigo` de los 6 documentos pasa de `nvarchar(100)` a `int`**
  (`documentosP`, `documentosC`, `documentosF`, `documentosI`, `documentosL`,
  `documentosT`). Desde la sesión 2026-07-29 el código es siempre el correlativo
  pelado, así que el texto no representaba nada que no fuera un entero y encima
  ordenaba mal ("10" antes que "9"). `articulos.codigo` queda en `nvarchar(100)`
  a pedido explícito del usuario. Las maestras ya eran `int`.
  **El cambio en la base se aplica corriendo `codigo-a-int.sql` (raíz del
  repo), y hasta que se corra este documento va por delante de la base real.**
  Del lado del código solo cambió `CodigoRegenerator`, que ahora emite
  `CAST(rn AS INT)` en vez de `CAST(rn AS NVARCHAR(50))` en las 6 consultas de
  documentos (los dos proyectos). El resto de la app ya era indiferente al tipo:
  lee con `ObtenerItem(...)?.ToString()` y escribe strings numéricos que SQL
  Server convierte solo.

Opciones de la base que conviene tener presentes (del mismo script):
`RECOVERY SIMPLE` (sin recuperación punto-en-el-tiempo: se depende del último
full backup), `FILEGROWTH = 1024KB` en el `.mdf`, y **ni un solo índice fuera de
las PK, ni `FOREIGN KEY`, vistas, procedimientos o triggers** en toda la base.

## Tablas

### `appsheets`
Sincronizada con `articulos` para la integración externa AppSheets (no forma
parte de la caché `SqlData`, ver `AppsheetsSync.cs`).

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| estadof     | nvarchar(100)       | sí   |
| emision     | datetime            | sí   |
| id          | uniqueidentifier    | NO   |
| sucursal    | uniqueidentifier    | sí   |
| articulo    | uniqueidentifier    | sí   |
| usuario     | uniqueidentifier    | sí   |
| empresa     | uniqueidentifier    | sí   |

### `articulos`
Catálogo de artículos.

| Columna      | Tipo               | Null | Nota |
|--------------|---------------------|------|------|
| descripcion  | nvarchar(255)       | sí   | |
| indice       | int                 | sí   | orden dentro de la familia (auto, `RecalcularIndicePorFamilia`) |
| modelo       | nvarchar(255)       | sí   | |
| observacion  | nvarchar(255)       | sí   | |
| estado       | nvarchar(100)       | sí   | "mostrar"/"ocultar" — visibilidad en plantillas (ex `estadoV`) |
| estadof      | nvarchar(100)       | sí   | |
| emision      | datetime            | sí   | |
| edicion      | datetime            | sí   | |
| codigo       | nvarchar(100)       | sí   | |
| id           | uniqueidentifier    | NO   | |
| categoria    | uniqueidentifier    | sí   | FK → categorias |
| familia      | uniqueidentifier    | sí   | FK → familias |
| industria    | uniqueidentifier    | sí   | FK → industrias |
| usuario      | uniqueidentifier    | sí   | FK → usuarios (creó) |
| usuarioE     | uniqueidentifier    | sí   | FK → usuarios (editó) |

### `categorias`
Catálogo de categorías (nombre de tabla en SQL es `categorias`, minúscula; el
manifiesto de `EsquemaValidator` la referencia como `Categorias` — coinciden
igual porque la comparación es sin distinguir mayúsculas).

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| descripcion | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| usuario     | uniqueidentifier    | sí   |
| usuarioE    | uniqueidentifier    | sí   |
| empresa     | uniqueidentifier    | sí   |

### `correcciones`
Líneas de `documentosC` (correcciones de stock).

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| indice      | int                 | sí   | |
| cantidad    | float               | sí   | |
| estadof     | nvarchar(100)       | sí   | |
| id          | uniqueidentifier    | NO   | |
| documentoC  | uniqueidentifier    | sí   | FK → documentosC |
| articulo    | uniqueidentifier    | sí   | FK → articulos |

### `documentosC`
Cabecera de correcciones de stock.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| fecha       | datetime            | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| referencia  | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| movimiento  | nvarchar(100)       | sí   |
| observacion | nvarchar(255)       | sí   |
| motivo      | nvarchar(255)       | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| sucursal    | uniqueidentifier    | sí   |
| usuario     | uniqueidentifier    | sí   | creó (fijo, no se toca al editar) — determina quién puede eliminar/ocultar el documento además del admin |
| usuarioE    | uniqueidentifier    | sí   | editó por última vez |

`movimiento`: "repuesta" suma stock y "retirado" lo resta. Antes fueron
"ingreso"/"egreso" y después "repuesta"/"descarga"; la app sigue leyendo los
valores viejos como su equivalente.

### `documentosF`
Cabecera de facturas.

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| id          | uniqueidentifier    | NO   | |
| codigo      | int                 | sí   | |
| fecha       | datetime            | sí   | |
| emision     | datetime            | sí   | |
| edicion     | datetime            | sí   | |
| estadof     | nvarchar(100)       | sí   | |
| observacion | nvarchar(255)       | sí   | |
| referencia  | nvarchar(255)       | sí   | |
| sucursal    | uniqueidentifier    | sí   | |
| usuario     | uniqueidentifier    | sí   | creó |
| usuarioE    | uniqueidentifier    | sí   | editó por última vez |
| estado      | nvarchar(100)       | sí   | "pendiente"/"entregado" |
| estadoC     | nvarchar(100)       | sí   | estado de cuenta, se recalcula con los cobros |
| movimiento  | nvarchar(100)       | sí   | "ingreso"/"egreso" — criterio de mercadería: una compra entra (ingreso), una venta sale (egreso). Antes "venta"/"compra"; la app sigue leyendo los valores viejos como su equivalente (venta→egreso, compra→ingreso) |
| tercero     | uniqueidentifier    | sí   | FK → terceros |
| relacion    | uniqueidentifier    | sí   | FK → documentosP: el pedido que factura (campo "Pedido" de `FacturasDetalle`) |

### `documentosI`
Cabecera de inventarios.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| observacion | nvarchar(255)       | sí   |
| fecha       | datetime            | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| estadof     | nvarchar(100)       | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| sucursal    | uniqueidentifier    | sí   |
| usuario     | uniqueidentifier    | sí   | creó (fijo, no se toca al editar) — determina quién puede eliminar/ocultar el documento además del admin |
| usuarioE    | uniqueidentifier    | sí   | editó por última vez |
| referencia  | nvarchar(255)       | sí   |

### `documentosL`
Cabecera de listas de precios.

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| id          | uniqueidentifier    | NO   | |
| codigo      | int                 | sí   | |
| fecha       | datetime            | sí   | |
| emision     | datetime            | sí   | |
| edicion     | datetime            | sí   | |
| estadof     | nvarchar(100)       | sí   | |
| observacion | nvarchar(255)       | sí   | |
| usuario     | uniqueidentifier    | sí   | |
| usuarioE    | uniqueidentifier    | sí   | |
| referencia  | nvarchar(255)       | sí   | |
| region      | uniqueidentifier    | sí   | |
| estado      | nvarchar(100)       | sí   | |
| empresa     | nvarchar(255)       | sí   | ⚠ `nvarchar`, no `uniqueidentifier` — funciona porque las consultas comparan contra literales de texto |

### `documentosP`
Cabecera de pedidos (ventas/compras).

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| fecha       | datetime            | sí   | |
| estado      | nvarchar(100)       | sí   | |
| tipo        | nvarchar(100)       | sí   | |
| emision     | datetime            | sí   | |
| edicion     | datetime            | sí   | |
| referencia  | nvarchar(255)       | sí   | |
| estadof     | nvarchar(100)       | sí   | |
| movimiento  | nvarchar(100)       | sí   | |
| observacion | nvarchar(255)       | sí   | |
| estadoC     | nvarchar(100)       | sí   | "pendiente"/"cancelado"/"pendiente parcial" — estado de cuenta |
| estadoA     | nvarchar(100)       | sí   | **sin uso** — quedó de la pestaña "Facturas del pedido" (eliminada); se puede `DROP COLUMN` |
| codigo      | int                 | sí   | |
| id          | uniqueidentifier    | NO   | |
| sucursal    | uniqueidentifier    | sí   | sucursal emisora (única columna de sucursal; `emitido` se eliminó por ser duplicada) |
| usuario     | uniqueidentifier    | sí   | creó (fijo, no se toca al editar) — determina quién puede eliminar/ocultar el documento además del admin |
| usuarioE    | uniqueidentifier    | sí   | editó por última vez |
| tercero     | uniqueidentifier    | sí   | cliente/proveedor |

### `documentosT`
Cabecera de traspasos entre sucursales.

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| fecha       | datetime            | sí   | |
| estado      | nvarchar(100)       | sí   | |
| emision     | datetime            | sí   | |
| edicion     | datetime            | sí   | |
| referencia  | nvarchar(255)       | sí   | |
| estadof     | nvarchar(100)       | sí   | |
| observacion | nvarchar(255)       | sí   | |
| codigo      | int                 | sí   | |
| id          | uniqueidentifier    | NO   | |
| origen      | uniqueidentifier    | sí   | |
| destino     | uniqueidentifier    | sí   | |
| emitido     | uniqueidentifier    | sí   | sucursal que emitió (ver cascada `emitido → sucursales → empresas` en `CodigoRegenerator`) |
| usuario     | uniqueidentifier    | sí   | creó (fijo, no se toca al editar) — determina quién puede eliminar/ocultar el documento además del admin |
| usuarioE    | uniqueidentifier    | sí   | editó por última vez |

### `empresas`

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| id          | uniqueidentifier    | NO   |
| descripcion | nvarchar(255)       | sí   |
| signo       | nvarchar(4)         | sí   |
| observacion | nvarchar(255)       | sí   |
| fecha       | datetime            | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| usuario     | uniqueidentifier    | sí   |
| usuarioE    | uniqueidentifier    | sí   |
| estadof     | nvarchar(100)       | sí   |
| codigo      | int                 | sí   |

### `entregas`
Líneas de entrega de un pedido.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| indice      | int                 | sí   |
| cantidad    | float               | sí   |
| fecha       | datetime            | sí   |
| estadof     | nvarchar(100)       | sí   |
| id          | uniqueidentifier    | NO   |
| documentoP  | uniqueidentifier    | sí   |
| articulo    | uniqueidentifier    | sí   |

### `facturas`
Líneas de `documentosF`.

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| id          | uniqueidentifier    | NO   | |
| indice      | int                 | sí   | |
| concepto    | nvarchar(255)       | sí   | |
| importe     | float               | sí   | lo que corresponde según el pedido |
| monto       | float               | sí   | lo efectivamente facturado — es la columna que muestra el grid |
| estadof     | nvarchar(100)       | sí   | |
| documentoF  | uniqueidentifier    | sí   | FK → documentosF |
| categoria   | uniqueidentifier    | sí   | FK → categorias |

### `familias`

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| descripcion | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| observacion | nvarchar(255)       | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| producto    | uniqueidentifier    | sí   |
| usuario     | uniqueidentifier    | sí   |
| usuarioE    | uniqueidentifier    | sí   |

### `industrias`

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| descripcion | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| usuario     | uniqueidentifier    | sí   |
| usuarioE    | uniqueidentifier    | sí   |
| empresa     | uniqueidentifier    | sí   |

### `inventarios`
Líneas de `documentosI`.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| cantidad    | float               | sí   |
| estadof     | nvarchar(100)       | sí   |
| id          | uniqueidentifier    | NO   |
| documentoI  | uniqueidentifier    | sí   |
| articulo    | uniqueidentifier    | sí   |

### `pedidos`
Líneas de `documentosP`.

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| indice      | int                 | sí   | |
| cantidad    | float               | sí   | |
| importe     | float               | sí   | |
| tipo        | nvarchar(100)       | sí   | |
| estadof     | nvarchar(100)       | sí   | |
| forma       | nvarchar(255)       | sí   | **sin uso** desde la sesión 2026-07-24; se puede `DROP COLUMN` |
| contable    | float               | sí   | **sin uso** desde la sesión 2026-07-24; se puede `DROP COLUMN` |
| id          | uniqueidentifier    | NO   | |
| documentoP  | uniqueidentifier    | sí   | |
| articulo    | uniqueidentifier    | sí   | |

### `precios`
Líneas de `documentosL`.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| precio      | float               | sí   |
| estadof     | nvarchar(100)       | sí   |
| id          | uniqueidentifier    | NO   |
| articulo    | uniqueidentifier    | sí   |
| documentoL  | uniqueidentifier    | sí   |

### `productos`

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| descripcion | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| usuario     | uniqueidentifier    | sí   |
| usuarioE    | uniqueidentifier    | sí   |
| empresa     | uniqueidentifier    | sí   |

### `regiones`

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| descripcion | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| usuarioE    | uniqueidentifier    | sí   |
| usuario     | uniqueidentifier    | sí   |
| signo       | nvarchar(4)         | sí   |
| empresa     | uniqueidentifier    | sí   |

### `sucursales`

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| nit         | nvarchar(255)       | sí   |
| descripcion | nvarchar(255)       | sí   |
| direccion   | nvarchar(255)       | sí   |
| telefono    | nvarchar(255)       | sí   |
| observacion | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| fecha       | datetime            | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| region      | uniqueidentifier    | sí   |
| usuario     | uniqueidentifier    | sí   |
| usuarioE    | uniqueidentifier    | sí   |
| signo       | nvarchar(4)         | sí   |
| empresa     | uniqueidentifier    | sí   |
| tipo        | nvarchar(100)       | sí   |

### `terceros`
Clientes/proveedores.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| nit         | nvarchar(255)       | sí   |
| descripcion | nvarchar(255)       | sí   |
| telefono    | nvarchar(255)       | sí   |
| contacto    | nvarchar(255)       | sí   |
| direccion   | nvarchar(255)       | sí   |
| contacto2   | nvarchar(255)       | sí   |
| telefono2   | nvarchar(255)       | sí   |
| observacion | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| emision     | datetime            | sí   |
| edicion     | datetime            | sí   |
| codigo      | int                 | sí   |
| id          | uniqueidentifier    | NO   |
| usuario     | uniqueidentifier    | sí   |
| usuarioE    | uniqueidentifier    | sí   |
| empresa     | uniqueidentifier    | sí   |

### `transaccionesF`
Cobros/pagos de `documentosF`.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| fecha       | datetime            | sí   |
| descripcion | nvarchar(255)       | sí   |
| indice      | int                 | sí   |
| importe     | float               | sí   |
| forma       | nvarchar(100)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| id          | uniqueidentifier    | NO   |
| documentoF  | uniqueidentifier    | sí   |

### `transaccionesP`
Cobros/pagos de `documentosP` (antes se llamaba `transacciones`).

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| fecha       | datetime            | sí   |
| descripcion | nvarchar(255)       | sí   |
| indice      | int                 | sí   |
| importe     | float               | sí   |
| forma       | nvarchar(255)       | sí   |
| estadof     | nvarchar(100)       | sí   |
| id          | uniqueidentifier    | NO   |
| documentoP  | uniqueidentifier    | sí   |

### `traspasos`
Líneas de `documentosT`.

| Columna     | Tipo               | Null |
|-------------|---------------------|------|
| indice      | int                 | sí   |
| cantidad    | float               | sí   |
| estadof     | nvarchar(100)       | sí   |
| id          | uniqueidentifier    | NO   |
| documentoT  | uniqueidentifier    | sí   |
| articulo    | uniqueidentifier    | sí   |

### `usuarios`

| Columna     | Tipo               | Null | Nota |
|-------------|---------------------|------|------|
| cuenta      | nvarchar(255)       | sí   | login |
| llave       | nvarchar(255)       | sí   | hash de contraseña (`PasswordHasher`) |
| nombres     | nvarchar(255)       | sí   | |
| apellidos   | nvarchar(255)       | sí   | |
| estadof     | nvarchar(100)       | sí   | |
| tipo        | nvarchar(100)       | sí   | rol (admin / otros) |
| codigo      | int                 | sí   | |
| id          | uniqueidentifier    | NO   | |
| sucursal    | uniqueidentifier    | sí   | |
| empresa     | uniqueidentifier    | sí   | |
| emision     | datetime            | sí   | agregada en esta sesión |
| edicion     | datetime            | sí   | agregada en esta sesión |

Sin columnas `descripcion`, `usuario` ni `usuarioE` (a diferencia del resto de
las tablas maestras).
