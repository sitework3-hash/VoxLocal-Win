# VoxLocal для macOS

VoxLocal — нативное приложение строки меню для локального голосового ввода. Удерживайте **Option + Space**, говорите и отпустите клавиши: распознанный текст будет вставлен в ранее активное приложение.

> Разработка функционального паритета с Windows продолжается в ветке `feature/macos`. Текущая версия уже поддерживает локальное Whisper-распознавание, Metal, глобальную горячую клавишу, оверлей, автоматическую вставку, модели, настройки и локальное улучшение через Ollama.

## Требования

- macOS 14 или новее;
- Apple Silicon (M1–M4 и новее) рекомендуется;
- Xcode Command Line Tools;
- Git;
- около 2 ГБ свободного места для стандартной установки и сборки.

Intel Mac поддерживается существующим кодом, но основная оптимизация и тестирование ориентированы на Apple Silicon.

## Установка из GitHub

Откройте Terminal:

```bash
git clone --branch feature/macos https://github.com/sitework3-hash/VoxLocal-Win.git VoxLocal
cd VoxLocal
chmod +x install-macos.sh
./install-macos.sh
```

Скрипт без Homebrew и без `sudo`:

1. проверит Xcode Command Line Tools;
2. загрузит проектные CMake и whisper.cpp;
3. соберёт whisper.cpp с Metal;
4. запустит тесты macOS;
5. загрузит модель Whisper `small`;
6. соберёт и подпишет локальной подписью `VoxLocal.app`;
7. установит приложение в `~/Applications` и запустит его.

### Выбор модели

```bash
./install-macos.sh --model base
./install-macos.sh --model small
./install-macos.sh --model large-v3-turbo
```

- `base` (~148 МБ) — максимальная скорость и минимальная нагрузка;
- `small` (~488 МБ) — рекомендуемый баланс точности и скорости;
- `large-v3-turbo` (~1,6 ГБ) — максимальное качество для мощного MacBook.

Другие параметры:

```bash
./install-macos.sh --no-launch
./install-macos.sh --skip-model
./install-macos.sh --install-dir /Applications  # может потребоваться sudo/пароль
```

## Разрешения macOS

При первом использовании нужно предоставить:

1. **Microphone** — для записи голоса;
2. **Accessibility** — для вставки текста в другие приложения.

Разрешения находятся в **System Settings → Privacy & Security**. macOS не позволяет установщику выдать их автоматически.

Локально подписанная сборка может потребовать первый запуск через правый щелчок → **Open**. Для публичного релиза без такого предупреждения потребуется Apple Developer ID и нотариализация Apple.

## Использование

- удерживать **Option + Space** — запись;
- отпустить клавиши — распознать и вставить;
- **Esc** — отменить текущую сессию;
- значок микрофона в строке меню — настройки, модели и завершение программы.

Аудио обрабатывается локально и удаляется после завершения или отмены. Журналы не содержат аудио, расшифровок или содержимого буфера обмена.

## Обновление

```bash
cd VoxLocal
git pull
./install-macos.sh
```

## Разработка

```bash
./scripts/bootstrap.sh
./scripts/test.sh
./scripts/download_model.sh small
./scripts/build_app.sh
./scripts/run.sh
```

Собранное приложение находится в `dist/VoxLocal.app`.

## Текущее различие с Windows

Windows-версия остаётся стабильной и развивается независимо. Для полного паритета в macOS ещё переносятся:

- предварительный распознанный текст в оверлее;
- Polza.ai/OpenAI-совместимое улучшение текста;
- хранение API-ключа в macOS Keychain;
- профили Gemini и DeepSeek;
- автоматические GitHub Releases (`.dmg`).
