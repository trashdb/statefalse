import SwiftUI

// MARK: - Connection Test Result

enum ConnectionTestResult {
    case success(String)
    case failure(String)

    var isSuccess: Bool {
        if case .success = self { return true }
        return false
    }

    var message: String {
        switch self {
        case .success(let m): return m
        case .failure(let m): return m
        }
    }
}

// MARK: - Collapsible Section

struct CollapsibleSection<Content: View>: View {
    let title: String
    let icon: String
    @State private var isExpanded = false
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Spacing.md) {
            Button {
                withAnimation(DS.Animation.hover) {
                    isExpanded.toggle()
                }
            } label: {
                HStack(spacing: DS.Spacing.sm) {
                    Image(systemName: icon)
                        .font(DS.Font.caption)
                        .foregroundStyle(DS.Color.accent)
                    Text(title)
                        .font(DS.Font.section)
                        .foregroundStyle(DS.Color.textPrimary)
                    Spacer()
                    Image(systemName: "chevron.right")
                        .font(DS.Font.micro)
                        .foregroundStyle(DS.Color.textTertiary)
                        .rotationEffect(.degrees(isExpanded ? 90 : 0))
                }
                .padding(.vertical, DS.Spacing.md)
                .padding(.horizontal, DS.Spacing.xs)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .cursor(.pointingHand)

            if isExpanded {
                content
                    .padding(.leading, DS.Spacing.md)
                    .padding(.bottom, DS.Spacing.md)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }

            Divider()
        }
    }
}

// MARK: - IDE List View

struct IDEListView: View {
    @Binding var defaultIDE: String
    @Binding var customIDECommand: String
    @State private var showPicker = false
    @State private var search = ""

    private var current: IDEDefinition { ideDefinition(for: defaultIDE) }
    private var filtered: [IDEDefinition] {
        search.isEmpty
            ? installedIDEs
            : installedIDEs.filter { $0.displayName.localizedCaseInsensitiveContains(search) || $0.id.localizedCaseInsensitiveContains(search) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Spacing.lg) {
            Text("Select your favourite IDE from the dropdown.")
                .font(DS.Font.small)
                .foregroundStyle(DS.Color.textSecondary)

            Button {
                showPicker = true
            } label: {
                HStack(spacing: DS.Spacing.lg) {
                    current.viewIcon(size: 22)
                    Text(current.displayName)
                        .font(DS.Font.body)
                    Spacer()
                    Image(systemName: "chevron.down")
                        .font(DS.Font.small)
                        .foregroundStyle(DS.Color.textSecondary)
                }
                .padding(.horizontal, DS.Spacing.xl)
                .padding(.vertical, DS.Spacing.lg - 1)
                .background(DS.Color.fieldBackground, in: RoundedRectangle(cornerRadius: DS.Radius.md))
                .overlay(
                    RoundedRectangle(cornerRadius: DS.Radius.md)
                        .stroke(DS.Color.divider, lineWidth: 1)
                )
            }
            .buttonStyle(.plain)
            .help("Choose your preferred IDE for opening files")
            .popover(isPresented: $showPicker, arrowEdge: .bottom) {
                VStack(spacing: DS.Spacing.md) {
                    styledTextField("Search IDE…", text: $search, help: "Filter available IDEs by name")

                    ScrollView(.vertical) {
                        LazyVStack(spacing: DS.Spacing.xs) {
                            ForEach(filtered, id: \.id) { ide in
                                IDERow(
                                    ide: ide,
                                    isSelected: defaultIDE == ide.id,
                                    action: {
                                        defaultIDE = ide.id
                                        if ide.id != "custom" { customIDECommand = "" }
                                        showPicker = false
                                        search = ""
                                    }
                                )
                            }
                        }
                    }
                    .frame(height: min(CGFloat(filtered.count) * 30 + 4, 300))
                }
                .padding(DS.Spacing.xl)
                .frame(width: 320)
            }
            .cursor(.pointingHand)

            if defaultIDE == "custom" {
                styledTextField("e.g. myeditor://open?file={file}&line={line}", text: $customIDECommand, help: "Custom URL scheme to open files in your editor")
            }
        }
    }
}

private struct IDERow: View {
    let ide: IDEDefinition
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: DS.Spacing.xl) {
                ide.viewIcon(size: 26)
                    .frame(width: 32)
                Text(ide.displayName)
                    .font(DS.Font.body)
                    .foregroundStyle(DS.Color.textPrimary)
                Spacer()
                if isSelected {
                    Image(systemName: "checkmark")
                        .font(DS.Font.body.bold())
                        .foregroundStyle(DS.Color.accent)
                }
            }
            .padding(.horizontal, DS.Spacing.xl)
            .padding(.vertical, DS.Spacing.lg)
            .background(
                isSelected
                    ? DS.Color.fieldBackground
                    : Color.clear,
                in: RoundedRectangle(cornerRadius: DS.Radius.sm)
            )
        }
        .buttonStyle(.plain)
        .cursor(.pointingHand)
    }
}
