# 🍽️ Restaurantes

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Blazor WebAssembly](https://img.shields.io/badge/Blazor_WebAssembly-10.0.12-512BD4?style=for-the-badge&logo=blazor&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![EF Core](https://img.shields.io/badge/EF_Core-10.0.4-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/ef/core/)
[![YARP](https://img.shields.io/badge/YARP-2.3.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://microsoft.github.io/reverse-proxy/)

[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-4.3.4-FF6600?style=for-the-badge&logo=rabbitmq&logoColor=white)](https://www.rabbitmq.com/)
[![Vault](https://img.shields.io/badge/Vault-1.21.4-000000?style=for-the-badge&logo=vault&logoColor=white)](https://developer.hashicorp.com/vault)
[![Docker Compose](https://img.shields.io/badge/Docker%20Compose-5.5.1-2496ED?style=for-the-badge&logo=docker&logoColor=white)](https://docs.docker.com/compose/)

[![OpenTelemetry .NET](https://img.shields.io/badge/OpenTelemetry_.NET-1.19.x-000000?style=for-the-badge&logo=opentelemetry&logoColor=white)](https://opentelemetry.io/)
[![OTel Collector](https://img.shields.io/badge/OTel_Collector-0.120.0-000000?style=for-the-badge&logo=opentelemetry&logoColor=white)](https://opentelemetry.io/docs/collector/)
[![Prometheus](https://img.shields.io/badge/Prometheus-3.8.0-E6522C?style=for-the-badge&logo=prometheus&logoColor=white)](https://prometheus.io/)
[![Grafana](https://img.shields.io/badge/Grafana-12.3.0-F46800?style=for-the-badge&logo=grafana&logoColor=white)](https://grafana.com/)

**Restaurantes** es una plataforma distribuida para la operación y gestión de restaurantes, construida sobre **.NET 10**. Reúne servicios para catálogo, sala, pedidos, cocina, pagos, caja, ventas, identidad y reporting, con aplicaciones para clientes, personal y administración.

El repositorio presenta la arquitectura, sus objetivos y las áreas funcionales. Los detalles de cada servicio, flujo e infraestructura se encuentran en los [documentos de `docs`](docs/).

> [!IMPORTANT]
> Proyecto educativo en **desarrollo activo**; no es un producto comercial terminado.

## Arquitectura general

```mermaid
flowchart TB
    subgraph Clients["Aplicaciones"]
        QR["Customer QR"]
        Commander["Comandero"]
        POS["TPV / POS"]
        KDS["KDS"]
        Backoffice["Backoffice"]
        Dashboard["Dashboard"]
    end

    subgraph Edge["Entrada"]
        PublicGateway["YARP · Gateway público"]
        PrivateGateway["YARP · Gateway privado"]
    end

    subgraph Services["Servicios de negocio"]
        RestaurantOperations["RestaurantOperations"]
        Catalog["Catalog"]
        Dining["Dining"]
        Orders["Orders"]
        Payments["Payments"]
        CashRegister["CashRegister"]
        Sales["Sales"]
        Identity["Identity"]
        Reporting["Reporting"]
        Integrations["Integrations"]
    end

    Rabbit[("RabbitMQ")]

    subgraph Data["Persistencia aislada · PostgreSQL"]
        WriteDB[("Modelos de escritura")]
        ReadDB[("Modelos de lectura / reporting")]
    end

    QR --> PublicGateway
    Commander --> PrivateGateway
    POS --> PrivateGateway
    KDS --> PrivateGateway
    Backoffice --> PrivateGateway
    Dashboard --> PrivateGateway
    PublicGateway --> Services
    PrivateGateway --> Services

    RestaurantOperations --> WriteDB
    Catalog --> WriteDB
    Dining --> WriteDB
    Orders --> WriteDB
    Payments --> WriteDB
    CashRegister --> WriteDB
    Sales --> WriteDB
    Identity --> WriteDB

    RestaurantOperations --> Rabbit
    Rabbit --> RestaurantOperations
    Catalog --> Rabbit
    Rabbit --> Catalog
    Rabbit --> Dining
    Orders --> Rabbit
    Rabbit --> Orders
    Payments --> Rabbit
    Rabbit --> Payments
    Rabbit --> CashRegister
    Sales --> Rabbit
    Rabbit --> Sales
    Reporting --> Rabbit
    Rabbit --> Reporting
    Rabbit --> ReadDB
    RestaurantOperations --> ReadDB
    Catalog --> ReadDB
    Orders --> ReadDB
    Sales --> ReadDB
    Reporting --> ReadDB
```

Los dominios **RestaurantOperations, Catalog, Orders y Sales** separan lectura y escritura; RabbitMQ actualiza sus proyecciones. Los demás servicios mantienen persistencia operacional.

Las aplicaciones entran por los gateways **YARP**. HTTP atiende operaciones síncronas, RabbitMQ distribuye eventos y SignalR notifica cambios en tiempo real. Rutas y observabilidad: [Gateways](docs/Gateways.README.md); flujos de negocio: documentos de cada servicio.

La autenticación usa **ASP.NET Core Identity y JWT**; la configuración y las reglas de acceso se describen en [Identity](docs/Identity.README.md).

## Servicios

| Servicio | Responsabilidad | Persistencia |
| --- | --- | --- |
| **[RestaurantOperations](docs/RestaurantOperations.README.md)** | Gestión e información base de restaurantes. | Write + Read |
| **[Catalog](docs/Catalog.README.md)** | Categorías, productos, precios, disponibilidad y configuración de estaciones por restaurante. | Write + Read |
| **[Orders](docs/Orders.README.md)** | Pedidos, líneas, estados de preparación y coordinación del flujo de cocina/KDS. | Write + Read |
| **[Dining](docs/Dining.README.md)** | Zonas, mesas, sesiones, QR, comensales y cuenta de mesa. | Write |
| **[Payments](docs/Payments.README.md)** | Capturas, devoluciones y estado de cobro. | Write |
| **[Sales](docs/Sales.README.md)** | Consolidación de ventas originadas en la operación. | Write + Read |
| **[CashRegister](docs/CashRegister.README.md)** | Apertura/cierre de caja, jornada operativa y conciliación de efectivo. | Write |
| **[Identity](docs/Identity.README.md)** | Usuarios, roles, autenticación y acceso por restaurante. | Write |
| **[Reporting](docs/Reporting.README.md)** | Proyecciones agregadas para actividad y ventas. | Read |
| **[Integrations](docs/Integrations.README.md)** | Punto de extensión para proveedores y sistemas externos. | — |

Cada servicio es propietario de sus datos; colaboran mediante contratos HTTP o eventos.

## Aplicaciones

| Perfil | Aplicación | Propósito | Tecnología |
| --- | --- | --- | --- |
| Cliente | **Customer QR** | Pago online obligatorio antes de enviar el pedido a cocina y seguimiento desde el QR asociado a una mesa/sesión. | Blazor WebAssembly PWA |
| Camarero | **Comandero** | Pedidos de mesa, cobro con tarjeta y liberación de mesa. | Blazor WebAssembly PWA |
| Caja | **TPV / POS** | Pedidos de sala y barra, canal Takeaway, cuentas y caja. | Blazor WebAssembly PWA |
| Cocina | **KDS** | Preparación y despacho por estación. | ASP.NET Core Web |
| Administración | **Backoffice** | Restaurantes, catálogo, sala y usuarios. | Blazor WebAssembly PWA |
| Oficina | **Dashboard** | Ventas, actividad operativa y salud de endpoints. | ASP.NET Core Web |

Los flujos de pedidos, pagos y cocina se detallan en [Orders](docs/Orders.README.md), [Dining](docs/Dining.README.md) y [Payments](docs/Payments.README.md).

## Estructura del repositorio

El código se organiza en `src` (servicios, clientes, gateways y componentes compartidos), `tests`, `docs` y `tools`. La solución y los perfiles de ejecución están en la raíz.

## Entorno local

### Requisitos

- .NET 10 SDK
- Docker / Docker Compose
- Visual Studio 2026 o un entorno compatible con .NET 10

### Infraestructura

Compose levanta la infraestructura local; SonarQube Community Build está en el perfil opcional `quality`.

Configura `tools/vault/.env` desde `.env.example` con valores locales; el archivo está excluido de Git.

```bash
docker compose --env-file tools/vault/.env up -d
```

### Observabilidad

La pila local usa OpenTelemetry, Prometheus y Grafana. Detalles en [Gateways](docs/Gateways.README.md#observabilidad-de-gateways-y-apis).

![Dashboard de Grafana con operación HTTP](assets/grafana/grafana-operacion-http.jpg)

![Consulta de Prometheus con el flujo completo](assets/prometheus/prometheus-flujo-completo.jpg)

### Calidad y seguridad local

SonarQube Community Build se inicia bajo el perfil opcional `quality`:

```powershell
docker compose --env-file tools/vault/.env --profile quality up -d sonarqube
```

Proporciona el token de análisis solo en la sesión actual de PowerShell:

```powershell
$env:SONAR_TOKEN = "<token-de-analisis>"
.\tools\sonar\Invoke-SonarQubeLocalAnalysis.ps1
```

![Quality Gate inicial de SonarQube](assets/sonar/sonarqube-quality-gate.jpg)

### Integración continua y calidad local

`develop` integra los cambios; `master` es la rama estable. El [workflow de CI](.github/workflows/ci.yml) compila y ejecuta pruebas. SonarQube local valida el Quality Gate antes de promover a `master`.

### Restaurar y compilar

```bash
dotnet tool restore
dotnet restore
dotnet build Restaurantes.slnx
```

Los perfiles `.slnLaunch` permiten ejecutar escenarios desde Visual Studio.

---

Este repositorio forma parte de mi portfolio técnico sobre arquitectura distribuida y aplicaciones .NET para un dominio operativo real.
