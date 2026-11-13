using System;

using UnityEngine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Точки стола одной стороны.
    ///
    /// Это просто пустые объекты в сцене:
    /// куда класть центр, коллаб, бэк и стопки.
    /// Двигаете их в редакторе — двигается стол.
    /// </summary>
    [Serializable]
    public class BoardZones
    {
        public Transform Oshi;
        public Transform Center;
        public Transform Collab;

        [Tooltip("Пять мест заднего ряда, слева направо")]
        public Transform[] Back = new Transform[5];

        public Transform Hand;
        public Transform Deck;
        public Transform CheerDeck;
        public Transform Archive;
        public Transform HoloPower;
        public Transform Life;


        [Header("Раскладка руки")]
        [Tooltip("Расстояние между картами в руке")]
        public float HandSpacing = 1.05f;

        [Tooltip("Наибольшая ширина руки. Если карт много, " +
                 "шаг между ними уменьшается, чтобы рука " +
                 "не уезжала за край экрана")]
        public float HandMaxWidth = 9f;

        [Tooltip("Считать предел по тому, что видит камера, " +
                 "а не по числу выше. Тогда рука не вылезет " +
                 "за экран ни при каком разрешении")]
        public bool HandFitToCamera = true;

        [Tooltip("Какую долю ширины экрана занимает рука")]
        [Range(0.3f, 1f)]
        public float HandScreenShare = 0.9f;

        [Tooltip("Наименьший шаг между картами. " +
                 "Ниже него карты перестают наезжать " +
                 "сильнее, даже если их очень много")]
        public float HandMinSpacing = 0.22f;

        [Tooltip("Отодвинуть руку от центра стола (+) " +
                 "или придвинуть к нему (−). У игрока и " +
                 "соперника настраивается отдельно")]
        public float HandDistanceOffset = 0f;

        [Tooltip("Сдвинуть руку вбок")]
        public float HandSideOffset = 0f;

        [Tooltip("Поднять руку над столом")]
        public float HandHeightOffset = 0f;

        [Tooltip("Насколько карта в руке приподнята над соседней")]
        public float HandLift = 0.01f;

        [Tooltip("Во сколько раз карты в руке крупнее, " +
                 "чем на столе. 1.6 закрывает поле — " +
                 "для обзора удобнее 1.0–1.2")]
        public float HandScale = 1.1f;


        [Tooltip("Размер карт на сцене")]
        public float StageScale = 1f;


        [Header("Прикреплённые cheer")]
        [Tooltip("Смещение первого cheer относительно карты. " +
                 "X вправо, Y вверх по столу, Z над столом")]
        public Vector3 CheerOffset =
            new Vector3(-0.24f, -0.52f, 0.006f);

        [Tooltip("Сдвиг каждого следующего cheer")]
        public Vector3 CheerStep =
            new Vector3(0.13f, 0f, 0.001f);

        [Tooltip("Размер cheer относительно карты")]
        public float CheerScale = 0.5f;


        [Header("Прикреплённые саппорты")]
        [Tooltip("Смещение первого саппорта")]
        public Vector3 SupportOffset =
            new Vector3(0.42f, 0.16f, 0.006f);

        public Vector3 SupportStep =
            new Vector3(0f, -0.16f, 0.001f);

        [Tooltip("Размер прикреплённого саппорта")]
        public float SupportScale = 0.55f;


        [Header("Стопки")]
        [Tooltip("Сдвиг каждой следующей карты в стопке")]
        public float StackOffset = 0.004f;


        /// <summary>
        /// Показывать ли руку лицом вверх.
        /// У бота выключено.
        /// </summary>
        public bool HandFaceUp = true;
    }
}