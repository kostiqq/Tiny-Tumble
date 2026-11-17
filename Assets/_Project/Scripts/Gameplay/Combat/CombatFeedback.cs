using System;
using System.Collections.Generic;
using MessagePipe;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Messages;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>
    /// «Сочность» попаданий на каждой машине: частицы в точке удара и тряска камеры. Всё пропорционально
    /// скорости отлёта, чтобы сильный удар ощущался сильнее. Хит-стопа нет: визуал персонажа — это его
    /// физические кости, у хоста их нельзя «заморозить» отдельно от симуляции.
    /// Живёт в сцене Match; частицы — пул экземпляров префаба.
    /// </summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _hitBurstPrefab;
        [SerializeField, Min(1)] private int _poolSize = 8;
        [Tooltip("Частиц на 1 м/с скорости отлёта.")]
        [SerializeField, Min(0f)] private float _particlesPerKnockback = 2f;
        [SerializeField] private CinemachineImpulseSource _impulse;

        private readonly List<ParticleSystem> _pool = new List<ParticleSystem>();
        private CombatConfig _config;
        private IObjectResolver _resolver;
        private IDisposable _subscription;
        private int _next;

        [Inject]
        public void Construct(CombatConfig config, ISubscriber<CharacterHit> hits, IObjectResolver resolver)
        {
            _config = config;
            _resolver = resolver;
            _subscription = hits.Subscribe(OnHit);
        }

        private void OnDestroy()
        {
            if (_subscription != null)
            {
                _subscription.Dispose();
                _subscription = null;
            }
        }

        private void OnHit(CharacterHit hit)
        {
            EmitParticles(hit.Point, hit.Knockback);

            if (_impulse != null)
            {
                float force = Mathf.Min(_config.MaxCameraShake, hit.Knockback * _config.CameraShakePerKnockback);
                _impulse.GenerateImpulseWithForce(force);
            }
        }

        private void EmitParticles(Vector3 point, float knockback)
        {
            if (_hitBurstPrefab == null)
            {
                return;
            }

            ParticleSystem burst = NextFromPool();
            burst.transform.position = point;
            burst.Emit(Mathf.Max(1, Mathf.RoundToInt(knockback * _particlesPerKnockback)));
        }

        private ParticleSystem NextFromPool()
        {
            if (_pool.Count < _poolSize)
            {
                ParticleSystem created = _resolver.Instantiate(_hitBurstPrefab, transform);
                _pool.Add(created);
                return created;
            }

            ParticleSystem pooled = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            return pooled;
        }
    }
}
