# CoinDetailCompare — Статус проекта

Обновляется по мере выполнения работ.

## Общие статусы
| Блок | Статус | Примечание |
|------|--------|-----------|
| ТЗ / архитектура | `[x]` | ТЗ, архитектура, план |
| План разработки (ROADMAP) | `[x]` | 9 фаз, статусы обновляются |
| C# (WPF, MVVM) | `[~]` | Код фаз 0–7 написан; сборка требует .NET 8 SDK (нет в этой среде) |
| Python (FastAPI) | `[x]` | Запускается; /health, /embed, /search, /anomaly, /references, /numista/parse, /index/rebuild проверены |
| SQLite схема | `[x]` | shared/sqlite_schema.sql; init_db.py проверен |
| FAISS интеграция | `[x]` | IndexIDMap, 2 индекса; numpy-fallback; rebuild из .npy; round-trip 100% |
| DINOv3 эмбеддинги | `[x]` | Реальная модель (DINOv2 ViT-L/14, dim 1024) через torch.hub; MockEmbedder — dev-fallback; GPU sm_120 → CPU-auto-fallback |
| Предобработка | `[~]` | Кроп/сегментация/фон/выравнивание (OpenCV) — тест на реальных снимках впереди |
| Аномалии (SuperADD/AnomalyDINO) | `[~]` | auto/anomalydino/superadd/patch + heatmap; `/anomaly` проверен TestClient (score, bbox, top-патчи, heatmap-файлы); production-модуль на целевой машине — впереди |
| Слой пометок (C#) | `[x]` | AnnotationCanvas + layer.json (линия/эллипс/стрелка, undo/clear/save) |
| UI / i18n | `[x]` | Все подписи через Strings-биндинги; ru/en, переключатель в статус-баре |
| Numista API | `[~]` | Клиент + адаптивный парсер + fetch_meta; живая сверка впереди (numista.com 403) |
| Экспорт OpenNumismat | `[~]` | scripts/export_opennumismat.py (структура .db как референс); сверка с реальным файлом впереди |

## Журнал статусов (changelog)
- **2026-11-22** — Фазы 2–7 (код):
  - Фаза 2: цепочка загрузки DINOv3 (локальные веса → torch.hub dinov3/dinov2 → HF →
    MockEmbedder), проверка реального GPU-запуска (sm_120 → CPU), FAISS round-trip
    (реальная модель, 1-е место = 100%), `/embed` + C# SaveAsync.
  - Фаза 3: `shared/references/*.json` → таблица `reference` (385 записей),
    `GET /references`, TypeEditor + dropdowns в UI.
  - Фаза 4: `anomaly_backend` (auto/superadd/anomalydino/patch), heatmap
    (`.npy` + PNG), UI «Проверить аномалии» + heatmap-оверлей;
    `/anomaly` проверен TestClient (score 0.128, bbox, top-патчи, heatmap-файлы).
  - Фаза 5: AnnotationCanvas + `{id}_{side}_layer.json` (линия/эллипс/стрелка).
  - Фаза 6: адаптивный парсер Numista, `fetch_meta` (API → page title),
    поиск + «подставить в карточку».
  - Фаза 7: `Strings` в App.Resources, все подписи XAML через i18n-биндинги,
    ru/en + переключатель языка.
  - Фаза 8 (начало): `scripts/export_opennumismat.py` (структура .db как референс
    из демо OpenNumismat), README: быстрый старт + экспорт + тесты.
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
