using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;

// Эти типы должны приходить ровно из одного
// пакета. Если в проекте стоят и Multiplayer
// Services, и старый отдельный Relay, здесь
// будет неоднозначность: у типов одинаковые
// полные имена, и выбрать нужный нельзя —
// лишний пакет надо удалить.
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Связь через Unity Relay.
    ///
    /// Своего сервера не нужно вовсе: Relay
    /// сводит игроков и передаёт данные, а код
    /// присоединения выдаёт сам.
    ///
    /// Роли те же, что и раньше: у создателя
    /// живёт движок и он решает, что законно,
    /// второй только показывает присланное.
    ///
    /// Главная особенность — размер. Relay шлёт
    /// небольшие пакеты, а состояние партии в JSON
    /// весит десятки килобайт, поэтому сообщение
    /// режется на куски и собирается обратно.
    /// Надёжный канал гарантирует порядок,
    /// так что сборка сводится к склейке.
    /// </summary>
    public class RelayLink : IGameLink
    {
        /// <summary>
        /// Сколько байт кладём в один пакет.
        ///
        /// Взято с запасом от предела Relay:
        /// лучше лишний пакет, чем потерянное
        /// сообщение.
        /// </summary>
        private const int ChunkSize = 400;


        private NetworkDriver _driver;
        private NetworkPipeline _pipeline;

        private NetworkConnection _connection;

        private readonly GameEngine _engine;

        private GameState _state;
        private List<GameAction> _actions;

        private readonly StringBuilder _incoming =
            new StringBuilder();


        /// <summary>
        /// Куски, ждущие отправки.
        ///
        /// Надёжный канал держит в полёте лишь
        /// несколько десятков пакетов, а состояние
        /// партии режется на полсотни и больше.
        /// Если валить их разом, лишние теряются
        /// молча — поэтому шлём по мере того,
        /// как канал освобождается.
        /// </summary>
        private readonly Queue<byte[]> _outbox =
            new Queue<byte[]>();


        public bool IsHost { get; private set; }

        public string JoinCode { get; private set; }

        public string Status { get; private set; }

        public int Received { get; private set; }


        /// <summary>
        /// Второй игрок назвал свою колоду.
        /// </summary>
        public System.Action<string> GuestDeckChosen;


        /// <summary>
        /// Колода, которой хочет играть этот игрок,
        /// если он присоединяется.
        /// </summary>
        public string MyDeckId;


        private bool _deckSent;


        /// <summary>
        /// Отправить партию заново — например,
        /// когда её пересобрали под колоду
        /// второго игрока.
        /// </summary>
        public void SendHelloAgain()
        {
            if (IsHost)
                SendHello();
        }


        /// <summary>
        /// Писать в консоль каждый шаг обмена.
        /// Пока связь не отлажена — нужно.
        /// </summary>
        public static bool Verbose = true;


        private int _chunksSent;
        private int _chunksGot;
        private int _bytesGot;


        /// <summary>
        /// Гость подтвердил, что получил партию.
        ///
        /// Пока подтверждения нет, хост повторяет
        /// первое сообщение: оно может потеряться,
        /// а больше ничего не происходит — и второй
        /// игрок ждёт вечно.
        /// </summary>
        private bool _helloAcked;

        private float _lastHelloAt;


        /// <summary>
        /// Сколько кусков ещё не ушло.
        /// Видно в статусе: так отличается
        /// «передаём» от «зависли».
        /// </summary>
        public int Pending
        {
            get { return _outbox.Count; }
        }


        public int LocalPlayerIndex
        {
            get { return IsHost ? 0 : 1; }
        }


        public bool Ready
        {
            get
            {
                return IsHost
                    ? _connection.IsCreated
                    : _state != null;
            }
        }


        /// <summary>
        /// Комната создана, второй игрок
        /// ещё не подключился.
        /// </summary>
        public bool WaitingForOpponent
        {
            get
            {
                return IsHost &&
                       !string.IsNullOrEmpty(JoinCode) &&
                       !_connection.IsCreated;
            }
        }


        public GameState State
        {
            get
            {
                if (!IsHost)
                    return _state;


                return _engine == null
                    ? null
                    : StateView.For(
                        _engine.State,
                        LocalPlayerIndex);
            }
        }


        public event Action<GameState, List<GameEvent>> Updated;


        public RelayLink(
            GameEngine engine)
        {
            _engine = engine;
        }


        // =========================================
        // ПОДКЛЮЧЕНИЕ
        // =========================================

        /// <summary>
        /// Создать партию и получить код.
        /// </summary>
        public async Task Host()
        {
            IsHost = true;

            try
            {
                Status = "просим у Relay место";

                // Один слот для второго игрока:
                // нас самих считать не надо.
                Allocation allocation =
                    await RelayService.Instance
                        .CreateAllocationAsync(1);


                JoinCode =
                    await RelayService.Instance
                        .GetJoinCodeAsync(
                            allocation.AllocationId);


                var relayData =
                    MakeServerData(
                        allocation.ServerEndpoints,
                        allocation.AllocationIdBytes,
                        allocation.ConnectionData,
                        allocation.ConnectionData,
                        allocation.Key
                    );

                CreateDriver(relayData);

                _driver.Listen();

                Status =
                    "код " + JoinCode +
                    ", ждём второго игрока";

                Debug.Log("Relay: код " + JoinCode);
            }
            catch (Exception error)
            {
                Status = "Relay отказал: " + error.Message;

                Debug.LogError("Relay: " + error);
            }
        }


        /// <summary>
        /// Присоединиться по коду.
        /// </summary>
        public async Task Join(
            string joinCode)
        {
            IsHost = false;

            JoinCode = (joinCode ?? string.Empty).Trim();

            try
            {
                Status = "ищем комнату " + JoinCode;

                JoinAllocation allocation =
                    await RelayService.Instance
                        .JoinAllocationAsync(JoinCode);


                var relayData =
                    MakeServerData(
                        allocation.ServerEndpoints,
                        allocation.AllocationIdBytes,
                        allocation.ConnectionData,
                        allocation.HostConnectionData,
                        allocation.Key
                    );

                CreateDriver(relayData);

                _connection =
                    _driver.Connect(relayData.Endpoint);

                Status = "подключаемся";
            }
            catch (Exception error)
            {
                Status = "не удалось войти: " + error.Message;

                Debug.LogError("Relay: " + error);
            }
        }


        /// <summary>
        /// Собрать данные Relay вручную.
        ///
        /// Короткий конструктор из одной строки
        /// менялся от версии к версии, а этот
        /// набор полей остаётся прежним.
        /// </summary>
        private static RelayServerData MakeServerData(
            List<RelayServerEndpoint> endpoints,
            byte[] allocationId,
            byte[] connectionData,
            byte[] hostConnectionData,
            byte[] key)
        {
            RelayServerEndpoint chosen = null;


            // Предпочитаем защищённое соединение,
            // но берём любое, если его нет.
            foreach (var endpoint in endpoints)
            {
                if (endpoint.ConnectionType == "dtls")
                {
                    chosen = endpoint;
                    break;
                }


                if (chosen == null)
                    chosen = endpoint;
            }


            if (chosen == null)
            {
                throw new Exception(
                    "Relay не предложил ни одной точки входа"
                );
            }


            return new RelayServerData(
                chosen.Host,
                (ushort)chosen.Port,
                allocationId,
                connectionData,
                hostConnectionData,
                key,
                chosen.Secure
            );
        }


        private void CreateDriver(
            RelayServerData relayData)
        {
            var settings =
                new NetworkSettings();

            settings.WithRelayParameters(ref relayData);

            _driver =
                NetworkDriver.Create(settings);

            // Надёжный упорядоченный канал:
            // на нём держится вся склейка кусков.
            _pipeline =
                _driver.CreatePipeline(
                    typeof(ReliableSequencedPipelineStage));


            if (IsHost)
                _driver.Bind(NetworkEndpoint.AnyIpv4);
        }


        // =========================================
        // ОБМЕН
        // =========================================

        public void Poll()
        {
            // Любая ошибка в обмене не должна
            // ронять игру: лучше показать её
            // в консоли и продолжить.
            try
            {
                PollInternal();
            }
            catch (Exception error)
            {
                Status = "сбой обмена: " + error.Message;

                Debug.LogError("Relay: " + error);
            }
        }


        private void PollInternal()
        {
            if (!_driver.IsCreated)
                return;


            _driver.ScheduleUpdate().Complete();


            if (IsHost)
                AcceptPending();


            Flush();

            RetryHello();


            NetworkConnection from;
            DataStreamReader reader;
            NetworkEvent.Type kind;

            while ((kind = _driver.PopEvent(
                out from, out reader)) !=
                NetworkEvent.Type.Empty)
            {
                switch (kind)
                {
                    case NetworkEvent.Type.Connect:

                        Status = "соединение установлено";

                        if (Verbose)
                        {
                            Debug.Log(
                                "Relay: событие Connect" +
                                (IsHost ? " (хост)" : " (гость)")
                            );
                        }

                        break;


                    case NetworkEvent.Type.Data:

                        Take(reader);

                        break;


                    case NetworkEvent.Type.Disconnect:

                        Status = "соединение потеряно";

                        if (Verbose)
                            Debug.LogWarning("Relay: Disconnect");

                        _connection = default(NetworkConnection);

                        break;
                }
            }
        }


        private void AcceptPending()
        {
            NetworkConnection incoming;

            while ((incoming = _driver.Accept()) !=
                default(NetworkConnection))
            {
                if (_connection.IsCreated)
                {
                    // Мест больше нет.
                    _driver.Disconnect(incoming);
                    continue;
                }


                _connection = incoming;

                Status = "второй игрок подключился";

                Debug.Log("Relay: второй игрок подключился");


                // Первое сообщение: кто он
                // и как выглядит партия.
                SendHello();

                Fire(new List<GameEvent>());
            }
        }


        /// <summary>
        /// Отправить гостю партию целиком.
        /// </summary>
        private void SendHello()
        {
            _lastHelloAt = Time.realtimeSinceStartup;

            Send(
                new NetMessage
                {
                    Kind = NetMessage.Hello,
                    PlayerIndex = 1,
                    Code = JoinCode,
                    State = StateView.For(_engine.State, 1),
                    Actions = _engine.GetLegalActions()
                }
            );
        }


        /// <summary>
        /// Повторить приветствие, если гость
        /// до сих пор не отозвался.
        /// </summary>
        private void RetryHello()
        {
            if (!IsHost ||
                _helloAcked ||
                !_connection.IsCreated ||
                _outbox.Count > 0)
            {
                return;
            }


            if (Time.realtimeSinceStartup - _lastHelloAt < 2f)
                return;


            if (Verbose)
                Debug.Log("Relay: повторяем приветствие");

            SendHello();
        }


        /// <summary>
        /// Забрать пришедшие байты и собрать
        /// из них целые сообщения.
        /// </summary>
        private void Take(
            DataStreamReader reader)
        {
            int length =
                reader.Length;

            if (length <= 0)
                return;


            var bytes =
                new byte[length];


            var buffer =
                new NativeArray<byte>(
                    length, Allocator.Temp);

            try
            {
                reader.ReadBytes(buffer);

                buffer.CopyTo(bytes);
            }
            finally
            {
                buffer.Dispose();
            }


            _chunksGot++;
            _bytesGot += length;

            if (Verbose)
            {
                Debug.Log(
                    "Relay: пришёл кусок " + length +
                    " байт (всего кусков " + _chunksGot +
                    ", байт " + _bytesGot + ")"
                );
            }


            _incoming.Append(
                Encoding.UTF8.GetString(bytes)
            );


            // Если разделитель потерян, накопитель
            // будет расти без конца. Лучше признать
            // поток испорченным, чем съесть память.
            if (_incoming.Length > 4 * 1024 * 1024)
            {
                Debug.LogError(
                    "Relay: накопитель переполнен, " +
                    "поток сброшен"
                );

                _incoming.Length = 0;

                return;
            }


            // Сообщения разделены переводом строки.
            // Всё, что до него, — целое сообщение.
            while (true)
            {
                string text =
                    _incoming.ToString();

                int end =
                    text.IndexOf('\n');

                if (end < 0)
                    return;


                string line =
                    text.Substring(0, end);

                _incoming.Remove(0, end + 1);


                if (line.Length == 0)
                    continue;


                if (Verbose)
                {
                    Debug.Log(
                        "Relay: собрано сообщение " +
                        line.Length + " знаков"
                    );
                }


                Handle(line);
            }
        }


        private void Handle(
            string line)
        {
            NetMessage message;

            try
            {
                message = Wire.Read(line);
            }
            catch (Exception error)
            {
                Status = "не разобрал сообщение: " + error.Message;

                Debug.LogError(
                    "Relay: не разобрал сообщение — " +
                    error.Message
                );

                return;
            }

            if (message == null)
                return;


            Received++;

            if (Verbose)
                Debug.Log("Relay: сообщение " + message.Kind);


            if (IsHost)
            {
                // Второй игрок назвал колоду —
                // партию пересоберут.
                if (message.Kind == NetMessage.Join &&
                    !string.IsNullOrEmpty(message.Deck1))
                {
                    if (GuestDeckChosen != null)
                        GuestDeckChosen(message.Deck1);

                    return;
                }


                // Любое сообщение от гостя означает,
                // что связь живая и партию он получил.
                if (!_helloAcked)
                {
                    _helloAcked = true;

                    if (Verbose)
                        Debug.Log("Relay: гость подтвердил партию");
                }


                if (message.Kind != NetMessage.Action ||
                    message.Payload == null)
                {
                    return;
                }


                // Ходить можно только за себя.
                if (message.Payload.ActorPlayerIndex != 1)
                    return;


                ApplyAsHost(message.Payload);

                return;
            }


            switch (message.Kind)
            {
                case NetMessage.Hello:

                    _state = message.State;
                    _actions = message.Actions;

                    Status = "партия началась";


                    if (!_deckSent &&
                        !string.IsNullOrEmpty(MyDeckId))
                    {
                        _deckSent = true;

                        Send(
                            new NetMessage
                            {
                                Kind = NetMessage.Join,
                                PlayerIndex = 1,
                                Deck1 = MyDeckId
                            }
                        );
                    }


                    // Подтверждаем получение, иначе
                    // хост будет слать партию снова.
                    Send(
                        new NetMessage
                        {
                            Kind = NetMessage.Created,
                            PlayerIndex = 1
                        }
                    );


                    Fire(new List<GameEvent>());

                    break;


                case NetMessage.Update:

                    _state = message.State;
                    _actions = message.Actions;

                    Fire(
                        message.Events ??
                            new List<GameEvent>()
                    );

                    break;
            }
        }


        // =========================================
        // ХОДЫ
        // =========================================

        public List<GameAction> LegalActions()
        {
            if (IsHost)
            {
                return _engine == null
                    ? new List<GameAction>()
                    : _engine.GetLegalActions();
            }


            return _actions ??
                new List<GameAction>();
        }


        public void Send(
            GameAction action)
        {
            if (IsHost)
            {
                ApplyAsHost(action);
                return;
            }


            Send(
                new NetMessage
                {
                    Kind = NetMessage.Action,
                    PlayerIndex = 1,
                    Payload = action
                }
            );
        }


        private void ApplyAsHost(
            GameAction action)
        {
            List<GameEvent> events;

            try
            {
                bool applied =
                    _engine.ApplyAction(action);

                events =
                    applied
                        ? new List<GameEvent>(
                            _engine.State.Events)
                        : new List<GameEvent>();
            }
            catch (Exception error)
            {
                Debug.LogError(
                    "Движок упал на " + action.Type +
                    ": " + error.Message);

                events = new List<GameEvent>();
            }


            Fire(events);


            // Ответ уходит всегда, даже если ход
            // отклонён: иначе второй игрок
            // будет ждать вечно.
            Send(
                new NetMessage
                {
                    Kind = NetMessage.Update,
                    PlayerIndex = 1,
                    State = StateView.For(_engine.State, 1),
                    Events = events,
                    Actions = _engine.GetLegalActions()
                }
            );
        }


        // =========================================
        // ОТПРАВКА
        // =========================================

        private void Send(
            NetMessage message)
        {
            if (!_driver.IsCreated ||
                !_connection.IsCreated)
            {
                return;
            }


            // Сжимаем: без этого состояние партии
            // весит больше двухсот килобайт и просто
            // не пролезает в канал.
            string line =
                Wire.Pack(message) + "\n";

            byte[] bytes =
                Encoding.UTF8.GetBytes(line);


            if (Verbose)
            {
                Debug.Log(
                    "Relay: отправляем " + message.Kind +
                    ", " + bytes.Length + " байт, " +
                    ((bytes.Length + ChunkSize - 1) / ChunkSize) +
                    " кусков"
                );
            }


            // Режем на куски и ставим в очередь.
            // Отправкой займётся Flush по кадрам.
            for (int offset = 0;
                 offset < bytes.Length;
                 offset += ChunkSize)
            {
                int size =
                    Math.Min(
                        ChunkSize,
                        bytes.Length - offset);

                var chunk =
                    new byte[size];

                Array.Copy(
                    bytes, offset, chunk, 0, size);

                _outbox.Enqueue(chunk);
            }


            Flush();
        }


        /// <summary>
        /// Отдать каналу столько кусков,
        /// сколько он готов принять.
        /// </summary>
        private void Flush()
        {
            if (!_driver.IsCreated ||
                !_connection.IsCreated)
            {
                return;
            }


            while (_outbox.Count > 0)
            {
                byte[] chunk =
                    _outbox.Peek();


                DataStreamWriter writer;

                if (_driver.BeginSend(
                    _pipeline,
                    _connection,
                    out writer) != 0)
                {
                    // Канал занят. Кусок остаётся
                    // в очереди и уйдёт следующим
                    // кадром — терять его нельзя,
                    // иначе сообщение не соберётся.
                    if (Verbose && _chunksSent == 0)
                    {
                        Debug.LogWarning(
                            "Relay: канал не принимает " +
                            "куски, в очереди " +
                            _outbox.Count
                        );
                    }

                    return;
                }


                // Кусок обязан влезать в пакет.
                //
                // Запись за границу буфера роняет
                // приложение целиком, поэтому
                // проверяем заранее и отказываемся
                // от отправки, а не падаем.
                if (chunk.Length > writer.Capacity)
                {
                    _driver.AbortSend(writer);

                    Debug.LogError(
                        "Relay: кусок " + chunk.Length +
                        " байт не влезает в пакет (" +
                        writer.Capacity + " байт). " +
                        "Уменьшите ChunkSize."
                    );

                    _outbox.Clear();

                    return;
                }


                var buffer =
                    new NativeArray<byte>(
                        chunk.Length, Allocator.Temp);

                for (int i = 0; i < chunk.Length; i++)
                    buffer[i] = chunk[i];


                writer.WriteBytes(buffer);

                buffer.Dispose();


                if (writer.HasFailedWrites)
                {
                    _driver.AbortSend(writer);

                    Debug.LogError(
                        "Relay: запись куска не удалась"
                    );

                    return;
                }


                _driver.EndSend(writer);


                _outbox.Dequeue();

                _chunksSent++;
            }
        }


        private void Fire(
            List<GameEvent> events)
        {
            if (Updated != null)
                Updated(State, events);
        }


        public void Dispose()
        {
            if (_driver.IsCreated)
                _driver.Dispose();
        }
    }
}