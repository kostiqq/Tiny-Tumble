# Оптимизация: список кандидатов

*Ревизия кода после этапов 3–4, 2026-10-24. Выбираем пункты и правим точечно.*

Больше всего дадут пункты 1, 2, 4, 6 и 13.

## Сеть (самый большой эффект)

1. **Снимки персонажей.** Сейчас это отдельное RPC на каждого персонажа 25 раз в секунду: время `double`, полный кватернион, `float`-векторы. Собрать всех персонажей в один пакет за тик и сжать (номер тика вместо времени, поворот в 4 байта, квантованные векторы). Ожидаемо трафик в 3–4 раза меньше. `Gameplay/Characters/CharacterNetSync.cs:60`, `Networking/Sync/BodySnapshot.cs`.
2. **Не слать снимки, когда некому.** В соло и локальном коопе хост всё равно сериализует RPC. Дешёвая проверка «есть ли удалённые клиенты». `CharacterNetSync.cs:60`.
3. **Реже слать снимки неподвижных тел**, например 5 раз в секунду, пока персонаж стоит.
4. **Ввод клиента.** Сейчас 50 пакетов в секунду, даже когда игрок стоит. Слать при изменении плюс редкий «пульс», кадр упаковать плотнее. `Networking/Input/NetworkPlayerInput.cs:53`.
5. **Предметы.** `NetworkTransform` шлёт полные `float`. Включить половинную точность, сжатие кватерниона и ненадёжные дельты. `Bootstrap/Editor/ProjectSetup.cs`, метод `CreateItemPrefab`.

## Физика

6. **Слои физики.** Всё на слое Default, матрица столкновений «все со всеми», запросы по всем слоям. Завести слои Character / Item / Arena / KillZone и маски для проверок: меньше пар при обработке столкновений и в запросах, проверка опоры не цепляется за предметы. `Gameplay/Characters/CharacterMotor.cs:91`, `Gameplay/Combat/CharacterCombat.cs:191, 286`, `Gameplay/Characters/PlayerCharacter.cs:363`.
7. **`PredictionHistory.Shift`** перебирает 128 записей на каждом тике коррекции. Хранить накопленный сдвиг — одна операция вместо 128. `Networking/Sync/PredictionHistory.cs:50`.

## Загрузки и память

8. **Лишняя сборка мусора на переходе.** После `Resources.UnloadUnusedAssets` вызывается ещё `GC.Collect`, хотя `UnloadUnusedAssets`, по всей видимости, сам запускает сборку. Проверить профайлером. `Infrastructure/Scenes/SceneLoader.cs:31–32`.
9. **Префаб персонажа грузится в каждом матче заново,** хотя он уже загружен реестром сетевых префабов и живёт всю игру. `Gameplay/Match/MatchEntryPoint.cs:160`.
10. **Пул частиц создаётся лениво:** первые попадания матча дают микрофриз. Прогреть пул при старте. `Gameplay/Combat/CombatFeedback.cs:87`.

## Рендер и UI

11. **HUD на общем Canvas с кнопкой:** каждое изменение встряска перестраивает весь Canvas. Вынести текст в отдельный Canvas, заодно перейти на TextMeshPro. `UI/Match/MatchHudView.cs:117`.
12. **Цвет персонажа через MaterialPropertyBlock** выключает SRP Batcher для этих объектов. С капсулами неважно, станет важно с детальными моделями. `Gameplay/Characters/CharacterPresentation.cs:83`.

## Отладка и инфраструктура

13. **Debug-консоль работает и в релизе:**
    - на каждое сообщение лога собирает строку;
    - `OnGUI` вызывается каждый кадр, даже когда консоль закрыта;
    - в открытом виде высота текста из 200 строк пересчитывается несколько раз за кадр.

    Исправление: убрать консоль из релизных сборок, `useGUILayout = false`, кешировать высоту. `Infrastructure/Debugging/DebugConsole.cs:62, 145`, `Infrastructure/Debugging/DebugConsoleView.cs:62, 96`.
14. **Ожидания опросом каждого кадра** (`await UniTask.Yield` в цикле): подключение, ожидание загрузки клиентов, переходы. Заменить на `UniTaskCompletionSource`. `Networking/Session/NetworkSession.cs:196, 312`, `Networking/Lobby/SessionLobby.cs:199`, `Networking/Flow/NetworkGameFlow.cs:108, 146`.
15. **`GetInvocationList()` при каждом событии** выделяет массив. События редкие, эффект маленький. `SessionLobby.cs:652, 673, 694`, `Gameplay/Match/MatchCharacters.cs:103`.
16. **Линейный поиск по маленьким спискам:** `MatchCharacters.TryGet`, `DevicePlayerInput.FindFlag`, сдвиг массива в `SnapshotInterpolator`. Трогать только заодно с чем-то ещё.

## Настройки проекта

17. **Сборка на Mono.** Для плейтестовых и релизных сборок IL2CPP вместе с Managed Stripping даёт заметный прирост производительности. `ProjectSettings/ProjectSettings.asset`.
