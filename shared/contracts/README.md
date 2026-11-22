# CoinDetailCompare — C# ↔ Python HTTP API (локальный)

Оба процесса на одной Windows-машине. Изображения НЕ гоняются по сети:
в запросах передаётся только **путь** к кропу в `/coimg/`. Тело — лёгкий JSON.
По умолчанию: `http://127.0.0.1:8300` (см. `config/settings.example.json`).

## Endpoints

| Метод | Путь | Назначение |
|-------|------|-----------|
| `GET` | `/health` | Статус бэкенда (версия, device, модель) |
| `POST` | `/preprocess` | Кроп/сегментация/выравнивание/фон + эмбеддинг |
| `POST` | `/search` | Векторный поиск по аверсу/реверсу (обе перестановки) |
| `POST` | `/anomaly` | Детекция аномалий + heatmap |
| `POST` | `/index/rebuild` | Пересборка FAISS из `.npy/.bin` |
| `POST` | `/numista/search` | Текстовый поиск по Numista API |

## Форматы
Примеры запросов/ответов — в соседних `.json`-файлах:
- `preprocess.request.json` / `preprocess.response.json`
- `search.request.json` / `search.response.json`
- `anomaly.request.json` / `anomaly.response.json`

## Соглашения
- Пути — абсолютные, Windows-стиль.
- `side` ∈ `{"obverse","reverse"}`.
- Проценты совпадения — `0..100` (cosine × 100).
- `orientation` ∈ `{"A-A+R-R","A-R+R-A"}` (вариант перестановки сторон).
- Нормализованные координаты слоёв/бокс — `0.0..1.0`.
