using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Интерфейс партии поверх стола.
    ///
    /// Действия с конкретной картой уже умеет
    /// BoardInput: щелчок по карте открывает меню.
    /// Здесь — всё, что к карте не привязано:
    /// счётчики обеих сторон, чей ход и какая
    /// фаза, кнопка конца фазы, скиллы оси,
    /// решения вроде муллигана, журнал событий
    /// и итог партии.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class GameHud : MonoBehaviour
    {
        [Tooltip("Сколько строк журнала показывать")]
        public int LogLines = 7;

        [Tooltip("Размер шрифта")]
        public int FontSize = 15;

        [Tooltip("Пробел заканчивает фазу")]
        public bool SpaceEndsPhase = true;

        [Tooltip("Через сколько секунд наведения " +
                 "показать текст карты")]
        public float TooltipDelay = 1f;


        private GameSession _session;

        private readonly List<string> _log =
            new List<string>();

        // Действие, выбранное кнопкой.
        //
        // Применять его прямо в отрисовке нельзя:
        // интерфейс Unity рисует кадр дважды —
        // сначала считает раскладку, потом рисует.
        // Если между этими проходами изменить
        // состояние партии, число кнопок разойдётся,
        // и Unity ругается на несовпадение раскладки.
        // Поэтому запоминаем и применяем в Update.
        private GameAction _queued;


        // Список действий, посчитанный один раз
        // за кадр.
        //
        // Интерфейс Unity рисует кадр в два прохода
        // и требует, чтобы между ними число кнопок
        // не менялось. А список может измениться
        // сам: бот сходил, пришло обновление
        // по сети. Поэтому считаем его на первом
        // проходе и весь кадр пользуемся одним
        // и тем же.
        private readonly List<GameAction> _frameActions =
            new List<GameAction>();

        private readonly List<GameAction> _frameGlobal =
            new List<GameAction>();


        // Всё остальное, что может измениться
        // посреди кадра, тоже снимается один раз.
        //
        // Журнал пополняется из событий партии,
        // а окно выбора карт поднимает свой признак
        // в собственной отрисовке — и то и другое
        // способно случиться между проходами.
        private readonly List<string> _frameLog =
            new List<string>();

        private bool _frameBlocking;

        private bool _frameFinished;


        // Короткое сообщение внизу экрана:
        // объяснение отказа, подсказка.
        private string _toast = string.Empty;

        private float _toastUntil;


        // Что случилось за партию — для итога.
        private int _myDowned;
        private int _theirDowned;
        private int _myDamage;
        private int _theirDamage;
        private int _arts;

        private int _frameTheirs;

        private bool _restartQueued;


        private GUIStyle _panel;
        private GUIStyle _text;
        private GUIStyle _title;
        private GUIStyle _big;
        private GUIStyle _small;
        private GUIStyle _strong;
        private GUIStyle _faint;

        private Texture2D _fade;
        private GUIStyle _button;
        private GUIStyle _primary;

        private Texture2D _dark;
        private Texture2D _accent;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
        }


        private void OnEnable()
        {
            if (_session != null)
                _session.EventsProduced += OnEvents;
        }


        private void OnDisable()
        {
            if (_session != null)
                _session.EventsProduced -= OnEvents;
        }


        // =========================================
        // ЖУРНАЛ
        // =========================================

        /// <summary>
        /// Пробел заканчивает фазу.
        ///
        /// Самое частое действие за партию не должно
        /// требовать точного попадания мышью
        /// в угол экрана.
        /// </summary>
        private void ReadSpace()
        {
            if (!SpaceEndsPhase || _queued != null)
                return;


            if (!SpacePressed())
                return;


            foreach (var action in _frameGlobal)
            {
                if (action.Type == GameActionType.EndPhase)
                {
                    _queued = action;
                    return;
                }
            }
        }


        private static bool SpacePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return
                UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current
                    .spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }


        /// <summary>
        /// Показать короткое сообщение.
        /// </summary>
        public void Toast(
            string text,
            float seconds = 3.5f)
        {
            if (string.IsNullOrEmpty(text))
                return;


            _toast = text;

            _toastUntil =
                Time.unscaledTime + seconds;
        }


        private void Update()
        {
            ReadSpace();


            if (_queued != null)
            {
                var action = _queued;

                _queued = null;

                _session.Apply(action);
            }


            if (_restartQueued)
            {
                _restartQueued = false;

                _log.Clear();

                _session.StartNewGame();
            }
        }


        private void OnEvents(
            List<GameEvent> events)
        {
            foreach (var e in events)
            {
                Count(e);


                string line =
                    Describe(e);

                if (string.IsNullOrEmpty(line))
                    continue;


                _log.Add(line);
            }


            while (_log.Count > 40)
                _log.RemoveAt(0);
        }


        /// <summary>
        /// Событие человеческими словами.
        ///
        /// Мелкие события вроде перемещения карт
        /// пропускаем: журнал должен рассказывать
        /// о ходе партии, а не пересказывать
        /// каждое движение.
        /// </summary>
        private void Count(
            GameEvent e)
        {
            bool mine =
                e.PlayerIndex == _session.LocalPlayerIndex;


            switch (e.Type)
            {
                case GameEventType.ArtUsed:

                    if (mine)
                        _arts++;

                    break;

                case GameEventType.DamageDealt:

                    // Событие принадлежит бьющему,
                    // поэтому урон свой — это урон,
                    // который нанесли мы.
                    if (mine)
                        _myDamage += e.Amount;
                    else
                        _theirDamage += e.Amount;

                    break;

                case GameEventType.CardDowned:

                    if (mine)
                        _myDowned++;
                    else
                        _theirDowned++;

                    break;
            }
        }


        private string Describe(
            GameEvent e)
        {
            string who =
                e.PlayerIndex == _session.LocalPlayerIndex
                    ? "Вы"
                    : "Соперник";


            switch (e.Type)
            {
                case GameEventType.TurnStarted:
                    return "— ход: " + who.ToLower() + " —";

                case GameEventType.Bloomed:
                    return who + ": Bloom " + e.CardName;

                case GameEventType.MovedToCollab:
                    return who + ": коллаб " + e.CardName;

                case GameEventType.SupportPlayed:
                    return who + ": разыграно " + e.CardName;

                case GameEventType.ArtUsed:
                    return who + ": " + e.CardName +
                        " → " + e.TargetCardName +
                        ", " + e.Amount;

                case GameEventType.CardDowned:
                    return e.CardName + " уничтожен";

                case GameEventType.LifeLost:
                    return who + " теряете жизнь";

                case GameEventType.OshiSkillUsed:
                    return who + ": скилл оси " + e.SourceName;

                case GameEventType.DieRolled:
                    return "Кубик: " + e.Amount;

                case GameEventType.GameOver:
                    return "Партия окончена";

                default:
                    return null;
            }
        }


        // =========================================
        // ОТРИСОВКА
        // =========================================

        private void OnGUI()
        {
            var state =
                _session.State;

            if (state == null)
                return;


            // Сборщик колод и меню занимают экран
            // целиком — поверх них не рисуем.
            var builder =
                GetComponent<DeckBuilder>();

            if (builder != null && builder.IsOpen)
                return;


            // Пересчитываем только на проходе
            // раскладки: на проходе отрисовки
            // список обязан быть тем же.
            if (Event.current.type == EventType.Layout)
                CacheFrame(state);


            EnsureStyles();


            // Другие окна меняют общие цвета
            // интерфейса и не всегда их
            // возвращают — сбрасываем явно.
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;


            int me =
                _session.LocalPlayerIndex;

            int them =
                me == 0 ? 1 : 0;


            DrawTurnBanner(state, me);

            DrawPlayerPanel(
                state.Players[them],
                "Соперник",
                new Rect(12f, 12f, 260f, 74f));

            DrawPlayerPanel(
                state.Players[me],
                "Вы",
                new Rect(
                    12f,
                    Screen.height - 86f,
                    260f,
                    74f));

            DrawLog();

            DrawToast();

            DrawTooltip();


            if (_frameFinished)
            {
                DrawGameOver(state, me);
                return;
            }


            // Пока открыто окно выбора карт,
            // решает оно — второй набор кнопок
            // только запутает.
            if (!_frameBlocking)
                DrawActions(state, me);
        }


        /// <summary>
        /// Во время подготовки номер хода и фаза
        /// ещё ничего не значат, а "ход соперника"
        /// прямо вводит в заблуждение: вы в это
        /// время как раз выбираете руку.
        /// </summary>
        private static bool IsSetup(
            GameAction action)
        {
            switch (action.Type)
            {
                case GameActionType.KeepOpeningHand:
                case GameActionType.MulliganOpeningHand:
                case GameActionType.PutSetupCardBottom:
                case GameActionType.ChooseStartingCenter:
                case GameActionType.PlayStartingBack:
                case GameActionType.FinishStartingBack:
                    return true;

                default:
                    return false;
            }
        }


        private void CacheFrame(
            GameState state)
        {
            int me0 =
                _session.LocalPlayerIndex;


            _frameBlocking =
                SelectionPrompt.Blocking;

            _frameFinished =
                state.IsFinished;


            _frameLog.Clear();

            int shown =
                Mathf.Min(LogLines, _log.Count);

            for (int i = _log.Count - shown;
                 i < _log.Count;
                 i++)
            {
                _frameLog.Add(_log[i]);
            }


            _frameActions.Clear();
            _frameGlobal.Clear();

            _frameTheirs = 0;


            var all =
                _session.LegalActions();

            if (all == null)
                return;


            foreach (var action in all)
            {
                if (action.ActorPlayerIndex != me0)
                {
                    _frameTheirs++;
                    continue;
                }


                _frameActions.Add(action);


                // Выбор цели, до которой нельзя
                // дотянуться щелчком, — в панель.
                //
                // Карты в архиве и в колодах лежат
                // стопкой: доступна только верхняя.
                // Раньше такой выбор можно было
                // сделать лишь через отладочный
                // экран по F1.
                if (action.Type ==
                        GameActionType.ChooseEffectTarget &&
                    !string.IsNullOrEmpty(action.TargetCardId) &&
                    !OnStage(state, action.TargetCardId))
                {
                    _frameGlobal.Add(action);
                    continue;
                }


                // Всё, что делается щелчком по карте,
                // в панель не выносим.
                if (!string.IsNullOrEmpty(action.CardId) ||
                    !string.IsNullOrEmpty(action.TargetCardId))
                {
                    continue;
                }


                if (action.Type == GameActionType.FinishSelection ||
                    action.Type == GameActionType.ChooseEffectTarget ||
                    action.Type == GameActionType.UseOshiSkill ||
                    action.Type == GameActionType.UseSpOshiSkill)
                {
                    continue;
                }


                _frameGlobal.Add(action);
            }
        }


        /// <summary>
        /// Лежит ли карта на сцене — то есть можно
        /// ли по ней щёлкнуть.
        /// </summary>
        private static bool OnStage(
            GameState state,
            string cardId)
        {
            foreach (var player in state.Players)
            {
                if (player.Center != null &&
                    player.Center.Id == cardId)
                {
                    return true;
                }


                if (player.Collab != null &&
                    player.Collab.Id == cardId)
                {
                    return true;
                }


                foreach (var card in player.Back)
                {
                    if (card != null && card.Id == cardId)
                        return true;
                }


                // Прикреплённые тоже на виду.
                foreach (var host in Stage(player))
                {
                    foreach (var cheer in host.AttachedCheer)
                    {
                        if (cheer.Id == cardId)
                            return true;
                    }


                    foreach (var gear in host.AttachedSupports)
                    {
                        if (gear.Id == cardId)
                            return true;
                    }
                }
            }


            return false;
        }


        private static IEnumerable<Card> Stage(
            PlayerState player)
        {
            if (player.Center != null)
                yield return player.Center;

            if (player.Collab != null)
                yield return player.Collab;

            foreach (var card in player.Back)
            {
                if (card != null)
                    yield return card;
            }
        }


        private void DrawTurnBanner(
            GameState state,
            int me)
        {
            bool mine =
                state.CurrentPlayerIndex == me;


            // Из кеша: на проходе отрисовки список
            // обязан быть тем же, что на проходе
            // раскладки.
            var actions =
                _frameActions;


            bool setup =
                actions.Any(IsSetup);


            string text;

            if (setup)
            {
                // В кеше только свои действия,
                // так что достаточно проверить,
                // есть ли среди них подготовка.
                bool myTurnToSetup =
                    actions.Any(IsSetup);

                text =
                    "Подготовка партии  ·  " +
                    (myTurnToSetup
                        ? "ваш выбор"
                        : "соперник готовится");
            }
            else
            {
                text =
                "Ход " + state.TurnNumber +
                "  ·  " +
                ActionLabels.Phase(state.Phase) +
                "  ·  " +
                (mine ? "ваш ход" : "ход соперника");
            }


            var rect =
                new Rect(
                    (Screen.width - 460f) * 0.5f,
                    10f,
                    460f,
                    34f);

            GUI.Box(rect, GUIContent.none, _panel);

            GUI.Label(rect, text, _title);
        }


        /// <summary>
        /// Панель стороны: имя оси и показатели
        /// одной строкой.
        ///
        /// Жизни и holoPower крупнее остального:
        /// во время партии они важнее, чем сколько
        /// карт осталось в колоде.
        /// </summary>
        private void DrawPlayerPanel(
            PlayerState player,
            string title,
            Rect rect)
        {
            // Без рамки: фон тёмный слева и
            // растворяется вправо. Два чёрных
            // прямоугольника поверх яркого коврика
            // смотрелись как отладочные окна.
            GUI.DrawTexture(rect, _fade);


            string oshi =
                player.Oshi == null
                    ? "—"
                    : player.Oshi.Name;


            // Имя — главное, подпись стороны —
            // мелко и тускло.
            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 2f,
                         rect.width - 20f, 24f),
                oshi,
                _strong);

            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 24f,
                         rect.width - 20f, 18f),
                title.ToUpper(),
                _faint);


            // Жизни и holoPower — единственное,
            // что важно каждый ход. Колода и cheer
            // теперь подписаны у самих стопок.
            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 40f,
                         rect.width - 20f, 28f),
                "♥ " + player.Life.Count +
                "     ✦ " + player.HoloPower.Count,
                _strong);
        }


        /// <summary>
        /// Сообщение по центру снизу — там, куда
        /// смотрят, и не поверх карт руки.
        /// </summary>
        private void DrawToast()
        {
            if (_toast.Length == 0 ||
                Time.unscaledTime > _toastUntil)
            {
                return;
            }


            float width = 560f;

            var rect =
                new Rect(
                    (Screen.width - width) * 0.5f,
                    Screen.height * 0.62f,
                    width,
                    52f);

            GUI.Box(rect, GUIContent.none, _panel);

            GUI.Label(rect, _toast, _title);
        }


        /// <summary>
        /// Текст карты, если на ней задержались.
        ///
        /// Ctrl показывает карту крупно, но для
        /// этого надо знать про Ctrl. Секунда
        /// наведения — то, что игрок делает сам.
        /// </summary>
        private void DrawTooltip()
        {
            var board =
                GetComponent<BoardView>();

            if (board == null)
                return;


            string id =
                board.HoveredCardId;


            if (string.IsNullOrEmpty(id))
            {
                _hoverSince = 0f;
                return;
            }


            if (id != _hoverId)
            {
                _hoverId = id;
                _hoverSince = Time.unscaledTime;

                return;
            }


            if (Time.unscaledTime - _hoverSince < TooltipDelay)
                return;


            var view =
                board.Find(id);

            if (view == null || view.Card == null)
                return;


            string text =
                CardSummary(view.Card);

            if (text.Length == 0)
                return;


            float width = 420f;

            var size =
                _text.CalcHeight(
                    new GUIContent(text), width - 24f);

            // Координаты берём из события отрисовки:
            // они уже в системе интерфейса и не
            // зависят от того, какая система ввода
            // включена в проекте.
            Vector2 mouse =
                Event.current.mousePosition;


            var rect =
                new Rect(
                    Mathf.Min(
                        mouse.x + 18f,
                        Screen.width - width - 10f),
                    Mathf.Clamp(
                        mouse.y + 18f,
                        10f,
                        Screen.height - size - 30f),
                    width,
                    size + 20f);


            GUI.Box(rect, GUIContent.none, _panel);

            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 10f,
                         rect.width - 24f, size),
                text,
                _text);
        }


        private string _hoverId;
        private float _hoverSince;


        private static string CardSummary(
            Card card)
        {
            var text =
                new System.Text.StringBuilder();

            text.Append(card.Name);


            if (card.Type == CardType.Holomem)
            {
                text.Append("  ");
                text.Append(card.CurrentHp);
                text.Append(" / ");
                text.Append(card.GetMaxHp());
            }


            foreach (var ability in card.Abilities)
            {
                if (ability == null ||
                    string.IsNullOrEmpty(ability.Description))
                {
                    continue;
                }


                text.Append("\n\n");
                text.Append(ability.Name);
                text.Append(": ");

                text.Append(
                    CardTranslations.Ability(
                        card.DefinitionId,
                        ability.Name,
                        ability.Description));
            }


            return text.ToString();
        }


        private void DrawLog()
        {
            if (_frameLog.Count == 0)
                return;


            float height =
                24f * _frameLog.Count + 16f;

            var rect =
                new Rect(
                    252f,
                    Screen.height - height - 12f,
                    330f,
                    height);


            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(
                new Rect(
                    rect.x + 10f, rect.y + 8f,
                    rect.width - 20f, rect.height - 16f));


            foreach (var line in _frameLog)
                GUILayout.Label(line, _text);


            GUILayout.EndArea();
        }


        /// <summary>
        /// Кнопки действий, не привязанных к карте.
        /// </summary>
        private void DrawActions(
            GameState state,
            int me)
        {
            var mine =
                _frameActions;

            var global =
                _frameGlobal;


            float width = 250f;

            float height =
                46f * (global.Count + 1) + 20f;

            var rect =
                new Rect(
                    Screen.width - width - 12f,
                    Screen.height - height - 12f,
                    width,
                    height);


            if (mine.Count == 0)
            {
                DrawWaiting(rect, state, me);
                return;
            }


            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(
                new Rect(
                    rect.x + 10f, rect.y + 10f,
                    rect.width - 20f, rect.height - 20f));


            // Конец фазы — главная кнопка, внизу
            // и выделенная. Остальное над ней.
            GameAction end = null;


            // Одно действие в наборе главное —
            // его и делают чаще всего. Оно крупнее
            // и ярче, остальные скромнее.
            GameAction primary =
                null;

            foreach (var action in global)
            {
                if (action.Type == GameActionType.EndPhase)
                {
                    end = action;
                    continue;
                }


                if (primary == null && IsPrimary(action))
                {
                    primary = action;
                    continue;
                }


                if (GUILayout.Button(
                    Label(action, state),
                    _button,
                    GUILayout.Height(34f)))
                {
                    _queued = action;
                }
            }


            if (primary != null &&
                GUILayout.Button(
                    Label(primary, state),
                    _primary,
                    GUILayout.Height(44f)))
            {
                _queued = primary;
            }


            if (end != null &&
                GUILayout.Button(
                    ActionLabels.EndPhase(state.Phase),
                    _primary,
                    GUILayout.Height(44f)))
            {
                _queued = end;
            }


            GUILayout.EndArea();
        }


        /// <summary>
        /// Подпись с подробностями: у скиллов
        /// оси — имя и стоимость.
        /// </summary>
        /// <summary>
        /// Какое действие в наборе главное.
        ///
        /// На муллигане это "оставить руку",
        /// в фазе добора — взять карту: то, что
        /// игрок выбирает в девяти случаях
        /// из десяти.
        /// </summary>
        private static bool IsPrimary(
            GameAction action)
        {
            switch (action.Type)
            {
                case GameActionType.KeepOpeningHand:
                case GameActionType.DrawCard:
                case GameActionType.FinishStartingBack:
                case GameActionType.RollDie:
                    return true;

                default:
                    return false;
            }
        }


        private string Label(
            GameAction action,
            GameState state)
        {
            var player =
                state.Players[_session.LocalPlayerIndex];


            if (player.Oshi != null)
            {
                if (action.Type ==
                        GameActionType.UseOshiSkill &&
                    player.Oshi.OshiSkill != null)
                {
                    return Skill(player.Oshi.OshiSkill);
                }


                if (action.Type ==
                        GameActionType.UseSpOshiSkill &&
                    player.Oshi.SpOshiSkill != null)
                {
                    return "SP: " + Skill(player.Oshi.SpOshiSkill);
                }
            }


            // У выбора цели показываем, что именно
            // выбираем: иначе три одинаковых
            // "Выбрать целью" подряд.
            if (action.Type ==
                    GameActionType.ChooseEffectTarget &&
                !string.IsNullOrEmpty(action.TargetCardId))
            {
                string name =
                    ActionLabels.CardName(
                        state,
                        action.TargetCardId);

                if (name.Length > 0)
                    return "Выбрать: " + name;
            }


            return ActionLabels.For(action, state);
        }


        private static string Skill(
            OshiSkill skill)
        {
            return skill.Name +
                "  (holoPower " +
                skill.HoloPowerCost + ")";
        }


        /// <summary>
        /// Когда действий нет, важно понимать почему.
        ///
        /// Если их нет и у соперника — партия встала,
        /// и это видно сразу, вместе с фазой и тем,
        /// чей ход. Раньше экран молча показывал
        /// "соперник думает", и отличить раздумье
        /// от зависания было невозможно.
        /// </summary>
        private void DrawWaiting(
            Rect rect,
            GameState state,
            int me)
        {
            int theirs =
                _frameTheirs;

            var all =
                _frameActions;


            string text;

            if (theirs > 0)
            {
                text = "Соперник думает…";
            }
            else
            {
                string kinds =
                    all.Count > 0
                        ? string.Join(", ",
                            all.Select(x => x.Type.ToString())
                               .Distinct()
                               .Take(3)
                               .ToArray())
                        : "действий нет ни у кого";

                text =
                    "Партия встала: " +
                    ActionLabels.Phase(state.Phase) +
                    ", ходит " +
                    (state.CurrentPlayerIndex == me
                        ? "вы"
                        : "соперник") +
                    "\n" + kinds +
                    "\nF1 — отладочный экран";
            }


            var small =
                new Rect(
                    rect.x,
                    Screen.height - 96f,
                    rect.width,
                    84f);

            GUI.Box(small, GUIContent.none, _panel);

            GUI.Label(small, text, _title);
        }


        private void DrawGameOver(
            GameState state,
            int me)
        {
            bool won =
                state.WinnerIndex == me;


            var rect =
                new Rect(
                    (Screen.width - 460f) * 0.5f,
                    (Screen.height - 320f) * 0.5f,
                    460f,
                    320f);

            GUI.Box(rect, GUIContent.none, _panel);


            GUI.Label(
                new Rect(rect.x, rect.y + 16f, rect.width, 60f),
                won ? "Победа!" : "Поражение",
                _big);


            // Короткая сводка: чем всё закончилось.
            // Числа говорят о партии больше,
            // чем одно слово.
            var summary =
                new System.Text.StringBuilder();

            summary.AppendLine(
                "Ходов сыграно: " + state.TurnNumber);

            summary.AppendLine(
                "Ваших атак: " + _arts);

            summary.AppendLine(
                "Урона нанесено: " + _myDamage +
                "     получено: " + _theirDamage);

            summary.AppendLine(
                "Уничтожено холомемов: " +
                _theirDowned + " у соперника, " +
                _myDowned + " у вас");

            summary.AppendLine(
                "Жизней осталось: " +
                state.Players[me].Life.Count + " против " +
                state.Players[me == 0 ? 1 : 0].Life.Count);


            GUI.Label(
                new Rect(rect.x + 30f, rect.y + 96f,
                         rect.width - 60f, 130f),
                summary.ToString(),
                _text);


            // Новую партию можно начать только
            // против бота: в сетевой её начинает
            // тот, у кого движок.
            if (_session.Mode ==
                GameSession.PlayMode.SinglePlayer)
            {
                if (GUI.Button(
                    new Rect(
                        rect.x + 130f, rect.y + 246f,
                        200f, 44f),
                    "Новая партия",
                    _primary))
                {
                    _restartQueued = true;

                    _myDowned = 0;
                    _theirDowned = 0;
                    _myDamage = 0;
                    _theirDamage = 0;
                    _arts = 0;
                }
            }
        }


        // =========================================
        // СТИЛИ
        // =========================================

        /// <summary>
        /// Стили создаются один раз и при первой
        /// отрисовке: GUI.skin доступен только
        /// внутри OnGUI.
        /// </summary>
        private void EnsureStyles()
        {
            // Текстуры фона, созданные в коде,
            // Unity может выгрузить — например,
            // при сборке неиспользуемых ресурсов.
            // Тогда панели становятся прозрачными
            // и остаётся один чёрный текст.
            // Проверяем не только стили, но и
            // сами текстуры, и пересоздаём всё.
            if (_panel != null &&
                _dark != null &&
                _accent != null &&
                _fade != null &&
                _panel.normal.background != null)
            {
                return;
            }


            _dark =
                Solid(new Color(0.08f, 0.08f, 0.12f, 0.82f));

            _accent =
                Solid(new Color(0.25f, 0.55f, 0.95f, 0.95f));


            _panel =
                new GUIStyle(GUI.skin.box);

            _panel.normal.background = _dark;


            _text =
                new GUIStyle(GUI.skin.label);

            _text.fontSize = FontSize;
            _text.normal.textColor = Color.white;


            _title =
                new GUIStyle(_text);

            _title.fontStyle = FontStyle.Bold;
            _title.alignment = TextAnchor.MiddleCenter;


            _big =
                new GUIStyle(_title);

            _big.fontSize = FontSize * 3;


            // Мелкое — для второстепенных чисел.
            _small =
                new GUIStyle(_text);

            _small.fontSize =
                Mathf.Max(10, FontSize - 3);

            _small.normal.textColor =
                new Color(0.78f, 0.80f, 0.86f);


            // Крупное — для жизней и holoPower.
            _strong =
                new GUIStyle(_text);

            _strong.fontSize = FontSize + 6;
            _strong.fontStyle = FontStyle.Bold;


            _faint =
                new GUIStyle(_text);

            _faint.fontSize =
                Mathf.Max(9, FontSize - 4);

            _faint.normal.textColor =
                new Color(0.65f, 0.68f, 0.76f);


            _fade = Fade();


            _button =
                new GUIStyle(GUI.skin.button);

            _button.fontSize = FontSize;


            _primary =
                new GUIStyle(_button);

            _primary.fontStyle = FontStyle.Bold;
            _primary.normal.background = _accent;
            _primary.hover.background = _accent;
            _primary.normal.textColor = Color.white;
            _primary.hover.textColor = Color.white;
        }


        /// <summary>
        /// Тёмная полоса, растворяющаяся вправо.
        ///
        /// Даёт читаемость текста без видимой
        /// границы панели: правого края просто нет.
        /// </summary>
        private static Texture2D Fade()
        {
            int width = 64;

            var texture =
                new Texture2D(width, 1);

            texture.hideFlags =
                HideFlags.HideAndDontSave;

            texture.wrapMode =
                TextureWrapMode.Clamp;


            for (int x = 0; x < width; x++)
            {
                float t =
                    x / (float)(width - 1);

                // Плотно слева, в ноль справа.
                float alpha =
                    Mathf.Lerp(0.72f, 0f,
                               Mathf.Pow(t, 0.7f));

                texture.SetPixel(
                    x, 0,
                    new Color(0.03f, 0.03f, 0.06f, alpha));
            }


            texture.Apply();

            return texture;
        }


        private static Texture2D Solid(
            Color color)
        {
            var texture =
                new Texture2D(1, 1);

            // Не даём Unity выгрузить текстуру:
            // на неё ссылаются только наши стили.
            texture.hideFlags =
                HideFlags.HideAndDontSave;

            texture.SetPixel(0, 0, color);
            texture.Apply();

            return texture;
        }
    }
}