using System.Collections.Generic;
using UnityEngine;
using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Жетоны на столе: урон на картах, метка хода,
    /// отметка использованного SP-скилла.
    ///
    /// Префабы не нужны — жетоны собираются кодом.
    /// Картинки, если положить их в
    /// Assets/Resources/Tokens/ с именами
    /// damage-100, damage-50, damage-10,
    /// turn-marker, sp-used — подхватятся сами.
    /// Без картинок рисуются цветные кружки.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    [RequireComponent(typeof(BoardView))]
    public class TableTokens : MonoBehaviour
    {
        [Header("Размеры")]
        public float DamageTokenSize = 0.22f;
        public float MarkerSize = 0.3f;

        [Tooltip("Насколько жетоны подняты над лицевой стороной карты " +
                 "(вдоль нормали лица). Увеличьте, если жетоны в меше.")]
        public float TokenHeight = 0.08f;

        [Tooltip("Шаг между жетонами урона в столбике")]
        public float TokenSpacing = 0.16f;

        [Header("Смещение на лице карты")]
        [Tooltip("Вправо от центра (локальный X)")]
        public float DamageOffsetX = 0.28f;
        [Tooltip("Вверх по лицу (локальный Y), первый жетон")]
        public float DamageOffsetY = 0.32f;

        [Header("Метка хода")]
        public float TurnMarkerX = -3.2f;
        public float TurnMarkerZ = 1.2f;

        private GameSession _session;
        private BoardView _board;
        private readonly List<GameObject> _damageTokens =
            new List<GameObject>();
        private GameObject _turnMarker;
        private readonly GameObject[] _spMarkers =
            new GameObject[2];

        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _board = GetComponent<BoardView>();
            _session.StateChanged += Refresh;
        }

        private void OnDestroy()
        {
            if (_session != null)
                _session.StateChanged -= Refresh;
        }

        private void Start()
        {
            Refresh();
        }

        private void Refresh()
        {
            var state = _session.State;
            if (state == null)
                return;

            RebuildDamage(state);
            PlaceTurnMarker(state);
            PlaceSpMarkers(state);
        }

        // =========================================
        // УРОН
        // =========================================

        private void RebuildDamage(GameState state)
        {
            foreach (var token in _damageTokens)
            {
                if (token != null)
                    Destroy(token);
            }
            _damageTokens.Clear();

            foreach (var player in state.Players)
            {
                AddDamage(player.Center);
                AddDamage(player.Collab);
                foreach (var card in player.Back)
                    AddDamage(card);
            }
        }

        private void AddDamage(Card card)
        {
            if (card == null)
                return;

            int damage = card.GetMaxHp() - card.CurrentHp;
            if (damage <= 0)
                return;

            // BoardView.Find должен возвращать CardView
            var view = _board.Find(card.Id) as CardView;
            if (view == null)
            {
                // На случай, если Find отдаёт Transform/GameObject
                var any = _board.Find(card.Id);
                if (any == null)
                    return;

                PlaceDamageOnTransform(any.transform, damage);
                return;
            }

            PlaceDamageOnCardView(view, damage);
        }

        private void PlaceDamageOnCardView(CardView view, int damage)
        {
            // Как у Halo: ориентируемся на Front.
            // При SwapFaces лицо смотрит в сторону -Z родителя
            // (Quad с Y=180), поэтому «над лицом» — сдвиг
            // вдоль той же оси, что и localPosition.z у Front,
            // но дальше от плоскости карты к камере.
            Transform face =
                view.Front != null
                    ? view.Front.transform
                    : view.transform;

            float faceZ =
                view.Front != null
                    ? view.Front.transform.localPosition.z
                    : 0f;

            // Знак: если Front.z > 0 (типичный SwapFaces),
            // видимая сторона смотрит в -Z → жетоны в -Z.
            // Если Front.z < 0 — наоборот.
            float normalSign = faceZ >= 0f ? -1f : 1f;
            float height = normalSign * TokenHeight;

            // Чуть дальше плоскости лица, чем сам Front.
            float baseZ = faceZ + height;

            Quaternion faceRot =
                view.Front != null
                    ? view.Front.transform.localRotation
                    : Quaternion.identity;

            int index = 0;
            foreach (var value in Split(damage))
            {
                var token = MakeToken(
                    "damage-" + value,
                    DamageTokenSize,
                    ColorFor(value),
                    value.ToString()
                );

                token.transform.SetParent(view.transform, false);

                token.transform.localPosition = new Vector3(
                    DamageOffsetX,
                    DamageOffsetY - index * TokenSpacing,
                    baseZ
                );

                // Тот же поворот, что у лица — иначе
                // квад смотрит рубашкой к камере.
                token.transform.localRotation = faceRot;

                _damageTokens.Add(token);
                index++;
            }
        }

        /// <summary>
        /// Запасной путь, если Find не вернул CardView.
        /// </summary>
        private void PlaceDamageOnTransform(Transform parent, int damage)
        {
            int index = 0;
            foreach (var value in Split(damage))
            {
                var token = MakeToken(
                    "damage-" + value,
                    DamageTokenSize,
                    ColorFor(value),
                    value.ToString()
                );

                token.transform.SetParent(parent, false);
                token.transform.localPosition = new Vector3(
                    DamageOffsetX,
                    DamageOffsetY - index * TokenSpacing,
                    TokenHeight
                );
                token.transform.localRotation = Quaternion.identity;

                _damageTokens.Add(token);
                index++;
            }
        }

        /// <summary>
        /// 100 / 50 / 10 + остаток &lt; 10 (раньше пропадал).
        /// </summary>
        private static IEnumerable<int> Split(int damage)
        {
            int rest = damage;

            while (rest >= 100)
            {
                rest -= 100;
                yield return 100;
            }

            while (rest >= 50)
            {
                rest -= 50;
                yield return 50;
            }

            while (rest >= 10)
            {
                rest -= 10;
                yield return 10;
            }

            if (rest > 0)
                yield return rest;
        }

        private static Color ColorFor(int value)
        {
            if (value >= 100)
                return new Color(0.25f, 0.60f, 0.85f);
            if (value >= 50)
                return new Color(0.95f, 0.80f, 0.20f);
            if (value >= 10)
                return new Color(0.45f, 0.80f, 0.45f);
            return new Color(0.55f, 0.85f, 0.55f);
        }

        // =========================================
        // МЕТКА ХОДА
        // =========================================

        private void PlaceTurnMarker(GameState state)
        {
            if (_turnMarker == null)
            {
                _turnMarker = MakeToken(
                    "turn-marker",
                    MarkerSize,
                    new Color(0.30f, 0.65f, 0.95f),
                    "TURN"
                );
                _turnMarker.transform.SetParent(transform, false);
            }

            float side =
                state.CurrentPlayerIndex == _session.LocalPlayerIndex
                    ? -1f
                    : 1f;

            _turnMarker.transform.position = new Vector3(
                TurnMarkerX,
                Mathf.Abs(TokenHeight),
                side * TurnMarkerZ
            );
            _turnMarker.transform.rotation =
                Quaternion.Euler(90f, 0f, 0f);
        }

        // =========================================
        // SP-СКИЛЛ ИСПОЛЬЗОВАН
        // =========================================

        private void PlaceSpMarkers(GameState state)
        {
            for (int i = 0; i < 2; i++)
            {
                var player = state.Players[i];
                bool used =
                    player.Oshi != null &&
                    player.Oshi.SpOshiSkill != null &&
                    player.Oshi.SpOshiSkill.UsedThisGame;

                if (!used)
                {
                    if (_spMarkers[i] != null)
                    {
                        Destroy(_spMarkers[i]);
                        _spMarkers[i] = null;
                    }
                    continue;
                }

                if (_spMarkers[i] != null)
                    continue;

                var zones =
                    i == _session.LocalPlayerIndex
                        ? _board.PlayerZones
                        : _board.BotZones;

                if (zones.Oshi == null)
                    continue;

                var marker = MakeToken(
                    "sp-used",
                    MarkerSize * 0.7f,
                    new Color(0.75f, 0.35f, 0.85f),
                    "SP"
                );
                marker.transform.SetParent(transform, false);
                marker.transform.position =
                    zones.Oshi.position +
                    Vector3.up * (Mathf.Abs(TokenHeight) * 2f);
                marker.transform.rotation =
                    Quaternion.Euler(90f, 0f, 0f);

                _spMarkers[i] = marker;
            }
        }

        // =========================================
        // СБОРКА ЖЕТОНА
        // =========================================

        private static GameObject MakeToken(
            string artKey,
            float size,
            Color fallbackColor,
            string caption)
        {
            var token = GameObject.CreatePrimitive(PrimitiveType.Quad);
            token.name = artKey;

            var collider = token.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            token.transform.localScale = Vector3.one * size;

            var renderer = token.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = FlatMaterials.TransparentOnTop();

            var texture = Resources.Load<Texture2D>("Tokens/" + artKey);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);

            if (texture != null)
            {
                block.SetTexture("_BaseMap", texture);
                block.SetTexture("_MainTex", texture);
                block.SetColor("_BaseColor", Color.white);
                block.SetColor("_Color", Color.white);
            }
            else
            {
                block.SetTexture("_BaseMap", Texture2D.whiteTexture);
                block.SetTexture("_MainTex", Texture2D.whiteTexture);
                block.SetColor("_BaseColor", fallbackColor);
                block.SetColor("_Color", fallbackColor);
            }

            renderer.SetPropertyBlock(block);

            if (texture != null || string.IsNullOrEmpty(caption))
                return token;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(token.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            labelObject.transform.localScale = Vector3.one * 0.12f;

            var label = labelObject.AddComponent<TextMesh>();
            label.text = caption;
            label.fontSize = 48;
            label.color = Color.black;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;

            return token;
        }
    }
}