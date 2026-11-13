using System.Collections.Generic;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Человеческие подписи для действий и фаз.
    ///
    /// Раньше подписи жили внутри BoardInput и
    /// покрывали только половину действий —
    /// остальные показывались внутренними именами
    /// вроде UseOshiSkill. Теперь одно место
    /// для всех экранов.
    /// </summary>
    public static class ActionLabels
    {
        public static string Phase(
            GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Reset: return "Перезагрузка";
                case GamePhase.Draw: return "Добор";
                case GamePhase.Cheer: return "Cheer";
                case GamePhase.Main: return "Главная фаза";
                case GamePhase.Performance: return "Атака";
                case GamePhase.End: return "Конец хода";
                case GamePhase.Finished: return "Партия окончена";
                default: return phase.ToString();
            }
        }


        /// <summary>
        /// Подпись для кнопки конца фазы:
        /// говорит, куда игра перейдёт дальше.
        /// </summary>
        public static string EndPhase(
            GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Main:
                    return "К атаке  ▶";

                case GamePhase.Performance:
                    return "Закончить ход  ▶";

                case GamePhase.End:
                    return "Передать ход  ▶";

                default:
                    return "Дальше  ▶";
            }
        }


        public static string For(
            GameAction action,
            GameState state)
        {
            string card =
                CardName(state, action.CardId);

            string target =
                CardName(state, action.TargetCardId);


            switch (action.Type)
            {
                case GameActionType.KeepOpeningHand:
                    return "Оставить руку";

                case GameActionType.MulliganOpeningHand:
                    return "Муллиган";

                case GameActionType.PutSetupCardBottom:
                    return "Под низ колоды";

                case GameActionType.ChooseStartingCenter:
                    return "В центр";

                case GameActionType.PlayStartingBack:
                    return "В задний ряд";

                case GameActionType.FinishStartingBack:
                    return "Готово — начать партию";

                case GameActionType.DrawCard:
                    return "Взять карту";

                case GameActionType.PlayToCenter:
                    return "В центр";

                case GameActionType.PlayToBack:
                    return "В задний ряд";

                case GameActionType.Bloom:
                    return target.Length > 0
                        ? "Bloom на " + target
                        : "Bloom";

                case GameActionType.Collab:
                    return "На коллаб";

                case GameActionType.BatonPass:
                    return "Baton Pass";

                case GameActionType.PayCheerCost:
                    return "Отдать за Baton Pass";

                case GameActionType.AttachCheer:
                    return target.Length > 0
                        ? "Cheer на " + target
                        : "Прикрепить cheer";

                case GameActionType.PlaySupport:
                    return "Разыграть";

                case GameActionType.AttachSupportTo:
                    return target.Length > 0
                        ? "Прикрепить к " + target
                        : "Прикрепить сюда";

                case GameActionType.PerformArt:
                    return
                        "Атака" +
                        (action.ArtIndex.HasValue
                            ? " #" + (action.ArtIndex.Value + 1)
                            : "");

                case GameActionType.UseOshiSkill:
                    return Skill(
                        state, action, false);

                case GameActionType.UseSpOshiSkill:
                    return Skill(
                        state, action, true);

                case GameActionType.ChooseCenterReplacement:
                    return "Новый центр";

                case GameActionType.SendLifeCheer:
                    return "Life cheer сюда";

                case GameActionType.ChooseEffectTarget:
                    return "Выбрать целью";

                case GameActionType.FinishSelection:
                    return "Готово";

                case GameActionType.RollDie:
                    return "Бросить кубик";

                case GameActionType.DeclareDieResult:
                    return action.Number.HasValue
                        ? "Выпало " + action.Number.Value
                        : "Объявить результат";

                case GameActionType.SkipEffect:
                    return "Пропустить";

                case GameActionType.EndPhase:
                    return "Дальше";

                default:
                    return action.Type.ToString();
            }
        }


        /// <summary>
        /// Скилл оси с именем и стоимостью.
        /// </summary>
        private static string Skill(
            GameState state,
            GameAction action,
            bool sp)
        {
            string fallback =
                sp ? "SP-скилл оси" : "Скилл оси";


            if (state == null ||
                action.ActorPlayerIndex < 0 ||
                action.ActorPlayerIndex >=
                    state.Players.Length)
            {
                return fallback;
            }


            var oshi =
                state.Players[action.ActorPlayerIndex].Oshi;

            if (oshi == null)
                return fallback;


            var skill =
                sp ? oshi.SpOshiSkill : oshi.OshiSkill;

            if (skill == null)
                return fallback;


            return
                (sp ? "SP: " : "") +
                skill.Name +
                "  (holoPower " +
                skill.HoloPowerCost + ")";
        }


        /// <summary>
        /// Имя карты по номеру экземпляра —
        /// ищем во всех зонах, включая
        /// прикреплённое.
        /// </summary>
        public static string CardName(
            GameState state,
            string cardId)
        {
            if (state == null ||
                string.IsNullOrEmpty(cardId))
            {
                return string.Empty;
            }


            foreach (var player in state.Players)
            {
                foreach (var card in AllOf(player))
                {
                    if (card != null &&
                        card.Id == cardId)
                    {
                        return card.Name;
                    }
                }
            }


            return string.Empty;
        }


        private static IEnumerable<Card> AllOf(
            PlayerState player)
        {
            var zones =
                new List<List<Card>>
                {
                    player.Hand,
                    player.Archive,
                    player.Deck,
                    player.CheerDeck,
                    player.Life,
                    player.HoloPower,
                    player.Back
                };


            foreach (var zone in zones)
            {
                foreach (var card in zone)
                {
                    yield return card;

                    foreach (var extra in Attached(card))
                        yield return extra;
                }
            }


            foreach (var card in new[]
                     { player.Center, player.Collab })
            {
                if (card == null)
                    continue;

                yield return card;

                foreach (var extra in Attached(card))
                    yield return extra;
            }
        }


        private static IEnumerable<Card> Attached(
            Card card)
        {
            if (card == null)
                yield break;

            foreach (var c in card.AttachedCheer)
                yield return c;

            foreach (var s in card.AttachedSupports)
                yield return s;
        }
    }
}