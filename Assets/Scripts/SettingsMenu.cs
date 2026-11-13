using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Настройки игры в одном месте.
    ///
    /// Всё, что мы разводили по разным компонентам —
    /// громкость, фильтрация картинок, предел кадров,
    /// размер карт, вид камеры, — собрано здесь
    /// и запоминается между запусками.
    ///
    /// Открывается клавишей и кнопкой из меню.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        private enum Tab
        {
            Sound,
            Graphics,
            Table
        }


        [Tooltip("Клавиша открытия")]
        public KeyCode OpenKey = KeyCode.Escape;

        public bool IsOpen { get; private set; }


        private Tab _tab = Tab.Sound;

        private GameAudio _audio;
        private GraphicsTuner _graphics;
        private CameraFocus _camera;
        private FrameLimiter _frames;
        private OshiCutIn _cutIn;
        private GameSession _session;

        private Vector2 _scroll;


        private void Awake()
        {
            _audio = FindObjectOfType<GameAudio>();
            _graphics = FindObjectOfType<GraphicsTuner>();
            _camera = FindObjectOfType<CameraFocus>();
            _frames = FindObjectOfType<FrameLimiter>();
            _cutIn = FindObjectOfType<OshiCutIn>();
            _session = FindObjectOfType<GameSession>();
        }


        private void Start()
        {
            Load();
        }


        private void Update()
        {
            if (Pressed())
                IsOpen = !IsOpen;
        }


        private bool Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard =
                Keyboard.current;

            if (keyboard == null)
                return false;


            return OpenKey == KeyCode.Escape
                ? keyboard.escapeKey.wasPressedThisFrame
                : keyboard.f10Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(OpenKey);
#endif
        }


        public void Open()
        {
            IsOpen = true;
        }


        // =========================================
        // ЭКРАН
        // =========================================

        private void OnGUI()
        {
            if (!IsOpen)
                return;


            float width = 520f;
            float height = 560f;

            var area =
                new Rect(
                    (Screen.width - width) * 0.5f,
                    (Screen.height - height) * 0.5f,
                    width,
                    height);

            GUILayout.BeginArea(area, GUI.skin.box);


            GUILayout.BeginHorizontal();

            Tabs();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Закрыть", GUILayout.Width(90f)))
            {
                Save();

                IsOpen = false;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(8f);


            _scroll =
                GUILayout.BeginScrollView(_scroll);

            switch (_tab)
            {
                case Tab.Sound: SoundTab(); break;
                case Tab.Graphics: GraphicsTab(); break;
                case Tab.Table: TableTab(); break;
            }

            GUILayout.EndScrollView();


            GUILayout.Space(6f);

            if (GUILayout.Button("Сбросить всё", GUILayout.Height(26f)))
                Reset();

            GUILayout.EndArea();
        }


        private void Tabs()
        {
            TabButton(Tab.Sound, "Звук");
            TabButton(Tab.Graphics, "Графика");
            TabButton(Tab.Table, "Стол");
        }


        // Название метода не должно совпадать
        // с названием перечисления: для C# это
        // два объявления одного имени.
        private void TabButton(
            Tab tab,
            string title)
        {
            GUI.color =
                _tab == tab ? Color.cyan : Color.white;

            if (GUILayout.Button(title, GUILayout.Width(110f)))
                _tab = tab;

            GUI.color = Color.white;
        }


        // =========================================
        // ВКЛАДКИ
        // =========================================

        private void SoundTab()
        {
            if (_audio == null)
            {
                GUILayout.Label("Звуковой компонент не найден.");
                return;
            }


            _audio.SfxVolume =
                Slider("Громкость звуков",
                       _audio.SfxVolume, 0f, 1f);

            _audio.MusicVolume =
                Slider("Громкость музыки",
                       _audio.MusicVolume, 0f, 1f);

            _audio.PitchSpread =
                Slider("Разброс высоты",
                       _audio.PitchSpread, 0f, 0.3f);


            GUILayout.Space(6f);

            if (GUILayout.Button("Следующий трек",
                GUILayout.Height(26f)))
            {
                _audio.SkipTrack();
            }
        }


        private void GraphicsTab()
        {
            if (_graphics != null)
            {
                GUILayout.Label("Картинки карт");

                bool point =
                    _graphics.Filter == FilterMode.Point;

                bool now =
                    GUILayout.Toggle(
                        point,
                        " Резкая фильтрация (Point). " +
                        "Чётче, но может рябить");

                if (now != point)
                {
                    _graphics.Filter =
                        now
                            ? FilterMode.Point
                            : FilterMode.Trilinear;
                }


                _graphics.Aniso =
                    (int)Slider("Анизотропия",
                                _graphics.Aniso, 0f, 16f);

                _graphics.MipBias =
                    Slider("Резкость уменьшенных",
                           _graphics.MipBias, -1f, 1f);


                GUILayout.Space(6f);
            }


            if (_frames == null)
                return;


            GUILayout.Label("Нагрузка");

            _frames.TargetFrameRate =
                (int)Slider("Предел кадров",
                            _frames.TargetFrameRate, 30f, 240f);

            _frames.VSync =
                (int)Slider("Ждать развёртку (0-2)",
                            _frames.VSync, 0f, 2f);
        }


        private void TableTab()
        {
            if (_camera != null)
            {
                GUILayout.Label("Карты");

                _camera.StageCardScale =
                    Slider("Размер на столе",
                           _camera.StageCardScale, 0.4f, 3f);

                _camera.HandCardScale =
                    Slider("Размер в руке",
                           _camera.HandCardScale, 0.4f, 3f);


                GUILayout.Space(6f);
                GUILayout.Label("Вид на стол");

                _camera.TableOrthographic =
                    GUILayout.Toggle(
                        _camera.TableOrthographic,
                        " Без перспективы");

                _camera.TableOrthoSize =
                    Slider("Приближение",
                           _camera.TableOrthoSize, 2f, 12f);

                _camera.TablePitch =
                    Slider("Наклон", _camera.TablePitch, 10f, 90f);


                GUILayout.Space(6f);
                GUILayout.Label("Вид на руку");

                _camera.HandPitch =
                    Slider("Наклон", _camera.HandPitch, 10f, 90f);

                _camera.HandDistance =
                    Slider("Расстояние",
                           _camera.HandDistance, 1f, 8f);

                _camera.HandAway =
                    Slider("Рука от центра",
                           _camera.HandAway, -3f, 3f);
            }


            if (_cutIn == null)
                return;


            GUILayout.Space(6f);
            GUILayout.Label("Врезка скилла оси");

            _cutIn.ShowForOpponent =
                GUILayout.Toggle(
                    _cutIn.ShowForOpponent,
                    " Показывать и за соперника");

            _cutIn.Duration =
                Slider("Длительность",
                       _cutIn.Duration, 0.3f, 2f);

            _cutIn.SpDuration =
                Slider("Длительность SP",
                       _cutIn.SpDuration, 0.5f, 3f);
        }


        private static float Slider(
            string title,
            float value,
            float min,
            float max)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(title, GUILayout.Width(190f));

            float result =
                GUILayout.HorizontalSlider(
                    value, min, max,
                    GUILayout.Width(200f));

            GUILayout.Label(
                result.ToString("0.##"),
                GUILayout.Width(50f));

            GUILayout.EndHorizontal();

            return result;
        }


        // =========================================
        // ПАМЯТЬ НАСТРОЕК
        // =========================================

        /// <summary>
        /// Настройки живут между запусками.
        /// Подбирать их заново каждый раз —
        /// худшее, что можно сделать.
        /// </summary>
        public void Save()
        {
            if (_audio != null)
            {
                PlayerPrefs.SetFloat("snd", _audio.SfxVolume);
                PlayerPrefs.SetFloat("mus", _audio.MusicVolume);
            }


            if (_graphics != null)
            {
                PlayerPrefs.SetInt(
                    "filter",
                    _graphics.Filter == FilterMode.Point ? 1 : 0);

                PlayerPrefs.SetInt("aniso", _graphics.Aniso);
                PlayerPrefs.SetFloat("mip", _graphics.MipBias);
            }


            if (_frames != null)
            {
                PlayerPrefs.SetInt("fps", _frames.TargetFrameRate);
                PlayerPrefs.SetInt("vsync", _frames.VSync);
            }


            if (_camera != null)
            {
                PlayerPrefs.SetFloat("stage", _camera.StageCardScale);
                PlayerPrefs.SetFloat("hand", _camera.HandCardScale);
                PlayerPrefs.SetFloat("ortho", _camera.TableOrthoSize);
                PlayerPrefs.SetFloat("pitch", _camera.TablePitch);
                PlayerPrefs.SetFloat("hpitch", _camera.HandPitch);
                PlayerPrefs.SetFloat("hdist", _camera.HandDistance);
                PlayerPrefs.SetFloat("haway", _camera.HandAway);
            }


            PlayerPrefs.Save();
        }


        private void Load()
        {
            if (_audio != null)
            {
                _audio.SfxVolume =
                    PlayerPrefs.GetFloat("snd", _audio.SfxVolume);

                _audio.MusicVolume =
                    PlayerPrefs.GetFloat("mus", _audio.MusicVolume);
            }


            if (_graphics != null)
            {
                _graphics.Filter =
                    PlayerPrefs.GetInt("filter", 0) == 1
                        ? FilterMode.Point
                        : FilterMode.Trilinear;

                _graphics.Aniso =
                    PlayerPrefs.GetInt("aniso", _graphics.Aniso);

                _graphics.MipBias =
                    PlayerPrefs.GetFloat("mip", _graphics.MipBias);
            }


            if (_frames != null)
            {
                _frames.TargetFrameRate =
                    PlayerPrefs.GetInt("fps", _frames.TargetFrameRate);

                _frames.VSync =
                    PlayerPrefs.GetInt("vsync", _frames.VSync);
            }


            if (_camera == null)
                return;


            _camera.StageCardScale =
                PlayerPrefs.GetFloat("stage", _camera.StageCardScale);

            _camera.HandCardScale =
                PlayerPrefs.GetFloat("hand", _camera.HandCardScale);

            _camera.TableOrthoSize =
                PlayerPrefs.GetFloat("ortho", _camera.TableOrthoSize);

            _camera.TablePitch =
                PlayerPrefs.GetFloat("pitch", _camera.TablePitch);

            _camera.HandPitch =
                PlayerPrefs.GetFloat("hpitch", _camera.HandPitch);

            _camera.HandDistance =
                PlayerPrefs.GetFloat("hdist", _camera.HandDistance);

            _camera.HandAway =
                PlayerPrefs.GetFloat("haway", _camera.HandAway);
        }


        [ContextMenu("Сбросить настройки")]
        public void Reset()
        {
            PlayerPrefs.DeleteKey("snd");
            PlayerPrefs.DeleteKey("mus");
            PlayerPrefs.DeleteKey("filter");
            PlayerPrefs.DeleteKey("aniso");
            PlayerPrefs.DeleteKey("mip");
            PlayerPrefs.DeleteKey("fps");
            PlayerPrefs.DeleteKey("vsync");
            PlayerPrefs.DeleteKey("stage");
            PlayerPrefs.DeleteKey("hand");
            PlayerPrefs.DeleteKey("ortho");
            PlayerPrefs.DeleteKey("pitch");
            PlayerPrefs.DeleteKey("hpitch");
            PlayerPrefs.DeleteKey("hdist");
            PlayerPrefs.DeleteKey("haway");

            PlayerPrefs.Save();

            Debug.Log(
                "Настройки сброшены. " +
                "Значения вернутся при следующем запуске.");
        }
    }
}