using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Кубик, который катится по столу.
    ///
    /// Важно: что выпало, решает движок. Физика
    /// только показывает. Кубик честно кувыркается,
    /// а когда останавливается — доворачивается
    /// нужной гранью вверх.
    ///
    /// Если бы результат определялся физикой,
    /// он расходился бы с логом и ломал скилл оси,
    /// который грань объявляет заранее.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public class DiceRoller : MonoBehaviour
    {
        [Header("Где бросаем")]
        public Vector3 DropPoint = new Vector3(2.6f, 1.6f, 0f);

        [Tooltip("Разброс точки падения")]
        public float DropSpread = 0.35f;

        [Header("Кубик")]
        public float DieSize = 0.32f;

        [Tooltip("Сила подкрутки")]
        public float TorqueForce = 0.6f;

        [Tooltip("Начальная скорость вбок, м/с")]
        public float PushForce = 1.2f;

        [Tooltip("Масса кубика, кг")]
        public float DieMass = 1f;

        [Header("Тайминги")]
        [Tooltip("Сколько ждём остановки, секунд")]
        public float MaxRollTime = 2.5f;

        [Tooltip("Длительность доворота нужной гранью, секунд")]
        public float SettleTime = 0.3f;

        [Tooltip("Через сколько секунд кубик убирается со стола. " +
                 "0 — остаётся лежать до следующего броска")]
        public float HideAfter = 3f;


        private GameSession _session;
        private GameObject _die;
        private Rigidbody _body;
        private Coroutine _running;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _session.EventsProduced += OnEvents;
        }


        private void OnDestroy()
        {
            if (_session != null)
                _session.EventsProduced -= OnEvents;
        }


        private void OnEvents(
            List<GameEvent> events)
        {
            foreach (var gameEvent in events)
            {
                if (gameEvent.Type !=
                    GameEventType.DieRolled)
                {
                    continue;
                }


                Roll(gameEvent.Amount);

                // За одно действие кубик бросается
                // максимум один раз.
                return;
            }
        }


        public void Roll(
            int result)
        {
            if (result < 1 || result > 6)
                return;


            EnsureFloor();
            EnsureDie();

            if (_running != null)
                StopCoroutine(_running);

            _running =
                StartCoroutine(
                    RollRoutine(result)
                );
        }


        private IEnumerator RollRoutine(
            int result)
        {
            _die.SetActive(true);

            _body.isKinematic = false;


            _die.transform.position =
                DropPoint +
                new Vector3(
                    Random.Range(-DropSpread, DropSpread),
                    0f,
                    Random.Range(-DropSpread, DropSpread)
                );

            _die.transform.rotation =
                Random.rotation;


            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;

            // VelocityChange вместо Impulse:
            // импульс делится на массу, и при лёгком
            // кубике скорость улетала в десятки м/с.
            _body.AddTorque(
                Random.onUnitSphere * TorqueForce,
                ForceMode.VelocityChange
            );

            _body.AddForce(
                new Vector3(
                    Random.Range(-PushForce, PushForce),
                    0f,
                    Random.Range(-PushForce, PushForce)
                ),
                ForceMode.VelocityChange
            );


            // Ждём, пока успокоится.
            float elapsed = 0f;

            while (elapsed < MaxRollTime)
            {
                elapsed += Time.deltaTime;

                if (elapsed > 0.4f &&
                    _body.linearVelocity.magnitude < 0.05f &&
                    _body.angularVelocity.magnitude < 0.2f)
                {
                    break;
                }


                // Если всё же улетел — возвращаем,
                // иначе кубик исчезнет из кадра.
                Vector3 position =
                    _die.transform.position;

                if (position.y < -1f ||
                    Mathf.Abs(position.x) > 8f ||
                    Mathf.Abs(position.z) > 8f)
                {
                    _die.transform.position = DropPoint;

                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                }

                yield return null;
            }


            _body.isKinematic = true;


            // Доворачиваем нужной гранью вверх,
            // сохранив случайный разворот вокруг
            // вертикали — чтобы не выглядело
            // подстроенным.
            Quaternion target =
                Quaternion.AngleAxis(
                    Random.Range(0f, 360f),
                    Vector3.up
                ) *
                Quaternion.FromToRotation(
                    FaceNormal(result),
                    Vector3.up
                );


            Quaternion from =
                _die.transform.rotation;

            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(SettleTime, 0.01f);

                _die.transform.rotation =
                    Quaternion.Slerp(from, target, t);

                yield return null;
            }


            _die.transform.rotation = target;


            // Даём разглядеть результат
            // и убираем со стола.
            if (HideAfter > 0f)
            {
                yield return new WaitForSeconds(HideAfter);

                _die.SetActive(false);
            }


            _running = null;
        }


        /// <summary>
        /// Куда смотрит грань с этим числом
        /// в системе координат кубика.
        /// Противоположные грани дают в сумме семь.
        /// </summary>
        private static Vector3 FaceNormal(
            int value)
        {
            switch (value)
            {
                case 1: return Vector3.up;
                case 2: return Vector3.forward;
                case 3: return Vector3.right;
                case 4: return Vector3.left;
                case 5: return Vector3.back;
                default: return Vector3.down;
            }
        }


        // =========================================
        // СБОРКА
        // =========================================

        private void EnsureDie()
        {
            if (_die != null)
                return;


            _die = new GameObject("Die");
            _die.transform.SetParent(transform, false);


            var collider =
                _die.AddComponent<BoxCollider>();

            collider.size =
                Vector3.one * DieSize;


            _body =
                _die.AddComponent<Rigidbody>();

            _body.mass = DieMass;
            _body.angularDamping = 1.6f;
            _body.linearDamping = 0.4f;


            for (int value = 1; value <= 6; value++)
                BuildFace(value);
        }


        private void BuildFace(
            int value)
        {
            var face =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad
                );

            face.name = "Face" + value;

            DestroyImmediate(
                face.GetComponent<Collider>()
            );

            face.transform.SetParent(
                _die.transform,
                false
            );


            Vector3 normal =
                FaceNormal(value);

            // Quad смотрит в +Z, разворачиваем
            // его наружу по нормали грани.
            face.transform.localRotation =
                Quaternion.LookRotation(
                    normal,
                    normal == Vector3.up ||
                    normal == Vector3.down
                        ? Vector3.forward
                        : Vector3.up
                );

            face.transform.localPosition =
                normal * (DieSize * 0.5f + 0.001f);

            face.transform.localScale =
                Vector3.one * DieSize;


            var renderer =
                face.GetComponent<MeshRenderer>();

            renderer.sharedMaterial =
                FlatMaterials.Transparent();

            var texture =
                Resources.Load<Texture2D>(
                    "Dice/face-" + value
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

                block.SetColor(
                    "_BaseColor",
                    new Color(0.95f, 0.95f, 0.92f)
                );
            }

            renderer.SetPropertyBlock(block);


            if (texture != null)
                return;


            // Без картинок пишем числа сами,
            // иначе кубик будет пустым.
            var labelObject =
                new GameObject("Pips");

            labelObject.transform.SetParent(
                face.transform,
                false
            );

            labelObject.transform.localPosition =
                new Vector3(0f, 0f, -0.02f);

            labelObject.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);

            labelObject.transform.localScale =
                Vector3.one * 0.1f;

            var label =
                labelObject.AddComponent<TextMesh>();

            label.text = value.ToString();
            label.fontSize = 72;
            label.color = Color.black;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
        }


        /// <summary>
        /// Кубику нужно обо что-то удариться.
        /// Коврик коллайдера не имеет намеренно —
        /// он не должен перехватывать клики,
        /// поэтому пол делаем отдельно.
        /// </summary>
        private void EnsureFloor()
        {
            if (transform.Find("DiceFloor") != null)
                return;


            var floor =
                new GameObject("DiceFloor");

            floor.transform.SetParent(
                transform,
                false
            );

            floor.transform.position =
                new Vector3(0f, -0.05f, 0f);


            var collider =
                floor.AddComponent<BoxCollider>();

            collider.size =
                new Vector3(20f, 0.1f, 20f);
        }
    }
}