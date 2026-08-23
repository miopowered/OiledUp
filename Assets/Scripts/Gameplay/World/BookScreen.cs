using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The reading view for a <see cref="ReferenceBook"/>.
    /// <para>
    /// Styled as paper rather than as a screen — warm stock, dark ink, no signal colours. The
    /// terminal is the machine you file with; a manual is a thing you hold, and they should not
    /// look like the same object.
    /// </para>
    /// The day clock keeps running while this is open, which is the whole cost of looking something
    /// up (§6.1).
    /// </summary>
    public sealed class BookScreen : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerInteractor interactor;

        private static readonly Color Paper = new(0.90f, 0.88f, 0.82f);
        private static readonly Color PaperEdge = new(0.82f, 0.79f, 0.72f);
        private static readonly Color Ink = new(0.13f, 0.12f, 0.11f);
        private static readonly Color InkSoft = new(0.38f, 0.36f, 0.33f);

        private VisualElement root;
        private List<BookPage> pages = new();
        private string title = "Reference";
        private int index;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (!IsOpen || Keyboard.current == null) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        public void Open(string bookTitle, List<BookPage> bookPages)
        {
            title = string.IsNullOrEmpty(bookTitle) ? "Reference" : bookTitle;
            pages = bookPages ?? new List<BookPage>();
            index = 0;

            IsOpen = true;
            root.style.display = DisplayStyle.Flex;
            PlayerController.SetCursorLocked(false);
            if (player != null) player.enabled = false;
            if (interactor != null) interactor.enabled = false;
            Rebuild();
        }

        public void Close()
        {
            IsOpen = false;
            root.style.display = DisplayStyle.None;
            PlayerController.SetCursorLocked(true);
            if (player != null) player.enabled = true;
            if (interactor != null) interactor.enabled = true;
        }

        private void Rebuild()
        {
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.05f, 0.88f));
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;

            var book = new VisualElement();
            book.style.width = Length.Percent(78);
            book.style.height = Length.Percent(82);
            book.style.backgroundColor = new StyleColor(Paper);
            book.style.paddingTop = 18;
            book.style.paddingBottom = 18;
            book.style.paddingLeft = 22;
            book.style.paddingRight = 22;
            LabHud.Round(book, 3);
            root.Add(book);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new StyleColor(PaperEdge);
            header.style.paddingBottom = 8;
            header.style.marginBottom = 10;

            var heading = new Label(title);
            heading.style.fontSize = 18;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color = new StyleColor(Ink);
            header.Add(heading);

            var close = new Button(Close) { text = "CLOSE  (Esc)" };
            StylePaperButton(close);
            header.Add(close);
            book.Add(header);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;
            book.Add(body);

            body.Add(Contents());
            body.Add(Page());
        }

        private VisualElement Contents()
        {
            var list = new ScrollView();
            list.style.width = 210;
            list.style.marginRight = 18;
            list.style.borderRightWidth = 1;
            list.style.borderRightColor = new StyleColor(PaperEdge);
            list.style.paddingRight = 10;

            for (int i = 0; i < pages.Count; i++)
            {
                int captured = i;
                var entry = new Button(() => { index = captured; Rebuild(); })
                { text = pages[i].Title };

                entry.style.backgroundColor = new StyleColor(
                    i == index ? PaperEdge : new Color(0f, 0f, 0f, 0f));
                entry.style.color = new StyleColor(Ink);
                entry.style.fontSize = 13;
                entry.style.unityTextAlign = TextAnchor.MiddleLeft;
                entry.style.marginLeft = 0;
                entry.style.marginBottom = 2;
                entry.style.paddingTop = 6;
                entry.style.paddingBottom = 6;
                entry.style.paddingLeft = 8;
                entry.style.borderTopWidth = 0;
                entry.style.borderBottomWidth = 0;
                entry.style.borderLeftWidth = 0;
                entry.style.borderRightWidth = 0;
                LabHud.Round(entry, 2);
                list.Add(entry);
            }

            if (pages.Count == 0)
            {
                var empty = new Label("This volume is empty.");
                empty.style.fontSize = 12;
                empty.style.color = new StyleColor(InkSoft);
                list.Add(empty);
            }

            return list;
        }

        private VisualElement Page()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            if (index < 0 || index >= pages.Count) return scroll;

            var page = pages[index];

            var heading = new Label(page.Title);
            heading.style.fontSize = 15;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color = new StyleColor(Ink);
            heading.style.marginBottom = 8;
            scroll.Add(heading);

            var text = new Label(page.Body);
            text.style.fontSize = 13;
            text.style.color = new StyleColor(Ink);
            text.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(text);

            var footer = new Label($"page {index + 1} of {pages.Count}");
            footer.style.fontSize = 11;
            footer.style.color = new StyleColor(InkSoft);
            footer.style.marginTop = 14;
            scroll.Add(footer);

            return scroll;
        }

        private static void StylePaperButton(Button button)
        {
            button.style.backgroundColor = new StyleColor(PaperEdge);
            button.style.color = new StyleColor(Ink);
            button.style.fontSize = 12;
            button.style.paddingTop = 6;
            button.style.paddingBottom = 6;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            LabHud.Round(button, 2);
        }
    }
}
