using System.Collections.Generic;
using System.Linq;

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Клики по картам.
    ///
    /// Правило простое: щелчок по карте — это не
    /// "сыграть её", а "найти среди легальных действий
    /// те, что её касаются". Поэтому нарисовать
    /// незаконный ход невозможно в принципе:
    /// если движок действие не предложил,
    /// кликом его не выполнить.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    [RequireComponent(typeof(BoardView))]
    public class BoardInput : MonoBehaviour
    {
        public Camera BoardCamera;

        private GameSession _session;
        private BoardView _board;
        private AttackArrow _arrow;

        // Если карта участвует сразу в нескольких
        // действиях, показываем выбор.
        private List<GameAction> _choices;
        private Vector2 _choicePosition;
        private string _choiceCardName;


        // Режим "карта выбрана, укажите цель".
        //
        // Так работает Bloom и всё похожее:
        // щёлкнули карту в руке — она выезжает
        // вперёд, подходящие цели на столе
        // подсвечиваются, щёлкнули цель — готово.
        private CardView _armed;
        private List<GameAction> _armedTargeted;
        private List<GameAction> _armedLoose;
        private readonly List<CardView> _armedTargets =
            new List<CardView>();
        private int _armedFrame = -1;

        [Tooltip("Подсветка целей для выбранной карты")]
        public Color TargetColor =
            new Color(0.4f, 0.75f, 1f, 1f);


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _board = GetComponent<BoardView>();
            _arrow = GetComponent<AttackArrow>();

            if (BoardCamera == null)
                BoardCamera = Camera.main;
        }


        private void OnEnable()
        {
            if (_session != null)
                _session.StateChanged += OnStateChanged;
        }


        private void OnDisable()
        {
            if (_session != null)
                _session.StateChanged -= OnStateChanged;
        }


        /// <summary>
        /// Партия сдвинулась — выбранная карта
        /// и её действия могли устареть.
        /// </summary>
        private void OnStateChanged()
        {
            if (_armed != null)
                Disarm();

            _choices = null;
        }


        // Проект может стоять и на старой системе
        // ввода, и на новой. Читаем мышь так,
        // чтобы работало в обоих случаях.
        private static bool ClickedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return
                Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }


        private static bool CancelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return
                (Mouse.current != null &&
                 Mouse.current.rightButton.wasPressedThisFrame) ||
                (Keyboard.current != null &&
                 Keyboard.current.escapeKey.wasPressedThisFrame);
#else
            return
                Input.GetMouseButtonDown(1) ||
                Input.GetKeyDown(KeyCode.Escape);
#endif
        }


        private static Vector3 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
                return Vector3.zero;

            Vector2 point =
                Mouse.current.position.ReadValue();

            return new Vector3(point.x, point.y, 0f);
#else
            return Input.mousePosition;
#endif
        }


        private void Update()
        {
            if (_queued != null)
            {
                var action = _queued;

                _queued = null;

                _session.Apply(action);

                return;
            }


            if (_armed != null)
            {
                UpdateArmed();
                return;
            }


            if (!ClickedThisFrame())
                return;


            // Пока висит меню выбора или открыто
            // окно выбора карт, клики по столу
            // игнорируем.
            if (_choices != null ||
                SelectionPrompt.Blocking)
            {
                return;
            }


            // Выбор цели ведёт стрелка.
            if (_arrow != null && _arrow.Active)
                return;


            var view =
                _board.Raycast(
                    BoardCamera,
                    MousePosition()
                );

            if (view == null || view.Card == null)
                return;


            HandleClick(view);
        }


        private void HandleClick(
            CardView view)
        {
            string id =
                view.Card.Id;


            var mine =
                _session
                    .LegalActions()
                    .Where(x => x.ActorPlayerIndex ==
                        _session.LocalPlayerIndex)
                    .ToList();


            // Ось на столе — обёртка, у её действий
            // нет номера карты. Щелчок по ней
            // показывает скиллы оси.
            bool isOshi =
                view.Card.Type == CardType.Oshi;


            var matching =
                isOshi
                    ? mine
                        .Where(x =>
                            x.Type ==
                                GameActionType.UseOshiSkill ||
                            x.Type ==
                                GameActionType.UseSpOshiSkill)
                        .ToList()
                    : mine
                        .Where(x =>
                            x.CardId == id ||
                            x.TargetCardId == id)
                        .ToList();


            // Действий нет — глухой отказ, чтобы
            // щелчок не казался потерянным.
            // Щелчок по своей колоде берёт карту.
            //
            // Добор — действие без карты, поэтому
            // раньше его можно было сделать только
            // кнопкой. А тянуться рука сама тянется
            // к колоде.
            if (matching.Count == 0)
            {
                var draw =
                    DeckClick(mine, view.Card);

                if (draw != null)
                {
                    _queued = draw;
                    return;
                }
            }


            if (matching.Count == 0)
            {
                GameAudio.Play(GameAudio.Denied, 0.6f);


                // Говорим словами, почему нельзя.
                var hud =
                    GetComponent<GameHud>();

                if (hud != null)
                {
                    hud.Toast(
                        RefusalReason.For(
                            _session.State,
                            view.Card,
                            _session.LocalPlayerIndex));
                }


                Debug.Log(
                    "С " + view.Card.Name +
                    " сейчас ничего сделать нельзя."
                );

                return;
            }


            // Атака выбирает цель стрелкой,
            // а не пунктом меню.
            var arts =
                matching
                    .Where(x =>
                        x.Type ==
                            GameActionType.PerformArt &&
                        x.CardId == id)
                    .ToList();

            if (_arrow != null && arts.Count > 0)
            {
                _arrow.Begin(view, arts);

                return;
            }


            // Карта в руке, которую надо положить
            // на кого-то: Bloom, прикрепление.
            // Выдвигаем её и ждём, куда укажут.
            if (!isOshi)
            {
                var targeted =
                    matching
                        .Where(x =>
                            x.CardId == id &&
                            !string.IsNullOrEmpty(x.TargetCardId))
                        .ToList();


                if (targeted.Count > 0)
                {
                    var loose =
                        matching
                            .Where(x => !targeted.Contains(x))
                            .ToList();

                    Arm(view, targeted, loose);

                    return;
                }
            }


            if (matching.Count == 1 && !isOshi)
            {
                _session.Apply(matching[0]);

                return;
            }


            _choices = matching;

            Vector3 mouse =
                MousePosition();

            _choicePosition =
                new Vector2(
                    mouse.x,
                    Screen.height - mouse.y
                );

            _choiceCardName =
                view.Card.Name;
        }


        // Как и в GameHud: действие, выбранное
        // кнопкой меню, применяется не в отрисовке,
        // а следующим кадром. Иначе раскладка
        // интерфейса Unity рассыпается — она
        // считается и рисуется в два прохода,
        // между которыми состояние меняться
        // не должно.
        private GameAction _queued;


        private CameraFocus _focus;


        private CameraFocus Focus
        {
            get
            {
                if (_focus == null)
                    _focus = GetComponent<CameraFocus>();

                return _focus;
            }
        }


        /// <summary>
        /// Если щёлкнули по своей колоде и добор
        /// сейчас разрешён — вернуть это действие.
        /// </summary>
        private GameAction DeckClick(
            List<GameAction> mine,
            Card card)
        {
            var state =
                _session.State;

            if (state == null || card == null)
                return null;


            var player =
                state.Players[_session.LocalPlayerIndex];


            bool inDeck =
                player.Deck.Any(x => x.Id == card.Id);

            if (!inDeck)
                return null;


            return mine.FirstOrDefault(
                x => x.Type == GameActionType.DrawCard);
        }


        private void Arm(
            CardView view,
            List<GameAction> targeted,
            List<GameAction> loose)
        {
            // Карта взята из руки — цель указывают
            // на столе, туда и смотрим.
            if (Focus != null &&
                Focus.LookAtTableWhenPlaying)
            {
                Focus.LookAtTable();
            }


            GameAudio.Play(GameAudio.Select);

            _armed = view;
            _armedTargeted = targeted;
            _armedLoose = loose;
            _armedFrame = Time.frameCount;

            _board.LiftedCardId = view.Card.Id;


            foreach (var action in targeted)
            {
                var target =
                    _board.Find(action.TargetCardId);

                if (target == null ||
                    _armedTargets.Contains(target))
                {
                    continue;
                }


                target.SetHighlight(true, TargetColor);

                _armedTargets.Add(target);
            }


            // Всё, кроме целей и самой карты,
            // уходит в тень.
            var highlighter =
                GetComponent<PlayableHighlighter>();

            if (highlighter != null)
            {
                var ids =
                    new List<string> { view.Card.Id };

                foreach (var t in _armedTargets)
                {
                    if (t != null && t.Card != null)
                        ids.Add(t.Card.Id);
                }


                highlighter.Focus(ids);
            }
        }


        private void Disarm()
        {
            if (_armed != null)
                GameAudio.Play(GameAudio.Cancel, 0.7f);


            foreach (var target in _armedTargets)
            {
                if (target != null)
                    target.SetHighlight(false, Color.white);
            }

            _armedTargets.Clear();


            var highlighter =
                GetComponent<PlayableHighlighter>();

            if (highlighter != null)
                highlighter.ClearFocus();


            _board.LiftedCardId = null;

            _armed = null;
            _armedTargeted = null;
            _armedLoose = null;
        }


        private void UpdateArmed()
        {
            if (CancelPressed())
            {
                Disarm();
                return;
            }


            if (!ClickedThisFrame() ||
                Time.frameCount == _armedFrame)
            {
                return;
            }


            // Щелчок по кнопке меню ловит OnGUI —
            // здесь его не трогаем.
            if (_menuRect.Contains(GuiMouse()))
                return;


            var view =
                _board.Raycast(
                    BoardCamera,
                    MousePosition()
                );


            if (view != null && view.Card != null)
            {
                var action =
                    _armedTargeted.FirstOrDefault(x =>
                        x.TargetCardId == view.Card.Id);


                if (action != null)
                {
                    Disarm();

                    _session.Apply(action);


                    // Карта сыграна — при желании
                    // возвращаемся к руке.
                    if (Focus != null &&
                        Focus.ReturnToHandAfterPlay)
                    {
                        Focus.LookAtHand();
                    }

                    return;
                }
            }


            // Щелчок мимо целей — передумали.
            Disarm();
        }


        private Rect _menuRect;


        private static Vector2 GuiMouse()
        {
            Vector3 mouse = MousePosition();

            return new Vector2(
                mouse.x,
                Screen.height - mouse.y);
        }


        /// <summary>
        /// Подсказка и прочие действия выбранной
        /// карты — например, "В задний ряд",
        /// если Debut можно и выложить,
        /// и блумнуть.
        /// </summary>
        private void DrawArmedMenu()
        {
            float width = 240f;

            int rows =
                1 + _armedLoose.Count + 1;

            float height = 30f * rows + 12f;


            var screen =
                BoardCamera != null
                    ? BoardCamera.WorldToScreenPoint(
                        _armed.transform.position)
                    : new Vector3(Screen.width * 0.5f, 200f, 0f);


            _menuRect =
                new Rect(
                    Mathf.Clamp(
                        screen.x + 60f,
                        10f,
                        Screen.width - width - 10f),
                    Mathf.Clamp(
                        Screen.height - screen.y - height,
                        10f,
                        Screen.height - height - 10f),
                    width,
                    height);


            GUILayout.BeginArea(_menuRect, GUI.skin.box);

            GUILayout.Label("Выберите цель на столе");


            foreach (var action in _armedLoose)
            {
                if (!GUILayout.Button(
                    ActionLabels.For(action, _session.State)))
                {
                    continue;
                }


                Disarm();

                _queued = action;

                GUILayout.EndArea();
                return;
            }


            if (GUILayout.Button("Отмена"))
                Disarm();

            GUILayout.EndArea();
        }


        private void OnGUI()
        {
            if (_armed != null)
            {
                DrawArmedMenu();
                return;
            }


            if (_choices == null)
                return;


            float width = 260f;
            float height = 28f * (_choices.Count + 2);

            float x =
                Mathf.Min(
                    _choicePosition.x,
                    Screen.width - width - 10f
                );

            float y =
                Mathf.Min(
                    _choicePosition.y,
                    Screen.height - height - 10f
                );


            GUILayout.BeginArea(
                new Rect(x, y, width, height),
                GUI.skin.box
            );

            GUILayout.Label(_choiceCardName);


            foreach (var action in _choices)
            {
                if (!GUILayout.Button(
                    ActionLabels.For(action, _session.State)))
                {
                    continue;
                }


                _choices = null;

                _queued = action;

                GUILayout.EndArea();
                return;
            }


            if (GUILayout.Button("Отмена"))
                _choices = null;

            GUILayout.EndArea();
        }
    }
}