using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using HololiveTCG.HololiveTCG.Engine;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Оживление атаки: снаряд летит от атакующего
    /// к цели, на попадании вспышка и всплывающее
    /// число урона.
    ///
    /// Питается потоком событий движка, поэтому
    /// ничего не знает о правилах: прилетело
    /// ArtUsed — показали полёт, прилетело
    /// DamageDealt — показали число.
    ///
    /// События складываются в очередь и играются
    /// по порядку, иначе при нескольких ударах
    /// за одно действие всё наложилось бы.
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    [RequireComponent(typeof(BoardView))]
    public class AttackFx : MonoBehaviour
    {
        [Header("Снаряд")]
        public float FlightTime = 0.35f;

        [Tooltip("Высота дуги")]
        public float ArcHeight = 0.7f;

        public float ProjectileSize = 0.4f;

        public Color ProjectileColor =
            new Color(1f, 0.85f, 0.4f);

        [Header("Попадание")]
        public float ImpactTime = 0.35f;

        public float ImpactSize = 1.8f;

        [Header("Число урона")]
        public float NumberTime = 0.9f;

        public float NumberRise = 0.9f;


        private GameSession _session;
        private BoardView _board;

        private readonly Queue<GameEvent> _queue =
            new Queue<GameEvent>();

        private bool _playing;


        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _board = GetComponent<BoardView>();

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
                if (gameEvent.Type ==
                        GameEventType.ArtUsed ||
                    gameEvent.Type ==
                        GameEventType.DamageDealt)
                {
                    _queue.Enqueue(gameEvent);
                }
            }


            if (!_playing && _queue.Count > 0)
                StartCoroutine(PlayQueue());
        }


        private IEnumerator PlayQueue()
        {
            _playing = true;


            while (_queue.Count > 0)
            {
                var gameEvent =
                    _queue.Dequeue();

                if (gameEvent.Type ==
                    GameEventType.ArtUsed)
                {
                    yield return Fly(gameEvent);
                }
                else
                {
                    yield return Hit(gameEvent);
                }
            }


            _playing = false;
        }


        // =========================================
        // ПОЛЁТ
        // =========================================

        private IEnumerator Fly(
            GameEvent gameEvent)
        {
            var from =
                _board.Find(gameEvent.CardId);

            var to =
                _board.Find(gameEvent.TargetCardId);

            if (from == null || to == null)
                yield break;


            Vector3 start =
                from.transform.position;

            Vector3 end =
                to.transform.position;


            var projectile =
                MakeQuad(
                    "Fx/projectile",
                    ProjectileSize,
                    ProjectileColor,
                    FxTextures.Glow()
                );

            var trail =
                projectile.AddComponent<TrailRenderer>();

            trail.time = 0.18f;
            trail.startWidth = ProjectileSize * 0.7f;
            trail.endWidth = 0f;
            var trailMaterial =
                new Material(
                    FlatMaterials.Transparent()
                );

            trailMaterial.mainTexture =
                FxTextures.Glow();

            trail.material = trailMaterial;
            trail.startColor = ProjectileColor;

            trail.endColor =
                new Color(
                    ProjectileColor.r,
                    ProjectileColor.g,
                    ProjectileColor.b,
                    0f
                );


            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime /
                     Mathf.Max(FlightTime, 0.01f);

                Vector3 point =
                    Vector3.Lerp(start, end, t);

                // Дуга: подъём к середине пути.
                point += Vector3.up *
                    (Mathf.Sin(t * Mathf.PI) * ArcHeight);

                projectile.transform.position = point;

                // Снаряд всегда лицом к камере.
                if (Camera.main != null)
                {
                    projectile.transform.rotation =
                        Camera.main.transform.rotation;
                }

                yield return null;
            }


            Destroy(projectile, trail.time);
        }


        // =========================================
        // ПОПАДАНИЕ
        // =========================================

        private IEnumerator Hit(
            GameEvent gameEvent)
        {
            var target =
                _board.Find(gameEvent.CardId);

            if (target == null)
                yield break;


            Vector3 point =
                target.transform.position +
                Vector3.up * 0.05f;


            var flash =
                MakeQuad(
                    "Fx/impact",
                    ProjectileSize,
                    new Color(1f, 0.95f, 0.7f),
                    FxTextures.Ring()
                );

            flash.transform.position = point;


            var number =
                MakeNumber(
                    gameEvent.Amount,
                    point
                );


            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime /
                     Mathf.Max(NumberTime, 0.01f);


                // Вспышка быстро раздувается
                // и гаснет.
                float flashT =
                    Mathf.Clamp01(
                        t * NumberTime /
                        Mathf.Max(ImpactTime, 0.01f)
                    );

                if (flash != null)
                {
                    flash.transform.localScale =
                        Vector3.one *
                        Mathf.Lerp(
                            ProjectileSize,
                            ImpactSize,
                            flashT
                        );

                    SetAlpha(flash, 1f - flashT);

                    if (Camera.main != null)
                    {
                        flash.transform.rotation =
                            Camera.main.transform.rotation;
                    }

                    if (flashT >= 1f)
                    {
                        Destroy(flash);
                        flash = null;
                    }
                }


                // Число поднимается и тает.
                if (number != null)
                {
                    number.transform.position =
                        point + Vector3.up * (NumberRise * t);

                    if (Camera.main != null)
                    {
                        number.transform.rotation =
                            Camera.main.transform.rotation;
                    }

                    var mesh =
                        number.GetComponent<TextMesh>();

                    var color = mesh.color;
                    color.a = 1f - t;
                    mesh.color = color;
                }

                yield return null;
            }


            if (flash != null)
                Destroy(flash);

            if (number != null)
                Destroy(number);
        }


        // =========================================
        // ПРИМИТИВЫ
        // =========================================

        private GameObject MakeQuad(
            string artKey,
            float size,
            Color fallback,
            Texture2D generated)
        {
            var quad =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad
                );

            quad.name = artKey;

            var collider =
                quad.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);


            quad.transform.SetParent(transform, false);

            quad.transform.localScale =
                Vector3.one * size;


            var renderer =
                quad.GetComponent<MeshRenderer>();

            renderer.sharedMaterial =
                FlatMaterials.Transparent();


            var texture =
                Resources.Load<Texture2D>(artKey);

            var block =
                new MaterialPropertyBlock();

            renderer.GetPropertyBlock(block);

            // Своя картинка, если положили;
            // иначе нарисованная кодом.
            if (texture == null)
                texture = generated;


            block.SetTexture("_BaseMap", texture);
            block.SetTexture("_MainTex", texture);

            block.SetColor(
                "_BaseColor",
                texture == generated
                    ? fallback
                    : Color.white
            );

            renderer.SetPropertyBlock(block);

            return quad;
        }


        private static void SetAlpha(
            GameObject quad,
            float alpha)
        {
            var renderer =
                quad.GetComponent<MeshRenderer>();

            var block =
                new MaterialPropertyBlock();

            renderer.GetPropertyBlock(block);

            Color color =
                block.GetColor("_BaseColor");

            if (color == default(Color))
                color = Color.white;

            color.a = alpha;

            block.SetColor("_BaseColor", color);

            renderer.SetPropertyBlock(block);
        }


        private GameObject MakeNumber(
            int amount,
            Vector3 position)
        {
            var holder =
                new GameObject("DamageNumber");

            holder.transform.SetParent(transform, false);
            holder.transform.position = position;
            holder.transform.localScale =
                Vector3.one * 0.15f;


            var mesh =
                holder.AddComponent<TextMesh>();

            mesh.text = "-" + amount;
            mesh.fontSize = 64;
            mesh.color = new Color(1f, 0.45f, 0.35f);
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;

            return holder;
        }
    }
}