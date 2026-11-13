using System.Collections.Generic;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Врезка при применении скилла оси.
    ///
    /// Персонаж влетает из-за края экрана, стол
    /// приглушается, появляется название скилла —
    /// и через мгновение всё уходит обратно.
    ///
    /// Картинка берётся из той же папки, что и
    /// карты: у оси есть свой номер, значит есть
    /// и рисунок. Новых ассетов не нужно, а когда
    /// появятся отдельные портреты, достаточно
    /// будет положить их в Resources/Oshi.
    ///
    /// Длительность намеренно около секунды:
    /// на двадцатой партии трёхсекундный ролик
    /// хочется пропустить.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class OshiCutIn : MonoBehaviour
    {
        [Header("Длительность")]
        [Tooltip("Обычный скилл оси")]
        public float Duration = 0.9f;

        [Tooltip("SP-скилл: дороже, значит заметнее")]
        public float SpDuration = 1.5f;


        [Header("Вид")]
        [Range(0f, 1f)]
        [Tooltip("Насколько приглушается стол")]
        public float Dim = 0.55f;

        [Tooltip("Какую долю высоты экрана занимает " +
                 "персонаж")]
        [Range(0.3f, 1f)]
        public float CharacterHeight = 0.8f;

        [Tooltip("Влетает справа. Снимите — будет слева")]
        public bool FromRight = true;

        [Tooltip("Цвет полосы за персонажем")]
        public Color BandColor =
            new Color(0.12f, 0.55f, 0.95f, 0.85f);

        [Tooltip("Цвет полосы у SP-скилла")]
        public Color SpBandColor =
            new Color(0.85f, 0.25f, 0.65f, 0.9f);


        [Header("Чужие скиллы")]
        [Tooltip("Показывать врезку и когда скилл " +
                 "применяет соперник")]
        public bool ShowForOpponent = true;


        private GameSession _session;

        private Texture2D _art;
        private string _oshiName;
        private string _skillName;
        private bool _isSp;

        private float _time = -1f;
        private float _length;

        private Texture2D _white;
        private GUIStyle _name;
        private GUIStyle _skill;
        private GUIStyle _kind;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
        }


        private void OnEnable()
        {
            _session.EventsProduced += OnEvents;
        }


        private void OnDisable()
        {
            _session.EventsProduced -= OnEvents;
        }


        // =========================================
        // ЗАПУСК
        // =========================================

        private void OnEvents(
            List<GameEvent> events)
        {
            foreach (var e in events)
            {
                if (e.Type != GameEventType.OshiSkillUsed)
                    continue;


                if (!ShowForOpponent &&
                    e.PlayerIndex != _session.LocalPlayerIndex)
                {
                    continue;
                }


                Show(e);
            }
        }


        private void Show(
            GameEvent e)
        {
            var state =
                _session.State;

            if (state == null)
                return;


            var oshi =
                state.Players[e.PlayerIndex].Oshi;

            if (oshi == null)
                return;


            // SP отличаем по имени скилла: движок
            // сообщает именно его.
            bool sp =
                oshi.SpOshiSkill != null &&
                oshi.SpOshiSkill.Name == e.SourceName;


            Play(
                CardArt.ById(oshi.Id),
                oshi.Name,
                string.IsNullOrEmpty(e.SourceName)
                    ? (sp ? "SP Oshi Skill" : "Oshi Skill")
                    : e.SourceName,
                sp);
        }


        /// <summary>
        /// Показать врезку. Можно вызывать и руками.
        /// </summary>
        public void Play(
            Texture2D art,
            string oshiName,
            string skillName,
            bool sp)
        {
            _art = art;
            _oshiName = oshiName;
            _skillName = skillName;
            _isSp = sp;

            _length = sp ? SpDuration : Duration;
            _time = 0f;


            GameAudio.Play(
                sp ? GameAudio.Victory : GameAudio.Effect);
        }


        /// <summary>
        /// Показать врезку прямо сейчас — проверить
        /// вид, не дожидаясь подходящего момента
        /// в партии.
        /// </summary>
        [ContextMenu("Проверить врезку")]
        public void Preview()
        {
            var state =
                _session != null ? _session.State : null;


            var oshi =
                state != null
                    ? state.Players[
                        _session.LocalPlayerIndex].Oshi
                    : null;


            if (oshi == null)
            {
                Debug.LogWarning(
                    "Врезке нужна ось: запустите партию.");

                return;
            }


            Play(
                CardArt.ById(oshi.Id),
                oshi.Name,
                oshi.OshiSkill != null
                    ? oshi.OshiSkill.Name
                    : "Oshi Skill",
                false);
        }


        private void Update()
        {
            if (_time < 0f)
                return;


            _time += Time.deltaTime;

            if (_time >= _length)
                _time = -1f;
        }


        // =========================================
        // ОТРИСОВКА
        // =========================================

        private void OnGUI()
        {
            if (_time < 0f)
                return;


            EnsureStyles();


            float t =
                Mathf.Clamp01(_time / _length);


            // Три части: влёт, показ, уход.
            float slide;
            float fade;

            if (t < 0.22f)
            {
                slide = 1f - (t / 0.22f);
                fade = t / 0.22f;
            }
            else if (t < 0.78f)
            {
                slide = 0f;
                fade = 1f;
            }
            else
            {
                slide = (t - 0.78f) / 0.22f;
                fade = 1f - (t - 0.78f) / 0.22f;
            }


            // Смягчаем движение: резкий влёт,
            // мягкая остановка.
            slide = slide * slide;


            DrawDim(fade);

            DrawBand(slide, fade);

            DrawCharacter(slide, fade);

            DrawText(slide, fade);
        }


        private void DrawDim(
            float fade)
        {
            GUI.color =
                new Color(0f, 0f, 0f, Dim * fade);

            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                _white);

            GUI.color = Color.white;
        }


        private void DrawBand(
            float slide,
            float fade)
        {
            float height =
                Screen.height * CharacterHeight;

            float y =
                (Screen.height - height) * 0.5f;


            var color =
                _isSp ? SpBandColor : BandColor;

            color.a *= fade;

            GUI.color = color;


            // Полоса едет в противоположную сторону
            // от персонажа — так появляется
            // ощущение глубины.
            float shift =
                slide * Screen.width *
                (FromRight ? -0.25f : 0.25f);

            GUI.DrawTexture(
                new Rect(
                    shift,
                    y + height * 0.55f,
                    Screen.width,
                    height * 0.28f),
                _white);

            GUI.color = Color.white;
        }


        private void DrawCharacter(
            float slide,
            float fade)
        {
            if (_art == null)
                return;


            float height =
                Screen.height * CharacterHeight;

            float width =
                height * _art.width / (float)_art.height;


            float rest =
                FromRight
                    ? Screen.width - width * 0.85f
                    : -width * 0.15f;

            float offscreen =
                FromRight
                    ? Screen.width + 20f
                    : -width - 20f;


            float x =
                Mathf.Lerp(rest, offscreen, slide);


            GUI.color =
                new Color(1f, 1f, 1f, fade);

            GUI.DrawTexture(
                new Rect(
                    x,
                    (Screen.height - height) * 0.5f,
                    width,
                    height),
                _art,
                ScaleMode.ScaleToFit);

            GUI.color = Color.white;
        }


        private void DrawText(
            float slide,
            float fade)
        {
            float height =
                Screen.height * CharacterHeight;

            float y =
                (Screen.height - height) * 0.5f +
                height * 0.52f;


            // Текст выезжает с другой стороны.
            float x =
                Mathf.Lerp(
                    60f,
                    FromRight ? -Screen.width : Screen.width,
                    slide);


            GUI.color =
                new Color(1f, 1f, 1f, fade);

            GUI.Label(
                new Rect(x, y, Screen.width * 0.6f, 34f),
                _oshiName,
                _name);

            GUI.Label(
                new Rect(x, y + 34f, Screen.width * 0.6f, 24f),
                _isSp ? "SP OSHI SKILL" : "OSHI SKILL",
                _kind);

            GUI.Label(
                new Rect(x, y + 58f, Screen.width * 0.6f, 30f),
                _skillName,
                _skill);

            GUI.color = Color.white;
        }


        private void EnsureStyles()
        {
            if (_white != null && _name != null)
                return;


            _white = new Texture2D(1, 1);

            _white.hideFlags =
                HideFlags.HideAndDontSave;

            _white.SetPixel(0, 0, Color.white);
            _white.Apply();


            _name =
                new GUIStyle(GUI.skin.label);

            _name.fontSize = 30;
            _name.fontStyle = FontStyle.Bold;
            _name.normal.textColor = Color.white;


            _kind =
                new GUIStyle(GUI.skin.label);

            _kind.fontSize = 14;
            _kind.normal.textColor =
                new Color(0.85f, 0.88f, 1f);


            _skill =
                new GUIStyle(GUI.skin.label);

            _skill.fontSize = 22;
            _skill.fontStyle = FontStyle.Italic;
            _skill.normal.textColor = Color.white;
        }
    }
}