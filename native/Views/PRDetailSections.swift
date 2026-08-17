import SwiftUI

// MARK: - Extracted Subviews

struct PRDetailBadges: View {
    let pr: PullRequest

    var body: some View {
        HStack(spacing: DS.Spacing.md) {
            let mergeColor = DS.Color.mergeableColor(pr.mergeableState)
            let mergeLabel = DS.Color.mergeableLabel(pr.mergeableState, ciStatus: pr.ciStatus, conclusion: pr.conclusion)
            Text(mergeLabel)
                .badge(mergeLabel, color: mergeColor)

            let ciColor = DS.Color.ciStatusColor(pr.ciStatus)
            let ciLabel = DS.Color.ciStatusLabel(pr.ciStatus)
            Text(ciLabel)
                .badge(ciLabel, color: ciColor)

            if let c = pr.conclusion {
                let clColor: SwiftUI.Color = c == "success" ? DS.Color.statusGreen : c == "failure" ? DS.Color.statusRed : DS.Color.statusGray
                let clLabel: String = c == "success" ? "CHECKS PASS"
                    : c == "failure" ? "CHECKS FAIL"
                    : c == "neutral" ? "CHECKS NEUTRAL"
                    : c.uppercased()
                Text(clLabel)
                    .badge(clLabel, color: clColor)
            }

            if pr.reviewApproved {
                Text("APPROVED")
                    .badge("APPROVED", color: DS.Color.statusGreen)
            }

            Spacer()
        }
        .transition(.scale.combined(with: .opacity))
    }
}

struct PRDetailDraftSection: View {
    @Binding var localDraft: Bool
    let togglingDraft: Bool
    let draftError: String?
    let onToggle: (Bool) -> Void

    var body: some View {
        HStack(spacing: DS.Spacing.md) {
            if localDraft {
                HStack(spacing: DS.Spacing.sm) {
                    Image(systemName: "pencil")
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.badgeGray)
                    Text("Draft")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.badgeGray)
                }
                .transition(.opacity)
                solidButton("Mark Ready", color: .blue, disabled: togglingDraft, help: "Mark this pull request as ready for review") {
                    onToggle(false)
                }
            } else {
                actionButton("Convert to Draft", color: .gray, help: "Convert this pull request back to draft") {
                    onToggle(true)
                }
                .transition(.opacity)
            }
            if let err = draftError {
                Text(err)
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.destructive)
                    .transition(.opacity)
            }
            if togglingDraft {
                ProgressView()
                    .scaleEffect(0.5)
                    .frame(width: 12)
                    .transition(.opacity.combined(with: .scale(scale: 0.5)))
            }
        }
        .animation(DS.Animation.default, value: localDraft)
        .animation(DS.Animation.default, value: togglingDraft)
        .animation(DS.Animation.default, value: draftError != nil)
    }
}

struct PRDetailBehindAhead: View {
    let behindBy: Int?
    let aheadBy: Int?
    let detailError: String?
    let updatingBranch: Bool
    let branchUpdateResult: String?
    let branchUpdateError: String?
    let onUpdateBranch: () -> Void

    var body: some View {
        if let behind = behindBy, let ahead = aheadBy {
            Divider()
            VStack(spacing: DS.Spacing.sm) {
                HStack(spacing: DS.Spacing.xl) {
                    if behind > 0 {
                        Label("\(behind) behind", systemImage: "arrow.down")
                            .font(DS.Font.small.medium())
                            .foregroundStyle(DS.Color.badgeOrange)
                    } else {
                        Label("Up to date", systemImage: "checkmark")
                            .font(DS.Font.small.medium())
                            .foregroundStyle(DS.Color.success)
                    }
                    if ahead > 0 {
                        Label("\(ahead) ahead", systemImage: "arrow.up")
                            .font(DS.Font.small.medium())
                            .foregroundStyle(DS.Color.accent)
                    }
                    Spacer()
                    if behind > 0 {
                        if updatingBranch {
                            ProgressView()
                                .scaleEffect(0.5)
                                .frame(width: 12)
                                .transition(.opacity)
                        } else if let result = branchUpdateResult {
                            Text(result)
                                .font(DS.Font.caption)
                                .foregroundStyle(DS.Color.success)
                                .transition(.opacity)
                        } else {
                            solidButton("Update branch", color: .orange, help: "Merge the latest base branch into this PR") {
                                onUpdateBranch()
                            }
                            .transition(.opacity)
                        }
                    }
                }
                if let err = branchUpdateError {
                    Text(err)
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.destructive)
                        .transition(.opacity)
                }
            }
            .animation(DS.Animation.default, value: updatingBranch)
            .animation(DS.Animation.default, value: branchUpdateResult != nil)
            .animation(DS.Animation.default, value: branchUpdateError != nil)
        }
        if let error = detailError {
            Text(error)
                .font(DS.Font.caption)
                .foregroundStyle(DS.Color.destructive)
                .transition(.opacity)
        }
    }
}

struct PRDetailMergeSection: View {
    let merging: Bool
    let mergeResult: String?
    let mergeError: String?
    @Binding var mergeMethod: String
    let onMerge: () -> Void

    var body: some View {
        Divider()
        VStack(spacing: DS.Spacing.md) {
            if let result = mergeResult {
                HStack(spacing: DS.Spacing.sm) {
                    Image(systemName: "checkmark.circle.fill")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.success)
                    Text(result)
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.success)
                }
                .transition(.opacity.combined(with: .scale(scale: 0.9)))
            } else if let err = mergeError {
                HStack(spacing: DS.Spacing.sm) {
                    Image(systemName: "exclamationmark.circle.fill")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.destructive)
                    Text(err)
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.destructive)
                        .lineLimit(2)
                }
                .transition(.opacity)
            }
            HStack(spacing: DS.Spacing.md) {
                if merging {
                    ProgressView()
                        .scaleEffect(0.4)
                        .frame(width: 12)
                        .transition(.opacity)
                }
                Picker("", selection: $mergeMethod) {
                    Text("Squash").tag("squash")
                    Text("Rebase").tag("rebase")
                    Text("Merge").tag("merge")
                }
                .pickerStyle(.segmented)
                .scaleEffect(0.75)
                .frame(width: 140)
                .disabled(merging)
                .cursor(.pointingHand)

                solidButton("Merge", color: .green, disabled: merging, help: "Merge this pull request") {
                    onMerge()
                }
            }
        }
        .animation(DS.Animation.default, value: merging)
        .animation(DS.Animation.default, value: mergeResult != nil)
        .animation(DS.Animation.default, value: mergeError != nil)
    }
}

struct PRDetailCommentCard: View {
    let commenter: String
    let commentBody: String
    let file: String?
    let line: Int?
    let url: String
    let pr: PullRequest
    let onOpenInIDE: (String, Int?) -> Void

    var body: some View {
        Button {
            if let u = URL(string: url) { NSWorkspace.shared.open(u) }
        } label: {
            HStack(spacing: DS.Spacing.md) {
                Image(systemName: "bubble.left")
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.accent)
                VStack(alignment: .leading, spacing: DS.Spacing.xs) {
                    HStack(spacing: DS.Spacing.sm) {
                        Text("@\(commenter)")
                            .font(DS.Font.small.medium())
                            .foregroundStyle(DS.Color.accent)
                        if let file = file {
                            Text(shortFile(file))
                                .font(DS.Font.mono(8))
                                .foregroundStyle(DS.Color.textTertiary)
                            if let line = line {
                                Text(":\(line)")
                                    .font(DS.Font.mono(8))
                                    .foregroundStyle(DS.Color.textTertiary)
                            }
                        }
                    }
                    Text(String(commentBody.prefix(200)).replacingOccurrences(of: "\n", with: " "))
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.textSecondary)
                        .lineLimit(4)
                }
            }
            .padding(.horizontal, DS.Spacing.md)
            .padding(.vertical, DS.Spacing.sm)
            .background(DS.Color.accent.opacity(0.1),
                        in: RoundedRectangle(cornerRadius: DS.Radius.sm))
        }
        .buttonStyle(.plain)
        .cursor(.pointingHand)
    }
}

private func shortFile(_ path: String) -> String {
    let parts = path.split(separator: "/")
    return parts.suffix(2).joined(separator: "/")
}
