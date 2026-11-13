using System.Linq;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Почему с картой сейчас ничего нельзя.
    ///
    /// Движок отказ не объясняет: он просто
    /// не предлагает действие. Поэтому причину
    /// восстанавливаем по состоянию — это всегда
    /// лучше, чем глухой щелчок в ответ.
    /// </summary>
    public static class RefusalReason
    {
        public static string For(
            GameState state,
            Card card,
            int me)
        {
            if (state == null || card == null)
                return "Сейчас с этой картой ничего нельзя.";


            var player =
                state.Players[me];


            bool mine =
                Owns(player, card.Id);


            if (!mine)
                return "Это карта соперника.";


            if (state.CurrentPlayerIndex != me)
                return "Сейчас ход соперника.";


            // Карты соперника и чужие руки сюда
            // не попадают, дальше разбираем свои.
            if (card.Type == CardType.Holomem)
                return Holomem(state, player, card);


            if (card.Type == CardType.Support)
                return Support(state, player, card);


            if (card.Type == CardType.Cheer)
            {
                return state.Phase == GamePhase.Cheer
                    ? "Прикрепите чир к холомему на сцене."
                    : "Чиры прикрепляются в свою фазу cheer.";
            }


            return "Сейчас с этой картой ничего нельзя.";
        }


        private static string Holomem(
            GameState state,
            PlayerState player,
            Card card)
        {
            bool onStage =
                OnStage(player, card.Id);


            if (!onStage)
            {
                // Карта в руке.
                if (state.Phase != GamePhase.Main)
                    return "Холомемов выкладывают в главную фазу.";


                if (card.Level != HolomemLevel.Debut &&
                    card.Level != HolomemLevel.Spot)
                {
                    return
                        "Такого холомема нельзя выложить " +
                        "просто так — только через Bloom " +
                        "на подходящего холомема на сцене.";
                }


                if (player.Back.Count >= 5)
                    return "Задний ряд заполнен.";


                return "Сейчас эту карту выложить нельзя.";
            }


            // Карта на сцене.
            if (card.IsResting)
            {
                return
                    "Этот холомем отдыхает: он не может " +
                    "ни атаковать, ни уйти в коллаб. " +
                    "Отдых снимается в фазу перезагрузки.";
            }


            if (state.Phase == GamePhase.Performance)
            {
                if (card.LastArtTurnNumber == state.TurnNumber)
                    return "Этот холомем уже атаковал в этот ход.";


                if (card.Arts == null || card.Arts.Count == 0)
                    return "У этого холомема нет артов.";


                if (!card.Arts.Any(
                        art => Rules.CanPayArtCost(card, art)))
                {
                    return
                        "Не хватает чиров для арта. " +
                        "Прикрепите ещё в свою фазу cheer.";
                }


                bool front =
                    (player.Center != null &&
                     player.Center.Id == card.Id) ||
                    (player.Collab != null &&
                     player.Collab.Id == card.Id);

                if (!front)
                    return "Атакуют только центр и коллаб.";


                return "Этим холомемом сейчас не ударить.";
            }


            if (state.Phase == GamePhase.Main)
            {
                if (player.CollabUsedThisTurn &&
                    player.Collab == null)
                {
                    return "Коллаб в этот ход уже использован.";
                }


                if (card.EnteredStageTurnNumber ==
                    state.TurnNumber)
                {
                    return
                        "Холомем вышел на сцену в этот ход: " +
                        "Bloom и коллаб ему пока недоступны.";
                }


                return
                    "С этим холомемом сейчас нечего делать. " +
                    "Проверьте фазу и доступные действия справа.";
            }


            return "Действия с холомемами идут в главную фазу.";
        }


        private static string Support(
            GameState state,
            PlayerState player,
            Card card)
        {
            if (state.Phase != GamePhase.Main)
                return "Саппорты разыгрывают в главную фазу.";


            if (card.IsLimited &&
                player.LastLimitedSupportTurnNumber ==
                    state.TurnNumber)
            {
                return
                    "LIMITED-саппорт уже разыгран " +
                    "в этот ход.";
            }


            if (card.SupportType == SupportType.Mascot ||
                card.SupportType == SupportType.Tool ||
                card.SupportType == SupportType.Fan)
            {
                return
                    "Эту карту прикрепляют к холомему, " +
                    "и подходящего сейчас нет.";
            }


            return
                "Условия этой карты сейчас не выполнены.";
        }


        private static bool OnStage(
            PlayerState player,
            string cardId)
        {
            if (player.Center != null &&
                player.Center.Id == cardId)
            {
                return true;
            }


            if (player.Collab != null &&
                player.Collab.Id == cardId)
            {
                return true;
            }


            foreach (var card in player.Back)
            {
                if (card != null && card.Id == cardId)
                    return true;
            }


            return false;
        }


        private static bool Owns(
            PlayerState player,
            string cardId)
        {
            if (OnStage(player, cardId))
                return true;


            foreach (var zone in new[]
                     {
                         player.Hand, player.Archive,
                         player.Deck, player.CheerDeck,
                         player.Life, player.HoloPower
                     })
            {
                foreach (var card in zone)
                {
                    if (card != null && card.Id == cardId)
                        return true;
                }
            }


            return false;
        }
    }
}