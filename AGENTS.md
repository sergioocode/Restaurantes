# Instrucciones del repositorio

## Ramas e integración

- `develop` recibe y espera los cambios terminados; actualízala desde el remoto antes de crear una rama de trabajo.
- `master` es la rama estable y candidata a entrega; no desarrolles directamente en ella.
- Trabaja en una rama corta `feature/...` o `fix/...` creada desde `develop`; intégrala en `develop` y elimínala después.
- No crees pull requests. Integra mediante merge directo y conserva un historial lineal. Usa `git merge --ff-only` tanto al integrar la rama de trabajo en `develop` como al promover `develop` a `master`; si no es posible, detente y revisa la divergencia.

## Cambios y commits

- Implementa una sola intención funcional por cambio y comprueba los proyectos afectados. Si modificas un servicio, cliente, biblioteca compartida, Compose o configuración, valida también la solución completa; las comprobaciones específicas no sustituyen esa validación.
- Antes de cada commit, ejecuta `dotnet csharpier format .`, revisa `git diff --check` y comprueba que solo incluyes archivos de la tarea, incluidos sus cambios de formato.
- Crea commits pequeños, en español, con un mensaje que empiece con mayúscula y termine en punto.

## CI, cobertura y SonarQube

- Los pushes a `develop` no ejecutan GitHub Actions. El CI se activa al subir `master`: restaura `Restaurantes.slnx`, compila la solución completa y ejecuta todos los proyectos de pruebas. Ninguna API ni cliente queda excluido por defecto.
- Las pruebas deben generar cobertura OpenCover antes del análisis. `tools/sonar/Invoke-SonarQubeLocalAnalysis.ps1` realiza el análisis local y `tools/sonar/Invoke-SonarQubeCloudAnalysis.ps1` el equivalente en SonarQube Cloud.
- Publica en SonarQube Cloud solo los análisis de `master`, la rama principal del proyecto Cloud.
- Corrige primero las incidencias de seguridad y fiabilidad. Agrupa las mejoras de mantenibilidad en commits funcionales separados cuando no pertenezcan a la misma corrección.
- Suministra secretos, cadenas de conexión, credenciales de Vault y tokens mediante secretos o variables de cada entorno; nunca desde `appsettings.json` versionado. No guardes tokens de SonarQube en archivos versionados, logs ni documentación; en GitHub, configúralos como secretos del repositorio.

## Promoción, despliegues y releases

- Valida localmente la rama de trabajo, intégrala por avance rápido en `develop` y ejecuta `git push origin develop`.
- Avanza `master` hasta el mismo commit con `git merge --ff-only develop` y ejecuta `git push origin master`. Comprueba que su CI y Quality Gate pasan antes de considerarla candidata a release.
- Activa despliegues solo desde `master`, con entorno objetivo explícito, variables separadas por entorno y comprobación de salud posterior.
- Crea una release solo desde un commit de `master` validado según lo anterior. Etiquétalo con una versión semántica (`vMAJOR.MINOR.PATCH`) y publica notas con alcance, servicios y clientes afectados, migraciones, configuración requerida y pasos de reversión.
- Si hay migraciones, valida antes su compatibilidad y orden de ejecución. Una release no debe incluir secretos ni depender de valores locales de desarrollo.
