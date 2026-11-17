using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Внешний вид персонажа: цвет слота на мешах модели. Движение и позы — это физические кости
    /// (<see cref="RagdollRig"/>, <see cref="ProceduralPose"/>), отдельного визуального слоя у персонажа нет.
    /// </summary>
    public sealed class CharacterPresentation : MonoBehaviour
    {
        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_colorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer[] _renderers = new Renderer[0];

        private MaterialPropertyBlock _propertyBlock;

        public void SetColor(Color color)
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer target = _renderers[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(s_baseColorId, color);
                _propertyBlock.SetColor(s_colorId, color);
                target.SetPropertyBlock(_propertyBlock);
            }
        }

#if UNITY_EDITOR
        /// <summary>Только для генератора префаба.</summary>
        public void EditorSetup(Renderer[] renderers)
        {
            _renderers = renderers;
        }
#endif
    }
}
