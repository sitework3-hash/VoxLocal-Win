import AVFoundation
import Foundation
import Speech

/// Provides a best-effort live preview through Apple's on-device speech
/// recognizer while Whisper remains the authoritative final transcription.
/// `requiresOnDeviceRecognition` is always enabled, so preview audio is never
/// sent to Apple servers. Failure or unavailability has no effect on dictation.
public final class LiveSpeechPreview: @unchecked Sendable {
    private let lock = NSLock()
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var task: SFSpeechRecognitionTask?

    public init() {}

    public var authorizationStatus: SFSpeechRecognizerAuthorizationStatus {
        SFSpeechRecognizer.authorizationStatus()
    }

    public func requestAuthorization() async -> Bool {
        if authorizationStatus == .authorized { return true }
        guard authorizationStatus == .notDetermined else { return false }
        return await withCheckedContinuation { continuation in
            SFSpeechRecognizer.requestAuthorization { status in
                continuation.resume(returning: status == .authorized)
            }
        }
    }

    /// Starts a partial-result session. Returns false when on-device speech
    /// recognition is unavailable for the selected language or permission was
    /// declined. The callback is always delivered on the main queue.
    public func start(language: String, onText: @escaping @MainActor @Sendable (String) -> Void) async -> Bool {
        guard await requestAuthorization() else { return false }
        stop(cancel: true)

        let localeIdentifier: String
        switch language {
        case "ru": localeIdentifier = "ru-RU"
        case "en": localeIdentifier = "en-US"
        default: localeIdentifier = Locale.current.identifier
        }
        guard let recognizer = SFSpeechRecognizer(locale: Locale(identifier: localeIdentifier)),
              recognizer.isAvailable,
              recognizer.supportsOnDeviceRecognition else {
            return false
        }

        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = true
        request.requiresOnDeviceRecognition = true
        request.taskHint = .dictation
        let task = recognizer.recognitionTask(with: request) { result, error in
            if let result {
                let text = result.bestTranscription.formattedString
                Task { @MainActor in onText(text) }
            }
            if error != nil || result?.isFinal == true {
                // The final result is intentionally ignored; Whisper performs
                // the authoritative transcription after recording stops.
            }
        }

        lock.lock()
        self.request = request
        self.task = task
        lock.unlock()
        return true
    }

    /// Called directly from AVAudioEngine's tap. Speech.framework accepts the
    /// native microphone format and copies the buffer into its request stream.
    public func append(_ buffer: AVAudioPCMBuffer) {
        lock.lock()
        let request = self.request
        lock.unlock()
        request?.append(buffer)
    }

    public func stop(cancel: Bool) {
        lock.lock()
        let request = self.request
        let task = self.task
        self.request = nil
        self.task = nil
        lock.unlock()

        if cancel {
            task?.cancel()
        } else {
            request?.endAudio()
            task?.finish()
        }
    }
}
