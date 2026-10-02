import Foundation

public enum OpenAICompatibleError: Error, Equatable {
    case invalidConfiguration
    case authentication
    case insufficientBalance
    case modelOrEndpointNotFound
    case rateLimited
    case timeout
    case serverUnavailable
    case invalidResponse
    case requestRejected(Int)
}

/// OpenAI-compatible chat/completions provider used for optional Polza.ai or
/// custom cloud refinement. Redirects are refused so the API key and transcript
/// are sent only to the endpoint explicitly configured by the user.
public final class OpenAICompatibleRefinementProvider: TextRefinementProvider, @unchecked Sendable {
    public let endpoint: URL
    public let model: String
    private let apiKey: String
    private let session: URLSession

    private final class NoRedirectDelegate: NSObject, URLSessionTaskDelegate {
        func urlSession(_ session: URLSession, task: URLSessionTask,
                        willPerformHTTPRedirection response: HTTPURLResponse,
                        newRequest request: URLRequest,
                        completionHandler: @escaping @Sendable (URLRequest?) -> Void) {
            completionHandler(nil)
        }
    }

    public init(baseURL: String, model: String, apiKey: String, session: URLSession? = nil) throws {
        let normalizedModel = model.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedKey = apiKey.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedModel.isEmpty, !normalizedKey.isEmpty else {
            throw OpenAICompatibleError.invalidConfiguration
        }
        self.endpoint = try Self.chatCompletionsURL(baseURL: baseURL)
        self.model = normalizedModel
        self.apiKey = normalizedKey
        if let session {
            self.session = session
        } else {
            let configuration = URLSessionConfiguration.ephemeral
            configuration.timeoutIntervalForRequest = 60
            configuration.timeoutIntervalForResource = 120
            self.session = URLSession(
                configuration: configuration,
                delegate: NoRedirectDelegate(),
                delegateQueue: nil)
        }
    }

    public static func chatCompletionsURL(baseURL: String) throws -> URL {
        let trimmed = baseURL.trimmingCharacters(in: .whitespacesAndNewlines)
        guard var components = URLComponents(string: trimmed),
              let scheme = components.scheme?.lowercased(),
              scheme == "https" || scheme == "http",
              components.host != nil,
              components.query == nil,
              components.fragment == nil else {
            throw OpenAICompatibleError.invalidConfiguration
        }
        var path = components.path
        while path.count > 1 && path.hasSuffix("/") { path.removeLast() }
        if !path.lowercased().hasSuffix("/chat/completions") {
            path += "/chat/completions"
        }
        components.path = path
        guard let url = components.url else { throw OpenAICompatibleError.invalidConfiguration }
        return url
    }

    struct ChatRequest: Encodable {
        struct Message: Encodable {
            let role: String
            let content: String
        }
        let model: String
        let messages: [Message]
        let temperature: Double
        let stream: Bool
    }

    private struct ChatResponse: Decodable {
        struct Choice: Decodable {
            struct Message: Decodable { let content: String? }
            let message: Message?
        }
        let choices: [Choice]?
    }

    public static func requestBody(model: String, transcript: String, context: RefinementContext) throws -> Data {
        try JSONEncoder().encode(ChatRequest(
            model: model,
            messages: [
                .init(role: "system", content: RefinementPromptBuilder.systemPrompt(for: context)),
                .init(role: "user", content: RefinementPromptBuilder.userPrompt(transcript: transcript)),
            ],
            temperature: 0.2,
            stream: false))
    }

    public func refine(_ transcript: String, context: RefinementContext) async throws -> String {
        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.httpBody = try Self.requestBody(model: model, transcript: transcript, context: context)
        request.timeoutInterval = context.timeout

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch let error as URLError where error.code == .timedOut {
            throw OpenAICompatibleError.timeout
        } catch let error as URLError where error.code == .cancelled {
            throw CancellationError()
        } catch {
            throw OpenAICompatibleError.serverUnavailable
        }

        guard let http = response as? HTTPURLResponse else {
            throw OpenAICompatibleError.invalidResponse
        }
        guard http.statusCode == 200 else { throw Self.error(for: http.statusCode) }
        guard let decoded = try? JSONDecoder().decode(ChatResponse.self, from: data),
              let content = decoded.choices?.first?.message?.content,
              !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw OpenAICompatibleError.invalidResponse
        }
        return content
    }

    public func checkAvailability() async -> RefinementAvailability {
        let context = RefinementContext(language: "ru", preset: .preserveSpokenWording, timeout: 8)
        do {
            _ = try await refine("Проверка подключения.", context: context)
            return .available
        } catch {
            return .serverUnreachable(String(describing: error))
        }
    }

    public static func error(for statusCode: Int) -> OpenAICompatibleError {
        switch statusCode {
        case 401, 403: .authentication
        case 402: .insufficientBalance
        case 404: .modelOrEndpointNotFound
        case 408, 504: .timeout
        case 429: .rateLimited
        case 500...599: .serverUnavailable
        default: .requestRejected(statusCode)
        }
    }
}
