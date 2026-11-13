using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Подсвечивает карты, с которыми сейчас
    /// можно что-то сделать.
    ///
    /// Смотрит на список легальных действий
    /// своего игрока: каждая карта, которая
    /// в нём упомянута как исполнитель или цель,
    /// получает зелёную рамку. Ось подсвечивается,
    /// если можно применить её скилл.
    ///
    /// Пересчёт — только при изменении
    /// состояния, а не каждый кадр.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    [RequireComponent(typeof(BoardView))]
    public class PlayableHighlighter : MonoBehaviour
    {
        [Tooltip("Цвет рамки доступных карт")]
        public Color Color =
            new Color(0.35f, 0.95f, 0.55f, 1f);


        private GameSession _session;
        private BoardView _board;

        private readonly HashSet<string> _lit =
            new HashSet<string>();


        // Когда карта выбрана для действия,
        // подходящие цели известны заранее —
        // всё остальное приглушается.
        private HashSet<string> _focus;

        [Tooltip("Приглушать отдыхающих холомемов: " +
                 "они не могут ни атаковать, ни уйти " +
                 "в коллаб")]
        public bool DimResting = true;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _board = GetComponent<BoardView>();
        }


        private void OnEnable()
        {
            if (_session != null)
                _session.StateChanged += Refresh;
        }


        private void OnDisable()
        {
            if (_session != null)
                _session.StateChanged -= Refresh;
        }


        /// <summary>
        /// BoardView раскладывает карты тоже по
        /// событию изменения состояния. Если мы
        /// успеем раньше него, свежих карт ещё не
        /// будет — поэтому досвечиваем в конце
        /// кадра то, что появилось.
        /// </summary>
        private void LateUpdate()
        {
            foreach (var view in _board.AllViews)
            {
                if (view == null || view.Card == null)
                    continue;

                if (_lit.Contains(view.Card.Id))
                    view.SetPlayable(true);
            }
        }


        /// <summary>
        /// Сузить внимание до этих карт.
        ///
        /// Вызывается, когда игрок взял карту
        /// и выбирает, куда её положить: всё, что
        /// не подходит, уходит в тень.
        /// </summary>
        public void Focus(
            IEnumerable<string> ids)
        {
            _focus =
                ids == null
                    ? null
                    : new HashSet<string>(ids);

            Refresh();
        }


        public void ClearFocus()
        {
            _focus = null;

            Refresh();
        }


        private void Refresh()
        {
            CardView.PlayableColor = Color;


            var wanted =
                Collect();


            foreach (var view in _board.AllViews)
            {
                if (view == null || view.Card == null)
                    continue;

                bool playable =
                    wanted.Contains(view.Card.Id);


                view.SetPlayable(playable);


                view.SetDimmed(
                    Dimmed(view.Card, playable));
            }


            _lit.Clear();

            foreach (var id in wanted)
                _lit.Add(id);
        }


        /// <summary>
        /// Надо ли приглушить эту карту.
        /// </summary>
        private bool Dimmed(
            Card card,
            bool playable)
        {
            // Идёт выбор цели: всё, что не цель,
            // уходит в тень. Сама выбранная карта
            // остаётся яркой.
            if (_focus != null)
                return !_focus.Contains(card.Id);


            // Отдыхающий холомем не может ничего:
            // ни ударить, ни уйти в коллаб.
            if (DimResting &&
                card.Type == CardType.Holomem &&
                card.IsResting)
            {
                return true;
            }


            return false;
        }


        private HashSet<string> Collect()
        {
            var ids =
                new HashSet<string>();

            var state =
                _session.State;

            if (state == null || state.IsFinished)
                return ids;


            int me =
                _session.LocalPlayerIndex;


            var mine =
                _session.LegalActions()
                    .Where(x => x.ActorPlayerIndex == me);


            foreach (var action in mine)
            {
                // Конец фазы — это кнопка, а не карта.
                if (action.Type == GameActionType.EndPhase)
                    continue;


                if (action.Type ==
                        GameActionType.UseOshiSkill ||
                    action.Type ==
                        GameActionType.UseSpOshiSkill)
                {
                    // Своя сторона всегда нулевая:
                    // стол раскладывается так, что
                    // ближние зоны — свои.
                    ids.Add(
                        BoardView.OshiViewId(0));

                    continue;
                }


                if (!string.IsNullOrEmpty(action.CardId))
                    ids.Add(action.CardId);

                if (!string.IsNullOrEmpty(action.TargetCardId))
                    ids.Add(action.TargetCardId);
            }


            return ids;
        }
    }
}