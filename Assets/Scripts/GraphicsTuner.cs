using UnityEngine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Сравнение способов фильтрации прямо в игре.
    ///
    /// Спор "Point или Trilinear" решается не
    /// рассуждением, а глазами: переключите и
    /// посмотрите на стол, потом переведите взгляд
    /// на руку и подвигайте камеру. Рябь видно
    /// именно в движении.
    /// </summary>
    public class GraphicsTuner : MonoBehaviour
    {
        [Tooltip("Point режет резче, но рябит " +
                 "на уменьшенных и повёрнутых картах")]
        public FilterMode Filter = FilterMode.Trilinear;

        [Range(0, 16)]
        [Tooltip("Анизотропная фильтрация: " +
                 "спасает карты, лежащие под углом")]
        public int Aniso = 9;

        [Range(-1f, 1f)]
        [Tooltip("Смещение уровня детализации. " +
                 "Минус — резче и рябит, " +
                 "плюс — мягче и спокойнее")]
        public float MipBias = -0.25f;

        [Tooltip("Клавиша для переключения Point " +
                 "и Trilinear на лету")]
        public KeyCode SwitchKey = KeyCode.F2;


        /// <summary>
        /// Отчёт без клавиш: правый щелчок
        /// по заголовку компонента в инспекторе →
        /// "Отчёт о картинках".
        ///
        /// Работает и когда клавиши не читаются:
        /// при новой системе ввода, при потере
        /// фокуса окном, при любой раскладке.
        /// </summary>
        [ContextMenu("Отчёт о картинках")]
        public void PrintReport()
        {
            Debug.Log(CardArt.Report());
        }


        [ContextMenu("Переключить фильтрацию")]
        public void SwitchFilter()
        {
            Filter =
                Filter == FilterMode.Point
                    ? FilterMode.Trilinear
                    : FilterMode.Point;

            Apply();

            Debug.Log("Фильтрация карт: " + Filter);
        }


        private void Awake()
        {
            // Пишем в Awake, а не в Start: если
            // объект выключен или скрипт не
            // прикреплён, не будет и этой строки —
            // сразу ясно, где искать.
            Debug.Log(
                "GraphicsTuner запущен на объекте " +
                gameObject.name
            );
        }


        private void Start()
        {
            Apply();

            Debug.Log(
                "GraphicsTuner работает. " +
                "F2 — сменить фильтрацию, " +
                "F3 — отчёт о картинках карт."
            );
        }


        private void OnValidate()
        {
            if (Application.isPlaying)
                Apply();
        }


        private void Update()
        {
            if (SwitchKey == KeyCode.None)
                return;


            if (Pressed(KeyCode.F3))
            {
                Debug.Log(CardArt.Report());
                return;
            }


            if (!Pressed(SwitchKey))
                return;


            Filter =
                Filter == FilterMode.Point
                    ? FilterMode.Trilinear
                    : FilterMode.Point;

            Apply();

            Debug.Log(
                "Фильтрация карт: " + Filter +
                ". Если разницы не видно — смотрите " +
                "отчёт по F3."
            );
        }


        private static bool Pressed(
            KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard =
                UnityEngine.InputSystem.Keyboard.current;

            if (keyboard == null)
                return false;


            if (key == KeyCode.F2)
                return keyboard.f2Key.wasPressedThisFrame;

            if (key == KeyCode.F3)
                return keyboard.f3Key.wasPressedThisFrame;

            if (key == KeyCode.F4)
                return keyboard.f4Key.wasPressedThisFrame;

            return false;
#else
            return Input.GetKeyDown(key);
#endif
        }


        private void Apply()
        {
            CardArt.Filter = Filter;
            CardArt.AnisoLevel = Aniso;
            CardArt.MipBias = MipBias;

            CardArt.RefreshFiltering();
        }
    }
}