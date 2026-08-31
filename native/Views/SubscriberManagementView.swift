import SwiftUI

// MARK: - Subscriber Management View
struct SubscriberManagementView: View {
    let pr: PullRequest
    let gitHubId: Int64
    private let api: ApiClientProtocol

    @State private var isLoading = false
    @State private var isLoadingUsers = false
    @State private var errorMessage: String?
    @State private var subscribers: [ApiSubscriberInfo] = []
    @State private var availableUsers: [ApiAvailableUser] = []
    @State private var selectedUserIds: Set<Int64> = []
    @State private var showUserPicker = false

    init(pr: PullRequest, gitHubId: Int64, deps: Dependencies) {
        self.pr = pr
        self.gitHubId = gitHubId
        self.api = deps.apiClient
    }
    
    var body: some View {
        VStack(alignment: .leading, spacing: DS.Spacing.sm) {
            HStack(spacing: DS.Spacing.sm) {
                Image(systemName: "person.2.fill")
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.accent)
                Text("Subscribers")
                    .font(DS.Font.small.medium())
                    .foregroundStyle(DS.Color.textSecondary)
                Spacer()
                
                solidButton("Add", color: DS.Color.accent, disabled: isLoadingUsers) {
                    Task { await loadAvailableUsers() }
                    showUserPicker = true
                }
            }
            
            // Current subscribers list
            if subscribers.isEmpty && !isLoading {
                Text("No subscribers yet")
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.textTertiary)
                    .padding(.vertical, DS.Spacing.xs)
            } else {
                ForEach(subscribers) { sub in
                    HStack(spacing: DS.Spacing.sm) {
                        AsyncImage(url: URL(string: sub.avatarUrl)) { img in
                            img.resizable()
                        } placeholder: {
                            Image(systemName: "person.circle.fill")
                                .foregroundStyle(DS.Color.textTertiary)
                        }
                        .frame(width: 18, height: 18)
                        .clipShape(Circle())
                        
                        Text("@\(sub.gitHubUsername)")
                            .font(DS.Font.small)
                            .foregroundStyle(DS.Color.textPrimary)
                            .lineLimit(1)
                        
                        Spacer()
                        
                        solidButton("Remove", color: .secondary, disabled: isLoading) {
                            Task { await removeSubscriber(sub.gitHubId) }
                        }
                    }
                    .padding(.vertical, 2)
                }
            }
            
            if let error = errorMessage {
                Text(error)
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.destructive)
            }
        }
        .padding(.vertical, DS.Spacing.xs)
        .onAppear {
            Task { await loadSubscribers() }
        }
        .popover(isPresented: $showUserPicker, arrowEdge: .bottom) {
            UserPickerView(
                users: availableUsers,
                currentSubscribers: subscribers.map { $0.gitHubId },
                currentUserId: gitHubId,
                selectedIds: $selectedUserIds,
                onDone: {
                    showUserPicker = false
                    Task { await addSelectedSubscribers() }
                },
                onCancel: { showUserPicker = false }
            )
            .frame(width: 220)
        }
    }
    
    private func loadSubscribers() async {
        isLoading = true
        let result = await api.fetchSubscribers(prNumber: pr.prNumber, repo: pr.repo)
        switch result {
        case .success(let subs): subscribers = subs
        case .failure: errorMessage = "Failed to load subscribers"
        }
        isLoading = false
    }
    
    private func loadAvailableUsers() async {
        isLoadingUsers = true
        let result = await api.fetchSubscriberCandidates(prNumber: pr.prNumber, repo: pr.repo)
        switch result {
        case .success(let users):
            availableUsers = users.filter { $0.gitHubId != gitHubId }
        case .failure:
            break
        }
        isLoadingUsers = false
    }
    
    private func addSelectedSubscribers() async {
        isLoading = true
        errorMessage = nil
        
        for subscriberId in selectedUserIds {
            if let error = await api.addSubscriber(prNumber: pr.prNumber, repo: pr.repo, subscriberId: subscriberId) {
                errorMessage = error
            }
        }
        
        selectedUserIds.removeAll()
        showUserPicker = false
        await loadSubscribers()
        isLoading = false
    }
    
    private func removeSubscriber(_ subscriberGitHubId: Int64) async {
        isLoading = true
        errorMessage = nil
        if let error = await api.removeSubscriber(prNumber: pr.prNumber, repo: pr.repo, subscriberId: subscriberGitHubId) {
            errorMessage = error
        } else {
            await loadSubscribers()
        }
        isLoading = false
    }
}

// MARK: - User Picker View
struct UserPickerView: View {
    let users: [ApiAvailableUser]
    let currentSubscribers: [Int64]
    let currentUserId: Int64
    @Binding var selectedIds: Set<Int64>
    let onDone: () -> Void
    let onCancel: () -> Void
    
    var filteredUsers: [ApiAvailableUser] {
        users.filter { user in
            user.gitHubId != currentUserId &&
            !currentSubscribers.contains(user.gitHubId) &&
            !selectedIds.contains(user.gitHubId)
        }
    }
    
    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("Add Subscribers")
                    .font(DS.Font.small.medium())
                    .foregroundStyle(DS.Color.textPrimary)
                Spacer()
            }
            .padding(DS.Spacing.md)
            
            Divider()
            
            if filteredUsers.isEmpty {
                VStack(spacing: DS.Spacing.sm) {
                    Image(systemName: "person.2")
                        .font(.title2)
                        .foregroundStyle(DS.Color.textTertiary)
                    Text("No other users available")
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.textSecondary)
                }
                .frame(maxWidth: .infinity)
                .padding(DS.Spacing.xl)
            } else {
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(filteredUsers) { user in
                            UserPickerRow(
                                user: user,
                                isSelected: selectedIds.contains(user.gitHubId),
                                onToggle: {
                                    if selectedIds.contains(user.gitHubId) {
                                        selectedIds.remove(user.gitHubId)
                                    } else {
                                        selectedIds.insert(user.gitHubId)
                                    }
                                }
                            )
                            Divider()
                        }
                    }
                }
            }
            
            Divider()
            
            HStack(spacing: DS.Spacing.sm) {
                Spacer()
                Button("Cancel", action: onCancel)
                    .buttonStyle(.plain)
                    .font(DS.Font.small)
                    .foregroundStyle(DS.Color.textSecondary)
                    .cursor(.pointingHand)
                solidButton("Add Selected", color: DS.Color.accent, disabled: selectedIds.isEmpty) {
                    onDone()
                }
            }
            .padding(DS.Spacing.md)
        }
        .background(Color(NSColor.windowBackgroundColor))
    }
}

struct UserPickerRow: View {
    let user: ApiAvailableUser
    let isSelected: Bool
    let onToggle: () -> Void
    
    var body: some View {
        Button(action: onToggle) {
            HStack(spacing: DS.Spacing.md) {
                Image(systemName: isSelected ? "checkmark.square.fill" : "square")
                    .font(DS.Font.small)
                    .foregroundStyle(isSelected ? DS.Color.accent : DS.Color.textTertiary)
                
                if let avatarUrl = user.avatarUrl, !avatarUrl.isEmpty {
                    AsyncImage(url: URL(string: avatarUrl)) { img in
                        img.resizable()
                    } placeholder: {
                        Image(systemName: "person.circle.fill")
                            .foregroundStyle(DS.Color.textTertiary)
                    }
                    .frame(width: 22, height: 22)
                    .clipShape(Circle())
                } else {
                    Image(systemName: "person.circle.fill")
                        .font(DS.Font.body)
                        .foregroundStyle(DS.Color.textTertiary)
                        .frame(width: 22, height: 22)
                }
                
                Text("@\(user.login)")
                    .font(DS.Font.body)
                    .foregroundStyle(isSelected ? DS.Color.textPrimary : DS.Color.textSecondary)
                
                Spacer()
            }
            .padding(.horizontal, DS.Spacing.xxl)
            .padding(.vertical, DS.Spacing.lg)
            .background(
                isSelected ? DS.Color.accent.opacity(0.08) : Color.clear,
                in: RoundedRectangle(cornerRadius: DS.Radius.sm)
            )
        }
        .buttonStyle(.plain)
        .cursor(.pointingHand)
    }
}
