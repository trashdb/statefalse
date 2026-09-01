import Combine
import Foundation
import SwiftUI

struct ContentView: View {
    private enum InternalScreen: Equatable {
        case home
        case settings
        case notificationHistory
        case workflowHistory
        case webhookLog
        case releaseNotes

        var title: String {
            switch self {
            case .home: return "Statefalse"
            case .settings: return "Settings"
            case .notificationHistory: return "Notification History"
            case .workflowHistory: return "Workflow History"
            case .webhookLog: return "Webhook Event Log"
            case .releaseNotes: return "What's New"
            }
        }
    }

    @ObservedObject var signalR: SignalRService
    @Environment(\.dependencies) private var deps

    @AppStorage("keepSignedIn") private var keepSignedIn = true
    @State private var isLoading = false
    @State private var loginTask: Task<Void, Never>?
    @State private var loginAttemptID: UUID?
    @State private var loginError: String?
    @State private var showQuickSearch = false
    @State private var internalScreen: InternalScreen = .home
    @FocusState private var quickSearchFocused: Bool
    @State private var resignFocusToken: Any?

    var body: some View {
        ZStack {
            VStack(spacing: 0) {
            VStack(alignment: .leading, spacing: DS.Spacing.xl) {
                    // Header
                    HStack(spacing: DS.Spacing.md) {
                        WaveMark(color: .red)
                            .frame(width: 22, height: 22)
                        Text("Statefalse")
                            .font(DS.Font.largeTitle)
                    }

                    Text("CI/CD notifications when a merged PR breaks the build.")
                        .font(DS.Font.body)
                        .foregroundStyle(DS.Color.textSecondary)
                        .lineLimit(2)

                    Divider()

                    if signalR.isLoggedIn && signalR.connectionState != .connected {
                        HStack(spacing: DS.Spacing.md) {
                            Image(systemName: "wifi.exclamationmark")
                            VStack(alignment: .leading, spacing: DS.Spacing.xs) {
                                Text(connectionStatusTitle)
                                    .font(DS.Font.small.medium())
                                Text("Realtime notifications will resume automatically.")
                                    .font(DS.Font.caption)
                                    .foregroundStyle(DS.Color.textSecondary)
                            }
                            Spacer()
                            Button("Retry now") {
                                signalR.retryConnection()
                            }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                        }
                        .foregroundStyle(.orange)
                        .padding(DS.Spacing.md)
                        .background(.orange.opacity(0.12), in: RoundedRectangle(cornerRadius: DS.Radius.lg))
                    }

                    if signalR.isLoggedIn {
                        LoggedInCardView(username: signalR.username, avatarUrl: signalR.avatarUrl, onSignOut: logout)
                        KeepSignedInToggleView(isOn: $keepSignedIn)
                    } else {
                        SignInCardView(isLoading: isLoading, loginError: loginError, onSignIn: login, onCancel: cancelLogin)
                    }

                    if signalR.isLoggedIn {
                        ActivePRsView(prs: signalR.activePRs, gitHubId: signalR.userGitHubId, deps: deps)
                        Divider()
                    }

                    if signalR.isLoggedIn {
                        if let notification = signalR.notifications.first {
                            VStack(alignment: .leading, spacing: DS.Spacing.md) {
                                HStack(spacing: DS.Spacing.xs) {
                                    WaveMark(color: DS.Color.destructive, lineWidth: 2)
                                        .frame(width: 16, height: 16)
                                    Text("Last Notification")
                                        .font(DS.Font.small.semibold())
                                        .foregroundStyle(DS.Color.destructive)
                                    Spacer()
                                    Button {
                                        internalScreen = .notificationHistory
                                    } label: {
                                        HStack(spacing: DS.Spacing.xs) {
                                            Text("Show history")
                                            if signalR.unreadNotificationCount > 0 {
                                                UnreadNotificationBadge(count: signalR.unreadNotificationCount)
                                            }
                                        }
                                    }
                                    .buttonStyle(.borderless)
                                    .font(DS.Font.caption)
                                }
                                Text(notification.title)
                                    .font(DS.Font.body.semibold())
                                    .foregroundStyle(DS.Color.textPrimary)
                                Text(notification.body)
                                    .font(DS.Font.small)
                                    .foregroundStyle(DS.Color.textSecondary)
                                if let repo = notification.repo {
                                    Text(repo)
                                        .font(DS.Font.small)
                                        .foregroundStyle(DS.Color.textTertiary)
                                        .lineLimit(1)
                                }
                                if let url = notification.prUrl {
                                    Button {
                                        open(notification, at: url)
                                    } label: {
                                        HStack(spacing: DS.Spacing.xs) {
                                            Image(systemName: "arrow.up.right")
                                                .font(DS.Font.caption)
                                            Text("Open in GitHub")
                                                .font(DS.Font.small.medium())
                                        }
                                    }
                                        .foregroundStyle(DS.Color.accent)
                                        .cursor(.pointingHand)
                                        .buttonStyle(.plain)
                                }
                            }
                            .padding(.horizontal, DS.Spacing.xl + 1)
                            .padding(.vertical, DS.Spacing.lg + 1)
                            .background(DS.Color.cardBackground, in: RoundedRectangle(cornerRadius: DS.Radius.lg + 1))
                            .overlay(
                                RoundedRectangle(cornerRadius: DS.Radius.lg + 1)
                                    .stroke(DS.Color.divider, lineWidth: 1)
                            )
                        } else {
                            EmptyNotificationView {
                                internalScreen = .notificationHistory
                            }
                        }
                    }

                    if signalR.isLoggedIn {
                        Divider()
                        LocalBranchesView(gitHubId: signalR.userGitHubId)
                    }
            }
            .foregroundStyle(DS.Color.textSecondary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.vertical, DS.Spacing.xl)
            .padding(.horizontal, DS.Spacing.xxl)

            Divider()

            // Toolbar
            HStack {
                if signalR.isLoggedIn {
                    toolbarButton(icon: "arrow.triangle.2.circlepath", help: "Full resync: workflows + PRs") {
                        Task {
                            _ = await signalR.syncPRsFromGitHub()
                            let n = await signalR.syncActiveWorkflows()
                            if n > 0 {
                                showNotification(
                                    title: "Workflows Synced",
                                    body: "\(n) new running workflow\(n == 1 ? "" : "s") found via GitHub API",
                                    subtitle: nil,
                                    actionURL: nil
                                )
                            }
                        }
                    }
                }

                toolbarButton(icon: "bell.fill", help: "Send Test Notification") {
                    showNotification(
                        title: "statefalse",
                        body: "Test notification from popover",
                        subtitle: "Works!",
                        actionURL: URL(string: "https://github.com")
                    )
                }

                if signalR.isLoggedIn {
                    toolbarButton(icon: "list.bullet.rectangle", help: "Workflow History") {
                        internalScreen = .workflowHistory
                    }

                    toolbarButton(icon: "antenna.radiowaves.left.and.right", help: "Webhook Event Log (debug)") {
                        internalScreen = .webhookLog
                    }

                    toolbarButton(icon: "tray.full", help: "See All PRs") {
                        let favoriteRepo = UserDefaults.standard.string(forKey: "favoriteRepo") ?? TeamDefaults.favoriteRepo
                        let branches = MenuBarBadgeService.shared.currentBranches
                        let favoritePath = branches.first(where: { $0.repoName == favoriteRepo })?.repoPath
                        Task {
                            let fullName: String?
                            if let favoritePath {
                                fullName = await deps.gitService.repoFullName(repoPath: favoritePath)
                            } else {
                                fullName = signalR.activePRs.first?.repo
                            }
                            let url = fullName.flatMap(GitService.pullRequestsURL(for:))
                                ?? URL(string: "https://github.com/pulls")!
                            NSWorkspace.shared.open(url)
                        }
                    }

                    toolbarButton(icon: "gearshape.fill", help: "Settings") {
                        internalScreen = .settings
                    }
                }

                if signalR.isLoggedIn, signalR.runningWorkflows.count > 0 {
                    Button {
                        internalScreen = .workflowHistory
                    } label: {
                        HStack(spacing: DS.Spacing.sm) {
                            Circle()
                                .fill(.orange)
                                .frame(width: 7, height: 7)
                            Text("\(signalR.runningWorkflows.count) \(signalR.runningWorkflows.count == 1 ? "workflow" : "workflows") running")
                                .font(DS.Font.small.medium())
                                .foregroundStyle(.orange)
                        }
                        .padding(.horizontal, DS.Spacing.lg)
                        .padding(.vertical, DS.Spacing.sm)
                        .background(.orange.opacity(0.12), in: RoundedRectangle(cornerRadius: DS.Radius.lg))
                        .overlay(
                            RoundedRectangle(cornerRadius: DS.Radius.lg)
                                .stroke(.orange.opacity(0.2), lineWidth: 1)
                        )
                    }
                    .buttonStyle(.plain)
                    .cursor(.pointingHand)
                }

                Spacer()

                toolbarButton(icon: "trash.fill", help: "Quit") {
                    NSApplication.shared.terminate(nil)
                }
            }
            .padding(.horizontal, DS.Spacing.xxl)
            .padding(.vertical, DS.Spacing.xl)
        }
            .opacity(internalScreen == .home ? 1 : 0)
            .allowsHitTesting(internalScreen == .home)
            .accessibilityHidden(internalScreen != .home)

            if internalScreen != .home {
                internalScreenView
            }
        }
        .frame(width: 460, height: 910, alignment: .top)
        .background(.regularMaterial)
        .onAppear {
            signalR.restoreSession()
            Task { await scanCurrentBranches() }
            setupQuickSearchShortcut()
            setupResignFocusMonitor()
            if ReleaseNotesStore.shouldPresentCurrentVersion {
                DispatchQueue.main.async {
                    internalScreen = .releaseNotes
                    ReleaseNotesStore.markCurrentVersionAsSeen()
                }
            }
        }
        .onChange(of: signalR.activePRs) { _, newValue in updateMenuBarBadge(newValue) }
        .onChange(of: signalR.runningWorkflows.count) { updateMenuBarBadge(signalR.activePRs) }
        .onChange(of: signalR.isConnected) { updateMenuBarBadge(signalR.activePRs) }
        .onChange(of: signalR.unreadNotificationCount) { updateMenuBarBadge(signalR.activePRs) }
        .overlay(QuickSearchView(
            isPresented: $showQuickSearch,
            actions: signalR.isLoggedIn ? quickSearchActions : [],
            signalR: signalR,
            gitHubId: signalR.userGitHubId
        ))
        .animation(DS.Animation.popover, value: showQuickSearch)
        .background(
            Button("") {
                internalScreen = .settings
            }
            .keyboardShortcut(",", modifiers: .command)
            .labelsHidden()
            .hidden()
        )
    }

    @ViewBuilder
    private var internalScreenView: some View {
        VStack(spacing: 0) {
            ZStack {
                Text(internalScreen.title)
                    .font(DS.Font.section)
                    .foregroundStyle(DS.Color.textPrimary)

                HStack {
                    Button {
                        withAnimation(DS.Animation.popover) { internalScreen = .home }
                    } label: {
                        Label("Back", systemImage: "chevron.left")
                    }
                    .buttonStyle(.borderless)
                    .font(DS.Font.small.medium())
                    .foregroundStyle(DS.Color.accent)
                    .cursor(.pointingHand)
                    Spacer()
                }
            }
            .padding(.horizontal, DS.Spacing.xl)
            .padding(.vertical, 12)

            Divider()

            Group {
                switch internalScreen {
                case .home:
                    EmptyView()
                case .settings:
                    SettingsView(
                        api: signalR.api,
                        onBack: { internalScreen = .home },
                        onShowReleaseNotes: { internalScreen = .releaseNotes }
                    )
                case .notificationHistory:
                    NotificationHistoryView(signalR: signalR, onBack: { internalScreen = .home })
                case .workflowHistory:
                    WorkflowHistoryView(
                        signalR: signalR,
                        gitHubId: signalR.userGitHubId,
                        onBack: { internalScreen = .home }
                    )
                case .webhookLog:
                    WebhookLogView(api: signalR.api, onBack: { internalScreen = .home })
                case .releaseNotes:
                    ReleaseNotesView(onBack: { internalScreen = .settings })
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .background(.regularMaterial)
    }

    private var quickSearchActions: [QuickSearchAction] {
        var actions: [QuickSearchAction] = []

        let repo = UserDefaults.standard.string(forKey: "favoriteRepo") ?? TeamDefaults.favoriteRepo

        actions.append(QuickSearchAction(
            id: "jira-board", title: "Open Jira Board",
            subtitle: "Browse all tickets",
            icon: "link", category: .jira
        ) {
            let url = UserDefaults.standard.string(forKey: "jiraBoardViewUrl") ?? TeamDefaults.jiraBoardViewUrl
            if let u = URL(string: url) { NSWorkspace.shared.open(u) }
        })

        let branches = MenuBarBadgeService.shared.currentBranches
        for branch in branches {
            actions.append(QuickSearchAction(
                id: "create-pr-\(branch.repoPath)-\(branch.name)",
                title: "Create PR from \(branch.name)",
                subtitle: "\(branch.repoName) → open PR preview",
                icon: "plus.circle", category: .branch
            ) {
                let info = BranchInfo(
                    name: branch.name, repoPath: branch.repoPath,
                    repoName: branch.repoName,
                    isCurrent: true, isLocal: true,
                    isMerged: false, isDefault: false
                )
                BranchDetailPanelManager.shared.show(
                    deps: deps,
                    info: info,
                    gitHubId: self.signalR.userGitHubId,
                    onCheckout: nil
                )
            })
            actions.append(QuickSearchAction(
                id: "open-ide-\(branch.repoPath)",
                title: "Open \(branch.repoName) in IDE",
                subtitle: branch.repoPath,
                icon: "chevron.left.forwardslash.chevron.right", category: .repo
            ) {
                IDEOpener.openRepo(repoPath: branch.repoPath)
            })
            if let ticket = branch.ticketNumber {
                actions.append(QuickSearchAction(
                    id: "jira-ticket-\(ticket)",
                    title: "Open Jira ticket \(ticket)",
                    subtitle: "\(branch.repoName) — current branch",
                    icon: "link", category: .jira
                ) {
                    let base = UserDefaults.standard.string(forKey: "jiraBoardUrl") ?? TeamDefaults.jiraBoardUrl
                    if let u = URL(string: "\(base)\(ticket)") {
                        NSWorkspace.shared.open(u)
                    }
                })
            }
        }
        if let fav = branches.first(where: { $0.repoName == repo }) {
            actions.append(QuickSearchAction(
                id: "checkout-main-\(fav.repoPath)",
                title: "Checkout main in \(fav.repoName)",
                subtitle: "Switch to main branch",
                icon: "arrow.triangle.branch", category: .branch
            ) {
                Task {
                    let git = deps.gitService
                    try? await git.checkoutBranch(repoPath: fav.repoPath, name: "main")
                    _ = await git.pullCurrentBranch(repoPath: fav.repoPath)
                    await self.scanCurrentBranches()
                }
            })
        }

        return actions
    }

    private func setupResignFocusMonitor() {
        if let t = resignFocusToken { NSEvent.removeMonitor(t) }
        resignFocusToken = NSEvent.addLocalMonitorForEvents(matching: .leftMouseDown) { event in
            guard let window = event.window,
                  let editor = window.firstResponder as? NSTextView else { return event }
            let click = event.locationInWindow
            let editorFrame = editor.convert(editor.bounds, to: nil)
            if !editorFrame.contains(click) {
                DispatchQueue.main.async { window.makeFirstResponder(nil) }
            }
            return event
        }
    }

    private func setupQuickSearchShortcut() {
        NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.modifierFlags.contains(.command) && event.charactersIgnoringModifiers == "k" {
                if let window = NSApp.keyWindow, window.level == .floating || window == NSApp.keyWindow {
                    self.showQuickSearch.toggle()
                    return nil
                }
            }
            return event
        }
    }

    private func scanCurrentBranches() async {
        let path = UserDefaults.standard.string(forKey: "workspacePath") ?? TeamDefaults.workspacePath
        let branches = await GitService.scanCurrentBranches(workspacePath: path)
        await MainActor.run {
            MenuBarBadgeService.shared.currentBranches = branches
        }
    }

    private func updateMenuBarBadge(_ prs: [PullRequest]) {
        let badge = MenuBarBadgeService.shared
        badge.activePRCount = prs.count
        badge.failedPRCount = prs.filter { $0.ciStatus == "failed" || $0.conclusion == "failure" }.count
        badge.draftCount = prs.filter { $0.draft }.count
        badge.waitingCount = prs.filter { $0.ciStatus == "waiting" }.count
        badge.reviewCount = prs.filter { $0.ciStatus == "review" }.count
        badge.readyCount = prs.filter { $0.ciStatus == "ready" || $0.ciStatus == "" }.count
        badge.mergedCount = prs.filter { $0.isMerged }.count
        badge.runningWorkflowCount = signalR.runningWorkflows.count
        badge.unreadNotificationCount = signalR.unreadNotificationCount

        if !signalR.isConnected {
            badge.connectionState = .disconnected
        } else if badge.failedPRCount > 0 {
            badge.connectionState = .hasFailures
        } else if badge.runningWorkflowCount > 0 {
            badge.connectionState = .hasRunning
        } else {
            badge.connectionState = .connected
        }
    }

    private var connectionStatusTitle: String {
        switch signalR.connectionState {
        case .connecting: return "Connecting to realtime updates…"
        case .reconnecting: return "Realtime connection lost"
        case .disconnected: return "Realtime updates disconnected"
        case .connected: return "Realtime updates connected"
        }
    }

    private func login() {
        let attemptID = UUID()
        loginAttemptID = attemptID
        isLoading = true
        loginError = nil
        loginTask = Task {
            do {
                try await signalR.login(keepSignedIn: keepSignedIn)
            } catch is CancellationError {
                // Cancellation is an intentional user action, not a login error.
            } catch {
                let message: String
                switch error {
                case OAuthService.OAuthError.cancelled:
                    message = "GitHub sign-in was cancelled."
                case OAuthService.OAuthError.timeout:
                    message = "GitHub sign-in timed out. Please try again."
                default:
                    message = "Login failed. Please try again."
                }
                await MainActor.run {
                    guard loginAttemptID == attemptID else { return }
                    loginError = message
                }
            }
            await MainActor.run {
                guard loginAttemptID == attemptID else { return }
                isLoading = false
                loginTask = nil
                loginAttemptID = nil
            }
        }
    }

    private func cancelLogin() {
        loginAttemptID = nil
        loginTask?.cancel()
        loginTask = nil
        isLoading = false
        loginError = nil
    }

    private func open(_ notification: ApiNotification, at url: URL) {
        Task {
            _ = await signalR.markNotificationRead(id: notification.id)
            NSWorkspace.shared.open(url)
        }
    }

    private func logout() {
        signalR.logout()
        loginError = nil
    }
}

private struct UnreadNotificationBadge: View {
    let count: Int

    var body: some View {
        Text(count > 99 ? "99+" : "\(count)")
            .font(DS.Font.caption.weight(.bold))
            .foregroundStyle(.white)
            .padding(.horizontal, 4)
            .padding(.vertical, 2)
            .background(DS.Color.warning, in: Capsule())
            .overlay(Capsule().stroke(.white.opacity(0.35), lineWidth: 0.5))
            .accessibilityLabel("\(count) unread notification\(count == 1 ? "" : "s")")
    }
}

#Preview {
    ContentView(signalR: SignalRService(baseUrl: backendUrl))
}
