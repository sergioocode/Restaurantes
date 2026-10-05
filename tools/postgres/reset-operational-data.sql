-- Reinicia los datos operativos locales de desarrollo en las dos instancias PostgreSQL.
-- Conserva Identity, locales, catálogo, mesas, zonas y configuración.
--
-- Antes de empezar:
--   1. Detén todos los servicios y workers de la aplicación.
--   2. Copia este archivo a ambos contenedores:
--      docker cp .\tools\postgres\reset-operational-data.sql restaurantes-postgres:/tmp/reset-operational-data.sql
--      docker cp .\tools\postgres\reset-operational-data.sql restaurantes-postgres-analytics:/tmp/reset-operational-data.sql
--   3. Ejecuta una vez en cada instancia, desde PowerShell:
--      docker exec -it restaurantes-postgres psql -U restaurants -d restaurants_operational -v confirm_reset=BORRAR -v reset_target=operational -f /tmp/reset-operational-data.sql
--      docker exec -it restaurantes-postgres-analytics psql -U restaurants -d restaurants_analytics -v confirm_reset=BORRAR -v reset_target=analytics -f /tmp/reset-operational-data.sql
--   4. Antes de arrancar los workers, abre RabbitMQ Management (http://localhost:15672),
--      ve a Queues and Streams y purga estas colas si existen, junto con sus sufijos
--      .retry.1, .retry.2 y .retry.3 y las DLQ indicadas:
--      dining.table-sessions, orders.kds.read-model, orders.kds.signalr,
--      payments.orders-integration, payments.payable-orders, payments.cash-register,
--      sales.checkout-process, sales.read-model, reporting.dashboard.read-model,
--      reporting.dashboard.signalr.projection-updated,
--      dining.dead-letter.messages, orders.kds.read-model.dead-letter,
--      payments.dead-letter.messages, sales.dead-letter.messages,
--      reporting.dead-letter.messages.
--      No purgues colas de Catalog ni RestaurantOperations porque sus datos se conservan.
--      Este archivo solo limpia PostgreSQL.
--
-- Ejecuta cada modo contra su instancia indicada. El script verifica tanto el modo
-- solicitado como current_database() para evitar limpiar la instancia equivocada.

\set ON_ERROR_STOP on

\if :{?confirm_reset}
\else
    \echo 'Abortado: falta -v confirm_reset=BORRAR.'
    \quit 3
\endif

\if :{?reset_target}
\else
    \echo 'Abortado: falta -v reset_target=operational o -v reset_target=analytics.'
    \quit 3
\endif

SELECT :'confirm_reset' = 'BORRAR' AS confirmed \gset
\if :confirmed
    \echo 'Confirmación recibida.'
\else
    \echo 'Abortado: confirm_reset debe ser BORRAR.'
    \quit 3
\endif

SELECT :'reset_target' IN ('operational', 'analytics') AS valid_target \gset
\if :valid_target
\else
    \echo 'Abortado: reset_target debe ser operational o analytics.'
    \quit 3
\endif

SELECT current_database() = CASE :'reset_target'
    WHEN 'operational' THEN 'restaurants_operational'
    WHEN 'analytics' THEN 'restaurants_analytics'
END AS correct_database \gset
\if :correct_database
    \echo 'Base de datos verificada:' :reset_target
\else
    \echo 'Abortado: reset_target no coincide con la base de datos conectada.'
    \quit 3
\endif

SELECT :'reset_target' = 'operational' AS reset_operational \gset
\if :reset_operational
    BEGIN;
    TRUNCATE TABLE
        orders_write.order_lines,
        orders_write.order_kitchen_stations,
        orders_write.order_payments,
        orders_write.orders,
        orders_write.outbox_messages,
        dining.pending_payments,
        dining.dining_session_orders,
        dining.dining_sessions,
        dining.inbox_messages,
        payments.payable_orders,
        payments.inbox_messages,
        payments.outbox_messages,
        cash_register.cash_movements,
        cash_register.cash_register_reconciliations,
        cash_register.cash_register_sessions,
        cash_register.inbox_messages,
        sales_write.sale_orders,
        sales_write.sales,
        sales_write.delivered_order_markers,
        sales_write.inbox_messages,
        sales_write.outbox_messages
    RESTART IDENTITY;
    COMMIT;
\endif

SELECT :'reset_target' = 'analytics' AS reset_analytics \gset
\if :reset_analytics
    BEGIN;
    TRUNCATE TABLE
        orders_read.kitchen_order_views,
        orders_read.inbox_messages,
        sales_read.sales,
        sales_read.inbox_messages,
        reporting.order_reporting_facts,
        reporting.completed_sale_facts,
        reporting.inbox_messages,
        reporting.outbox_messages
    RESTART IDENTITY;
    COMMIT;
\endif

\echo 'Limpieza completada. Identity, catálogo y configuración se conservaron.'
