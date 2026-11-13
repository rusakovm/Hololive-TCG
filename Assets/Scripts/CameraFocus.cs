using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Два взгляда: на руку и на стол.
    ///
    /// Так играют живые люди: держат карты близко
    /// к глазам, чтобы читать, и поднимают голову,
    /// чтобы увидеть поле. Вместо того чтобы искать
    /// один компромиссный масштаб, в котором плохо
    /// и то и другое, камера просто переводит взгляд.
    ///
    /// W — на стол, S — на руку. Клавиши и обе
    /// точки обзора настраиваются в инспекторе.
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    public class CameraFocus : MonoBehaviour
    {
        public enum View
        {
            Table,
            Hand
        }


        [Header("Камера")]
        [Tooltip("Если пусто — берётся главная камера сцены")]
        public Camera Target;

        [Tooltip("С какого вида начинать")]
        public View StartView = View.Table;


        [Header("Клавиши")]
        public KeyCode TableKey = KeyCode.W;
        public KeyCode HandKey = KeyCode.S;

        [Tooltip("Переключает между видами одной клавишей")]
        public KeyCode ToggleKey = KeyCode.Tab;


        [Header("Вид на стол")]
        [Range(10f, 90f)]
        [Tooltip("Наклон камеры. 90 — строго сверху, " +
                 "меньше — взгляд из-за своего края стола")]
        public float TablePitch = 72f;

        [Tooltip("Расстояние до точки взгляда. " +
                 "В режиме без перспективы на размер " +
                 "НЕ влияет — там размер задаёт " +
                 "Table Ortho Size")]
        public float TableDistance = 9f;

        [Tooltip("Дополнительный подъём, если нужно")]
        public float TableHeight = 0f;

        [Tooltip("Угол обзора. Работает только когда " +
                 "режим без перспективы выключен")]
        public float TableFov = 55f;

        [Tooltip("Смотреть на стол без перспективы. " +
                 "Дальние карты не сжимаются и не " +
                 "мылятся сильнее ближних — весь стол " +
                 "одинаково чёткий")]
        public bool TableOrthographic = true;

        [Tooltip("Половина высоты видимой области " +
                 "для вида без перспективы. Меньше — " +
                 "крупнее карты")]
        public float TableOrthoSize = 5.5f;

        [Tooltip("Сдвиг точки, на которую смотрим, " +
                 "к своей стороне")]
        public float TableShift = 0f;


        [Header("Вид на руку")]
        [Range(10f, 90f)]
        [Tooltip("Наклон камеры над рукой. Живой игрок " +
                 "смотрит на карты под углом, а не сверху — " +
                 "30-45 выглядит естественно")]
        public float HandPitch = 38f;

        [Tooltip("Расстояние до руки")]
        public float HandDistance = 3.4f;

        [Tooltip("Дополнительный подъём, если нужно")]
        public float HandHeight = 0f;

        [Tooltip("Угол обзора камеры. Меньше — крупнее карты")]
        public float HandFov = 38f;

        [Tooltip("Куда смотреть относительно руки: " +
                 "плюс — дальше в стол")]
        public float HandShift = 0.6f;


        [Header("Плавность")]
        [Tooltip("Скорость перевода взгляда")]
        public float MoveSpeed = 6f;


        [Header("Рука игрока")]
        [Tooltip("Отодвинуть руку от центра стола (+) " +
                 "или придвинуть (−). Меняется на лету")]
        public float HandAway = 0f;

        [Tooltip("Сдвинуть руку вбок")]
        public float HandSide = 0f;

        [Tooltip("Поднять руку над столом")]
        public float HandRaise = 0f;


        [Header("Размер карт")]
        [Tooltip("Размер карт на столе. Меняется на лету")]
        public float StageCardScale = 1f;

        [Tooltip("Размер карт в руке")]
        public float HandCardScale = 1.1f;


        [Header("Поведение")]
        [Tooltip("Когда берёте карту из руки для Bloom " +
                 "или прикрепления, взгляд сам переходит " +
                 "на стол — там надо указать цель")]
        public bool LookAtTableWhenPlaying = true;

        [Tooltip("После того как карта сыграна, вернуться " +
                 "к руке")]
        public bool ReturnToHandAfterPlay;


        private BoardView _board;
        private View _view;

        private float _lastStage = -1f;
        private float _lastHand = -1f;


        public View Current
        {
            get { return _view; }
        }


        private void Awake()
        {
            _board = GetComponent<BoardView>();

            _view = StartView;

            if (Target == null)
                Target = Camera.main;


            // Если камера не помечена как Main,
            // Camera.main вернёт пусто — и тогда
            // ни одна настройка вида не сработает,
            // молча. Берём любую камеру сцены.
            if (Target == null)
                Target = FindObjectOfType<Camera>();


            if (Target == null)
            {
                Debug.LogWarning(
                    "CameraFocus: камера не найдена. " +
                    "Укажите её в поле Target."
                );
            }
        }


        private void Start()
        {
            ApplyScales(true);

            // Первый кадр ставим камеру сразу,
            // без плавного подъезда.
            Move(1f);
        }


        public void LookAtTable()
        {
            _view = View.Table;
        }


        public void LookAtHand()
        {
            _view = View.Hand;
        }


        public void Toggle()
        {
            _view =
                _view == View.Table
                    ? View.Hand
                    : View.Table;
        }


        /// <summary>
        /// Где теряется размер карт.
        ///
        /// Печатает: что стоит в настройке, что
        /// лежит в раскладке стола и какой масштаб
        /// у самих объектов карт. По трём числам
        /// сразу видно, на каком шаге значение
        /// не доходит.
        /// </summary>
        [ContextMenu("Проверить камеру")]
        public void CheckCamera()
        {
            if (Target == null)
            {
                Debug.LogWarning(
                    "Камера не назначена и не найдена.");

                return;
            }


            bool ortho =
                TableOrthographic && _view == View.Table;


            Debug.Log(
                "Камера: " + Target.name +
                "\nВид: " + _view +
                "\nБез перспективы: " + Target.orthographic +
                "\nРазмер области: " +
                Target.orthographicSize.ToString("0.00") +
                "\nУгол обзора: " +
                Target.fieldOfView.ToString("0.0") +
                "\nПоложение: " + Target.transform.position +
                "\n\n" +
                (ortho
                    ? "Сейчас работают только Table Pitch " +
                      "и Table Ortho Size. Расстояние и угол " +
                      "обзора в этом режиме ни на что не влияют."
                    : "Работают Table Pitch, Table Distance " +
                      "и Table Fov.")
            );
        }


        [ContextMenu("Проверить размер карт")]
        public void CheckScales()
        {
            var text =
                new System.Text.StringBuilder();

            text.AppendLine(
                "В настройке: стол " + StageCardScale +
                ", рука " + HandCardScale);

            text.AppendLine(
                "В раскладке: стол " +
                _board.PlayerZones.StageScale +
                ", рука " +
                _board.PlayerZones.HandScale);


            int shown = 0;

            foreach (var view in _board.AllViews)
            {
                if (view == null || view.Card == null)
                    continue;


                text.AppendLine(
                    view.Card.Name + " [" +
                    view.Card.Id + "]: масштаб " +
                    view.transform.localScale.x.ToString("0.00"));


                if (++shown >= 6)
                    break;
            }


            Debug.Log(text.ToString());
        }


        private void Update()
        {
            ReadKeys();

            ApplyScales(false);

            Move(Time.deltaTime * MoveSpeed);
        }


        private void ReadKeys()
        {
            if (Pressed(TableKey))
                LookAtTable();

            if (Pressed(HandKey))
                LookAtHand();

            if (Pressed(ToggleKey))
                Toggle();
        }


        /// <summary>
        /// Размеры карт можно крутить прямо во время
        /// игры: значения уезжают в зоны стола.
        /// </summary>
        private void ApplyScales(
            bool force)
        {
            bool changed = false;

            if (force ||
                !Mathf.Approximately(_lastStage, StageCardScale))
            {
                _board.PlayerZones.StageScale = StageCardScale;
                _board.BotZones.StageScale = StageCardScale;

                _lastStage = StageCardScale;

                changed = true;
            }


            if (force ||
                !Mathf.Approximately(_lastHand, HandCardScale))
            {
                _board.PlayerZones.HandScale = HandCardScale;
                _board.BotZones.HandScale = HandCardScale;

                _lastHand = HandCardScale;

                changed = true;
            }


            // Положение своей руки — тоже отсюда,
            // чтобы подбирать вид и руку вместе,
            // не лазая по двум компонентам.
            var mine =
                _board.PlayerZones;

            if (force ||
                !Mathf.Approximately(
                    mine.HandDistanceOffset, HandAway) ||
                !Mathf.Approximately(
                    mine.HandSideOffset, HandSide) ||
                !Mathf.Approximately(
                    mine.HandHeightOffset, HandRaise))
            {
                mine.HandDistanceOffset = HandAway;
                mine.HandSideOffset = HandSide;
                mine.HandHeightOffset = HandRaise;

                changed = true;
            }


            // Размеры и места карт берутся при
            // перестроении стола, а оно бывает
            // только когда меняется партия.
            // Поэтому после правки настроек просим
            // разложить стол заново — иначе новое
            // значение лежит в раскладке, но
            // ни на что не влияет.
            if (changed)
                _board.Rebuild();
        }


        private void Move(
            float t)
        {
            if (Target == null)
                return;


            Vector3 focus;
            Vector3 position;
            float fov;

            Compute(out focus, out position, out fov);


            float step =
                Mathf.Clamp01(t);

            Target.transform.position =
                Vector3.Lerp(
                    Target.transform.position,
                    position,
                    step);

            Target.transform.rotation =
                Quaternion.Slerp(
                    Target.transform.rotation,
                    Quaternion.LookRotation(
                        focus - position),
                    step);

            // Вид без перспективы — только для стола:
            // рука под наклоном без перспективы
            // выглядит неестественно.
            bool ortho =
                TableOrthographic &&
                _view == View.Table;

            Target.orthographic = ortho;


            if (ortho)
            {
                Target.orthographicSize =
                    Mathf.Lerp(
                        Target.orthographicSize,
                        TableOrthoSize,
                        step);
            }
            else
            {
                Target.fieldOfView =
                    Mathf.Lerp(
                        Target.fieldOfView,
                        fov,
                        step);
            }
        }


        /// <summary>
        /// Куда смотреть и откуда.
        ///
        /// Направление "к себе" берётся от середины
        /// стола к своей руке — тогда настройки
        /// не зависят от того, как повёрнут стол
        /// в сцене.
        /// </summary>
        private void Compute(
            out Vector3 focus,
            out Vector3 position,
            out float fov)
        {
            var zones =
                _board.PlayerZones;

            Vector3 middle =
                zones.Center != null &&
                _board.BotZones.Center != null
                    ? (zones.Center.position +
                       _board.BotZones.Center.position) * 0.5f
                    : transform.position;


            Vector3 hand =
                zones.Hand != null
                    ? zones.Hand.position
                    : middle;


            Vector3 toMe =
                hand - middle;

            toMe.y = 0f;

            if (toMe.sqrMagnitude < 0.0001f)
                toMe = Vector3.back;

            toMe.Normalize();


            if (_view == View.Table)
            {
                focus = middle + toMe * TableShift;

                position =
                    focus +
                    Direction(toMe, TablePitch) * TableDistance +
                    Vector3.up * TableHeight;

                fov = TableFov;

                return;
            }


            focus = hand - toMe * HandShift;

            position =
                focus +
                Direction(toMe, HandPitch) * HandDistance +
                Vector3.up * HandHeight;

            fov = HandFov;
        }


        /// <summary>
        /// Куда отойти от точки взгляда при
        /// заданном наклоне.
        ///
        /// 90 градусов — прямо вверх, взгляд строго
        /// сверху. Меньше — камера отходит назад,
        /// к своему краю стола, и смотрит
        /// под углом, как человек.
        /// </summary>
        private static Vector3 Direction(
            Vector3 back,
            float pitch)
        {
            float radians =
                pitch * Mathf.Deg2Rad;

            return (back * Mathf.Cos(radians) +
                    Vector3.up * Mathf.Sin(radians))
                   .normalized;
        }


        // Проект может стоять и на старой системе
        // ввода, и на новой.
        private static bool Pressed(
            KeyCode key)
        {
            if (key == KeyCode.None)
                return false;


#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return false;


            switch (key)
            {
                case KeyCode.W:
                    return Keyboard.current.wKey.wasPressedThisFrame;

                case KeyCode.S:
                    return Keyboard.current.sKey.wasPressedThisFrame;

                case KeyCode.A:
                    return Keyboard.current.aKey.wasPressedThisFrame;

                case KeyCode.D:
                    return Keyboard.current.dKey.wasPressedThisFrame;

                case KeyCode.Tab:
                    return Keyboard.current.tabKey.wasPressedThisFrame;

                case KeyCode.Space:
                    return Keyboard.current.spaceKey.wasPressedThisFrame;

                default:
                    return false;
            }
#else
            return Input.GetKeyDown(key);
#endif
        }
    }
}