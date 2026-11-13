using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;
using HololiveTCG.HololiveTCG.AI;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Мост между движком и сценой.
    ///
    /// Держит GameEngine, грузит базы из StreamingAssets
    /// и после каждого действия отдаёт события,
    /// чтобы их проиграла анимация.
    ///
    /// Ничего не рисует. Всё, что связано с картинкой,
    /// живёт в других скриптах и подписывается
    /// на события этого.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        [Tooltip("Колода игрока, Id из decks.json")]
        public string PlayerDeckId = "hSD01_SORAZ";

        [Tooltip("Колода бота, Id из decks.json")]
        public string BotDeckId = "hSD03_OKAYU";

        [Tooltip("Писать лог движка в консоль Unity")]
        public bool LogToConsole = true;


        public enum PlayMode
        {
            // Игра с ботом в этом же процессе.
            SinglePlayer,

            // Партия считается здесь, второй
            // игрок подключается по сети.
            Host,

            // Движка нет, всё приходит от хоста.
            Guest,

            // Игра по коду приглашения: создаём
            // комнату на сервере и получаем код.
            RoomCreate,

            // Входим в чужую комнату по коду.
            RoomJoin,

            // Unity Relay: своего сервера
            // не нужно вовсе.
            RelayHost,

            RelayJoin
        }


        [Header("Сеть")]
        public PlayMode Mode = PlayMode.SinglePlayer;

        [Tooltip("Порт, на котором ждёт хост")]
        public int Port = 7777;

        [Tooltip("Адрес хоста для гостя")]
        public string HostAddress = "127.0.0.1";

        [Header("Игра по коду приглашения")]
        [Tooltip("Адрес сервера комнат")]
        public string RoomServerAddress = "127.0.0.1";

        [Tooltip("Код чужой комнаты")]
        public string RoomCode = "";

        [Tooltip("Код присоединения Relay")]
        public string RelayCode = "";

        [Tooltip("Начинать партию сразу при запуске. " +
                 "Выключается экраном выбора режима")]
        public bool AutoStart = true;


        /// <summary>
        /// Связь с игрой.
        ///
        /// Интерфейс работает только через неё
        /// и не знает, где считается партия.
        /// </summary>
        public IGameLink Link { get; private set; }


        public GameEngine Engine { get; private set; }

        // Урезанная копия состояния для показа.
        //
        // Связь строит её заново при каждом
        // обращении — полторы сотни карт.
        // Интерфейс спрашивает состояние десятки
        // раз за кадр, так что копию держим до
        // следующего изменения партии.
        private GameState _stateCache;


        /// <summary>
        /// Партия изменилась — копию надо
        /// построить заново.
        /// </summary>
        private void InvalidateState()
        {
            _stateCache = null;
        }


        public GameState State
        {
            get
            {
                // У гостя движка нет вовсе,
                // состояние приходит по сети.
                if (Link != null)
                {
                    if (_stateCache == null)
                        _stateCache = Link.State;

                    return _stateCache;
                }


                return Engine == null
                    ? null
                    : Engine.State;
            }
        }


        /// <summary>
        /// Партия идёт по сети.
        /// </summary>
        public bool IsNetwork
        {
            get { return Mode != PlayMode.SinglePlayer; }
        }


        /// <summary>
        /// За какую сторону играет этот клиент.
        /// </summary>
        public int LocalPlayerIndex
        {
            get
            {
                return Link == null
                    ? 0
                    : Link.LocalPlayerIndex;
            }
        }


        public CardDatabase Cards { get; private set; }

        /// <summary>
        /// Представление бота о колоде игрока.
        /// Создаётся вместе с партией.
        /// </summary>
        public OpponentModel BotOpponentModel { get; private set; }


        /// <summary>
        /// Выученное представление о стиле игрока.
        /// Живёт между партиями: чем дольше играете,
        /// тем точнее бот предсказывает ваши ходы.
        /// </summary>
        public PlayerStyleModel PlayerStyle { get; private set; }


        /// <summary>
        /// Память бота между запусками игры.
        /// Лежит рядом с сохранениями Unity.
        /// </summary>
        public BotMemory Memory { get; private set; }


        private string MemoryPath
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    "bot-memory.json"
                );
            }
        }

        public OshiDatabase Oshi { get; private set; }
        public DeckDatabase Decks { get; private set; }


        /// <summary>
        /// Состояние изменилось — перерисовать доску.
        /// </summary>
        public event Action StateChanged;

        /// <summary>
        /// Что именно произошло за последнее действие.
        /// Список идёт по порядку: его можно проигрывать
        /// как сценарий анимации.
        /// </summary>
        public event Action<List<GameEvent>> EventsProduced;


        private void Awake()
        {
            LoadDatabases();

            // Сначала показываем, что лежит
            // на диске БУКВАЛЬНО, до всякого
            // разбора. Если тут одно, а после
            // загрузки другое — виноват разбор.
            // Если тут уже ноль — файл переписали
            // раньше, и искать надо запись.
            DumpMemoryFile();


            Memory =
                BotMemory.Load(MemoryPath);

            Debug.Log(
                "Память бота: " + Memory +
                "  [" + Memory.LoadReport + "]" +
                "  (" + MemoryPath + ")"
            );


            // Второй GameSession в сцене — самая
            // частая причина странностей с памятью:
            // один экземпляр пишет, другой затирает.
            var sessions =
                FindObjectsByType<GameSession>(
                    FindObjectsSortMode.None
                );

            if (sessions.Length > 1)
            {
                Debug.LogError(
                    "В сцене " + sessions.Length +
                    " объектов GameSession. " +
                    "Должен быть один, иначе память " +
                    "бота будет затираться."
                );
            }


            if (Memory.LoadFailed)
            {
                Debug.LogWarning(
                    "Память бота не загрузилась, " +
                    "и записываться поверх она НЕ будет. " +
                    "Разберитесь с файлом, иначе " +
                    "накопленное обучение потеряется."
                );
            }

            if (AutoStart)
                Begin();
        }


        private void OnApplicationQuit()
        {
            SaveMemory();
        }


        private void OnDisable()
        {
            // В редакторе выход из Play не всегда
            // доходит до OnApplicationQuit,
            // а терять накопленное нельзя.
            SaveMemory();
        }


        /// <summary>
        /// Сохранить выученное.
        ///
        /// Вызывается при выходе и в конце каждой
        /// партии — чтобы результат не потерялся,
        /// если игру закроют не по-хорошему.
        /// </summary>
        public void SaveMemory()
        {
            if (Memory == null)
                return;


            // Никогда не пишем пустую память
            // поверх файла, который не смогли
            // прочитать: именно так теряется
            // накопленное.
            if (Memory.LoadFailed)
                return;


            Memory.Absorb(PlayerStyle);

            // Слияние вместо простой записи:
            // в сцене может оказаться второй
            // GameSession со старой памятью,
            // и он не должен затирать свежую.
            if (!Memory.SaveMerged(MemoryPath))
            {
                Debug.LogError(
                    "Не удалось сохранить память бота: " +
                    MemoryPath
                );
            }
        }


        /// <summary>
        /// Сырое содержимое файла памяти
        /// и время его изменения.
        /// </summary>
        private void DumpMemoryFile()
        {
            try
            {
                if (!File.Exists(MemoryPath))
                {
                    Debug.Log(
                        "Файл памяти не существует: " +
                        MemoryPath
                    );

                    return;
                }


                string text =
                    File.ReadAllText(MemoryPath);


                int marker =
                    text.IndexOf("\"Generations\"");

                string fragment =
                    marker >= 0
                        ? text.Substring(
                            marker,
                            System.Math.Min(
                                40,
                                text.Length - marker
                            ))
                        : "поля Generations нет";


                Debug.Log(
                    "Файл памяти: изменён " +
                    File.GetLastWriteTime(MemoryPath)
                        .ToString("HH:mm:ss") +
                    ", размер " + text.Length +
                    ", " + fragment.Replace("\n", " ")
                );
            }
            catch (System.Exception error)
            {
                Debug.LogWarning(
                    "Не удалось прочитать файл памяти: " +
                    error.Message
                );
            }
        }


        /// <summary>
        /// Что лежит в файле прямо сейчас.
        ///
        /// Нужно, чтобы отличить "сохранили" от
        /// "думали, что сохранили": запись могла
        /// не дойти до диска или её мог перезаписать
        /// другой вызов.
        /// </summary>
        public string InspectSavedMemory()
        {
            var onDisk =
                BotMemory.Load(MemoryPath);

            return
                "в файле: поколений " +
                onDisk.Generations +
                ", партий " +
                onDisk.GamesPlayed;
        }


        private void LoadDatabases()
        {
            Cards = new CardDatabase();
            Oshi = new OshiDatabase();

            Cards.LoadFromFile(
                DataPath("cards.json")
            );

            Oshi.LoadFromFile(
                DataPath("oshi.json")
            );

            Decks = new DeckDatabase(
                Cards,
                Oshi
            );

            Decks.LoadFromFile(
                DataPath("decks.json")
            );

            Debug.Log(
                "Движок: сборка " +
                GameEngine.BuildTag
            );


            // Сетевой обмен восстанавливает карты
            // по номеру из этой базы.
            StateCompactor.Cards = Cards;


            Debug.Log(
                "Loaded " +
                Cards.Cards.Count +
                " cards, " +
                Oshi.OshiCards.Count +
                " oshi, " +
                Decks.Decks.Count +
                " decks."
            );
        }


        private static string DataPath(
            string fileName)
        {
            return Path.Combine(
                Application.streamingAssetsPath,
                fileName
            );
        }


        private bool _resultRecorded;


        public void StartNewGame()
        {
            InvalidateState();

            _resultRecorded = false;

            if (!Validate(PlayerDeckId) ||
                !Validate(BotDeckId))
            {
                return;
            }


            Engine = new GameEngine();

            if (LogToConsole)
                Engine.LogMessage += OnEngineLog;

            Engine.StartGame();


            var player = Engine.State.Players[0];
            var bot = Engine.State.Players[1];

            player.Name = "Player";
            bot.Name = "Bot";

            Decks.BuildDeck(player, PlayerDeckId, "P");
            Decks.BuildDeck(bot, BotDeckId, "B");


            Debug.Log(
                "Player: " + PlayerDeckId +
                " (" + player.Oshi.Name + ", Life " +
                player.Oshi.Life + ")   " +
                "Bot: " + BotDeckId +
                " (" + bot.Oshi.Name + ", Life " +
                bot.Oshi.Life + ")"
            );

            // Бот смотрит на сторону игрока,
            // то есть на индекс 0.
            if (PlayerStyle == null)
            {
                PlayerStyle = new PlayerStyleModel(0);

                // Продолжаем с того, на чём
                // остановились в прошлый раз.
                if (Memory != null)
                    Memory.ApplyTo(PlayerStyle);
            }


            BotOpponentModel =
                new OpponentModel(
                    Decks,
                    Cards,
                    0
                );


            Engine.BeginSetup();


            // Связь поверх движка.
            //
            // В одиночной игре она локальная,
            // в сетевой хост ещё и слушает порт.
            // Интерфейс в обоих случаях один.
            if (Mode == PlayMode.Host)
            {
                var host =
                    new GameHost(Engine);

                host.GuestDeckChosen += UseGuestDeck;

                host.Listen(Port);

                Link = host;

                Debug.Log(
                    "Хост слушает порт " + Port +
                    ". Гостю нужен адрес этой машины."
                );
            }
            else
            {
                Link =
                    new LocalGameLink(Engine, 0);
            }


            Link.Updated += OnLinkUpdated;

            Publish(
                new List<GameEvent>()
            );
        }


        /// <summary>
        /// Начать партию в выбранном режиме.
        ///
        /// Гость ничего не создаёт: партия придёт
        /// от хоста целиком, вместе с колодами
        /// обеих сторон. Поэтому синхронизировать
        /// раздачу отдельно не нужно — хост
        /// единственный, кто её строит.
        /// </summary>
        public void Begin()
        {
            switch (Mode)
            {
                case PlayMode.Guest:
                    ConnectAsGuest();
                    break;

                case PlayMode.RoomCreate:
                case PlayMode.RoomJoin:
                    ConnectToRoomServer();
                    break;

                case PlayMode.RelayHost:
                case PlayMode.RelayJoin:
                    ConnectThroughRelay();
                    break;

                default:
                    StartNewGame();
                    break;
            }
        }


        /// <summary>
        /// Что сейчас со связью.
        /// </summary>
        public string LinkStatus
        {
            get
            {
                var host =
                    Link as GameHost;

                if (host != null)
                {
                    return host.Ready
                        ? "гость подключён, получено " +
                          host.Received + " сообщений"
                        : host.Status;
                }


                var relay =
                    Link as RelayLink;

                if (relay != null)
                {
                    if (relay.WaitingForOpponent)
                    {
                        return "код: " + relay.JoinCode +
                               " — ждём второго игрока";
                    }


                    if (!relay.Ready)
                        return relay.Status;


                    return
                        "Relay: получено " +
                        relay.Received + " сообщений" +
                        (relay.Pending > 0
                            ? ", отправляется " +
                              relay.Pending + " кусков"
                            : "");
                }


                var room =
                    Link as RoomClient;

                if (room != null)
                {
                    if (room.WaitingForOpponent)
                    {
                        return "код комнаты: " + room.Code +
                               " — ждём второго игрока";
                    }


                    return room.Ready
                        ? "комната " + room.Code +
                          ", получено " + room.Received +
                          " обновлений"
                        : room.Status;
                }


                var guest =
                    Link as GameGuest;

                if (guest != null)
                {
                    return guest.Ready
                        ? "подключено к хосту, получено " +
                          guest.Received + " обновлений"
                        : guest.Status;
                }


                return null;
            }
        }


        /// <summary>
        /// Пересобрать партию под колоду,
        /// которую выбрал второй игрок.
        ///
        /// Вызывается сразу после подключения,
        /// пока никто не сходил, поэтому партию
        /// можно спокойно начать заново.
        /// </summary>
        private void UseGuestDeck(
            string deckId)
        {
            if (Engine == null ||
                string.IsNullOrEmpty(deckId) ||
                deckId == BotDeckId)
            {
                return;
            }


            if (!Validate(deckId))
                return;


            BotDeckId = deckId;


            Engine.StartGame();

            Engine.State.Players[0].Name = "Вы";
            Engine.State.Players[1].Name = "Соперник";

            Decks.BuildDeck(
                Engine.State.Players[0],
                PlayerDeckId,
                "P");

            Decks.BuildDeck(
                Engine.State.Players[1],
                BotDeckId,
                "B");

            Engine.BeginSetup();


            InvalidateState();


            var host =
                Link as GameHost;

            if (host != null)
                host.SendHello();


            var relay =
                Link as RelayLink;

            if (relay != null)
                relay.SendHelloAgain();


            Publish(new List<GameEvent>());


            Debug.Log(
                "Соперник выбрал колоду " + deckId +
                ", партия пересобрана."
            );
        }


        private bool Validate(
            string deckId)
        {
            var validation =
                Decks.ValidateDeck(
                    deckId
                );

            if (validation.IsValid)
                return true;


            foreach (var error in validation.Errors)
            {
                Debug.LogError(
                    deckId + ": " + error
                );
            }

            return false;
        }


        private void OnEngineLog(
            string message)
        {
            Debug.Log("[engine] " + message);
        }


        /// <summary>
        /// Единственная точка, через которую
        /// сцена меняет игру.
        /// </summary>
        public bool Apply(
            GameAction action)
        {
            if (action == null)
                return false;


            // Гость ничего не решает сам:
            // действие уходит хосту, а результат
            // придёт обновлением состояния.
            if (Mode == PlayMode.Guest)
            {
                if (Link != null)
                    Link.Send(action);

                return true;
            }


            if (Engine == null)
                return false;


            // Запоминаем позицию и выбор игрока
            // до применения — на этом учится
            // модель его стиля.
            GameState before = null;
            List<GameAction> options = null;

            if (action.ActorPlayerIndex == 0 &&
                PlayerStyle != null)
            {
                before = Engine.State.Clone();

                options =
                    Engine
                        .GetLegalActions()
                        .FindAll(
                            x => x.ActorPlayerIndex == 0
                        );
            }


            // Действие идёт ЧЕРЕЗ связь, а не прямо
            // в движок.
            //
            // Раньше хост применял ход сам, и гость
            // не получал ничего после первого
            // состояния: отправка живёт внутри связи.
            //
            // Связь сама применит действие, разошлёт
            // результат и вызовет OnLinkUpdated,
            // откуда идёт Publish.
            if (Link == null)
                return false;


            Link.Send(action);

            // На случай, если связь применила
            // действие, не известив нас: копию
            // состояния всё равно пересобираем.
            InvalidateState();


            if (before != null)
            {
                PlayerStyle.Observe(
                    before,
                    options,
                    action
                );
            }


            // Партия закончилась — записываем
            // результат и сохраняем выученное.
            if (Engine.State.IsFinished &&
                !_resultRecorded)
            {
                _resultRecorded = true;

                if (Memory != null)
                {
                    Memory.RecordResult(
                        Engine.State,
                        1
                    );
                }

                SaveMemory();

                Debug.Log(
                    "Итог: " + Memory
                );
            }


            return true;
        }


        public List<GameAction> LegalActions()
        {
            if (Link != null)
                return Link.LegalActions();


            return Engine == null
                ? new List<GameAction>()
                : Engine.GetLegalActions();
        }


        /// <summary>
        /// Состояние пришло от связи.
        ///
        /// У гостя это единственный способ узнать,
        /// что произошло: своего движка у него нет.
        /// </summary>
        private void OnLinkUpdated(
            GameState state,
            List<GameEvent> events)
        {
            Publish(events ?? new List<GameEvent>());
        }


        /// <summary>
        /// Подключиться к хосту.
        /// </summary>
        public void ConnectAsGuest()
        {
            Mode = PlayMode.Guest;

            var guest =
                new GameGuest();

            guest.MyDeckId = PlayerDeckId;

            guest.Connect(HostAddress, Port);

            Link = guest;

            Link.Updated += OnLinkUpdated;

            Debug.Log(
                "Подключаемся к " +
                HostAddress + ":" + Port
            );
        }


        /// <summary>
        /// Игра по коду приглашения.
        ///
        /// Партию собирает сервер, поэтому здесь
        /// ни колод, ни движка — только связь.
        /// </summary>
        public void ConnectToRoomServer()
        {
            var client =
                new RoomClient();

            if (Mode == PlayMode.RoomCreate)
            {
                client.Create(
                    RoomServerAddress,
                    Port,
                    PlayerDeckId,
                    BotDeckId
                );

                Debug.Log(
                    "Создаём комнату на " +
                    RoomServerAddress + ":" + Port
                );
            }
            else
            {
                client.Join(
                    RoomServerAddress,
                    Port,
                    RoomCode
                );

                Debug.Log(
                    "Входим в комнату " + RoomCode
                );
            }


            Link = client;

            Link.Updated += OnLinkUpdated;
        }


        /// <summary>
        /// Мы создали партию и ждём соперника.
        ///
        /// Только в этом случае код нужно
        /// показывать: присоединяющийся код уже
        /// знает, он его и вводил.
        /// </summary>
        public bool WaitingForOpponent
        {
            get
            {
                var relay =
                    Link as RelayLink;

                if (relay != null)
                    return relay.WaitingForOpponent;


                var room =
                    Link as RoomClient;

                if (room != null)
                    return room.WaitingForOpponent;


                var host =
                    Link as GameHost;

                return host != null && !host.Ready;
            }
        }


        /// <summary>
        /// Код созданной комнаты.
        /// Пусто, если игра не по коду.
        /// </summary>
        public string CurrentRoomCode
        {
            get
            {
                var relay =
                    Link as RelayLink;

                if (relay != null)
                    return relay.JoinCode;


                var client =
                    Link as RoomClient;

                return client == null
                    ? null
                    : client.Code;
            }
        }


        /// <summary>
        /// Игра через Unity Relay.
        ///
        /// Создающий поднимает у себя движок,
        /// как в обычном хосте. Отличие только
        /// в том, что соединение идёт не напрямую,
        /// а через Relay, и адрес заменяет код.
        /// </summary>
        private async void ConnectThroughRelay()
        {
            // Службы должны быть подняты, иначе
            // Relay откажет.
            await UgsBootstrap.SignIn();

            if (!UgsBootstrap.Ready)
            {
                Debug.LogError(
                    "Relay недоступен: " +
                    UgsBootstrap.Status
                );

                return;
            }


            RelayLink link;

            if (Mode == PlayMode.RelayHost)
            {
                if (!Validate(PlayerDeckId) ||
                    !Validate(BotDeckId))
                {
                    return;
                }


                Engine = new GameEngine();

                if (LogToConsole)
                    Engine.LogMessage += OnEngineLog;

                Engine.StartGame();

                Engine.State.Players[0].Name = "Вы";
                Engine.State.Players[1].Name = "Соперник";

                Decks.BuildDeck(
                    Engine.State.Players[0],
                    PlayerDeckId,
                    "P");

                Decks.BuildDeck(
                    Engine.State.Players[1],
                    BotDeckId,
                    "B");

                Engine.BeginSetup();


                link = new RelayLink(Engine);

                Link = link;

                Link.Updated += OnLinkUpdated;

                link.GuestDeckChosen += UseGuestDeck;

                await link.Host();
            }
            else
            {
                link = new RelayLink(null);

                Link = link;

                Link.Updated += OnLinkUpdated;

                link.MyDeckId = PlayerDeckId;

                await link.Join(RelayCode);
            }


            Publish(new List<GameEvent>());
        }


        private void Update()
        {
            // Сетевая связь разбирает входящие
            // сообщения здесь, в главном потоке:
            // читает их фоновый поток, а трогать
            // состояние игры можно только отсюда.
            if (Link != null)
                Link.Poll();
        }


        private void Publish(
            List<GameEvent> events)
        {
            // Сначала сбрасываем копию: слушатели
            // сейчас прочтут состояние и должны
            // увидеть уже новое.
            InvalidateState();


            if (EventsProduced != null)
                EventsProduced(events);

            if (StateChanged != null)
                StateChanged();
        }
    }
}