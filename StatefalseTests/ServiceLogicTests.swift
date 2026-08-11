import XCTest
@testable import Statefalse

final class DTOMapperTests: XCTestCase {
    func testWorkflowRunMapsFieldsAndDefaults() {
        let dto = ApiWorkflowRun(
            id: 7, runId: 42, workflowName: nil, repo: "owner/repo", actor: "alice",
            headBranch: "feature/x", trigger: "push", prNumber: 12, prTitle: "Title",
            status: "in_progress", htmlUrl: nil,
            startedAt: Date(timeIntervalSince1970: 1_700_000_000),
            targetGitHubIds: nil
        )

        let run = DTOMapper.workflowRun(dto)

        XCTAssertEqual(run.dbId, 7)
        XCTAssertEqual(run.runId, 42)
        XCTAssertEqual(run.workflowName, "Workflow")
        XCTAssertEqual(run.repo, "owner/repo")
        XCTAssertEqual(run.headBranch, "feature/x")
        XCTAssertEqual(run.prNumber, 12)
        XCTAssertEqual(run.status, "in_progress")
        XCTAssertEqual(run.htmlUrl, "")
        XCTAssertEqual(run.startedAt, Date(timeIntervalSince1970: 1_700_000_000))
        XCTAssertTrue(run.targetGitHubIds.isEmpty)
        XCTAssertTrue(run.isRunning)
    }

    func testPullRequestMapsFieldsAndDefaults() {
        let dto = ApiPullRequest(
            prNumber: 9, title: "Fix", repo: "owner/repo", headBranch: nil,
            baseBranch: nil, htmlUrl: "https://github.com/owner/repo/pull/9",
            status: "open", conclusion: nil, draft: nil, mergeableState: "clean",
            ciStatus: nil, reviewApproved: true, lastCommentBy: nil, lastCommentBody: nil,
            lastCommentAt: nil, isSubscribed: nil, subscriberIds: nil, authorGitHubId: 123
        )

        let pr = DTOMapper.pullRequest(dto)

        XCTAssertEqual(pr.prNumber, 9)
        XCTAssertEqual(pr.title, "Fix")
        XCTAssertEqual(pr.headBranch, "")
        XCTAssertEqual(pr.baseBranch, "")
        XCTAssertEqual(pr.status, "open")
        XCTAssertEqual(pr.ciStatus, "ready")
        XCTAssertFalse(pr.draft)
        XCTAssertTrue(pr.reviewApproved)
        XCTAssertTrue(pr.subscriberIds.isEmpty)
        XCTAssertEqual(pr.authorGitHubId, 123)
        XCTAssertEqual(pr.prUrl.absoluteString, "https://github.com/owner/repo/pull/9")
    }

    func testStartedDateParsesISOAndFallsBack() {
        let parsed = DTOMapper.startedDate(from: "2024-01-01T10:00:00Z")
        XCTAssertEqual(parsed.timeIntervalSince1970, 1_704_103_200, accuracy: 60)

        let fallback = DTOMapper.startedDate(from: nil)
        XCTAssertLessThan(abs(fallback.timeIntervalSinceNow), 5)
    }
}

final class ReadyMergeNotifierTests: XCTestCase {
    private func pr(
        status: String = "open",
        draft: Bool = false,
        ciStatus: String = "ready",
        mergeableState: String? = "clean",
        prNumber: Int64 = 1
    ) -> PullRequest {
        PullRequest(
            prNumber: prNumber, title: "PR \(prNumber)", repo: "owner/repo",
            headBranch: "feature/x", baseBranch: "main",
            htmlUrl: nil, status: status, conclusion: nil, draft: draft,
            mergeableState: mergeableState, ciStatus: ciStatus,
            reviewApproved: true, lastCommentBy: nil, lastCommentBody: nil,
            lastCommentAt: nil, lastCommentUrl: nil, lastReviewFilePath: nil,
            lastReviewLine: nil, isSubscribed: false, subscriberIds: [],
            authorGitHubId: 123
        )
    }

    func testIsReadyToMerge_OnlyWhenOpenNotDraftReadyClean() {
        XCTAssertTrue(ReadyMergeNotifier.isReadyToMerge(pr()))
        XCTAssertFalse(ReadyMergeNotifier.isReadyToMerge(pr(status: "merged")))
        XCTAssertFalse(ReadyMergeNotifier.isReadyToMerge(pr(draft: true)))
        XCTAssertFalse(ReadyMergeNotifier.isReadyToMerge(pr(ciStatus: "review")))
        XCTAssertFalse(ReadyMergeNotifier.isReadyToMerge(pr(mergeableState: "dirty")))
        XCTAssertFalse(ReadyMergeNotifier.isReadyToMerge(pr(mergeableState: "behind")))
    }

    func testProcess_SeedsWithoutNotificationsOnFirstCall() {
        var fired: [String] = []
        let notifier = ReadyMergeNotifier { title, _, _, _ in fired.append(title) }

        notifier.process(current: [pr(prNumber: 1), pr(prNumber: 2)])

        XCTAssertTrue(fired.isEmpty, "First sync must seed silently")
    }

    func testProcess_FiresForNewlyReadyAndDedupsWhileReady() {
        var fired: [String] = []
        let notifier = ReadyMergeNotifier { title, _, _, _ in fired.append(title) }

        notifier.process(current: [pr(prNumber: 1)])
        fired.removeAll()

        notifier.process(current: [pr(prNumber: 1), pr(prNumber: 2)])
        XCTAssertEqual(fired, ["PR #2 ready to merge 🚀"])

        // Still ready -> no re-fire
        fired.removeAll()
        notifier.process(current: [pr(prNumber: 1), pr(prNumber: 2)])
        XCTAssertTrue(fired.isEmpty)
    }

    func testProcess_FiresAgainAfterRegressing() {
        var fired: [String] = []
        let notifier = ReadyMergeNotifier { title, _, _, _ in fired.append(title) }

        notifier.process(current: [pr(prNumber: 1)])
        fired.removeAll()

        notifier.process(current: [pr(prNumber: 1)])
        XCTAssertTrue(fired.isEmpty)

        // Regresses (new commits) then ready again -> notify again
        notifier.process(current: [pr(ciStatus: "review", prNumber: 1)])
        notifier.process(current: [pr(prNumber: 1)])
        XCTAssertEqual(fired.count, 1)
    }

    func testProcess_ForgetsGonePRs() {
        var fired: [String] = []
        let notifier = ReadyMergeNotifier { title, _, _, _ in fired.append(title) }

        notifier.process(current: [pr(prNumber: 1)])
        notifier.process(current: [pr(prNumber: 2)])
        XCTAssertEqual(fired, ["PR #2 ready to merge 🚀"])
    }
}

final class WorkflowEventReducerTests: XCTestCase {
    private func runningRun(runId: Int64, name: String = "CI") -> WorkflowRun {
        WorkflowRun(
            id: UUID(), dbId: 1, runId: runId, workflowName: name, repo: "owner/repo",
            actor: "alice", headBranch: "feature/x", trigger: "push",
            prNumber: 3, prTitle: "PR", status: "in_progress",
            htmlUrl: "https://github.com/owner/repo/actions/runs/\(runId)",
            startedAt: Date(), completedAt: nil, targetGitHubIds: [123]
        )
    }

    func testReduceCompleted_FailureProducesEventAndNotification() {
        let event = WorkflowCompletedEvent(
            runId: 5, succeeded: false, conclusion: "failure", workflowName: "CI",
            repo: "owner/repo", actor: "alice", htmlUrl: nil, trigger: "push"
        )

        let update = WorkflowEventReducer.reduceCompleted(
            event, runningWorkflows: [runningRun(runId: 5)], recentWorkflows: []
        )

        XCTAssertEqual(update.runStatus, .failure)
        XCTAssertTrue(update.shouldResetStatus)
        XCTAssertTrue(update.runningWorkflows.isEmpty)
        XCTAssertEqual(update.recentWorkflows.first?.status, "failure")
        XCTAssertNotNil(update.lastEvent)
        XCTAssertEqual(update.lastEvent?.culprit, "alice")
        XCTAssertNotNil(update.notification)
    }

    func testReduceCompleted_SuccessNoNotification() {
        let event = WorkflowCompletedEvent(
            runId: 5, succeeded: true, conclusion: "success", workflowName: "CI",
            repo: "owner/repo", actor: "alice", htmlUrl: nil, trigger: "push"
        )

        let update = WorkflowEventReducer.reduceCompleted(
            event, runningWorkflows: [runningRun(runId: 5)], recentWorkflows: []
        )

        XCTAssertEqual(update.runStatus, .success)
        XCTAssertNil(update.lastEvent)
        XCTAssertNil(update.notification)
        XCTAssertEqual(update.recentWorkflows.first?.status, "success")
    }

    func testReduceCompleted_CancelledConclusionMapsToCancelled() {
        let event = WorkflowCompletedEvent(
            runId: 5, succeeded: false, conclusion: "cancelled", workflowName: "CI",
            repo: "owner/repo", actor: "alice", htmlUrl: nil, trigger: "push"
        )

        let update = WorkflowEventReducer.reduceCompleted(event, runningWorkflows: [], recentWorkflows: [])

        XCTAssertEqual(update.recentWorkflows.first?.status, "cancelled")
        XCTAssertNil(update.lastEvent)
        XCTAssertNil(update.notification)
    }

    func testReduceCompleted_ReplacesInProgressRunInRecent() {
        let existing = runningRun(runId: 5)
        let event = WorkflowCompletedEvent(
            runId: 5, succeeded: true, conclusion: "success", workflowName: "CI",
            repo: "owner/repo", actor: "alice", htmlUrl: nil, trigger: "push"
        )

        let update = WorkflowEventReducer.reduceCompleted(event, runningWorkflows: [], recentWorkflows: [existing])

        XCTAssertEqual(update.recentWorkflows.count, 1)
        XCTAssertEqual(update.recentWorkflows.first?.status, "success")
        XCTAssertEqual(update.recentWorkflows.first?.dbId, existing.dbId)
        XCTAssertEqual(update.recentWorkflows.first?.startedAt, existing.startedAt)
        XCTAssertNotNil(update.recentWorkflows.first?.completedAt)
    }
}

final class ApiJSONDecodingTests: XCTestCase {
    private func decode<T: Decodable>(_ type: T.Type, _ json: String) throws -> T {
        try ApiJSON.decoder.decode(T.self, from: Data(json.utf8))
    }

    func testApiPullRequestDecodesRoundTripDate() throws {
        let json = """
        {"prNumber":9,"title":"Fix","repo":"owner/repo","headBranch":null,"baseBranch":null,
         "htmlUrl":"https://github.com/owner/repo/pull/9","status":"open","conclusion":null,
         "draft":false,"mergeableState":"clean","ciStatus":"ready","reviewApproved":true,
         "lastCommentBy":"bob","lastCommentBody":"lgtm","lastCommentAt":"2024-06-01T09:30:00.0000000Z",
         "lastCommentUrl":null,"lastReviewFilePath":null,"lastReviewLine":null,
         "isSubscribed":true,"subscriberIds":[1,2],"authorGitHubId":123}
        """
        let pr = try decode(ApiPullRequest.self, json)

        XCTAssertEqual(pr.lastCommentBy, "bob")
        XCTAssertNotNil(pr.lastCommentAt)
        let expected = Date(timeIntervalSince1970: 1_717_234_200)
        XCTAssertEqual(pr.lastCommentAt!.timeIntervalSince1970, expected.timeIntervalSince1970, accuracy: 60)
        XCTAssertEqual(pr.subscriberIds, [1, 2])
        XCTAssertEqual(pr.authorGitHubId, 123)
    }

    func testApiPullRequestPlainDecoderRejectsRoundTripDate() throws {
        let json = """
        {"prNumber":9,"title":"Fix","repo":"owner/repo","lastCommentAt":"2024-06-01T09:30:00.0000000Z"}
        """
        let data = Data(json.utf8)
        XCTAssertThrowsError(try JSONDecoder().decode([ApiPullRequest].self, from: data))
    }

    func testApiWorkflowRunDecodesRoundTripDate() throws {
        let json = """
        {"id":7,"runId":42,"workflowName":null,"repo":"owner/repo","actor":"alice",
         "headBranch":"feature/x","trigger":"push","prNumber":12,"prTitle":"Title",
         "status":"in_progress","htmlUrl":null,"startedAt":"2024-06-01T09:30:00.0000000Z",
         "targetGitHubIds":null}
        """
        let run = try decode(ApiWorkflowRun.self, json)

        XCTAssertEqual(run.runId, 42)
        XCTAssertEqual(run.status, "in_progress")
        XCTAssertNotNil(run.startedAt)
        let expected = Date(timeIntervalSince1970: 1_717_234_200)
        XCTAssertEqual(run.startedAt.timeIntervalSince1970, expected.timeIntervalSince1970, accuracy: 60)
        XCTAssertNil(run.targetGitHubIds)
    }

    func testWebhookLogEntryDecodesRoundTripDate() throws {
        let json = """
        {"eventType":"workflow_run","action":"completed","repo":"owner/repo",
         "workflowName":"CI","outcome":"failure","message":"conclusion=failure",
         "occurredAt":"2024-06-01T09:30:00.0000000Z"}
        """
        let entry = try decode(WebhookLogEntry.self, json)

        XCTAssertEqual(entry.eventType, "workflow_run")
        XCTAssertEqual(entry.outcome, "failure")
        let expected = Date(timeIntervalSince1970: 1_717_234_200)
        XCTAssertEqual(entry.occurredAt.timeIntervalSince1970, expected.timeIntervalSince1970, accuracy: 60)
    }

    func testHubEventsDecodeFromSignalRArgs() throws {
        let completed = try decode(WorkflowCompletedEvent.self,
            #"{"runId":5,"succeeded":false,"conclusion":"failure","workflowName":"CI","repo":"owner/repo","actor":"alice","htmlUrl":null,"trigger":"push"}"#)
        XCTAssertEqual(completed.runId, 5)
        XCTAssertEqual(completed.conclusion, "failure")
        XCTAssertEqual(completed.repo, "owner/repo")

        let started = try decode(WorkflowStartedEvent.self,
            #"{"id":1,"runId":9,"workflowName":"CI","repo":"owner/repo","actor":"alice","htmlUrl":"u","startedAt":"2024-06-01T09:30:00Z","branch":"feature/x","trigger":"push"}"#)
        XCTAssertEqual(started.runId, 9)
        XCTAssertEqual(started.branch, "feature/x")
        XCTAssertEqual(started.startedAt, "2024-06-01T09:30:00Z")

        let approved = try decode(PrEvent.self,
            #"{"prNumber":3,"repo":"owner/repo","reviewerLogin":"carol","title":"PR"}"#)
        XCTAssertEqual(approved.prNumber, 3)
        XCTAssertEqual(approved.reviewerLogin, "carol")

        let commented = try decode(PrCommentedEvent.self,
            #"{"prNumber":3,"repo":"owner/repo","commenterLogin":"dave","title":"PR","commentBody":"nits","commentUrl":"u"}"#)
        XCTAssertEqual(commented.commenterLogin, "dave")
        XCTAssertEqual(commented.commentBody, "nits")

        let mainUpdated = try decode(MainBranchUpdatedEvent.self,
            #"{"repo":"owner/repo","prNumber":3,"mergedBy":"eve","headSha":"abc123"}"#)
        XCTAssertEqual(mainUpdated.prNumber, 3)
        XCTAssertEqual(mainUpdated.headSha, "abc123")
    }
}

final class ModelLogicTests: XCTestCase {
    func testExtractTicketNumber() {
        XCTAssertEqual(extractTicketNumber(from: "feature/LOY-123-foo"), "LOY-123")
        XCTAssertEqual(extractTicketNumber(from: "fix-ABC-42"), "ABC-42")
        XCTAssertEqual(extractTicketNumber(from: "main"), nil)
        XCTAssertEqual(extractTicketNumber(from: "release/1234"), nil)
        XCTAssertEqual(extractTicketNumber(from: "bug/EZY-9-wip"), "EZY-9")
    }

    func testBranchInfoTicketNumberAndJiraUrl() {
        let ticketBranch = BranchInfo(name: "feature/LOY-123", repoPath: "/", repoName: "r",
                                      isCurrent: false, isLocal: true, isMerged: false, isDefault: false)
        XCTAssertEqual(ticketBranch.ticketNumber, "LOY-123")

        let noTicket = BranchInfo(name: "main", repoPath: "/", repoName: "r",
                                  isCurrent: true, isLocal: true, isMerged: false, isDefault: true)
        XCTAssertNil(noTicket.ticketNumber)
        XCTAssertNil(noTicket.jiraUrl)
    }

    func testShortRepo() {
        XCTAssertEqual(shortRepo("owner/repo"), "repo")
        XCTAssertEqual(shortRepo("repo"), "repo")
        XCTAssertEqual(shortRepo("a/b/c"), "b/c")
    }

    func testWorkflowRunDurationAndIsRunning() {
        let start = Date(timeIntervalSince1970: 1_717_234_000)
        let running = WorkflowRun(id: UUID(), dbId: nil, runId: 1, workflowName: "CI", repo: "r",
                                  actor: "a", headBranch: nil, trigger: nil, prNumber: nil, prTitle: nil,
                                  status: "in_progress", htmlUrl: "", startedAt: start, completedAt: nil,
                                  targetGitHubIds: [])
        XCTAssertTrue(running.isRunning)
        XCTAssertNil(running.duration)

        let done = WorkflowRun(id: UUID(), dbId: nil, runId: 1, workflowName: "CI", repo: "r",
                               actor: "a", headBranch: nil, trigger: nil, prNumber: nil, prTitle: nil,
                               status: "success", htmlUrl: "", startedAt: start,
                               completedAt: start.addingTimeInterval(90), targetGitHubIds: [])
        XCTAssertFalse(done.isRunning)
        XCTAssertEqual(done.duration ?? -1, 90, accuracy: 0.001)
    }

    func testPullRequestHelpers() {
        let withUrl = PullRequest(prNumber: 1, title: "t", repo: "owner/repo", headBranch: "h",
                                  baseBranch: "m", htmlUrl: URL(string: "https://github.com/owner/repo/pull/1"),
                                  status: "open", conclusion: nil, draft: false, mergeableState: nil,
                                  ciStatus: "ready", reviewApproved: false, lastCommentBy: nil,
                                  lastCommentBody: nil, lastCommentAt: nil, lastCommentUrl: nil,
                                  lastReviewFilePath: nil, lastReviewLine: nil, isSubscribed: false,
                                  subscriberIds: [], authorGitHubId: nil)
        XCTAssertEqual(withUrl.id, "owner/repo#1")
        XCTAssertEqual(withUrl.prUrl.absoluteString, "https://github.com/owner/repo/pull/1")
        XCTAssertFalse(withUrl.isMerged)

        let noUrl = PullRequest(prNumber: 2, title: "t", repo: "owner/repo", headBranch: "h",
                                baseBranch: "m", htmlUrl: nil, status: "merged", conclusion: nil,
                                draft: false, mergeableState: nil, ciStatus: "ready", reviewApproved: false,
                                lastCommentBy: nil, lastCommentBody: nil, lastCommentAt: nil,
                                lastCommentUrl: nil, lastReviewFilePath: nil, lastReviewLine: nil,
                                isSubscribed: false, subscriberIds: [], authorGitHubId: nil)
        XCTAssertTrue(noUrl.isMerged)
        XCTAssertEqual(noUrl.prUrl.absoluteString, "https://github.com/owner/repo/pull/2")

        XCTAssertEqual(withUrl, PullRequest(prNumber: 1, title: "different", repo: "owner/repo",
                                            headBranch: "h", baseBranch: "m", htmlUrl: nil,
                                            status: "open", conclusion: nil, draft: false,
                                            mergeableState: nil, ciStatus: "ready", reviewApproved: false,
                                            lastCommentBy: nil, lastCommentBody: nil, lastCommentAt: nil,
                                            lastCommentUrl: nil, lastReviewFilePath: nil,
                                            lastReviewLine: nil, isSubscribed: false,
                                            subscriberIds: [], authorGitHubId: nil))
    }
}

final class SessionExpiryTests: XCTestCase {
    private final class MockSignalRTransport: SignalRClientProtocol {
        func connectAndListen(token: String, username: String, onEvent: @escaping (HubEvent) -> Void) async throws {}
    }

    @MainActor
    func testSessionExpiryLogsOutAndClearsKeychain() async {
        let keychain = MockKeychainService()
        keychain.savedSession = KeychainService.Session(gitHubId: 123, username: "alice", avatarUrl: nil, token: "jwt")
        let api = MockApiClient()
        let service = SignalRService(
            baseUrl: "https://mock.example.com",
            keychain: keychain,
            persistence: MockPersistenceService(),
            oauth: MockOAuthService(),
            api: api,
            signalRClient: MockSignalRTransport()
        )
        await MainActor.run {
            service.isLoggedIn = true
            service.authToken = "jwt"
            api.authToken = "jwt"
        }

        await service.handleSessionExpired()

        await MainActor.run {
            XCTAssertFalse(service.isLoggedIn)
            XCTAssertNil(service.authToken)
            XCTAssertNil(api.authToken)
        }
        XCTAssertNil(keychain.savedSession)
    }

    @MainActor
    func testSessionExpiryIsIdempotent() async {
        let keychain = MockKeychainService()
        keychain.savedSession = KeychainService.Session(gitHubId: 123, username: "alice", avatarUrl: nil, token: "jwt")
        let service = SignalRService(
            baseUrl: "https://mock.example.com",
            keychain: keychain,
            persistence: MockPersistenceService(),
            oauth: MockOAuthService(),
            api: MockApiClient(),
            signalRClient: MockSignalRTransport()
        )
        await MainActor.run { service.isLoggedIn = true }
        await service.handleSessionExpired()
        await service.handleSessionExpired()
        await MainActor.run { XCTAssertFalse(service.isLoggedIn) }
        XCTAssertNil(keychain.savedSession)
    }
}
