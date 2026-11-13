using System.Collections.Generic;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Разметка стола: затемнение, подписи зон
    /// и счётчики у стопок.
    ///
    /// Коврик яркий и подробный, карты на нём
    /// теряются — поэтому под игровой областью
    /// лежит лёгкое затемнение. Зоны подписаны
    /// тускло, и подпись гаснет, когда в зоне
    /// появляются карты: пустое место надо
    /// объяснять, занятое говорит само за себя.
    ///
    /// Числа стоят рядом со стопками, а не
    /// в углу экрана: так понятно, к чему они
    /// относятся.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    [RequireComponent(typeof(BoardView))]
    public class TableOverlay : MonoBehaviour
    {
        [Header("Затемнение поля")]
        public bool Dim = true;

        [Range(0f, 0.8f)]
        [Tooltip("Насколько приглушить коврик " +
                 "под игровой областью")]
        public float DimStrength = 0.25f;

        [Tooltip("Размер затемнённой области")]
        public Vector2 DimSize =
            new Vector2(16f, 12f);

        [Tooltip("Высота над ковриком")]
        public float DimHeight = 0.004f;


        [Header("Подписи зон")]
        public bool ZoneLabels = true;

        [Tooltip("Размер подписи")]
        public float LabelSize = 0.16f;

        [Tooltip("Сдвиг подписи от центра зоны")]
        public float LabelOffset = 0.75f;


        [Header("Счётчики у стопок")]
        public bool StackCounts = true;

        [Tooltip("Размер числа")]
        public float CountSize = 0.22f;


        private GameSession _session;
        private BoardView _board;

        private GameObject _dim;

        private readonly List<TextMesh> _labels =
            new List<TextMesh>();

        // Счётчики: зона → надпись.
        private readonly Dictionary<string, TextMesh> _counts =
            new Dictionary<string, TextMesh>();


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _board = GetComponent<BoardView>();
        }


        private void OnEnable()
        {
            _session.StateChanged += Refresh;
        }


        private void OnDisable()
        {
            _session.StateChanged -= Refresh;
        }


        private void Start()
        {
            Build();

            Refresh();
        }


        // =========================================
        // ПОСТРОЕНИЕ
        // =========================================

        private void Build()
        {
            BuildDim();

            if (!ZoneLabels && !StackCounts)
                return;


            Side(_board.PlayerZones, true);
            Side(_board.BotZones, false);
        }


        private void BuildDim()
        {
            if (!Dim ||
                _board.PlayerZones.Center == null ||
                _board.BotZones.Center == null)
            {
                return;
            }


            _dim =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad);

            _dim.name = "TableDim";

            var collider =
                _dim.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);


            Vector3 middle =
                (_board.PlayerZones.Center.position +
                 _board.BotZones.Center.position) * 0.5f;


            _dim.transform.SetParent(transform, false);

            _dim.transform.position =
                middle + Vector3.up * DimHeight;

            _dim.transform.rotation =
                Quaternion.Euler(90f, 0f, 0f);

            _dim.transform.localScale =
                new Vector3(DimSize.x, DimSize.y, 1f);


            var renderer =
                _dim.GetComponent<MeshRenderer>();

            renderer.sharedMaterial =
                FlatMaterials.Transparent();

            var block =
                new MaterialPropertyBlock();

            var color =
                new Color(0f, 0f, 0f, DimStrength);

            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);

            renderer.SetPropertyBlock(block);
        }


        private void Side(
            BoardZones zones,
            bool mine)
        {
            string prefix =
                mine ? "P" : "B";


            Zone(zones.Deck, "КОЛОДА", prefix + "-deck");
            Zone(zones.CheerDeck, "CHEER", prefix + "-cheer");
            Zone(zones.Archive, "АРХИВ", prefix + "-archive");
            Zone(zones.HoloPower, "HOLOPOWER", prefix + "-power");
            Zone(zones.Life, "ЖИЗНИ", prefix + "-life");
            Zone(zones.Oshi, "ОСЬ", null);
            Zone(zones.Center, "ЦЕНТР", null);
            Zone(zones.Collab, "КОЛЛАБ", null);
        }


        private void Zone(
            Transform anchor,
            string caption,
            string countKey)
        {
            if (anchor == null)
                return;


            if (ZoneLabels)
            {
                var label =
                    MakeText(
                        anchor,
                        caption,
                        new Vector3(0f, -LabelOffset, 0.01f),
                        LabelSize,
                        new Color(1f, 1f, 1f, 0.35f),
                        TextAnchor.UpperCenter);

                _labels.Add(label);
            }


            if (StackCounts && countKey != null)
            {
                var count =
                    MakeText(
                        anchor,
                        string.Empty,
                        new Vector3(0f, 0f, 0.02f),
                        CountSize,
                        Color.white,
                        TextAnchor.MiddleCenter);

                _counts[countKey] = count;
            }
        }


        private TextMesh MakeText(
            Transform anchor,
            string text,
            Vector3 offset,
            float size,
            Color color,
            TextAnchor align)
        {
            var host =
                new GameObject("Label");

            host.transform.SetParent(transform, false);

            host.transform.position =
                anchor.position + anchor.rotation * offset;

            host.transform.rotation =
                anchor.rotation;


            var mesh =
                host.AddComponent<TextMesh>();

            mesh.text = text;
            mesh.fontSize = 72;
            mesh.characterSize = size * 0.5f;
            mesh.color = color;
            mesh.anchor = align;
            mesh.alignment = TextAlignment.Center;

            return mesh;
        }


        // =========================================
        // ОБНОВЛЕНИЕ
        // =========================================

        private void Refresh()
        {
            var state =
                _session.State;

            if (state == null)
                return;


            int me =
                _session.LocalPlayerIndex;

            var mine =
                state.Players[me];

            var theirs =
                state.Players[me == 0 ? 1 : 0];


            Count("P-deck", mine.Deck.Count);
            Count("P-cheer", mine.CheerDeck.Count);
            Count("P-archive", mine.Archive.Count);
            Count("P-power", mine.HoloPower.Count);
            Count("P-life", mine.Life.Count);

            Count("B-deck", theirs.Deck.Count);
            Count("B-cheer", theirs.CheerDeck.Count);
            Count("B-archive", theirs.Archive.Count);
            Count("B-power", theirs.HoloPower.Count);
            Count("B-life", theirs.Life.Count);
        }


        private void Count(
            string key,
            int value)
        {
            TextMesh mesh;

            if (!_counts.TryGetValue(key, out mesh) ||
                mesh == null)
            {
                return;
            }


            // Пустую зону числом не подписываем:
            // там и так видно, что ничего нет.
            mesh.text =
                value > 0
                    ? value.ToString()
                    : string.Empty;
        }
    }
}