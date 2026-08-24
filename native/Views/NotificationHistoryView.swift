import SwiftUI

struct NotificationHistoryView: View {
    @ObservedObject var signalR: SignalRService

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Spacing.md) {
            HStack {
                Text("Notification history")
                    .font(DS.Font.title)
                Spacer()
                Text("Last 24 hours")
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.textSecondary)
            }

            let unreadCount = signalR.notifications.filter { !$0.isRead }.count
            if unreadCount > 0 {
                HStack {
                    Text("\(unreadCount) unread")
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.textSecondary)
                    Spacer()
                    Button("Mark all as read") {
                        Task { _ = await signalR.markAllNotificationsRead() }
                    }
                    .font(DS.Font.caption)
                    .buttonStyle(.link)
                }
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
                                    Link("Open in GitHub", destination: url)
                                        .font(DS.Font.caption)
                                }
                                if !notification.isRead {
                                    Button("Mark as read") {
                                        Task { _ = await signalR.markNotificationRead(id: notification.id) }
                                    }
                                    .font(DS.Font.caption)
                                    .buttonStyle(.link)
                                }
                            }
                            .padding(.vertical, DS.Spacing.md)
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
        .frame(width: 430, height: 520)
        .background(VisualEffectBackground(material: .sidebar))
        .closeOnEscape { NotificationHistoryPanelManager.shared.close() }
        .closeOnCmdW { NotificationHistoryPanelManager.shared.close() }
    }
}

