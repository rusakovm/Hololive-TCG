using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Окно выбора карт.
    ///
    /// Показывается только когда выбирает сам игрок.
    /// Карты противника при этом не раскрываются:
    /// для его выбора окно не появляется вовсе,
    /// и карты остаются в колоде рубашкой вверх.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class SelectionPrompt : MonoBehaviour
    {
        [Tooltip("Высота карточки в окне, пикселей")]
        public float CardHeight = 190f;

        [Tooltip("Доля высоты экрана под окно")]
        [Range(0.3f, 1f)]
        public float PanelHeight = 0.55f;


        /// <summary>
        /// Пока окно открыто, клики по столу
        /// обрабатывать не нужно.
        /// </summary>
        public static bool Blocking { get; private set; }


        private GameSession _session;

        // Действие применяется следующим кадром:
        // менять состояние посреди отрисовки
        // интерфейса нельзя, иначе раскладка
        // рассыпается.
        private GameAction _queued;
        private Vector2 _scroll;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
        }


        private void OnDisable()
        {
            Blocking = false;
        }


        private PendingEffect Current()
        {
            var state = _session.State;

            if (state == null ||
                state.PendingEffects.Count == 0)
            {
                return null;
            }


            var pending =
                state.PendingEffects[0];

            // Чужой выбор не показываем.
            if (pending.ActorPlayerIndex !=
                _session.LocalPlayerIndex)
                return null;


            // Набор кандидатов заполняется движком
            // при построении списка действий,
            // поэтому сначала запрашиваем его.
            _session.LegalActions();


            // Признак того, что идёт выбор из набора, —
            // непустой список кандидатов. На внутренний
            // шаг не опираемся: он деталь движка.
            if (pending.CandidateCardIds == null ||
                pending.CandidateCardIds.Count == 0)
            {
                return null;
            }


            return pending;
        }



        private void Update()
        {
            if (_queued == null)
                return;


            var action = _queued;

            _queued = null;

            _session.Apply(action);
        }

        private void OnGUI()
        {
            var pending = Current();

            Blocking = pending != null;

            if (pending == null)
                return;


            var state = _session.State;

            var player =
                state.Players[
                    pending.SourcePlayerIndex
                ];


            float height =
                Screen.height * PanelHeight;

            var panel =
                new Rect(
                    20f,
                    (Screen.height - height) * 0.5f,
                    Screen.width - 40f,
                    height
                );


            GUI.Box(panel, string.Empty);

            GUILayout.BeginArea(
                new Rect(
                    panel.x + 12f,
                    panel.y + 8f,
                    panel.width - 24f,
                    panel.height - 16f
                )
            );


            GUILayout.Label(
                pending.AbilityName +
                " — " +
                Hint(
                    pending.Effect,
                    pending.SelectedCardIds.Count
                )
            );


            DrawButtons();

            DrawCards(
                pending,
                player,
                panel.width - 24f
            );

            GUILayout.EndArea();
        }


        private void DrawCards(
            PendingEffect pending,
            PlayerState player,
            float availableWidth)
        {
            var actions =
                _session
                    .LegalActions()
                    .Where(x =>
                        x.ActorPlayerIndex ==
                            _session.LocalPlayerIndex &&
                        x.Type ==
                            GameActionType.ChooseEffectTarget)
                    .ToDictionary(
                        x => x.TargetCardId,
                        x => x
                    );


            float cardWidth =
                CardHeight * 0.716f;

            float cellWidth =
                cardWidth + 12f;


            // Сколько карт влезает в строку.
            int columns =
                Mathf.Max(
                    1,
                    Mathf.FloorToInt(
                        (availableWidth - 24f) / cellWidth
                    )
                );


            _scroll =
                GUILayout.BeginScrollView(
                    _scroll
                );


            int inRow = 0;

            GUILayout.BeginHorizontal();


            foreach (var id in pending.CandidateCardIds)
            {
                var card =
                    FindAnywhere(player, id);

                if (card == null)
                    continue;


                if (inRow == columns)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.Space(6f);
                    GUILayout.BeginHorizontal();

                    inRow = 0;
                }


                bool picked =
                    pending.SelectedCardIds.Contains(id);

                bool selectable =
                    actions.ContainsKey(id);


                GUILayout.BeginVertical(
                    GUILayout.Width(cellWidth)
                );


                var previous = GUI.color;

                if (picked)
                    GUI.color = new Color(0.6f, 1f, 0.6f);
                else if (!selectable)
                    GUI.color = new Color(1f, 1f, 1f, 0.45f);


                var texture =
                    CardArt.Front(card);

                bool clicked;

                if (texture != null)
                {
                    clicked =
                        GUILayout.Button(
                            texture,
                            GUILayout.Width(cardWidth),
                            GUILayout.Height(CardHeight)
                        );
                }
                else
                {
                    clicked =
                        GUILayout.Button(
                            card.Name,
                            GUILayout.Width(cardWidth),
                            GUILayout.Height(CardHeight)
                        );
                }

                GUI.color = previous;


                GUILayout.Label(
                    (picked ? "+ " : "") +
                    card.Name,
                    GUILayout.Width(cardWidth)
                );

                GUILayout.EndVertical();

                inRow++;


                if (clicked && selectable)
                {
                    _queued = actions[id];

                    GUILayout.EndHorizontal();
                    GUILayout.EndScrollView();
                    return;
                }
            }


            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }


        private void DrawButtons()
        {
            var actions =
                _session
                    .LegalActions()
                    .Where(x => x.ActorPlayerIndex ==
                        _session.LocalPlayerIndex)
                    .ToList();


            GUILayout.BeginHorizontal();

            var finish =
                actions.FirstOrDefault(
                    x => x.Type ==
                        GameActionType.FinishSelection
                );

            if (finish != null &&
                GUILayout.Button(
                    "Готово",
                    GUILayout.Width(160f)))
            {
                _queued = finish;
            }


            var skip =
                actions.FirstOrDefault(
                    x => x.Type ==
                        GameActionType.SkipEffect
                );

            if (skip != null &&
                GUILayout.Button(
                    "Отказаться",
                    GUILayout.Width(160f)))
            {
                _queued = skip;
            }

            GUILayout.EndHorizontal();
        }


        private static Card FindAnywhere(
            PlayerState player,
            string id)
        {
            var zones =
                new List<Card>[]
                {
                    player.Deck,
                    player.Hand,
                    player.HoloPower,
                    player.Archive,
                    player.CheerDeck
                };


            foreach (var zone in zones)
            {
                foreach (var card in zone)
                {
                    if (card.Id == id)
                        return card;
                }
            }


            return null;
        }


        private static string Hint(
            CardEffect effect,
            int picked)
        {
            if (effect == null)
                return "выберите карты";


            if (effect.MaxSelect == 1 &&
                effect.MinSelect == 1)
            {
                return "выберите одну карту";
            }


            string text =
                "выбрано " + picked;

            if (effect.MaxSelect > 0)
                text += " из " + effect.MaxSelect;

            if (effect.MinSelect > 0 &&
                picked < effect.MinSelect)
            {
                text +=
                    ", нужно минимум " +
                    effect.MinSelect;
            }

            return text;
        }
    }
}