# Instrucciones del repositorio

## Ramas y promoción

- `develop` es la rama de trabajo e integración; actualízala desde el remoto antes de empezar. `master` es la rama estable y de entrega; no trabajes directamente en ella.
- Los cambios pequeños pueden hacerse en `develop`. Para trabajos amplios, crea una rama `feature/...` o `fix/...` desde `develop`, intégrala con `git merge --ff-only` y elimínala. No crees pull requests.
- Tras validar localmente, promueve desde `master` con `git merge --ff-only develop`. Así se incorpora todo el historial sin crear un commit de merge ni un mensaje nuevo. Si no puede avanzarse por fast-forward, detente y revisa la divergencia.
- Antes de un push, informa el diff y los commits incluidos. Una orden explícita del usuario para subir autoriza el push; no solicites una segunda confirmación. Tras validar `master` y su CI y Quality Gate, actualiza `develop` con `git merge --ff-only master`.
- Despliega solo desde `master`, con entorno explícito, variables separadas y comprobación de salud. Publica releases solo desde un `master` validado, con versión semántica y notas de alcance, configuración, migraciones y reversión.

## Cambios y validación

- Implementa una intención funcional por cambio. Valida los proyectos afectados y, si modificas un servicio, cliente, biblioteca compartida, Compose o configuración, valida también la solución completa.
- Antes de cada commit, ejecuta `dotnet csharpier format .` y `git diff --check`; confirma que solo incluyes archivos de la tarea. Escribe los mensajes como `tipo: resumen breve en español`, sin ámbitos ni etiquetas. Usa únicamente `feat` (funcionalidad), `fix` (corrección), `docs` (documentación), `refactor` (reestructuración sin cambio funcional), `chore` (mantenimiento) o `test` (pruebas). El tipo va en minúsculas; el resumen empieza en minúscula y termina en punto.
- El CI de `master` restaura `Restaurantes.slnx`, compila la solución y ejecuta todas las pruebas. Genera cobertura OpenCover antes del análisis SonarQube.
- `tools/sonar/Invoke-SonarQubeLocalAnalysis.ps1` analiza localmente. Ejecuta `tools/sonar/Invoke-SonarQubeCloudAnalysis.ps1` deliberadamente, solo desde `master` y con el árbol limpio. Publica análisis Cloud únicamente para `master`.

## Seguridad y releases

- Corrige primero los problemas de seguridad y fiabilidad. No guardes secretos, credenciales ni tokens en archivos versionados, logs o documentación; usa secretos o variables del entorno.
- Antes de incluir migraciones en una release, valida su compatibilidad y orden de ejecución. No dependas de configuración local de desarrollo.
