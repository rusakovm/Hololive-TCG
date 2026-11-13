using System.Collections.Generic;

using UnityEngine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Звук игры: короткие эффекты и фоновая музыка.
    ///
    /// Файлы кладутся в Assets/Resources/Sounds
    /// и Assets/Resources/Music. Имена — из списка
    /// ниже. Чего нет, то просто не звучит: игра
    /// работает и без единого файла, поэтому можно
    /// добавлять их по одному.
    ///
    /// Один и тот же звук слегка меняет высоту при
    /// каждом воспроизведении — иначе повторяющиеся
    /// щелчки быстро приедаются.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        // ==== имена звуков ====
        // Наведение и выбор
        public const string Hover = "hover";
        public const string Select = "select";
        public const string Cancel = "cancel";
        public const string Zoom = "zoom";
        public const string Denied = "denied";

        // Карты
        public const string Draw = "draw";
        public const string ToHand = "to-hand";
        public const string PlayCard = "play-card";
        public const string Move = "move";
        public const string ToArchive = "to-archive";
        public const string Reveal = "reveal";
        public const string Flip = "flip";
        public const string Shuffle = "shuffle";
        public const string Mulligan = "mulligan";

        // Механики
        public const string Bloom = "bloom";
        public const string Attach = "attach";
        public const string Support = "support";
        public const string Cheer = "cheer";
        public const string Power = "power";
        public const string Spend = "spend";

        // Бой
        public const string AttackStart = "attack";
        public const string Hit = "hit";
        public const string BigHit = "big-hit";
        public const string Damage = "damage";
        public const string Heal = "heal";
        public const string Downed = "downed";
        public const string LifeLost = "life-lost";

        // Эффекты и выбор цели
        public const string Effect = "effect";
        public const string Lock = "lock";
        public const string Confirm = "confirm";

        // Кубик
        public const string DiceRoll = "dice-roll";
        public const string DiceStop = "dice-stop";

        // Ход и исход
        public const string YourTurn = "your-turn";
        public const string EnemyTurn = "enemy-turn";
        public const string Phase = "phase";
        public const string Victory = "victory";
        public const string Defeat = "defeat";


        [Header("Громкость")]
        [Range(0f, 1f)]
        public float SfxVolume = 0.8f;

        [Range(0f, 1f)]
        public float MusicVolume = 0.35f;

        [Tooltip("Разброс высоты звука, чтобы " +
                 "повторы не надоедали")]
        [Range(0f, 0.3f)]
        public float PitchSpread = 0.06f;


        [Header("Фоновая музыка")]
        [Tooltip("Имена файлов из Resources/Music. " +
                 "Играют по кругу")]
        public string[] Tracks = { "theme" };

        [Tooltip("Плавность смены громкости")]
        public float MusicFade = 1.5f;


        [Header("Защита от частоты")]
        [Tooltip("Один и тот же звук не повторяется " +
                 "чаще, чем раз в столько секунд")]
        public float MinRepeat = 0.04f;


        private static GameAudio _instance;

        private readonly List<AudioSource> _pool =
            new List<AudioSource>();

        private AudioSource _music;

        private readonly Dictionary<string, float> _lastPlayed =
            new Dictionary<string, float>();

        private readonly Dictionary<string, AudioClip> _clips =
            new Dictionary<string, AudioClip>();

        private readonly List<string> _missing =
            new List<string>();

        private int _track;


        private void Awake()
        {
            _instance = this;

            _music =
                gameObject.AddComponent<AudioSource>();

            _music.loop = false;
            _music.playOnAwake = false;
            _music.volume = 0f;
        }


        private void Start()
        {
            NextTrack();
        }


        // =========================================
        // ЭФФЕКТЫ
        // =========================================

        /// <summary>
        /// Проиграть звук по имени.
        /// Файла нет — тишина, и это нормально.
        /// </summary>
        public static void Play(
            string name,
            float volume = 1f)
        {
            if (_instance != null)
                _instance.PlayLocal(name, volume);
        }


        private void PlayLocal(
            string name,
            float volume)
        {
            if (string.IsNullOrEmpty(name))
                return;


            float last;

            if (_lastPlayed.TryGetValue(name, out last) &&
                Time.unscaledTime - last < MinRepeat)
            {
                return;
            }


            var clip =
                Clip(name);

            if (clip == null)
                return;


            _lastPlayed[name] = Time.unscaledTime;


            var source =
                FreeSource();

            source.clip = clip;
            source.volume = SfxVolume * volume;

            source.pitch =
                1f + Random.Range(-PitchSpread, PitchSpread);

            source.Play();
        }


        private AudioClip Clip(
            string name)
        {
            AudioClip clip;

            if (_clips.TryGetValue(name, out clip))
                return clip;


            clip =
                Resources.Load<AudioClip>("Sounds/" + name);


            if (clip == null)
            {
                // Пропуск не кешируем: файл могли
                // положить уже после запуска.
                if (!_missing.Contains(name))
                {
                    _missing.Add(name);

                    Debug.Log(
                        "Нет звука Sounds/" + name +
                        " — событие пройдёт молча.");
                }

                return null;
            }


            _clips[name] = clip;

            return clip;
        }


        /// <summary>
        /// Свободный источник звука.
        ///
        /// Их несколько, чтобы удар и звон эффекта
        /// могли звучать одновременно, не обрывая
        /// друг друга.
        /// </summary>
        private AudioSource FreeSource()
        {
            foreach (var source in _pool)
            {
                if (!source.isPlaying)
                    return source;
            }


            var created =
                gameObject.AddComponent<AudioSource>();

            created.playOnAwake = false;
            created.loop = false;

            _pool.Add(created);

            return created;
        }


        // =========================================
        // МУЗЫКА
        // =========================================

        private void Update()
        {
            if (_music == null)
                return;


            // Плавно подводим громкость к заданной.
            float target =
                _music.clip == null ? 0f : MusicVolume;

            _music.volume =
                Mathf.MoveTowards(
                    _music.volume,
                    target,
                    Time.unscaledDeltaTime /
                        Mathf.Max(MusicFade, 0.01f));


            if (_music.clip != null && !_music.isPlaying)
                NextTrack();
        }


        private void NextTrack()
        {
            if (Tracks == null || Tracks.Length == 0)
                return;


            var clip =
                Resources.Load<AudioClip>(
                    "Music/" + Tracks[_track % Tracks.Length]);

            _track++;


            if (clip == null)
                return;


            _music.clip = clip;
            _music.Play();
        }


        [ContextMenu("Следующий трек")]
        public void SkipTrack()
        {
            NextTrack();
        }
    }
}