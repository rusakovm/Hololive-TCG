using System.Collections.Generic;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// ѕереводит событи€ партии в звук.
    ///
    /// —лушает поток событий движка Ч тот самый,
    /// на котором держатс€ анимации, Ч и на каждое
    /// подбирает звук. «десь же смена хода и итог
    /// партии.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class AudioDirector : MonoBehaviour
    {
        [Tooltip("—читать удар сильным, начина€ " +
                 "с этого урона")]
        public int BigHitFrom = 60;


        private GameSession _session;

        private int _lastTurn = -1;

        private bool _finished;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
        }


        private void OnEnable()
        {
            _session.EventsProduced += OnEvents;
            _session.StateChanged += OnStateChanged;
        }


        private void OnDisable()
        {
            _session.EventsProduced -= OnEvents;
            _session.StateChanged -= OnStateChanged;
        }


        private void OnEvents(
            List<GameEvent> events)
        {
            foreach (var e in events)
                Sound(e);
        }


        private void Sound(
            GameEvent e)
        {
            switch (e.Type)
            {
                case GameEventType.CardDrawn:
                    GameAudio.Play(GameAudio.Draw);
                    break;

                case GameEventType.CardMoved:
                    GameAudio.Play(GameAudio.Move, 0.6f);
                    break;

                case GameEventType.CardRevealed:
                    GameAudio.Play(GameAudio.Reveal);
                    break;

                case GameEventType.PlayedToStage:
                    GameAudio.Play(GameAudio.PlayCard);
                    break;

                case GameEventType.Bloomed:
                    GameAudio.Play(GameAudio.Bloom);
                    break;

                case GameEventType.MovedToCollab:
                    GameAudio.Play(GameAudio.PlayCard);
                    break;

                case GameEventType.BatonPassed:
                    GameAudio.Play(GameAudio.Move);
                    break;

                case GameEventType.CheerAttached:
                    GameAudio.Play(GameAudio.Cheer);
                    break;

                case GameEventType.CheerArchived:
                    GameAudio.Play(GameAudio.ToArchive, 0.7f);
                    break;

                case GameEventType.SupportPlayed:
                    GameAudio.Play(GameAudio.Support);
                    break;

                case GameEventType.SupportAttached:
                    GameAudio.Play(GameAudio.Attach);
                    break;

                case GameEventType.ArtUsed:
                    GameAudio.Play(GameAudio.AttackStart);
                    break;

                case GameEventType.DamageDealt:

                    // —ильный удар звучит весомее:
                    // по звуку должно быть слышно,
                    // насколько плохо дело.
                    GameAudio.Play(
                        e.Amount >= BigHitFrom
                            ? GameAudio.BigHit
                            : GameAudio.Hit);

                    break;

                case GameEventType.Healed:
                    GameAudio.Play(GameAudio.Heal);
                    break;

                case GameEventType.CardDowned:
                    GameAudio.Play(GameAudio.Downed);
                    break;

                case GameEventType.DieRolled:
                    GameAudio.Play(GameAudio.DiceStop);
                    break;

                case GameEventType.OshiSkillUsed:
                    GameAudio.Play(GameAudio.Effect);
                    break;

                case GameEventType.LifeLost:
                    GameAudio.Play(GameAudio.LifeLost);
                    break;

                case GameEventType.GameOver:
                    Finish();
                    break;
            }
        }


        /// <summary>
        /// —мена хода и конец партии Ч не событи€,
        /// а состо€ние, поэтому ловим их отдельно.
        /// </summary>
        private void OnStateChanged()
        {
            var state =
                _session.State;

            if (state == null)
                return;


            if (state.IsFinished)
            {
                Finish();
                return;
            }


            if (state.TurnNumber == _lastTurn)
                return;


            _lastTurn = state.TurnNumber;


            // ѕервый ход не объ€вл€ем: он совпадает
            // с началом партии, и звук сливаетс€
            // с раздачей.
            if (state.TurnNumber <= 1)
                return;


            GameAudio.Play(
                state.CurrentPlayerIndex ==
                    _session.LocalPlayerIndex
                    ? GameAudio.YourTurn
                    : GameAudio.EnemyTurn);
        }


        private void Finish()
        {
            if (_finished)
                return;


            var state =
                _session.State;

            if (state == null || !state.IsFinished)
                return;


            _finished = true;


            GameAudio.Play(
                state.WinnerIndex == _session.LocalPlayerIndex
                    ? GameAudio.Victory
                    : GameAudio.Defeat);
        }
    }
}