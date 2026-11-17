using TinyTumble.Networking.Sync;
using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Физический скелет персонажа: кости на Rigidbody, соединённые <see cref="ConfigurableJoint"/>.
    /// Приводы суставов тянут кости к процедурной позе (<see cref="ProceduralPose"/>); сила приводов
    /// зависит от состояния (обычное, ragdoll, подъём). Собирается генератором префаба по гуманоидной модели.
    /// На хосте кости симулируются; у клиентов они кинематические и ставятся по снимкам хоста.
    /// «Оси персонажа» здесь — оси корневого объекта в позе покоя: x вправо, y вверх, z вперёд.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollRig : MonoBehaviour
    {
        private const float MaxAngularVelocity = 50f;

        [SerializeField] private Rigidbody[] _bodies = new Rigidbody[RagdollBones.Count];
        [SerializeField] private ConfigurableJoint[] _joints = new ConfigurableJoint[RagdollBones.Count];
        [Tooltip("Куда смотрит кость в покое (следующая кость цепочки): по нему строятся позы конечностей.")]
        [SerializeField] private Transform[] _ends = new Transform[RagdollBones.Count];
        [SerializeField] private float[] _massShares = new float[RagdollBones.Count];
        [SerializeField] private Collider[] _colliders = new Collider[0];
        [Tooltip("Рост от подошв до макушки, м.")]
        [SerializeField, Min(0.01f)] private float _height = 1f;
        [Tooltip("Высота таза над опорой в позе покоя, м: на ней его держит пружина «ног».")]
        [SerializeField, Min(0.01f)] private float _rideHeight = 0.4f;

        private readonly Quaternion[] _restCharacter = new Quaternion[RagdollBones.Count];
        private readonly Vector3[] _restDirection = new Vector3[RagdollBones.Count];
        private readonly Quaternion[] _restRelative = new Quaternion[RagdollBones.Count];
        private readonly Quaternion[] _jointFrames = new Quaternion[RagdollBones.Count];
        private readonly Vector3[] _restLocalPositions = new Vector3[RagdollBones.Count];
        private readonly Quaternion[] _restLocalRotations = new Quaternion[RagdollBones.Count];

        private Collider[] _legColliders = new Collider[0];
        private bool _legsCollide = true;
        private float _totalMass = 1f;
        private float _poseFrequency = 22f;
        private float _poseDamping = 0.7f;
        private float _driveScale = -1f;
        private bool _cached;

        public float Height => _height;

        public float RideHeight => _rideHeight;

        public float TotalMass => _totalMass;

        public Rigidbody Hips => _bodies[RagdollBones.Hips];

        /// <summary>Все коллайдеры персонажа: для отключения столкновений при захвате.</summary>
        public Collider[] Colliders => _colliders;

        /// <summary>Точка под тазом на уровне подошв в позе стоя.</summary>
        public Vector3 FeetPosition => Hips.position - Vector3.up * _rideHeight;

        /// <summary>«Вверх» персонажа по тазу: у стоящего совпадает с мировым.</summary>
        public Vector3 Up => CharacterRotation * Vector3.up;

        /// <summary>«Вперёд» персонажа по тазу.</summary>
        public Vector3 Forward => CharacterRotation * Vector3.forward;

        /// <summary>Поворот осей персонажа по текущему положению таза.</summary>
        private Quaternion CharacterRotation => Hips.rotation * Quaternion.Inverse(_restCharacter[RagdollBones.Hips]);

        public Rigidbody Body(int bone)
        {
            return _bodies[bone];
        }

        /// <summary>Поворот кости в покое, в осях персонажа.</summary>
        public Quaternion RestRotation(int bone)
        {
            return _restCharacter[bone];
        }

        /// <summary>Направление кости в покое, в осях персонажа.</summary>
        public Vector3 RestDirection(int bone)
        {
            return _restDirection[bone];
        }

        public bool Owns(Rigidbody body)
        {
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                if (_bodies[i] == body)
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            CacheRest();
            IgnoreSelfCollisions();
        }

        /// <summary>Масса и параметры приводов из конфига; повторный вызов допустим (конфиг правят на лету).</summary>
        public void Configure(float totalMass, float poseFrequency, float poseDamping)
        {
            CacheRest();
            _totalMass = 0f;
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                _bodies[i].mass = Mathf.Max(0.001f, totalMass * _massShares[i]);
                _totalMass += _bodies[i].mass;
            }

            _poseFrequency = poseFrequency;
            _poseDamping = poseDamping;
            float scale = _driveScale < 0f ? 1f : _driveScale;
            _driveScale = -1f;
            SetDriveStrength(scale);
        }

        /// <summary>Хост: тело симулируется физикой. Клиент: кинематическое, его ставят снимки.</summary>
        public void SetSimulated(bool simulated)
        {
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                Rigidbody body = _bodies[i];
                body.isKinematic = !simulated;
                // Интерполяция Rigidbody спорила бы с прямой записью трансформов у клиента.
                body.interpolation = simulated ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            }
        }

        /// <summary>Хост: таз ведётся кодом (персонажа держат), конечности болтаются.</summary>
        public void SetHipsKinematic(bool kinematic)
        {
            Hips.isKinematic = kinematic;
        }

        /// <summary>
        /// Сталкиваются ли ноги с миром. На ногах — нет: вес держит пружина таза, а короткие ноги иначе
        /// цеплялись бы за стол и предметы и складывались под тело. В ragdoll — да, чтобы не проваливались.
        /// </summary>
        public void SetLegCollisions(bool collide)
        {
            if (_legsCollide == collide)
            {
                return;
            }

            _legsCollide = collide;
            LayerMask exclude = collide ? 0 : ~0;
            for (int i = 0; i < _legColliders.Length; i++)
            {
                _legColliders[i].excludeLayers = exclude;
            }
        }

        /// <summary>Сила приводов суставов: 1 — держит позу, 0 — полностью обмяк.</summary>
        public void SetDriveStrength(float scale)
        {
            if (Mathf.Abs(scale - _driveScale) < 1e-3f)
            {
                return;
            }

            _driveScale = scale;
            float frequency = _poseFrequency;
            // Привод в единицах ускорения: PhysX сам учитывает массу и инерцию кости и всего, что на ней висит,
            // поэтому частота одинакова для любой кости и любой модели.
            var drive = new JointDrive
            {
                positionSpring = frequency * frequency * scale,
                positionDamper = 2f * _poseDamping * frequency * scale,
                maximumForce = float.MaxValue,
                useAcceleration = true,
            };
            for (int i = 1; i < RagdollBones.Count; i++)
            {
                _joints[i].slerpDrive = drive;
            }
        }

        /// <summary>Хост: тянуть кости к позе. <paramref name="desired"/> — повороты костей в осях персонажа.</summary>
        public void DriveTowards(Quaternion[] desired)
        {
            for (int i = 1; i < RagdollBones.Count; i++)
            {
                Quaternion relative = Quaternion.Inverse(desired[RagdollBones.Parent(i)]) * desired[i];
                _joints[i].targetRotation = JointTargets.ToJointSpace(_jointFrames[i], relative, _restRelative[i]);
            }
        }

        /// <summary>Поворот кости относительно родительской, как его видит физика сейчас.</summary>
        public Quaternion RelativeRotation(int bone)
        {
            return Quaternion.Inverse(_bodies[RagdollBones.Parent(bone)].rotation) * _bodies[bone].rotation;
        }

        /// <summary>Хост: позу всех костей, кроме таза, — в снимок.</summary>
        public void CapturePose(ref PackedPose pose)
        {
            for (int i = 1; i < RagdollBones.Count; i++)
            {
                pose[i - 1] = QuaternionPacking.Pack(RelativeRotation(i));
            }
        }

        /// <summary>
        /// Клиент: поставить скелет между двумя снимками хоста. Кости без собственного положения:
        /// оно следует из иерархии, суставы держат их на месте.
        /// </summary>
        public void ApplyPose(Vector3 hipsPosition, Quaternion hipsRotation, in PackedPose from, in PackedPose to, float t)
        {
            Hips.transform.SetPositionAndRotation(hipsPosition, hipsRotation);
            for (int i = 1; i < RagdollBones.Count; i++)
            {
                Quaternion relative = Quaternion.Slerp(QuaternionPacking.Unpack(from[i - 1]), QuaternionPacking.Unpack(to[i - 1]), t);
                _bodies[i].transform.rotation = _bodies[RagdollBones.Parent(i)].transform.rotation * relative;
            }
        }

        /// <summary>Клиент: подмешать к показанной позе желаемую (мгновенный отклик владельца).</summary>
        public void BlendTowards(Quaternion[] desired, int bone, float weight)
        {
            Quaternion relative = Quaternion.Inverse(desired[RagdollBones.Parent(bone)]) * desired[bone];
            Transform target = _bodies[bone].transform;
            Quaternion goal = _bodies[RagdollBones.Parent(bone)].transform.rotation * relative;
            target.rotation = Quaternion.Slerp(target.rotation, goal, weight);
        }

        /// <summary>Поставить персонажа стоя в точку (pivot у ног) и погасить все скорости.</summary>
        public void ResetPose(Vector3 position, Quaternion rotation)
        {
            CacheRest();
            transform.SetPositionAndRotation(position, rotation);
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                _bodies[i].transform.SetLocalPositionAndRotation(_restLocalPositions[i], _restLocalRotations[i]);
            }

            for (int i = 0; i < RagdollBones.Count; i++)
            {
                Rigidbody body = _bodies[i];
                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            Physics.SyncTransforms();
        }

        public void SetVelocity(Vector3 velocity)
        {
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                if (!_bodies[i].isKinematic)
                {
                    _bodies[i].linearVelocity = velocity;
                }
            }
        }

        public void AddVelocity(Vector3 delta)
        {
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                if (!_bodies[i].isKinematic)
                {
                    _bodies[i].linearVelocity += delta;
                }
            }
        }

        /// <summary>Одинаковое ускорение всем костям (дополнительная гравитация падения).</summary>
        public void AddAcceleration(Vector3 acceleration)
        {
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                if (!_bodies[i].isKinematic)
                {
                    _bodies[i].AddForce(acceleration, ForceMode.Acceleration);
                }
            }
        }

        private void CacheRest()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            Quaternion inverseRoot = Quaternion.Inverse(transform.rotation);
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                Transform bone = _bodies[i].transform;
                _restCharacter[i] = inverseRoot * bone.rotation;
                _restLocalPositions[i] = bone.localPosition;
                _restLocalRotations[i] = bone.localRotation;

                Vector3 direction = _ends[i] != null ? _ends[i].position - bone.position : transform.up;
                _restDirection[i] = direction.sqrMagnitude > 1e-8f ? (inverseRoot * direction).normalized : Vector3.up;
                _bodies[i].maxAngularVelocity = MaxAngularVelocity;
            }

            int legs = 0;
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (IsLeg(_colliders[i].attachedRigidbody))
                {
                    legs++;
                }
            }

            _legColliders = new Collider[legs];
            legs = 0;
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (IsLeg(_colliders[i].attachedRigidbody))
                {
                    _legColliders[legs++] = _colliders[i];
                }
            }

            _restRelative[RagdollBones.Hips] = Quaternion.identity;
            for (int i = 1; i < RagdollBones.Count; i++)
            {
                _restRelative[i] = Quaternion.Inverse(_restCharacter[RagdollBones.Parent(i)]) * _restCharacter[i];
                ConfigurableJoint joint = _joints[i];
                _jointFrames[i] = JointTargets.JointFrame(joint.axis, joint.secondaryAxis);
            }
        }

        private bool IsLeg(Rigidbody body)
        {
            return body == _bodies[RagdollBones.LeftUpperLeg] || body == _bodies[RagdollBones.LeftLowerLeg]
                || body == _bodies[RagdollBones.RightUpperLeg] || body == _bodies[RagdollBones.RightLowerLeg];
        }

        private void IgnoreSelfCollisions()
        {
            for (int a = 0; a < _colliders.Length; a++)
            {
                for (int b = a + 1; b < _colliders.Length; b++)
                {
                    Physics.IgnoreCollision(_colliders[a], _colliders[b], true);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>Только для генератора префаба.</summary>
        public void EditorSetup(Rigidbody[] bodies, ConfigurableJoint[] joints, Transform[] ends, float[] massShares,
            Collider[] colliders, float height, float rideHeight)
        {
            _bodies = bodies;
            _joints = joints;
            _ends = ends;
            _massShares = massShares;
            _colliders = colliders;
            _height = height;
            _rideHeight = rideHeight;
        }
#endif
    }
}
