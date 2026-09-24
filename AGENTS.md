# Instrucciones del repositorio

## Commits

- Todos los mensajes de commit deben comenzar con mayúscula y terminar con un punto.

## Formato

- Antes de crear cualquier commit, ejecuta `dotnet csharpier format .`.
- Incluye en el commit los cambios de formato que correspondan a la tarea.

## Integración de ramas

- No crear pull requests.
- Integrar las ramas mediante merge directo hacia la rama de destino.

## Flujo de ramas, calidad y entregas

### Ramas

- `develop` es la rama de integración y de trabajo habitual. Toda funcionalidad, corrección o ajuste de infraestructura comienza y se valida allí.
- `master` representa la versión estable y candidata a entrega. No se desarrolla directamente en ella.
- Para una tarea amplia o experimental se puede crear una rama corta desde `develop` (`feature/...` o `fix/...`), integrarla primero en `develop` y eliminarla después. Para cambios pequeños realizados por una sola persona se puede trabajar directamente en `develop`.
- Mantener el historial lineal: al promover una versión, usar `git merge --ff-only develop` desde `master`. Si no es posible avanzar rápidamente, detenerse y revisar antes de crear un merge commit.

### Ciclo de trabajo

1. Actualizar `develop` desde el remoto antes de empezar una sesión de trabajo.
2. Implementar una única intención funcional y comprobar los proyectos afectados.
3. Antes de cada commit, ejecutar `dotnet csharpier format .`, revisar `git diff --check` y comprobar que solo se van a incluir archivos propios de la tarea.
4. Crear commits pequeños, en español, con mensaje que comience en mayúscula y termine en punto.
5. Subir primero `develop` con `git push origin develop`. El CI de GitHub debe compilar la solución y ejecutar las pruebas automatizadas.
6. Cuando `develop` esté validada, cambiar a `master`, integrar con `git merge --ff-only develop` y subir con `git push origin master`.
7. El CI de `master` debe volver a compilar, ejecutar pruebas, publicar cobertura y lanzar el análisis oficial de SonarQube. Solo una ejecución correcta convierte `master` en candidata a release.

### SonarQube y cobertura

- Las pruebas deben generar cobertura OpenCover antes del análisis. `tools/sonar/Invoke-SonarQubeAnalysis.ps1` mantiene el análisis local; `tools/sonar/Invoke-SonarQubeCloudAnalysis.ps1` ejecuta el análisis equivalente contra SonarQube Cloud.
- No guardar tokens de SonarQube en archivos versionados, logs ni documentación. En GitHub se configurarán como secretos del repositorio.
- SonarQube Community local sigue disponible como entorno opcional de aprendizaje y diagnóstico. SonarQube Cloud es la fuente de referencia de CI; la organización es `sergioocode` y el proyecto es `sergioocode_Restaurantes`; sus tokens se mantienen fuera del repositorio.
- El análisis de `develop` y `master` se publica en la rama correspondiente de SonarQube Cloud. Un Quality Gate fallido impide considerar `master` candidata a release.
- Corregir primero incidencias de seguridad y fiabilidad. Las mejoras de mantenibilidad se agrupan en commits funcionales separados cuando no formen parte de la misma corrección.

### CI/CD para una solución con varias APIs y clientes

- El pipeline debe descubrir y restaurar la solución completa (`Restaurantes.slnx`), compilarla y ejecutar todos los proyectos de pruebas. Ninguna API o cliente queda excluido por defecto.
- Los cambios que afecten a un servicio, cliente, biblioteca compartida, Compose o configuración se validan como solución completa; las comprobaciones adicionales pueden limitarse a los proyectos afectados para acelerar el diagnóstico, no para omitir la validación global.
- Los secretos, cadenas de conexión, credenciales de Vault y tokens se suministran mediante secretos y variables del entorno de cada entorno; nunca desde `appsettings.json` versionado.
- Los despliegues no se activan desde `develop`. Se habilitarán únicamente desde `master`, con un entorno objetivo explícito, variables separadas por entorno y una comprobación de salud posterior al despliegue.

### Releases

- Crear una release solo desde un commit de `master` que haya pasado CI, pruebas, cobertura y Quality Gate.
- Etiquetar esa confirmación con una versión semántica (`vMAJOR.MINOR.PATCH`) y publicar las notas de la versión: alcance, servicios/clientes afectados, migraciones, configuración requerida y pasos de reversión.
- Si hay migraciones, validar previamente su compatibilidad y orden de ejecución. Un release no debe incluir secretos ni depender de valores locales de desarrollo.
