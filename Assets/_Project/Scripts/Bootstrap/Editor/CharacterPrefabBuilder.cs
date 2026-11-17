using System;
using System.Collections.Generic;
using TinyTumble.Core.Configs;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Networking.Input;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TinyTumble.Bootstrap.Editor
{
    /// <summary>
    /// Собирает сетевой префаб физического персонажа из гуманоидной модели по <see cref="CharacterProfile"/>:
    /// масштабирует модель до роста из профиля, ставит подошвы в pivot, находит кости по аватару Humanoid
    /// и вешает на фиксированный набор костей (<see cref="RagdollBones"/>) Rigidbody, коллайдеры и суставы.
    /// Префаб пересоздаётся целиком; всё, что нужно подкрутить под модель, — в профиле.
    /// </summary>
    public static class CharacterPrefabBuilder
    {
        private const int SolverIterations = 12;
        private const int SolverVelocityIterations = 4;
        private const float MaxSwingLimit = 177f;

        // Подошва чуть над столом в позе стоя: вес держит пружина таза, а не ноги.
        private const float SoleClearance = 0.02f;

        private static readonly HumanBodyBones[] s_humanBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
        };

        /// <param name="legPhysics">Материал ног: без трения, иначе ноги цепляются за стол и опрокидывают тело,
        /// которое тянет мотор.</param>
        public static void Build(CharacterProfile profile, string prefabPath, Material material, PhysicsMaterial physics,
            PhysicsMaterial legPhysics, CharacterConfig config)
        {
            if (profile == null || profile.Model == null)
            {
                throw new InvalidOperationException("Character profile has no model.");
            }

            string modelPath = AssetDatabase.GetAssetPath(profile.Model);
            EnsureHumanoid(modelPath);
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(prefabPath));
            try
            {
                root.AddComponent<NetworkObject>();
                // Движение синхронизирует CharacterNetSync (таз и кости), а не NetworkTransform.
                var networkInput = root.AddComponent<NetworkPlayerInput>();
                var sync = root.AddComponent<CharacterNetSync>();
                var rig = root.AddComponent<RagdollRig>();
                var presentation = root.AddComponent<CharacterPresentation>();
                var character = root.AddComponent<PlayerCharacter>();

                GameObject model = Object.Instantiate(modelAsset, root.transform, false);
                model.name = "Model";
                Animator animator = model.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    throw new InvalidOperationException(
                        $"{modelPath}: humanoid avatar is not valid. Open the model's Rig tab, press Configure and fix the bone mapping.");
                }

                Transform[] bones = FindBones(animator, modelPath);
                FitModel(model.transform, bones, profile.Height);

                Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                if (leftHand == null || rightHand == null || leftFoot == null || rightFoot == null)
                {
                    throw new InvalidOperationException($"{modelPath}: hands and feet are required.");
                }

                // Аватар больше не нужен: позы процедурные, а Animator спорил бы с физикой за кости.
                Object.DestroyImmediate(animator);

                var ends = new Transform[RagdollBones.Count];
                ends[RagdollBones.Chest] = neck != null ? neck : bones[RagdollBones.Head];
                ends[RagdollBones.LeftUpperArm] = bones[RagdollBones.LeftLowerArm];
                ends[RagdollBones.LeftLowerArm] = leftHand;
                ends[RagdollBones.RightUpperArm] = bones[RagdollBones.RightLowerArm];
                ends[RagdollBones.RightLowerArm] = rightHand;
                ends[RagdollBones.LeftUpperLeg] = bones[RagdollBones.LeftLowerLeg];
                ends[RagdollBones.LeftLowerLeg] = leftFoot;
                ends[RagdollBones.RightUpperLeg] = bones[RagdollBones.RightLowerLeg];
                ends[RagdollBones.RightLowerLeg] = rightFoot;

                float height = profile.Height;
                float headTop = MeasureBounds(model).max.y;
                float[] shares = MassShares(profile);
                var bodies = new Rigidbody[RagdollBones.Count];
                var colliders = new List<Collider>(RagdollBones.Count);

                for (int i = 0; i < RagdollBones.Count; i++)
                {
                    bodies[i] = AddBody(bones[i], config.Mass * shares[i]);
                }

                // Коллайдеры: капсулы вдоль костей, голова — шар до макушки.
                Vector3 leftHip = bones[RagdollBones.LeftUpperLeg].position;
                Vector3 rightHip = bones[RagdollBones.RightUpperLeg].position;
                Vector3 hipsLift = (bones[RagdollBones.Hips].position - (leftHip + rightHip) * 0.5f) * 0.5f;
                colliders.Add(AddCapsule(bones[RagdollBones.Hips], leftHip + hipsLift, rightHip + hipsLift, profile.HipsRadius * height, physics));

                Vector3 chestFrom = spine != null
                    ? Vector3.Lerp(spine.position, bones[RagdollBones.Chest].position, 0.5f)
                    : bones[RagdollBones.Chest].position;
                colliders.Add(AddCapsule(bones[RagdollBones.Chest], chestFrom, ends[RagdollBones.Chest].position, profile.ChestRadius * height, physics));

                Vector3 head = bones[RagdollBones.Head].position;
                float headRadius = profile.HeadRadius > 0f
                    ? profile.HeadRadius * height
                    : Mathf.Clamp((headTop - head.y) * 0.5f, 0.02f * height, 0.3f * height);
                colliders.Add(AddSphere(bones[RagdollBones.Head], head + Vector3.up * headRadius, headRadius, physics));

                float arm = profile.ArmRadius * height;
                AddArm(bones, RagdollBones.LeftUpperArm, RagdollBones.LeftLowerArm, leftHand, arm, physics, colliders);
                AddArm(bones, RagdollBones.RightUpperArm, RagdollBones.RightLowerArm, rightHand, arm, physics, colliders);

                float leg = profile.LegRadius * height;
                float clearance = SoleClearance * height;
                AddLeg(bones, RagdollBones.LeftUpperLeg, RagdollBones.LeftLowerLeg, leftFoot, leg, clearance, legPhysics, colliders);
                AddLeg(bones, RagdollBones.RightUpperLeg, RagdollBones.RightLowerLeg, rightFoot, leg, clearance, legPhysics, colliders);

                var joints = new ConfigurableJoint[RagdollBones.Count];
                for (int i = 1; i < RagdollBones.Count; i++)
                {
                    joints[i] = AddJoint(bones[i], bodies[RagdollBones.Parent(i)], ends[i], Limits(profile, i));
                }

                for (int i = 0; i < RagdollBones.Count; i++)
                {
                    bones[i].gameObject.AddComponent<CharacterBodyPart>().EditorSetup(character, i);
                    if (RagdollBones.IsHand(i))
                    {
                        bones[i].gameObject.AddComponent<HandContact>().EditorSetup(character);
                    }
                }

                float rideHeight = bones[RagdollBones.Hips].position.y;
                rig.EditorSetup(bodies, joints, ends, shares, colliders.ToArray(), height, rideHeight);
                presentation.EditorSetup(SetupRenderers(model, material));

                var serialized = new SerializedObject(character);
                serialized.FindProperty("_rig").objectReferenceValue = rig;
                serialized.FindProperty("_networkInput").objectReferenceValue = networkInput;
                serialized.FindProperty("_sync").objectReferenceValue = sync;
                serialized.FindProperty("_presentation").objectReferenceValue = presentation;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[Setup] Character {prefabPath}: height {height:0.00} m, hips at {rideHeight:0.00} m, {colliders.Count} colliders.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Импорт Humanoid нужен ради аватара: по нему кости находятся на любой гуманоидной модели.</summary>
        private static void EnsureHumanoid(string modelPath)
        {
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
            {
                throw new InvalidOperationException($"{modelPath} is not a model.");
            }

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }

            if (importer.importAnimation)
            {
                importer.importAnimation = false;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static Transform[] FindBones(Animator animator, string modelPath)
        {
            var bones = new Transform[RagdollBones.Count];
            for (int i = 0; i < RagdollBones.Count; i++)
            {
                Transform bone = animator.GetBoneTransform(s_humanBones[i]);
                if (bone == null && i == RagdollBones.Chest)
                {
                    // Грудь в аватаре необязательна: тогда торс — это позвоночник.
                    bone = animator.GetBoneTransform(HumanBodyBones.Spine);
                }

                if (bone == null)
                {
                    throw new InvalidOperationException($"{modelPath}: humanoid bone {s_humanBones[i]} is missing.");
                }

                bones[i] = bone;
            }

            return bones;
        }

        /// <summary>Лицом по +Z, рост из профиля, подошвы на нуле, таз над pivot.</summary>
        private static void FitModel(Transform model, Transform[] bones, float height)
        {
            Vector3 right = bones[RagdollBones.RightUpperArm].position - bones[RagdollBones.LeftUpperArm].position;
            right.y = 0f;
            if (right.sqrMagnitude > 1e-6f)
            {
                Vector3 forward = Vector3.Cross(right.normalized, Vector3.up);
                model.rotation = Quaternion.FromToRotation(forward, Vector3.forward) * model.rotation;
            }

            Bounds bounds = MeasureBounds(model.gameObject);
            if (bounds.size.y < 1e-4f)
            {
                throw new InvalidOperationException("Model has no visible mesh.");
            }

            model.localScale *= height / bounds.size.y;
            bounds = MeasureBounds(model.gameObject);
            Vector3 hips = bones[RagdollBones.Hips].position;
            model.position += new Vector3(-hips.x, -bounds.min.y, -hips.z);
        }

        /// <summary>Границы меша в текущей позе (мировые координаты; корень префаба — в нуле).</summary>
        private static Bounds MeasureBounds(GameObject model)
        {
            bool any = false;
            var bounds = new Bounds();
            var baked = new Mesh();
            try
            {
                foreach (SkinnedMeshRenderer skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skinned.BakeMesh(baked, true);
                    Transform space = skinned.transform;
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        // С useScale вершины уже в масштабе трансформа: остаются поворот и сдвиг.
                        Encapsulate(ref bounds, ref any, space.position + space.rotation * vertex);
                    }
                }

                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null)
                    {
                        continue;
                    }

                    foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    {
                        Encapsulate(ref bounds, ref any, filter.transform.TransformPoint(vertex));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }

            return bounds;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Vector3 point)
        {
            if (!any)
            {
                bounds = new Bounds(point, Vector3.zero);
                any = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        private static Rigidbody AddBody(Transform bone, float mass)
        {
            var body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.001f, mass);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Тело бывает кинематическим (у клиентов, таз при захвате): Speculative работает в обоих режимах.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = SolverIterations;
            body.solverVelocityIterations = SolverVelocityIterations;
            body.angularDamping = 0.5f;
            return body;
        }

        private static void AddArm(Transform[] bones, int upper, int lower, Transform hand, float radius, PhysicsMaterial physics, List<Collider> colliders)
        {
            Vector3 elbow = bones[lower].position;
            Vector3 wrist = hand.position;
            // Кисть — продолжение предплечья: ей бьют, поэтому капсула толще и длиннее запястья.
            Vector3 fist = wrist + (wrist - elbow).normalized * Mathf.Max(radius * 2f, (wrist - elbow).magnitude * 0.4f);
            colliders.Add(AddCapsule(bones[upper], bones[upper].position, elbow, radius, physics));
            colliders.Add(AddCapsule(bones[lower], elbow, fist, radius * 1.2f, physics));
        }

        private static void AddLeg(Transform[] bones, int upper, int lower, Transform foot, float radius, float clearance,
            PhysicsMaterial physics, List<Collider> colliders)
        {
            Vector3 knee = bones[lower].position;
            // Голень почти до подошвы: в позе стоя низ капсулы чуть выше пола.
            var sole = new Vector3(foot.position.x, radius + clearance, foot.position.z);
            colliders.Add(AddCapsule(bones[upper], bones[upper].position, knee, radius, physics));
            colliders.Add(AddCapsule(bones[lower], knee, sole, radius, physics));
        }

        private static Collider AddCapsule(Transform bone, Vector3 from, Vector3 to, float radius, PhysicsMaterial physics)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            var holder = new GameObject("Collider");
            holder.transform.SetParent(bone, false);
            holder.transform.SetPositionAndRotation((from + to) * 0.5f,
                length > 1e-5f ? Quaternion.FromToRotation(Vector3.up, axis / length) : Quaternion.identity);

            float scale = Mathf.Abs(holder.transform.lossyScale.x);
            var capsule = holder.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius / scale;
            capsule.height = (length + radius * 2f) / scale;
            capsule.sharedMaterial = physics;
            return capsule;
        }

        private static Collider AddSphere(Transform bone, Vector3 center, float radius, PhysicsMaterial physics)
        {
            var holder = new GameObject("Collider");
            holder.transform.SetParent(bone, false);
            holder.transform.position = center;
            var sphere = holder.AddComponent<SphereCollider>();
            sphere.radius = radius / Mathf.Abs(holder.transform.lossyScale.x);
            sphere.sharedMaterial = physics;
            return sphere;
        }

        private static ConfigurableJoint AddJoint(Transform bone, Rigidbody parent, Transform end, Vector2 limits)
        {
            var joint = bone.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = Vector3.zero;

            // Ось сустава — вдоль кости (скручивание), вторая — «вперёд» персонажа (отклонения).
            Vector3 along = end != null ? end.position - bone.position : Vector3.up;
            Vector3 axis = bone.InverseTransformDirection(along).normalized;
            Vector3 secondary = Vector3.ProjectOnPlane(bone.InverseTransformDirection(Vector3.forward), axis);
            if (secondary.sqrMagnitude < 1e-4f)
            {
                secondary = Vector3.ProjectOnPlane(bone.InverseTransformDirection(Vector3.up), axis);
            }

            joint.axis = axis;
            joint.secondaryAxis = secondary.normalized;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;

            float twist = Mathf.Clamp(limits.x, 0f, MaxSwingLimit);
            float swing = Mathf.Clamp(limits.y, 0f, MaxSwingLimit);
            joint.lowAngularXLimit = new SoftJointLimit { limit = -twist };
            joint.highAngularXLimit = new SoftJointLimit { limit = twist };
            joint.angularYLimit = new SoftJointLimit { limit = swing };
            joint.angularZLimit = new SoftJointLimit { limit = swing };

            // Силу приводу задаёт RagdollRig из конфига (привод в единицах ускорения).
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            // Препроцессинг суставов делает цепочки рэгдолла дёргаными.
            joint.enablePreprocessing = false;
            return joint;
        }

        private static Renderer[] SetupRenderers(GameObject model, Material material)
        {
            var renderers = new List<Renderer>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    // Кости убегают далеко от корня модели: границы должны следовать за ними, иначе меш пропадёт.
                    skinned.updateWhenOffscreen = true;
                }

                renderers.Add(renderer);
            }

            return renderers.ToArray();
        }

        private static float[] MassShares(CharacterProfile profile)
        {
            var shares = new float[RagdollBones.Count];
            shares[RagdollBones.Hips] = profile.HipsMass;
            shares[RagdollBones.Chest] = profile.ChestMass;
            shares[RagdollBones.Head] = profile.HeadMass;
            shares[RagdollBones.LeftUpperArm] = profile.UpperArmMass;
            shares[RagdollBones.RightUpperArm] = profile.UpperArmMass;
            shares[RagdollBones.LeftLowerArm] = profile.LowerArmMass;
            shares[RagdollBones.RightLowerArm] = profile.LowerArmMass;
            shares[RagdollBones.LeftUpperLeg] = profile.UpperLegMass;
            shares[RagdollBones.RightUpperLeg] = profile.UpperLegMass;
            shares[RagdollBones.LeftLowerLeg] = profile.LowerLegMass;
            shares[RagdollBones.RightLowerLeg] = profile.LowerLegMass;

            float total = 0f;
            for (int i = 0; i < shares.Length; i++)
            {
                total += shares[i];
            }

            for (int i = 0; i < shares.Length; i++)
            {
                shares[i] /= total;
            }

            return shares;
        }

        private static Vector2 Limits(CharacterProfile profile, int bone)
        {
            switch (bone)
            {
                case RagdollBones.Chest:
                    return profile.SpineLimits;
                case RagdollBones.Head:
                    return profile.HeadLimits;
                case RagdollBones.LeftUpperArm:
                case RagdollBones.RightUpperArm:
                    return profile.UpperArmLimits;
                case RagdollBones.LeftLowerArm:
                case RagdollBones.RightLowerArm:
                    return profile.LowerArmLimits;
                case RagdollBones.LeftUpperLeg:
                case RagdollBones.RightUpperLeg:
                    return profile.UpperLegLimits;
                default:
                    return profile.LowerLegLimits;
            }
        }
    }
}
