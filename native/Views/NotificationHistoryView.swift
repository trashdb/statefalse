import AppKit
import SwiftUI

struct NotificationHistoryView: View {
    @ObservedObject var signalR: SignalRService
    var onBack: (() -> Void)? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Spacing.md) {
            if signalR.unreadNotificationCount > 0 {
                Text("\(signalR.unreadNotificationCount) unread")
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.textSecondary)
            }

            ScrollView {
                if signalR.notifications.isEmpty {
                    EmptyNotificationView()
                } else {
                    LazyVStack(alignment: .leading, spacing: 0) {
                        ForEach(signalR.notifications) { notification in
                            VStack(alignment: .leading, spacing: DS.Spacing.xs) {
                                HStack(alignment: .firstTextBaseline) {
                                    Circle()
                                        .fill(notification.isRead ? DS.Color.textSecondary.opacity(0.35) : DS.Color.accent)
                                        .frame(width: 6, height: 6)
                                    Text(notification.title)
                                        .font(DS.Font.small.medium())
                                        .lineLimit(2)
                                    Spacer(minLength: DS.Spacing.md)
                                    Text(notification.createdAt, style: .relative)
                                        .font(DS.Font.caption)
                                        .foregroundStyle(DS.Color.textSecondary)
                                        .lineLimit(1)
                                }
                                Text(notification.body)
                                    .font(DS.Font.caption)
                                    .foregroundStyle(DS.Color.textSecondary)
                                    .lineLimit(4)
                                if let url = notification.prUrl {
                                    Button("Open in GitHub") {
                                        open(notification, at: url)
                                    }
                                        .font(DS.Font.caption)
                                        .buttonStyle(.link)
                                }
                            }
                            .padding(.vertical, DS.Spacing.md)
                            .contentShape(Rectangle())
                            .onTapGesture {
                                guard notification.prUrl == nil else { return }
                                Task { _ = await signalR.markNotificationRead(id: notification.id) }
                            }
                            if notification.id != signalR.notifications.last?.id {
                                Divider()
                            }
                        }
                    }
                }
            }
        }
        .padding(.horizontal, DS.Spacing.xxl)
        .padding(.vertical, DS.Spacing.xl)
        .background(VisualEffectBackground(material: .sidebar))
        .closeOnEscape {
            if let onBack { onBack() } else { NotificationHistoryPanelManager.shared.close() }
        }
        .closeOnCmdW {
            if let onBack { onBack() } else { NotificationHistoryPanelManager.shared.close() }
        }
    }

    private func open(_ notification: ApiNotification, at url: URL) {
        Task {
            _ = await signalR.markNotificationRead(id: notification.id)
            NSWorkspace.shared.open(url)
        }
    }
}

