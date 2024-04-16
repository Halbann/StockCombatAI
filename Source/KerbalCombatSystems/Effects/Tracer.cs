using UnityEngine;

namespace KerbalCombatSystems.Effects
{
    public class Tracer : MonoBehaviour
    {
        // Setup.

        public float exposureTime = 0.04f;
        public float offset = 0;
        public float widthVariation = 0.2f;

        public Rigidbody rb;

        public Color Colour
        {
            get => _colour;
            set
            {
                _colour = value;
                line.startColor = _colour;
                line.endColor = _colour;
            }
        }

        public float Width
        {
            get => _width;
            set
            {
                _width = value;
                line.startWidth = _width + (widthVariation * (Random.value * 2 - 1));
                line.endWidth = _width + (widthVariation * (Random.value * 2 - 1));
            }
        }

        public Material Material
        {
            get => trailMaterial;
            set
            {
                trailMaterial = value;
                line.material = trailMaterial;
            }
        }

        // Variables.

        public Vector3 startPosition;
        public Vector3 endPosition = Vector3.one * 100;

        private LineRenderer line;
        private Vector3 startPosWorld;
        private bool destroy = false;

        private Color _colour = Color.white;
        private float _width = 0.6f;
        private Material trailMaterial;

        internal void Awake()
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Width = _width;
            Colour = _colour;

            startPosWorld = transform.position;
        }

        internal void Start()
        {
            // Get rigidbody
            rb = rb ?? gameObject.GetComponent<Rigidbody>();

            if (rb == null)
            {
                Debug.LogError("Tracer couldn't find a rigidbody. Tracer must only be added to objects with a rigidbody.");
                destroy = true;
                Destroy(line);
                Destroy(this);

                return;
            }
        }

        internal void Update()
        {
            if (destroy)
                return;

            Vector3 origin = transform.position + rb.velocity.normalized * offset;

            float distanceTravelled = Vector3.Distance(origin, startPosWorld);
            Vector3 tail = rb.velocity * exposureTime;
            tail = tail.normalized * Mathf.Min(tail.magnitude, distanceTravelled);

            line.SetPosition(0, origin);
            line.SetPosition(1, origin - tail);

            startPosition = origin;
            endPosition = origin - tail;

            Width = _width;
        }
    }
}
