# Imágenes de productos

El Backoffice envía una sola imagen original (máximo 10 MB). La API valida el formato, limita la imagen a 40 megapíxeles y genera tres variantes WebP manteniendo la proporción:

- `thumbnail.webp`: lado máximo de 160 px para listados.
- `card.webp`: lado máximo de 640 px para TPV y Comandero.
- `detail.webp`: lado máximo de 1600 px para Cliente QR.

La API guarda el original y esas tres variantes con la misma revisión. PostgreSQL solo guarda la revisión y el tipo MIME del original. Las URLs incluyen la revisión para poder usar caché inmutable; los clientes no descargan el original para redimensionarlo.

En producción se recomienda Blob Storage u otro almacenamiento de objetos compatible con CDN. Activa el proveedor con `CatalogMedia:Provider=Blob`, `CatalogMedia:Blob:ConnectionString` y `CatalogMedia:Blob:Container` (por defecto `catalog-media`). Las credenciales deben llegar por secretos o variables de entorno. El CDN puede cachear directamente las variantes.

La implementación vive en `Restaurantes.Catalog.Infrastructure.Media`: allí están el contrato de almacenamiento, el proveedor Azure Blob/Azurite y el procesamiento de imágenes. El API de Catalog es la frontera HTTP; Backoffice sube las imágenes al API y TPV, Comandero y Cliente QR solo consumen sus URLs. No se referencia el SDK de Azure ni la cadena de conexión desde los clientes. `Restaurantes.Integrations` queda reservado para adaptadores externos como Glovo o Uber.

Para probar el mismo proveedor localmente, inicia Azurite desde Visual Studio o con:

`"C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\Extensions\Microsoft\Azure Storage Emulator\azurite.exe" --skipApiVersionCheck`

Después agrega en Azure Storage Explorer un nodo Azurite con la cuenta `devstoreaccount1`, el endpoint Blob `http://127.0.0.1:10000/devstoreaccount1` y la clave predeterminada del emulador. El repositorio incluye `appsettings.Development.example.json`; copia su contenido a `appsettings.Development.json` si el archivo local no existe. Ese archivo está ignorado por Git y apunta al Azurite local.

Para desarrollo se puede usar `CatalogMedia:Provider=FileSystem`. La ruta se configura con `CatalogMedia:Root`; si no se configura en Development se usa `%LOCALAPPDATA%/Restaurantes/CatalogMedia`. En producción, el proveedor de archivos necesita un volumen persistente y compartido.

Las rutas públicas son `/api/catalog/products/{id}/photos/thumbnail`, `/card` y `/detail`. La API acepta temporalmente `small`, `medium` y `large` como alias de compatibilidad. La API no guarda vídeos ni archivos animados en esta fase.
