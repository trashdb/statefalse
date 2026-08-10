import SwiftUI

// MARK: - Extracted Subviews

struct LocalBranchRow: View {
    let branch: GitBranch
    let isPulling: Bool
    let isDefault: Bool
    let onSelect: () -> Void
    let onPull: () -> Void
    let onDelete: () -> Void

    var body: some View {
        HStack(spacing: DS.Spacing.sm) {
            Button(action: onSelect) {
                HStack(spacing: DS.Spacing.xs) {
                    Text(branch.isCurrent ? "*" : " ")
                        .font(DS.Font.mono(10).bold())
                        .foregroundStyle(DS.Color.success)
                        .frame(width: 8)
                    Text(branch.name)
                        .font(DS.Font.mono(10))
                        .foregroundStyle(branch.isCurrent ? DS.Color.success : DS.Color.textSecondary)
                        .lineLimit(1)
                    if branch.isCurrent {
                        Text("(current)")
                            .font(DS.Font.micro)
                            .foregroundStyle(DS.Color.textTertiary)
                    }
                }
            }
            .buttonStyle(.plain)
            .cursor(.pointingHand)
            .help("Details for \"\(branch.name)\"")
            .frame(maxWidth: .infinity, alignment: .leading)
            if !branch.isCurrent && !isDefault {
                Button(action: onPull) {
                    if isPulling {
                        ProgressView()
                            .scaleEffect(0.5)
                            .frame(width: 14, height: 14)
                    } else {
                        Image(systemName: "arrow.triangle.2.circlepath")
                            .font(DS.Font.micro)
                            .foregroundStyle(DS.Color.textTertiary)
                            .padding(3)
                    }
                }
                .buttonStyle(.plain)
                .help("Pull \"\(branch.name)\" (fetch + rebase)")
                .cursor(.pointingHand)
                .disabled(isPulling)
                Button(action: onDelete) {
                    Image(systemName: "trash")
                        .font(DS.Font.micro)
                        .foregroundStyle(DS.Color.destructive.opacity(0.7))
                        .padding(3)
                        .background(DS.Color.destructive.opacity(0.08), in: RoundedRectangle(cornerRadius: DS.Radius.sm))
                }
                .buttonStyle(.plain)
                .help("Delete \"\(branch.name)\"")
                .cursor(.pointingHand)
            }
        }
        .hoverEffect(cornerRadius: DS.Radius.sm)
        .padding(.leading, 18)
        .padding(.trailing, DS.Spacing.md)
        .padding(.vertical, DS.Spacing.xs)
    }
}

struct RemoteBranchRow: View {
    let branch: RemoteBranch
    let isDefault: Bool
    let onSelect: () -> Void
    let onDelete: () -> Void

    var body: some View {
        HStack(spacing: DS.Spacing.sm) {
            Circle()
                .fill(branch.isMerged ? DS.Color.success : DS.Color.warning)
                .frame(width: 6, height: 6)

            Button(action: onSelect) {
                HStack(spacing: DS.Spacing.xs) {
                    Text(branch.name)
                        .font(DS.Font.mono(10))
                        .foregroundStyle(DS.Color.textSecondary)
                        .lineLimit(1)
                    Text(branch.isMerged ? "merged" : "unmerged")
                        .font(DS.Font.micro)
                        .foregroundStyle(branch.isMerged ? DS.Color.success : DS.Color.warning)
                }
            }
            .buttonStyle(.plain)
            .cursor(.pointingHand)
            .help("Details for \"\(branch.name)\"")
            .frame(maxWidth: .infinity, alignment: .leading)
            if isDefault {
                Text("protected")
                    .font(DS.Font.micro)
                    .foregroundStyle(DS.Color.textTertiary)
            } else {
                Button(action: onDelete) {
                    Image(systemName: "trash")
                        .font(DS.Font.micro)
                        .foregroundStyle(branch.isMerged ? DS.Color.destructive.opacity(0.7) : DS.Color.textTertiary.opacity(0.3))
                        .padding(3)
                        .background((branch.isMerged ? DS.Color.destructive : DS.Color.textTertiary).opacity(0.08), in: RoundedRectangle(cornerRadius: DS.Radius.sm))
                }
                .buttonStyle(.plain)
                .help(branch.isMerged ? "Delete \"\(branch.name)\" (safe — merged)" : "Not merged yet — cannot delete")
                .cursor(.pointingHand)
                .disabled(!branch.isMerged)
            }
        }
        .hoverEffect(cornerRadius: DS.Radius.sm)
        .padding(.leading, 18)
        .padding(.trailing, DS.Spacing.md)
        .padding(.vertical, DS.Spacing.xs)
    }
}

struct DeleteBranchOverlay: View {
    let branchToDelete: (repo: ScannedRepo, branch: GitBranch)?
    let remoteBranchToDelete: (repo: ScannedRepo, branch: RemoteBranch)?
    let onCancel: () -> Void
    let onDelete: () -> Void

    var body: some View {
        ZStack {
            DS.Color.textPrimary.opacity(0.3)
                .ignoresSafeArea()
            VStack(spacing: DS.Spacing.xl) {
                let isRemote = remoteBranchToDelete != nil
                Text(isRemote
                     ? "Delete remote branch \"\(remoteBranchToDelete!.branch.name)\"?"
                     : "Delete branch \"\(branchToDelete!.branch.name)\"?")
                    .font(DS.Font.title)
                    .foregroundStyle(DS.Color.textPrimary)
                Text(isRemote
                     ? "This will run `git push origin --delete` on the remote."
                     : "This will run `git branch -D` locally. Unmerged changes will be lost.")
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.textSecondary)
                    .multilineTextAlignment(.center)
                HStack(spacing: DS.Spacing.xl) {
                    actionButton("Cancel", color: DS.Color.textSecondary, action: onCancel)
                    solidButton("Delete", color: DS.Color.destructive, action: onDelete)
                }
            }
            .padding(DS.Spacing.xxl)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: DS.Radius.xl))
            .shadow(color: .black.opacity(0.3), radius: 16, y: 8)
            .overlay(
                RoundedRectangle(cornerRadius: DS.Radius.xl)
                    .stroke(DS.Color.divider, lineWidth: 1)
            )
            .padding(.horizontal, DS.Spacing.xxl)
        }
    }
}
