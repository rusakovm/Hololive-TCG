using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Экран выбора: играть с ботом, создать
    /// игру или подключиться к чужой.
    ///
    /// Показывается до начала партии. Как только
    /// режим выбран, GameSession поднимает нужную
    /// связь, и меню исчезает.
    ///
    /// Отдельный компонент, а не часть сессии:
    /// сессия не должна ничего знать про экраны.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class NetworkMenu : MonoBehaviour
    {
        [Tooltip("Показывать меню при запуске. " +
                 "Выключите, если хотите сразу " +
                 "играть в режиме из GameSession")]
        public bool ShowAtStart = true;

        [Tooltip("Ширина окна меню")]
        public float Width = 380f;


        private bool _open;
        private GameSession _session;

        private string _address = "127.0.0.1";
        private string _port = "7777";

        private string _roomServer = "127.0.0.1";
        private string _roomCode = "";

        private string _relayCode = "";
        private string _hint = string.Empty;


        // Выбор колоды перед сетевой партией.
        //
        // В сети колоду надо выбрать до того, как
        // соединение установлено: после этого партия
        // уже собрана, и менять её поздно.
        private GameSession.PlayMode? _awaitingDeck;

        private Vector2 _deckScroll;
        private Vector2 _menuScroll;

        private Vector2 _scroll;


        private void Awake()
        {
            _session = GetComponent<GameSession>();

            _open = ShowAtStart;


            if (_open)
            {
                // Пока игрок не выбрал, сессия
                // не должна ничего поднимать.
                _session.AutoStart = false;
            }


            _address = _session.HostAddress;
            _port = _session.Port.ToString();
            _roomServer = _session.RoomServerAddress;
        }


        private void OnGUI()
        {
            // Сборщик колод занимает весь экран.
            // Пока он открыт, меню молчит: иначе
            // кнопки ложатся поверх списка карт.
            var deckBuilder =
                _session.GetComponent<DeckBuilder>();

            if (deckBuilder != null &&
                deckBuilder.IsOpen)
            {
                return;
            }


            // Настройки открываются поверх всего
            // и решают сами за себя.
            var settings =
                FindObjectOfType<SettingsMenu>();

            if (settings != null && settings.IsOpen)
                return;


            if (!_open)
            {
                DrawStatus();
                return;
            }


            // Выбор колоды вместо меню: сначала
            // решаем, чем играть, потом соединяемся.
            if (_awaitingDeck.HasValue)
            {
                DrawDeckChoice();
                return;
            }


            // Высота по экрану, но не больше его.
            //
            // Пунктов в меню стало много: при
            // фиксированной высоте нижние уезжали
            // за край. Теперь меню занимает сколько
            // нужно, а лишнее прокручивается.
            float height =
                Mathf.Min(760f, Screen.height - 40f);

            GUILayout.BeginArea(
                new Rect(
                    (Screen.width - Width) * 0.5f,
                    (Screen.height - height) * 0.5f,
                    Width,
                    height
                ),
                GUI.skin.box
            );

            GUILayout.Label("hololive OFFICIAL CARD GAME");
            GUILayout.Space(8f);


            // Всё содержимое — внутри прокрутки:
            // на низком экране пункты не обрежутся.
            _menuScroll =
                GUILayout.BeginScrollView(_menuScroll);


            // Выбор колод. Свои колоды из сборщика
            // уже подмешаны в базу, поэтому видны
            // здесь наравне с поставляемыми.
            DrawDeckPicker(
                "Ваша колода",
                _session.PlayerDeckId,
                id => _session.PlayerDeckId = id);

            DrawDeckPicker(
                "Колода соперника",
                _session.BotDeckId,
                id => _session.BotDeckId = id);


            GUILayout.Space(8f);


            if (GUILayout.Button("Играть с ботом", GUILayout.Height(34f)))
            {
                Launch(GameSession.PlayMode.SinglePlayer);
            }


            // Колоду можно собрать до партии:
            // сборщик правит базу, а выбор колоды
            // идёт уже из неё.
            //
            // Сам сборщик найден выше, в проверке
            // на то, открыт ли он.
            if (deckBuilder != null &&
                GUILayout.Button("Собрать колоду",
                    GUILayout.Height(28f)))
            {
                deckBuilder.Open();
            }


            // Настройки: найдены выше, в проверке
            // на то, открыты ли они.
            if (settings != null &&
                GUILayout.Button("Настройки",
                    GUILayout.Height(28f)))
            {
                settings.Open();
            }


            GUILayout.Space(12f);
            GUILayout.Label("Игра вдвоём по локальной сети");


            GUILayout.BeginHorizontal();
            GUILayout.Label("Порт", GUILayout.Width(50f));
            _port = GUILayout.TextField(_port);
            GUILayout.EndHorizontal();


            if (GUILayout.Button("Создать игру (хост)",
                GUILayout.Height(30f)))
            {
                Begin(GameSession.PlayMode.Host);
            }


            GUILayout.Space(6f);


            GUILayout.BeginHorizontal();
            GUILayout.Label("Адрес", GUILayout.Width(50f));
            _address = GUILayout.TextField(_address);
            GUILayout.EndHorizontal();


            if (GUILayout.Button("Подключиться (гость)",
                GUILayout.Height(30f)))
            {
                Begin(GameSession.PlayMode.Guest);
            }


            GUILayout.Space(14f);
            GUILayout.Label("Игра по коду приглашения");


            GUILayout.BeginHorizontal();
            GUILayout.Label("Сервер", GUILayout.Width(55f));
            _roomServer = GUILayout.TextField(_roomServer);
            GUILayout.EndHorizontal();


            if (GUILayout.Button("Создать комнату",
                GUILayout.Height(30f)))
            {
                Begin(GameSession.PlayMode.RoomCreate);
            }


            GUILayout.Space(6f);


            GUILayout.BeginHorizontal();
            GUILayout.Label("Код", GUILayout.Width(55f));

            // Коды диктуют голосом, а вводят
            // как попало — приводим к верхнему
            // регистру сразу.
            _roomCode =
                GUILayout.TextField(_roomCode)
                    .ToUpperInvariant();

            GUILayout.EndHorizontal();


            if (GUILayout.Button("Войти по коду",
                GUILayout.Height(30f)))
            {
                Begin(GameSession.PlayMode.RoomJoin);
            }


            GUILayout.Space(14f);
            GUILayout.Label(
                "Через интернет (Unity Relay) — " +
                "сервер не нужен");


            if (GUILayout.Button("Создать партию",
                GUILayout.Height(32f)))
            {
                Begin(GameSession.PlayMode.RelayHost);
            }


            GUILayout.Space(6f);


            GUILayout.BeginHorizontal();
            GUILayout.Label("Код", GUILayout.Width(55f));

            _relayCode =
                GUILayout.TextField(_relayCode)
                    .ToUpperInvariant();

            GUILayout.EndHorizontal();


            if (GUILayout.Button("Присоединиться",
                GUILayout.Height(32f)))
            {
                Begin(GameSession.PlayMode.RelayJoin);
            }


            GUILayout.EndScrollView();


            if (_hint.Length > 0)
            {
                GUI.color = new Color(1f, 0.7f, 0.4f);

                GUILayout.Label(_hint);

                GUI.color = Color.white;
            }


            GUILayout.EndArea();
        }


        /// <summary>
        /// Запустить партию в выбранном режиме.
        ///
        /// Имя намеренно не Start: так называется
        /// метод, который Unity вызывает сама,
        /// и параметров он не принимает.
        /// </summary>
        /// <summary>
        /// Строка выбора колоды: название
        /// и стрелки перебора.
        /// </summary>
        private void DrawDeckPicker(
            string title,
            string currentId,
            System.Action<string> choose)
        {
            var decks =
                _session.Decks == null
                    ? null
                    : _session.Decks.Decks;

            if (decks == null || decks.Count == 0)
                return;


            int index = 0;

            for (int i = 0; i < decks.Count; i++)
            {
                if (decks[i].Id == currentId)
                {
                    index = i;
                    break;
                }
            }


            GUILayout.BeginHorizontal();

            GUILayout.Label(title,
                GUILayout.Width(120f));


            if (GUILayout.Button("<",
                GUILayout.Width(26f)))
            {
                index =
                    (index - 1 + decks.Count) % decks.Count;

                choose(decks[index].Id);
            }


            var deck =
                decks[index];

            string mark =
                DeckStorage.IsCustom(deck.Id)
                    ? "★ "
                    : "";

            GUILayout.Label(
                mark + (string.IsNullOrEmpty(deck.Name)
                    ? deck.Id
                    : deck.Name));


            if (GUILayout.Button(">",
                GUILayout.Width(26f)))
            {
                index = (index + 1) % decks.Count;

                choose(decks[index].Id);
            }

            GUILayout.EndHorizontal();
        }


        /// <summary>
        /// Сетевые режимы сначала спрашивают колоду.
        /// </summary>
        private void Begin(
            GameSession.PlayMode mode)
        {
            if (mode == GameSession.PlayMode.SinglePlayer)
            {
                Launch(mode);
                return;
            }


            _awaitingDeck = mode;

            _hint = string.Empty;
        }


        /// <summary>
        /// Экран выбора колоды.
        /// </summary>
        private void DrawDeckChoice()
        {
            var decks =
                _session.Decks == null
                    ? null
                    : _session.Decks.Decks;


            float width = 420f;
            float height = 520f;

            var area =
                new Rect(
                    (Screen.width - width) * 0.5f,
                    (Screen.height - height) * 0.5f,
                    width,
                    height);

            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.Label("Какой колодой играете?");

            GUILayout.Space(6f);


            _deckScroll =
                GUILayout.BeginScrollView(
                    _deckScroll,
                    GUILayout.Height(height - 120f));


            if (decks != null)
            {
                foreach (var deck in decks)
                {
                    bool chosen =
                        deck.Id == _session.PlayerDeckId;


                    GUI.color =
                        chosen ? Color.cyan : Color.white;


                    string title =
                        (DeckStorage.IsCustom(deck.Id)
                            ? "★ "
                            : "") +
                        (string.IsNullOrEmpty(deck.Name)
                            ? deck.Id
                            : deck.Name);


                    if (GUILayout.Button(
                        title,
                        GUILayout.Height(30f)))
                    {
                        _session.PlayerDeckId = deck.Id;
                    }
                }
            }


            GUI.color = Color.white;

            GUILayout.EndScrollView();


            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(
                "Играть этой колодой",
                GUILayout.Height(34f)))
            {
                var mode =
                    _awaitingDeck.Value;

                _awaitingDeck = null;

                Launch(mode);
            }


            if (GUILayout.Button(
                "Назад",
                GUILayout.Width(90f),
                GUILayout.Height(34f)))
            {
                _awaitingDeck = null;
            }

            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }


        private void Launch(
            GameSession.PlayMode mode)
        {
            // Без кода подключаться некуда:
            // служба отвечает исключением,
            // а игрок видит красную ошибку
            // вместо понятного отказа.
            if (mode == GameSession.PlayMode.RelayJoin &&
                string.IsNullOrWhiteSpace(_relayCode))
            {
                _hint = "Введите код партии";
                return;
            }


            if (mode == GameSession.PlayMode.RoomJoin &&
                string.IsNullOrWhiteSpace(_roomCode))
            {
                _hint = "Введите код комнаты";
                return;
            }


            _hint = string.Empty;

            int port;

            if (!int.TryParse(_port, out port))
                port = 7777;


            _session.Port = port;
            _session.HostAddress = _address.Trim();

            _session.RoomServerAddress =
                _roomServer.Trim();

            _session.RoomCode =
                _roomCode.Trim();

            _session.RelayCode =
                _relayCode.Trim();

            _session.Mode = mode;

            _session.Begin();

            _open = false;
        }


        /// <summary>
        /// Короткая строка о состоянии связи.
        ///
        /// Нужна больше, чем кажется: пока хост
        /// ждёт гостя, экран выглядит замершим,
        /// и без подписи непонятно, работает ли
        /// вообще что-то.
        /// </summary>
        private void DrawStatus()
        {
            DrawRoomCode();

            DrawJoiningNotice();


            string text =
                _session.LinkStatus;

            if (string.IsNullOrEmpty(text))
                return;


            GUI.Label(
                new Rect(
                    Screen.width - 420f,
                    4f,
                    410f,
                    22f
                ),
                text
            );
        }


        /// <summary>
        /// Код созданной комнаты крупно по центру.
        ///
        /// Пока второй игрок не вошёл, это
        /// единственное, что нужно на экране:
        /// код надо продиктовать или скопировать.
        /// </summary>
        private void DrawRoomCode()
        {
            // Код показываем ТОЛЬКО создателю
            // партии, пока он ждёт соперника.
            //
            // Раньше окно видел и тот, кто
            // присоединялся: код у него тоже есть,
            // он его сам ввёл — и получалась
            // надпись «продиктуйте» у того, кто
            // как раз принимал приглашение.
            if (!_session.WaitingForOpponent)
                return;


            string code =
                _session.CurrentRoomCode;

            if (string.IsNullOrEmpty(code))
                return;


            var style =
                new GUIStyle(GUI.skin.label);

            style.fontSize = 42;
            style.alignment = TextAnchor.MiddleCenter;


            var area =
                new Rect(
                    (Screen.width - 420f) * 0.5f,
                    Screen.height * 0.35f,
                    420f,
                    150f
                );

            GUI.Box(area, string.Empty);

            GUI.Label(
                new Rect(area.x, area.y + 10f, area.width, 30f),
                "Код комнаты"
            );

            GUI.Label(
                new Rect(area.x, area.y + 45f, area.width, 60f),
                code,
                style
            );

            GUI.Label(
                new Rect(area.x, area.y + 110f, area.width, 30f),
                "Продиктуйте его второму игроку"
            );
        }


        /// <summary>
        /// Присоединившемуся нужна своя подпись:
        /// он ждёт не соперника, а данные от него.
        /// </summary>
        private void DrawJoiningNotice()
        {
            if (_session.WaitingForOpponent ||
                _session.State != null ||
                _session.Mode == GameSession.PlayMode.SinglePlayer)
            {
                return;
            }


            var style =
                new GUIStyle(GUI.skin.label);

            style.alignment = TextAnchor.MiddleCenter;


            var area =
                new Rect(
                    (Screen.width - 420f) * 0.5f,
                    Screen.height * 0.4f,
                    420f,
                    60f
                );

            GUI.Box(area, string.Empty);

            GUI.Label(
                area,
                "Подключились, ждём партию от соперника",
                style
            );
        }
    }
}