# Roadmap exhaustivo de Statefalse

## Objetivo

Convertir Statefalse en producto macOS distribuible y mantenible para uso personal primero y para equipos después, sin entrar todavía en App Store ni asumir el coste del Apple Developer Program.

Orden de trabajo recomendado:

1. Reducir riesgo operativo y de seguridad.
2. Separar desarrollo de producción.
3. Sustituir ngrok por dominio propio y HTTPS estable.
4. Crear distribución reproducible y landing pública.
5. Mejorar CI/CD, observabilidad y recuperación.
6. Pulir producto, branding y experiencia de instalación.
7. Preparar multi-tenant y distribución empresarial cuando exista demanda real.

---

# 0. Auditoría inicial: situación actual

## Arquitectura verificada

- Backend ASP.NET Core Minimal API sobre .NET 10.
- EF Core + SQLite.
- SignalR para tiempo real.
- macOS menu-bar app en SwiftUI/AppKit.
- OAuth de GitHub + JWT de sesión + PAT opcional.
- VPS Hetzner con Kestrel en `localhost:5000`.
- systemd para backend y túnel.
- SQLite en `/var/lib/statefalse/statefalse.db`.
- ngrok gratuito como exposición pública.
- GitHub Actions ya existentes para tests backend, tests Swift y deploy backend.
- Native app sin dependencias externas; compilación mediante Xcode.
- Tests backend y native existentes.

## Situación de Docker

No hay `Dockerfile` ni `docker-compose.yml` implementados actualmente. Docker aparece únicamente como propuesta documental en `PROFESIONALIZACION.md` y `MULTI-TENANT.md`.

## Situación de CI/CD

Ya existe:

- `.github/workflows/backend-tests.yml`.
- `.github/workflows/swift-tests.yml`.
- `.github/workflows/deploy-backend.yml`.
- Deploy automático a VPS al hacer push a `main` con cambios en backend/tests.
- Health check posterior al reinicio.

Riesgos actuales:

- `main` es producción de facto.
- No existe staging real.
- El deploy apunta directamente a VPS y reinicia systemd.
- No se observa rollback automático.
- El deploy usa conexión SSH desde GitHub Actions.
- No hay flujo claro de release de la app macOS.
- No hay artefacto versionado de descarga pública.
- Hay configuración antigua/documentación que todavía referencia ngrok.
- El job de deploy debe revisar que haga restore explícito antes de `dotnet test`/`dotnet publish`.

## Situación de documentación

Existe documentación amplia, pero parte está desactualizada o mezcla:

- Estado actual.
- Planes futuros.
- Arquitectura multi-tenant hipotética.
- Monetización futura.
- Referencias antiguas a ngrok.

Debe consolidarse en documentación operativa única y documentos de diseño separados.

---

# 1. P0 — Seguridad, control de cambios y producción protegida

Prioridad máxima. Hacer antes de añadir funcionalidades o publicar ampliamente.

## 1.1 Congelar estado y crear inventario

- Crear tag de estado actual, por ejemplo `v0.1.0-internal`.
- Registrar commit actualmente desplegado en producción.
- Documentar versión de backend y versión de app nativa.
- Exportar esquema/migraciones y confirmar backup recuperable de SQLite.
- Confirmar qué secretos existen en:
  - GitHub Actions secrets/variables.
  - `/etc/statefalse/statefalse.env`.
  - `appsettings.Production.json`.
  - Keychain local.
- Eliminar o rotar cualquier secreto que haya aparecido en documentación, logs o commits.

## 1.2 Proteger GitHub

- Activar protección de `main`.
- Exigir Pull Request.
- Exigir workflows de backend y native cuando los paths correspondan.
- Exigir review antes de merge cuando empiece a participar el equipo.
- Desactivar push directo salvo emergencia documentada.
- Añadir CODEOWNERS para backend, native, infraestructura y workflows.
- Activar secret scanning, Dependabot y alertas de dependencias.
- Definir política de commits y releases.

## 1.3 Revisar autenticación y secretos

Auditar:

- JWT: longitud, expiración, issuer, audience y rotación.
- OAuth callback y validación de `redirect_uri`.
- Cookies/headers/query string y exposición del JWT en SignalR.
- PAT: cifrado en reposo, logs, eliminación y precedencia.
- HMAC del webhook con `FixedTimeEquals`.
- Comportamiento cuando `WebhookSecret` está vacío.
- CORS abierto y orígenes permitidos.
- Rate limiting diferenciado por usuario, endpoint y webhook.
- Validación de ownership en todos los endpoints multi-tenant.
- Protección contra replay de webhooks.
- Payloads máximos y límites de tiempo.
- Redacción de tokens, headers Authorization y payloads sensibles en logs.

Criterio de salida:

- Checklist de seguridad firmado.
- Ningún secreto en Git.
- CORS restringido al dominio de la app/API cuando exista.
- Webhook sin secret no permitido en producción.
- Logs revisados sin tokens.

## 1.4 Backups y recuperación

Implementar:

- Backup SQLite diario.
- Retención mínima de 7-30 días.
- Copia fuera del mismo disco/VPS.
- Backup antes de migraciones.
- Script de restore probado en entorno aislado.
- Verificación periódica de integridad con `PRAGMA integrity_check`.
- Procedimiento de rollback de binario y base de datos.
- Documentación de RPO/RTO iniciales.

No considerar backup terminado hasta haber restaurado una copia real.

---

# 2. P0 — Separar desarrollo, staging y producción

Antes de que el equipo use la aplicación.

## 2.1 Entornos objetivo

### Local

- Backend en `http://localhost:5000`.
- SQLite local separada.
- Secretos de desarrollo en archivo ignorado o variables de entorno.
- OAuth App de GitHub para desarrollo.
- Webhooks locales mediante túnel temporal o payloads simulados.
- Native app apuntando explícitamente a localhost.

### Staging

- VPS, contenedor o instancia separada.
- Base de datos independiente.
- OAuth App independiente.
- Webhook secret independiente.
- Dominio como `staging.statefalse.com` o `api-staging.statefalse.com`.
- Deploy automático desde rama `develop` o manual desde GitHub Actions.
- Datos sintéticos o sanitizados; nunca copia directa de producción con tokens.

### Producción

- Dominio estable.
- Base de datos y secretos propios.
- Deploy solo desde release aprobada o `main` protegido.
- Aprobación manual mediante GitHub Environment `production`.
- Backups y monitorización obligatorios.

## 2.2 Estrategia de ramas

Recomendación inicial:

- `main`: producción.
- `develop`: integración/staging.
- `feature/*`: cambios individuales.
- `hotfix/*`: incidencias críticas.
- Tags `vX.Y.Z`: releases.

Alternativa futura: trunk-based development con feature flags. No necesario hasta que el equipo crezca.

## 2.3 Configuración por entorno

Eliminar URLs hardcodeadas donde afecten al despliegue:

- `backendUrl` de la app.
- OAuth redirect URI.
- Webhook URL.
- Connection string.
- GitHub OAuth credentials.
- JWT secrets.
- PAT compartido, si continúa existiendo.

Usar:

- `appsettings.Development.json` sin secretos.
- `appsettings.Staging.json` sin secretos.
- `appsettings.Production.json` sin secretos sensibles.
- Variables de entorno/secret manager para secretos.
- Configuración de build o archivo de entorno para native.

Añadir una pantalla de entorno/debug visible solo en builds no productivas.

## 2.4 Criterios de salida

- Imposible desplegar a producción accidentalmente desde una feature branch.
- Staging puede probar OAuth, webhook, SignalR y SQLite sin tocar producción.
- App local nunca apunta a producción por accidente.
- README explica cómo identificar el entorno activo.

---

# 3. P0 — Dominio `statefalse.com` y eliminación progresiva de ngrok

## Estado actual — 2026-08-12

**Configuración de custom domain completada.** `api.statefalse.com` resuelve por HTTPS, `/health` responde `200`, webhooks reales de GitHub llegan y app nativa ya usa dominio propio. Falta validación end-to-end completa.

Incidencia detectada durante validación: backend procesa fallos (`failure handled`), pero conexión SignalR nativa recibía `400 Connection ID required` porque cliente abría WebSocket sin ejecutar negociación SignalR. Cliente actualizado para negociar antes de conectar. Pendiente desplegar y comprobar notificación real de build fallido.

## 3.1 Comprar y configurar dominio

- Registrar `statefalse.com` con proveedor fiable.
- Activar MFA y bloqueo de transferencia.
- Usar DNS gestionado por Cloudflare o proveedor equivalente.
- Configurar:
  - `statefalse.com` para landing.
  - `www.statefalse.com` redirigido a raíz.
  - `api.statefalse.com` para backend público.
  - `staging.statefalse.com` o `api-staging.statefalse.com` para staging.
  - `download.statefalse.com` opcional si se separan descargas.
- No activar wildcard multi-tenant todavía salvo necesidad real.
- Configurar SPF/DKIM/DMARC solo si se enviarán emails.

## 3.2 HTTPS y reverse proxy

Recomendación: Caddy antes que nginx para primera versión.

Razones:

- HTTPS automático con Let's Encrypt.
- Configuración pequeña.
- WebSocket/SignalR sencillo.
- Menos mantenimiento que nginx + certbot manual.

Arquitectura:

```text
Internet
  ├── https://statefalse.com       → landing estática
  ├── https://api.statefalse.com  → Caddy → Kestrel localhost:5000
  └── https://staging...           → staging independiente
```

Caddy debe:

- Escuchar 80/443.
- Redirigir HTTP a HTTPS.
- Enviar headers `X-Forwarded-*` correctamente.
- Soportar WebSocket para SignalR.
- No exponer Kestrel directamente.
- Tener logs rotados.

Firewall VPS:

- Permitir 22 solo desde IPs necesarias si es viable.
- Permitir 80/443.
- Mantener 5000 cerrado públicamente.
- Desactivar/eliminar túnel ngrok después de validar HTTPS.

## 3.3 Migración de URLs

Actualizar de forma coordinada:

- GitHub OAuth Homepage URL.
- GitHub OAuth callback URL.
- Webhook de cada repositorio.
- `WebhookSecret` por entorno.
- `TeamDefaults.backendUrl`.
- `UserDefaults` existente y migración de URL antigua.
- README.
- `ARQUITECTURA.md`.
- `docs.md`.
- systemd/Caddy.
- Health checks de CI/CD.

Mantener redirección o mensaje de migración para instalaciones antiguas que todavía usen ngrok.

## 3.4 Criterios de salida

- [x] `https://api.statefalse.com/health` responde `200`.
- [ ] SignalR conecta mediante HTTPS/WSS y registra conexión.
- [x] OAuth básico funciona con dominio propio.
- [x] Webhook real llega desde GitHub y procesa HMAC/configuración vigente.
- [x] App nueva usa el dominio propio.
- [x] ngrok detenido en producción.
- [ ] Build fallido produce notificación visible en app.

---

# 4. P0 — Landing page pública

## 4.1 Objetivo

Landing sencilla, rápida y honesta. No vender todavía. Explicar qué hace Statefalse, requisitos, privacidad y cómo descargar desde GitHub.

## 4.2 Tecnología recomendada

Primera versión:

- Sitio estático.
- GitHub Pages, Cloudflare Pages o similar.
- Dominio `statefalse.com`.
- Sin backend propio para la landing.
- HTTPS automático.
- Deploy desde rama `main` o `website`.

No usar Docker para la landing inicial salvo que se quiera uniformidad futura.

## 4.3 Secciones

1. Hero:
   - Logo propio.
   - Nombre `Statefalse`.
   - Frase: monitorización de PRs y GitHub Actions desde el menú bar de macOS.
   - CTA `Download from GitHub`.
   - CTA secundario `View source`.
2. Demo visual:
   - Captura o GIF corto del menú bar y Active PRs.
   - No incluir repos, nombres, tokens o datos reales.
3. Funcionalidades:
   - Active PRs.
   - Estados CI.
   - Notificaciones.
   - Workflows y rerun.
   - Gestión de ramas.
   - Creación de PRs.
   - Detección de conflictos.
4. Requisitos:
   - macOS compatible.
   - Cuenta GitHub.
   - Xcode/instalación desde código actualmente.
   - Explicar claramente que no está en App Store.
5. Instalación:
   - Descargar release o clonar repo.
   - Ejecutar instalación.
   - Abrir app.
   - Autorizar GitHub.
   - Configurar PAT solo si se necesitan acciones avanzadas.
6. Seguridad y privacidad:
   - Qué tokens se solicitan.
   - Dónde se guardan.
   - Qué datos pasan por el backend.
   - Cómo borrar cuenta/token.
   - Enlace a política de privacidad.
7. Estado del proyecto:
   - Early access / beta.
   - Limitaciones actuales.
   - Cómo reportar problemas.
8. FAQ:
   - Por qué no App Store.
   - Qué significa app no notarizada.
   - Cómo actualizar.
   - Cómo cambiar backend.
9. Footer:
   - GitHub.
   - Releases.
   - Docs.
   - Privacy.
   - Contacto.

## 4.4 Distribución inicial desde GitHub

Crear GitHub Releases con:

- `Statefalse-vX.Y.Z.zip`.
- `SHA256SUMS`.
- Changelog.
- Requisitos de macOS.
- Instrucciones de instalación.
- Aviso de firma/notarización.

El instalador actual basado en Xcode puede continuar para desarrolladores, pero usuarios normales deberían descargar un artefacto ya construido.

## 4.5 Limitación importante sin Apple Developer

Sin pagar Apple Developer Program:

- No hay notarización oficial.
- Gatekeeper puede mostrar advertencia.
- La primera ejecución puede requerir `Control + click → Open` o permitir la app en Privacy & Security.
- Los usuarios necesitarán confianza en el repo y en los checksums.
- No prometer experiencia equivalente a una app notarizada.

Mitigación temporal:

- Firmar ad hoc o con identidad local cuando sea posible.
- Publicar checksums SHA-256.
- Mantener builds reproducibles.
- Explicar la advertencia en la landing sin ocultarla.
- En el futuro, pagar Apple Developer antes de distribución amplia.

---

# 5. P1 — Logo y sistema visual propio

## 5.1 Dirección de diseño

Mantener concepto llama, pero crear identidad propia en vez de depender solo de `flame.fill` de SF Symbols.

Propuesta:

- Símbolo: llama geométrica compacta, reconocible a 16-24 px.
- Formas simples para funcionar en menú bar.
- Variante monocroma para menu bar.
- Variante color para app, landing y releases.
- Evitar detalles finos que desaparezcan en tamaño pequeño.
- No copiar logos de terceros ni usar una llama genérica sin diferenciación.

## 5.2 Entregables

- Logo principal SVG/PNG.
- Icono macOS `.icns` con tamaños requeridos.
- App icon para Asset Catalog.
- Variante menu bar monocroma.
- Favicon y favicon dark/light.
- Open Graph image.
- Avatar/social image.
- Guía breve de uso: colores, padding, tamaños mínimos.

## 5.3 Integración

- Añadir assets a `native/Assets.xcassets`.
- Sustituir/acompañar el icono SF Symbol del header.
- Mantener `flame.fill` como fallback funcional del menu bar si el asset no escala bien.
- Usar logo en la landing.
- Usar mismo nombre, color y tono en OAuth success page.
- Actualizar screenshots.
- Añadir snapshot/regression visual del header.

## 5.4 Criterios de aceptación

- Legible en menu bar claro y oscuro.
- Visible a 16 px.
- No depende de una fuente o recurso externo.
- Assets incluidos en el repo.
- El icono de app y el icono de menu bar no se confunden.

---

# 6. P1 — Distribución y release de la app macOS

## 6.1 Separar dos flujos

### Desarrollo

- Xcode abre `native/statefalse.xcodeproj`.
- Scheme `Statefalse`.
- Run en Mac local.
- Backend seleccionado por configuración local.

### Usuario final

- Descarga ZIP desde GitHub Release.
- Descomprime `Statefalse.app`.
- Mueve a `~/Applications` o `/Applications`.
- Abre y autoriza primera ejecución.
- No necesita Xcode.

## 6.2 Release workflow

Crear workflow que:

1. Ejecute tests Swift.
2. Compile Release.
3. Genere ZIP.
4. Genere checksum.
5. Cree GitHub Release con tag.
6. Publique changelog.
7. Opcionalmente actualice la landing.

No publicar automáticamente una release en cada push. Usar tag o aprobación manual.

## 6.3 Actualizaciones

Primera versión:

- Actualización manual desde GitHub Releases.
- Mostrar versión actual dentro de Settings.
- Enlace `Check for updates` hacia releases.

Futuro:

- Sparkle para auto-update.
- Requiere resolver firma, feed y seguridad de actualización.
- No priorizar antes de tener firma/notarización estable.

---

# 7. P1 — CI/CD profesional

## 7.1 Pipeline de validación

Unificar o coordinar checks para Pull Requests:

- Backend restore.
- Backend build.
- Backend unit/integration tests.
- Coverage y umbral mínimo gradual.
- Swift build.
- Swift tests.
- Lint/format si se adopta herramienta.
- Scan de secretos.
- Scan de dependencias.
- Validación de Dockerfile y compose cuando existan.
- Validación de documentación/links opcional.

Corregir primero el job de deploy para que no dependa accidentalmente de artefactos restaurados por otro job.

## 7.2 Artefactos

Guardar en cada pipeline:

- `.trx` backend.
- Coverage Cobertura.
- `.xcresult` native.
- Logs de build.
- Binary/package de release cuando corresponda.

## 7.3 Deploy staging

- Push/merge a `develop` → tests → build → deploy staging.
- Health check.
- Smoke tests:
  - `/health`.
  - OpenAPI.
  - conexión SignalR.
  - OAuth redirect configurado.
  - webhook de prueba.

## 7.4 Deploy production

- Tag/release o merge aprobado a `main`.
- GitHub Environment `production`.
- Approval manual.
- Backup antes del deploy.
- Publicación a directorio versionado.
- Health check.
- Smoke test externo.
- Notificación de éxito/fallo.
- Rollback documentado.

## 7.5 Rollback

Implementar al menos una de estas estrategias:

- Directorios `/opt/statefalse/releases/<version>` y symlink `current`.
- Mantener última versión funcional.
- `systemctl revert` o script de rollback.
- Backup DB vinculado a cada migración.

Evitar `rsync --delete` sobre el único directorio vivo sin copia previa.

## 7.6 SSH deploy

Mejoras:

- Usuario de deploy no root.
- SSH key dedicada y de mínimo alcance.
- `known_hosts` fijo, no `ssh-keyscan` ciego en cada ejecución.
- Comandos sudo limitados a systemctl/deploy.
- Secrets en GitHub Environment, no variables globales.
- Auditar logs de deploy.

---

# 8. P1 — Docker: introducirlo sin romper la operación actual

Docker no es obligatorio para el primer producto, pero sí útil para staging, reproducibilidad y futuro multi-tenant.

## 8.1 Orden recomendado

1. Crear `backend/Dockerfile` o Dockerfile de raíz bien documentado.
2. Crear `.dockerignore`.
3. Crear `compose.dev.yml` o `docker-compose.dev.yml`.
4. Ejecutar backend local en contenedor.
5. Crear `compose.staging.yml`.
6. Validar migraciones, SQLite, volumen y SignalR.
7. Migrar staging a Docker.
8. Migrar producción solo después de backup y rollback probado.

## 8.2 Imagen

Usar multi-stage build:

- SDK .NET 10 para build.
- Runtime ASP.NET o runtime-deps según publicación.
- Usuario no root.
- Puerto interno 5000.
- Healthcheck.
- Imagen mínima.
- Tags inmutables por commit/version.
- No usar `latest` en producción.

## 8.3 Volúmenes y SQLite

- Persistir `/data/statefalse.db` en volumen host.
- Nunca guardar DB dentro de capa efímera del contenedor.
- Backup antes de recrear contenedor.
- Comprobar locking SQLite y una sola instancia escritora.
- Si el volumen o concurrencia dejan de ser suficientes, evaluar PostgreSQL; no migrar antes de necesitarlo.

## 8.4 Compose

Servicios iniciales:

- `api`.
- `caddy` o reverse proxy separado.
- Opcional `backup`.

No incluir ngrok en producción nueva. Mantener túnel solo para desarrollo webhooks si resulta útil.

## 8.5 Criterios de salida

- `docker compose up` reproduce entorno local.
- Tests no dependen de Docker si no lo necesitan.
- Staging se reconstruye desde cero sin pasos manuales ocultos.
- Persistencia y restore documentados.

---

# 9. P1 — Guía oficial de desarrollo local

Crear `DEVELOPMENT.md` como guía única. Debe cubrir macOS y backend.

## 9.1 Requisitos

- macOS compatible.
- Xcode y Command Line Tools.
- .NET 10 SDK.
- Git.
- Opcional Docker Desktop.
- Acceso GitHub para OAuth/API.

Comandos de comprobación:

```zsh
xcodebuild -version
dotnet --version
git --version
docker --version
```

## 9.2 Clonar y restaurar

```zsh
git clone git@github.com:trashdb/statefalse.git
cd statefalse
dotnet restore Statefalse.slnx
```

## 9.3 Arrancar backend local

```zsh
cd backend
dotnet run
```

Documentar URLs esperadas:

- API: `http://localhost:5000`.
- Health: `http://localhost:5000/health`.
- OpenAPI/Scalar: según configuración vigente.

Comprobar:

```zsh
curl -i http://localhost:5000/health
```

Indicar dónde se crea la SQLite local y cómo borrarla para reiniciar datos.

## 9.4 Configuración local

Documentar:

- `appsettings.Development.json`.
- Variables requeridas.
- OAuth App de desarrollo.
- Redirect URI local si se usa.
- Secretos de ejemplo sin valores reales.
- PAT opcional y riesgos de usarlo.
- Diferencia entre OAuth token y PAT.

## 9.5 Arrancar native desde Xcode

```zsh
open native/statefalse.xcodeproj
```

En Xcode:

1. Seleccionar scheme `Statefalse`.
2. Seleccionar destino `My Mac`.
3. Revisar configuración de backend local.
4. Pulsar Run/Play.
5. Abrir icono del menú bar.
6. Ejecutar login de GitHub.
7. Revisar logs de Xcode y Activity Log si existe.

Explicar que la app es `LSUIElement`: no aparece como app normal en Dock.

## 9.6 Instalar build local

```zsh
cd native
bash install.sh
```

Documentar:

- Ruta `~/Applications/Statefalse.app`.
- Reemplazo de instancia previa.
- Cómo cerrar/reabrir.
- Cómo borrar preferencias y Keychain solo para reset de desarrollo.

## 9.7 Tests

Backend:

```zsh
cd tests-backend
dotnet test
```

Native:

```zsh
cd native
xcodebuild test \
  -scheme StatefalseTests \
  -project statefalse.xcodeproj \
  -destination 'platform=macOS'
```

Explicar por qué CI omite snapshots headless si sigue siendo necesario.

## 9.8 Desarrollo de webhooks

Documentar tres opciones:

1. Payloads simulados con `curl`.
2. ngrok local temporal.
3. Endpoint de staging.

Nunca apuntar por accidente webhooks de repositorios productivos al backend local.

## 9.9 Troubleshooting

Añadir soluciones para:

- Puerto 5000 ocupado.
- OAuth callback no llega.
- Keychain bloqueado.
- App no aparece en menu bar.
- SignalR no conecta.
- SQLite bloqueada.
- Xcode firma o no encuentra destino.
- Git CLI no encuentra repos.
- Gatekeeper en builds descargadas.

---

# 10. P1 — Auditoría técnica y refactors pendientes

## 10.1 Backend

Prioridad alta:

- Revisar referencias de `Application` a EF Core/AppDbContext y alinearlas con la convención repository/unit-of-work.
- Evitar que servicios de Application conozcan infraestructura.
- Crear repositorios para acceso a datos todavía directo.
- Revisar scopes y concurrencia de `DbContext`.
- Revisar índices de `PullRequestEvents`, autor, repo y workflow.
- Introducir paginación real en endpoints públicos.
- Versionar API (`/api/v1`) sin romper cliente actual.
- Separar DTOs externos de entidades persistentes.
- Añadir idempotency keys o deduplicación robusta para webhooks.
- Implementar cola/reintento de webhook si el procesamiento crece.
- Revisar self-healing: coste de llamadas a GitHub por PR.
- Añadir cache con límites y expiración antes de introducir Redis.
- Medir GitHub API rate limit.
- Revisar cancellation tokens y timeouts de HttpClient.
- Revisar errores consistentes mediante `ApiResult`.
- Revisar endpoints legacy, especialmente interpretación de lenguaje natural.

Prioridad media:

- Health checks separados: liveness y readiness.
- Métricas de sincronización, webhooks y latencia.
- Background jobs con estado visible.
- Revisión de migraciones automáticas en producción.
- Evaluar PostgreSQL solo cuando SQLite sea limitación real.

## 10.2 Native

Prioridad alta:

- Reducir singletons de panel managers.
- Separar estado de UI de reglas de dominio.
- Mantener protocolos/mocks para servicios restantes.
- Centralizar configuración de entorno.
- Revisar lifecycle de SignalR y reconexión.
- Revisar cancelación de Tasks al cerrar paneles.
- Revisar concurrencia Swift 6/MainActor.
- Revisar almacenamiento de sesión y logout completo.
- Añadir versión visible en Settings.
- Añadir Activity Log para diagnóstico sin Xcode.

Prioridad media:

- Accesibilidad VoiceOver.
- Dynamic Type.
- Reduced Motion.
- Keyboard navigation completa.
- High contrast.
- Notificaciones agrupadas.
- Abrir archivos del diff en IDE.
- Snapshot testing estable.
- Filtros Active PRs por repo/estado.
- Cache offline más completa.

Futuro:

- Widget macOS.
- Shortcuts/App Intents avanzados.
- Sparkle auto-update.
- Modo multi-tenant seleccionable desde app.

## 10.3 Documentación

- Consolidar `README.md` como entrada de usuario.
- Crear `DEVELOPMENT.md` para desarrollo.
- Crear `OPERATIONS.md` para VPS, backups, logs, rollback y alertas.
- Crear `SECURITY.md` para threat model y reporte de vulnerabilidades.
- Crear `RELEASE.md` para versionado y publicación.
- Convertir `ARQUITECTURA.md` en arquitectura actual, no mezcla de planes.
- Mantener `MULTI-TENANT.md` como RFC futuro y marcar supuestos.
- Eliminar URLs ngrok obsoletas tras migración.
- Evitar IPs, nombres de repos y datos personales innecesarios en documentación pública.

---

# 11. P1 — Observabilidad y operación

## 11.1 Monitorización mínima

- Uptime check externo para `https://api.statefalse.com/health`.
- Alertas por caída del API.
- Alertas por servicio systemd/container reiniciándose.
- Alertas por disco lleno.
- Alertas por backup fallido.
- Alertas por certificado próximo a expirar si el proxy no lo gestiona automáticamente.
- Revisión de logs sin depender siempre de SSH.

## 11.2 Endpoints de salud

Separar:

- Liveness: proceso vivo.
- Readiness: DB disponible y migraciones aplicadas.
- Dependencias opcionales: GitHub API no debería hacer caer liveness.

## 11.3 Logs

- JSON estructurado en backend.
- Correlation ID por request/webhook.
- Event ID de GitHub para deduplicación.
- Redacción automática de secretos.
- Rotación y retención.
- Activity Log nativo con exportación segura.

## 11.4 Runbooks

Crear procedimientos para:

- API caída.
- Webhooks retrasados.
- OAuth roto.
- DB corrupta.
- Migración fallida.
- Deploy fallido.
- Rollback.
- Rotación de secreto.
- Pérdida del VPS.
- Restauración completa.

---

# 12. P2 — Calidad de producto y experiencia

## 12.1 UX

- Empty states consistentes.
- Errores accionables.
- Loading states.
- Confirmación para acciones destructivas.
- Estado offline visible.
- Indicador de entorno en debug.
- Filtros y búsqueda de PRs.
- Notificaciones agrupadas.
- Mejor detalle de PR.
- Preferencias con icono/logo propio.

## 12.2 Testing

Objetivo mínimo:

- Unit tests de dominio y servicios críticos.
- Integration tests con DB real temporal.
- Contrato OpenAPI validado.
- Tests de webhook para todos los eventos y acciones relevantes.
- Tests de idempotencia y duplicados.
- Tests de aislamiento entre usuarios.
- Tests de OAuth fallido/caducado.
- Tests SignalR reconexión.
- Tests de migración desde DB existente.
- Smoke test staging.
- E2E básico solo para caminos críticos.

No perseguir cobertura del 100%; priorizar autenticación, aislamiento, webhooks, acciones destructivas y sincronización.

## 12.3 Accesibilidad

- VoiceOver labels.
- Contraste.
- Tamaño de texto.
- Reduced Motion.
- Navegación por teclado.
- Estados no comunicados solo por color.

---

# 13. P2 — Multi-tenant y uso por equipos

No empezar antes de tener staging, seguridad, backups y dominio funcionando.

## 13.1 Primera decisión

Elegir entre:

### Modelo A: instancia por organización

- Una app/backend por tenant.
- SQLite independiente.
- OAuth App y webhook secret independientes.
- Menor riesgo de fuga de datos.
- Más operación.

### Modelo B: multi-tenant dentro de una instancia

- TenantId en todas las entidades.
- Aislamiento obligatorio en cada query.
- OAuth/webhooks con resolución de tenant.
- Mayor eficiencia, mayor riesgo.

Recomendación inicial: instancia por organización si habrá pocos equipos; no construir plataforma multi-tenant completa antes de tener usuarios reales.

## 13.2 Antes de abrir a equipo

- Tests de aislamiento.
- Roles y permisos.
- Auditoría de acciones.
- Revocación de usuarios.
- Gestión de OAuth por organización.
- Política de retención.
- Consentimiento y privacidad.
- Rate limits por tenant.
- Backup/restauración por tenant.

## 13.3 Dominio futuro

- `easyjet.statefalse.com`.
- `team.statefalse.com`.
- Wildcard DNS/TLS solo cuando exista necesidad.
- Automatizar provisioning, no hacerlo con comandos manuales.

---

# 14. P3 — Firma, notarización y App Store

No es requisito inmediato, pero será importante para distribución amplia.

## Cuando pagar Apple Developer

Pagar los 99 USD/anuales cuando ocurra cualquiera:

- Más de unos pocos usuarios externos.
- El equipo necesite instalación sin advertencias.
- Se quiera actualizador automático fiable.
- Se quiera distribución empresarial seria.
- Se quiera publicar en App Store.

## Después del alta

- Developer ID Application.
- Firma de app y helpers.
- Notarización.
- Staple del ticket.
- DMG/ZIP firmado.
- Sparkle firmado si se añade auto-update.
- CI con certificados protegidos.
- No almacenar certificados en texto plano.

App Store puede continuar siendo una decisión posterior. La distribución directa notarizada probablemente encaja mejor antes que App Store para una herramienta interna.

---

# 15. P3 — Monetización y producto comercial

No priorizar hasta validar uso real.

Posibles fases:

1. Gratis para uso personal.
2. Beta para equipo pequeño.
3. Medir usuarios activos, PRs gestionadas y retención.
4. Definir si hay costes de backend por organización.
5. Licencias solo si existe demanda.
6. Stripe/planes únicamente después de resolver privacidad, soporte y facturación.

No implementar aún:

- Stripe.
- Seats.
- Trial.
- Licencias por dispositivo.
- Pricing complejo.

La documentación existente de monetización debe marcarse como propuesta, no como funcionalidad actual.

---

# 16. Cronograma recomendado

## Semana 1: control y seguridad

- Proteger `main`.
- Auditar secretos, CORS, JWT y webhooks.
- Tag de versión.
- Backup y restore real.
- Corregir documentación peligrosa/obsoleta.
- Crear `SECURITY.md` y `OPERATIONS.md`.

## Semana 2: entornos

- Crear configuración local/staging/prod.
- Preparar OAuth Apps separadas.
- Crear staging.
- Separar DBs y secretos.
- Crear `develop` y GitHub Environments.
- Añadir smoke tests.

## Semana 3: dominio e infraestructura

- Comprar/configurar `statefalse.com`.
- Configurar DNS.
- Instalar Caddy.
- Publicar API en `api.statefalse.com`.
- Migrar OAuth/webhooks.
- Validar SignalR y eliminar ngrok de producción.

## Semana 4: distribución y landing

- Crear landing estática.
- Crear GitHub Release inicial.
- Generar ZIP y SHA256.
- Documentar Gatekeeper/no notarización.
- Crear guía de instalación de usuario.

## Semana 5: logo y release pipeline

- Diseñar logo propio.
- Integrar Asset Catalog, menu bar, landing y favicon.
- Crear workflow de release native.
- Añadir versión visible y changelog.

## Semanas 6-7: CI/CD y rollback

- Revisar restore/build/test del pipeline.
- Añadir deploy staging.
- Añadir approval producción.
- Deploy versionado.
- Backup pre-deploy.
- Rollback probado.
- Usuario de deploy no root.

## Semanas 8-9: Docker y operación

- Dockerfile multi-stage.
- Compose local.
- Migrar staging a Docker.
- Volumen SQLite y backup.
- Caddy + Docker.
- Decidir si producción migra o conserva systemd.

## Semanas 10-12: refactor y calidad

- Repository/unit-of-work donde falte.
- Paginación e índices.
- API versioning.
- Webhook idempotency/retry.
- Activity Log.
- Accesibilidad.
- Tests críticos y cobertura.

## Después de 12 semanas

- Multi-tenant si existe demanda.
- Firma/notarización al aumentar usuarios.
- Sparkle.
- Widget/Shortcuts.
- Monetización.
- PostgreSQL si SQLite deja de ser suficiente.

---

# 17. Backlog final priorizado

## P0 — imprescindible antes de equipo

- [ ] Proteger `main` y exigir PR.
- [ ] Separar local, staging y producción.
- [ ] Backups y restore probado.
- [ ] Auditoría de seguridad.
- [ ] CORS y secretos revisados.
- [x] Dominio `statefalse.com`.
- [x] `api.statefalse.com` con HTTPS.
- [x] Migrar OAuth/webhooks desde ngrok.
- [ ] Validar SignalR y notificación de build fallido end-to-end.
- [ ] Landing inicial.
- [ ] GitHub Releases descargables.
- [ ] Guía `DEVELOPMENT.md`.
- [ ] Guía de instalación de usuario.

## P1 — profesionalización inmediata

- [ ] Logo propio.
- [ ] Workflow de release native.
- [ ] Staging deploy automático.
- [ ] Production approval.
- [ ] Rollback versionado.
- [ ] Usuario SSH de deploy no root.
- [ ] Monitorización y alertas.
- [ ] Runbooks operativos.
- [ ] Docker local/staging.
- [ ] Activity Log.
- [ ] Refactor repository/unit-of-work.
- [ ] Paginación, índices y API versioning.
- [ ] Webhook idempotency/retry.
- [ ] Accessibility básica.

## P2 — producto maduro

- [ ] Migrar producción a Docker si aporta valor.
- [ ] Notificaciones agrupadas.
- [ ] Mejoras de PR detail.
- [ ] Cache offline ampliada.
- [ ] E2E crítico.
- [ ] Widget.
- [ ] Shortcuts.
- [ ] Multi-tenant por instancia.

## P3 — futuro

- [ ] Apple Developer Program.
- [ ] Firma y notarización.
- [ ] Sparkle auto-update.
- [ ] App Store.
- [ ] Stripe/licencias.
- [ ] PostgreSQL.
- [ ] Multi-tenant completo con roles y billing.

---

# 18. Definición de producto listo para equipo

Statefalse estará listo para que lo use el equipo cuando:

- Cada desarrollador tenga entorno local documentado.
- Nadie pueda alterar producción por push accidental.
- Staging reproduzca el flujo real.
- Producción tenga dominio HTTPS propio.
- OAuth, webhooks y SignalR funcionen sin ngrok.
- Existan backups restaurables.
- Haya rollback probado.
- Los secrets no aparezcan en código ni logs.
- La app pueda descargarse sin Xcode desde GitHub Releases.
- Landing explique requisitos y limitaciones.
- Logo y branding sean consistentes.
- CI valide backend, native y artefactos.
- Haya monitorización y responsable de incidentes.
- El equipo sepa cómo reportar errores y dónde consultar logs.



