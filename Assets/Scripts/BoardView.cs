using System.Collections.Generic;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Раскладывает состояние игры по столу.
    ///
    /// Работает так: на каждое обновление считает,
    /// где какая карта должна лежать, и ведёт объекты
    /// к этим позициям. Ничего не телепортируется,
    /// поэтому перемещения выглядят как движение.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class BoardView : MonoBehaviour
    {
        public CardView CardPrefab;

        public BoardZones PlayerZones = new BoardZones();
        public BoardZones BotZones = new BoardZones();

        [Tooltip("Скорость, с которой карты едут к своим местам")]
        public float MoveSpeed = 8f;

        [Tooltip("На сколько приподнимается карта под курсором")]
        public float HoverLift = 0.12f;



        /// <summary>
        /// Карта под курсором. Ставится извне
        /// (CardZoom), поднимается здесь.
        /// </summary>
        public string HoveredCardId { get; set; }


        /// <summary>
        /// Карта, выбранная для действия с целью —
        /// например, Bloom. Она выдвигается
        /// из руки к центру стола и ждёт, пока
        /// игрок укажет, на кого её положить.
        /// </summary>
        public string LiftedCardId { get; set; }

        [Tooltip("Насколько выдвигается выбранная карта " +
                 "(если она не уезжает в сторону)")]
        public Vector3 LiftOffset =
            new Vector3(0f, 0.6f, 1.2f);

        [Header("Выбранная карта")]
        [Tooltip("Уводить выбранную для Bloom карту " +
                 "на левый край, посередине стола, " +
                 "чтобы она не закрывала поле")]
        public bool LiftToSide = true;

        [Tooltip("Насколько левее своего центра")]
        public float LiftSideOffset = 5f;

        [Tooltip("Высота над столом")]
        public float LiftHeight = 0.8f;


        private GameSession _session;

        private readonly Dictionary<string, CardView> _views =
            new Dictionary<string, CardView>();

        private readonly Dictionary<string, Vector3> _targetPositions =
            new Dictionary<string, Vector3>();

        private readonly List<string> _alive =
            new List<string>();


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _session.StateChanged += Rebuild;
        }


        private void OnDestroy()
        {
            if (_session != null)
                _session.StateChanged -= Rebuild;
        }


        private void Start()
        {
            Rebuild();
        }


        private string _lastHovered;


        private void Update()
        {
            // Тихий отклик на наведение: карта
            // отзывается ещё до щелчка.
            if (HoveredCardId != _lastHovered)
            {
                _lastHovered = HoveredCardId;

                if (!string.IsNullOrEmpty(HoveredCardId))
                    GameAudio.Play(GameAudio.Hover, 0.35f);
            }


            foreach (var pair in _views)
            {
                Vector3 target;

                if (!_targetPositions.TryGetValue(
                    pair.Key,
                    out target))
                {
                    continue;
                }


                if (pair.Key == LiftedCardId)
                {
                    target =
                        LiftedPosition(target);
                }
                else if (pair.Key == HoveredCardId)
                {
                    target +=
                        Vector3.up * HoverLift;
                }


                pair.Value.transform.position =
                    Vector3.Lerp(
                        pair.Value.transform.position,
                        target,
                        Time.deltaTime * MoveSpeed
                    );
            }
        }


        public void Rebuild()
        {
            var state = _session.State;

            if (state == null || CardPrefab == null)
                return;


            _alive.Clear();

            // Свою сторону всегда кладём в ближние
            // зоны. У гостя он первый игрок, а не
            // нулевой — без этого он видел бы свой
            // стол на месте противника.
            int me =
                _session.LocalPlayerIndex;

            int other =
                me == 0 ? 1 : 0;


            Place(state.Players[me], PlayerZones, 0);
            Place(state.Players[other], BotZones, 1);

            RemoveDisappeared();
        }


        private void Place(
            PlayerState player,
            BoardZones zones,
            int seat)
        {
            Single(
                OshiAsCard(player, seat),
                zones.Oshi,
                true,
                zones);

            Single(player.Center, zones.Center, true, zones);
            Single(player.Collab, zones.Collab, true, zones);

            for (int i = 0; i < player.Back.Count; i++)
            {
                if (i >= zones.Back.Length)
                    break;

                Single(player.Back[i], zones.Back[i], true, zones);
            }


            Hand(player.Hand, zones);

            Stack(player.Deck, zones.Deck, false, zones);
            Stack(player.CheerDeck, zones.CheerDeck, false, zones);
            Stack(player.Archive, zones.Archive, true, zones);
            Stack(player.HoloPower, zones.HoloPower, false, zones);
            Stack(player.Life, zones.Life, false, zones);
        }


        // Оши это не Card, а отдельный класс,
        // поэтому для показа собираем лёгкую
        // карту-обёртку. Одну на игрока.
        private readonly Dictionary<string, Card> _oshiCards =
            new Dictionary<string, Card>();


        /// <summary>
        /// Обёртка оси для показа.
        ///
        /// Два исправления против прежнего:
        ///
        /// Номер берётся по месту за столом, а не
        /// по имени игрока. Имена могут совпасть —
        /// тогда обе оси получали один номер, и на
        /// столе показывалась одна и та же.
        ///
        /// И обёртка ОБНОВЛЯЕТСЯ, если ось
        /// сменилась. Раньше она создавалась один
        /// раз навсегда, поэтому после новой партии
        /// или смены колоды на столе оставалась
        /// старая ось.
        /// </summary>
        private Card OshiAsCard(
            PlayerState player,
            int seat)
        {
            if (player == null || player.Oshi == null)
                return null;


            string id =
                OshiViewId(seat);


            Card card;

            if (!_oshiCards.TryGetValue(id, out card))
            {
                card =
                    new Card
                    {
                        Id = id,
                        Type = CardType.Oshi
                    };

                _oshiCards[id] = card;
            }


            // Ось могла смениться — переписываем.
            if (card.DefinitionId != player.Oshi.Id)
            {
                card.DefinitionId = player.Oshi.Id;
                card.Name = player.Oshi.Name;
                card.Color = player.Oshi.Color;


                // Картинка привязана к номеру карты,
                // поэтому вид надо пересобрать.
                CardView view;

                if (_views.TryGetValue(id, out view) &&
                    view != null)
                {
                    view.Bind(card, true);
                }
            }


            return card;
        }


        private void Single(
            Card card,
            Transform anchor,
            bool faceUp,
            BoardZones zones)
        {
            if (card == null || anchor == null)
                return;


            var view =
                Obtain(card, faceUp);

            view.transform.localScale =
                Vector3.one *
                Mathf.Max(zones.StageScale, 0.01f);

            _targetPositions[card.Id] =
                anchor.position;

            view.transform.rotation =
                anchor.rotation *
                Quaternion.Euler(0f, faceUp ? 0f : 180f, 0f);


            // Прикреплённые cheer и саппорты.
            //
            // Позиции считаются в системе координат
            // самой зоны: X вправо, Y вверх по столу,
            // Z над столом. Поэтому они одинаково
            // ложатся на обеих сторонах и не уезжают
            // под карту.
            for (int i = 0;
                 i < card.AttachedCheer.Count;
                 i++)
            {
                var cheer =
                    card.AttachedCheer[i];

                var cheerView =
                    Obtain(cheer, true);

                cheerView.transform.localScale =
                    Vector3.one * zones.CheerScale;

                _targetPositions[cheer.Id] =
                    anchor.position +
                    anchor.rotation *
                        (zones.CheerOffset +
                         zones.CheerStep * i);

                cheerView.transform.rotation =
                    anchor.rotation;
            }


            for (int i = 0;
                 i < card.AttachedSupports.Count;
                 i++)
            {
                var support =
                    card.AttachedSupports[i];

                var supportView =
                    Obtain(support, true);

                supportView.transform.localScale =
                    Vector3.one * zones.SupportScale;

                _targetPositions[support.Id] =
                    anchor.position +
                    anchor.rotation *
                        (zones.SupportOffset +
                         zones.SupportStep * i);

                supportView.transform.rotation =
                    anchor.rotation;
            }
        }


        private void Hand(
            List<Card> hand,
            BoardZones zones)
        {
            if (zones.Hand == null)
                return;


            // Шаг между картами: заданный, но рука
            // не шире предела. Мало карт — лежат
            // свободно, много — наезжают друг
            // на друга сильнее.
            float spacing =
                zones.HandSpacing;

            float limit =
                HandWidthLimit(zones);


            if (hand.Count > 1 &&
                limit > 0f &&
                (hand.Count - 1) * spacing > limit)
            {
                spacing =
                    limit / (hand.Count - 1);
            }


            // Но не теснее заданного: иначе при
            // большой руке от карт остаются
            // одни полоски.
            if (spacing < zones.HandMinSpacing)
                spacing = zones.HandMinSpacing;


            float start =
                -(hand.Count - 1) * spacing * 0.5f;


            Vector3 origin =
                HandOrigin(zones);


            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];

                var view =
                    Obtain(card, zones.HandFaceUp);

                view.transform.localScale =
                    Vector3.one *
                    Mathf.Max(zones.HandScale, 0.01f);

                _targetPositions[card.Id] =
                    origin +
                    zones.Hand.right * (start + i * spacing) +
                    zones.Hand.forward * (-zones.HandLift * i);

                view.transform.rotation =
                    zones.Hand.rotation *
                    Quaternion.Euler(
                        0f,
                        zones.HandFaceUp ? 0f : 180f,
                        0f
                    );
            }
        }


        private void Stack(
            List<Card> cards,
            Transform anchor,
            bool faceUp,
            BoardZones zones)
        {
            if (anchor == null)
                return;


            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];

                var view =
                    Obtain(card, faceUp);

                view.transform.localScale =
                    Vector3.one *
                    Mathf.Max(zones.StageScale, 0.01f);

                _targetPositions[card.Id] =
                    anchor.position +
                    anchor.forward * (zones.StackOffset * i);

                view.transform.rotation =
                    anchor.rotation *
                    Quaternion.Euler(0f, faceUp ? 0f : 180f, 0f);
            }
        }


        private CardView Obtain(
            Card card,
            bool faceUp)
        {
            _alive.Add(card.Id);


            CardView view;

            if (_views.TryGetValue(card.Id, out view))
            {
                // Карта могла измениться: Bloom,
                // урон, новый прикреплённый саппорт.
                view.Bind(card, faceUp);

                return view;
            }


            view =
                Instantiate(
                    CardPrefab,
                    transform
                );


            // Новая карта появляется у колоды
            // своего владельца и едет на место
            // сама — получается раздача, без
            // отдельной анимации.
            Transform from =
                DealFrom(card);

            if (from != null)
                view.transform.position = from.position;

            view.name =
                card.Id + " " + card.Name;

            view.Bind(card, faceUp);

            _views[card.Id] = view;

            return view;
        }


        /// <summary>
        /// Откуда прилетает новая карта.
        ///
        /// Чиры — из cheer-колоды, остальное —
        /// из обычной. Своя сторона определяется
        /// по номеру карты: он начинается с буквы
        /// владельца.
        /// </summary>
        private Transform DealFrom(
            Card card)
        {
            var state =
                _session.State;

            if (state == null)
                return null;


            int me =
                _session.LocalPlayerIndex;


            bool mine =
                Owns(state.Players[me], card.Id);


            var zones =
                mine ? PlayerZones : BotZones;


            return card.Type == CardType.Cheer
                ? zones.CheerDeck
                : zones.Deck;
        }


        private static bool Owns(
            PlayerState player,
            string cardId)
        {
            foreach (var zone in new[]
                     {
                         player.Deck, player.CheerDeck,
                         player.Hand, player.Archive,
                         player.Life, player.HoloPower,
                         player.Back
                     })
            {
                foreach (var card in zone)
                {
                    if (card != null && card.Id == cardId)
                        return true;
                }
            }


            return
                (player.Center != null &&
                 player.Center.Id == cardId) ||
                (player.Collab != null &&
                 player.Collab.Id == cardId);
        }


        private void RemoveDisappeared()
        {
            var stale =
                new List<string>();


            foreach (var pair in _views)
            {
                if (!_alive.Contains(pair.Key))
                    stale.Add(pair.Key);
            }


            foreach (var id in stale)
            {
                Destroy(_views[id].gameObject);

                _views.Remove(id);
                _targetPositions.Remove(id);
            }
        }


        /// <summary>
        /// Объект карты по id экземпляра.
        /// </summary>
        /// <summary>
        /// Насколько широкой может быть рука.
        ///
        /// Считается по тому, что камера реально
        /// видит на глубине руки: так предел
        /// не зависит ни от разрешения, ни от того,
        /// насколько близко подведена камера.
        /// Ширина самой карты вычитается, чтобы
        /// за край не уехали её края.
        /// </summary>
        private float HandWidthLimit(
            BoardZones zones)
        {
            if (!zones.HandFitToCamera)
                return zones.HandMaxWidth;


            var camera =
                Camera.main;

            if (camera == null || zones.Hand == null)
                return zones.HandMaxWidth;


            float visible;

            if (camera.orthographic)
            {
                visible =
                    camera.orthographicSize * 2f *
                    camera.aspect;
            }
            else
            {
                // Глубина руки относительно камеры:
                // чем она дальше, тем шире видимая
                // полоса.
                float depth =
                    Vector3.Dot(
                        zones.Hand.position -
                            camera.transform.position,
                        camera.transform.forward);

                if (depth <= 0.01f)
                    return zones.HandMaxWidth;


                visible =
                    2f * depth *
                    Mathf.Tan(
                        camera.fieldOfView * 0.5f *
                        Mathf.Deg2Rad) *
                    camera.aspect;
            }


            // Карта примерно 0.7 ширины при
            // единичном масштабе.
            float cardWidth =
                0.7f * Mathf.Max(zones.HandScale, 0.01f);


            float width =
                visible * zones.HandScreenShare - cardWidth;

            return Mathf.Max(width, cardWidth);
        }


        /// <summary>
        /// Где лежит середина руки с учётом сдвигов.
        ///
        /// "От центра" считается по прямой от
        /// середины стола к руке — так одна и та же
        /// настройка значит одно и то же и для своей
        /// руки, и для чужой, хотя смотрят они
        /// в разные стороны.
        /// </summary>
        private Vector3 HandOrigin(
            BoardZones zones)
        {
            Vector3 origin =
                zones.Hand.position;


            if (!Mathf.Approximately(
                    zones.HandDistanceOffset, 0f) &&
                PlayerZones.Center != null &&
                BotZones.Center != null)
            {
                Vector3 middle =
                    (PlayerZones.Center.position +
                     BotZones.Center.position) * 0.5f;

                Vector3 away =
                    origin - middle;

                away.y = 0f;


                if (away.sqrMagnitude < 0.0001f)
                    away = -zones.Hand.forward;


                origin +=
                    away.normalized *
                    zones.HandDistanceOffset;
            }


            origin +=
                zones.Hand.right * zones.HandSideOffset +
                Vector3.up * zones.HandHeightOffset;


            return origin;
        }


        /// <summary>
        /// Куда встаёт выбранная карта.
        ///
        /// Сбоку, на уровне середины стола — между
        /// своей и чужой сценой. Так она видна,
        /// но не закрывает холомемов, на которых
        /// её собираются положить.
        /// </summary>
        private Vector3 LiftedPosition(
            Vector3 fromHand)
        {
            if (!LiftToSide ||
                PlayerZones.Center == null ||
                BotZones.Center == null)
            {
                return fromHand + LiftOffset;
            }


            Vector3 middle =
                (PlayerZones.Center.position +
                 BotZones.Center.position) * 0.5f;


            return
                middle -
                PlayerZones.Center.right * LiftSideOffset +
                Vector3.up * LiftHeight;
        }


        /// <summary>
        /// Все карты, лежащие на столе.
        /// </summary>
        public IEnumerable<CardView> AllViews
        {
            get { return _views.Values; }
        }


        /// <summary>
        /// Номер, под которым ось лежит на столе.
        ///
        /// Ось — не карта движка, а обёртка,
        /// поэтому номер свой: 0 — своя сторона,
        /// 1 — сторона соперника.
        /// </summary>
        public static string OshiViewId(
            int seat)
        {
            return "OSHI-" + seat;
        }


        public CardView Find(
            string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
                return null;


            CardView view;

            return _views.TryGetValue(instanceId, out view)
                ? view
                : null;
        }


        /// <summary>
        /// Найти карту под курсором.
        /// Нужно для кликов.
        /// </summary>
        public CardView Raycast(
            Camera camera,
            Vector3 screenPosition)
        {
            if (camera == null)
                return null;


            RaycastHit hit;

            if (!Physics.Raycast(
                camera.ScreenPointToRay(screenPosition),
                out hit))
            {
                return null;
            }


            return hit.collider
                .GetComponentInParent<CardView>();
        }
    }
}