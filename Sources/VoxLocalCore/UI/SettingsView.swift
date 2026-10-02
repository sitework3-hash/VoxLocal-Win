import AppKit
import ServiceManagement
import SwiftUI

/// Container passed into settings/onboarding so views can reach services.
@MainActor
public final class SettingsDependencies: ObservableObject {
    public let settings: SettingsStore
    public let modelManager: ModelManager
    public let permissions: PermissionsChecking
    public let dictation: DictationController
    public let history: TranscriptionHistoryStore
    /// Re-registers the global hotkey; returns a localized error, nil on success.
    public let applyHotkey: (KeyCombo) -> String?

    public init(
        settings: SettingsStore,
        modelManager: ModelManager,
        permissions: PermissionsChecking,
        dictation: DictationController,
        history: TranscriptionHistoryStore,
        applyHotkey: @escaping (KeyCombo) -> String?
    ) {
        self.settings = settings
        self.modelManager = modelManager
        self.permissions = permissions
        self.dictation = dictation
        self.history = history
        self.applyHotkey = applyHotkey
    }
}

public struct SettingsView: View {
    @ObservedObject var settings: SettingsStore
    let deps: SettingsDependencies

    public init(deps: SettingsDependencies) {
        self.deps = deps
        self.settings = deps.settings
    }

    public var body: some View {
        TabView {
            GeneralSettingsTab(settings: settings, deps: deps)
                .tabItem { Label(L10n.t("settings.tab.general"), systemImage: "gearshape") }
            TranscriptionSettingsTab(settings: settings, modelManager: deps.modelManager)
                .tabItem { Label(L10n.t("settings.tab.transcription"), systemImage: "waveform") }
            RefinementSettingsTab(settings: settings)
                .tabItem { Label(L10n.t("settings.tab.refinement"), systemImage: "wand.and.stars") }
            HistorySettingsTab(history: deps.history)
                .tabItem { Label(L10n.t("settings.tab.history"), systemImage: "clock.arrow.circlepath") }
            PrivacySettingsTab(settings: settings)
                .tabItem { Label(L10n.t("settings.tab.privacy"), systemImage: "lock.shield") }
        }
        .frame(width: 560, height: 520)
    }
}

// MARK: - General

struct GeneralSettingsTab: View {
    @ObservedObject var settings: SettingsStore
    let deps: SettingsDependencies
    @State private var hotkeyError: String?
    @State private var loginItemError: String?
    @State private var devices: [AudioDeviceFinder.Device] = []

    var body: some View {
        Form {
            Section(L10n.t("settings.hotkey.section")) {
                HStack {
                    Text(L10n.t("settings.hotkey"))
                    Spacer()
                    HotkeyRecorderView(
                        current: KeyCombo(keyCode: settings.hotkeyKeyCode, modifiers: settings.hotkeyModifiers)
                    ) { combo in
                        if let error = deps.applyHotkey(combo) {
                            hotkeyError = error
                        } else {
                            hotkeyError = nil
                            settings.hotkeyKeyCode = combo.keyCode
                            settings.hotkeyModifiers = combo.modifiers
                        }
                    }
                }
                if let hotkeyError {
                    Text(hotkeyError)
                        .font(.callout)
                        .foregroundStyle(.red)
                }
                Picker(L10n.t("settings.hotkey.mode"), selection: $settings.hotkeyMode) {
                    Text(L10n.t("settings.hotkey.mode.hold")).tag(HotkeyMode.pressAndHold)
                    Text(L10n.t("settings.hotkey.mode.toggle")).tag(HotkeyMode.toggle)
                }
                .pickerStyle(.radioGroup)
            }

            Section(L10n.t("settings.audio.section")) {
                Picker(L10n.t("settings.mic"), selection: Binding(
                    get: { settings.inputDeviceUID ?? "" },
                    set: { settings.inputDeviceUID = $0.isEmpty ? nil : $0 })
                ) {
                    Text(L10n.t("settings.mic.default")).tag("")
                    ForEach(devices) { device in
                        Text(device.name).tag(device.uid)
                    }
                }
            }

            Section(L10n.t("settings.insertion.section")) {
                Picker(L10n.t("settings.insertion"), selection: $settings.insertionMode) {
                    Text(L10n.t("settings.insertion.auto")).tag(InsertionMode.automatic)
                    Text(L10n.t("settings.insertion.clipboard")).tag(InsertionMode.clipboardOnly)
                }
                .pickerStyle(.radioGroup)
            }

            Section(L10n.t("settings.app.section")) {
                Toggle(L10n.t("settings.launchAtLogin"), isOn: Binding(
                    get: { settings.launchAtLogin },
                    set: { newValue in
                        settings.launchAtLogin = newValue
                        loginItemError = LoginItemService.setEnabled(newValue)
                    }))
                if let loginItemError {
                    Text(loginItemError)
                        .font(.callout)
                        .foregroundStyle(.orange)
                }
                Picker(L10n.t("settings.language.ui"), selection: $settings.interfaceLanguage) {
                    Text(L10n.t("settings.language.ui.ru")).tag(L10n.Language.russian)
                    Text(L10n.t("settings.language.ui.en")).tag(L10n.Language.english)
                    Text(L10n.t("settings.language.ui.system")).tag(L10n.Language.system)
                }
            }
        }
        .formStyle(.grouped)
        .onAppear { devices = AudioDeviceFinder.inputDevices() }
    }
}

/// Captures the next key press (with modifiers) via a local event monitor.
struct HotkeyRecorderView: View {
    let current: KeyCombo
    let onCapture: (KeyCombo) -> Void
    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        Button(recording ? L10n.t("settings.hotkey.press") : current.displayString) {
            recording ? stopRecording() : startRecording()
        }
        .accessibilityLabel(L10n.t("settings.hotkey"))
        .onDisappear { stopRecording() }
    }

    private func startRecording() {
        recording = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            defer { stopRecording() }
            if event.keyCode == 53 { // Esc cancels recording
                return nil
            }
            let combo = KeyCombo.fromNSEvent(keyCode: event.keyCode, flags: event.modifierFlags)
            onCapture(combo)
            return nil
        }
    }

    private func stopRecording() {
        recording = false
        if let monitor {
            NSEvent.removeMonitor(monitor)
            self.monitor = nil
        }
    }
}

enum LoginItemService {
    /// Returns a localized error string, or nil on success.
    static func setEnabled(_ enabled: Bool) -> String? {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            return nil
        } catch {
            return L10n.t("settings.launchAtLogin.error", error.localizedDescription)
        }
    }
}

// MARK: - Transcription

struct TranscriptionSettingsTab: View {
    @ObservedObject var settings: SettingsStore
    @ObservedObject var modelManager: ModelManager
    @State private var confirmDownload: WhisperModelInfo?
    @State private var downloadError: String?

    var body: some View {
        Form {
            Section(L10n.t("settings.model.installed")) {
                if modelManager.installedModels.isEmpty {
                    Text(L10n.t("settings.model.none"))
                        .foregroundStyle(.secondary)
                }
                Picker(L10n.t("settings.model"), selection: $settings.whisperModel) {
                    ForEach(modelManager.installedModels) { model in
                        Text("\(model.name) (\(ByteCountFormatter.string(fromByteCount: model.sizeBytes, countStyle: .file)))")
                            .tag(model.name)
                    }
                    if !modelManager.installedModels.contains(where: { $0.name == settings.whisperModel }) {
                        Text(L10n.t("settings.model.notInstalled", settings.whisperModel))
                            .tag(settings.whisperModel)
                    }
                }
            }

            Section(L10n.t("settings.model.download")) {
                ForEach(WhisperModelCatalog.models) { info in
                    HStack {
                        Text(info.name)
                        if !info.multilingual {
                            Text(L10n.t("settings.model.englishOnly"))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                        Text(info.sizeLabel)
                            .foregroundStyle(.secondary)
                        if modelManager.installedModels.contains(where: { $0.name == info.name }) {
                            Image(systemName: "checkmark.circle.fill")
                                .foregroundStyle(.green)
                                .accessibilityLabel(L10n.t("settings.model.installedAx"))
                        } else if modelManager.downloadingModel == info.name {
                            ProgressView(value: modelManager.downloadProgress ?? 0)
                                .frame(width: 90)
                            Button(L10n.t("common.cancel")) {
                                modelManager.cancelDownload()
                            }
                        } else {
                            Button(L10n.t("settings.model.get")) {
                                confirmDownload = info
                            }
                            .disabled(modelManager.isDownloading)
                        }
                    }
                }
                if let downloadError {
                    Text(downloadError)
                        .font(.callout)
                        .foregroundStyle(.red)
                }
            }

            Section(L10n.t("settings.recognition.section")) {
                Picker(L10n.t("settings.spoken"), selection: $settings.spokenLanguage) {
                    Text(L10n.t("settings.spoken.auto")).tag(SpokenLanguage.auto)
                    Text(L10n.t("settings.spoken.ru")).tag(SpokenLanguage.russian)
                    Text(L10n.t("settings.spoken.en")).tag(SpokenLanguage.english)
                }
                Stepper(value: $settings.whisperThreads, in: 1...16) {
                    Text(L10n.t("settings.threads", settings.whisperThreads))
                }
                Toggle(L10n.t("settings.artifacts"), isOn: $settings.removeArtifacts)
            }
        }
        .formStyle(.grouped)
        .onAppear { modelManager.refreshInstalledModels() }
        // Explicit size confirmation before any model download.
        .confirmationDialog(
            L10n.t("settings.model.confirm.title"),
            isPresented: Binding(get: { confirmDownload != nil }, set: { if !$0 { confirmDownload = nil } })
        ) {
            if let info = confirmDownload {
                Button(L10n.t("settings.model.confirm.download", info.sizeLabel)) {
                    start(info)
                }
                Button(L10n.t("common.cancel"), role: .cancel) {}
            }
        } message: {
            if let info = confirmDownload {
                Text(L10n.t("settings.model.confirm.message", info.name, info.sizeLabel))
            }
        }
    }

    private func start(_ info: WhisperModelInfo) {
        confirmDownload = nil
        downloadError = nil
        Task {
            do {
                _ = try await modelManager.download(info)
            } catch is CancellationError {
                // user cancelled — nothing to report
            } catch let error as URLError where error.code == .cancelled {
                // user cancelled — nothing to report
            } catch {
                downloadError = L10n.t("settings.model.download.error", error.localizedDescription)
            }
        }
    }
}

// MARK: - Refinement

struct RefinementSettingsTab: View {
    @ObservedObject var settings: SettingsStore
    @State private var availability: String?
    @State private var ollamaModels: [String] = []
    @State private var apiKey = ""
    @State private var hasSavedAPIKey = false
    private let secretStore: SecretStoring = KeychainSecretStore.shared

    var body: some View {
        Form {
            Section {
                Toggle(L10n.t("settings.refine.enabled"), isOn: $settings.refinementEnabled)
                Text(L10n.t("settings.refine.hint"))
                    .font(.callout)
                    .foregroundStyle(.secondary)
                Picker(L10n.t("settings.refine.provider"), selection: $settings.refinementProvider) {
                    Text(L10n.t("settings.refine.provider.cloud")).tag(RefinementProviderKind.openAICompatible)
                    Text(L10n.t("settings.refine.provider.ollama")).tag(RefinementProviderKind.ollama)
                }
            }

            if settings.refinementProvider == .openAICompatible {
                Section(L10n.t("settings.refine.cloud.section")) {
                    TextField(L10n.t("settings.refine.baseURL"), text: $settings.openAIBaseURL)
                        .textFieldStyle(.roundedBorder)
                    Picker(L10n.t("settings.refine.profile"), selection: $settings.openAIModelProfile) {
                        Text("Gemini 2.5 Flash").tag(OpenAIModelProfile.geminiFlash)
                        Text("DeepSeek Chat").tag(OpenAIModelProfile.deepSeekChat)
                        Text(L10n.t("settings.refine.profile.custom")).tag(OpenAIModelProfile.custom)
                    }
                    .onChange(of: settings.openAIModelProfile) { profile in
                        if let modelID = profile.modelID { settings.openAIModel = modelID }
                    }
                    TextField(L10n.t("settings.refine.model"), text: $settings.openAIModel)
                        .textFieldStyle(.roundedBorder)
                        .disabled(settings.openAIModelProfile != .custom)
                    SecureField(
                        hasSavedAPIKey ? L10n.t("settings.refine.key.saved") : L10n.t("settings.refine.key"),
                        text: $apiKey)
                        .textFieldStyle(.roundedBorder)
                    HStack {
                        Button(L10n.t("settings.refine.key.save")) { saveAPIKey() }
                            .disabled(apiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        Button(L10n.t("settings.refine.key.delete"), role: .destructive) { deleteAPIKey() }
                            .disabled(!hasSavedAPIKey)
                        Text(L10n.t("settings.refine.key.keychain"))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                .disabled(!settings.refinementEnabled)
            } else {
                Section(L10n.t("settings.refine.ollama.section")) {
                    TextField(L10n.t("settings.refine.endpoint"), text: $settings.ollamaEndpoint)
                        .textFieldStyle(.roundedBorder)
                    HStack {
                        TextField(L10n.t("settings.refine.model"), text: $settings.ollamaModel)
                            .textFieldStyle(.roundedBorder)
                        if !ollamaModels.isEmpty {
                            Picker("", selection: $settings.ollamaModel) {
                                ForEach(ollamaModels, id: \.self) { Text($0).tag($0) }
                            }
                            .labelsHidden()
                            .frame(width: 30)
                        }
                    }
                }
                .disabled(!settings.refinementEnabled)
            }

            Section {
                HStack {
                    Button(L10n.t("settings.refine.check")) { check() }
                    if let availability {
                        Text(availability)
                            .font(.callout)
                            .foregroundStyle(.secondary)
                    }
                }
                HStack {
                    Text(L10n.t("settings.refine.timeout"))
                    Slider(value: $settings.refinementTimeout, in: 5...60, step: 1)
                    Text("\(Int(settings.refinementTimeout)) s")
                        .monospacedDigit()
                }
            }
            .disabled(!settings.refinementEnabled)

            Section(L10n.t("settings.refine.preset.section")) {
                Picker(L10n.t("settings.refine.preset"), selection: $settings.refinementPreset) {
                    ForEach(RefinementPreset.allCases, id: \.self) { preset in
                        Text(L10n.t(preset.titleKey)).tag(preset)
                    }
                }
                if settings.refinementPreset == .custom {
                    TextEditor(text: $settings.customInstruction)
                        .frame(height: 70)
                        .font(.system(size: 12))
                        .accessibilityLabel(L10n.t("settings.refine.custom"))
                }
            }
            .disabled(!settings.refinementEnabled)
        }
        .formStyle(.grouped)
        .task { hasSavedAPIKey = (try? secretStore.read()) != nil }
    }

    private func saveAPIKey() {
        do {
            try secretStore.save(apiKey)
            apiKey = ""
            hasSavedAPIKey = true
            availability = L10n.t("settings.refine.key.saved.status")
        } catch {
            availability = L10n.t("settings.refine.key.error")
        }
    }

    private func deleteAPIKey() {
        do {
            try secretStore.delete()
            apiKey = ""
            hasSavedAPIKey = false
            availability = L10n.t("settings.refine.key.deleted")
        } catch {
            availability = L10n.t("settings.refine.key.error")
        }
    }

    private func check() {
        availability = L10n.t("settings.refine.status.checking")
        Task {
            do {
                let provider: TextRefinementProvider
                if settings.refinementProvider == .openAICompatible {
                    guard let key = try secretStore.read(), !key.isEmpty else {
                        availability = L10n.t("settings.refine.key.required")
                        return
                    }
                    provider = try OpenAICompatibleRefinementProvider(
                        baseURL: settings.openAIBaseURL,
                        model: settings.openAIModel,
                        apiKey: key)
                } else {
                    let ollama = try OllamaRefinementProvider(
                        endpoint: settings.ollamaEndpoint,
                        model: settings.ollamaModel)
                    let names = try await ollama.installedModels()
                    ollamaModels = names
                    if settings.ollamaModel.isEmpty {
                        availability = L10n.t("settings.refine.status.pickModel", names.joined(separator: ", "))
                        return
                    }
                    provider = ollama
                }
                switch await provider.checkAvailability() {
                case .available:
                    availability = L10n.t("settings.refine.status.ok")
                case .modelMissing(let available):
                    availability = L10n.t("settings.refine.status.nomodel", available.joined(separator: ", "))
                case .serverUnreachable:
                    availability = L10n.t("settings.refine.status.failed")
                }
            } catch RefinementError.nonLocalEndpoint {
                availability = L10n.t("settings.refine.nonlocal")
            } catch {
                availability = L10n.t("settings.refine.status.failed")
            }
        }
    }
}

// MARK: - History

struct HistorySettingsTab: View {
    @ObservedObject var history: TranscriptionHistoryStore

    var body: some View {
        VStack(spacing: 0) {
            if history.entries.isEmpty {
                ContentUnavailableView(
                    L10n.t("history.empty.title"),
                    systemImage: "text.bubble",
                    description: Text(L10n.t("history.empty.message")))
            } else {
                List(history.entries) { entry in
                    VStack(alignment: .leading, spacing: 7) {
                        HStack {
                            Text(entry.createdAt, format: .dateTime.day().month().hour().minute())
                                .font(.caption)
                                .foregroundStyle(.secondary)
                            Spacer()
                            Button(L10n.t("history.copy")) {
                                NSPasteboard.general.clearContents()
                                NSPasteboard.general.setString(entry.text, forType: .string)
                            }
                            .buttonStyle(.borderless)
                        }
                        Text(entry.text)
                            .lineLimit(4)
                            .textSelection(.enabled)
                    }
                    .padding(.vertical, 5)
                }
            }

            Divider()
            HStack {
                Text(L10n.t("history.privacy"))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button(L10n.t("history.clear"), role: .destructive) {
                    history.clear()
                }
                .disabled(history.entries.isEmpty)
            }
            .padding()
        }
    }
}

// MARK: - Privacy

struct PrivacySettingsTab: View {
    @ObservedObject var settings: SettingsStore

    var body: some View {
        Form {
            Section(L10n.t("privacy.title")) {
                ForEach(1...5, id: \.self) { index in
                    Label(L10n.t("privacy.p\(index)"), systemImage: "checkmark.shield")
                        .font(.callout)
                }
            }
            Section(L10n.t("settings.diagnostics.section")) {
                Picker(L10n.t("settings.loglevel"), selection: $settings.logLevel) {
                    Text(L10n.t("settings.loglevel.off")).tag(LogLevel.off)
                    Text(L10n.t("settings.loglevel.error")).tag(LogLevel.error)
                    Text(L10n.t("settings.loglevel.info")).tag(LogLevel.info)
                    Text(L10n.t("settings.loglevel.debug")).tag(LogLevel.debug)
                }
                Text(L10n.t("privacy.logs"))
                    .font(.callout)
                    .foregroundStyle(.secondary)
                Button(L10n.t("menu.logs")) {
                    NSWorkspace.shared.open(Log.shared.directory)
                }
            }
            Section {
                Button(L10n.t("settings.resetOnboarding")) {
                    settings.resetOnboarding()
                }
            }
        }
        .formStyle(.grouped)
    }
}
