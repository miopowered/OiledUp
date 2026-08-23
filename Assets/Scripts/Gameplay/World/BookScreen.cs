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

        [Header("Feel")]
        [SerializeField] private int openMilliseconds = 220;
        [SerializeField] private int turnMilliseconds = 160;

        private static readonly Color Paper = new(0.90f, 0.88f, 0.82f);
        private static readonly Color PaperEdge = new(0.82f, 0.79f, 0.72f);
        private static readonly Color Ink = new(0.13f, 0.12f, 0.11f);
        private static readonly Color InkSoft = new(0.38f, 0.36f, 0.33f);

        private VisualElement root;
        private VisualElement bookPanel;
        private ScrollView contentsList;
        private VisualElement pageHost;
        private Label headingLabel;

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

            if (Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }

            // Arrow keys turn pages. Reading a manual should not require aiming a mouse at a list.
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame) Turn(1);
            else if (Keyboard.current.leftArrowKey.wasPressedThisFrame) Turn(-1);
        }

        private void Turn(int delta)
        {
            int next = Mathf.Clamp(index + delta, 0, Mathf.Max(0, pages.Count - 1));
            if (next != index) ShowPage(next, delta);
        }

        // -- Lifecycle -------------------------------------------------------------------------------

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

            BuildShell();
            ShowPage(0, 0);

            // The covers coming up: the panel rises and fades rather than appearing, and the dim
            // behind it comes in with it. Without this a manual pops into existence, which reads as
            // a menu rather than as something you opened.
            Animate(root, 0f, 0f, openMilliseconds);
            Animate(bookPanel, 0f, 26f, openMilliseconds);
        }

        public void Close()
        {
            IsOpen = false;
            root.style.display = DisplayStyle.None;
            PlayerController.SetCursorLocked(true);
            if (player != null) player.enabled = true;
            if (interactor != null) interactor.enabled = true;
        }

        // -- Shell -----------------------------------------------------------------------------------

        private void BuildShell()
        {
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.05f, 0.88f));
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;

            bookPanel = new VisualElement();
            bookPanel.style.width = Length.Percent(78);
            bookPanel.style.height = Length.Percent(82);
            bookPanel.style.backgroundColor = new StyleColor(Paper);
            bookPanel.style.paddingTop = 18;
            bookPanel.style.paddingBottom = 18;
            bookPanel.style.paddingLeft = 22;
            bookPanel.style.paddingRight = 22;
            LabHud.Round(bookPanel, 3);
            root.Add(bookPanel);

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
            bookPanel.Add(header);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;
            bookPanel.Add(body);

            contentsList = new ScrollView();
            contentsList.style.width = 210;
            contentsList.style.marginRight = 18;
            contentsList.style.borderRightWidth = 1;
            contentsList.style.borderRightColor = new StyleColor(PaperEdge);
            contentsList.style.paddingRight = 10;
            body.Add(contentsList);

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            body.Add(right);

            headingLabel = new Label();
            headingLabel.style.fontSize = 15;
            headingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headingLabel.style.color = new StyleColor(Ink);
            headingLabel.style.marginBottom = 8;
            right.Add(headingLabel);

            pageHost = new VisualElement();
            pageHost.style.flexGrow = 1f;
            right.Add(pageHost);

            RefreshContents();
        }

        private void RefreshContents()
        {
            contentsList.Clear();

            for (int i = 0; i < pages.Count; i++)
            {
                int captured = i;
                var entry = new Button(() => ShowPage(captured, captured > index ? 1 : -1))
                { text = pages[i].Title };

                entry.style.backgroundColor = new StyleColor(
                    i == index ? PaperEdge : new Color(0f, 0f, 0f, 0f));
                entry.style.color = new StyleColor(i == index ? Ink : InkSoft);
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
                contentsList.Add(entry);
            }

            if (pages.Count == 0)
            {
                var empty = new Label("This volume is empty.");
                empty.style.fontSize = 12;
                empty.style.color = new StyleColor(InkSoft);
                contentsList.Add(empty);
            }
        }

        /// <summary>
        /// Swap the page body. <paramref name="direction"/> is +1 forward, -1 back, 0 for the first
        /// page on open — the new sheet slides in from the side you turned towards, so a page turn
        /// has a direction rather than just blinking.
        /// </summary>
        private void ShowPage(int newIndex, int direction)
        {
            index = Mathf.Clamp(newIndex, 0, Mathf.Max(0, pages.Count - 1));
            RefreshContents();

            pageHost.Clear();
            if (pages.Count == 0) return;

            var page = pages[index];
            headingLabel.text = page.Title;

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            var text = new Label(page.Body);
            text.style.fontSize = 13;
            text.style.color = new StyleColor(Ink);
            text.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(text);

            var footer = new Label($"page {index + 1} of {pages.Count}    [<] [>] to turn");
            footer.style.fontSize = 11;
            footer.style.color = new StyleColor(InkSoft);
            footer.style.marginTop = 14;
            scroll.Add(footer);

            pageHost.Add(scroll);

            if (direction != 0) Animate(pageHost, direction * 34f, 0f, turnMilliseconds);
        }

        // -- Animation -------------------------------------------------------------------------------

        /// <summary>
        /// Fade and slide an element into place from an offset.
        /// <para>
        /// Transitions are disabled while the starting state is applied, otherwise the element would
        /// animate <i>to</i> the offset first and the movement would run backwards.
        /// </para>
        /// </summary>
        private static void Animate(VisualElement element, float fromX, float fromY, int milliseconds)
        {
            if (element == null) return;

            SetTransition(element, 0);
            element.style.opacity = 0f;
            element.style.translate = new Translate(fromX, fromY);

            element.schedule.Execute(() =>
            {
                SetTransition(element, milliseconds);
                element.style.opacity = 1f;
                element.style.translate = new Translate(0f, 0f);
            }).StartingIn(16);
        }

        private static void SetTransition(VisualElement element, int milliseconds)
        {
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { "opacity", "translate" });
            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new(milliseconds, TimeUnit.Millisecond) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new(EasingMode.EaseOutCubic) });
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
