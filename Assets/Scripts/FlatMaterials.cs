using UnityEngine;
using UnityEngine.Rendering;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Материалы для плоских объектов: жетонов,
    /// граней кубика, коврика.
    ///
    /// Стандартный материал URP непрозрачный,
    /// поэтому картинки с вырезанным фоном
    /// показывались бы с чёрной подложкой.
    /// Здесь материал собирается один раз
    /// и переиспользуется всеми.
    ///
    /// Unlit выбран намеренно: жетоны и кубик
    /// не должны темнеть от освещения сцены.
    /// </summary>
    public static class FlatMaterials
    {
        private static Material _transparent;
        private static Material _opaque;


        public static Material Transparent()
        {
            if (_transparent != null)
                return _transparent;


            _transparent =
                Create();

            _transparent.SetFloat("_Surface", 1f);
            _transparent.SetFloat("_Blend", 0f);
            _transparent.SetFloat("_ZWrite", 0f);
            _transparent.SetFloat("_AlphaClip", 0f);

            _transparent.SetFloat(
                "_SrcBlend",
                (float)BlendMode.SrcAlpha
            );

            _transparent.SetFloat(
                "_DstBlend",
                (float)BlendMode.OneMinusSrcAlpha
            );

            _transparent.EnableKeyword(
                "_SURFACE_TYPE_TRANSPARENT"
            );

            _transparent.renderQueue =
                (int)RenderQueue.Transparent;

            return _transparent;
        }


        /// <summary>
        /// Прозрачный, но рисуется ПОСЛЕ обычного
        /// прозрачного.
        ///
        /// Жетоны урона и рамка подсветки были
        /// одним материалом, и порядок между ними
        /// выбирал движок — рамка, которая больше
        /// карты, закрывала жетоны. Отдельная
        /// очередь отрисовки решает это наверняка.
        /// </summary>
        public static Material TransparentOnTop()
        {
            if (_onTop != null)
                return _onTop;


            _onTop =
                new Material(Transparent());

            _onTop.renderQueue =
                (int)RenderQueue.Transparent + 50;

            return _onTop;
        }


        private static Material _onTop;


        public static Material Opaque()
        {
            if (_opaque == null)
                _opaque = Create();

            return _opaque;
        }


        private static Material Create()
        {
            var shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit"
                );

            if (shader == null)
            {
                // Проект не на URP — берём что есть.
                shader = Shader.Find("Unlit/Texture");
            }

            return new Material(shader);
        }
    }
}