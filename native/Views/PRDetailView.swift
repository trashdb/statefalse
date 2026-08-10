import SwiftUI

struct PRDetailView: View {
    let pr: PullRequest
    let gitHubId: Int64
    private let deps: Dependencies
    @StateObject private var model: PRDetailViewModel

    @AppStorage("workspacePath") private var workspacePath = TeamDefaults.workspacePath

    init(pr: PullRequest,
         gitHubId: Int64,
         optimisticDraft: Bool? = nil,
         deps: Dependencies,
         onDraftChanged: ((Bool) -> Void)? = nil) {
        self.pr = pr
        self.gitHubId = gitHubId
        self.deps = deps
        _model = StateObject(wrappedValue: PRDetailViewModel(
            pr: pr,
            optimisticDraft: optimisticDraft,
            api: deps.apiClient,
            signalR: deps.signalRService,
            onDraftChanged: onDraftChanged
        ))
    }

    var compareUrl: URL {
        URL(string: "https://github.com/\(pr.repo)/compare/\(pr.baseBranch)...\(pr.headBranch)")!
    }

    var checksUrl: URL {
        URL(string: "\(pr.prUrl)/checks")!
    }

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Spacing.md) {
            Picker("", selection: $model.selectedTab) {
                Text("Details").tag(0)
                Text("Commits").tag(1)
                Text("Files").tag(2)
                Text("Checks").tag(3)
            }
            .pickerStyle(.segmented)
            .frame(maxWidth: .infinity)
            .cursor(.pointingHand)
            .accessibilityLabel("PR detail tabs")
            .accessibilityHint("Double tap to switch between Details, Commits, Files, and Checks tabs")

            switch model.selectedTab {
            case 0: detailsTab
            case 1: commitsTab
            case 2: filesTab
            case 3: checksTab
            default: detailsTab
            }
        }
        .padding(DS.Spacing.xxl)
        .frame(width: 320, height: 480)
        .animation(DS.Animation.default, value: model.selectedTab)
        .onAppear { model.loadDetails() }
        .onChange(of: model.selectedTab) { _, newTab in
            switch newTab {
            case 1 where model.commits.isEmpty && !model.loadingCommits: model.loadCommits()
            case 2 where model.files.isEmpty && !model.loadingFiles: model.loadFiles()
            case 3 where model.checks.isEmpty && !model.loadingChecks: model.loadChecks()
            default: break
            }
        }
        .closeOnEscape { PRDetailPanelManager.shared.close() }
        .closeOnCmdW { PRDetailPanelManager.shared.close() }
    }

    // MARK: - Details Tab
    @ViewBuilder
    private var detailsTab: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: DS.Spacing.lg) {
                // Top links
                HStack(spacing: DS.Spacing.sm) {
                    Spacer()
                    linkButton("Open PR", url: pr.prUrl, help: "Open this pull request on GitHub")
                        .accessibilityLabel("Open PR on GitHub")
                        .accessibilityHint("Opens the pull request in your browser")
                    if !pr.isMerged {
                        linkButton("Compare", url: compareUrl, help: "Compare base and head branches on GitHub")
                            .accessibilityLabel("Compare branches on GitHub")
                        linkButton("Checks", url: checksUrl, help: "View CI checks for this pull request")
                            .accessibilityLabel("View CI checks on GitHub")
                    }
                }

                // Badges
                if !pr.isMerged {
                    PRDetailBadges(pr: pr)
                }

                // Subscriber management
                if !pr.isMerged && pr.repo != "trashdb/statefalse" {
                    let isAuthor = pr.authorGitHubId != nil && pr.authorGitHubId == gitHubId
                    if isAuthor {
                        SubscriberManagementView(
                            pr: pr,
                            gitHubId: gitHubId,
                            deps: deps
                        )
                    } else {
                        // Self-subscribe for non-authors
                        HStack(spacing: DS.Spacing.sm) {
                            if pr.isSubscribed {
                                Image(systemName: "bell.fill")
                                    .font(DS.Font.caption)
                                    .foregroundStyle(DS.Color.accent)
                                Text("Subscribed")
                                    .font(DS.Font.small)
                                    .foregroundStyle(DS.Color.accent)
                                Spacer()
                                solidButton("Unsubscribe", color: .secondary, help: "Stop receiving notifications for this PR") {
                                    Task { await model.performUnsubscribe() }
                                }
                            } else {
                                Image(systemName: "bell")
                                    .font(DS.Font.caption)
                                    .foregroundStyle(DS.Color.textTertiary)
                                Text("Not subscribed")
                                    .font(DS.Font.small)
                                    .foregroundStyle(DS.Color.textTertiary)
                                Spacer()
                                solidButton("Subscribe", color: DS.Color.accent, help: "Get notified of comments, reviews, and status changes") {
                                    Task { await model.performSubscribe() }
                                }
                            }
                        }
                        .padding(.vertical, DS.Spacing.xs)
                        .animation(DS.Animation.default, value: pr.isSubscribed)
                    }
                }

                // Title
                Text(pr.title)
                    .font(DS.Font.title)
                    .foregroundStyle(DS.Color.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)

                // Repo → branch
                HStack(spacing: DS.Spacing.sm) {
                    Text(shortRepo(pr.repo))
                        .font(DS.Font.mono(11))
                        .foregroundStyle(DS.Color.textSecondary)
                    Text("→")
                        .font(DS.Font.body)
                        .foregroundStyle(DS.Color.textTertiary)
                    Text(pr.baseBranch)
                        .font(DS.Font.mono(11))
                        .foregroundStyle(DS.Color.accent)
                }

                // Head + PR number
                HStack(spacing: DS.Spacing.sm) {
                    Text("head:")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textTertiary)
                    Text(pr.headBranch)
                        .font(DS.Font.mono(10))
                        .foregroundStyle(DS.Color.textSecondary)
                    Spacer()
                    Text("PR #\(pr.prNumber)")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textSecondary)
                }

                if !pr.isMerged {
                    PRDetailDraftSection(
                        localDraft: $model.localDraft,
                        togglingDraft: model.togglingDraft,
                        draftError: model.draftError,
                        onToggle: model.performToggleDraft
                    )
                    .transition(.opacity.combined(with: .move(edge: .top)))

                    PRDetailBehindAhead(
                        behindBy: model.behindBy,
                        aheadBy: model.aheadBy,
                        detailError: model.detailError,
                        updatingBranch: model.updatingBranch,
                        branchUpdateResult: model.branchUpdateResult,
                        branchUpdateError: model.branchUpdateError,
                        onUpdateBranch: model.performUpdateBranch
                    )

                    if model.canMerge {
                        PRDetailMergeSection(
                            merging: model.merging,
                            mergeResult: model.mergeResult,
                            mergeError: model.mergeError,
                            mergeMethod: $model.mergeMethod,
                            onMerge: model.performMerge
                        )
                        .transition(.opacity.combined(with: .move(edge: .top)))
                    }
                }

                Divider()
                    .padding(.top, DS.Spacing.sm)

                VStack(alignment: .leading, spacing: DS.Spacing.md) {
                    Text("Latest Comment")
                        .font(DS.Font.small.medium())
                        .foregroundStyle(DS.Color.textSecondary)

                    if let commenter = pr.lastCommentBy, let body = pr.lastCommentBody {
                        PRDetailCommentCard(
                            commenter: commenter,
                            commentBody: body,
                            file: pr.lastReviewFilePath,
                            line: pr.lastReviewLine,
                            url: pr.lastCommentUrl ?? "https://github.com/\(pr.repo)/pull/\(pr.prNumber)",
                            pr: pr,
                            onOpenInIDE: openInRider
                        )
                    } else {
                        VStack(spacing: DS.Spacing.sm) {
                            Image(systemName: "bubble.left")
                                .font(.title2)
                                .foregroundStyle(DS.Color.textTertiary)
                            Text("No comments yet")
                                .font(DS.Font.small)
                                .foregroundStyle(DS.Color.textSecondary)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, DS.Spacing.xl)
                    }
                }
            }
            .animation(DS.Animation.default, value: model.canMerge)
            .animation(DS.Animation.default, value: model.localDraft)
        }
    }

    // MARK: - Commits Tab
    @ViewBuilder
    private var commitsTab: some View {
        Group {
            if model.loadingCommits {
                Spacer()
                ProgressView()
                Spacer()
            } else if let error = model.commitsError {
                VStack(spacing: DS.Spacing.md) {
                    Spacer()
                    Image(systemName: "exclamationmark.circle")
                        .font(.title2)
                        .foregroundStyle(DS.Color.destructive)
                    Text(error)
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.destructive)
                        .multilineTextAlignment(.center)
                    solidButton("Retry", color: .blue) {
                        model.commitsError = nil
                        model.loadCommits()
                    }
                    Spacer()
                }
            } else if model.commits.isEmpty {
                VStack(spacing: DS.Spacing.sm) {
                    Spacer()
                    Image(systemName: "git.commit")
                        .font(.title2)
                        .foregroundStyle(DS.Color.textTertiary)
                    Text("No commits found")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textSecondary)
                    Spacer()
                }
            } else {
                List(model.commits) { commit in
                    Button {
                        if let urlStr = commit.url, let url = URL(string: urlStr) {
                            NSWorkspace.shared.open(url)
                        }
                    } label: {
                        HStack(spacing: DS.Spacing.sm) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(commit.message?.trimmingCharacters(in: .newlines) ?? "")
                                    .font(DS.Font.small.medium())
                                    .foregroundStyle(DS.Color.textPrimary)
                                    .lineLimit(2)
                                HStack(spacing: DS.Spacing.sm) {
                                    Text(commit.authorName ?? commit.authorLogin ?? "unknown")
                                        .font(DS.Font.caption)
                                        .foregroundStyle(DS.Color.accent)
                                    if let date = commit.date {
                                        Text(date)
                                            .font(DS.Font.caption)
                                            .foregroundStyle(DS.Color.textTertiary)
                                    }
                                }
                            }
                            Spacer()
                            if let sha = commit.sha, sha.count >= 7 {
                                Text(String(sha.prefix(7)))
                                    .font(DS.Font.mono(8))
                                    .foregroundStyle(DS.Color.textTertiary)
                            }
                        }
                        .padding(.vertical, 2)
                    }
                    .buttonStyle(.plain)
                    .cursor(.pointingHand)
                    .accessibilityLabel("Commit by \(commit.authorName ?? commit.authorLogin ?? "unknown")")
                    .accessibilityHint("Opens commit on GitHub")
                    .accessibilityAddTraits(.isButton)
                }
                .listStyle(.plain)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: - Files Tab
    @ViewBuilder
    private var filesTab: some View {
        Group {
            if model.loadingFiles {
                Spacer()
                ProgressView()
                Spacer()
            } else if let error = model.filesError {
                VStack(spacing: DS.Spacing.md) {
                    Spacer()
                    Image(systemName: "exclamationmark.circle")
                        .font(.title2)
                        .foregroundStyle(DS.Color.destructive)
                    Text(error)
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.destructive)
                        .multilineTextAlignment(.center)
                    solidButton("Retry", color: .blue) {
                        model.filesError = nil
                        model.loadFiles()
                    }
                    Spacer()
                }
            } else if model.files.isEmpty {
                VStack(spacing: DS.Spacing.sm) {
                    Spacer()
                    Image(systemName: "doc.text")
                        .font(.title2)
                        .foregroundStyle(DS.Color.textTertiary)
                    Text("No files changed")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textSecondary)
                    Spacer()
                }
            } else {
                List(model.files) { file in
                    Button {
                        if let filename = file.filename {
                            openInRider(file: filename, line: nil)
                        }
                    } label: {
                        HStack(spacing: DS.Spacing.sm) {
                            Image(systemName: DS.Icon.fileStatus(file.status))
                                .font(DS.Font.caption)
                                .foregroundStyle(DS.Color.fileStatusColor(file.status))
                            Text(file.filename ?? "")
                                .font(DS.Font.small)
                                .foregroundStyle(DS.Color.textPrimary)
                            Spacer()
                            if let adds = file.additions, adds > 0 {
                                Text("+\(adds)")
                                    .font(DS.Font.mono(8))
                                    .foregroundStyle(DS.Color.success)
                            }
                            if let dels = file.deletions, dels > 0 {
                                Text("-\(dels)")
                                    .font(DS.Font.mono(8))
                                    .foregroundStyle(DS.Color.destructive)
                            }
                        }
                        .padding(.vertical, 2)
                    }
                    .buttonStyle(.plain)
                    .cursor(.pointingHand)
                    .accessibilityLabel("\(file.filename ?? "file"), \(file.status ?? "modified")")
                    .accessibilityHint("Opens in IDE")
                    .accessibilityAddTraits(.isButton)
                }
                .listStyle(.plain)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: - Checks Tab
    @ViewBuilder
    private var checksTab: some View {
        Group {
            if model.loadingChecks {
                Spacer()
                ProgressView()
                Spacer()
            } else if let error = model.checksError {
                VStack(spacing: DS.Spacing.md) {
                    Spacer()
                    Image(systemName: "exclamationmark.circle")
                        .font(.title2)
                        .foregroundStyle(DS.Color.destructive)
                    Text(error)
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.destructive)
                        .multilineTextAlignment(.center)
                    solidButton("Retry", color: .blue) {
                        model.checksError = nil
                        model.loadChecks()
                    }
                    Spacer()
                }
            } else if model.checks.isEmpty {
                VStack(spacing: DS.Spacing.sm) {
                    Spacer()
                    Image(systemName: "checkmark.circle")
                        .font(.title2)
                        .foregroundStyle(DS.Color.textTertiary)
                    Text("No checks found")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textSecondary)
                    Spacer()
                }
            } else {
                List(model.checks) { check in
                    Button {
                        if let urlStr = check.url, let url = URL(string: urlStr) {
                            NSWorkspace.shared.open(url)
                        }
                    } label: {
                        HStack(spacing: DS.Spacing.sm) {
                            Image(systemName: DS.Icon.check(check.conclusion))
                                .font(DS.Font.small)
                                .foregroundStyle(DS.Color.checkColor(check.conclusion))
                            VStack(alignment: .leading, spacing: 2) {
                                Text(check.name ?? "")
                                    .font(DS.Font.small.medium())
                                    .foregroundStyle(DS.Color.textPrimary)
                                HStack(spacing: DS.Spacing.sm) {
                                    Text(check.status ?? "")
                                        .font(DS.Font.caption)
                                        .foregroundStyle(DS.Color.textTertiary)
                                    if let conclusion = check.conclusion {
                                        Text(conclusion)
                                            .font(DS.Font.caption)
                                            .foregroundStyle(DS.Color.checkColor(conclusion))
                                    }
                                }
                            }
                            Spacer()
                            if let started = ApiJSON.parseISO8601(check.startedAt ?? "") {
                                Text(started, style: .relative)
                                    .font(DS.Font.caption)
                                    .foregroundStyle(DS.Color.textTertiary)
                            }
                        }
                        .padding(.vertical, 2)
                    }
                    .buttonStyle(.plain)
                    .cursor(.pointingHand)
                    .accessibilityLabel("\(check.name ?? "check"), \(check.conclusion ?? check.status ?? "pending")")
                    .accessibilityHint("Opens check on GitHub")
                    .accessibilityAddTraits(.isButton)
                }
                .listStyle(.plain)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: - Helpers
    // MARK: - IDE
    private func openInRider(file: String, line: Int?) {
        Task {
            let gitService = deps.gitService
            guard let repoPath = await gitService.findRepoPath(ownerRepo: pr.repo, workspacePath: workspacePath) else {
                return
            }
            let fullPath = (repoPath as NSString).appendingPathComponent(file)
            IDEOpener.openFile(filePath: fullPath, line: line)
        }
    }
}
