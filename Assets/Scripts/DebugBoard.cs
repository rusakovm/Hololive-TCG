using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;
using HololiveTCG.HololiveTCG.AI;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Временный интерфейс: состояние текстом
    /// и кнопки легальных действий.
    ///
    /// Ходы бота выполняются сами, с паузой,
    /// чтобы было видно, что происходит.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class DebugBoard : MonoBehaviour
    {
        [Tooltip("Пауза между действиями бота, секунды")]
        public float BotDelay = 0.4f;

        [Tooltip("Предохранитель от зацикливания")]
        public int MaxBotActionsInARow = 200;


        private GameSession _session;
        private BotPlayer _bot;

        private List<GameAction> _actions =
            new List<GameAction>();

        private Vector2 _scroll;
        private bool _botRunning;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _bot = new BotPlayer();

            _session.StateChanged += OnStateChanged;
            _session.EventsProduced += OnEvents;
        }


        private void OnDestroy()
        {
            if (_session == null)
                return;

            _session.StateChanged -= OnStateChanged;
            _session.EventsProduced -= OnEvents;
        }


        private void OnStateChanged()
        {
            _actions = _session.LegalActions();

            TryRunBot();
        }


        private void Start()
        {
            OnStateChanged();
        }


        private void OnEvents(
            List<GameEvent> events)
        {
            foreach (var gameEvent in events)
                Debug.Log("[event] " + gameEvent);
        }


        /// <summary>
        /// Пока игроку нечего делать, ходит бот.
        /// Раньше это была кнопка, и при первом ходе
        /// бота игра выглядела зависшей.
        /// </summary>
        private void TryRunBot()
        {
            if (_botRunning)
                return;


            // В сетевой партии за вторую сторону
            // играет человек. Бот там не нужен
            // и вредит: он ходил бы за противника.
            if (_session.IsNetwork)
                return;


            var state = _session.State;

            if (state == null || state.IsFinished)
                return;


            bool playerCanAct =
                _actions.Any(
                    x => x.ActorPlayerIndex ==
                        _session.LocalPlayerIndex
                );

            if (playerCanAct)
                return;


            StartCoroutine(
                RunBot()
            );
        }


        private IEnumerator RunBot()
        {
            _botRunning = true;

            int guard = 0;


            while (true)
            {
                var state = _session.State;

                if (state == null || state.IsFinished)
                    break;


                var actions =
                    _session.LegalActions();

                if (actions.Any(
                x => x.ActorPlayerIndex ==
                    _session.LocalPlayerIndex))
                    break;


                if (actions.Count == 0)
                {
                    Debug.LogError(
                        "Тупик: легальных действий нет ни у кого. " +
                        "Фаза " + state.Phase +
                        ", ход " + state.TurnNumber +
                        ", ходит " + state.CurrentPlayer.Name
                    );

                    break;
                }


                // Бот пользуется моделью колоды
                // игрока, если она уже создана.
                _bot.Opponent =
                    _session.BotOpponentModel;

                _bot.Style =
                    _session.PlayerStyle;

                // Играем весами, выученными
                // самоигрой.
                if (_session.Memory != null &&
                    _session.Memory.BotWeights != null)
                {
                    _bot.Weights =
                        new EvaluationWeights(
                            _session.Memory.BotWeights
                        );
                }


                var action =
                    _bot.ChooseAction(_session.Engine);


                // Отдельно отмечаем случай, когда бот
                // уходит из фазы атаки, хотя бить было
                // чем: это самая заметная для игрока
                // странность, и её надо видеть.
                if (action != null &&
                    action.Type == GameActionType.EndPhase &&
                    state.Phase == GamePhase.Performance)
                {
                    int available =
                        actions.Count(
                            x => x.Type ==
                                GameActionType.PerformArt
                        );

                    if (available > 0)
                    {
                        Debug.LogWarning(
                            "Бот закончил фазу атаки, " +
                            "имея " + available +
                            " доступных артов."
                        );
                    }
                }

                if (action == null)
                {
                    Debug.LogError(
                        "Бот не смог выбрать действие, " +
                        "хотя действия есть: " +
                        string.Join(
                            ", ",
                            actions.Select(x => x.Type.ToString()).ToArray()
                        )
                    );

                    break;
                }


                if (!_session.Apply(action))
                {
                    Debug.LogError(
                        "Движок отклонил действие бота: " + action
                    );

                    break;
                }


                guard++;

                if (guard > MaxBotActionsInARow)
                {
                    Debug.LogError(
                        "Бот сделал " + guard +
                        " действий подряд — похоже на зацикливание."
                    );

                    break;
                }


                yield return new WaitForSeconds(BotDelay);
            }


            _botRunning = false;

            _actions = _session.LegalActions();
        }


        [Tooltip("Показывать отладочный экран. " +
                 "Переключается клавишей F1")]
        public bool Visible;


        private void OnGUI()
        {
            // F1 ловим через событие интерфейса:
            // так работает при любой системе ввода.
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.F1)
            {
                Visible = !Visible;
                Event.current.Use();
            }


            if (!Visible)
                return;


            // Пока открыт сборщик колод, отладочный
            // экран не мешается.
            var builder =
                GetComponent<DeckBuilder>();

            if (builder != null && builder.IsOpen)
                return;


            var state = _session.State;

            if (state == null)
                return;


            GUILayout.BeginArea(
                new Rect(10, 10, 460, Screen.height - 20)
            );

            if (_bot != null &&
                _bot.Opponent != null)
            {
                GUILayout.Label(
                    "Бот думает, что у вас: " +
                    _bot.Opponent
                );
            }


            if (_session.PlayerStyle != null)
            {
                GUILayout.Label(
                    "Ваш стиль: " +
                    _session.PlayerStyle
                );
            }


            if (_session.Memory != null)
            {
                GUILayout.Label(
                    "Опыт бота: " +
                    _session.Memory
                );
            }


            GUILayout.Label(
                "Turn " + state.TurnNumber +
                "   Phase: " + state.Phase +
                "   Current: " + state.CurrentPlayer.Name
            );

            Describe(
                state.Players[_session.LocalPlayerIndex]
            );

            Describe(
                state.Players[
                    _session.LocalPlayerIndex == 0 ? 1 : 0
                ]
            );

            GUILayout.Space(10);


            if (state.IsFinished)
            {
                GUILayout.Label(
                    "WINNER: " +
                    state.Players[state.WinnerIndex.Value].Name
                );

                GUILayout.EndArea();
                return;
            }


            if (_botRunning)
            {
                GUILayout.Label("Ход бота...");

                GUILayout.EndArea();
                return;
            }


            var mine =
                _actions
                    .Where(x => x.ActorPlayerIndex ==
                        _session.LocalPlayerIndex)
                    .ToList();

            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (var action in mine)
            {
                if (GUILayout.Button(Text(action)))
                {
                    _session.Apply(action);
                    break;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }


        private void Describe(
            PlayerState player)
        {
            GUILayout.Label(
                player.Name +
                "  Life " + player.Life.Count +
                "  Hand " + player.Hand.Count +
                "  Deck " + player.Deck.Count +
                "  Center: " +
                (player.Center == null
                    ? "-"
                    : player.Center.Name +
                      " " + player.Center.CurrentHp +
                      "/" + player.Center.GetMaxHp())
            );
        }


        private static string Text(
            GameAction action)
        {
            return
                action.Type +
                (string.IsNullOrEmpty(action.CardId)
                    ? ""
                    : "  " + action.CardId) +
                (string.IsNullOrEmpty(action.TargetCardId)
                    ? ""
                    : " -> " + action.TargetCardId) +
                (action.Number.HasValue
                    ? "  #" + action.Number.Value
                    : "");
        }
    }
}