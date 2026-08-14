# statefalse — Guía Completa de Arquitectura

## Arquitectura General

```
[macOS App] ←SignalR+REST→ [ngrok tunnel] → [ASP.NET Kestrel:5000] → [SQLite DB]
                              ↑                                          ↑
                         (Hetzner VPS)                        /var/lib/statefalse/
                                                                   statefalse.db
```

- **Backend**: App .NET 10 self-hosted en un VPS de Hetzner con systemd
- **Frontend**: App macOS nativa (SwiftUI) como menu bar utility (sin Dock, sin ventana principal)
- **Túnel público**: ngrok gratuito para recibir webhooks de GitHub
- **Sin Docker, sin nginx**

---

## Backend (`backend/`)

### Stack

| Componente | Tecnología |
|------------|-----------|
| Runtime | .NET 10 (`net10.0`) |
| API | ASP.NET Core Minimal API (sin MVC controllers) |
| ORM | Entity Framework Core 10 + SQLite |
| Tiempo real | SignalR (WebSocket) |
| Auth | GitHub OAuth + sesión JWT (JwtBearer) |
| Rate limiting | `AddRateLimiter` (políticas `api` y `webhook`) |
| Logs | Serilog (console + archivo rotativo, retención 30 días) |
| API docs | OpenAPI + Scalar (`/scalar`) |

### Estructura de archivos (Clean Architecture, 4 proyectos)

```
backend/
├── Program.cs                          # Composition root: DI, CORS, auth, rate limit, migrations
├── ApiEndpoints.cs                     # Minimal API routes (reemplaza los antiguos controllers)
├── Statefalse.Api.csproj               # Entry point web (→ Application, Infrastructure)
│
├── Statefalse.Domain/                  # CERO dependencias
│   ├── Models/                         # Entidades EF: GitHubUser, WorkflowRun, PullRequestEvent,
│   │                                   #   CheckSuiteEvent, PunishmentEvent
│   ├── Contracts/                      # DTOs: AuthDtos, GitHubDtos, HubEventDtos, PullRequestDtos,
│   │                                   #   PunishmentDtos, WorkflowDtos
│   └── Mappers/                        # Lógica pura: CiStatusCalculator, WorkflowConclusionMapper,
│                                       #   IdListSerializer, IgnoredWorkflows, CheckRunStatusMapper,
│                                       #   UtcDateTimeConverter
│
├── Statefalse.Application/             # → Domain. Lógica de negocio, HTTP-agnóstica
│   ├── ApiResult.cs                    # Resultado de servicio: status + value (sin IActionResult)
│   ├── IAppDbContext.cs                # Contrato de persistencia (DbSet + SaveChanges)
│   ├── ISignalRNotifier.cs             # Abstracción de notificaciones SignalR
│   ├── Services/                       # AuthService, WebhookService, PullRequestQueryService,
│   │                                   #   PullRequestSyncService, PullRequestActionService,
│   │                                   #   WorkflowService, GitHubApiService, AiService,
│   │                                   #   PunishmentService, JwtTokenService, ...
│   │   ├── IWebhookHandler.cs          # Dispatch por X-GitHub-Event
│   │   └── *WebhookHandler.cs          # workflow_run, check_suite, pull_request, review,
│   │                                   #   issue_comment, review_comment
│   └── GlobalUsings.cs                 # usings globales de la capa
│
└── Statefalse.Infrastructure/          # → Application. EF, HTTP, SignalR
    ├── Data/AppDbContext.cs            # DbContext: 5 DbSets + índices
    ├── Migrations/                     # Migraciones EF (3: InitialCreate, AddSubscriberIds, Baseline)
    ├── GitHubClient.cs                 # IGitHubClient — thin client REST/GraphQL
    ├── GitHubTokenResolver.cs          # IGitHubTokenResolver — precedencia de tokens
    ├── Hubs/PunishmentHub.cs           # SignalR hub: RegisterConnection, user groups
    ├── SignalRNotifier.cs              # ISignalRNotifier — envía eventos al hub
    └── WorkflowCleanupService.cs       # BackgroundService: marca runs stuck/superseded
```

### Capas y dependencias

```
Api → Application → Domain
   ↘ Infrastructure → Application → Domain
```

- `Domain`: cero paquetes, cero framework.
- `Application`: referencia `Domain` + abstracciones ASP.NET (`Microsoft.AspNetCore.App`) y EF Core para el contrato `IAppDbContext`.
- `Infrastructure`: implementa contratos de Application (EF Core, HttpClient, SignalR).
- `Api`: compone todo en `Program.cs` (composition root).

### Base de datos (SQLite)

| Tabla | Propósito |
|-------|-----------|
| `GitHubUsers` | Usuarios con OAuth token + PAT opcional + conexión SignalR |
| `WorkflowRuns` | Cada ejecución de workflow (status: in_progress, success, failure, cancelled, superseded) |
| `PullRequestEvents` | PRs abiertos/mergeados con estado de CI, aprobación, comentarios, subscriptores |
| `CheckSuiteEvents` | Check suites completadas (para notificar al autor) |
| `PunishmentEvents` | Histórico de "castigos" por workflows fallidos |

### API endpoints (Minimal API)

#### Auth
| Método | Ruta | Función | Auth |
|--------|------|---------|------|
| GET | `/api/v1/auth/login` | Redirige al usuario a GitHub OAuth | anónimo |
| GET | `/api/auth/callback` | Exchange code → token, upsert usuario, redirect a app | anónimo |
| GET | `/api/v1/auth/me` | Perfil del usuario | JWT |
| POST | `/api/v1/auth/pat` | Guardar/borrar PAT del usuario | JWT |
| GET | `/api/v1/auth/token` | Token efectivo resuelto | JWT |

#### Pull Requests
| Método | Ruta | Función | Auth |
|--------|------|---------|------|
| POST | `/api/v1/pullrequests/sync` | Sync desde GitHub API | JWT+limit |
| GET | `/api/v1/pullrequests/active?page=&pageSize=` | PRs activos con ciStatus, comentarios, self-healing | JWT+limit |
| GET | `/api/v1/pullrequests/{n}/detail?repo=` | Mergeable state, behind/ahead | JWT+limit |
| GET | `/api/v1/pullrequests/{n}/commits` · `/files` · `/checks` | Proxies a GitHub | JWT |
| POST | `/api/v1/pullrequests/{n}/merge` · `/draft` · `/update-branch` | Acciones | JWT |
| POST | `/api/v1/pullrequests/{n}/subscribe` · `/unsubscribe` | Subscripción a PR | JWT+limit |
| GET | `/api/v1/pullrequests/{n}/subscribers` | Lista subscriptores | JWT+limit |
| POST | `/api/v1/pullrequests/{n}/add-subscriber` · `/remove-subscriber` | Gestionar subscriptores | JWT+limit |

#### Workflows
| Método | Ruta | Función | Auth |
|--------|------|---------|------|
| GET | `/api/v1/workflows/runs?limit=` | Runs recientes (propios + targeted + subscribed) | JWT+limit |
| PUT | `/api/v1/workflows/runs/{id}/target` | Asignar usuarios a notificar | JWT |
| POST | `/api/v1/workflows/runs/{runId}/rerun` | Re-ejecutar workflow | JWT |
| POST | `/api/v1/workflows/sync-active` | Sincroniza runs in_progress desde GitHub API | JWT |

#### GitHub API proxy
| Método | Ruta | Función | Auth |
|--------|------|---------|------|
| GET | `/api/v1/github/my-branches?repo=` | Ramas del usuario en un repo | JWT |
| POST | `/api/v1/github/create-pr` | Crear PR | JWT |
| POST | `/api/v1/github/pr-preview` | Preview con template + commits + resumen Copilot | JWT |
| POST | `/api/v1/github/interpret` | Interpretar lenguaje natural (legacy, fuera del UI) | JWT |

#### Webhook + sistema
| Método | Ruta | Función | Auth |
|--------|------|---------|------|
| POST | `/api/webhook/github` | Webhook GitHub, verificación HMAC-SHA256 | anónimo+limit |
| GET | `/api/v1/webhook/logs` | Ring buffer de últimos webhooks | JWT |
| GET | `/health` | Health check (DB connect) | anónimo |
| GET | `/api/v1/punishments` · `/api/v1/punishments/summary` | Histórico de castigos | JWT |
| GET | `/api/v1/users` | Lista usuarios registrados | JWT |
| GET | `/scalar` · OpenAPI | Documentación | - |

### Webhooks de GitHub que maneja

| Evento | Acciones | Qué hace |
|--------|----------|----------|
| `workflow_run` | in_progress, requested, completed | Crea/actualiza WorkflowRun, supersede siblings, persiste castigos, notifica SignalR |
| `check_suite` | requested, completed | Crea CheckSuiteEvent, notifica al autor |
| `pull_request` | opened, synchronize, closed, ready_for_review, converted_to_draft | Crea/actualiza PullRequestEvent |
| `pull_request_review` | submitted | Marca approved, notifica `PrApproved` |
| `issue_comment` | created (en PRs) | Notifica `PrCommented` |
| `pull_request_review_comment` | created | Notifica `PrCommented` con file/line |

### Señales de SignalR que envía al cliente

| Evento | Payload | Cuándo |
|--------|---------|--------|
| `WorkflowRunStarted` | id, runId, workflowName, repo, branch, actor | Workflow empieza |
| `WorkflowRunCompleted` | runId, succeeded, conclusion, workflowName, repo, actor | Workflow termina |
| `PullRequestsUpdated` | *(ninguno)* | Cualquier cambio en PRs → cliente refetch |
| `PrApproved` | prNumber, repo, reviewerLogin, title | PR aprobado |
| `PrCommented` | prNumber, repo, commenterLogin, commentBody, commentUrl, filePath, line | Comentario nuevo |
| `MainBranchUpdated` | repo, prNumber, mergedBy, headSha | PR mergeado a main |
| `CheckSuiteStarted` / `CheckSuiteCompleted` | checkSuiteId, appName, repo, branch, prNumber, author | Check suite events |

### Gestión de tokens (orden de prioridad)
```
UserPatToken (PAT propio) > AccessToken (OAuth) > GitHub:PatToken (PAT compartido del servidor)
```

### Sesión JWT
- Emitido por `JwtTokenService` en login/callback
- Requerido en todos los endpoints salvo `/health`, login/callback y webhook
- SignalR lee el token del query param `access_token` (WebSocket no puede setear headers)
- Config en `Jwt:Secret` (min 32 bytes, env var en systemd) + `Jwt:Issuer` + `Jwt:Audience`
- Client detecta 401 → auto-logout

### Flujo de OAuth
1. App abre `{backend}/api/v1/auth/login?redirect_uri=http://localhost:{random_port}/callback`
2. Backend redirige a GitHub → usuario autoriza → GitHub redirige a `/api/auth/callback`
3. Backend cambia code por access_token, busca/crea usuario en DB, emite JWT, redirige de vuelta a `localhost`
4. App captura la respuesta en un `NWListener` TCP, extrae `id`, `username`, `avatar`, `token`
5. App guarda sesión en Keychain (opcional, si "Keep signed in")

---

## Frontend macOS (`native/`)

### Stack
| Componente | Tecnología |
|------------|-----------|
| UI | SwiftUI (sin storyboards ni xibs) |
| Ventanas | `NSPanel` flotantes para views modales (vía `PanelFactory` + managers) |
| Menú bar | `MenuBarExtra` con estilo `.window` |
| SignalR | `URLSessionWebSocketTask` — protocolo manual (sin librería) |
| OAuth | `NWListener` TCP local para capturar callback |
| Git | Shell out a `git` CLI via `Process` |
| Keychain | Security framework directamente |
| Dependencias externas | **CERO** — solo Apple SDKs |

### Estructura de archivos

```
native/
├── App/
│   ├── StatefalseApp.swift          # @main, MenuBarExtra, LoginItem, inyecta Dependencies
│   └── AppIntents.swift             # Shortcuts / AppIntents
├── Models/Models.swift              # DTOs Swift + backendUrl (UserDefaults)
├── Services/
│   ├── ServiceProtocols.swift       # Protocolos: Git, SignalR, Keychain, Persistence, OAuth, ConflictWatcher, ApiClient
│   ├── Dependencies.swift           # Struct Dependencies + EnvironmentValues (DI)
│   ├── SignalRService.swift         # Facade: estado observable UI + reglas de dominio
│   ├── SignalRClient.swift          # Transporte WebSocket + parseo de eventos
│   ├── ApiClient.swift              # Todas las llamadas REST + JWT + detección 401
│   ├── DTOMapper.swift              # Api* → modelos UI
│   ├── WorkflowEventReducer.swift   # Lógica pura de reducción de eventos de workflow
│   ├── ReadyMergeNotifier.swift     # Deduplica notificaciones "ready to merge"
│   ├── GitService.swift             # Git CLI actor
│   ├── OAuthService.swift           # GitHub login via NWListener
│   ├── KeychainService.swift        # Sesión persistente
│   ├── PersistenceService.swift     # Cache offline (UserDefaults/JSON)
│   ├── NotificationManager.swift    # Sonidos + dispatch a CustomNotification
│   ├── CustomNotification.swift     # NSPanel flotante tipo banner
│   ├── ConflictWatcherService.swift # Detecta conflictos (poll + SignalR)
│   ├── MenuBarBadgeService.swift    # Contadores para el menú bar
│   └── MockServices.swift           # Mocks para tests
├── ViewModels/PRDetailViewModel.swift
├── Views/
│   ├── ContentView.swift            # Popover principal (400×820)
│   ├── MenuBarLabelView.swift       # Label del menú bar (4 modos)
│   ├── SignInCardView.swift / LoggedInCardView.swift / KeepSignedInToggleView.swift
│   ├── ActivePRsView.swift / PRDetailView.swift / PRDetailPanelManager.swift
│   ├── LocalBranchesView.swift / BranchDetailView.swift / BranchDetailPanelManager.swift
│   ├── CreatePRPreviewView.swift / PRPreviewPanelManager.swift
│   ├── QuickSearchView.swift        # Spotlight ⌘K
│   ├── WorkflowHistoryView.swift / WorkflowHistoryPanelManager.swift
│   ├── WebhookLogView.swift / WebhookLogPanelManager.swift
│   ├── LastNotificationCardView.swift / EmptyNotificationView.swift
│   ├── SettingsView.swift / SettingsPanelManager.swift
│   └── PanelFactory.swift           # Dedupe de panel managers
└── Utils/
    ├── DesignSystem.swift           # DS.Color/Font/Spacing/Radius/Animation + componentes
    ├── IDEOpener.swift              # 27 IDEs detectados
    ├── RemoteImageView.swift
    └── TeamDefaults.swift           # Defaults hardcodeados del equipo
```

### Cómo funciona la app

1. **Inicio**: `SMAppService.mainApp.register()` → auto-arranque. `MenuBarExtra` con icono 🔥 + popover.
2. **Login**: `OAuthService` abre Safari para OAuth de GitHub. App captura callback vía TCP local. Sesión opcional en Keychain.
3. **Tiempo real**: `SignalRService` (facade) delega en `SignalRClient` (WebSocket a `/hub/punishment`) y `ApiClient` (REST).
4. **PRs activos**: Cada 30s (o al recibir `PullRequestsUpdated`), refetch `GET /api/v1/pullrequests/active`.
5. **Acciones en PRs**: Desde el popover de detalle: togglear draft, merge, update branch, comentarios.
6. **Ramas locales**: `GitService` descubre repos recursivamente (max 3 niveles) desde `workspacePath`.
7. **Spotlight (⌘K)**: Queries inteligentes: ticket Jira, checkout de rama, crear PR, abrir board.
8. **Detección de conflictos**: Cada 60s + cuando alguien mergea a `main`, compara archivos.
9. **Cache offline**: `PersistenceService` guarda PRs y workflows en Application Support.

### UserDefaults keys

| Key | Default | Para qué |
|-----|---------|----------|
| `backendUrl` | `https://api.statefalse.com` | URL del backend |
| `workspacePath` | `~/Desktop/dev` | Dónde buscar repos |
| `jiraBoardUrl` | `https://easyjet.atlassian.net/browse/` | Base para tickets |
| `jiraBoardViewUrl` | URL del board LOY | Vista del board |
| `favoriteRepo` | `dcp-loyalty-monorepo` | Repo favorito |
| `defaultIDE` | `rider` | IDE por defecto |
| `customIDECommand` | `""` | Comando IDE custom |
| `menuBarWidgetMode` | `"Minimal"` | Modo del menú bar |

### Cómo se instala la app (`native/install.sh`)
```bash
xcodebuild -project statefalse.xcodeproj -scheme Statefalse -configuration Release build
cp Statefalse.app ~/Applications/
lsregister -f ~/Applications/Statefalse.app
pkill -x Statefalse; open ~/Applications/Statefalse.app
```

---

## Despliegue

### Servidor (Hetzner VPS)

| Propiedad | Valor |
|-----------|-------|
| Host | `underlayer` (alias SSH) |
| IP | `49.13.88.205` |
| Usuario | `root` |
| SSH key | `~/.ssh/underlayer_ci_deploy` |
| OS | Linux (Debian/Ubuntu) |
| App path | `/opt/statefalse/` |
| DB path | `/var/lib/statefalse/statefalse.db` |

### Servicios systemd (`deploy/`)

1. **`statefalse.service`**: Ejecuta `Statefalse.Api` en `localhost:5000` (envs: Jwt, GitHubOAuth, WebhookSecret, ConnectionStrings)
2. **Nginx compartido**: publica `api.statefalse.com` con HTTPS y proxy hacia el gateway Docker del host en `5000`

### Cómo desplegar

```bash
# Manual
./deploy.sh underlayer

# Automático: push a main con cambios en backend/** o tests-backend/**
# → GitHub Actions (deploy-backend.yml) → publish → rsync → systemctl restart → health check
```

### Configuración necesaria en GitHub

1. **GitHub OAuth App** en `github.com/settings/developers`:
   - Homepage URL: `https://statefalse.com`
   - Callback URL: `https://api.statefalse.com/api/auth/callback`
   - Scopes: `read:user`, `repo`
   - Client ID + Secret → env vars de systemd

2. **Webhook** en cada repo (o a nivel org):
   - URL: `https://api.statefalse.com/api/webhook/github`
   - Eventos: Workflow runs, Check suites, Pull requests, Pull request reviews, Issue comments, Pull request review comments
   - Secret: `WebhookSecret` en systemd

3. **PAT compartido** (opcional): `GitHub__PatToken`

---

## PR status (ciStatus) calculation

`ciStatus` se muestra en cada tarjeta de PR: WAITING, REVIEW, FAIL, READY, DRAFT, MERGED.

### Matching runs to PRs

Workflow runs se matching con PRs por `(repo, headSha)`. El `headSha` es el commit SHA del head del PR.

### SyncCheckRunsForCommit

En cada `GET /api/v1/pullrequests/active`, el backend fetchea check-runs de GitHub para cada head SHA único y hace upsert en DB (`PullRequestSyncService`).

### Lógica (`CiStatusCalculator` en Domain)

1. No workflow runs para ese headSha → `waiting`
2. Any run `in_progress` → `waiting`
3. Any run `failure` → `failed`
4. All runs `success` → `review` (necesita aprobación humana)
5. `review` + `reviewApproved` → `ready`
6. `draft = true` → badge **DRAFT** (gray), overrides CI status
7. PR merged → `merged`

### Self-healing

En `GetActiveAsync`, cada PR abierto se compara contra la API de GitHub:
- Si GitHub dice `closed`/`merged` y DB dice `open` → corrige status (webhook perdido)
- Si `merged_at` difiere de `OccurredAt` → corrige timestamp (ventana 24h "recently merged" precisa)
- Sync de `ReviewApproved` desde `/pulls/{n}/reviews` (per-reviewer latest state)

---

## Seguridad y Profesionalización

### Estado actual

| Problema | Riesgo | Estado |
|----------|--------|--------|
| Webhook sin HMAC | - | ✅ HMAC-SHA256 con `FixedTimeEquals`, se salta si secret no configurado |
| Secrets en appsettings | Filtración | ✅ Secrets via env vars de systemd (Jwt, GitHubOAuth, WebhookSecret) |
| Rate limiting | Abuso | ✅ `api` (100/min) + `webhook` (50/min) |
| CORS abierto | - | ⚠️ `AllowAnyOrigin` en `api`, política `SignalR` con credentials — mitigado por JWT |
| Sin HTTPS Kestrel↔ngrok | Tráfico localhost | ✅ Solo localhost (ngrok termina TLS) |
| Migraciones | - | ✅ EF Migrate en startup + baseline para DBs legacy |

### CI/CD

- **Backend Tests** (`.github/workflows/backend-tests.yml`): push/PR a main con cambios en `backend/**`
- **Swift Tests** (`.github/workflows/swift-tests.yml`): push/PR con cambios en `native/**` — build + 65 tests en macos-26
- **Deploy Backend** (`.github/workflows/deploy-backend.yml`): push a main → test → publish linux-x64 → rsync → restart → health check

### Costes actuales

| Concepto | Coste |
|----------|-------|
| VPS Hetzner | ~4-6 €/mes |
| ngrok (gratuito) | 0 € (URL estática) |
| Apple Developer | 0 € (sin App Store) |
| **Total** | **~5 €/mes** |
