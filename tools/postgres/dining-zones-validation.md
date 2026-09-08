# Fase de zonas de Dining

La migración `20260907120000_AddDiningZones` crea `dining_zones`, añade
`restaurant_tables.ZoneId` inicialmente nullable y asigna todas las ubicaciones
existentes a una zona `General` por restaurante. Después exige `ZoneId` y crea
una clave foránea compuesta `(RestaurantId, ZoneId)`. No cambia los identificadores
de mesas, códigos QR, sesiones ni pedidos. EF ejecuta la migración en su transacción
habitual; no debe ejecutarse el SQL de relleno por separado.

Los restaurantes nuevos sin ubicaciones crean su primera zona desde Backoffice.
El seed de desarrollo crea `General` antes de insertar las mesas. El nombre de
zona es único por restaurante y distingue mayúsculas como los índices de texto
existentes. El orden admite enteros no negativos; los empates se ordenan por nombre.

La migración adicional `20260907130000_RemoveLocationTypes` elimina la columna
obsoleta `LocationType`, sin cambiar zonas, nombres, códigos QR ni sesiones.
Se mantiene separada para soportar bases donde ya se haya aplicado la migración
anterior. La jerarquía es restaurante → zona → ubicación, sin subtipos: «Puesto 1»
es un nombre libre. Todas las ubicaciones admiten QR propio, incluso si se llaman
puestos o pertenecen a una zona llamada Barra. Renombrar o cambiar de zona conserva
el QR; solo la acción explícita de regenerarlo cambia su código.
Los pedidos nuevos en el local usan `DineIn`; se acepta `Bar` por compatibilidad
con pedidos históricos, sin clasificar ubicaciones ni deducir nada de sus nombres.
## API

Se reutilizan `tables.read` y `tables.manage`:

- `GET /api/dining/restaurants/{restaurantId}/zones`: listado por orden y nombre.
- `POST /api/dining/restaurants/{restaurantId}/zones`: `{ "name": "Terraza", "sortOrder": 10 }`.
- `PUT /api/dining/restaurants/{restaurantId}/zones/{zoneId}`: mismo cuerpo.
- `DELETE /api/dining/restaurants/{restaurantId}/zones/{zoneId}`: solo zonas sin mesas,
  incluidas las inactivas. Devuelve 409 si quedan ubicaciones.

Crear y editar mesas requiere `zoneId` del mismo restaurante. El listado de mesas
añade `zoneId`, `zoneName` y `zoneSortOrder` y elimina `locationType`.
La asignación a zona no modifica la sesión activa ni el QR. Una ubicación ocupada no se puede desactivar.

Backoffice permite mantener zonas y asignar ubicaciones. TPV y Comandero agrupan
las ubicaciones recibidas en cada carga únicamente por zona; TPV conserva el filtro de estado.
Los cambios de configuración se reflejan al volver a cargar las mesas; esta fase
no añade un evento de configuración a SignalR. Delivery no se representa con
mesas ni con zonas especiales.

## Comprobaciones pendientes al autorizar compilación y ejecución

1. Compilar Dining y las tres PWA, y comprobar que EF no detecta diferencias entre
   el modelo y el snapshot.
2. Aplicar ambas migraciones a una copia con mesas de varios restaurantes, incluyendo
   ubicaciones inactivas y sesiones abiertas. Comparar antes y después IDs, QR,
   sesiones y pedidos; comprobar que cada mesa tiene una zona de su restaurante.
3. Aplicar todas las migraciones en una base vacía y ejecutar el seed dos veces.
   Debe mantenerse una sola zona General por restaurante, sin duplicar mesas.
4. Crear, renombrar y ordenar zonas en Backoffice. Rechazar nombres vacíos,
   mayores de 80 caracteres, duplicados por local y órdenes negativos.
5. Intentar asignar una mesa a una zona ajena o inexistente: debe rechazarse.
   Probar permisos de lectura, gestión y acceso a otros restaurantes.
6. Intentar eliminar una zona con mesas activas o inactivas: 409. Reasignarlas,
   guardar y eliminar la zona vacía: 204. Verificar también el conflicto ante
   asignación y eliminación concurrentes.
7. Escanear el QR de una ubicación antes llamada puesto de barra. Renombrarla y moverla de zona; el mismo QR debe seguir identificándola.
8. Recargar TPV y Comandero: comprobar orden de zonas, filtros, selección de mesa,
   sesiones abiertas y conservación de los flujos QR y para llevar.

Al revertir RemoveLocationTypes se restaura la columna con el valor Table para todas las ubicaciones; no se recupera la clasificación anterior.

Al revertir AddDiningZones se eliminan las zonas y su asignación, conservando las mesas
y sesiones. Solo se debe probar en una copia desechable; pierde la distribución
de zonas configurada después de migrar.

## Eliminación de ubicaciones e historial

`20260907140000_AddTableSoftDeletion` añade `DeletedAtUtc` y restringe la unicidad
(RestaurantId, Code) a ubicaciones no eliminadas. El botón Eliminar llama a
`DELETE /api/dining/tables/{tableId}` con permiso `tables.manage`. Solo se permite
sin sesión Open, incluso cuando esa sesión todavía no tiene pedidos. La operación
conserva la fila original y sus sesiones; no borra ventas ni cambia TableId.
La apertura por QR, TPV o Comandero y la eliminación bloquean la misma fila durante
su transacción para resolver intentos simultáneos. Las modificaciones y la rotación
del QR también toman ese bloqueo. Las ubicaciones eliminadas no se listan, no se
pueden editar o reactivar y su QR deja de abrir sesiones. El DELETE es idempotente.
Los clientes operativos reflejan el cambio al recargar sus mesas.

Crear otra MESA001 produce un nuevo ID y QR. Los pedidos y sesiones conservan sus
TableId originales. Un informe por mesa debe agrupar por RestaurantId y TableId,
mostrando código y, para distinguir códigos reutilizados, fecha de alta/baja o ID.
Los totales globales del restaurante sí incluyen las ventas de ambas mesas.
Reporting todavía no ofrece desglose por mesa; este cambio conserva las identidades
necesarias, sin añadir dicho informe. Las zonas sin ubicaciones actuales se eliminan lógicamente; las referencias históricas se conservan.
El seed no recrea ubicaciones eliminadas. La reversión de esta migración se rechaza
si hay ubicaciones eliminadas, para no revivirlas ni mezclar códigos reutilizados.

Validación pendiente de ejecución (sin compilar en esta tarea):

- Intentar eliminar con sesión abierta, también vacía: 409.
- Cerrar la sesión y eliminar: 204; pedidos/sesiones e ID original permanecen.
- Recargar Backoffice, TPV y Comandero: no aparece; QR antiguo y edición: 404.
- Crear una ubicación con el mismo código: nuevo ID/QR, historial anterior intacto.
- Repetir DELETE: 204; intentar DELETE sin permisos o desde otro restaurante: 403.
- Abrir por QR/TPV y eliminar simultáneamente: solo puede prevalecer una operación;
  nunca debe quedar una sesión abierta sobre una ubicación eliminada.
- Ejecutar el seed dos veces tras eliminar una ubicación: no recrearla ni duplicar QR.
## Zonas vacías con ubicaciones históricas

`20260907150000_AddZoneSoftDeletion` añade DeletedAtUtc a las zonas. El borrado
comprueba exactamente las ubicaciones sin eliminar (activas o inactivas), igual
que el contador del Backoffice. Las ubicaciones eliminadas no bloquean la acción:
la zona desaparece del listado y de los selectores, pero permanece en la base con
su ID y sus referencias históricas. No se borran físicamente mesas ni sesiones.
El nombre queda disponible para otra zona con un ID nuevo. No se permite editar
ni asignar ubicaciones a zonas eliminadas. El seed no recrea una General eliminada.

La asignación de ubicaciones y el borrado de zona bloquean la misma fila dentro
de una transacción. Una petición concurrente no puede dejar una ubicación actual
en una zona eliminada. DeletedAtUtc es token de concurrencia para impedir que una
edición antigua modifique una zona eliminada. Repetir DELETE devuelve 204.

Pendiente de comprobar en ejecución, después de compilar y migrar:

1. Zona con cero ubicaciones actuales y varias eliminadas: DELETE devuelve 204;
   desaparece de Backoffice, las ubicaciones históricas mantienen ZoneId.
2. Zona con una ubicación activa o inactiva sin eliminar: DELETE devuelve 409.
3. Crear otra zona con el mismo nombre: obtiene otro ID; ambas historias permanecen.
4. Asignar una mesa o editar una zona eliminada: rechazo; ningún cambio de historial.
5. Asignar mesa y eliminar zona simultáneamente: una operación prevalece, sin
   ubicaciones actuales en zonas eliminadas.
6. Repetir el seed: no recrea General si se eliminó, ni duplica nombres vigentes.
## Cantidad de comensales

`20260907160000_AddSessionGuestCount` añade RequestGuestCount a cada ubicación y
RequestGuestCount/GuestCount a la sesión. La opción «Solicita cantidad de comensales
antes de pedir» se configura en Backoffice al crear o editar cualquier ubicación.
No depende del nombre de la zona ni del nombre de la ubicación. Inicialmente está
desactivada; se activa individualmente donde se necesite. Las sesiones existentes
quedan sin obligación de introducir datos. Las nuevas copian la configuración al
abrirse, y los cambios posteriores en Backoffice afectan a la siguiente sesión.

TPV, Comandero y cliente QR muestran una entrada numérica si la sesión lo solicita.
Antes de crear el primer pedido guardan el número mediante
PUT /api/dining/sessions/{sessionId}/guests con { "guestCount": 4 }. El número
permanece en la sesión y se muestra al regresar. Se valida un entero de 1 a 999;
null significa desconocido/no solicitado, nunca cero comensales. No se pide en
para llevar ni en ubicaciones donde no esté activada la opción.

El endpoint requiere permiso orders.create del restaurante o el token de esa sesión
CustomerQr. Bloquea la sesión durante la escritura; no permite cambiar una cantidad
ya registrada por otra distinta ni modificar sesiones cerradas. El endpoint de
validación usado por Orders rechaza crear pedidos cuando la cantidad es obligatoria
y falta. No se confía únicamente en el formulario. La cantidad se guarda por sesión,
no se suma de nuevo en cada pedido. No se añade aún un informe de comensales.

Comprobaciones pendientes tras compilar y aplicar la migración:

- Activar la opción, abrir una sesión desde cada uno de los tres clientes e introducir
  comensales; comprobar persistencia al regresar y ausencia de una segunda pregunta.
- Intentar crear un pedido por API sin registrar cantidad: rechazo en sesión obligatoria.
- Probar sin cantidad, cero, negativos y valores superiores a 999; no deben crear pedidos.
- Desactivar la opción en un puesto y abrir otra sesión: no se solicita cantidad.
- Cambiar la opción con una sesión abierta: solo afecta a la siguiente sesión.
- Token QR ajeno, permiso de otro restaurante y sesión cerrada: no permiten escribir.
- Dos dispositivos que intentan cantidades distintas: conservar la primera confirmada.
- Reabrir la misma ubicación tras cerrar su sesión: empieza otra cantidad independiente.
## Cabecera del pedido y KDS

GuestCount se copia desde la sesión validada a orders al crear el pedido. El cliente
no puede suministrar otra cantidad en CreateOrderRequest. Dining mantiene HTTP 204
en /validate y adjunta X-Dining-Guest-Count cuando existe, por lo que Payments
conserva su protocolo de validación. Orders obtiene la cantidad de esa respuesta.

Todos los eventos de pedido/ticket transportan GuestCount. La proyección lo guarda
en kitchen_order_views y la API lo devuelve en la cabecera de OrderResponse.
KDS muestra «👥 4» en las tarjetas, con título y etiqueta accesible «4 comensales».
Si GuestCount es null, no aparece el indicador. Las migraciones AddOrderGuestCount
son independientes para Orders Write y Read; el histórico queda null, sin inventar
cantidades. El dato de cada pedido es una copia del de su sesión, por lo que los
informes de comensales deben deduplicar por DiningSessionId en la base de pedidos,
no sumar GuestCount de todos los pedidos de una misma mesa/sesión.

Validación pendiente al autorizar compilación y ejecución:

- Crear un pedido con cuatro comensales desde TPV, Comandero y QR: comprobar la
  sesión, orders.GuestCount, eventos, kitchen_order_views.GuestCount y tarjeta KDS.
- Cambiar de estación/estado, despachar y cancelar una línea: conservar el indicador.
- Emitir varios pedidos en la misma sesión: cada cabecera contiene la misma cantidad.
- Ubicación sin solicitud y pedido histórico: null y sin indicador, no cero ficticio.
- Confirmar que Payments sigue aceptando la validación HTTP 204 del cliente QR.
## Consulta QR sin ocupar la ubicación

GET /api/dining/qr/{qrCode} valida el QR activo y la red configurada y devuelve la
ubicación y política para consultar la carta. No abre sesiones, no exige caja abierta,
no publica ocupación y no modifica BD. Recupera una sesión CustomerQr únicamente
si el token enviado coincide; de lo contrario devuelve session=null, sin datos de
la cuenta existente. El personal puede seguir atendiendo sesiones desde sus clientes.

La PWA usa GET al cargar o reintentar la página. Solo al confirmar un pedido con
productos usa POST /qr/{qrCode}/sessions. En ese momento se comprueba la caja,
la ocupación y la cantidad obligatoria de comensales (guestCount en query), que se
guarda al crear la sesión. El token se persiste inmediatamente al recibirla, antes
de crear o pagar el pedido. La consulta de estados solo comienza con sesión.
No se usa el cierre de pestaña para liberar mesas, ni se exponen cuentas de terceros.

La apertura de sesión y la creación/pago del pedido siguen siendo pasos distribuidos:
si falla un paso posterior a la confirmación, la sesión se conserva para reintentar.
No se libera automáticamente porque el pedido puede haberse creado aunque se pierda
la respuesta. Las sesiones vacías creadas antes de este cambio tampoco se cancelan
automáticamente; el personal puede cancelar las que compruebe que están sin uso.

Pendiente de validación en ejecución:

- Escanear varias veces y cerrar sin confirmar: ninguna sesión nueva ni ocupación.
- Consultar con caja cerrada: carta disponible; confirmar exige caja abierta.
- Cantidad obligatoria inválida: no abrir sesión.
- Confirmar, recargar con el mismo token: recuperar la sesión, sin duplicarla.
- Escanear desde otro navegador una mesa ocupada: carta sin acceso a la cuenta;
  confirmar rechaza una segunda ocupación.
- Pedir desde Comandero/TPV sobre la sesión existente: usar la misma cuenta.
- QR eliminado/inactivo o red no autorizada: rechazo incluso al consultar.