# CoinDetailCompare — Схема данных (SQLite + FAISS)

## SQLite

### Таблица `coin_type` — общая карточка серии (справка)
Один объект на тип/серию. Ссылается из каждой монеты.

```sql
CREATE TABLE coin_type (
  id            INTEGER PRIMARY KEY,
  numista_n     TEXT,            -- N#966
  numista_url   TEXT,            -- https://ru.numista.com/966
  name          TEXT,            -- "1 New Dollar"
  issuer        TEXT,            -- Тайвань
  period        TEXT,            -- Республика (1949-...)
  type          TEXT,            -- Стандартные монеты обращения
  years         TEXT,            -- 70-113 (1981-2024)
  calendar      TEXT,            -- Китайский республиканский календарь (Миньго)
  value         TEXT,            -- 1 юань
  rate          TEXT,            -- 1 TWD = 2.47 RUB
  currency      TEXT,            -- Новый доллар (1949-...)
  composition   TEXT,            -- Алюминиево-никелевая бронза (92/6/2)
  mass          TEXT,            -- 3.8 g
  diameter      TEXT,            -- 20 mm
  thickness     TEXT,            -- 1.57 mm
  shape         TEXT,            -- Круглая
  technique     TEXT,            -- Машинная чеканка
  orientation   TEXT,            -- Медальная ориентация ↑↑
  issued_date   TEXT,            -- 8 Декабря 1981
  links         TEXT,            -- Y#551, Schön#21, PCGS#74600-74602, 74605, ...
  obverse_text  TEXT,            -- блок «Аверс» полностью
  reverse_text  TEXT,            -- блок «Реверс» полностью
  rim_text      TEXT,            -- блок «Гурт» полностью
  created_at    TEXT
);
```

### Таблица `coin` — индивидуальная монета
```sql
CREATE TABLE coin (
  id              INTEGER PRIMARY KEY,   -- = FAISS vector id
  type_id         INTEGER REFERENCES coin_type(id),
  mint_year       TEXT,
  mint            TEXT,                  -- монетный двор (если есть)
  quality         TEXT,                  -- G VG F VF XF AU UNC
  price           REAL DEFAULT 0,       -- цена (зависит от качества и аномалий)
  album_name      TEXT,                  -- номер/название альбома (локальный)
  page_no         INTEGER,              -- номер страницы (локальный)
  order_no        INTEGER,              -- номер по порядку (локальный)
  obverse_photo   TEXT,                  -- /coimg/{id}_obverse.png
  reverse_photo   TEXT,                  -- /coimg/{id}_reverse.png
  obverse_heatmap TEXT,                  -- /coimg/{id}_obverse_heatmap.png|.npy
  reverse_heatmap TEXT,
  obverse_layer   TEXT,                  -- /coimg/{id}_obverse_layer.json
  reverse_layer   TEXT,                  -- /coimg/{id}_reverse_layer.json
  obverse_vector  TEXT,                  -- /coimg/{id}_obverse.npy
  reverse_vector  TEXT,                  -- /coimg/{id}_reverse.npy
  is_anomaly      INTEGER DEFAULT 0,     -- 0/1
  anomaly_desc    TEXT,                  -- описание аномалии
  numista_url     TEXT,                  -- ручная ссылка (если не определена/уточнено)
  source_kind     TEXT,                  -- 'match' | 'duplicate' | 'variant' | 'anomaly' | 'manual' | 'api'
  etalon_id       INTEGER REFERENCES coin(id),  -- эталон (нормальная монета) при аномалии
  created_at      TEXT
);
CREATE INDEX idx_coin_type ON coin(type_id);
CREATE INDEX idx_coin_album_page ON coin(album_name, page_no, order_no);
CREATE INDEX idx_coin_quality ON coin(quality);
```

### Таблица `reference` — справочники (из JSON, референс OpenNumismat)
```sql
CREATE TABLE reference (
  id        INTEGER PRIMARY KEY,
  kind      TEXT,    -- 'quality' | 'rim' | 'orientation' | 'country' | 'denomination' | 'currency'
  code      TEXT,    -- 'VF', 'reeded', ...
  ru        TEXT,
  en        TEXT
);
```

> Значения справочников (сохранности, типы гуртов, ориентации, страны, номиналы, валюты)
> выгружаются в JSON по референсу OpenNumismat и загружаются в `reference`.
> **Код OpenNumismat не копируется** — используется только структура/справки.

### Дубликаты / другие сохранности
Каждая запись (дубликат, другая сохранность, аномалия) — **отдельная строка** в `coin`,
связанная с эталоном через `etalon_id` и типом `source_kind`.

## FAISS

- `IndexIDMap` поверх метрического индекса (`IndexFlatIP` для старта;
  при росте коллекции — `IndexHNSWFlat` / `IVF`).
- **Два индекса**: `obverse` и `reverse`, чтобы независимо искать по каждой стороне
  и проверять перестановку (A–A+R–R и A–R+R–A).
- **ID вектора = `coin.id`** (жёсткая привязка к SQLite).
- Векторы хранятся на диске (`.npy`/`.bin`) и собираются при запуске/по кнопке
  «Пересобрать поисковый индекс».

```
obverse.faiss  →  IndexIDMap:  { 9001: vec, 9002: vec, ... }   # id = coin.id
reverse.faiss  →  IndexIDMap:  { 9001: vec, 9002: vec, ... }   # id = coin.id
```

## Соглашение имён файлов в `/coimg/`
```
/coimg/{id}_obverse.png          # фото аверса (кроп ≥512, целевое 1024)
/coimg/{id}_reverse.png
/coimg/{id}_obverse.npy          # вектор аверса (DINOv3)
/coimg/{id}_reverse.npy
/coimg/{id}_obverse_heatmap.png  # heatmap (визуализация) + .npy (64×64/128×128)
/coimg/{id}_reverse_heatmap.png
/coimg/{id}_obverse_layer.json   # пользовательский векторный слой (норм. коорд. 0.0–1.0)
/coimg/{id}_reverse_layer.json
```
