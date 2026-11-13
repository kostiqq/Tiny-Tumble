using TinyTumble.Core.Assets;
using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Правила матча и состав. Стартовые значения — из разделов 3 и 6 GDD, уточняются плейтестом.
    /// </summary>
    [CreateAssetMenu(fileName = "MatchConfig", menuName = "Tiny Tumble/Configs/Match")]
    public sealed class MatchConfig : ScriptableObject
    {
        [Header("Состав")]
        [SerializeField, Range(1, 4)] private int _maxLocalPlayers = 4;
        [SerializeField, Range(0, 3)] private int _botCount = 1;

        [Header("Раунды")]
        [SerializeField, Min(1)] private int _pointsToWin = 3;
        [SerializeField, Min(1)] private int _minRounds = 3;
        [SerializeField, Min(1)] private int _maxRounds = 5;
        [SerializeField, Min(1f)] private float _minRoundDuration = 60f;
        [SerializeField, Min(1f)] private float _maxRoundDuration = 120f;
        [SerializeField, Min(0f)] private float _modifierVoteDuration = 5f;

        [Header("Арена")]
        [SerializeField] private string _arenaKey = AddressKeys.TestArena;
        [SerializeField] private string _characterKey = AddressKeys.Character;
        [Tooltip("Радиус окружности, по которой расставляются участники.")]
        [SerializeField, Min(0f)] private float _spawnRadius = 3f;
        [Tooltip("Высота спавна над столом.")]
        [SerializeField] private float _spawnHeight = 1f;
        [Tooltip("Ниже этой высоты участник считается выпавшим со стола (страховка, если у арены нет kill-зоны).")]
        [SerializeField] private float _eliminationHeight = -5f;

        [Header("Выпадение")]
        [Tooltip("Выпавший возвращается на стол со сброшенным встряском, а не выбывает. Режимы раундов — этап 6.")]
        [SerializeField] private bool _respawnOnFall;

        [Header("Предметы для броска")]
        [SerializeField] private string _itemKey = AddressKeys.ThrowableItem;
        [SerializeField, Range(0, 8)] private int _itemCount = 4;
        [Tooltip("Радиус, внутри которого раскладываются предметы (и куда возвращаются упавшие).")]
        [SerializeField, Min(0f)] private float _itemSpawnRadius = 2f;

        [Header("Цвета")]
        [SerializeField] private Color[] _playerColors =
        {
            new Color(0.93f, 0.26f, 0.21f),
            new Color(0.13f, 0.59f, 0.95f),
            new Color(0.30f, 0.69f, 0.31f),
            new Color(1.00f, 0.76f, 0.03f),
        };
        [SerializeField] private Color _botColor = new Color(0.62f, 0.62f, 0.62f);

        public int MaxLocalPlayers => _maxLocalPlayers;
        public int BotCount => _botCount;
        public int PointsToWin => _pointsToWin;
        public int MinRounds => _minRounds;
        public int MaxRounds => _maxRounds;
        public float MinRoundDuration => _minRoundDuration;
        public float MaxRoundDuration => _maxRoundDuration;
        public float ModifierVoteDuration => _modifierVoteDuration;
        public string ArenaKey => _arenaKey;
        public string CharacterKey => _characterKey;
        public float SpawnRadius => _spawnRadius;
        public float SpawnHeight => _spawnHeight;
        public float EliminationHeight => _eliminationHeight;
        public bool RespawnOnFall => _respawnOnFall;
        public string ItemKey => _itemKey;
        public int ItemCount => _itemCount;
        public float ItemSpawnRadius => _itemSpawnRadius;
        public Color BotColor => _botColor;

        public Color GetPlayerColor(int playerIndex)
        {
            if (_playerColors == null || _playerColors.Length == 0)
            {
                return Color.white;
            }

            return _playerColors[playerIndex % _playerColors.Length];
        }
    }
}
