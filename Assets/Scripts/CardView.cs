using System.Linq;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Одна карта на столе.
    ///
    /// Префаб: пустой корень, внутри два Quad —
    /// Front смотрит в +Z, Back развёрнут на 180°.
    /// Переворот карты это поворот корня по Y,
    /// поэтому рубашка нужна настоящая, а не шейдерная.
    /// </summary>
    public class CardView : MonoBehaviour
    {
        [Header("Ссылки на части префаба")]
        public MeshRenderer Front;
        public MeshRenderer Back;

        [Tooltip("Рисовать лицо и рубашку без освещения. " +
                 "Освещаемый материал приглушает картинку " +
                 "и съедает контраст мелкого текста — " +
                 "карта выглядит мутнее, чем исходный PNG")]
        public bool UnlitFaces = true;


        [Header("Тень под картой")]
        [Tooltip("Мягкая тень, прижимающая карту " +
                 "к столу. Без неё карты выглядят " +
                 "наклейками поверх экрана")]
        public bool ContactShadow = true;

        [Range(0f, 1f)]
        public float ShadowStrength = 0.35f;

        [Tooltip("Насколько тень больше карты")]
        public float ShadowSpread = 1.06f;

        [Tooltip("Насколько тень ниже карты")]
        public float ShadowDrop = 0.01f;


        private GameObject _shadow;


        [Header("Подписи поверх карты")]
        [Tooltip("Рисовать имя и HP настоящим текстом. " +
                 "Текст — геометрия, он остаётся чётким " +
                 "при любом размере карты на экране, " +
                 "в отличие от текста на картинке")]
        public bool AutoLabels;

        [Tooltip("Высота строки в долях карты")]
        public float LabelHeight = 0.09f;

        [Tooltip("Насколько подписи подняты над лицом")]
        public float LabelLift = 0.012f;


        [Header("Текст способности поверх карты")]
        [Tooltip("Закрыть печатный текст плашкой и " +
                 "написать его заново шрифтом. " +
                 "Печатный мелкий текст нечитаем при " +
                 "любой фильтрации, свой — чёткий всегда")]
        public bool ShowRulesText;

        [Tooltip("Середина текстовой зоны по высоте карты")]
        public float RulesY = -0.16f;

        [Tooltip("Высота текстовой зоны в долях карты")]
        public float RulesHeight = 0.42f;

        [Tooltip("Ширина текстовой зоны в долях карты")]
        public float RulesWidth = 0.92f;

        [Tooltip("Высота строки в долях карты")]
        public float RulesLineHeight = 0.05f;

        [Tooltip("Сколько знаков помещается в строку")]
        public int RulesLineLength = 30;

        [Tooltip("Непрозрачность плашки")]
        [Range(0f, 1f)]
        public float RulesPlateAlpha = 0.86f;


        private TextMesh _rulesText;

        // Подписи, созданные нами: только их
        // и прячем, когда настройку выключают.
        private TextMesh _autoName;
        private TextMesh _autoHp;

        [Tooltip("Если карты лежат не той стороной — снимите галочку")]
        public bool SwapFaces = true;

        [Header("Текст поверх карты (можно не задавать)")]
        public TextMesh NameLabel;
        public TextMesh HpLabel;


        public Card Card { get; private set; }

        public string InstanceId
        {
            get
            {
                return Card == null
                    ? null
                    : Card.Id;
            }
        }


        private void Awake()
        {
            ForceChildOrientation();
        }


        /// <summary>
        /// Лицо смотрит вперёд, рубашка назад —
        /// независимо от того, как собран префаб.
        ///
        /// Если положиться на ручную настройку,
        /// достаточно один раз перепутать поворот
        /// у одного из Quad, и весь стол окажется
        /// разложен рубашкой вверх.
        /// </summary>
        private void ForceChildOrientation()
        {
            // Встроенный Quad в Unity показывает
            // ту грань, что смотрит от нас, поэтому
            // лицо приходится разворачивать.
            float frontAngle =
                SwapFaces ? 180f : 0f;

            float backAngle =
                SwapFaces ? 0f : 180f;

            float frontDepth =
                SwapFaces ? 0.001f : -0.001f;


            if (Front != null)
            {
                Front.transform.localRotation =
                    Quaternion.Euler(0f, frontAngle, 0f);

                Front.transform.localPosition =
                    new Vector3(0f, 0f, frontDepth);
            }


            if (Back != null)
            {
                Back.transform.localRotation =
                    Quaternion.Euler(0f, backAngle, 0f);

                Back.transform.localPosition =
                    new Vector3(0f, 0f, -frontDepth);
            }
        }


        public void Bind(
            Card card,
            bool faceUp)
        {
            ForceChildOrientation();

            Card = card;

            ApplyArt();

            ApplyTint();

            SetFaceUp(faceUp);

            RefreshLabels();
        }


        /// <summary>
        /// Перевести лицо и рубашку на материал
        /// без освещения.
        ///
        /// Материал общий на все карты, а картинки
        /// подставляются через MaterialPropertyBlock,
        /// так что копий материала не появляется
        /// и отрисовка не дробится.
        /// </summary>
        /// <summary>
        /// Мягкая тень под картой.
        ///
        /// Чуть больше карты, приглушённая,
        /// лежит ниже неё. Этого хватает, чтобы
        /// карта перестала выглядеть наклейкой
        /// и начала касаться стола.
        /// </summary>
        private void EnsureShadow()
        {
            if (!ContactShadow || Front == null)
                return;


            if (_shadow != null)
                return;


            _shadow =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad);

            _shadow.name = "Shadow";

            var collider =
                _shadow.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);


            _shadow.transform.SetParent(
                transform, false);


            float faceZ =
                Front.transform.localPosition.z;

            // Ниже лица — с той стороны, где стол.
            float drop =
                faceZ >= 0f ? -ShadowDrop : ShadowDrop;


            _shadow.transform.localPosition =
                new Vector3(0f, -ShadowDrop * 0.5f,
                            faceZ + drop);

            _shadow.transform.localRotation =
                Front.transform.localRotation;

            _shadow.transform.localScale =
                Front.transform.localScale * ShadowSpread;


            var renderer =
                _shadow.GetComponent<MeshRenderer>();

            renderer.sharedMaterial =
                FlatMaterials.Transparent();

            var block =
                new MaterialPropertyBlock();

            var color =
                new Color(0f, 0f, 0f, ShadowStrength);

            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);

            renderer.SetPropertyBlock(block);
        }


        private void MakeUnlit()
        {
            if (!UnlitFaces)
                return;


            var flat =
                FlatMaterials.Opaque();

            if (flat == null)
                return;


            if (Front != null)
                Front.sharedMaterial = flat;

            if (Back != null)
                Back.sharedMaterial = flat;
        }


        private void ApplyArt()
        {
            MakeUnlit();

            EnsureShadow();


            if (Front == null)
                return;


            var texture =
                CardArt.Front(Card);


            // MaterialPropertyBlock вместо material:
            // не плодит копии материала на каждую карту.
            var block =
                new MaterialPropertyBlock();

            Front.GetPropertyBlock(block);

            if (texture != null)
            {
                block.SetTexture("_BaseMap", texture);
                block.SetColor("_BaseColor", Color.white);
            }
            else
            {
                // Текстуру надо именно заменить,
                // а не просто не трогать.
                //
                // Иначе на лицевой стороне остаётся
                // картинка из материала — и если
                // Front и Back делят один материал
                // с рубашкой, все карты без своей
                // картинки выглядят лежащими
                // рубашкой вверх.
                block.SetTexture(
                    "_BaseMap",
                    Texture2D.whiteTexture
                );

                block.SetColor(
                    "_BaseColor",
                    CardArt.PlaceholderColor(Card)
                );
            }

            Front.SetPropertyBlock(block);


            if (Back == null)
                return;


            var backTexture =
                CardArt.Back();

            if (backTexture == null)
                return;


            var backBlock =
                new MaterialPropertyBlock();

            Back.GetPropertyBlock(backBlock);
            backBlock.SetTexture("_BaseMap", backTexture);
            Back.SetPropertyBlock(backBlock);
        }


        public void SetFaceUp(
            bool faceUp)
        {
            transform.localRotation =
                Quaternion.Euler(
                    0f,
                    faceUp ? 0f : 180f,
                    0f
                );
        }


        // =========================================
        // ПОДСВЕТКА
        // =========================================

        private GameObject _halo;


        /// <summary>
        /// Рамка вокруг карты.
        ///
        /// Это квад чуть больше карты, лежащий
        /// прямо под её лицом: по краям видна
        /// цветная кайма, середину закрывает
        /// сама карта.
        /// </summary>
        // Два уровня подсветки.
        //
        // Постоянный — "с картой можно что-то
        // сделать". Временный — стрелка атаки,
        // выбор цели. Временный рисуется поверх,
        // а когда его снимают, возвращается
        // постоянный, а не пустота. Иначе стрелка
        // гасила бы подсветку доступных карт.
        private bool _playable;
        private bool _overridden;

        private bool _dimmed;

        public static Color PlayableColor =
            new Color(0.35f, 0.95f, 0.55f, 1f);


        /// <summary>
        /// С картой сейчас можно что-то сделать.
        /// </summary>
        public void SetPlayable(
            bool on)
        {
            _playable = on;

            if (_overridden)
                return;


            if (on)
                ShowHalo(PlayableColor);
            else
                HideHalo();
        }


        /// <summary>
        /// Приглушить карту.
        ///
        /// Так помечается то, с чем сейчас ничего
        /// не сделать: отдыхающие холомемы и всё,
        /// что не подходит под выбранное действие.
        /// Взгляд сам находит доступное, когда
        /// недоступное уходит в тень.
        /// </summary>
        public void SetDimmed(
            bool on)
        {
            if (_dimmed == on)
                return;


            _dimmed = on;

            ApplyTint();
        }


        private void ApplyTint()
        {
            var color =
                _dimmed
                    ? new Color(0.45f, 0.45f, 0.52f)
                    : Color.white;


            Tint(Front, color);
            Tint(Back, color);
        }


        private static void Tint(
            MeshRenderer renderer,
            Color color)
        {
            if (renderer == null)
                return;


            var block =
                new MaterialPropertyBlock();

            renderer.GetPropertyBlock(block);

            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);

            renderer.SetPropertyBlock(block);
        }


        public void SetHighlight(
            bool on,
            Color color)
        {
            if (!on)
            {
                _overridden = false;

                if (_playable)
                    ShowHalo(PlayableColor);
                else
                    HideHalo();

                return;
            }


            _overridden = true;

            ShowHalo(color);
        }


        private void HideHalo()
        {
            if (_halo != null)
                _halo.SetActive(false);
        }


        private void ShowHalo(
            Color color)
        {
            if (_halo == null)
                BuildHalo();

            _halo.SetActive(true);


            var renderer =
                _halo.GetComponent<MeshRenderer>();

            var block =
                new MaterialPropertyBlock();

            renderer.GetPropertyBlock(block);

            block.SetTexture(
                "_BaseMap",
                Texture2D.whiteTexture
            );

            block.SetColor("_BaseColor", color);

            renderer.SetPropertyBlock(block);
        }


        private void BuildHalo()
        {
            _halo =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad
                );

            _halo.name = "Halo";

            var collider =
                _halo.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);


            _halo.transform.SetParent(
                transform,
                false
            );


            // Берём размер лицевой стороны
            // и раздуваем: у префаба масштаб
            // может стоять и на детях, и на корне.
            Vector3 size =
                Front != null
                    ? Front.transform.localScale
                    : Vector3.one;

            _halo.transform.localScale =
                new Vector3(
                    size.x * 1.16f,
                    size.y * 1.12f,
                    1f
                );


            float depth =
                Front != null
                    ? Front.transform.localPosition.z
                    : 0f;

            // Чуть ближе к рубашке, чем лицо,
            // чтобы карта закрывала середину.
            _halo.transform.localPosition =
                new Vector3(
                    0f,
                    0f,
                    depth * 0.5f
                );

            _halo.transform.localRotation =
                Front != null
                    ? Front.transform.localRotation
                    : Quaternion.identity;


            _halo.GetComponent<MeshRenderer>()
                .sharedMaterial =
                    FlatMaterials.Transparent();
        }


        /// <summary>
        /// Создать подписи, если их не назначили
        /// в префабе.
        ///
        /// Имя внизу, HP в правом верхнем углу —
        /// там же, где они напечатаны на самой
        /// карте, чтобы не спорить с рисунком.
        /// </summary>
        private void EnsureLabels()
        {
            // Галочку сняли — убираем то, что
            // успели создать. Подписи, назначенные
            // в префабе вручную, не трогаем:
            // они не наши.
            if (!AutoLabels)
            {
                Hide(_autoName);
                Hide(_autoHp);

                return;
            }


            if (Front == null)
                return;


            if (NameLabel == null)
            {
                NameLabel =
                    MakeLabel(
                        "AutoName",
                        new Vector2(0f, -0.40f),
                        TextAnchor.MiddleCenter);

                _autoName = NameLabel;
            }


            if (HpLabel == null)
            {
                HpLabel =
                    MakeLabel(
                        "AutoHp",
                        new Vector2(0.30f, 0.42f),
                        TextAnchor.MiddleRight);

                _autoHp = HpLabel;
            }


            Show(_autoName);
            Show(_autoHp);
        }


        private static void Show(
            TextMesh label)
        {
            if (label != null)
                label.gameObject.SetActive(true);
        }


        private static void Hide(
            TextMesh label)
        {
            if (label != null)
                label.gameObject.SetActive(false);
        }


        private TextMesh MakeLabel(
            string name,
            Vector2 place,
            TextAnchor anchor)
        {
            var host =
                new GameObject(name);

            host.transform.SetParent(transform, false);


            // Лицо карты может смотреть в любую
            // сторону — зависит от того, как собран
            // префаб. Подпись кладём с той же
            // стороны и на чуть меньшей глубине,
            // чтобы она была поверх рисунка.
            float faceZ =
                Front.transform.localPosition.z;

            float lift =
                faceZ >= 0f ? LabelLift : -LabelLift;

            host.transform.localPosition =
                new Vector3(
                    place.x,
                    place.y,
                    faceZ + lift);

            host.transform.localRotation =
                Front.transform.localRotation;


            var text =
                host.AddComponent<TextMesh>();

            text.fontSize = 72;
            text.characterSize = LabelHeight * 0.5f;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;


            // Тёмная подложка: белый текст на светлой
            // иллюстрации иначе теряется.
            var plate =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad);

            var collider =
                plate.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            plate.name = "Plate";

            plate.transform.SetParent(
                host.transform, false);

            plate.transform.localPosition =
                new Vector3(0f, 0f, lift * 0.5f);

            plate.transform.localScale =
                new Vector3(0.92f, LabelHeight, 1f);

            var renderer =
                plate.GetComponent<MeshRenderer>();

            renderer.sharedMaterial =
                FlatMaterials.Transparent();

            var block =
                new MaterialPropertyBlock();

            block.SetColor(
                "_BaseColor",
                new Color(0f, 0f, 0f, 0.62f));

            block.SetColor(
                "_Color",
                new Color(0f, 0f, 0f, 0.62f));

            renderer.SetPropertyBlock(block);


            return text;
        }


        /// <summary>
        /// Закрыть печатный текст и написать его
        /// заново — шрифтом, по-русски, если есть
        /// перевод.
        /// </summary>
        private void EnsureRulesText()
        {
            if (!ShowRulesText)
            {
                if (_rulesText != null)
                    _rulesText.transform.parent
                        .gameObject.SetActive(false);

                return;
            }


            if (Front == null)
                return;


            string body =
                RulesBody();


            if (string.IsNullOrEmpty(body))
            {
                if (_rulesText != null)
                    _rulesText.transform.parent.gameObject
                        .SetActive(false);

                return;
            }


            if (_rulesText == null)
                BuildRulesText();


            _rulesText.transform.parent.gameObject
                .SetActive(true);

            _rulesText.text =
                Wrap(body, RulesLineLength);
        }


        /// <summary>
        /// Что писать: тексты способностей карты.
        /// </summary>
        private string RulesBody()
        {
            if (Card == null ||
                Card.Abilities == null)
            {
                return string.Empty;
            }


            var text =
                new System.Text.StringBuilder();


            foreach (var ability in Card.Abilities)
            {
                if (ability == null ||
                    string.IsNullOrEmpty(ability.Description))
                {
                    continue;
                }


                if (text.Length > 0)
                    text.Append("\n");


                text.Append(ability.Name);
                text.Append(": ");

                text.Append(
                    CardTranslations.Ability(
                        Card.DefinitionId,
                        ability.Name,
                        ability.Description));
            }


            return text.ToString();
        }


        /// <summary>
        /// Разбить текст по строкам: TextMesh сам
        /// переносить не умеет.
        /// </summary>
        private static string Wrap(
            string text,
            int width)
        {
            if (width < 8)
                width = 8;


            var result =
                new System.Text.StringBuilder();

            int line = 0;


            foreach (var word in text.Split(' '))
            {
                if (word.Contains("\n"))
                    line = 0;


                if (line + word.Length > width && line > 0)
                {
                    result.Append('\n');
                    line = 0;
                }


                result.Append(word);
                result.Append(' ');

                line += word.Length + 1;
            }


            return result.ToString();
        }


        private void BuildRulesText()
        {
            var host =
                new GameObject("RulesText");

            host.transform.SetParent(transform, false);


            float faceZ =
                Front.transform.localPosition.z;

            float lift =
                faceZ >= 0f ? LabelLift : -LabelLift;


            host.transform.localPosition =
                new Vector3(0f, RulesY, faceZ + lift);

            host.transform.localRotation =
                Front.transform.localRotation;


            // Плашка закрывает печатный текст —
            // иначе свой ляжет поверх чужого
            // и оба станут нечитаемы.
            var plate =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad);

            plate.name = "Plate";

            var collider =
                plate.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);


            plate.transform.SetParent(
                host.transform, false);

            plate.transform.localPosition =
                new Vector3(0f, 0f, -lift * 0.4f);

            plate.transform.localScale =
                new Vector3(RulesWidth, RulesHeight, 1f);


            var renderer =
                plate.GetComponent<MeshRenderer>();

            renderer.sharedMaterial =
                FlatMaterials.Transparent();

            var block =
                new MaterialPropertyBlock();

            var color =
                new Color(0.05f, 0.05f, 0.08f,
                          RulesPlateAlpha);

            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);

            renderer.SetPropertyBlock(block);


            // Сам текст — от левого верхнего угла
            // плашки вниз.
            var textObject =
                new GameObject("Text");

            textObject.transform.SetParent(
                host.transform, false);

            textObject.transform.localPosition =
                new Vector3(
                    -RulesWidth * 0.5f + 0.03f,
                    RulesHeight * 0.5f - 0.02f,
                    0f);


            _rulesText =
                textObject.AddComponent<TextMesh>();

            _rulesText.fontSize = 72;
            _rulesText.characterSize = RulesLineHeight * 0.5f;
            _rulesText.anchor = TextAnchor.UpperLeft;
            _rulesText.alignment = TextAlignment.Left;
            _rulesText.color = Color.white;
            _rulesText.lineSpacing = 0.9f;
        }


        public void RefreshLabels()
        {
            if (Card == null)
                return;


            EnsureLabels();

            EnsureRulesText();


            if (NameLabel != null)
            {
                NameLabel.text =
                    Card.Name;
            }


            if (HpLabel == null)
                return;


            if (Card.Type == CardType.Holomem)
            {
                HpLabel.text =
                    Card.CurrentHp +
                    " / " +
                    Card.GetMaxHp();
            }
            else
            {
                HpLabel.text =
                    string.Empty;
            }
        }
    }
}