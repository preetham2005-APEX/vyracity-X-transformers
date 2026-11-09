using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VeyraCity.UI
{
    public sealed class MenuEntry
    {
        public string Label;
        public string Right;
        public string Info;
        public Action OnSelect;
        public Action<int> OnAdjust;
        public bool Enabled = true;
        public Color? Tint;

        public MenuEntry(string label, Action onSelect = null, string info = null, string right = null)
        {
            Label = label;
            OnSelect = onSelect;
            Info = info;
            Right = right;
        }
    }

    /// <summary>
    /// Generic full-screen list menu (main menu, events, garage, settings, results, pause) and a story
    /// dialog. Keyboard, gamepad (via the EventSystem) and mouse all work; left/right adjusts sliders.
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        Canvas canvas;
        RectTransform root, listContent, infoRoot, dialogRoot;
        Text titleText, subtitleText, infoText, footerText, dialogSpeaker, dialogBody, headerRight;
        Image dim;
        ScrollRect scroll;
        readonly List<(Button button, MenuEntry item, Text right)> rows = new List<(Button, MenuEntry, Text)>();
        Action onBack;
        Action dialogContinue;
        float inputCooldown;
        int lastSelected = -1;

        public bool IsOpen => canvas != null && root.gameObject.activeSelf;
        public bool DialogOpen => dialogRoot != null && dialogRoot.gameObject.activeSelf;
        public bool AnyOpen => IsOpen || DialogOpen;

        public void Build()
        {
            if (canvas != null) return;
            canvas = UIKit.CreateCanvas("Menus", 50, transform);
            var cr = (RectTransform)canvas.transform;
            root = UIKit.Rect(cr, "Menu", Vector2.zero, Vector2.one);
            dim = UIKit.Image(UIKit.Rect(root, "Dim", Vector2.zero, Vector2.one), new Color(0f, 0f, 0f, 0.35f), UIKit.Texture2DSprite());
            dim.raycastTarget = true;
            // Left column: title + list
            var left = UIKit.Rect(root, "Left", new Vector2(0.04f, 0.06f), new Vector2(0.46f, 0.94f));
            titleText = UIKit.Text(left, "Title", "", 76, TextAnchor.UpperLeft, UIKit.TextMain, FontStyle.Bold);
            Place(titleText.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 1f));
            subtitleText = UIKit.Text(left, "Subtitle", "", 26, TextAnchor.UpperLeft, UIKit.Accent, FontStyle.Bold);
            Place(subtitleText.rectTransform, new Vector2(0f, 0.80f), new Vector2(1f, 0.86f));
            var listFrame = UIKit.Rect(left, "ListFrame", new Vector2(0f, 0.06f), new Vector2(1f, 0.79f));
            var viewport = UIKit.Rect(listFrame, "Viewport", Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            listContent = UIKit.Rect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            listContent.pivot = new Vector2(0.5f, 1f);
            var vlg = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            var fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = listFrame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            footerText = UIKit.Text(left, "Footer", "", 22, TextAnchor.LowerLeft, UIKit.TextDim);
            Place(footerText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.05f));
            // Right column: info card
            infoRoot = UIKit.Rect(root, "Info", new Vector2(0.54f, 0.10f), new Vector2(0.96f, 0.80f));
            UIKit.Image(infoRoot, UIKit.Panel);
            infoText = UIKit.Text(infoRoot, "Text", "", 28, TextAnchor.UpperLeft, UIKit.TextMain);
            infoText.supportRichText = true;
            UIKit.Stretch(infoText.rectTransform, 30, 26, 30, 26);
            headerRight = UIKit.Text(root, "HeaderRight", "", 30, TextAnchor.UpperRight, UIKit.Amber, FontStyle.Bold);
            Place(headerRight.rectTransform, new Vector2(0.54f, 0.88f), new Vector2(0.96f, 0.95f));

            // Story dialog
            dialogRoot = UIKit.Rect(cr, "Dialog", new Vector2(0.12f, 0.06f), new Vector2(0.88f, 0.36f));
            UIKit.Image(dialogRoot, new Color(0.02f, 0.03f, 0.05f, 0.9f));
            dialogSpeaker = UIKit.Text(dialogRoot, "Speaker", "", 34, TextAnchor.UpperLeft, UIKit.Accent, FontStyle.Bold);
            Place(dialogSpeaker.rectTransform, new Vector2(0.03f, 0.74f), new Vector2(0.97f, 0.95f));
            dialogBody = UIKit.Text(dialogRoot, "Body", "", 30, TextAnchor.UpperLeft, UIKit.TextMain);
            Place(dialogBody.rectTransform, new Vector2(0.03f, 0.16f), new Vector2(0.97f, 0.74f));
            var hint = UIKit.Text(dialogRoot, "Hint", "Enter / A  -  continue", 22, TextAnchor.LowerRight, UIKit.TextDim);
            Place(hint.rectTransform, new Vector2(0.5f, 0.03f), new Vector2(0.97f, 0.16f));
            dialogRoot.gameObject.SetActive(false);
            root.gameObject.SetActive(false);
        }

        static void Place(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public void Show(string title, string subtitle, IList<MenuEntry> items, string info = null, Action back = null, int selected = 0, string headerRightText = null, float dimAlpha = 0.35f)
        {
            Build();
            root.gameObject.SetActive(true);
            titleText.text = title;
            subtitleText.text = subtitle ?? "";
            headerRight.text = headerRightText ?? "";
            dim.color = new Color(0f, 0f, 0f, dimAlpha);
            onBack = back;
            footerText.text = back != null ? "Enter / A  select      Esc / B  back      ← →  adjust" : "Enter / A  select      ← →  adjust";
            foreach (Transform c in listContent) Destroy(c.gameObject);
            rows.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var rowRt = UIKit.Rect(listContent, "Row" + i, Vector2.zero, Vector2.one);
                var le = rowRt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 64f;
                var b = UIKit.Button(rowRt, "Button", item.Label, null, 30);
                var captured = item;
                int index = i;
                b.onClick.AddListener(() => { if (captured.Enabled) captured.OnSelect?.Invoke(); });
                b.interactable = true;
                var label = b.GetComponentInChildren<Text>();
                if (!item.Enabled) label.color = UIKit.TextDim;
                if (item.Tint.HasValue) label.color = item.Tint.Value;
                var right = UIKit.Text(b.transform, "Right", item.Right ?? "", 26, TextAnchor.MiddleRight, item.Enabled ? UIKit.Amber : UIKit.TextDim, FontStyle.Bold);
                right.rectTransform.offsetMax = new Vector2(-20, 0);
                var trigger = b.gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
                entry.callback.AddListener(_ => Focus(index));
                trigger.triggers.Add(entry);
                var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                hover.callback.AddListener(_ => EventSystem.current?.SetSelectedGameObject(b.gameObject));
                trigger.triggers.Add(hover);
                rows.Add((b, item, right));
            }
            // explicit vertical navigation with wrap
            for (int i = 0; i < rows.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = rows[(i - 1 + rows.Count) % rows.Count].button;
                nav.selectOnDown = rows[(i + 1) % rows.Count].button;
                rows[i].button.navigation = nav;
            }
            infoRoot.gameObject.SetActive(!string.IsNullOrEmpty(info) || HasAnyInfo(items));
            infoText.text = info ?? "";
            lastSelected = -1;
            if (rows.Count > 0)
            {
                int sel = Mathf.Clamp(selected, 0, rows.Count - 1);
                EventSystem.current?.SetSelectedGameObject(null);
                EventSystem.current?.SetSelectedGameObject(rows[sel].button.gameObject);
                Focus(sel);
            }
            inputCooldown = 0.15f;
        }

        static bool HasAnyInfo(IList<MenuEntry> items)
        {
            foreach (var i in items) if (!string.IsNullOrEmpty(i.Info)) return true;
            return false;
        }

        void Focus(int index)
        {
            if (index < 0 || index >= rows.Count) return;
            lastSelected = index;
            var item = rows[index].item;
            if (!string.IsNullOrEmpty(item.Info)) infoText.text = item.Info;
            // keep the focused row visible
            Canvas.ForceUpdateCanvases();
            var content = listContent;
            var viewport = (RectTransform)content.parent;
            var row = (RectTransform)rows[index].button.transform.parent;
            float rowTop = -row.anchoredPosition.y - row.rect.height * (1f - row.pivot.y);
            float rowBottom = rowTop + row.rect.height;
            float viewH = viewport.rect.height;
            var pos = content.anchoredPosition;
            if (rowTop < pos.y) pos.y = rowTop;
            else if (rowBottom > pos.y + viewH) pos.y = rowBottom - viewH;
            content.anchoredPosition = pos;
        }

        public int SelectedIndex => lastSelected;

        public void SetItemRight(int index, string right)
        {
            if (index >= 0 && index < rows.Count && rows[index].right != null) rows[index].right.text = right;
        }

        public void SetInfo(string text)
        {
            if (infoText == null) return;
            infoRoot.gameObject.SetActive(true);
            infoText.text = text;
        }

        public void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        public void ShowDialog(string speaker, string body, Action onContinue)
        {
            Build();
            dialogRoot.gameObject.SetActive(true);
            dialogSpeaker.text = speaker;
            dialogBody.text = body;
            dialogContinue = onContinue;
            inputCooldown = 0.35f;
        }

        void Update()
        {
            if (canvas == null) return;
            inputCooldown -= Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if (DialogOpen)
            {
                bool next = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                            || (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame))
                            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                if (next && inputCooldown <= 0f)
                {
                    dialogRoot.gameObject.SetActive(false);
                    var cb = dialogContinue;
                    dialogContinue = null;
                    cb?.Invoke();
                }
                return;
            }
            if (!IsOpen || inputCooldown > 0f) return;
            bool back = (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (gp != null && gp.buttonEast.wasPressedThisFrame);
            if (back && onBack != null)
            {
                inputCooldown = 0.2f;
                onBack.Invoke();
                return;
            }
            int adjust = 0;
            if ((kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (gp != null && gp.dpad.left.wasPressedThisFrame)) adjust = -1;
            if ((kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (gp != null && gp.dpad.right.wasPressedThisFrame)) adjust = 1;
            if (adjust != 0 && lastSelected >= 0 && lastSelected < rows.Count)
            {
                var item = rows[lastSelected].item;
                if (item.OnAdjust != null && item.Enabled) item.OnAdjust(adjust);
            }
            // Re-select if focus was lost (mouse click on background etc.)
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && rows.Count > 0)
                EventSystem.current.SetSelectedGameObject(rows[Mathf.Clamp(lastSelected, 0, rows.Count - 1)].button.gameObject);
        }
    }
}
