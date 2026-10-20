# Assembly Definitions: зависимости

*Черновик от 2026-10-06. Дополняет раздел 5 CONTRIBUTING.md.*

## Почему в CONTRIBUTING была неполная цепочка

Там была записана цепочка `UI → Gameplay → Infrastructure → Core`. Она пропускает `Networking` и `AI`, и в ней нет места, где собираются DI-скоупы. `LifetimeScope` должен знать о сервисах всех слоёв, поэтому его нельзя положить в `Infrastructure`: тогда она ссылалась бы вверх, на `Gameplay`. Поэтому ниже добавлена сборка **`Bootstrap`** — корень композиции, единственная, которая видит всех.

## Граф

Стрелка `A → B` значит «A ссылается на B». Ссылки идут только вниз.

```
                     ┌────────────┐
                     │ Bootstrap  │  LifetimeScope'ы, точка входа
                     └─────┬──────┘
          ┌────────────────┼─────────────────┐
          ▼                ▼                 ▼
     ┌────────┐       ┌────────┐        ┌────────┐
     │   UI   │       │   AI   │        │ (все   │
     └───┬────┘       └───┬────┘        │ ниже)  │
         │                │             └────────┘
         └───────┬────────┘
                 ▼
           ┌──────────┐
           │ Gameplay │
           └────┬─────┘
                ▼
          ┌────────────┐
          │ Networking │
          └─────┬──────┘
                ▼
        ┌────────────────┐
        │ Infrastructure │
        └───────┬────────┘
                ▼
            ┌──────┐
            │ Core │
            └──────┘

Служебные сборки (не попадают в билд игры, кроме PlayMode-тестов в тестовом прогоне):

  Editor           → Core, Infrastructure, Networking, Gameplay, AI, UI
  Tests.EditMode   → Core, Infrastructure, Networking, Gameplay, AI
  Tests.PlayMode   → все runtime-сборки, включая Bootstrap

  На Editor и Tests не ссылается ни одна сборка.
```

`UI` и `AI` стоят на одном уровне и **друг о друге не знают**.

## Сборки по порядку (снизу вверх)

### 1. `TinyTumble.Core`
Чистые типы без логики сервисов: интерфейсы, данные, сообщения, утилиты.

- **Наши ссылки:** нет.
- **Пакеты:** `R3.Unity` (+ `R3.dll`), `UniTask`. Больше ничего: ни VContainer, ни NGO, ни Addressables.
- **Что лежит:** `IPlayerInput` и структура ввода, ID игроков и команд, типы конфигов (`ScriptableObject` с балансом: встряск, окна парирования), структуры сообщений для MessagePipe (`PlayerEliminated`, `RoundEnded`), общие интерфейсы сервисов (`ISceneLoader`, `IAssetProvider`, `ISaveService`), математика и расширения.
- **Правило:** если тип нужен двум слоям, интерфейс кладём сюда, а реализацию — в слой, где она живёт.

### 2. `TinyTumble.Infrastructure`
Технические сервисы, не знающие об игре.

- **Наши ссылки:** `Core`.
- **Пакеты:** `VContainer`, `MessagePipe`, `MessagePipe.VContainer`, `UniTask`, `UniTask.Addressables`, `Unity.Addressables`, `Unity.ResourceManager`, `Unity.InputSystem`, `Unity.Localization`.
- **Что лежит:** загрузка сцен, сервис Addressables, сохранения и настройки, аудио, локализация, ввод с устройств (реализация `IPlayerInput` для геймпада/клавиатуры), присоединение локальных игроков, логирование, debug-консоль.

### 3. `TinyTumble.Networking`
Сетевая обвязка NGO, без игровых правил.

- **Наши ссылки:** `Core`, `Infrastructure`.
- **Пакеты:** `Unity.Netcode.Runtime`, `Unity.Networking.Transport`, `VContainer`, `UniTask`, `R3.Unity`; Steam-транспорт и `Facepunch.Steamworks.dll` (precompiled reference) — только здесь.
- **Что лежит:** `INetworkSession` (StartHost/StartClient/Shutdown), `INetworkPrefabInstanceHandler` с инстанцированием через VContainer, регистрация сетевых префабов из Addressables, синхронизация загрузки сцен, модель `PlayerSlot`, лобби, сетевое время, транспорт Steam/Relay.
- **Правило:** Steam API вызывается только из этой сборки. Остальные работают с интерфейсами (`IFriendsService`, `ILobbyService` и т.п. — интерфейсы в `Core`).

### 4. `TinyTumble.Gameplay`
Всё, что делает игру игрой.

- **Наши ссылки:** `Core`, `Infrastructure`, `Networking`.
- **Пакеты:** `Unity.Netcode.Runtime` (NetworkBehaviour, RPC), `VContainer`, `R3.Unity`, `MessagePipe`, `UniTask`, `Unity.Cinemachine`, `Unity.Animation.Rigging`.
- **Что лежит:** персонаж (движение, ragdoll, wobble), бой (толчок, встряск, захват, бросок, парирование, рывок), предметы, арены и модули опасностей, события окружения, режимы и цикл матча, модификаторы и голосование, ведущие (логика боя, фазы, терпение), повтор лучшего момента, кампания (правила испытаний и звёзды).
- **Не ссылается на `Unity.InputSystem`:** ввод приходит через `IPlayerInput` из `Core`. Это то, что позволяет боту и игроку управлять одним персонажем.

### 5. `TinyTumble.AI`
Боты и «мозги» ведущих.

- **Наши ссылки:** `Core`, `Gameplay`.
- **Пакеты:** `VContainer`, `R3.Unity`, `UniTask`.
- **Что лежит:** Utility AI ботов, реализация `IPlayerInput` для бота, уровни сложности, бот-напарник против ведущего, конечный автомат выбора атаки ведущего.
- **Не ссылается на `Networking`:** ИИ работает только на хосте, а решение «запускать ли ИИ» принимает `Gameplay`/`Bootstrap`.
- **Почему отдельно от Gameplay:** ИИ часто меняется и тюнится, отдельная сборка быстрее перекомпилируется и не тянет за собой UI.

### 6. `TinyTumble.UI`
View и Presenter.

- **Наши ссылки:** `Core`, `Infrastructure`, `Gameplay`.
- **Пакеты:** `VContainer`, `R3.Unity`, `MessagePipe`, `UniTask`, `Unity.Localization`, `Unity.TextMeshPro`, `UnityEngine.UI` (или UI Toolkit), `Unity.InputSystem` (только для навигации в меню и ребинда).
- **Что лежит:** экраны, HUD, гардероб, голосование, итоги.
- **Правило:** UI читает состояние через `ReactiveProperty` и интерфейсы моделей, а действия отправляет командами/сообщениями. UI никогда не меняет игровое состояние напрямую и не знает про NGO.

### 7. `TinyTumble.Bootstrap`
Корень композиции.

- **Наши ссылки:** все runtime-сборки.
- **Пакеты:** `VContainer`, `MessagePipe.VContainer`, `Unity.Netcode.Runtime` (для регистрации `NetworkManager`); `Unity.InputSystem` и `R3.Unity` — для сетевого автотеста (`Diagnostics/NetworkAutoTest`, только development-сборки).
- **Что лежит:** `RootLifetimeScope`, `SceneLifetimeScope`'ы меню и лобби, `MatchLifetimeScope`, точка входа сцены `Boot`. Только регистрации, никакой игровой логики.

### Служебные

| Сборка | Платформы | Ссылки |
|---|---|---|
| `TinyTumble.Editor` | только Editor | `Core`, `Infrastructure`, `Networking`, `Gameplay`, `AI`, `UI` + `UnityEditor` API. Валидатор арен, гизмо, инспекторы конфигов, тулзы. На `Bootstrap` не ссылается: тулзы не должны зависеть от того, как собраны скоупы. |
| `TinyTumble.Tests.EditMode` | только Editor | `Core`, `Infrastructure`, `Networking`, `Gameplay`, `AI` + `Unity.Netcode.Runtime`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `nunit.framework.dll`. Логика правил, модификаторов, голосования, подсчёта очков; правила сессии (слоты, допуск, барьер загрузки, сетевой ввод). |
| `TinyTumble.Tests.PlayMode` | все | всё, включая `Bootstrap` + `UnityEngine.TestRunner`, `nunit.framework.dll`. Smoke-тест матча с ботами. |

## Сводная таблица

| Сборка | Core | Infra | Net | Gameplay | AI | UI | Bootstrap |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| **Core** | | | | | | | |
| **Infrastructure** | ✓ | | | | | | |
| **Networking** | ✓ | ✓ | | | | | |
| **Gameplay** | ✓ | ✓ | ✓ | | | | |
| **AI** | ✓ | | | ✓ | | | |
| **UI** | ✓ | ✓ | | ✓ | | | |
| **Bootstrap** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| *Editor* | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| *Tests.EditMode* | ✓ | ✓ | ✓ | ✓ | ✓ | | |
| *Tests.PlayMode* | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

Редакторный код, который нужен рядом с конкретной фичей (кастомный инспектор модуля опасности), можно класть в отдельную editor-only сборку этого слоя, например `TinyTumble.Gameplay.Editor` с платформой только Editor. Правило то же: она ссылается вниз и на свой слой, на неё не ссылается никто.

Пример asmdef для тестов (`Assets/_Project/Scripts/Tests/EditMode/TinyTumble.Tests.EditMode.asmdef`):
```json
{
    "name": "TinyTumble.Tests.EditMode",
    "rootNamespace": "TinyTumble.Tests",
    "references": [
        "TinyTumble.Core",
        "TinyTumble.Infrastructure",
        "TinyTumble.Gameplay",
        "TinyTumble.AI",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "autoReferenced": false
}
```
`TinyTumble.Tests.PlayMode` такой же, но `includePlatforms: []` (все платформы), без `UnityEditor.TestRunner` и со ссылками на все runtime-сборки, включая `TinyTumble.Bootstrap`.

## Общие настройки всех наших asmdef

- `Auto Referenced: false`: никто не ссылается на наши сборки «случайно».
- `Root Namespace`: `TinyTumble.<Слой>`.
- Ссылки на сторонние сборки — только явные, по списку выше. Новый пакет в сборку добавляется через PR с пояснением.
- Unity-типы (`Vector3`, `ScriptableObject`) разрешены везде, включая `Core`.

## Как слой ниже узнаёт о событиях слоя выше

Никак напрямую. Три разрешённых способа:
1. **Интерфейс в нижнем слое, реализация в верхнем.** Пример: `IPlayerInput` в `Core`, реализации в `Infrastructure` (устройства) и `AI` (бот); `Gameplay` знает только интерфейс.
2. **Сообщение через MessagePipe.** Тип сообщения лежит в `Core`, публикует один слой, подписывается любой.
3. **Регистрация в `Bootstrap`.** Связывание конкретных реализаций с интерфейсами делается только там.

## Когда дробить дальше

Пока `Gameplay` — одна сборка. Если она станет долго компилироваться (условный ориентир — больше 10–15 секунд на правку), делим по фичам на `Gameplay.Character`, `Gameplay.Combat`, `Gameplay.Arena`, `Gameplay.Match`, `Gameplay.Host` (ведущие), сохраняя то же правило «только вниз».

## Оговорки

- Имена сторонних asmdef (`R3.Unity`, `Unity.Cinemachine`, `Unity.TextMeshPro` и др.) указаны по известным мне версиям пакетов. Перед созданием сверьте их в инспекторе asmdef: Unity сам подсказывает доступные сборки.
- R3 ставится через NuGetForUnity (`R3.dll`) плюс пакет `R3.Unity`; если поставите иначе, имена ссылок могут отличаться.
