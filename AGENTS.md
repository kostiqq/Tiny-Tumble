# Tiny Tumble

3D casual co-op arena brawler на Unity («арена на столе»: игрушки выталкивают друг друга с бытовых предметов). Бой без HP — процент «встряска» увеличивает отлёт, выпавший за край выбывает. Steam (PC) на старте. Команда RevShare из 4 человек; пользователь — лид-программист.

## Документация (читать перед работой над фичей)

Всё в `Assets/docs/`:
- `development-roadmap.md` — стек, сетевая модель, этапы 0–14 и вехи A–D, риски.
- `CONTRIBUTING.md` — ветки, коммиты, стиль кода, правила VContainer / R3 / UniTask / Addressables / NGO, критерии «готово».
- `assembly-definitions.md` — граф сборок и разрешённые пакеты для каждой.
- `networking.md` — сессия NGO, лобби, миграция хоста, автотест на двух сборках, консольные команды `net.*`.
- `character-and-combat.md` — персонаж, состояния, бой, сетевая синхронизация движения, боты вехи A.
- `optimization-backlog.md` — пронумерованные кандидаты на оптимизацию с путями к коду.
- `coop-arena-brawl-ideas.md`, `market-analysis-party-brawlers.md` — брейншторм и рынок.

Правила ассистента: `Assets/_claude/unity_developer/SKILL.md` (код без лишних аллокаций, оценка уверенности 🟢/🟡/🔴, не додумывать факты).

## Стек

Unity 6000.5.6f1 (URP), Netcode for GameObjects (хост авторитетен, P2P), VContainer (скоупы Root → Scene → Match), R3 + MessagePipe, UniTask, Addressables, New Input System, Cinemachine, Steamworks.NET (`com.rlabrecque.steamworks.net`).

## Структура кода

`Assets/_Project/Scripts/<Слой>/`. Каждый слой — asmdef `TinyTumble.<Слой>`, ссылки только вниз:
`Bootstrap → UI / AI → Gameplay → Networking → Infrastructure → Core`. UI и AI друг о друге не знают; AI и UI не ссылаются на NGO; Gameplay не ссылается на Input System (только `IPlayerInput`); Steam API — только в Networking.

Контент — в `Assets/_Project/` (`Art`, `Audio`, `Prefabs`, `Scenes`, `Configs`, `AddressableAssets`); конфиги баланса — `Assets/_Project/Configs/*.asset`. Сцены: `Boot` → `MainMenu` → `Lobby` → `Match` → `Results`; запуск всегда с `Boot`.

## Ключевые правила

- Соло и локальный кооп тоже работают как хост NGO — без отдельной офлайн-ветки логики.
- Без синглтонов и `Find*`; `[Inject] Construct(...)` в MonoBehaviour; рантайм-префабы через `IObjectResolver.Instantiate`.
- Не использовать `?.` / `??` с `UnityEngine.Object`; `async void` запрещён; подписки R3 освобождать.
- Поля `_camelCase`, скобки Allman, комментарии на русском, имена в коде на английском.
- Новый сетевой префаб: `NetworkObject` + метка Addressables `network`.
- Фича готова, если работает в соло, локальном коопе и по сети.

## Git: только пользователь

Ассистенту **запрещено** делать коммиты (включая `--amend`), создавать и переключать ветки, пушить, создавать и вливать PR — ни через `git`, ни через `gh`, ни через IDE. Ассистент только меняет файлы в рабочей копии; коммитами, ветками и PR занимается пользователь. Можно: читать состояние (`git status`, `diff`, `log`, `show`) и предлагать текст коммита. Правила Conventional Commits и веток из `CONTRIBUTING.md` относятся к людям и к тексту, который ассистент предлагает, а не к его действиям.

## Статус (2026-11-13)

Код этапов 0–4 готов, проект перед плейтестом вехи A. Сделаны Steam-транспорт с приглашениями, пати отдельно от сессии и поиск игры с ботами на пустых местах (шаги 1–4 плана в `networking.md`); дальше — UI пати и поиска, люди между матчами (шаг 5). Не сделано: Relay, выбор персонажа в лобби, модель/риг персонажа, звук. GDD (`design/gdd-tabletop-arena.md`) упоминается в роадмапе, но ещё не написан — пользователь распишет его позже.

Внешний документ с контекстом проекта (Codex Docs): https://claude.ai/artifact/6hjHqF7aKtVoPBPLL3o9xP
