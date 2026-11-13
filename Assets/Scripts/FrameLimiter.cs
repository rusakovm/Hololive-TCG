using UnityEngine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// ќграничение частоты кадров.
    ///
    ///  арточной игре нечего считать, поэтому без
    /// ограничени€ видеокарта рисует по нескольку
    /// сотен кадров в секунду Ч впустую. ќтсюда
    /// и шум вентил€торов на мощной карте: загрузка
    /// стопроцентна€, а на экране ничего
    /// не мен€етс€.
    ///
    /// “ридцати-шестидес€ти кадров здесь хватает
    /// с избытком: анимации у нас плавные за счЄт
    /// движени€ к цели, а не за счЄт частоты.
    /// </summary>
    public class FrameLimiter : MonoBehaviour
    {
        [Tooltip("ѕредел кадров в секунду. " +
                 "0 Ч без ограничени€")]
        public int TargetFrameRate = 60;

        [Tooltip("∆дать развЄртку монитора. " +
                 "0 Ч не ждать, 1 Ч каждый кадр, " +
                 "2 Ч через один")]
        [Range(0, 2)]
        public int VSync = 1;

        [Tooltip("ѕонижать предел, когда окно " +
                 "не в фокусе Ч чтобы игра в фоне " +
                 "не грела видеокарту")]
        public bool EaseWhenUnfocused = true;

        [Tooltip("ѕредел в фоне")]
        public int BackgroundFrameRate = 15;


        private bool _focused = true;


        private void Start()
        {
            Apply();
        }


        private void OnApplicationFocus(
            bool focused)
        {
            _focused = focused;

            Apply();
        }


        /// <summary>
        /// Ќастройки можно мен€ть пр€мо во врем€
        /// игры Ч примен€ютс€ сразу.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying)
                Apply();
        }


        private void Apply()
        {
            QualitySettings.vSyncCount = VSync;


            // ѕри включЄнном ожидании развЄртки
            // предел кадров Unity игнорирует,
            // поэтому он нужен в первую очередь
            // тем, у кого развЄртки нет.
            Application.targetFrameRate =
                !_focused && EaseWhenUnfocused
                    ? BackgroundFrameRate
                    : TargetFrameRate;
        }
    }
}