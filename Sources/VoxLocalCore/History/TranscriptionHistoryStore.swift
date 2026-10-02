import Combine
import Foundation

public struct TranscriptionHistoryEntry: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let createdAt: Date
    public let text: String

    public init(id: UUID = UUID(), createdAt: Date = Date(), text: String) {
        self.id = id
        self.createdAt = createdAt
        self.text = text
    }
}

/// Keeps a small, local-only history of successful dictations. The history is
/// intentionally separate from the application log so normal diagnostics
/// never contain recognized text.
@MainActor
public final class TranscriptionHistoryStore: ObservableObject {
    public static let capacity = 5

    @Published public private(set) var entries: [TranscriptionHistoryEntry]

    public let fileURL: URL
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    public init(fileURL: URL? = nil) {
        self.fileURL = fileURL ?? FileManager.default
            .homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/VoxLocal/history.json")

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        self.encoder = encoder

        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        self.decoder = decoder

        self.entries = []
        self.entries = load()
    }

    public func add(_ text: String, createdAt: Date = Date()) {
        let normalized = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty else { return }

        entries.insert(TranscriptionHistoryEntry(createdAt: createdAt, text: normalized), at: 0)
        if entries.count > Self.capacity {
            entries.removeLast(entries.count - Self.capacity)
        }
        save()
    }

    public func clear() {
        entries = []
        do {
            if FileManager.default.fileExists(atPath: fileURL.path) {
                try FileManager.default.removeItem(at: fileURL)
            }
        } catch {
            Log.shared.error("history clear failed: \(error.localizedDescription)")
        }
    }

    private func load() -> [TranscriptionHistoryEntry] {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return [] }
        do {
            let data = try Data(contentsOf: fileURL)
            return Array(try decoder.decode([TranscriptionHistoryEntry].self, from: data).prefix(Self.capacity))
        } catch {
            Log.shared.error("history load failed: \(error.localizedDescription)")
            return []
        }
    }

    private func save() {
        do {
            try FileManager.default.createDirectory(
                at: fileURL.deletingLastPathComponent(),
                withIntermediateDirectories: true)
            try encoder.encode(entries).write(to: fileURL, options: .atomic)
        } catch {
            Log.shared.error("history save failed: \(error.localizedDescription)")
        }
    }
}
