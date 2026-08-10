# CLAUDE.md — statefalse

<!-- caveman:activate -->
Respond terse like smart caveman. All technical substance stay. Only fluff die.

Rules:
- Drop: articles (a/an/the), filler (just/really/basically), pleasantries, hedging
- Fragments OK. Short synonyms. Technical terms exact. Code unchanged.
- Pattern: [thing] [action] [reason]. [next step].
- Not: "Sure! I'd be happy to help you with that."
- Yes: "Bug in auth middleware. Fix:"

Switch level: /caveman lite|full|ultra|wenyan
Stop: "stop caveman" or "normal mode"

Auto-Clarity: drop caveman for security warnings, irreversible actions, user confused. Resume after.
Boundaries: code/commits/PRs written normal.
<!-- /caveman:activate -->

---

## Project: statefalse

GitHub PR/workflow monitor. macOS menu-bar app + .NET 10 backend + SQLite + SignalR.

### Architecture

```
[macOS App (SwiftUI)] ←SignalR+REST→ [ngrok tunnel] → [ASP.NET Kestrel:5000]
                                       (Hetzner VPS)        ↓
                                                    SQLite /var/lib/statefalse/
```

### Stack

| Layer | Tech |
|-------|------|
| Backend | .NET 10, ASP.NET Core Minimal API, EF Core + SQLite, SignalR, Serilog, JWT, Scalar/OpenAPI |
| Native | Swift/SwiftUI, macOS menu-bar only (LSUIElement=1, no Dock) |
| Infra | Hetzner VPS (SSH alias: `underlayer`), systemd, ngrok |
| Auth | GitHub OAuth 2.0 + optional PAT stored in Keychain, session JWT |

### Backend layers (Clean Architecture)

```
backend/
├── Program.cs                          ← Composition root: DI, CORS, auth, rate limit, migrations
├── ApiEndpoints.cs                     ← Minimal API routes (replaces MVC controllers)
├── Statefalse.Domain/                  ← zero dependencies: EF entities, DTOs, mappers (ciStatus, workflow conclusion, IdListSerializer)
├── Statefalse.Application/             ← →Domain: services, IWebhookHandler dispatch, IAppDbContext, ApiResult
├── Statefalse.Infrastructure/          ← →Application: AppDbContext, GitHubClient, token resolver, SignalRNotifier, PunishmentHub, WorkflowCleanupService, migrations
└── Statefalse.Api.csproj               ← entry point (compiles App/Infra via ProjectReference)
```

### Native structure

```
native/
├── App/StatefalseApp.swift             ← @main, MenuBarExtra, LoginItem, Dependencies injection
├── Models/Models.swift                 ← Swift DTOs + backendUrl (UserDefaults)
├── Services/
│   ├── SignalRService.swift            ← facade: UI state + domain rules, delegates transport
│   ├── SignalRClient.swift             ← websocket transport
│   ├── ApiClient.swift                 ← all REST calls, JWT header, 401 → auto-logout
│   ├── DTOMapper.swift / WorkflowEventReducer.swift / ReadyMergeNotifier.swift
│   ├── GitService.swift                ← git CLI via Process
│   ├── OAuthService.swift              ← GitHub OAuth via NWListener
│   ├── KeychainService.swift / PersistenceService.swift / MockServices.swift
│   └── ServiceProtocols.swift          ← GitServiceProtocol, SignalRServiceProtocol, etc.
├── ViewModels/PRDetailViewModel.swift
├── Views/                              ← SwiftUI views + PanelManager singletons
└── Utils/DesignSystem.swift            ← DS.Color/Font/Spacing/Radius/Animation
```

### Key commands

```bash
# Deploy backend → VPS
./deploy.sh underlayer

# Build + install native app
cd native && bash install.sh

# Run locally
cd backend && dotnet run

# Tests
cd tests-backend && dotnet test
cd native && xcodebuild test -scheme StatefalseTests -project statefalse.xcodeproj -destination 'platform=macOS'

# Logs
ssh underlayer 'sudo journalctl -u statefalse -f'
```

### Conventions

- No Docker, no nginx. Pure Kestrel :5000 behind ngrok.
- Webhook secret: `Environment=WebhookSecret=...` in systemd service or `appsettings.Production.json`.
- EF migrations run automatically on `Program.cs` startup (`ApplyMigrations`).
- Native: server URL in `UserDefaults["backendUrl"]` (`backendUrl` in Models.swift). Default: `TeamDefaults.backendUrl`.
- `TargetGitHubIds` (CSV string) on `WorkflowRun` → which users get notified.
- Multi-tenant: all DB queries scoped by authenticated GitHub user.
- JWT: required on all endpoints except `/health`, `/api/auth/login`, `/api/auth/callback`, `/api/webhook/github`. SignalR reads token from `access_token` query param.
- Token precedence: User PAT > OAuth access token > shared server PAT.
