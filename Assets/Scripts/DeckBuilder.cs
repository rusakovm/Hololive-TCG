using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Сборщик колод.
    ///
    /// Игрок выбирает ось, набирает пятьдесят карт
    /// и двадцать чиров, проверяет и сохраняет.
    /// Свои колоды лежат отдельным файлом и
    /// переживают обновления игры.
    ///
    /// Нарисовано встроенными средствами, как и
    /// остальной наш интерфейс: задача — работать,
    /// а не выглядеть.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class DeckBuilder : MonoBehaviour
    {
        [Tooltip("Открыть сборщик при запуске")]
        public bool OpenAtStart;

        [Tooltip("Ширина окна")]
        public float Width = 980f;

        [Tooltip("Высота окна")]
        public float Height = 640f;


        private GameSession _session;

        private bool _open;


        /// <summary>
        /// Сборщик открыт.
        ///
        /// Другие окна на это смотрят и не рисуются
        /// поверх: иначе кнопки меню оказываются
        /// сверху списка карт.
        /// </summary>
        public bool IsOpen
        {
            get { return _open; }
        }

        private DeckDefinition _deck;

        private string _search = string.Empty;
        private CardColor? _colorFilter;
        private string _setFilter;
        private CardType? _typeFilter;

        [Tooltip("Ширина карточки в списке")]
        public float CardWidth = 118f;

        [Tooltip("Отношение высоты к ширине")]
        public float CardAspect = 1.4f;

        private Vector2 _listScroll;
        private Vector2 _deckScroll;

        private string _status = string.Empty;
        private string _hovered = string.Empty;


        // Что показано на экране прямо сейчас.
        //
        // Пролистав всю базу, сборщик тянул в память
        // все триста с лишним картинок. Держим
        // только видимые и то, что в колоде.
        private readonly HashSet<string> _onScreen =
            new HashSet<string>();

        private int _lastCleanup;


        // Отбор пересчитывается только при смене
        // фильтров. Раньше он шёл каждый кадр по
        // всей базе — отсюда и тормоза.
        private List<CardDefinition> _filtered;
        private string _filterKey;

        private List<string> _sets;

        private List<DeckDefinition> _custom =
            new List<DeckDefinition>();


        /// <summary>
        /// Где лежат свои колоды.
        /// </summary>
        public string StoragePath
        {
            get
            {
                return System.IO.Path.Combine(
                    Application.persistentDataPath,
                    "my-decks.json"
                );
            }
        }


        private void Awake()
        {
            _session = GetComponent<GameSession>();
        }


        private void Start()
        {
            LoadCustomDecks();

            if (OpenAtStart)
                Open();
        }


        /// <summary>
        /// Подмешать свои колоды к поставляемым.
        ///
        /// Делается один раз при запуске: дальше
        /// они ничем не отличаются от обычных
        /// и доступны в игре и боту.
        /// </summary>
        public void LoadCustomDecks()
        {
            if (_session == null ||
                _session.Decks == null)
            {
                return;
            }


            _custom =
                DeckStorage.Load(StoragePath);


            foreach (var deck in _custom)
                _session.Decks.AddOrReplace(deck);


            if (_custom.Count > 0)
            {
                Debug.Log(
                    "Своих колод загружено: " +
                    _custom.Count
                );
            }
        }


        [ContextMenu("Открыть сборщик колод")]
        public void Open()
        {
            _open = true;

            if (_deck == null)
                NewDeck();
        }


        public void Close()
        {
            _open = false;
        }


        private void NewDeck()
        {
            _deck =
                new DeckDefinition
                {
                    Id =
                        DeckStorage.NewId(_custom),

                    Name = "Моя колода",

                    OshiCardId =
                        _session.Oshi.OshiCards.Count > 0
                            ? _session.Oshi.OshiCards[0].Id
                            : string.Empty
                };


            _status = "новая колода";
        }


        // =========================================
        // ПОДСЧЁТЫ
        // =========================================

        private int MainCount
        {
            get
            {
                return _deck == null
                    ? 0
                    : _deck.MainDeck.Sum(x => x.Count);
            }
        }


        private int CheerCount
        {
            get
            {
                return _deck == null
                    ? 0
                    : _deck.CheerDeck.Sum(x => x.Count);
            }
        }


        private int CountOf(
            string cardId)
        {
            var entry =
                _deck.MainDeck
                    .FirstOrDefault(x => x.CardId == cardId);

            return entry == null ? 0 : entry.Count;
        }


        /// <summary>
        /// Сколько копий этой карты разрешено.
        /// Ноль в данных означает "сколько угодно".
        /// </summary>
        private int LimitOf(
            CardDefinition card)
        {
            // Соглашение движка: −1 значит
            // "сколько угодно", 0 — обычный предел
            // из правил колоды, больше нуля —
            // собственный предел карты.
            //
            // Раньше сборщик понимал ноль как
            // "без ограничения" и расходился
            // с проверкой колод: он разрешал шесть
            // копий, а проверка потом их отвергала.
            if (card.MaxDeckCopies < 0)
                return 99;

            return card.MaxDeckCopies > 0
                ? card.MaxDeckCopies
                : 4;
        }


        private void Add(
            CardDefinition card)
        {
            int have =
                CountOf(card.Id);

            if (have >= LimitOf(card))
            {
                _status =
                    card.Name +
                    ": больше копий нельзя";

                return;
            }


            var entry =
                _deck.MainDeck
                    .FirstOrDefault(x => x.CardId == card.Id);

            if (entry == null)
            {
                _deck.MainDeck.Add(
                    new DeckCardEntry
                    {
                        CardId = card.Id,
                        Count = 1
                    }
                );
            }
            else
            {
                entry.Count++;
            }


            _status = string.Empty;
        }


        private void Remove(
            string cardId)
        {
            var entry =
                _deck.MainDeck
                    .FirstOrDefault(x => x.CardId == cardId);

            if (entry == null)
                return;


            entry.Count--;

            if (entry.Count <= 0)
                _deck.MainDeck.Remove(entry);
        }


        private void ChangeCheer(
            CardColor color,
            int delta)
        {
            var entry =
                _deck.CheerDeck
                    .FirstOrDefault(x => x.Color == color);

            if (entry == null)
            {
                if (delta <= 0)
                    return;


                _deck.CheerDeck.Add(
                    new CheerDeckEntry
                    {
                        Color = color,
                        Count = delta
                    }
                );

                return;
            }


            entry.Count += delta;

            if (entry.Count <= 0)
                _deck.CheerDeck.Remove(entry);
        }


        // =========================================
        // ЭКРАН
        // =========================================

        private void OnGUI()
        {
            if (!_open)
                return;


            var area =
                new Rect(
                    (Screen.width - Width) * 0.5f,
                    (Screen.height - Height) * 0.5f,
                    Width,
                    Height
                );

            _hovered = string.Empty;

            _onScreen.Clear();

            GUILayout.BeginArea(area, GUI.skin.box);

            DrawHeader();

            GUILayout.BeginHorizontal();

            DrawCardList();
            DrawDeck();

            GUILayout.EndHorizontal();

            DrawFooter();

            GUILayout.EndArea();

            Cleanup();
        }


        /// <summary>
        /// Выгрузить картинки, уехавшие за край
        /// прокрутки.
        ///
        /// Раз в несколько кадров: чаще незачем,
        /// а выгрузка сама по себе не бесплатна.
        /// </summary>
        private void Cleanup()
        {
            if (Time.frameCount - _lastCleanup < 30)
                return;

            _lastCleanup = Time.frameCount;


            if (_onScreen.Count == 0)
                return;


            CardArt.KeepOnly(_onScreen);
        }


        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label("Сборщик колод",
                GUILayout.Width(130f));

            GUILayout.Label("Название",
                GUILayout.Width(70f));

            _deck.Name =
                GUILayout.TextField(
                    _deck.Name,
                    GUILayout.Width(200f));


            GUILayout.Label("Ось",
                GUILayout.Width(30f));

            DrawOshiPicker();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Закрыть",
                GUILayout.Width(90f)))
            {
                Close();
            }

            GUILayout.EndHorizontal();


            GUILayout.BeginHorizontal();

            GUILayout.Label("Поиск", GUILayout.Width(45f));

            _search =
                GUILayout.TextField(
                    _search,
                    GUILayout.Width(200f));


            DrawColorFilter();

            GUILayout.FlexibleSpace();

            GUILayout.Label(
                "Колода " + MainCount + "/50   " +
                "Cheer " + CheerCount + "/20"
            );

            GUILayout.EndHorizontal();


            GUILayout.BeginHorizontal();

            DrawSetFilter();

            GUILayout.Space(12f);

            DrawTypeFilter();

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
        }


        private void DrawOshiPicker()
        {
            var all =
                _session.Oshi.OshiCards;

            int index = 0;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == _deck.OshiCardId)
                {
                    index = i;
                    break;
                }
            }


            string label =
                all.Count == 0
                    ? "нет осей"
                    : all[index].Name;


            if (GUILayout.Button(label,
                GUILayout.Width(190f)) &&
                all.Count > 0)
            {
                // Перебор по кругу: список короткий,
                // выпадающего меню тут не нужно.
                index =
                    (index + 1) % all.Count;

                _deck.OshiCardId =
                    all[index].Id;
            }
        }


        private void DrawColorFilter()
        {
            var colors =
                new CardColor?[]
                {
                    null,
                    CardColor.White,
                    CardColor.Green,
                    CardColor.Red,
                    CardColor.Blue,
                    CardColor.Purple,
                    CardColor.Yellow,
                    CardColor.Neutral
                };


            foreach (var color in colors)
            {
                string label =
                    color == null
                        ? "все"
                        : color.ToString().Substring(0, 3);


                bool active =
                    _colorFilter.HasValue == color.HasValue &&
                    (!color.HasValue ||
                     _colorFilter.Value == color.Value);


                GUI.color =
                    active ? Color.cyan : Color.white;


                if (GUILayout.Button(label,
                    GUILayout.Width(44f)))
                {
                    _colorFilter = color;
                }
            }


            GUI.color = Color.white;
        }


        /// <summary>
        /// Наборы, из которых есть карты.
        ///
        /// Список собирается по номерам карт,
        /// поэтому новый набор появится здесь сам.
        /// </summary>
        private List<string> Sets()
        {
            if (_sets != null)
                return _sets;


            var sets =
                new List<string>();


            foreach (var card in _session.Cards.Cards)
            {
                int dash =
                    card.Id.IndexOf('-');

                if (dash <= 0)
                    continue;


                string set =
                    card.Id.Substring(0, dash);

                if (!sets.Contains(set))
                    sets.Add(set);
            }


            sets.Sort();

            _sets = sets;

            return sets;
        }


        private void DrawSetFilter()
        {
            GUI.color =
                _setFilter == null
                    ? Color.cyan
                    : Color.white;

            if (GUILayout.Button("все наборы",
                GUILayout.Width(90f)))
            {
                _setFilter = null;
            }


            foreach (var set in Sets())
            {
                GUI.color =
                    _setFilter == set
                        ? Color.cyan
                        : Color.white;

                if (GUILayout.Button(set,
                    GUILayout.Width(62f)))
                {
                    _setFilter = set;
                }
            }


            GUI.color = Color.white;
        }


        private void DrawTypeFilter()
        {
            var kinds =
                new CardType?[]
                {
                    null,
                    CardType.Holomem,
                    CardType.Support
                };

            var labels =
                new[] { "всё", "холомемы", "саппорты" };


            for (int i = 0; i < kinds.Length; i++)
            {
                bool active =
                    _typeFilter.HasValue == kinds[i].HasValue &&
                    (!kinds[i].HasValue ||
                     _typeFilter.Value == kinds[i].Value);


                GUI.color =
                    active ? Color.cyan : Color.white;

                if (GUILayout.Button(labels[i],
                    GUILayout.Width(80f)))
                {
                    _typeFilter = kinds[i];
                }
            }


            GUI.color = Color.white;
        }


        /// <summary>
        /// Сетка карточек с картинками.
        ///
        /// Карту проще узнать в лицо, чем по номеру,
        /// поэтому список сделан плиткой. Левый
        /// щелчок добавляет, правый убирает —
        /// так набор идёт быстрее, чем кнопками.
        /// </summary>
        private void DrawCardList()
        {
            float paneWidth =
                Width * 0.62f;

            GUILayout.BeginVertical(
                GUILayout.Width(paneWidth));

            float viewHeight =
                Height * 0.72f;

            _listScroll =
                GUILayout.BeginScrollView(
                    _listScroll,
                    GUILayout.Height(viewHeight));


            int columns =
                Mathf.Max(
                    1,
                    Mathf.FloorToInt(
                        (paneWidth - 30f) /
                        (CardWidth + 6f))
                );


            var cards =
                FilteredCached();

            int rows =
                (cards.Count + columns - 1) / columns;


            // Высота ряда вместе с отступами,
            // которые добавляет раскладка.
            float rowHeight =
                CardWidth * CardAspect + 6f;


            // Рисуем только ряды, попадающие в окно.
            // Остальные заменяем пустым местом той же
            // высоты, чтобы полоса прокрутки
            // оставалась верной.
            int first =
                Mathf.Max(
                    0,
                    Mathf.FloorToInt(
                        _listScroll.y / rowHeight) - 1);

            int last =
                Mathf.Min(
                    rows,
                    first +
                    Mathf.CeilToInt(
                        viewHeight / rowHeight) + 3);


            GUILayout.Space(first * rowHeight);


            for (int row = first; row < last; row++)
            {
                GUILayout.BeginHorizontal();

                for (int col = 0; col < columns; col++)
                {
                    int index =
                        row * columns + col;

                    if (index >= cards.Count)
                        break;

                    DrawCardTile(cards[index]);
                }

                GUILayout.EndHorizontal();
            }


            GUILayout.Space((rows - last) * rowHeight);


            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }


        private void DrawCardTile(
            CardDefinition card)
        {
            float height =
                CardWidth * CardAspect;

            var rect =
                GUILayoutUtility.GetRect(
                    CardWidth,
                    height,
                    GUILayout.Width(CardWidth),
                    GUILayout.Height(height)
                );


            _onScreen.Add(card.Id);


            var texture =
                CardArt.ById(card.Id);


            if (texture != null)
            {
                GUI.DrawTexture(
                    rect,
                    texture,
                    ScaleMode.ScaleToFit
                );
            }
            else
            {
                // Картинки может не быть — тогда
                // показываем хотя бы цвет и имя,
                // чтобы карту можно было выбрать.
                GUI.color =
                    CardArt.PlaceholderColor(card.Color);

                GUI.DrawTexture(rect, Texture2D.whiteTexture);

                GUI.color = Color.white;

                GUI.Label(
                    rect,
                    card.Name + "\n" + card.Id
                );
            }


            int have =
                CountOf(card.Id);


            // Сколько копий уже взято — крупно
            // в углу, чтобы видеть на глаз.
            if (have > 0)
            {
                var badge =
                    new Rect(rect.x + 4f, rect.y + 4f,
                             34f, 24f);

                GUI.color =
                    new Color(0f, 0f, 0f, 0.75f);

                GUI.DrawTexture(badge, Texture2D.whiteTexture);

                GUI.color = Color.white;

                GUI.Label(badge, "  " + have);
            }


            if (Event.current.type == EventType.MouseDown &&
                rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0)
                    Add(card);
                else if (Event.current.button == 1)
                    Remove(card.Id);


                Event.current.Use();
            }


            if (rect.Contains(Event.current.mousePosition))
            {
                _hovered =
                    card.Name +
                    "  [" + card.Id + "]  " +
                    Describe(card);
            }
        }


        private void DrawDeckTile(
            DeckCardEntry entry,
            float width)
        {
            float height =
                width * CardAspect;

            var rect =
                GUILayoutUtility.GetRect(
                    width, height,
                    GUILayout.Width(width),
                    GUILayout.Height(height));


            _onScreen.Add(entry.CardId);


            var texture =
                CardArt.ById(entry.CardId);

            var card =
                _session.Cards.GetById(entry.CardId);


            if (texture != null)
            {
                GUI.DrawTexture(
                    rect, texture,
                    ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.color =
                    card == null
                        ? Color.grey
                        : CardArt.PlaceholderColor(card.Color);

                GUI.DrawTexture(rect, Texture2D.whiteTexture);

                GUI.color = Color.white;

                GUI.Label(
                    rect,
                    card == null ? entry.CardId : card.Name);
            }


            // Число копий на плитке.
            var badge =
                new Rect(rect.x + 2f, rect.y + 2f,
                         30f, 20f);

            GUI.color = new Color(0f, 0f, 0f, 0.8f);

            GUI.DrawTexture(badge, Texture2D.whiteTexture);

            GUI.color = Color.white;

            GUI.Label(badge, " x" + entry.Count);


            if (Event.current.type == EventType.MouseDown &&
                rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    Remove(entry.CardId);
                else if (Event.current.button == 0 &&
                         card != null)
                    Add(card);

                Event.current.Use();
            }


            if (rect.Contains(Event.current.mousePosition) &&
                card != null)
            {
                _hovered =
                    card.Name + "  [" + card.Id + "]  " +
                    Describe(card);
            }
        }


        private string Describe(
            CardDefinition card)
        {
            if (card.Type == CardType.Support)
            {
                return card.SupportType +
                    (card.IsLimited ? " LIMITED" : "");
            }


            return card.Color +
                " " + card.Level +
                " HP" + card.Hp;
        }


        /// <summary>
        /// Отобранные карты с кешем.
        ///
        /// Ключ склеен из всех фильтров: пока он
        /// не меняется, отбор не пересчитывается.
        /// </summary>
        private List<CardDefinition> FilteredCached()
        {
            string key =
                (_search ?? string.Empty) + "|" +
                _colorFilter + "|" +
                _typeFilter + "|" +
                _setFilter;


            if (_filtered == null || key != _filterKey)
            {
                _filtered = Filtered().ToList();
                _filterKey = key;
            }


            return _filtered;
        }


        private IEnumerable<CardDefinition> Filtered()
        {
            string needle =
                (_search ?? string.Empty).Trim().ToLower();


            foreach (var card in _session.Cards.Cards)
            {
                if (card.Type == CardType.Cheer)
                    continue;


                if (_colorFilter.HasValue &&
                    card.Color != _colorFilter.Value)
                {
                    continue;
                }


                if (_typeFilter.HasValue &&
                    card.Type != _typeFilter.Value)
                {
                    continue;
                }


                if (_setFilter != null &&
                    !card.Id.StartsWith(_setFilter))
                {
                    continue;
                }


                if (needle.Length > 0 &&
                    !card.Name.ToLower().Contains(needle) &&
                    !card.Id.ToLower().Contains(needle))
                {
                    continue;
                }


                yield return card;
            }
        }


        private void DrawDeck()
        {
            GUILayout.BeginVertical();

            GUILayout.Label("В колоде  (правый щелчок — убрать):");

            _deckScroll =
                GUILayout.BeginScrollView(
                    _deckScroll,
                    GUILayout.Height(Height * 0.45f));


            // Взятые карты — тоже картинками,
            // но помельче: здесь их надо окинуть
            // взглядом, а не разглядывать.
            float small =
                CardWidth * 0.62f;

            int perRow =
                Mathf.Max(
                    1,
                    Mathf.FloorToInt(
                        (Width * 0.34f) / (small + 4f)));


            var entries =
                _deck.MainDeck.ToList();

            int inRow = 0;


            foreach (var entry in entries)
            {
                if (inRow == 0)
                    GUILayout.BeginHorizontal();


                DrawDeckTile(entry, small);

                inRow++;


                if (inRow >= perRow)
                {
                    GUILayout.EndHorizontal();
                    inRow = 0;
                }
            }


            if (inRow > 0)
                GUILayout.EndHorizontal();


            GUILayout.EndScrollView();


            GUILayout.Space(6f);
            GUILayout.Label("Cheer-колода:");


            foreach (var color in new[]
                {
                    CardColor.White, CardColor.Green,
                    CardColor.Red, CardColor.Blue,
                    CardColor.Purple, CardColor.Yellow
                })
            {
                var entry =
                    _deck.CheerDeck
                        .FirstOrDefault(x => x.Color == color);

                GUILayout.BeginHorizontal();

                GUILayout.Label(
                    color.ToString(),
                    GUILayout.Width(70f));

                GUILayout.Label(
                    (entry == null ? 0 : entry.Count)
                        .ToString(),
                    GUILayout.Width(30f));


                if (GUILayout.Button("+",
                    GUILayout.Width(26f)))
                {
                    ChangeCheer(color, 1);
                }

                if (GUILayout.Button("−",
                    GUILayout.Width(26f)))
                {
                    ChangeCheer(color, -1);
                }

                GUILayout.EndHorizontal();
            }


            GUILayout.EndVertical();
        }


        private void DrawFooter()
        {
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();


            if (GUILayout.Button("Проверить",
                GUILayout.Width(110f)))
            {
                Check();
            }


            if (GUILayout.Button("Сохранить",
                GUILayout.Width(110f)))
            {
                Save();
            }


            if (GUILayout.Button("Новая",
                GUILayout.Width(90f)))
            {
                NewDeck();
            }


            DrawLoadButtons();

            GUILayout.EndHorizontal();


            GUILayout.Label(
                _hovered.Length > 0
                    ? _hovered
                    : _status
            );
        }


        private void DrawLoadButtons()
        {
            if (_custom.Count == 0)
                return;


            GUILayout.Label("Свои:",
                GUILayout.Width(45f));


            foreach (var deck in _custom.ToList())
            {
                if (GUILayout.Button(
                    deck.Name,
                    GUILayout.Width(130f)))
                {
                    _deck = Copy(deck);

                    _status =
                        "загружена " + deck.Name;
                }
            }
        }


        /// <summary>
        /// Копия колоды для правки.
        ///
        /// Без неё правка меняла бы сохранённую
        /// колоду ещё до нажатия "Сохранить".
        /// </summary>
        private DeckDefinition Copy(
            DeckDefinition source)
        {
            return new DeckDefinition
            {
                Id = source.Id,
                Name = source.Name,
                OshiCardId = source.OshiCardId,

                MainDeck =
                    source.MainDeck
                        .Select(x => new DeckCardEntry
                        {
                            CardId = x.CardId,
                            Count = x.Count
                        })
                        .ToList(),

                CheerDeck =
                    source.CheerDeck
                        .Select(x => new CheerDeckEntry
                        {
                            Color = x.Color,
                            Count = x.Count
                        })
                        .ToList()
            };
        }


        // =========================================
        // ПРОВЕРКА И СОХРАНЕНИЕ
        // =========================================

        private bool Check()
        {
            var result =
                _session.Decks.ValidateDefinition(_deck);


            if (result.IsValid)
            {
                _status = "колода в порядке";
                return true;
            }


            _status =
                "не годится: " +
                string.Join("; ", result.Errors.ToArray());

            return false;
        }


        private void Save()
        {
            // Негодную колоду сохранять можно:
            // игрок мог остановиться на середине.
            // Но он должен видеть, что она негодна.
            Check();


            var stored =
                _custom.FirstOrDefault(
                    x => x.Id == _deck.Id);

            if (stored != null)
                _custom.Remove(stored);


            _custom.Add(Copy(_deck));


            if (!DeckStorage.Save(StoragePath, _custom))
            {
                _status = "не удалось сохранить файл";
                return;
            }


            _session.Decks.AddOrReplace(Copy(_deck));


            // Только что собранной колодой обычно
            // и хотят сыграть — выбираем её сразу.
            _session.PlayerDeckId = _deck.Id;


            _status =
                "сохранено и выбрано для игры: " +
                _deck.Name;
        }
    }
}