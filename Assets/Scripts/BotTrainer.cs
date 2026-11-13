using System.Collections;
using System.Diagnostics;
using System.Threading;

using UnityEngine;

using HololiveTCG.HololiveTCG.AI;

using Debug = UnityEngine.Debug;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Обучение бота самоигрой.
    ///
    /// Кадр отдаётся движку после каждой партии,
    /// а не после поколения: серия из восьми партий
    /// за один кадр выглядит как зависший редактор.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class BotTrainer : MonoBehaviour
    {
        [Header("Колоды для обучения")]
        public string DeckA = "hSD01_SORAZ";
        public string DeckB = "hSD03_OKAYU";

        [Header("Объём обучения")]
        public int Generations = 10;

        [Tooltip("Партий в поединке поколений. " +
                 "Число должно быть чётным: партии " +
                 "играются парами на одной раздаче " +
                 "со сменой сторон")]
        public int MatchesPerGeneration = 20;

        [Tooltip("Насколько сильно меняются веса. " +
                 "На плато нужны мелкие шаги")]
        [Range(0.03f, 0.6f)]
        public float Mutation = 0.12f;

        [Tooltip("Какую долю партий должен выиграть " +
                 "претендент, чтобы его приняли. " +
                 "0.55 это почти монетка — " +
                 "принимается шум")]
        [Range(0.5f, 0.8f)]
        public float AcceptThreshold = 0.62f;

        [Tooltip("Сколько пар должны разойтись в исходе, " +
                 "чтобы выводу можно было верить. " +
                 "Зеркальные пары ничего не значат")]
        public int MinDecisivePairs = 3;

        [Tooltip("Предел, до которого разрешено " +
                 "разгонять мутацию, когда поколения " +
                 "выходят зеркальными")]
        [Range(0.2f, 2f)]
        public float MaxMutation = 1.2f;

        [Header("Глубина при обучении")]
        [Tooltip("Глубина перебора при обучении. " +
                 "Должна совпадать с той, что бот " +
                 "использует в игре, иначе веса " +
                 "подбираются под другого бота")]
        public int Depth = 2;

        public int NodeBudget = 120;

        [Tooltip("Предел действий на партию")]
        public int MaxActions = 800;

        [Tooltip("Сколько одинаковых действий подряд " +
                 "считать зацикливанием")]
        public int LoopLimit = 60;

        [Tooltip("Сколько секунд ждать партию, " +
                 "прежде чем признать её зависшей")]
        public float MatchTimeout = 25f;

        [Header("Показ")]
        public bool ShowProgressOnScreen = true;


        public bool Running { get; private set; }

        public string Status { get; private set; }


        private GameSession _session;
        private int _lastMatchActions;
        private string _lastError;


        private void Awake()
        {
            _session = GetComponent<GameSession>();

            Status = "обучение не запускалось";
        }


        [ContextMenu("Обучить бота")]
        public void StartTraining()
        {
            if (Running)
            {
                Debug.LogWarning("Обучение уже идёт.");
                return;
            }


            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "Обучение работает только в режиме " +
                    "игры: базы карт загружаются при старте."
                );

                return;
            }


            StartCoroutine(TrainRoutine());
        }


        [ContextMenu("Остановить обучение")]
        public void StopTraining()
        {
            Running = false;
        }


        private IEnumerator TrainRoutine()
        {
            Running = true;

            var timer =
                Stopwatch.StartNew();


            var play =
                new SelfPlay(
                    _session.Cards,
                    _session.Oshi,
                    _session.Decks
                );

            play.Depth = Depth;
            play.NodeBudget = NodeBudget;
            play.MaxActions = MaxActions;
            play.LoopLimit = LoopLimit;


            var trainer =
                new WeightTrainer(
                    play,
                    Random.Range(1, int.MaxValue)
                );

            trainer.Mutation = Mutation;
            trainer.AcceptThreshold = AcceptThreshold;
            trainer.MatchesPerGeneration =
                MatchesPerGeneration;


            var champion =
                new EvaluationWeights(
                    _session.Memory.BotWeights
                );


            // Видно ли, что бот уже чему-то учился.
            //
            // Без этого невозможно отличить
            // "продолжаем накопленное" от
            // "начали с нуля" — а начиналось
            // с нуля каждый раз, когда менялся
            // набор признаков.
            Debug.Log(
                "Старт обучения. Поколений в памяти: " +
                _session.Memory.Generations +
                ". Веса " +
                (SameAsDefault(champion)
                    ? "исходные (обучения ещё не было)"
                    : "уже изменённые обучением") +
                ". Бот ценит: " +
                champion.Describe(4)
            );


            int accepted = 0;

            double mutation = Mutation;


            for (int generation = 1;
                 generation <= Generations && Running;
                 generation++)
            {
                // Новый набор раздач на поколение:
                // иначе веса подгонятся под одни
                // и те же партии.
                trainer.NewSeedBase();


                // Если прошлое поколение оказалось
                // целиком зеркальным, значит сдвиг
                // весов не меняет решений вообще.
                // Маленькие шаги здесь бесполезны:
                // нужно бить сильнее, иначе обучение
                // просто стоит на месте.
                trainer.Mutation = mutation;


                var challenger =
                    trainer.Mutate(champion);


                // Считаем не партии, а ПАРЫ.
                //
                // Пара — это одна раздача, сыгранная
                // дважды со сменой сторон. Если оба
                // набора весов играют одинаково,
                // пара даёт 1:1 и не говорит ничего.
                // Раньше такие пары попадали в счёт
                // и намертво держали результат
                // на пятидесяти процентах.
                int decisiveWins = 0;
                int decisiveLosses = 0;
                int mirrored = 0;

                bool firstOfPair = false;
                int pairsPlayed = 0;


                for (int match = 0;
                     match < MatchesPerGeneration && Running;
                     match++)
                {
                    Status =
                        "поколение " + generation +
                        " из " + Generations +
                        ", партия " + (match + 1) +
                        " из " + MatchesPerGeneration +
                        (_lastMatchActions > 0
                            ? "  (прошлая: " +
                              _lastMatchActions +
                              " действий)"
                            : "");


                    yield return null;


                    bool won = false;
                    bool done = false;

                    var worker =
                        new Thread(
                            delegate ()
                            {
                                try
                                {
                                    won =
                                        trainer.PlayOne(
                                            challenger,
                                            champion,
                                            DeckA,
                                            DeckB,
                                            match
                                        );
                                }
                                catch (System.Exception error)
                                {
                                    _lastError =
                                        error.GetType().Name +
                                        ": " +
                                        error.Message;
                                }

                                done = true;
                            }
                        );

                    worker.IsBackground = true;
                    worker.Start();


                    float waited = 0f;

                    while (!done && waited < MatchTimeout)
                    {
                        waited += Time.unscaledDeltaTime;

                        yield return null;
                    }


                    if (!done)
                    {
                        Debug.LogWarning(
                            "  партия " + (match + 1) +
                            ": зависла, пропускаем"
                        );

                        continue;
                    }


                    _lastMatchActions = play.LastActions;

                    Debug.Log(
                        "  партия " + (match + 1) +
                        ": " + play.LastReport +
                        (_lastError == null
                            ? ""
                            : "  ОШИБКА: " + _lastError)
                    );

                    _lastError = null;


                    // Первая партия пары запоминается,
                    // вторая сравнивается с ней.
                    if (match % 2 == 0)
                    {
                        firstOfPair = won;
                        continue;
                    }


                    pairsPlayed++;

                    if (won && firstOfPair)
                        decisiveWins++;
                    else if (!won && !firstOfPair)
                        decisiveLosses++;
                    else
                        mirrored++;
                }


                if (!Running)
                    break;


                int decisive =
                    decisiveWins + decisiveLosses;

                double rate =
                    decisive == 0
                        ? 0.5
                        : (double)decisiveWins / decisive;

                bool better =
                    decisive >= MinDecisivePairs &&
                    rate >= AcceptThreshold;

                if (better)
                {
                    champion = challenger;
                    accepted++;

                    // Нашли улучшение — дальше
                    // ищем осторожнее.
                    mutation =
                        System.Math.Max(
                            Mutation,
                            mutation * 0.7
                        );
                }
                else if (decisive < MinDecisivePairs)
                {
                    // Различий не было вовсе —
                    // увеличиваем шаг.
                    mutation =
                        System.Math.Min(
                            MaxMutation,
                            mutation * 1.6
                        );
                }


                Debug.Log(
                    "Поколение " + generation +
                    " (мутация " +
                    mutation.ToString("0.00") +
                    "): пар " + pairsPlayed +
                    ", зеркальных " + mirrored +
                    ", решающих " + decisive +
                    " (выиграно " + decisiveWins +
                    ", " + (int)(rate * 100) + "%)" +
                    (better
                        ? " — принят"
                        : decisive < MinDecisivePairs
                            ? " — слишком мало различий"
                            : " — отклонён")
                );


                yield return null;
            }


            _session.Memory.BotWeights =
                champion.Values;

            _session.Memory.Generations +=
                Generations;

            _session.SaveMemory();


            // Проверяем, что сохранение реально
            // произошло: молчаливая потеря опыта —
            // худшее, что может случиться
            // с обучением.
            Debug.Log(
                "Сохранено. Поколений в памяти: " +
                _session.Memory.Generations +
                ". Веса " +
                (SameAsDefault(champion)
                    ? "остались исходными"
                    : "изменены обучением") +
                ". Проверка — " +
                _session.InspectSavedMemory()
            );


            timer.Stop();

            Status =
                "готово: принято " + accepted +
                " улучшений, " +
                (int)timer.Elapsed.TotalSeconds +
                " секунд";

            Debug.Log(
                Status +
                ". Доиграно " + play.Finished +
                ", оборвано " + play.Aborted +
                ". Бот ценит: " +
                champion.Describe(4)
            );


            if (play.Aborted > play.Finished)
            {
                Debug.LogWarning(
                    "Больше половины партий не " +
                    "доигрывается до конца — обучение " +
                    "тратит время впустую. Стоит " +
                    "разобраться, почему партии " +
                    "не заканчиваются."
                );
            }

            Running = false;
        }


        /// <summary>
        /// Отличаются ли веса от заводских.
        /// </summary>
        private static bool SameAsDefault(
            EvaluationWeights weights)
        {
            var defaults =
                EvaluationWeights.Default();

            for (int i = 0;
                 i < EvaluationWeights.Count;
                 i++)
            {
                if (System.Math.Abs(
                    weights.Values[i] - defaults[i]) > 0.001)
                {
                    return false;
                }
            }

            return true;
        }


        /// <summary>
        /// Одна кнопка, которая отвечает на вопрос
        /// "сохраняется ли вообще что-нибудь".
        ///
        /// Прибавляет поколение, сохраняет и тут же
        /// перечитывает файл с диска. Если число
        /// в файле не выросло — проблема в записи,
        /// а не в обучении.
        /// </summary>
        [ContextMenu("Проверить сохранение памяти")]
        public void TestMemorySave()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "Проверка работает только " +
                    "в режиме игры."
                );

                return;
            }


            if (_session == null)
                _session = GetComponent<GameSession>();


            int before =
                _session.Memory.Generations;

            _session.Memory.Generations = before + 1;

            _session.SaveMemory();


            Debug.Log(
                "Было " + before +
                ", стало в памяти " +
                _session.Memory.Generations +
                ", " +
                _session.InspectSavedMemory()
            );
        }


        [ContextMenu("Сбросить обучение")]
        public void ResetTraining()
        {
            if (_session == null ||
                _session.Memory == null)
            {
                return;
            }


            _session.Memory.BotWeights =
                EvaluationWeights.Default();

            _session.Memory.Generations = 0;

            _session.SaveMemory();

            Debug.Log("Обучение бота сброшено.");
        }


        private void OnGUI()
        {
            if (!ShowProgressOnScreen || !Running)
                return;


            GUI.Box(
                new Rect(
                    Screen.width * 0.5f - 200f,
                    10f,
                    400f,
                    28f
                ),
                "Обучение: " + Status
            );
        }
    }
}