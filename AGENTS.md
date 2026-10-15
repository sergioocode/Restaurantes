# Instrucciones del repositorio

## Ramas e integración

- `develop` es la rama habitual de trabajo e integración; actualízala desde el remoto antes de empezar.
- `master` es la rama estable y candidata a entrega; no desarrolles directamente en ella.
- Para trabajos amplios o experimentales, puedes crear una rama corta `feature/...` o `fix/...` desde `develop`, integrarla de nuevo allí y eliminarla después. Los cambios pequeños pueden hacerse directamente en `develop`.
- No crees pull requests. Integra las ramas cortas en `develop` con `git merge --ff-only`. Al promover cambios de `develop` a `master`, crea siempre un commit de merge con `git merge --no-ff` y un mensaje que resuma la promoción, aunque se integre un solo commit. Conserva los commits originales. Si aparece un conflicto o una divergencia inesperada, detente y revísala.

## Cambios y commits

- Implementa una sola intención funcional por cambio y comprueba los proyectos afectados. Si modificas un servicio, cliente, biblioteca compartida, Compose o configuración, valida también la solución completa; las comprobaciones específicas no sustituyen esa validación.
- Antes de cada commit de trabajo, ejecuta `dotnet csharpier format .`, revisa `git diff --check` y comprueba que solo incluyes archivos de la tarea, incluidos sus cambios de formato. Antes del commit de merge en `master`, comprueba que el árbol esté limpio y que los commits de `develop` ya estén formateados y validados; no introduzcas cambios de formato durante la promoción.
- Crea commits pequeños, en español, con un mensaje que empiece con mayúscula y termine en punto.

## CI, cobertura y SonarQube

- Los pushes a `develop` no activan GitHub Actions. El CI se activa al subir `master` o manualmente: restaura `Restaurantes.slnx`, compila la solución completa y ejecuta todos los proyectos de pruebas. Ninguna API ni cliente queda excluido por defecto.
- Las pruebas deben generar cobertura OpenCover antes del análisis. `tools/sonar/Invoke-SonarQubeLocalAnalysis.ps1` realiza el análisis local. `tools/sonar/Invoke-SonarQubeCloudAnalysis.ps1` permite un análisis Cloud manual desde `master`; publica un nuevo resultado en el mismo proyecto usado por CI, así que ejecútalo solo de forma deliberada y con el árbol limpio.
- Publica en SonarQube Cloud solo los análisis de `master`, la rama principal del proyecto Cloud.
- Corrige primero las incidencias de seguridad y fiabilidad. Agrupa las mejoras de mantenibilidad en commits funcionales separados cuando no pertenezcan a la misma corrección.
- Suministra secretos, cadenas de conexión, credenciales de Vault y tokens mediante secretos o variables de cada entorno; nunca desde `appsettings.json` versionado. No guardes tokens de SonarQube en archivos versionados, logs ni documentación; en GitHub, configúralos como secretos del repositorio.

## Promoción, despliegues y releases

- Antes de hacer un push, presenta al propietario el diff y los commits incluidos y espera su visto bueno explícito.
- Valida localmente los cambios. Si utilizaste una rama corta, intégrala por avance rápido en `develop`; después, con el visto bueno indicado arriba, ejecuta `git push origin develop`.
- Desde `master`, integra `develop` con `git merge --no-ff -m "Resumen de los cambios incorporados." develop` (sustituye el ejemplo por un resumen real) y, con el visto bueno indicado arriba, ejecuta `git push origin master`. Comprueba que su CI y Quality Gate pasan antes de considerarla candidata a release.
- Después de validar `master`, incorpora su commit de merge a `develop` mediante `git merge --ff-only master` y sube `develop` con el visto bueno indicado arriba, para que ambas ramas compartan ese punto de partida antes del siguiente trabajo.
- Activa despliegues solo desde `master`, con entorno objetivo explícito, variables separadas por entorno y comprobación de salud posterior.
- Crea una release solo desde un commit de `master` validado según lo anterior. Etiquétalo con una versión semántica (`vMAJOR.MINOR.PATCH`) y publica notas con alcance, servicios y clientes afectados, migraciones, configuración requerida y pasos de reversión.
- Si hay migraciones, valida antes su compatibilidad y orden de ejecución. Una release no debe incluir secretos ni depender de valores locales de desarrollo.
