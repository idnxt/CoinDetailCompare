# CoinDetailCompare — Справочные репозитории

> ⚠️ **Не копировать код напрямую** из референсных проектов (особенно OpenNumismat).
> Используются как источники идей, схем данных и справочных данных.

## Основные модели / методы
- **DINOv3** — https://github.com/facebookresearch/dinov3
  Основная модель эмбеддингов. ViT-H+/16 (≈840M) на GPU; ViT-L/16 и ниже на CPU.
  Патч-эмбеддинги — для поиска и патч-уровневого сравнения аномалий.
- **SuperADD** — https://github.com/LukasRoom/SuperADD
  Детекция аномалий (приоритет). Бэкбон `anomalib`, по умолчанию ViT-H+/16,
  memory bank + тепловые карты.
- **AnomalyDINO** — https://github.com/dammsi/AnomalyDINO
  Training-free альтернатива. Официально B/L; H+ не валидирован; DINOv2
  иногда лучше DINOv3.
- **anomalib** (Intel) — https://github.com/open-edge-platform/anomalib
  Промышленная библиотека для интеграции.

## Устаревшие (источник идей)
- **Coin-CLIP** — https://github.com/breezedeus/Coin-CLIP (устарело).
- **WinCLIP** — https://github.com/mala-lab/WinCLIP (устарело; основная — DINOv3).

## Справочные данные / схема БД
- **OpenNumismat** — https://github.com/OpenNumismat/open-numismat
  - Схема SQLite — референс для хранения нумизматических атрибутов.
  - Готовые переводы, списки сохранностей, типов гуртов, ориентаций, стран,
    номиналов — выгрузить в JSON и использовать в C#-интерфейсе.
  - Интеграция с Numista API — ориентир.
  - Перспектива: кнопка «Экспорт в формат OpenNumismat (.db)» и обмен коллекциями.

## API
- **Numista API** (текстовый поиск): https://ru.numista.com/api/doc/index.php
