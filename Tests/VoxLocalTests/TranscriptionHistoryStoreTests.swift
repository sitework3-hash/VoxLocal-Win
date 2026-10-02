import XCTest
@testable import VoxLocalCore

@MainActor
final class TranscriptionHistoryStoreTests: XCTestCase {
    private var directory: URL!
    private var fileURL: URL!

    override func setUp() {
        super.setUp()
        directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("voxlocal-history-\(UUID().uuidString)", isDirectory: true)
        fileURL = directory.appendingPathComponent("history.json")
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    func testKeepsFiveNewestEntriesAndPersistsThem() {
        let store = TranscriptionHistoryStore(fileURL: fileURL)
        for index in 1...7 {
            store.add("Текст \(index)", createdAt: Date(timeIntervalSince1970: TimeInterval(index)))
        }

        XCTAssertEqual(store.entries.map(\.text), ["Текст 7", "Текст 6", "Текст 5", "Текст 4", "Текст 3"])

        let reloaded = TranscriptionHistoryStore(fileURL: fileURL)
        XCTAssertEqual(reloaded.entries.map(\.text), store.entries.map(\.text))
        XCTAssertEqual(reloaded.entries.map(\.createdAt), store.entries.map(\.createdAt))
    }

    func testIgnoresEmptyTextAndTrimsWhitespace() {
        let store = TranscriptionHistoryStore(fileURL: fileURL)
        store.add("   \n")
        store.add("  готовый текст  \n")

        XCTAssertEqual(store.entries.map(\.text), ["готовый текст"])
    }

    func testClearRemovesMemoryAndFile() {
        let store = TranscriptionHistoryStore(fileURL: fileURL)
        store.add("Текст")
        XCTAssertTrue(FileManager.default.fileExists(atPath: fileURL.path))

        store.clear()

        XCTAssertTrue(store.entries.isEmpty)
        XCTAssertFalse(FileManager.default.fileExists(atPath: fileURL.path))
    }

    func testCorruptedFileFallsBackToEmptyHistory() throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try Data("not-json".utf8).write(to: fileURL)

        let store = TranscriptionHistoryStore(fileURL: fileURL)

        XCTAssertTrue(store.entries.isEmpty)
    }
}
