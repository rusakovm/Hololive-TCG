using System.Collections.Generic;
using System.IO;

using UnityEngine;

using Newtonsoft.Json;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Русские тексты карт.
    ///
    /// Лежат отдельным файлом cards.ru.json
    /// рядом с базой карт: номер карты → имя
    /// способности → перевод. Движок их не видит —
    /// правила записаны отдельно, а текст нужен
    /// только чтобы показать игроку. Поэтому перевод
    /// ничего не может сломать.
    ///
    /// Нет перевода — показывается оригинал.
    /// </summary>
    public static class CardTranslations
    {
        private static Dictionary<string,
            Dictionary<string, string>> _table;

        private static bool _loaded;


        /// <summary>
        /// Показывать ли переводы. Можно выключить,
        /// чтобы сверить с оригиналом.
        /// </summary>
        public static bool Enabled = true;


        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;


            string path =
                Path.Combine(
                    Application.streamingAssetsPath,
                    "cards.ru.json");


            if (!File.Exists(path))
            {
                _table =
                    new Dictionary<string,
                        Dictionary<string, string>>();

                return;
            }


            try
            {
                _table =
                    JsonConvert.DeserializeObject<
                        Dictionary<string,
                            Dictionary<string, string>>>(
                        File.ReadAllText(path))
                    ?? new Dictionary<string,
                        Dictionary<string, string>>();

                Debug.Log(
                    "Переводы карт загружены: " +
                    _table.Count);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning(
                    "Не удалось прочитать переводы: " +
                    error.Message);

                _table =
                    new Dictionary<string,
                        Dictionary<string, string>>();
            }
        }


        /// <summary>
        /// Текст способности по-русски, если есть.
        /// </summary>
        public static string Ability(
            string cardId,
            string abilityName,
            string original)
        {
            if (!Enabled ||
                string.IsNullOrEmpty(cardId) ||
                string.IsNullOrEmpty(abilityName))
            {
                return original;
            }


            EnsureLoaded();


            Dictionary<string, string> card;

            if (!_table.TryGetValue(cardId, out card))
                return original;


            string text;

            return card.TryGetValue(abilityName, out text) &&
                   !string.IsNullOrEmpty(text)
                ? text
                : original;
        }
    }
}