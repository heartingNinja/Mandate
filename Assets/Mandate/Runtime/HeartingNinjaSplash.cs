using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mandate
{
    /// <summary>
    /// The opening splash: the heartingNinja logo animation on black, then a fade into the Mandate scene, which
    /// loads in the background meanwhile. Any key, click or tap skips it. It lives alone in Assets/Scenes/Splash.unity,
    /// which is first in the build, and replaces Unity's own splash screen (turned off in Player Settings).
    ///
    /// The frames come from the Fantasy-Baseball project: an Aseprite sprite sheet plus Aseprite's JSON of frame
    /// rects and durations, both in Assets/Resources/Brand. Each frame is drawn at a whole-number scale so every
    /// art pixel stays the same size at any resolution. Drawn with OnGUI like the rest of Mandate's screens.
    /// </summary>
    public class HeartingNinjaSplash : MonoBehaviour
    {
        [Tooltip("Scene to open after the splash. It must be in File > Build Profiles > Scene List.")]
        public string nextScene = "Mandate";
        [Tooltip("Seconds to ignore input at the start, so a key still held from launching doesn't skip the splash.")]
        public float skipAfter = 0.25f;
        [Tooltip("Seconds of fade to black after the animation ends.")]
        public float fadeTime = 0.35f;
        [Tooltip("Seconds of fade to black when the player skips.")]
        public float skipFadeTime = 0.15f;
        [Tooltip("Roughly how much of the screen height the logo fills (it snaps to a whole-number pixel scale).")]
        [Range(0.2f, 1f)] public float heightShare = 0.62f;
        [Tooltip("Background colour behind the logo (the art's own black).")]
        public Color background = new Color32(11, 10, 12, 255);

        // Aseprite's JSON export, reduced to the fields we read
        [System.Serializable] class FrameRect { public int x, y, w, h; }
        [System.Serializable] class Frame { public FrameRect frame; public int duration; }
        [System.Serializable] class Sheet { public Frame[] frames; }

        Texture2D sheet;
        Frame[] frames;
        int current = -1;      // frame on screen, -1 = none yet
        float fade;            // 0 = clear, 1 = black
        float started;
        bool skipped;

        void Awake()
        {
            sheet = Resources.Load<Texture2D>("Brand/HeartingNinjaSplash");
            TextAsset data = Resources.Load<TextAsset>("Brand/HeartingNinjaSplash");
            if (sheet != null && data != null) frames = JsonUtility.FromJson<Sheet>(data.text).frames;
            if (sheet != null) sheet.filterMode = FilterMode.Point;
            else Debug.LogWarning("[Splash] Resources/Brand/HeartingNinjaSplash is missing; going straight to " + nextScene + ".");
        }

        IEnumerator Start()
        {
            started = Time.unscaledTime;
            AsyncOperation load = SceneManager.LoadSceneAsync(nextScene);
            load.allowSceneActivation = false;   // finish loading but wait for the splash to end

            if (frames != null)
            {
                for (int i = 0; i < frames.Length && !skipped; i++)
                {
                    current = i;
                    float until = Time.unscaledTime + frames[i].duration / 1000f;
                    while (Time.unscaledTime < until && !skipped) yield return null;
                }
            }

            float time = skipped ? skipFadeTime : fadeTime;
            for (float t = 0; t < time; t += Time.unscaledDeltaTime)
            {
                fade = t / time;
                yield return null;
            }
            fade = 1f;
            load.allowSceneActivation = true;
        }

        void OnGUI()
        {
            // any key, click or tap skips (touches arrive here as mouse events)
            Event e = Event.current;
            if (!skipped && Time.unscaledTime - started > skipAfter && (e.type == EventType.KeyDown || e.type == EventType.MouseDown))
            {
                skipped = true;
                e.Use();
            }
            if (e.type != EventType.Repaint) return;

            Rect screen = new Rect(0, 0, Screen.width, Screen.height);
            Fill(screen, background);
            if (current >= 0) DrawFrame(frames[current].frame);
            if (fade > 0f) Fill(screen, new Color(0, 0, 0, fade));
        }

        // one frame of the sheet, centred, scaled by a whole number so the pixel art stays crisp
        void DrawFrame(FrameRect f)
        {
            int scale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(Screen.height * heightShare / f.h, Screen.width * 0.85f / f.w)));
            int w = f.w * scale, h = f.h * scale;
            Rect at = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            // Aseprite measures y from the top, texture coordinates from the bottom
            float sw = sheet.width, sh = sheet.height;
            Rect uv = new Rect(f.x / sw, 1f - (f.y + f.h) / sh, f.w / sw, f.h / sh);
            GUI.DrawTextureWithTexCoords(at, sheet, uv);
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
