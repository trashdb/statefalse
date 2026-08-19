import SwiftUI

struct NotificationHistoryView: View {
    let notifications: [ApiNotification]

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

            if notifications.isEmpty {
                EmptyNotificationView()
            } else {
                ForEach(notifications) { notification in
                    VStack(alignment: .leading, spacing: DS.Spacing.xs) {
                        HStack {
                            Text(notification.title)
                                .font(DS.Font.small.medium())
                            Spacer()
                            Text(notification.createdAt, style: .relative)
                                .font(DS.Font.caption)
                                .foregroundStyle(DS.Color.textSecondary)
                        }
                        Text(notification.body)
                            .font(DS.Font.caption)
                            .foregroundStyle(DS.Color.textSecondary)
                        if let url = notification.prUrl {
                            Link("Open in GitHub", destination: url)
                                .font(DS.Font.caption)
                        }
                    }
                    .padding(.vertical, DS.Spacing.sm)
                    Divider()
                }
            }
        }
        .padding(DS.Spacing.xl)
        .frame(width: 390)
    }
}

