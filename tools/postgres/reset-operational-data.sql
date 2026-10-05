-- Limpia pedidos, pagos, cajas y proyecciones de desarrollo.
-- Conserva Identity, locales, catálogo, mesas, zonas, políticas y configuración.
-- Ejecutar con todos los servicios detenidos para evitar que RabbitMQ repueble los datos.
-- También hay que purgar las colas de eventos de negocio antes de volver a iniciar los workers.
-- Uso: psql -d postgres -v confirm_reset=BORRAR -f tools/postgres/reset-operational-data.sql

\set ON_ERROR_STOP on

SELECT :'confirm_reset' = 'BORRAR' AS confirmed \gset
\if :confirmed
    \echo 'Confirmación recibida. Se eliminarán los datos operativos.'
\else
    \echo 'Abortado. Ejecuta psql con -v confirm_reset=BORRAR para confirmar.'
    \quit 3
\endif

\connect orders_write
BEGIN;
TRUNCATE TABLE
    order_lines,
    order_kitchen_stations,
    order_payments,
    orders,
    outbox_messages
RESTART IDENTITY;
COMMIT;

\connect orders_read
BEGIN;
TRUNCATE TABLE kitchen_order_views, inbox_messages RESTART IDENTITY;
COMMIT;

\connect dining_write
BEGIN;
TRUNCATE TABLE
    pending_payments,
    dining_session_orders,
    dining_sessions,
    inbox_messages
RESTART IDENTITY;
COMMIT;

\connect payments_write
BEGIN;
TRUNCATE TABLE payable_orders, inbox_messages, outbox_messages RESTART IDENTITY;
COMMIT;

\connect cash_register_write
BEGIN;
TRUNCATE TABLE
    cash_movements,
    cash_register_reconciliations,
    cash_register_sessions,
    inbox_messages
RESTART IDENTITY;
COMMIT;

\connect sales_write
BEGIN;
TRUNCATE TABLE
    sale_orders,
    sales,
    delivered_order_markers,
    inbox_messages,
    outbox_messages
RESTART IDENTITY;
COMMIT;

\connect sales_read
BEGIN;
TRUNCATE TABLE sales, inbox_messages RESTART IDENTITY;
COMMIT;

\connect reporting_read
BEGIN;
TRUNCATE TABLE
    order_reporting_facts,
    completed_sale_facts,
    inbox_messages,
    outbox_messages
RESTART IDENTITY;
COMMIT;

\echo 'Limpieza operativa completada. Identity, catálogo y locales se conservaron.'
