using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VeyraCity.UI
{
    /// <summary>
    /// Code-built uGUI with one consistent style: dark glass panels, neon cyan accent, amber for nitro.
    /// Everything in the game UI is created through these helpers so the look stays coherent.
    /// </summary>
    public static class UIKit
    {
        public static readonly Color Panel = new Color(0.03f, 0.04f, 0.07f, 0.78f);
        public static readonly Color PanelLight = new Color(0.08f, 0.10f, 0.15f, 0.85f);
        public static readonly Color Accent = new Color(0.20f, 0.85f, 1.00f);
        public static readonly Color Accent2 = new Color(1.00f, 0.25f, 0.62f);
        public static readonly Color Amber = new Color(1.00f, 0.66f, 0.12f);
        public static readonly Color Good = new Color(0.35f, 1.00f, 0.55f);
        public static readonly Color Bad = new Color(1.00f, 0.32f, 0.28f);
        public static readonly Color TextMain = new Color(0.94f, 0.96f, 1.00f);
        public static readonly Color TextDim = new Color(0.62f, 0.68f, 0.78f);

        static Font font;
        static Sprite circle, ring, softRect;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Sprite Circle => circle != null ? circle : (circle = MakeCircle(128, 0f));
        public static Sprite Ring => ring != null ? ring : (ring = MakeCircle(256, 0.86f));
        public static Sprite SoftRect => softRect != null ? softRect : (softRect = MakeSoftRect());

        static Sprite MakeCircle(int size, float inner)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01((1f - d) * r * 0.5f);
                    if (inner > 0f) a *= Mathf.Clamp01((d - inner) * r * 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite MakeSoftRect()
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Min(x, s - 1 - x), dy = Mathf.Min(y, s - 1 - y);
                    float a = Mathf.Clamp01(Mathf.Min(dx, dy) / 3f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
        }

        public static Canvas CreateCanvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>Anchored box: pivot/anchor at <paramref name="anchor"/>, positioned by pixels from it.</summary>
        public static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, anchor, anchor);
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(RectTransform rt, Color color, Sprite sprite = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite != null ? sprite : SoftRect;
            img.type = sprite == null ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(Transform parent, string name, string value, int size, TextAnchor align, Color color, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = value;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        public static Image Bar(Transform parent, string name, Color back, Color fill, out Image fillImage, Image.FillMethod method = UnityEngine.UI.Image.FillMethod.Horizontal)
        {
            var bg = Rect(parent, name, Vector2.zero, Vector2.one);
            var bgImg = Image(bg, back);
            var f = Rect(bg, "Fill", Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
            fillImage = f.gameObject.AddComponent<Image>();
            fillImage.sprite = Texture2DSprite();
            fillImage.color = fill;
            fillImage.type = UnityEngine.UI.Image.Type.Filled;
            fillImage.fillMethod = method;
            fillImage.fillOrigin = 0;
            fillImage.raycastTarget = false;
            return bgImg;
        }

        static Sprite white;
        public static Sprite Texture2DSprite()
        {
            if (white != null) return white;
            var tex = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return white;
        }

        public static Button Button(Transform parent, string name, string label, UnityAction onClick, int fontSize = 30)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one);
            var img = Image(rt, PanelLight);
            img.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 0.75f);
            cb.highlightedColor = new Color(0.55f, 0.95f, 1f, 1f);
            cb.selectedColor = new Color(0.40f, 0.90f, 1f, 1f);
            cb.pressedColor = new Color(1f, 0.4f, 0.75f, 1f);
            cb.disabledColor = new Color(0.4f, 0.4f, 0.45f, 0.5f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Text(rt, "Label", label, fontSize, TextAnchor.MiddleLeft, TextMain, FontStyle.Bold);
            t.rectTransform.offsetMin = new Vector2(24, 0);
            t.rectTransform.offsetMax = new Vector2(-16, 0);
            return b;
        }

        public static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static string FormatTime(float seconds)
        {
            if (seconds <= 0f || float.IsInfinity(seconds) || seconds >= float.MaxValue * 0.5f) return "--:--.---";
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m:00}:{s:00.000}";
        }

        public static string Money(int amount) => "$" + amount.ToString("N0");

        public static string Ordinal(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13) return n + "th";
            switch (n % 10) { case 1: return n + "st"; case 2: return n + "nd"; case 3: return n + "rd"; default: return n + "th"; }
        }
    }
}
