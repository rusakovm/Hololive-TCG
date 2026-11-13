using System.Text;

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Разглядывание карты.
    ///
    /// Курсор над картой — она приподнимается.
    /// Зажат Ctrl — показывается крупно, а рядом
    /// текст карты, собранный из данных движка.
    ///
    /// Текст берётся не с картинки, а из базы,
    /// поэтому читается при любом разрешении
    /// исходника.
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    public class CardZoom : MonoBehaviour
    {
        public enum Placement
        {
            Right,
            Left,
            Center,
            NearCursor
        }


        public Camera BoardCamera;

        [Header("Где показывать")]
        public Placement Where = Placement.Right;

        [Tooltip("Отступ от края экрана, пикселей")]
        public float Margin = 24f;

        [Tooltip("Доля высоты экрана под карту")]
        [Range(0.3f, 1f)]
        public float ZoomHeight = 0.8f;

        [Tooltip("Отношение ширины к высоте. " +
                 "0.716 — пропорции настоящей карты " +
                 "63 на 88 мм")]
        [Range(0.4f, 1.5f)]
        public float CardAspect = 0.716f;

        [Tooltip("Показывать только при зажатом Ctrl")]
        public bool RequireCtrl = true;

        [Header("Текст карты")]
        [Tooltip("Показывать описание рядом с картой")]
        public bool ShowDetails = true;

        [Tooltip("Ширина панели с текстом, пикселей")]
        public float DetailsWidth = 320f;

        [Range(9, 28)]
        public int FontSize = 14;

        [Header("Оформление")]
        public bool ShowBackground = true;

        public Color BackgroundColor =
            new Color(0.05f, 0.05f, 0.08f, 0.85f);

        public Color TextColor =
            new Color(0.95f, 0.95f, 0.95f);


        private BoardView _board;
        private CardView _hovered;
        private GUIStyle _textStyle;


        private void Awake()
        {
            _board = GetComponent<BoardView>();

            if (BoardCamera == null)
                BoardCamera = Camera.main;
        }


        private void Update()
        {
            _hovered =
                _board.Raycast(
                    BoardCamera,
                    MousePosition()
                );


            _board.HoveredCardId =
                _hovered == null || _hovered.Card == null
                    ? null
                    : _hovered.Card.Id;
        }


        private void OnGUI()
        {
            if (_hovered == null || _hovered.Card == null)
                return;


            if (RequireCtrl && !CtrlHeld())
                return;


            EnsureStyle();


            var card =
                _hovered.Card;

            float cardHeight =
                Screen.height * ZoomHeight;

            float cardWidth =
                cardHeight * CardAspect;

            float panelWidth =
                ShowDetails
                    ? cardWidth + DetailsWidth + Margin
                    : cardWidth;


            Vector2 origin =
                Origin(panelWidth, cardHeight);


            if (ShowBackground)
            {
                DrawBox(
                    new Rect(
                        origin.x - 10f,
                        origin.y - 10f,
                        panelWidth + 20f,
                        cardHeight + 20f
                    ),
                    BackgroundColor
                );
            }


            var cardRect =
                new Rect(
                    origin.x,
                    origin.y,
                    cardWidth,
                    cardHeight
                );

            var texture =
                CardArt.Front(card);

            if (texture != null)
            {
                GUI.DrawTexture(
                    cardRect,
                    texture,
                    ScaleMode.ScaleToFit
                );
            }
            else
            {
                DrawBox(
                    cardRect,
                    CardArt.PlaceholderColor(card)
                );
            }


            if (!ShowDetails)
                return;


            var textRect =
                new Rect(
                    origin.x + cardWidth + Margin,
                    origin.y,
                    DetailsWidth,
                    cardHeight
                );

            GUI.Label(
                textRect,
                Describe(card),
                _textStyle
            );
        }


        private Vector2 Origin(
            float width,
            float height)
        {
            float y =
                (Screen.height - height) * 0.5f;


            switch (Where)
            {
                case Placement.Left:
                    return new Vector2(Margin, y);

                case Placement.Center:
                    return new Vector2(
                        (Screen.width - width) * 0.5f,
                        y
                    );

                case Placement.NearCursor:
                    {
                        Vector3 mouse =
                            MousePosition();

                        float x =
                            Mathf.Min(
                                mouse.x + 20f,
                                Screen.width - width - Margin
                            );

                        float cursorY =
                            Mathf.Clamp(
                                Screen.height - mouse.y - height * 0.5f,
                                Margin,
                                Screen.height - height - Margin
                            );

                        return new Vector2(x, cursorY);
                    }

                default:
                    return new Vector2(
                        Screen.width - width - Margin,
                        y
                    );
            }
        }


        /// <summary>
        /// Текст карты из данных движка.
        /// </summary>
        private static string Describe(
            Card card)
        {
            var text =
                new StringBuilder();

            text.AppendLine(card.Name);


            if (card.Type == CardType.Holomem)
            {
                text.Append(
                    card.Level.HasValue
                        ? card.Level.Value.ToString()
                        : "Holomem"
                );

                text.Append("   ");
                text.Append(card.Color);

                if (card.ExtraColors.Count > 0)
                {
                    foreach (var extra in card.ExtraColors)
                    {
                        text.Append(" / ");
                        text.Append(extra);
                    }
                }

                text.AppendLine();

                text.AppendLine(
                    "HP " + card.CurrentHp +
                    " / " + card.GetMaxHp() +
                    "   Baton Pass " + card.BatonPass
                );

                if (card.IsBuzz)
                    text.AppendLine("BUZZ");
            }
            else if (card.Type == CardType.Support)
            {
                text.AppendLine(
                    "Support · " +
                    card.SupportType +
                    (card.IsLimited ? " · LIMITED" : "")
                );
            }


            if (card.Tags.Count > 0)
                text.AppendLine(string.Join(" ", card.Tags.ToArray()));


            foreach (var art in card.Arts)
            {
                text.AppendLine();

                text.Append(Cost(art));
                text.Append("  ");
                text.Append(art.Name);
                text.Append("   ");
                text.Append(art.Damage);

                if (art.CriticalColor.HasValue &&
                    art.CriticalBonus != 0)
                {
                    text.Append(
                        "  (+" + art.CriticalBonus +
                        " по " + art.CriticalColor.Value + ")"
                    );
                }

                text.AppendLine();
            }


            foreach (var ability in card.Abilities)
            {
                if (string.IsNullOrEmpty(ability.Description))
                    continue;

                text.AppendLine();
                text.AppendLine(ability.Name);

                // Русский текст, если он есть;
                // иначе оригинал.
                text.AppendLine(
                    CardTranslations.Ability(
                        card.DefinitionId,
                        ability.Name,
                        ability.Description));
            }


            foreach (var support in card.AttachedSupports)
            {
                text.AppendLine();
                text.AppendLine("Прикреплено: " + support.Name);
            }


            return text.ToString();
        }


        private static string Cost(
            Art art)
        {
            if (art.Cost.Count == 0)
                return "-";


            var text =
                new StringBuilder();

            foreach (var part in art.Cost)
            {
                if (text.Length > 0)
                    text.Append("+");

                text.Append(part.Amount);
                text.Append(part.Color.ToString().Substring(0, 1));
            }

            return text.ToString();
        }


        private void EnsureStyle()
        {
            if (_textStyle != null &&
                _textStyle.fontSize == FontSize)
            {
                return;
            }


            _textStyle =
                new GUIStyle(GUI.skin.label);

            _textStyle.fontSize = FontSize;
            _textStyle.wordWrap = true;
            _textStyle.alignment = TextAnchor.UpperLeft;
            _textStyle.normal.textColor = TextColor;
        }


        private static Texture2D _box;


        private static void DrawBox(
            Rect rect,
            Color color)
        {
            if (_box == null)
            {
                _box =
                    new Texture2D(1, 1);

                _box.SetPixel(0, 0, Color.white);
                _box.Apply();
            }


            var previous = GUI.color;

            GUI.color = color;

            GUI.DrawTexture(rect, _box);

            GUI.color = previous;
        }


        private static bool CtrlHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return
                Keyboard.current != null &&
                (Keyboard.current.leftCtrlKey.isPressed ||
                 Keyboard.current.rightCtrlKey.isPressed);
#else
            return
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);
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