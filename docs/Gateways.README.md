# Gateways YARP

La solución utiliza **YARP** como punto de entrada para las aplicaciones cliente, las APIs HTTP y los hubs SignalR. Los clientes no necesitan conocer las direcciones físicas de los servicios: cargan la interfaz y consumen `/api/*` y `/hubs/*` mediante el mismo gateway.

## Gateways

| Proyecto | Audiencia | Puerto de desarrollo |
| --- | --- | --- |
| `Restaurantes.Gateway.Public` | Tráfico permitido desde clientes externos | `http://localhost:5000` |
| `Restaurantes.Gateway.Private` | Personal del local, administración y oficina | `http://localhost:5001` |

La clasificación público/privado describe el perímetro de red. La existencia de una ruta en YARP no reemplaza las reglas de autenticación, autorización o validación aplicadas por cada servicio.

## Rutas de las interfaces

| Interfaz | Entrada de desarrollo | Gateway |
| --- | --- | --- |
| Customer QR | `http://localhost:5000/qr/{codigo}` | Public |
| Backoffice | `http://localhost:5001/backoffice/` | Private |
| Comandero | `http://localhost:5001/commander/` | Private |
| TPV | `http://localhost:5001/pos/` | Private |
| KDS | `http://localhost:5001/kds/` | Private |
| Dashboard | `http://localhost:5001/dashboard/` | Private |

Cada interfaz conserva la misma URL antes y después de autenticarse. Dashboard, por ejemplo, muestra el formulario de acceso o el panel dentro de `/dashboard/`; no publica una ruta de interfaz independiente `/login/`.

El Private Gateway publica además `/qr/{codigo}` para que Backoffice pueda previsualizar Customer QR dentro del entorno interno. La URL que se imprime o entrega al cliente utiliza el origen del Public Gateway. El flujo completo se explica en **[Dining](Dining.README.md#interfaz-customer-qr)**.

## Destinos internos

Los puertos de los proyectos cliente y de las APIs son destinos internos de YARP, no URLs de navegación. En desarrollo, las interfaces se reenvían así:

| Ruta recibida por el gateway | Destino interno |
| --- | --- |
| `/backoffice/*` | `http://localhost:5500` |
| `/commander/*` | `http://localhost:5200` |
| `/pos/*` | `http://localhost:5400` |
| `/qr/*` | `http://localhost:5300` |
| `/kds/*` | `http://localhost:5700` |
| `/dashboard/*` | `http://localhost:5600` |

Las rutas `/api/*` y `/hubs/*` se dirigen a los servicios correspondientes. En los dominios que separan lectura y escritura, YARP también puede seleccionar clusters diferentes según el método HTTP.

Si cambia un puerto o una dirección interna, se modifica la configuración YARP del ambiente. El código de los clientes continúa utilizando el origen desde el que fue cargado.

## Mismo origen y CORS

El navegador ve una única secuencia:

```text
Navegador -> Gateway -> interfaz/API/SignalR
```

El segundo salto lo realiza YARP en el servidor. Como interfaz, APIs y SignalR comparten el mismo origen visible para el navegador, los gateways no necesitan una política CORS con la topología actual.

CORS sólo debe configurarse si un despliegue hace que el navegador cargue la interfaz desde un origen y consuma la API desde otro origen distinto. Un cambio en las direcciones internas de los clusters no crea por sí mismo una petición cross-origin.

## Configuración de producción

El contrato estable de publicación está formado por los prefijos `/qr/`, `/backoffice/`, `/commander/`, `/pos/`, `/kds/` y `/dashboard/`. En producción se reemplaza el origen local por el dominio HTTPS asignado al gateway correspondiente.

| Interfaz | Ejemplo de producción |
| --- | --- |
| Customer QR | `https://clientes.example.com/qr/{codigo}` |
| Backoffice | `https://operacion.example.com/backoffice/` |
| Comandero | `https://operacion.example.com/commander/` |
| TPV | `https://operacion.example.com/pos/` |
| KDS | `https://operacion.example.com/kds/` |
| Dashboard | `https://oficina.example.com/dashboard/` |

Los dominios son ilustrativos. Dashboard debe permanecer accesible únicamente desde el entorno de oficina y las aplicaciones privadas deben limitarse a las redes autorizadas.

`launchSettings.json` sólo controla el arranque local y no participa en un despliegue publicado. Los destinos `localhost` de los clusters YARP deben sobrescribirse mediante configuración del ambiente, como `appsettings.Production.json`, variables de entorno o configuración del orquestador.

El tráfico exterior debe terminar en HTTPS. YARP puede comunicarse por HTTP con los servicios internos cuando la red de despliegue sea confiable. La infraestructura que se encuentre delante de YARP debe admitir WebSockets para los hubs SignalR.

Mientras se mantengan los prefijos no es necesario modificar los clientes al cambiar dominios, certificados, puertos externos o destinos internos. Cambiar uno de los prefijos sí altera el contrato de publicación y requiere coordinar YARP con la ruta base de la interfaz afectada.

## Observabilidad de gateways y APIs

`AddServiceDefaults()` instrumenta HTTP entrante, llamadas `HttpClient` y el runtime .NET. Las métricas salen por OTLP/gRPC hacia `http://localhost:4317`; `OTEL_EXPORTER_OTLP_ENDPOINT` permite indicar otro Collector. Prometheus recoge `http://otel-collector:9464/metrics` y Grafana consulta esa fuente.

### Dashboards

Los JSON de `tools/grafana/dashboards` se aprovisionan en la carpeta **Restaurantes**. Todos llevan los tags `restaurantes` y `observability`, además de los específicos de su responsabilidad:

| Dashboard | Contenido | Tags específicos |
| --- | --- | --- |
| Entrada · Gateways | Peticiones `/api/*` recibidas por los gateways, respuestas y rankings de rutas por método | `http`, `gateways` |
| Servicios · APIs | Peticiones atendidas por las APIs internas, respuestas y rankings de rutas por método | `http`, `apis` |
| Dependencias · HTTP saliente | Llamadas salientes, destinos y errores de HTTP o transporte | `http`, `dependencies` |
| Runtime · .NET | CPU, memoria, heap, asignaciones, pausas de GC, excepciones y ThreadPool | `dotnet`, `runtime` |
| Flujo de pedidos | Latencia, tráfico y errores de creación, envío a cocina, estados KDS, pagos y cierre de cuenta | `orders`, `kds`, `payments`, `http` |

Los filtros permiten seleccionar servicio e instancia; los dashboards HTTP añaden ruta y método o destino y puerto. La selección inicial muestra un servicio para mantener las gráficas legibles; **All** permite agruparlos. El menú **Dashboards de Restaurantes** conserva el rango temporal al cambiar de vista.

Grafana revisa los archivos de dashboards cada 30 segundos y los paneles se refrescan cada 30 segundos. El exportador .NET utiliza por defecto un intervalo de 60 segundos y Prometheus recoge el Collector cada 15 segundos: el refresco de pantalla no equivale a recibir una muestra nueva. El dashboard de entrada conserva el UID `restaurantes-gateway` para actualizar el anterior sin duplicarlo.

### Interpretación

- **Collector accesible** confirma únicamente que Prometheus puede recoger su endpoint. **Instancias con series runtime disponibles** cuenta instancias con métricas publicadas; el Collector conserva temporalmente sus últimas métricas, por lo que ese contador no es una comprobación de salud de los procesos.
- **Sin datos** distingue la falta de muestras utilizables de un valor cero. Los totales de errores solo muestran cero cuando existe una base HTTP para la selección; se ocultan los datos operativos cuando falla el scrape del Collector o faltan las series runtime de la instancia.
- Los indicadores superiores cubren los **últimos cinco minutos**, hasta el final del rango elegido. Los rankings en tabla se calculan sobre **todo el rango seleccionado**. Las tasas de las gráficas ajustan su ventana al intervalo de consulta.
- Los incrementos son estimaciones de contadores y pueden ser fraccionarios. Una primera petición registrada antes de la primera muestra no se reconstruye con `increase()`. El p95 es una estimación de histograma, especialmente sensible al poco tráfico; no mide por sí solo el tiempo entre enviar un pedido y verlo en KDS.
- Una petición puede aparecer en el gateway y en una API interna. Cada dashboard cuenta las observaciones de su responsabilidad; sumar ambas vistas no da peticiones externas únicas ni pedidos.
- En HTTP saliente, **Tráfico OTLP: Ocultar** excluye los puertos estándar `4317` y `4318`; **Mostrar** permite inspeccionarlos. El filtro debe adaptarse si el transporte OTLP utiliza otros puertos. Los errores salientes incluyen respuestas HTTP clasificadas como error y fallos de transporte.
- En Runtime, CPU se expresa en núcleos utilizados; las pausas de GC son una fracción del tiempo, promediada entre instancias. Las excepciones incluyen las capturadas. La cola y los hilos del ThreadPool se muestran como valores actuales.

Para observar una operación, selecciona su intervalo en Grafana y filtra por servicio, ruta y método. Las métricas permiten comparar actividad y latencia agregadas sin borrar el historial de Prometheus.
