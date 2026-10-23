# Simulador de pedidos

Herramienta local para generar pedidos en todos los restaurantes configurados a través del Gateway privado. Crea pedidos POS en mesas libres, avanza cada línea por las operaciones normales del KDS, entrega la comanda, registra el cobro y libera la mesa. No accede directamente a la base de datos ni publica eventos por su cuenta.

## Preparar la configuración

1. Copia `simulator.sample.json` como `simulator.local.json`.
2. Incluye los locales que quieras simular.
3. Ajusta los productos máximos, los métodos de pago y las propinas.
4. Configura `OpeningFloat` si quieres un fondo inicial distinto de cero.
5. En cada local, configura `PosAccessToken` y `KdsAccessToken` con cuentas autorizadas para ese local.

`simulator.sample.json` deja vacíos ambos tokens. La herramienta los lee desde `simulator.local.json`, que está excluido de Git; no los solicita por consola ni los escribe en logs. Si falta alguno, informa el campo y el local y se detiene. Si la URL de la API no es local, también se detiene; para otra URL hay que cambiar explícitamente `AllowNonLocalApi` en la configuración.

Para obtener los tokens en el equipo de desarrollo, inicia sesión en cada cliente con una cuenta autorizada para el local, abre F12 → Consola y copia el token correspondiente.

En TPV:

```javascript
copy(JSON.parse(sessionStorage.getItem('restaurantes.pos.login')).AccessToken)
```

En KDS:

```javascript
copy(JSON.parse(sessionStorage.getItem('restaurantes.kds.login')).accessToken)
```

Pega cada valor en `PosAccessToken` o `KdsAccessToken` del local correspondiente. El token de TPV ejecuta también las operaciones de pedidos, caja, pagos y liberación de la mesa; por eso debe tener esos permisos en ese local. El token KDS se usa para consultar y avanzar las estaciones. No se requiere un token independiente para cada API.

## Ejecutar

Desde la raíz del repositorio:

```powershell
dotnet run --project tools/order-simulator/Restaurantes.OrderSimulator -- --config tools/order-simulator/simulator.local.json
```

El `ApiBaseUrl` de ejemplo apunta al Gateway privado de desarrollo (`http://localhost:5001`). Abre los KDS de los locales configurados para observar los cambios. `Ctrl+C` detiene la creación de pedidos nuevos y deja terminar los pedidos que ya están en curso.

Antes de generar pedidos, valida el acceso de TPV y KDS de todos los locales configurados, además de sus menús, mesas y cajas. Si un local no supera la validación, no inicia pedidos en ninguno. Después abre automáticamente la caja de cada local con su `OpeningFloat`; si ya está abierta, la reutiliza. La deja abierta al finalizar.

Durante `RunDurationMinutes` (10 minutos de forma predeterminada), cada local consulta cada segundo las mesas disponibles y lanza pedidos hasta cubrir `OrdersPerBatch` flujos simultáneos (20 por local de forma predeterminada). `MaxConcurrentOrders` limita cuántos pedidos de todos los locales avanzan a la vez (10 de forma predeterminada); entre los locales con pedidos esperando, el simulador elige aleatoriamente cuál avanza, dejando margen para las conexiones de los servicios y otras herramientas locales. Al pagar y liberar una mesa, vuelve a entrar en la rotación. Cuando acaba el tiempo o se pulsa `Ctrl+C`, no inicia más pedidos y espera a que terminen los que siguen activos. No hay límite total de pedidos ni espera de tres minutos entre tandas; el total depende del ritmo al que se completen y liberen mesas. Todos los mensajes de consola incluyen fecha, hora local, milisegundos y zona horaria.

## Comportamiento aleatorio

- Elige productos disponibles del menú de cada local, cantidades de 1 a 3 y excluye los productos de la categoría `Extras` como platos independientes. Solo añade extras configurados para el producto elegido, respetando sus grupos y límites.
- Abre sesiones con una cantidad aleatoria de 2 a 10 comensales cuando la zona la solicita; zonas como barra, que no lo piden, no reciben ese dato.
- Avanza cada línea por los estados visibles del KDS: en preparación y lista. Espera `KitchenStepDelaySeconds` (2 segundos de forma predeterminada) antes de marcarla lista, deja ese mismo intervalo para ver todos los platos listos y espera entre los pases despachados por el KDS principal. Las estaciones con `RequiresPrimaryDispatch` solo se despachan cuando están en `Ready`, en orden de prioridad; las demás se despachan al marcarlas listas. Comprueba que cada plato activo termine `Dispatched` y que el pedido esté `Ready` antes de entregarlo, cobrarlo y liberar la mesa.
- Con `SplitProbability` puede dividir la cuenta entre comensales y pagarla usando `Card`, `Cash` y `Transfer`; los pedidos divididos combinan los tres medios. `Methods.Weight` controla la frecuencia en el resto de pagos.
- Elige una propina positiva de `TipPercentages` en todos los pedidos, la reparte entre los pagos y puede registrar cambio cuando el medio es efectivo. Transferencia siempre se paga exacta, según las reglas de TPV.
- `OrdersPerBatch` controla cuántos pedidos mantiene en rotación cada local. `MaxConcurrentOrders` fija el máximo global de pedidos que ejecutan su ciclo a la vez y selecciona aleatoriamente entre los locales que tienen pedidos esperando. `RunDurationMinutes` determina durante cuánto tiempo acepta pedidos nuevos. Un pedido se paga solo después de confirmar el despacho KDS y el estado `Ready`; al completar el pago se cierra la sesión y la mesa vuelve a estar disponible para la siguiente ronda.
- `KitchenStepDelaySeconds` controla cuánto se muestran los pasos de preparación KDS; admite valores de 1 a 30 segundos.
- `RandomSeed` ayuda a repetir selecciones y repartos durante la depuración; la concurrencia puede cambiar el orden exacto de las operaciones.

Antes de cobrar, el simulador espera a que Dining refleje el pedido como entregado. Después del último pago, espera a que la cuenta quede saldada y `CanRelease` confirme que la mesa ya se puede liberar; entonces cierra la sesión. Consulta estas proyecciones cada dos segundos y sigue esperando hasta que el estado llegue, también durante el drenaje final. Los pagos se registran como operaciones locales de caja; no se envían cargos a un proveedor bancario. `Online` no se ofrece porque la pantalla de cobro del TPV para estas cuentas admite tarjeta, efectivo y transferencia.
