import Foundation

// MARK: - Hub events

/// Typed events delivered by the SignalR client. Payloads are decoded into
/// concrete types so consumers never touch raw JSON dicts.
enum HubEvent {
    case connectionEstablished
    case workflowStarted(WorkflowStartedEvent)
    case workflowCompleted(WorkflowCompletedEvent)
    case pullRequestsUpdated
    case prApproved(PrEvent)
    case prCommented(PrCommentedEvent)
    case mainBranchUpdated(MainBranchUpdatedEvent)
    case notificationCreated(ApiNotification)
    case notificationHistory([ApiNotification])
    case connectionClosed
}

struct WorkflowStartedEvent {
    let id: Int?
    let runId: Int64
    let workflowName: String?
    let repo: String
    let actor: String?
    let htmlUrl: String?
    let startedAt: String?
    let branch: String?
    let trigger: String?
}
nonisolated extension WorkflowStartedEvent: Decodable {}

struct WorkflowCompletedEvent {
    let runId: Int64
    let succeeded: Bool?
    let conclusion: String?
    let workflowName: String?
    let repo: String
    let actor: String?
    let htmlUrl: String?
    let trigger: String?
}
nonisolated extension WorkflowCompletedEvent: Decodable {}

struct PrEvent {
    let prNumber: Int?
    let repo: String?
    let reviewerLogin: String?
    let title: String?
}
nonisolated extension PrEvent: Decodable {}

struct PrCommentedEvent {
    let prNumber: Int?
    let repo: String?
    let commenterLogin: String?
    let title: String?
    let commentBody: String?
    let commentUrl: String?
}
nonisolated extension PrCommentedEvent: Decodable {}

struct MainBranchUpdatedEvent {
    let repo: String?
    let prNumber: Int?
    let mergedBy: String?
    let headSha: String?
}
nonisolated extension MainBranchUpdatedEvent: Decodable {}

// MARK: - Protocol

protocol SignalRClientProtocol: AnyObject {
    func connectAndListen(token: String, username: String, onEvent: @escaping (HubEvent) -> Void) async throws
}

// MARK: - Live Implementation

/// Raw SignalR-over-websocket transport. Handles handshake, ping frames and
/// frame-splitting; parses invocations into `HubEvent` values and forwards them
/// via the `onEvent` callback.
final class LiveSignalRClient: SignalRClientProtocol {
    private let baseUrl: String

    private struct NegotiateResponse: Decodable {
        let connectionId: String?
        let connectionToken: String?
    }

    init(baseUrl: String) {
        self.baseUrl = baseUrl
    }

    private func hubWebSocketUrl(token: String, connectionId: String) -> URL {
        let wsUrl = baseUrl
            .replacingOccurrences(of: "https://", with: "wss://")
            .replacingOccurrences(of: "http://", with: "ws://")
        // SignalR WebSockets cannot set Authorization headers; the token travels
        // as the access_token query param, which the JwtBearer handler accepts.
        var components = URLComponents(string: "\(wsUrl)/hub/punishment")!
        components.queryItems = [
            URLQueryItem(name: "access_token", value: token),
            URLQueryItem(name: "id", value: connectionId)
        ]
        return components.url!
    }

    private func negotiate(token: String) async throws -> String {
        guard let url = URL(string: "\(baseUrl)/hub/punishment/negotiate?negotiateVersion=1") else {
            throw SignalRClientError.negotiateFailed
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 15
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.httpBody = Data("{}".utf8)

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200,
              let negotiated = try? JSONDecoder().decode(NegotiateResponse.self, from: data),
              let connectionId = negotiated.connectionToken ?? negotiated.connectionId,
              !connectionId.isEmpty else {
            throw SignalRClientError.negotiateFailed
        }
        return connectionId
    }

    func connectAndListen(token: String, username: String, onEvent: @escaping (HubEvent) -> Void) async throws {
        let connectionId = try await negotiate(token: token)
        let ws = URLSession.shared.webSocketTask(with: hubWebSocketUrl(token: token, connectionId: connectionId))
        ws.resume()
        defer { ws.cancel(with: .normalClosure, reason: nil) }

        try await ws.send(.string("{\"protocol\":\"json\",\"version\":1}\u{1e}"))
        let handshake = try await ws.receiveText()
        let normalizedHandshake = handshake.trimmingCharacters(in: CharacterSet(charactersIn: "\u{1e}\n\r "))
        guard normalizedHandshake.isEmpty || normalizedHandshake == "{}" else { throw SignalRClientError.handshakeFailed }

        let register = "{\"type\":1,\"target\":\"RegisterConnection\",\"arguments\":[\"\(username)\"],\"invocationId\":\"1\"}\u{1e}"
        try await ws.send(.string(register))
        onEvent(.connectionEstablished)

        let watchdog = Task { [ws] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 10_000_000_000)
                guard !Task.isCancelled else { break }
                ws.sendPing { error in
                    if error != nil {
                        ws.cancel(with: .abnormalClosure, reason: nil)
                    }
                }
            }
        }
        defer { watchdog.cancel() }

        while !Task.isCancelled {
            let text = try await ws.receiveText()
            if handleText(text, ws: ws, onEvent: onEvent) {
                throw SignalRClientError.connectionClosed
            }
        }
    }

    @discardableResult
    private nonisolated func handleText(_ text: String, ws: URLSessionWebSocketTask, onEvent: (HubEvent) -> Void) -> Bool {
        for part in text.components(separatedBy: "\u{1e}").filter({ !$0.isEmpty }) {
            guard let data = part.data(using: .utf8),
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let type = json["type"] as? Int else { continue }

            switch type {
            case 1:
                if let event = parseInvocation(json) {
                    onEvent(event)
                }
            case 6:
                Task { try? await ws.send(.string("{\"type\":6}\u{1e}")) }
            case 7:
                onEvent(.connectionClosed)
                return true
            default:
                break
            }
        }
        return false
    }

    private nonisolated func parseInvocation(_ json: [String: Any]) -> HubEvent? {
        guard let target = json["target"] as? String,
              let args = json["arguments"] as? [[String: Any]],
              let data = args.first else {
            print("Statefalse SignalR invocation has an invalid shape: \(json["target"] ?? "unknown")")
            return nil
        }

        let decoder = JSONDecoder()
        func decode<T: Decodable>(_ type: T.Type) -> T? {
            guard let d = try? JSONSerialization.data(withJSONObject: data) else {
                print("Statefalse SignalR event \(target) could not serialize its payload")
                return nil
            }
            do {
                return try decoder.decode(T.self, from: d)
            } catch {
                print("Statefalse SignalR event \(target) could not decode: \(error)")
                return nil
            }
        }

        switch target {
        case "WorkflowRunStarted":
            return decode(WorkflowStartedEvent.self).map { .workflowStarted($0) }
        case "WorkflowRunCompleted":
            return decode(WorkflowCompletedEvent.self).map { .workflowCompleted($0) }
        case "PullRequestsUpdated":
            return .pullRequestsUpdated
        case "PrApproved":
            return decode(PrEvent.self).map { .prApproved($0) }
        case "PrCommented":
            return decode(PrCommentedEvent.self).map { .prCommented($0) }
        case "MainBranchUpdated":
            return decode(MainBranchUpdatedEvent.self).map { .mainBranchUpdated($0) }
        case "NotificationCreated":
            return decode(ApiNotification.self).map { .notificationCreated($0) }
        case "NotificationHistory":
            return decode([ApiNotification].self).map { .notificationHistory($0) }
        default:
            return nil
        }
    }

    enum SignalRClientError: Error {
        case handshakeFailed
        case negotiateFailed
        case connectionClosed
    }
}

private extension URLSessionWebSocketTask {
    func receiveText() async throws -> String {
        switch try await receive() {
        case .string(let text):
            return text
        case .data(let data):
            guard let text = String(data: data, encoding: .utf8) else {
                throw LiveSignalRClient.SignalRClientError.handshakeFailed
            }
            return text
        @unknown default:
            throw LiveSignalRClient.SignalRClientError.handshakeFailed
        }
    }
}
