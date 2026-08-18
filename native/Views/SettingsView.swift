import SwiftUI

struct SettingsView: View {
    let api: ApiClientProtocol

    @AppStorage("workspacePath") private var workspacePath = TeamDefaults.workspacePath
    @AppStorage("jiraBoardUrl") private var jiraBoardUrl = TeamDefaults.jiraBoardUrl
    @AppStorage("jiraBoardViewUrl") private var jiraBoardViewUrl = TeamDefaults.jiraBoardViewUrl
    @AppStorage("favoriteRepo") private var favoriteRepo = TeamDefaults.favoriteRepo
    @AppStorage("defaultIDE") private var defaultIDE = "rider"
    @AppStorage("customIDECommand") private var customIDECommand = ""
    @AppStorage("backendUrl") private var settingsBackendUrl = TeamDefaults.backendUrl
    @AppStorage("showMergedPRs") private var showMergedPRs = true
    @State private var pathDraft = ""
    @State private var pathError: String?
    @State private var jiraDraft = ""
    @State private var jiraError: String?
    @State private var jiraViewDraft = ""
    @State private var jiraViewError: String?
    @State private var backendUrlDraft = ""
    @State private var backendUrlError: String?
    @State private var patDraft = ""
    @State private var ideDraft = ""
    @State private var patSaved = false
    @State private var patSaving = false
    @State private var patError: String?
    @State private var discoveredRepos: [String] = []
    @State private var scanning = true

    // Connection test states
    @State private var backendTestResult: ConnectionTestResult?
    @State private var backendTesting = false
    @State private var jiraTestResult: ConnectionTestResult?

    var body: some View {
        ZStack {
            VisualEffectBackground(material: .sidebar)

            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    HStack(spacing: DS.Spacing.lg) {
                        Text("Settings")
                            .font(DS.Font.largeTitle)
                    }
                    .padding(.bottom, DS.Spacing.xl)

                    CollapsibleSection(title: "About", icon: "info.circle") {
                        aboutSection
                    }

                    CollapsibleSection(title: "Workspace", icon: "folder") {
                        workspaceSection
                    }

                    CollapsibleSection(title: "Jira", icon: "link") {
                        jiraSection
                    }

                    CollapsibleSection(title: "IDE", icon: "chevron.left.forwardslash.chevron.right") {
                        IDEListView(defaultIDE: $defaultIDE, customIDECommand: $customIDECommand)
                    }

                    CollapsibleSection(title: "Favorite Repo", icon: "star") {
                        favoriteRepoSection
                    }

                    CollapsibleSection(title: "Backend URL", icon: "server.rack") {
                        backendSection
                    }

                    CollapsibleSection(title: "Personal Access Token", icon: "key.fill") {
                        patSection
                    }

                    CollapsibleSection(title: "Pull Requests", icon: "arrow.triangle.branch") {
                        pullRequestsSection
                    }
                }
                .padding(24)
            }
        }
        .frame(width: 540, height: 620)
        .onAppear { Task { await scanForRepos() } }
        .closeOnEscape { SettingsPanelManager.shared.close() }
        .closeOnCmdW { SettingsPanelManager.shared.close() }
    }

    // MARK: - Sections

    @ViewBuilder
    private var workspaceSection: some View {
        Text("Local git repos are discovered recursively under this directory. Changes apply immediately without restart.")
            .font(DS.Font.small)
            .foregroundStyle(DS.Color.textSecondary)
        HStack(spacing: DS.Spacing.md) {
            styledTextField(
                "e.g. ~/Desktop/dev",
                text: $pathDraft,
                help: "Absolute path to the parent directory containing your git repositories. Subdirectories are scanned recursively.",
                error: $pathError
            )
            .onAppear { pathDraft = workspacePath }
            solidButton("Save", color: .green) {
                let expanded = (pathDraft as NSString).expandingTildeInPath
                if FileManager.default.fileExists(atPath: expanded) {
                    workspacePath = pathDraft
                    pathError = nil
                    SettingsPanelManager.shared.close()
                } else {
                    pathError = "Directory not found at \(expanded)"
                }
            }
        }
    }

    @ViewBuilder
    private var pullRequestsSection: some View {
        Text("Control which pull requests appear in the Active PRs list.")
            .font(DS.Font.small)
            .foregroundStyle(DS.Color.textSecondary)

        Toggle(isOn: $showMergedPRs) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Show merged PRs")
                    .font(DS.Font.body)
                    .foregroundStyle(DS.Color.textPrimary)
                Text("When on, recently merged PRs stay visible for 24h. Turn off to only see open PRs.")
                    .font(DS.Font.small)
                    .foregroundStyle(DS.Color.textSecondary)
            }
        }
        .toggleStyle(.switch)
        .tint(DS.Color.success)
    }

    @ViewBuilder
    private var aboutSection: some View {
        VStack(alignment: .leading, spacing: DS.Spacing.md) {
            HStack(alignment: .top, spacing: DS.Spacing.md) {
                Image(systemName: "flame.fill")
                    .font(.system(size: 24))
                    .foregroundStyle(DS.Color.accent)
                VStack(alignment: .leading, spacing: DS.Spacing.xs) {
                    Text("Statefalse")
                        .font(DS.Font.body.medium())
                        .foregroundStyle(DS.Color.textPrimary)
                    Text(ReleaseInfo.displayVersion)
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textSecondary)
                    Text("GitHub workflows and pull requests from your menu bar.")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textSecondary)
                }
            }

            actionButton("View What's New", color: .blue, help: "Read the release notes for this version") {
                SettingsPanelManager.shared.showReleaseNotes()
            }
        }
    }

    @ViewBuilder
    private var jiraSection: some View {
        Text("Used to build links to tickets extracted from branch names (e.g. LOY-1234 → https://.../browse/LOY-1234). Paste the full URL including /browse/.")
        Text("Used to build links to tickets extracted from branch names (e.g. LOY-1234 → https://.../browse/LOY-1234). Paste the full URL including /browse/.")
            .font(DS.Font.small)
            .foregroundStyle(DS.Color.textSecondary)

        VStack(alignment: .leading, spacing: DS.Spacing.sm) {
            sectionHeader("Ticket Base URL")
            HStack(spacing: DS.Spacing.md) {
                urlTextField(
                    "https://your-domain.atlassian.net/browse/",
                    text: $jiraDraft,
                    required: false,
                    help: "Base URL for opening individual Jira tickets from branch names (e.g. LOY-123 → https://domain.atlassian.net/browse/LOY-123).",
                    error: $jiraError
                )
                .onAppear { jiraDraft = jiraBoardUrl }
                solidButton("Save", color: .green) {
                    saveJiraUrl()
                }
            }

            sectionHeader("Board URL")
            HStack(spacing: DS.Spacing.md) {
                urlTextField(
                    "https://your-domain.atlassian.net/jira/...",
                    text: $jiraViewDraft,
                    required: false,
                    help: "Full URL to your Jira board for quick access from the toolbar menu.",
                    error: $jiraViewError
                )
                .onAppear { jiraViewDraft = jiraBoardViewUrl }
                solidButton("Save", color: .green) {
                    saveJiraViewUrl()
                }
            }

            HStack(spacing: DS.Spacing.sm) {
                actionButton("Test Connection", color: .blue, help: "Open Jira board in browser to verify URL") {
                    let url = jiraBoardViewUrl.isEmpty ? jiraBoardUrl : jiraBoardViewUrl
                    if let u = URL(string: url) {
                        NSWorkspace.shared.open(u)
                        jiraTestResult = .success("Opened in browser")
                    } else {
                        jiraTestResult = .failure("Invalid URL")
                    }
                }
                if let result = jiraTestResult {
                    connectionTestBadge(result)
                }
            }
        }
    }

    private func saveJiraUrl() {
        if jiraDraft.isEmpty {
            jiraBoardUrl = jiraDraft
            jiraError = nil
        } else if URL(string: jiraDraft) != nil {
            jiraBoardUrl = jiraDraft
            jiraError = nil
        } else {
            jiraError = "Invalid URL format"
        }
    }

    private func saveJiraViewUrl() {
        if jiraViewDraft.isEmpty {
            jiraBoardViewUrl = jiraViewDraft
            jiraViewError = nil
        } else if URL(string: jiraViewDraft) != nil {
            jiraBoardViewUrl = jiraViewDraft
            jiraViewError = nil
        } else {
            jiraViewError = "Invalid URL format"
        }
    }

    @ViewBuilder
    private var favoriteRepoSection: some View {
        HStack(spacing: DS.Spacing.md) {
            if scanning {
                ProgressView()
                    .scaleEffect(0.6)
                    .frame(width: 100)
            } else if discoveredRepos.isEmpty {
                Text("No repos found — check workspace path")
                    .font(DS.Font.body)
                    .foregroundStyle(DS.Color.textSecondary)
            } else {
                Picker(selection: $favoriteRepo) {
                    ForEach(discoveredRepos, id: \.self) { repo in
                        HStack(spacing: DS.Spacing.sm) {
                            if repo == favoriteRepo {
                                Image(systemName: "star.fill")
                                    .foregroundStyle(.yellow)
                            }
                            Text(repo)
                        }
                        .tag(repo)
                    }
                } label: {
                    HStack(spacing: DS.Spacing.sm) {
                        Image(systemName: "star.fill")
                            .foregroundStyle(.yellow)
                            .font(DS.Font.caption)
                        Text(favoriteRepo)
                    }
                }
                .pickerStyle(.menu)
                .frame(maxWidth: .infinity, alignment: .leading)
                .nativeCursor(.pointingHand)
            }
            actionButton("Refresh", color: .green) {
                Task { await scanForRepos() }
            }
        }
    }

    @ViewBuilder
    private var backendSection: some View {
        Text("The backend server URL. Only change if self-hosting the statefalse server. Must point to a running instance with /health endpoint.")
            .font(DS.Font.small)
            .foregroundStyle(DS.Color.textSecondary)

        HStack(spacing: DS.Spacing.md) {
            urlTextField(
                "https://your-server.com",
                text: $backendUrlDraft,
                help: "Full URL of the statefalse backend server, including protocol (https://). The server must expose a /health endpoint.",
                error: $backendUrlError
            )
            .onAppear { backendUrlDraft = settingsBackendUrl }
            solidButton("Save", color: .green) {
                saveBackendUrl()
            }
        }

        HStack(spacing: DS.Spacing.sm) {
            actionButton("Test Connection", color: .blue, help: "Test connectivity to the backend server by calling its /health endpoint") {
                Task { await testBackendConnection() }
            }
            .disabled(backendTesting)
            if backendTesting {
                ProgressView()
                    .scaleEffect(0.5)
                    .frame(width: 16, height: 16)
            }
            if let result = backendTestResult {
                connectionTestBadge(result)
            }
        }
    }

    private func saveBackendUrl() {
        if backendUrlDraft.isEmpty {
            backendUrlError = "URL is required"
        } else if URL(string: backendUrlDraft) != nil {
            settingsBackendUrl = backendUrlDraft
            backendUrlError = nil
        } else {
            backendUrlError = "Invalid URL format"
        }
    }

    @ViewBuilder
    private var patSection: some View {
        Text("Optional. Used to access org repos when OAuth is blocked. Create at github.com/settings/tokens with repo scope.")
            .font(DS.Font.small)
            .foregroundStyle(DS.Color.textSecondary)
            .padding(.top, -8)
        HStack(spacing: DS.Spacing.md) {
            styledTextField(
                "github_pat_...",
                text: $patDraft,
                help: "GitHub Personal Access Token (classic) with repo scope. Required when OAuth authentication does not grant access to organisation repositories."
            )
            .onAppear { patDraft = "" }
            solidButton(patSaving ? "Saving…" : "Save", color: .green, disabled: patSaving || patDraft.isEmpty) {
                Task { await savePat() }
            }
        }
        if patSaved {
            Text("PAT saved successfully")
                .font(DS.Font.small)
                .foregroundStyle(DS.Color.success)
        }
        if let patError {
            Text(patError)
                .font(DS.Font.small)
                .foregroundStyle(DS.Color.destructive)
        }
    }

    // MARK: - Helpers

    private func scanForRepos() async {
        scanning = true
        let expanded = (workspacePath as NSString).expandingTildeInPath
        let paths = GitService.discoverRepos(workspacePath: expanded)
        await MainActor.run {
            discoveredRepos = paths.compactMap { GitService.repoName(from: $0) }.sorted()
            if !discoveredRepos.contains(favoriteRepo), let first = discoveredRepos.first {
                favoriteRepo = first
            }
            scanning = false
        }
    }

    private func savePat() async {
        patSaving = true
        patSaved = false
        patError = nil
        defer { patSaving = false }

        let url = backendUrlDraft.isEmpty ? settingsBackendUrl : backendUrlDraft
        guard api.authToken != nil else {
            patError = "Not signed in"
            return
        }

        if await api.savePAT(patToken: patDraft, to: url) {
            patSaved = true
            patDraft = ""
        } else {
            patError = "Saved locally, but backend rejected it"
        }
    }

    private func testBackendConnection() async {
        backendTesting = true
        backendTestResult = nil
        defer { backendTesting = false }

        let url = backendUrlDraft.isEmpty ? settingsBackendUrl : backendUrlDraft
        guard let u = URL(string: "\(url)/health") else {
            backendTestResult = .failure("Invalid URL")
            return
        }
        do {
            let (data, resp) = try await URLSession.shared.data(from: u)
            if let http = resp as? HTTPURLResponse, http.statusCode == 200 {
                if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   let status = json["status"] as? String {
                    backendTestResult = .success("Healthy (\(status))")
                } else {
                    backendTestResult = .success("Connected")
                }
            } else {
                backendTestResult = .failure("HTTP \((resp as? HTTPURLResponse)?.statusCode ?? 0)")
            }
        } catch {
            backendTestResult = .failure(error.localizedDescription)
        }
    }

    @ViewBuilder
    private func connectionTestBadge(_ result: ConnectionTestResult) -> some View {
        HStack(spacing: DS.Spacing.xs) {
            Circle()
                .fill(result.isSuccess ? DS.Color.success : DS.Color.destructive)
                .frame(width: 6, height: 6)
            Text(result.message)
                .font(DS.Font.tiny)
                .foregroundStyle(result.isSuccess ? DS.Color.success : DS.Color.destructive)
        }
        .padding(.horizontal, DS.Spacing.sm)
        .padding(.vertical, DS.Spacing.xs)
        .background(
            (result.isSuccess ? DS.Color.success : DS.Color.destructive).opacity(0.1),
            in: RoundedRectangle(cornerRadius: DS.Radius.sm)
        )
    }
}
// MARK: - Release information

enum ReleaseInfo {
    static let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    static let build = Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "0"
    static let displayVersion = "Version \(version) (Build \(build))"
}

struct ReleaseNoteSection: Identifiable {
    let id = UUID()
    let title: String
    let icon: String
    let items: [String]
}

struct ReleaseNotesEntry {
    let version: String
    let title: String
    let summary: String
    let sections: [ReleaseNoteSection]
}

enum ReleaseNotesStore {
    private static let lastSeenVersionKey = "releaseNotes.lastSeenVersion"

    static var current: ReleaseNotesEntry {
        ReleaseNotesEntry(
            version: ReleaseInfo.version,
            title: "What's new in Statefalse \(ReleaseInfo.version)",
            summary: "The latest improvements to your GitHub workflow companion.",
            sections: [
                ReleaseNoteSection(
                    title: "Reliability",
                    icon: "checkmark.shield",
                    items: [
                        "Improved GitHub login through the production proxy.",
                        "Retry now performs a clean realtime connection reset."
                    ]
                ),
                ReleaseNoteSection(
                    title: "Release experience",
                    icon: "sparkles",
                    items: [
                        "See the installed app version and build in Settings.",
                        "Open these release notes again whenever you need them."
                    ]
                )
            ]
        )
    }

    static var shouldPresentCurrentVersion: Bool {
        UserDefaults.standard.string(forKey: lastSeenVersionKey) != current.version
    }

    static func markCurrentVersionAsSeen() {
        UserDefaults.standard.set(current.version, forKey: lastSeenVersionKey)
    }
}

struct ReleaseNotesView: View {
    private let release = ReleaseNotesStore.current

    var body: some View {
        ZStack {
            VisualEffectBackground(material: .sidebar)

            ScrollView {
                VStack(alignment: .leading, spacing: DS.Spacing.xl) {
                    VStack(alignment: .leading, spacing: DS.Spacing.sm) {
                        HStack(spacing: DS.Spacing.md) {
                            Image(systemName: "sparkles")
                                .foregroundStyle(DS.Color.accent)
                            Text(release.title)
                                .font(DS.Font.largeTitle)
                                .foregroundStyle(DS.Color.textPrimary)
                        }
                        Text(release.summary)
                            .font(DS.Font.body)
                            .foregroundStyle(DS.Color.textSecondary)
                    }

                    ForEach(release.sections) { section in
                        VStack(alignment: .leading, spacing: DS.Spacing.md) {
                            Label(section.title, systemImage: section.icon)
                                .font(DS.Font.section)
                                .foregroundStyle(DS.Color.textPrimary)

                            VStack(alignment: .leading, spacing: DS.Spacing.sm) {
                                ForEach(section.items, id: \.self) { item in
                                    HStack(alignment: .top, spacing: DS.Spacing.sm) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundStyle(DS.Color.success)
                                        Text(item)
                                            .font(DS.Font.body)
                                            .foregroundStyle(DS.Color.textSecondary)
                                    }
                                }
                            }
                        }
                        .padding(DS.Spacing.lg)
                        .background(DS.Color.cardBackground, in: RoundedRectangle(cornerRadius: DS.Radius.lg))
                    }
                }
                .padding(DS.Spacing.xxl)
            }
        }
        .frame(width: 560, height: 620)
        .closeOnEscape { SettingsPanelManager.shared.closeReleaseNotes() }
        .closeOnCmdW { SettingsPanelManager.shared.closeReleaseNotes() }
    }
}
