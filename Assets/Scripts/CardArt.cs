using System.Collections.Generic;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Находит картинку карты по её номеру.
    ///
    /// Файлы лежат в Assets/Resources/Cards/
    /// и называются точно как номер карты:
    ///   hSD01-011.png
    ///   cheer-White.png
    ///   card-back.png
    ///
    /// Загруженное кешируется: одна текстура
    /// на все копии карты в партии.
    /// </summary>
    public static class CardArt
    {
        private const string Folder = "Cards/";

        private const string BackName = "card-back";


        private static readonly Dictionary<string, Texture2D> _cache =
            new Dictionary<string, Texture2D>();


        // Чтобы не засорять консоль одной и той же
        // жалобой на каждую копию карты.
        private static readonly HashSet<string> _reported =
            new HashSet<string>();


        // Когда картинку показывали в последний раз.
        //
        // Картинка карты 1200x1600 занимает в памяти
        // видеокарты около восьми мегабайт. Триста
        // с лишним карт — это почти три гигабайта,
        // и именно столько сборщик колод и съедал,
        // пролистанный до конца. Поэтому давно
        // не нужные выгружаем.
        private static readonly Dictionary<string, int> _touched =
            new Dictionary<string, int>();


        /// <summary>
        /// Сколько картинок держать в памяти.
        ///
        /// Партии хватает с запасом: на столе и в двух
        /// колодах редко больше сотни разных карт.
        /// </summary>
        public static int Budget = 140;


        public static Texture2D Front(
            Card card)
        {
            if (card == null)
                return null;

            return Load(card.DefinitionId);
        }


        /// <summary>
        /// Картинка по номеру карты.
        ///
        /// Нужна сборщику колод: там карт ещё нет
        /// на столе, есть только описания из базы.
        /// </summary>
        public static Texture2D ById(
            string definitionId)
        {
            return Load(definitionId);
        }


        /// <summary>
        /// Применить фильтрацию к уже загруженным
        /// картинкам — чтобы сравнивать способы
        /// не перезапуская игру.
        /// </summary>
        public static void RefreshFiltering()
        {
            foreach (var texture in _cache.Values)
            {
                if (texture == null)
                    continue;


                texture.filterMode = Filter;
                texture.anisoLevel = AnisoLevel;
                texture.mipMapBias = MipBias;
            }
        }


        /// <summary>
        /// Что на самом деле с картинками карт.
        ///
        /// Настройки импорта видны только здесь:
        /// сжатие, размер и наличие мип-карт
        /// задаются при импорте и в игре
        /// не меняются никакими полями.
        /// </summary>
        public static string Report()
        {
            if (_cache.Count == 0)
                return "Картинки карт ещё не загружены.";


            var text =
                new System.Text.StringBuilder();

            text.AppendLine(
                "Картинок в памяти: " + _cache.Count +
                " (предел " + Budget + ")");


            int shown = 0;

            foreach (var pair in _cache)
            {
                var t = pair.Value;

                if (t == null)
                    continue;


                text.AppendLine(
                    pair.Key + ": " +
                    t.width + "x" + t.height +
                    ", " + t.format +
                    ", мип-карт " + t.mipmapCount +
                    ", фильтр " + t.filterMode +
                    ", анизо " + t.anisoLevel);


                if (++shown >= 4)
                    break;
            }


            text.AppendLine();
            text.AppendLine(
                "Мип-карт 1 — значит они выключены " +
                "в настройках импорта: отсюда рябь " +
                "на уменьшенных картах.");

            text.AppendLine(
                "Формат вида DXT/BC — картинка сжата, " +
                "мелкий текст от этого страдает " +
                "в первую очередь.");

            return text.ToString();
        }


        /// <summary>
        /// Выгрузить давно не нужные картинки.
        ///
        /// Самые старые по последнему показу уходят
        /// первыми: то, что сейчас на столе,
        /// трогается каждый кадр и не пострадает.
        /// </summary>
        private static void Trim()
        {
            if (Budget <= 0 || _cache.Count <= Budget)
                return;


            var order =
                new List<KeyValuePair<string, int>>(_touched);

            order.Sort(
                (a, b) => a.Value.CompareTo(b.Value));


            int extra =
                _cache.Count - Budget;


            foreach (var pair in order)
            {
                if (extra <= 0)
                    break;


                Texture2D texture;

                if (!_cache.TryGetValue(pair.Key, out texture))
                    continue;


                // Рубашку не трогаем: она нужна
                // всегда и всем.
                if (pair.Key == BackName)
                    continue;


                _cache.Remove(pair.Key);
                _touched.Remove(pair.Key);

                if (texture != null)
                    Resources.UnloadAsset(texture);

                extra--;
            }
        }


        /// <summary>
        /// Оставить в памяти только перечисленные
        /// картинки. Нужно сборщику колод: там
        /// пролистывается вся база, а видно от силы
        /// два десятка карт.
        /// </summary>
        public static void KeepOnly(
            ICollection<string> keys)
        {
            var doomed =
                new List<string>();


            foreach (var pair in _cache)
            {
                if (pair.Key != BackName &&
                    !keys.Contains(pair.Key))
                {
                    doomed.Add(pair.Key);
                }
            }


            foreach (var key in doomed)
            {
                Texture2D texture =
                    _cache[key];

                _cache.Remove(key);
                _touched.Remove(key);

                if (texture != null)
                    Resources.UnloadAsset(texture);
            }
        }


        public static Texture2D Back()
        {
            return Load(BackName);
        }


        /// <summary>
        /// Степень анизотропной фильтрации: 0 — нет,
        /// 16 — максимум. На картах под углом
        /// разница видна сразу.
        ///
        /// Работает только если в настройках качества
        /// Unity анизотропия не выключена совсем.
        /// </summary>
        public static int AnisoLevel = 9;


        /// <summary>
        /// Способ фильтрации картинок карт.
        ///
        /// Point — самый резкий, но только когда
        /// карта показана почти в натуральную
        /// величину и лицом к камере. На столе,
        /// где карта уменьшена и повёрнута,
        /// он даёт рябь на мелких деталях.
        ///
        /// Меняется на лету: можно сравнить
        /// в одной партии.
        /// </summary>
        public static FilterMode Filter =
            FilterMode.Trilinear;


        /// <summary>
        /// Смещение уровня детализации.
        ///
        /// Отрицательное значение заставляет движок
        /// брать копию крупнее, чем он считает
        /// нужным: текст на уменьшенной карте
        /// становится резче — но именно этим
        /// и вызывается рябь на мелких деталях.
        ///
        /// Рябит — поставьте 0. Слишком мутно —
        /// уводите в минус, но не дальше -0.5.
        /// </summary>
        public static float MipBias = -0.25f;


        private static Texture2D Load(
            string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;


            Texture2D cached;

            if (_cache.TryGetValue(key, out cached))
            {
                _touched[key] = Time.frameCount;

                return cached;
            }


            var texture =
                Resources.Load<Texture2D>(
                    Folder + key
                );


            // Карта на столе лежит под углом,
            // и именно в этом случае обычная
            // фильтрация мылит сильнее всего.
            // Анизотропная снимает мыло, трилинейная
            // убирает ступеньку между уровнями
            // детализации при отъезде камеры.
            //
            // Всё это — то же самое, что галочки
            // в настройках импорта текстуры,
            // только не надо трогать каждую
            // из сотен картинок руками.
            if (texture != null)
            {
                texture.anisoLevel = AnisoLevel;

                texture.filterMode = Filter;

                texture.wrapMode =
                    TextureWrapMode.Clamp;

                texture.mipMapBias = MipBias;
            }


            if (texture == null)
            {
                // Пропуск не кешируем: картинку могли
                // положить в папку уже после запуска,
                // и она должна подхватиться сама.
                if (!_reported.Contains(key))
                {
                    _reported.Add(key);

                    Debug.LogWarning(
                        "Нет картинки карты: ожидается файл " +
                        "Assets/Resources/" + Folder + key +
                        " (без суффиксов редкости в имени, " +
                        "Texture Type должен быть Default)"
                    );
                }

                return null;
            }


            _cache[key] = texture;

            _touched[key] = Time.frameCount;

            Trim();

            return texture;
        }


        /// <summary>
        /// Цвет-заглушка, пока картинки нет.
        /// </summary>
        /// <summary>
        /// Цвет-заглушка прямо по цвету карты.
        /// Нужен сборщику: там есть описание,
        /// но нет экземпляра карты.
        /// </summary>
        public static Color PlaceholderColor(
            CardColor color)
        {
            return ColorOf(color);
        }


        public static Color PlaceholderColor(
            Card card)
        {
            if (card == null)
                return Color.grey;


            return ColorOf(card.Color);
        }


        private static Color ColorOf(
            CardColor color)
        {
            switch (color)
            {
                case CardColor.White: return new Color(0.93f, 0.93f, 0.90f);
                case CardColor.Green: return new Color(0.40f, 0.72f, 0.42f);
                case CardColor.Red: return new Color(0.82f, 0.33f, 0.33f);
                case CardColor.Blue: return new Color(0.35f, 0.55f, 0.85f);
                case CardColor.Purple: return new Color(0.58f, 0.42f, 0.78f);
                case CardColor.Yellow: return new Color(0.90f, 0.80f, 0.35f);
                default: return new Color(0.70f, 0.70f, 0.70f);
            }
        }
    }
}