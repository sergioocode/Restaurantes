# Instrucciones del repositorio

## Ramas e integración

- `develop` es la rama habitual de trabajo e integración. Actualízala desde el remoto antes de empezar una sesión.
- `master` es la rama estable y candidata a entrega; no desarrolles directamente en ella.
- Para trabajos amplios o experimentales, crea una rama corta `feature/...` o `fix/...` desde `develop`, intégrala primero allí y elimínala después. Los cambios pequeños de una sola persona pueden hacerse en `develop`.
- No crees pull requests. Integra mediante merge directo y conserva un historial lineal. Para promover `develop` a `master`, ejecuta `git merge --ff-only develop` desde `master`; si no es posible, detente y revisa la divergencia antes de crear un merge commit.

## Cambios y commits

- Implementa una sola intención funcional por cambio y comprueba los proyectos afectados. Si modificas un servicio, cliente, biblioteca compartida, Compose o configuración, valida también la solución completa; las comprobaciones específicas no sustituyen esa validación.
- Antes de cada commit, ejecuta `dotnet csharpier format .`, revisa `git diff --check` y comprueba que solo incluyes archivos de la tarea, incluidos sus cambios de formato.
- Crea commits pequeños, en español, con un mensaje que empiece con mayúscula y termine en punto.

## CI, cobertura y SonarQube

- En `develop` y `master`, el CI debe descubrir y restaurar `Restaurantes.slnx`, compilar la solución completa y ejecutar todos los proyectos de pruebas. Ninguna API ni cliente queda excluido por defecto.
- Las pruebas deben generar cobertura OpenCover antes del análisis. `tools/sonar/Invoke-SonarQubeLocalAnalysis.ps1` realiza el análisis local.
- Corrige primero las incidencias de seguridad y fiabilidad. Agrupa las mejoras de mantenibilidad en commits funcionales separados cuando no pertenezcan a la misma corrección.
- Suministra secretos, cadenas de conexión, credenciales de Vault y tokens mediante secretos o variables de cada entorno; nunca desde `appsettings.json` versionado. No guardes tokens de SonarQube en archivos versionados, logs ni documentación; en GitHub, configúralos como secretos del repositorio.

## Promoción, despliegues y releases

- Sube primero `develop` con `git push origin develop` y verifica su CI.
- Cuando `develop` esté validada, intégrala en `master` mediante el avance rápido indicado arriba y ejecuta `git push origin master`. Su CI debe compilar, ejecutar pruebas y publicar cobertura; valida el Quality Gate antes de considerar `master` candidata a release.
- Activa despliegues solo desde `master`, con entorno objetivo explícito, variables separadas por entorno y comprobación de salud posterior.
- Crea una release solo desde un commit de `master` validado según lo anterior. Etiquétalo con una versión semántica (`vMAJOR.MINOR.PATCH`) y publica notas con alcance, servicios y clientes afectados, migraciones, configuración requerida y pasos de reversión.
- Si hay migraciones, valida antes su compatibilidad y orden de ejecución. Una release no debe incluir secretos ni depender de valores locales de desarrollo.
