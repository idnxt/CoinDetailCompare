# CoinDetailCompare

Приложение для каталогизации монет: фото монеты с веб-камеры, поиск
похожих эталонов по эмбеддингам, обнаружение аномалий (царапины, потёртости,
дефэкты) с тепловой картой, сохранение результатов и аннотационных слоёв.

## Архитектура

Два процесса на одной Windows-машине:

| Процесс | Стек | Ответственность |
|---|---|---|
| **C# Desktop** (`src/CoinDetailCompare.App`) | WPF, .NET 8, MVVM (CommunityToolkit.Mvvm) | камера, UI, SQLite, настройки, i18n (RU/EN) |
| **Python-бэкенд** (`src/CoinDetailCompare.Backend`) | FastAPI, OpenCV, DINO, FAISS, torch | препроцессинг, эмбеддинги, поиск, аномалии |

Обмен — лёгкий JSON по `127.0.0.1:8300`; все файлы (фото, векторы,
heatmap-ы, слои) лежат на общем диске в `coimg/`:

```
coimg/
  {coin_id}_{obverse|reverse}.png / .npy      # фото + эмбеддинг
  {coin_id}_{obverse|reverse}_heatmap.png/.npy
  {coin_id}_{obverse|reverse}_layer.json      # слой аннотаций
  catalog.db                                  # SQLite
  index/obverse.faiss, index/reverse.faiss    # FAISS (IndexIDMap → coin_id)
```

- **Поиск**: эмбеддинги DINO (ViT-L/14, 1024D) + FAISS, два индекса
  (аверс/реверс), поиск крест-на-перекрёст с процентами по сторонам.
- **Аномалии**: SuperADD / AnomalyDINO (anomalib) с откатом на
  patch-уровневое сравнение с memory bank эталонов; heatmap 64×64 / 128×128,
  геометрическая нормализация сторон перед сравнением (выравнивание центра
  и радиуса гурта + остаточного вращения).
- **Камера**: окно 16:9 — слева живой видок с круговым шаблоном монеты,
  справа превью сторон и кнопки. Горячие клавиши: `1` — аверс, `2` — реверс,
  `Esc` — закрыть.

Подробности: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
[docs/PIPELINE.md](docs/PIPELINE.md), [docs/DB_SCHEMA.md](docs/DB_SCHEMA.md),
контракты JSON — [shared/contracts/](shared/contracts/).

## Структура репозитория

```
src/CoinDetailCompare.App/         # WPF-приложение (UI, VM, сервисы, i18n)
src/CoinDetailCompare.Backend/     # FastAPI-бэкенд (coinbackend/*)
shared/                            # контракты JSON, схема SQLite
docs/                             # публичная документация
scripts/                          # setup/run-скрипты
tools/xaml-smoke/                 # headless smoke-тест XAML-окон
tests/                            # smoke-тесты
coimg/                            # локальные данные (не в git)
```

## Быстрый старт

Требования: Windows, .NET 8 SDK, Python 3.10+ (проверено на 3.12).

```powershell
# 1. Python-окружение (однократно)
scripts\setup.ps1

# 2. База данных (однократно)
python scripts\init_db.py

# 3. Бэкенд (терминал 1)
scripts\run_backend.ps1           # http://127.0.0.1:8300

# 4. Приложение (терминал 2)
scripts\run_app.ps1
```

Сборка и smoke-тест:

```powershell
dotnet build CoinDetailCompare.sln -c Release
dotnet build tools\xaml-smoke\xaml-smoke.csproj -c Release
tools\xaml-smoke\bin\Release\net8.0-windows\xaml-smoke.exe
```

> Примечание: torch для GPU-карт Blackwell (sm_120) в настоящее время
> работает в CPU-режиме — бэкенд это определяет сам, поиск и аномалии
> работают (на CPU patch-анализ ≈ 1–2 мин при сетке 16).
