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
    /// Выбор цели атаки стрелкой.
    ///
    /// Щёлкнули по своему холомему — от него
    /// тянется стрелка за курсором. Навели на
    /// доступную цель — стрелка прилипает к ней,
    /// цель подсвечивается. Щелчок подтверждает.
    ///
    /// Правый клик или Esc отменяют.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    [RequireComponent(typeof(BoardView))]
    public class AttackArrow : MonoBehaviour
    {
        public Camera BoardCamera;

        [Header("Стрелка")]
        public Color ArrowColor =
            new Color(1f, 0.55f, 0.35f);

        public float ArrowWidth = 0.09f;

        public float HeadSize = 0.32f;

        [Tooltip("Высота стрелки над столом")]
        public float ArrowHeight = 0.25f;

        [Header("Подсветка")]
        public Color AttackerColor =
            new Color(1f, 0.85f, 0.3f, 0.9f);

        public Color TargetColor =
            new Color(1f, 0.35f, 0.35f, 0.9f);


        private GameSession _session;
        private BoardView _board;

        private LineRenderer _line;
        private GameObject _head;

        private CardView _attacker;
        private List<GameAction> _arts;
        private CardView _highlighted;

        // Меню артов: показывается ДО стрелки,
        // если у холомема их несколько.
        private List<GameAction> _artMenu;

        // Кадр, в котором началось прицеливание.
        // Щелчок, открывший его, в этом же кадре
        // нельзя принимать за выбор цели — иначе
        // стрелка тут же гасла, не успев появиться.
        private int _startFrame = -1;


        /// <summary>
        /// Идёт выбор цели — обычные клики
        /// по столу в это время не нужны.
        /// </summary>
        public bool Active
        {
            get { return _attacker != null; }
        }


        private bool PickingArt
        {
            get { return _artMenu != null; }
        }


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _board = GetComponent<BoardView>();

            if (BoardCamera == null)
                BoardCamera = Camera.main;

            BuildArrow();
            HideArrow();
        }


        /// <summary>
        /// Начать выбор цели для этого холомема.
        /// </summary>
        public void Begin(
            CardView attacker,
            List<GameAction> artActions)
        {
            _attacker = attacker;
            _arts = artActions;

            _startFrame = Time.frameCount;

            attacker.SetHighlight(true, AttackerColor);


            // Разные арты этого холомема — по одному
            // действию на арт, для меню.
            var perArt =
                artActions
                    .GroupBy(x => x.ArtIndex ?? 0)
                    .Select(g => g.First())
                    .ToList();


            // Арт один — сразу к стрелке.
            // Несколько — сначала спрашиваем какой.
            _artMenu =
                perArt.Count > 1
                    ? perArt
                    : null;
        }


        /// <summary>
        /// Арт выбран — оставляем только его цели
        /// и переходим к стрелке.
        /// </summary>
        private void ChooseArt(
            int artIndex)
        {
            _arts =
                _arts
                    .Where(x => (x.ArtIndex ?? 0) == artIndex)
                    .ToList();

            _artMenu = null;

            _startFrame = Time.frameCount;
        }


        public void Cancel()
        {
            if (_attacker != null)
                _attacker.SetHighlight(false, Color.white);

            ClearTargetHighlight();

            _attacker = null;
            _arts = null;
            _artMenu = null;

            HideArrow();
        }


        private void Update()
        {
            if (!Active)
                return;


            if (RightClicked() || EscapePressed())
            {
                Cancel();
                return;
            }


            // Пока выбирают арт, стрелки нет:
            // её место займёт меню.
            if (PickingArt)
            {
                HideArrow();
                return;
            }


            // Куда смотрит курсор на плоскости стола.
            Vector3 point =
                CursorOnTable();


            var hovered =
                _board.Raycast(
                    BoardCamera,
                    MousePosition()
                );

            var matching =
                hovered == null || hovered.Card == null
                    ? null
                    : _arts
                        .Where(x =>
                            x.TargetCardId ==
                                hovered.Card.Id)
                        .ToList();


            bool valid =
                matching != null &&
                matching.Count > 0;


            if (valid)
            {
                point =
                    hovered.transform.position;

                if (_highlighted != hovered)
                {
                    ClearTargetHighlight();

                    _highlighted = hovered;

                    _highlighted.SetHighlight(
                        true,
                        TargetColor
                    );
                }
            }
            else
            {
                ClearTargetHighlight();
            }


            DrawArrow(
                _attacker.transform.position,
                point,
                valid
            );


            if (!LeftClicked())
                return;


            // Тот самый щелчок, что начал
            // прицеливание, — не выбор цели.
            if (Time.frameCount == _startFrame)
                return;


            if (!valid)
            {
                Cancel();
                return;
            }


            // Арт уже выбран, поэтому на одну цель
            // приходится ровно одно действие.
            var action = matching[0];

            Cancel();

            _session.Apply(action);
        }


        private void OnGUI()
        {
            if (!PickingArt || _attacker == null)
                return;


            float width = 260f;
            float height = 34f * (_artMenu.Count + 2);


            // Меню рядом с атакующим, а не посреди
            // экрана: взгляд уже там.
            Vector3 screen =
                BoardCamera != null
                    ? BoardCamera.WorldToScreenPoint(
                        _attacker.transform.position)
                    : new Vector3(
                        Screen.width * 0.5f,
                        Screen.height * 0.5f, 0f);


            float x =
                Mathf.Clamp(
                    screen.x + 40f,
                    10f,
                    Screen.width - width - 10f);

            float y =
                Mathf.Clamp(
                    Screen.height - screen.y - height * 0.5f,
                    10f,
                    Screen.height - height - 10f);


            GUILayout.BeginArea(
                new Rect(x, y, width, height),
                GUI.skin.box
            );

            GUILayout.Label("Чем бить?");


            foreach (var action in _artMenu)
            {
                if (!GUILayout.Button(
                    ArtName(action),
                    GUILayout.Height(28f)))
                {
                    continue;
                }


                ChooseArt(action.ArtIndex ?? 0);

                GUILayout.EndArea();
                return;
            }


            if (GUILayout.Button("Отмена"))
                Cancel();

            GUILayout.EndArea();
        }


        private string ArtName(
            GameAction action)
        {
            if (_attacker == null ||
                _attacker.Card == null ||
                !action.ArtIndex.HasValue)
            {
                return "Атака";
            }


            var arts =
                _attacker.Card.Arts;

            int index =
                action.ArtIndex.Value;

            return
                index >= 0 && index < arts.Count
                    ? arts[index].Name +
                      "  " + arts[index].Damage
                    : "Атака";
        }


        // =========================================
        // ОТРИСОВКА
        // =========================================

        private void BuildArrow()
        {
            var lineObject =
                new GameObject("AttackArrow");

            lineObject.transform.SetParent(
                transform,
                false
            );

            _line =
                lineObject.AddComponent<LineRenderer>();

            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.material = FlatMaterials.Transparent();
            _line.numCapVertices = 4;


            _head =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad
                );

            _head.name = "ArrowHead";

            var collider =
                _head.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            _head.transform.SetParent(
                transform,
                false
            );

            _head.GetComponent<MeshRenderer>()
                .sharedMaterial =
                    FlatMaterials.Transparent();
        }


        private void DrawArrow(
            Vector3 from,
            Vector3 to,
            bool valid)
        {
            Color color =
                valid
                    ? TargetColor
                    : ArrowColor;


            _line.gameObject.SetActive(true);
            _head.SetActive(true);

            from += Vector3.up * ArrowHeight;
            to += Vector3.up * ArrowHeight;

            _line.startWidth = ArrowWidth;
            _line.endWidth = ArrowWidth;

            _line.startColor = color;
            _line.endColor = color;

            _line.SetPosition(0, from);
            _line.SetPosition(1, to);


            _head.transform.position = to;

            _head.transform.localScale =
                Vector3.one * HeadSize;

            // Наконечник лежит плашмя и смотрит
            // вдоль стрелки.
            Vector3 direction =
                to - from;

            if (direction.sqrMagnitude > 0.0001f)
            {
                _head.transform.rotation =
                    Quaternion.LookRotation(
                        Vector3.up,
                        direction.normalized
                    );
            }


            var renderer =
                _head.GetComponent<MeshRenderer>();

            var block =
                new MaterialPropertyBlock();

            renderer.GetPropertyBlock(block);

            block.SetTexture(
                "_BaseMap",
                FxTextures.Glow()
            );

            block.SetColor("_BaseColor", color);

            renderer.SetPropertyBlock(block);
        }


        private void HideArrow()
        {
            if (_line != null)
                _line.gameObject.SetActive(false);

            if (_head != null)
                _head.SetActive(false);
        }


        private void ClearTargetHighlight()
        {
            if (_highlighted == null)
                return;

            _highlighted.SetHighlight(false, Color.white);
            _highlighted = null;
        }


        private Vector3 CursorOnTable()
        {
            var plane =
                new Plane(Vector3.up, Vector3.zero);

            var ray =
                BoardCamera.ScreenPointToRay(
                    MousePosition()
                );

            float distance;

            return plane.Raycast(ray, out distance)
                ? ray.GetPoint(distance)
                : _attacker.transform.position;
        }


        // =========================================
        // ВВОД
        // =========================================

        private static bool LeftClicked()
        {
#if ENABLE_INPUT_SYSTEM
            return
                Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }


        private static bool RightClicked()
        {
#if ENABLE_INPUT_SYSTEM
            return
                Mouse.current != null &&
                Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }


        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
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
    }
}