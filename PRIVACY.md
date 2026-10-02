# VoxLocal Privacy Policy / Политика конфиденциальности

## English

VoxLocal is designed so that your speech never leaves your Mac.

- **Audio.** Recording goes to a temporary 16 kHz WAV file in the system temporary directory. It exists only for the duration of one dictation and is deleted immediately after successful insertion, cancellation, or any error. There is no recording history and no opt-in to keep recordings in this version.
- **Transcription.** Performed entirely on-device by a bundled `whisper.cpp` binary. No transcription service, no API key, no account.
- **Live preview (optional system feature).** During recording, macOS Speech Recognition may provide a partial preview. VoxLocal requests on-device recognition only (`requiresOnDeviceRecognition`); the preview is not authoritative, is never stored in history and is never logged. If unavailable or denied, dictation continues with Whisper.
- **Text refinement (optional).** The default cloud option is an OpenAI-compatible provider such as Polza.ai. If enabled, only the final transcript text is sent; audio is never sent. The API key is stored in macOS Keychain and never in settings files or logs. Local Ollama remains available and is restricted to loopback addresses (`127.0.0.1`, `localhost`, `::1`). Any provider error, timeout or implausible response falls back to the raw transcript.
- **Network.** The app may download a Whisper model from the official `ggerganov/whisper.cpp` Hugging Face repository only when you explicitly start a download. It may also send final transcript text to the explicitly configured cloud provider only when refinement is enabled. Nothing is sent by default.
- **Clipboard.** Used only as an insertion mechanism. When simulated paste is used, the previous clipboard contents are restored automatically unless another app changed the clipboard in the meantime. Clipboard contents are never logged.
- **Analytics / telemetry.** None. No trackers, no crash reporters, no advertising SDKs, no unique identifiers.
- **Logs.** A local, size-bounded log (`~/Library/Logs/VoxLocal/`) records events and errors. It never contains raw audio, dictated text, or clipboard contents; home-directory paths are shortened. You can set the log level to *Off*.
- **History.** The five latest successful transcripts are stored locally in `~/Library/Application Support/VoxLocal/history.json` so the user can review or copy them. This file is private user content and is not a log.
- **Settings.** Stored locally in macOS `UserDefaults`; cloud API keys are stored separately in macOS Keychain.
- **Permissions.** Microphone, Speech Recognition (for optional local preview) and Accessibility (for inserting text) are requested through standard macOS mechanisms and can be revoked at any time in System Settings; the app degrades gracefully without Accessibility.

## Русский

VoxLocal устроен так, чтобы ваша речь никогда не покидала ваш Mac.

- **Аудио.** Запись идёт во временный WAV-файл (16 кГц) в системной временной папке. Он существует только на время одной диктовки и удаляется сразу после вставки, отмены или ошибки. Истории записей нет; в этой версии нет даже настройки, позволяющей их хранить.
- **Распознавание.** Выполняется целиком на устройстве встроенным `whisper.cpp`. Без сервисов распознавания, ключей API и аккаунтов.
- **Предварительный текст (необязательно).** Во время записи macOS Speech Recognition может показывать частичный результат. VoxLocal запрашивает только локальное распознавание (`requiresOnDeviceRecognition`); предварительный текст не является итоговым, не сохраняется в историю и не пишется в журнал. При недоступности функции диктовка продолжается через Whisper.
- **Улучшение текста (опционально).** По умолчанию облачный вариант использует OpenAI-compatible провайдера, например Polza.ai. При включении отправляется только готовый текст, аудио не отправляется. API-ключ хранится в macOS Keychain и не попадает в настройки или журналы. Локальный Ollama также доступен, но только через loopback (`127.0.0.1`, `localhost`, `::1`). При любой ошибке, тайм-ауте или неправдоподобном ответе используется исходная расшифровка.
- **Сеть.** Приложение загружает модель Whisper из официального репозитория `ggerganov/whisper.cpp` на Hugging Face только по явной команде пользователя. Текст может отправляться указанному облачному провайдеру только при явно включённом улучшении. По умолчанию ничего не отправляется.
- **Буфер обмена.** Используется только как механизм вставки. При имитации ⌘V прежнее содержимое буфера автоматически восстанавливается, если его тем временем не изменило другое приложение. Содержимое буфера никогда не пишется в журнал.
- **Аналитика / телеметрия.** Отсутствуют. Ни трекеров, ни crash-репортеров, ни рекламных SDK, ни уникальных идентификаторов.
- **Журналы.** Локальный журнал ограниченного размера (`~/Library/Logs/VoxLocal/`) фиксирует события и ошибки. В нём нет аудио, текста диктовок и содержимого буфера; пути внутри домашней папки сокращаются. Уровень журнала можно выключить совсем.
- **История.** Пять последних успешных расшифровок хранятся локально в `~/Library/Application Support/VoxLocal/history.json`, чтобы их можно было просмотреть или скопировать. Это приватные данные пользователя, а не журнал.
- **Настройки.** Хранятся локально в `UserDefaults` macOS; облачные API-ключи хранятся отдельно в macOS Keychain.
- **Разрешения.** Микрофон, Speech Recognition (для необязательного локального предпросмотра) и Универсальный доступ (вставка текста) запрашиваются стандартными средствами macOS и могут быть отозваны в Настройках системы; без Универсального доступа остаётся режим «только буфер обмена».
