using UnityEngine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// “екстуры эффектов, нарисованные кодом.
    ///
    /// ћ€гкое п€тно и кольцо Ч это просто функци€
    /// от рассто€ни€ до центра. —качивать ради них
    /// ассеты незачем, а заменить своими картинками
    /// можно в любой момент: файл из Resources
    /// имеет приоритет.
    /// </summary>
    public static class FxTextures
    {
        private const int Size = 128;

        private static Texture2D _glow;
        private static Texture2D _ring;


        /// <summary>
        /// ћ€гкое свет€щеес€ п€тно дл€ снар€да.
        /// </summary>
        public static Texture2D Glow()
        {
            if (_glow != null)
                return _glow;


            _glow = Build(
                delegate (float distance)
                {
                    // ѕлотное €дро, м€гкий спад.
                    float alpha =
                        Mathf.Clamp01(1f - distance);

                    return alpha * alpha * alpha;
                }
            );

            return _glow;
        }


        /// <summary>
        ///  ольцо дл€ вспышки попадани€.
        /// </summary>
        public static Texture2D Ring()
        {
            if (_ring != null)
                return _ring;


            _ring = Build(
                delegate (float distance)
                {
                    // ярка€ полоса на радиусе 0.75.
                    float band =
                        Mathf.Abs(distance - 0.75f);

                    float alpha =
                        Mathf.Clamp01(
                            1f - band / 0.25f
                        );

                    return alpha * alpha;
                }
            );

            return _ring;
        }


        private delegate float Falloff(
            float distance);


        private static Texture2D Build(
            Falloff falloff)
        {
            var texture =
                new Texture2D(
                    Size,
                    Size,
                    TextureFormat.RGBA32,
                    false
                );

            texture.wrapMode =
                TextureWrapMode.Clamp;

            texture.filterMode =
                FilterMode.Bilinear;


            float center =
                (Size - 1) * 0.5f;

            var pixels =
                new Color32[Size * Size];


            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx =
                        (x - center) / center;

                    float dy =
                        (y - center) / center;

                    float distance =
                        Mathf.Sqrt(dx * dx + dy * dy);


                    float alpha =
                        distance > 1f
                            ? 0f
                            : Mathf.Clamp01(
                                falloff(distance)
                            );


                    pixels[y * Size + x] =
                        new Color32(
                            255,
                            255,
                            255,
                            (byte)(alpha * 255f)
                        );
                }
            }


            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }
    }
}