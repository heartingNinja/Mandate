// BoardView — the board in the centre of the screen: world-space sprites, a fitted camera, and labels.
//
// Build() lays the hub at the origin and the other nodes on a ring, draws one LineRenderer per adjacency and two
// sprites per node (an outline ring behind a coloured fill). Refresh() recolours after every step: fill = owner,
// ring = white when the node changed hands in the last resolution. FitCamera() keeps the whole board inside the
// free region between the IMGUI panels at any window size. DrawLabels() is the IMGUI pass that writes the node
// id, production and fortification pips over each sprite.
//
// Art comes from the view's inspector slots (nodeSprite, ringSprite, spriteMaterial, lineMaterial, boardRadius,
// backgroundColor). Empty slots get generated placeholders, flagged HideAndDontSave so Unity's unused-asset
// sweep does not destroy them.

using System;
using System.Collections.Generic;
using UnityEngine;
using Mandate.Core;

namespace Mandate.Viewer
{
    public sealed class BoardView
    {
        readonly MandateGameView view;   // art settings and the parent transform come from here

        Camera cam;
        Transform root;                  // parent of every board object; rebuilt each game
        Sprite fallbackCircle;
        Material fallbackLine, fallbackSprite;
        readonly Dictionary<int, SpriteRenderer> fills = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> rings = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, Vector3> nodePos = new Dictionary<int, Vector3>();

        public BoardView(MandateGameView view)
        {
            this.view = view;
            SetupCamera();
        }

        // ------------------------------------------------------------------ camera
        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = view.backgroundColor;
            cam.transform.rotation = Quaternion.identity;
            cam.transform.position = new Vector3(0f, 0f, -10f);
        }

        /// <summary>Keeps the whole board inside the free region between the panels, whatever the window size.
        /// bottomH is the height of whatever is drawn along the bottom (your controls, the scoreboard, or nothing).</summary>
        public void FitCamera(float bottomH)
        {
            if (cam == null) return;
            float s = Ui.Scale;                                       // panel sizes are virtual points -> real pixels
            float w = Screen.width, h = Screen.height;
            float freeW = Mathf.Max(200f, w - (Ui.LeftW + Ui.RightW) * s);
            float freeH = Mathf.Max(200f, h - (Ui.TopH + bottomH) * s);
            float need = view.boardRadius * 2f + 2.4f;               // diameter plus node size and margin, world units
            float unitsPerPixel = Mathf.Max(need / freeW, need / freeH);
            cam.orthographicSize = unitsPerPixel * h / 2f;
            float cx = Ui.LeftW * s + freeW / 2f, cy = Ui.TopH * s + freeH / 2f;   // free-region centre in real pixels (y down)
            cam.transform.position = new Vector3((w / 2f - cx) * unitsPerPixel, (cy - h / 2f) * unitsPerPixel, -10f);
            cam.backgroundColor = view.backgroundColor;
        }

        // ------------------------------------------------------------------ sprites
        /// <summary>Destroys the old board and builds sprites and edges for these nodes.</summary>
        public void Build(List<Node> nodes)
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = new GameObject("Board").transform;
            root.SetParent(view.transform, false);
            fills.Clear(); rings.Clear(); nodePos.Clear();

            // Unity's "== null" also catches assets the engine has destroyed (an unused-asset sweep will take
            // runtime-created ones), which the C# ??= operator would not, so placeholders are rebuilt whenever missing.
            if (view.nodeSprite == null && fallbackCircle == null) fallbackCircle = MakeCircleSprite(128);
            if (view.spriteMaterial == null && fallbackSprite == null) fallbackSprite = MakeSpriteMaterial();
            if (view.lineMaterial == null && fallbackLine == null) fallbackLine = MakeLineMaterial();
            var fill = view.nodeSprite != null ? view.nodeSprite : fallbackCircle;
            var ring = view.ringSprite != null ? view.ringSprite : fill;
            var spriteMat = view.spriteMaterial != null ? view.spriteMaterial : fallbackSprite;
            var lineMat = view.lineMaterial != null ? view.lineMaterial : fallbackLine;

            int ringCount = nodes.Count - 1;   // every node except the hub sits on the ring

            // Hub in the centre, everything else evenly around the ring going clockwise. The last ring node
            // (the neutral between the last seat and the first) sits at the top, with node 1 just clockwise of it.
            nodePos[0] = Vector3.zero;
            for (int i = 1; i <= ringCount; i++)
            {
                float ang = Mathf.PI / 2f - 2f * Mathf.PI * i / ringCount;
                nodePos[i] = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * view.boardRadius;
            }

            // Edges: one line per adjacency pair.
            var drawn = new HashSet<(int, int)>();
            foreach (var nd in nodes)
                foreach (var adj in nd.Adjacent)
                {
                    var key = (Math.Min(nd.Id, adj), Math.Max(nd.Id, adj));
                    if (!drawn.Add(key)) continue;
                    var go = new GameObject($"edge {key.Item1}-{key.Item2}");
                    go.transform.SetParent(root, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.material = lineMat;
                    lr.useWorldSpace = true;
                    lr.positionCount = 2;
                    lr.SetPosition(0, nodePos[nd.Id]);
                    lr.SetPosition(1, nodePos[adj]);
                    lr.startWidth = lr.endWidth = 0.05f;
                    lr.startColor = lr.endColor = new Color(0.55f, 0.55f, 0.62f, 0.9f);
                    lr.sortingOrder = 0;
                }

            // Nodes: outline ring behind, coloured fill in front. Hub drawn a little larger.
            foreach (var nd in nodes)
            {
                float size = NodeSize(nd.Id);
                rings[nd.Id] = NewSprite($"ring {nd.Id}", ring, spriteMat, nodePos[nd.Id], size * 1.18f, 1);
                fills[nd.Id] = NewSprite($"node {nd.Id}", fill, spriteMat, nodePos[nd.Id], size, 2);
            }
        }

        static float NodeSize(int id) => id == 0 ? 1.35f : 1.05f;   // world units across; the hub is bigger

        SpriteRenderer NewSprite(string name, Sprite sprite, Material mat, Vector3 pos, float scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Recolours every node: fill = owner, ring = white if the node changed hands in the last resolution.</summary>
        public void Refresh(List<Node> nodes, HashSet<int> changed, SeatColors colors)
        {
            if (root == null) return;
            foreach (var nd in nodes)
            {
                fills[nd.Id].color = colors.Of(nd.Owner);
                rings[nd.Id].color = changed.Contains(nd.Id) ? Color.white : new Color(0.12f, 0.12f, 0.15f);
            }
        }

        // ------------------------------------------------------------------ labels (IMGUI, drawn over the sprites)
        /// <summary>Node id, production and fortification pips over each node, and "you" above the nodes of 'you'
        /// (your seat, or null when nobody is playing). Call from OnGUI, in virtual points.</summary>
        public void DrawLabels(List<Node> nodes, string you)
        {
            if (cam == null || root == null) return;
            float pxPerUnit = Ui.CanvasH / (2f * cam.orthographicSize);        // virtual points per world unit
            foreach (var nd in nodes)
            {
                var sp = cam.WorldToScreenPoint(nodePos[nd.Id]);               // real pixels, y up
                float x = sp.x / Ui.Scale, y = (Screen.height - sp.y) / Ui.Scale;   // virtual points, y down
                float diameter = NodeSize(nd.Id) * pxPerUnit;

                ShadowLabel(new Rect(x - 40, y - 17, 80, 20), nd.Id.ToString(), Ui.NodeId);
                ShadowLabel(new Rect(x - 40, y + 1, 80, 16), "+" + nd.Production, Ui.NodeSmall);
                if (you != null && nd.Owner == you)
                    ShadowLabel(new Rect(x - 40, y - diameter / 2f - 16f, 80, 16), "<b>you</b>", Ui.NodeSmall);   // just above the circle

                // Fortification pips just inside the bottom of the circle.
                for (int i = 0; i < nd.Fortify; i++)
                {
                    var r = new Rect(x - 4 - (nd.Fortify - 1) * 5.5f + i * 11f, y + diameter / 2f - 14f, 8, 8);
                    GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, 10, 10), Texture2D.whiteTexture);
                    GUI.color = Color.white; GUI.DrawTexture(r, Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }
        }

        // A label with a dark copy one point down and right, so it reads on any node colour.
        static void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        // ------------------------------------------------------------------ generated placeholders
        static Sprite MakeCircleSprite(int size)
        {
            // HideAndDontSave keeps the generated texture and sprite out of the scene file and out of Unity's
            // unused-asset sweep, which would otherwise destroy them the first time it runs.
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, name = "MandateCircle", hideFlags = HideFlags.HideAndDontSave };
            float r = size / 2f - 1f, c = size / 2f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f));   // 1px anti-aliased edge
                }
            tex.SetPixels(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);   // 1 world unit across
            sprite.name = "MandateCircle";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // URP's unlit sprite shader renders under both the 2D and the 3D (Universal) renderer; the built-in
        // Sprites/Default is the fallback for non-URP projects. Both honour vertex colour and the sprite tint.
        static Material MakeSpriteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Sprites/Default");
            return new Material(shader) { name = "MandateSprite", hideFlags = HideFlags.HideAndDontSave };
        }

        static Material MakeLineMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            return new Material(shader) { name = "MandateLine", hideFlags = HideFlags.HideAndDontSave };
        }
    }
}
