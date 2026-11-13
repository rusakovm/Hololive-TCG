using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Расставляет зоны стола по формуле,
    /// вместо того чтобы двигать два десятка
    /// объектов мышью.
    ///
    /// Повесьте на объект Table, укажите PlayerSide
    /// и BotSide, потом правой кнопкой по компоненту
    /// в инспекторе → "Построить стол".
    ///
    /// Работает в режиме редактора, Play не нужен.
    /// Цифры внизу можно крутить и строить заново,
    /// пока не понравится.
    /// </summary>
    public class TableBuilder : MonoBehaviour
    {
        [Tooltip("Собирать стол при запуске игры. " +
                 "Тогда сохранять расстановку в сцене " +
                 "не обязательно")]
        public bool BuildOnAwake = true;


        [Header("Стороны стола")]
        public Transform PlayerSide;
        public Transform BotSide;

        [Tooltip("Заполнить поля зон в BoardView автоматически")]
        public BoardView Board;

        [Tooltip("Поставить камеру над столом")]
        public Camera TableCamera;


        [Header("Коврик")]
        [Tooltip("Класть под стол коврик")]
        public bool BuildMat = true;

        [Tooltip("Размер коврика: ширина и глубина")]
        public Vector2 MatSize = new Vector2(9f, 8f);

        [Tooltip("Материал коврика. " +
                 "Создайте его в проекте (Create → Material), " +
                 "положите в него картинку мата и перетащите сюда. " +
                 "Только так картинка сохранится в сцене")]
        public Material MatMaterial;

        [Tooltip("Цвет, если материал не задан. " +
                 "Действует только во время игры")]
        public Color MatColor = new Color(0.16f, 0.22f, 0.18f);


        [Header("Ориентация карт")]
        [Tooltip("Все карты повёрнуты к игроку. " +
                 "Выключите, если хотите как за настоящим столом — " +
                 "карты бота к нему")]
        public bool AllCardsFacePlayer = true;

        [Tooltip("Доворот карт вокруг вертикали, градусы. " +
                 "Если текст на картах вверх ногами — поменяйте на 0")]
        public float CardYaw = 180f;


        [Header("Размеры")]
        [Tooltip("Шаг между местами заднего ряда")]
        public float BackSpacing = 0.75f;

        [Tooltip("Как далеко от центра стола стоит центровой")]
        public float CenterDistance = 1.0f;

        [Tooltip("Как далеко задний ряд")]
        public float BackDistance = 1.9f;

        [Tooltip("Сдвиг коллаба влево от центра")]
        public float CollabOffset = 0.85f;

        [Tooltip("Насколько вбок вынесены стопки")]
        public float SideOffset = 1.9f;

        [Tooltip("Расстояние до руки")]
        public float HandDistance = 4.1f;

        [Tooltip("Высота руки над столом")]
        public float HandHeight = 0.9f;

        [Tooltip("Наклон руки к камере, градусы")]
        public float HandTilt = 70f;


        [Header("Камера")]
        [Tooltip("Высота камеры над столом")]
        public float CameraHeight = 3.4f;

        [Tooltip("Насколько камера отодвинута к игроку")]
        public float CameraDistance = 5.6f;

        [Tooltip("Наклон камеры вниз, градусы")]
        public float CameraPitch = 40f;

        [Tooltip("Угол обзора. Меньше — площе и дальше")]
        public float CameraFov = 55f;


        private void Awake()
        {
            if (BuildOnAwake)
                Build();
        }


        [ContextMenu("Построить стол")]
        public void Build()
        {
            if (PlayerSide != null)
                BuildSide(PlayerSide, -1f);

            if (BotSide != null)
                BuildSide(BotSide, 1f);


            if (Board != null)
            {
                Assign(Board.PlayerZones, PlayerSide);
                Assign(Board.BotZones, BotSide);

                Board.BotZones.HandFaceUp = false;
            }


            if (BuildMat)
                MakeMat();


            PlaceCamera();


#if UNITY_EDITOR
            // Без этого правки, сделанные скриптом
            // в редакторе, не попадают в сохранённую
            // сцену — и стол приходится собирать заново.
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(gameObject);

                UnityEditor.SceneManagement
                    .EditorSceneManager
                    .MarkSceneDirty(gameObject.scene);
            }
#endif

            Debug.Log("Стол построен. Не забудьте сохранить сцену.");
        }


        /// <summary>
        /// side = -1 для ближней стороны,
        /// +1 для дальней.
        /// </summary>
        private void BuildSide(
            Transform root,
            float side)
        {
            // Карты лежат на столе лицом вверх.
            // Quad смотрит в +Z, поэтому наклоняем
            // его на 90 градусов.
            //
            // Дальняя сторона дополнительно
            // развёрнута, чтобы её карты
            // "смотрели" на своего игрока.
            float yaw =
                AllCardsFacePlayer
                    ? CardYaw
                    : (side < 0f ? CardYaw : CardYaw + 180f);


            Quaternion flat =
                Quaternion.Euler(
                    -90f,
                    yaw,
                    0f
                );


            Put(root, "Center",
                new Vector3(0f, 0f, side * CenterDistance), flat);

            Put(root, "Collab",
                new Vector3(-side * CollabOffset, 0f, side * CenterDistance), flat);


            float start =
                -(4f * BackSpacing) * 0.5f;

            for (int i = 0; i < 5; i++)
            {
                Put(root, "Back" + (i + 1),
                    new Vector3(
                        side * (start + i * BackSpacing),
                        0f,
                        side * BackDistance),
                    flat);
            }


            Put(root, "Oshi",
                new Vector3(-side * SideOffset, 0f, side * BackDistance), flat);

            Put(root, "Life",
                new Vector3(-side * SideOffset, 0f, side * CenterDistance), flat);

            Put(root, "Deck",
                new Vector3(side * SideOffset, 0f, side * BackDistance), flat);

            Put(root, "CheerDeck",
                new Vector3(side * SideOffset, 0f, side * CenterDistance), flat);

            Put(root, "Archive",
                new Vector3(side * (SideOffset + 0.8f), 0f, side * BackDistance), flat);

            Put(root, "HoloPower",
                new Vector3(side * (SideOffset + 0.8f), 0f, side * CenterDistance), flat);


            // Рука стоит вертикальнее остального,
            // чтобы её было видно с камеры.
            Put(root, "Hand",
                new Vector3(0f, HandHeight, side * HandDistance),
                Quaternion.Euler(
                    -HandTilt,
                    yaw,
                    0f
                ));
        }


        private static void Put(
            Transform root,
            string childName,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            var child =
                root.Find(childName);

            if (child == null)
            {
                Debug.LogWarning(
                    "Нет объекта " +
                    childName +
                    " внутри " +
                    root.name
                );

                return;
            }


            child.localPosition = localPosition;
            child.localRotation = localRotation;
        }


        private static void Assign(
            BoardZones zones,
            Transform root)
        {
            if (root == null)
                return;


            zones.Center = root.Find("Center");
            zones.Collab = root.Find("Collab");
            zones.Oshi = root.Find("Oshi");
            zones.Hand = root.Find("Hand");
            zones.Deck = root.Find("Deck");
            zones.CheerDeck = root.Find("CheerDeck");
            zones.Archive = root.Find("Archive");
            zones.HoloPower = root.Find("HoloPower");
            zones.Life = root.Find("Life");

            zones.Back = new Transform[5];

            for (int i = 0; i < 5; i++)
                zones.Back[i] = root.Find("Back" + (i + 1));
        }


        /// <summary>
        /// Коврик — обычный Quad под картами.
        /// Лежит чуть ниже нуля, чтобы карты
        /// не мерцали сквозь него.
        /// </summary>
        private void MakeMat()
        {
            var existing =
                transform.Find("Mat");

            GameObject mat;

            if (existing != null)
            {
                mat = existing.gameObject;
            }
            else
            {
                mat =
                    GameObject.CreatePrimitive(
                        PrimitiveType.Quad
                    );

                mat.name = "Mat";

                mat.transform.SetParent(
                    transform,
                    false
                );


                // Коврик не должен перехватывать
                // клики по картам.
                var collider =
                    mat.GetComponent<Collider>();

                if (collider != null)
                    DestroyImmediate(collider);
            }


            mat.transform.localPosition =
                new Vector3(0f, -0.01f, 0f);

            mat.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);

            mat.transform.localScale =
                new Vector3(
                    MatSize.x,
                    MatSize.y,
                    1f
                );


            var renderer =
                mat.GetComponent<MeshRenderer>();


            // Если материал задан в инспекторе,
            // берём его и больше ничего не трогаем.
            // Это единственный вариант, который
            // переживает сохранение сцены.
            if (MatMaterial != null)
            {
                renderer.sharedMaterial =
                    MatMaterial;

                return;
            }


            // Запасной вариант: цвет через блок
            // свойств. В сцене он не сохраняется,
            // зато не требует заводить материал
            // ради проверки раскладки.
            var texture =
                Resources.Load<Texture2D>(
                    "Table/playmat"
                );

            var block =
                new MaterialPropertyBlock();

            renderer.GetPropertyBlock(block);

            if (texture != null)
            {
                block.SetTexture("_BaseMap", texture);
                block.SetTexture("_MainTex", texture);
                block.SetColor("_BaseColor", Color.white);
            }
            else
            {
                block.SetTexture(
                    "_BaseMap",
                    Texture2D.whiteTexture
                );

                block.SetColor("_BaseColor", MatColor);
            }

            renderer.SetPropertyBlock(block);
        }


        private void PlaceCamera()
        {
            if (TableCamera == null)
                return;


            TableCamera.transform.position =
                new Vector3(
                    0f,
                    CameraHeight,
                    -CameraDistance
                );

            TableCamera.transform.rotation =
                Quaternion.Euler(
                    CameraPitch,
                    0f,
                    0f
                );

            TableCamera.fieldOfView =
                CameraFov;
        }
    }
}