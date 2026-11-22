# CoinDetailCompare — Статус проекта

Обновляется по мере выполнения работ.

## Общие статусы
| Блок | Статус | Примечание |
|------|--------|-----------|
| ТЗ / архитектура | `~ готово` | Черновик ТЗ, согласование архитектуры |
| План разработки (ROADMAP) | `[x]` | 9 фаз, статусы обновляются |
| Каркас C# (WPF, MVVM) | `[~]` | Код написан (сервисы, UI, VM); сборка требует .NET 8 SDK |
| Каркас Python (FastAPI) | `[x]` | Запускается; /health, /index/rebuild, /numista/parse проверены |
| SQLite схема | `[x]` | shared/sqlite_schema.sql; init_db.py проверен |
| FAISS интеграция | `[x]` | IndexIDMap, 2 индекса; numpy-fallback; rebuild из .npy |
| DINOv3 эмбеддинги | `[~]` | Обёртка + MockEmbedder (dev); реальные веса — впереди |
| Предобработка | `[~]` | Кроп/сегментация/фон/выравнивание (OpenCV) — тест на реальных снимках впереди |
| Аномалии (SuperADD/AnomalyDINO) | `[~]` | Scaffolding + patch-dev-fallback; production-модуль впереди |
| UI / i18n | `[~]` | UI-каркас + ru/en переводы; связывание всех строк впереди |
| Numista API | `[~]` | Клиент + endpoint; парсинг живой API впереди |

## Журнал статусов (changelog)
- **2026-11-22** — Фаза 0 (каркас): 
  - git инициализирован, docs закоммичены (ТЗ в `docs/internal` — не публикуется, gitignored).
  - Python: `coinbackend` (config, contracts, storage, preprocess, embed, search, anomaly, numista, app),
    `run.py`, `requirements.txt`, `pyproject.toml`.
  - C#: `CoinDetailCompare.App` (WPF, MVVM: Models, Services, ViewModels, Views, i18n), `.sln`.
  - Shared: `sqlite_schema.sql`, JSON-контракты API, пример конфигов, скрипты (setup/run/init).
  - Проверено: py-сборка всех модулей, `init_db.py` (SQLite), smoke-тест поиска обеих перестановок,
    запуск FastAPI + TestClient (`/health`, `/index/rebuild`, `/numista/parse`).
- **2026-11-22** — Создана рабочая документация: ТЗ, архитектура, план, статусы, схема,
  конвейер, аномалии, UI/UX, i18n, Numista, референсы.

## Открытые решения (блокируют/влияют на следующие фазы)
1. Выбор детектора аномалий: SuperADD (приоритет) vs AnomalyDINO (fallback) — тестирование.
2. Нестрого круглые монеты: HoughCircles vs лёгкая сегментационная модель.
3. Выравнивание угла: авто (RANSAC по патчам DINOv3) vs ручная «крутилка» — оба, как опции.
4. Размер heatmap: 64×64 vs 128×128 (конфигурируемо).
5. Размер/тип вектора DINOv3 (пул vs патч-эмбеддинги).
6. Хранение дубликатов/разных сохранностей: отдельные записи со связью `etalon_id`.
7. C# не собирается в текущей среде (нет .NET 8 SDK) — проверить сборку при наличии SDK.
