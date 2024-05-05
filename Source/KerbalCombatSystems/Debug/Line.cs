using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace KerbalCombatSystems.Debug2
{
    public class Line : MonoBehaviour
    {
        #region Fields

        private static readonly HashSet<LineInfo> lineCalls = new HashSet<LineInfo>();

        private readonly HashSet<PooledLine> linePool = new HashSet<PooledLine>();
        private readonly HashSet<PooledLine> activeLines = new HashSet<PooledLine>();
        private readonly List<Material> materials = new List<Material>();
        private float lastFixedUpdate;

        public int PoolCount => linePool.Count;
        public int ActiveCount => activeLines.Count;
        public static int CallCount => lineCalls.Count;

        class PooledLine
        {
            public LineRenderer renderer;
            public Color colour;

            public bool Enabled
            {
                set => renderer.enabled = value;
            }
        }

        public struct LineInfo
        {
            public Vector3 start;
            public Vector3 end;
            public Vector3[] points;
            public Color colour;
            public Vector3 origin;
            public float alpha;
            public float width;
            public float fixedTime;
        }

        #endregion

        #region Main

        internal void Start()
        {
            StartCoroutine(EndOfFrame());
        }

        private IEnumerator EndOfFrame()
        {
            var eof = new WaitForEndOfFrame();

            while (true)
            {
                yield return eof;

                // Move all the used lines back to the pool at the end of each frame.
                foreach (var line in activeLines)
                {
                    linePool.Add(line);
                    line.Enabled = false;
                }

                activeLines.Clear();
            }
        }

        internal void FixedUpdate()
        {
            // todo: why do lines flicker every few seconds?
            // FO shift, krakensbane?

            lineCalls.Clear(); // If we only clear here then update calls can build up? :L
            
            // Because fixed updates can occur multiple times per rendered frame,
            // store the time of the last fixed update, so we can discard the older calls.
            lastFixedUpdate = Time.fixedTime;
        }

        internal void LateUpdate()
        {
            // Realise all lines that were called in the last fixed update.
            // Just before rendering.

            foreach (var line in lineCalls)
            {
                if (line.fixedTime >= lastFixedUpdate)
                    PrepareLine(line);
            }
        }

        internal void OnDestroy()
        {
            linePool.Clear();
            activeLines.Clear();
            lineCalls.Clear();
            materials.ForEach(m =>
            {
                Destroy(m.mainTexture);
                Destroy(m);
            });
            materials.Clear();
        }

        #endregion

        #region Functions

        private void PrepareLine(LineInfo info)
        {
            info.colour.a = info.alpha;
            PooledLine line = linePool.FirstOrDefault(l => l.colour == info.colour);

            if (line == null)
            {
                // Nothing in the line pool, create a new one.
                var renderer = CreateLine(info.colour, info.alpha, info.width);
                line = new PooledLine { renderer = renderer, colour = info.colour };
            }
            else
            {
                // Move an existing line from the pool.
                linePool.Remove(line);
            }

            line.Enabled = true;
            activeLines.Add(line);
            UpdateLine(line, info);
        }

        private void UpdateLine(PooledLine line, LineInfo info)
        {
            LineRenderer r = line.renderer;
            r.widthMultiplier = info.width;

            if (info.points != null)
            {
                r.useWorldSpace = false;
                r.transform.position = info.origin;

                r.positionCount = info.points.Length;
                r.SetPositions(info.points);
            }
            else
            {
                r.useWorldSpace = true;
                r.positionCount = 2;
                r.SetPosition(0, info.origin + info.start);
                r.SetPosition(1, info.origin + info.end);
            }
        }

        private LineRenderer CreateLine(Color colour, float alpha, float width)
        {
            Material mat;
            if (alpha == 1f)
            {
                mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = colour;
            }
            else
            {
                colour.a = alpha;
                mat = new Material(Shader.Find("Unlit/Transparent"));
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, colour);
                tex.Apply();
                mat.SetTexture("_MainTex", tex);
            }
            materials.Add(mat);

            LineRenderer line = new GameObject().AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = mat;
            line.startWidth = width;
            line.endWidth = width;

            return line;
        }

        #endregion

        #region User Functions

        public static void Draw(Vector3 start, Vector3 end, Color colour, Vector3 origin = default, float alpha = 1f, float width = 0.1f)
        {
            if (!Debug.Visible)
                return;

            // Define a start, and end position, optionally offset by an origin.
            lineCalls.Add(new LineInfo
            {
                start = start,
                end = end,
                colour = colour,
                origin = origin,
                alpha = alpha,
                width = width,
                fixedTime = Time.fixedTime
            });
        }

        public static void Draw(Vector3 origin, Vector3 end, Color colour, float alpha = 1f, float width = 0.1f)
        {
            if (!Debug.Visible)
                return;

            // Define an combined origin and start position, and an end position offset by the origin.
            lineCalls.Add(new LineInfo
            {
                start = Vector3.zero,
                end = end,
                colour = colour,
                origin = origin,
                alpha = alpha,
                width = width,
                fixedTime = Time.fixedTime
            });
        }

        public static void Draw(Vector3 origin, Vector3 direction, float length, Color colour, float alpha = 1f, float width = 0.1f)
        {
            if (!Debug.Visible)
                return;

            // Define a combined origin and start position, and a direction and length.
            lineCalls.Add(new LineInfo
            {
                start = Vector3.zero,
                end = direction.normalized * length,
                colour = colour,
                origin = origin,
                alpha = alpha,
                width = width,
                fixedTime = Time.fixedTime
            });
        }

        public static void Draw(Vector3[] points, Color colour, Vector3 origin = default, float alpha = 1f, float width = 0.1f)
        {
            if (!Debug.Visible)
                return;

            // Define a series of points, usually for drawing a curve, optionally offset by an origin.
            lineCalls.Add(new LineInfo
            {
                colour = colour,
                origin = origin,
                alpha = alpha,
                width = width,
                fixedTime = Time.fixedTime,
                points = points
            });
        }

        #endregion
    }
}
