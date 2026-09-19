using UnityEngine;

namespace AgesOfConflict
{
    /// <summary>A first-visit field guide, also available from the simulation's Help button.</summary>
    public sealed class WelcomeGuide
    {
        private const string SeenPreferenceKey = "AgesOfConflict.WelcomeGuideSeen";
        private static readonly Color Accent = new Color(0.40f, 0.86f, 0.73f);
        private static readonly Color Muted = new Color(0.64f, 0.72f, 0.82f);

        private sealed class Page
        {
            public readonly string title;
            public readonly string subtitle;
            public readonly (string label, string text)[] tips;

            public Page(string title, string subtitle, params (string label, string text)[] tips)
            {
                this.title = title;
                this.subtitle = subtitle;
                this.tips = tips;
            }
        }

        private Page[] pages;
        private int pageIndex;
        private Vector2 scrollPosition;
        private bool firstVisit;
        private GUIStyle titleStyle, subtitleStyle, headingStyle, labelStyle, textStyle;
        private GUIStyle tabStyle, selectedTabStyle, buttonStyle, primaryButtonStyle;

        public bool IsOpen { get; private set; }
        public bool ShouldShowOnStartup => PlayerPrefs.GetInt(SeenPreferenceKey, 0) == 0;

        public void Open(SimulationManager simulation)
        {
            pages = CreatePages(simulation);
            firstVisit = ShouldShowOnStartup;
            pageIndex = 0;
            scrollPosition = Vector2.zero;
            IsOpen = true;
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            // Persist only after dismissal, so an interrupted first launch still shows the welcome.
            if (firstVisit)
            {
                PlayerPrefs.SetInt(SeenPreferenceKey, 1);
                PlayerPrefs.Save();
            }
        }

        public void Draw(float uiWidth, float uiHeight)
        {
            InitializeStyles();
            Color previousColor = GUI.color;
            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            bool previousEnabled = GUI.enabled;
            GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            GUI.enabled = true;
            try
            {
                DrawPanel(new Rect(0f, 0f, uiWidth, uiHeight), new Color(0.01f, 0.02f, 0.04f, 0.88f));
                float width = Mathf.Min(1120f, uiWidth - 64f);
                float height = Mathf.Min(650f, uiHeight - 48f);
                Rect panel = new Rect((uiWidth - width) / 2f, (uiHeight - height) / 2f, width, height);
                DrawPanel(new Rect(panel.x + 6f, panel.y + 8f, width, height), new Color(0f, 0f, 0f, 0.35f));
                DrawPanel(panel, new Color(0.055f, 0.08f, 0.12f));
                DrawPanel(new Rect(panel.x, panel.y, panel.width, 3f), Accent);

                float left = panel.x + 28f;
                float innerWidth = panel.width - 56f;
                GUI.Label(new Rect(left, panel.y + 19f, innerWidth, 20f), "AGES OF CONFLICT  /  FIELD GUIDE", headingStyle);
                GUI.Label(
                    new Rect(left, panel.y + 43f, innerWidth, 42f),
                    firstVisit ? "Welcome, commander." : "Your field guide",
                    titleStyle
                );
                GUI.Label(
                    new Rect(left, panel.y + 88f, innerWidth, 24f),
                    "Build a nation. Choose your battles. The simulation waits while you read.",
                    subtitleStyle
                );

                float tabWidth = (innerWidth - (pages.Length - 1) * 8f) / pages.Length;
                for (int i = 0; i < pages.Length; i++)
                {
                    bool selected = i == pageIndex;
                    Rect tab = new Rect(left + i * (tabWidth + 8f), panel.y + 128f, tabWidth, 38f);
                    if (selected)
                        DrawPanel(tab, Accent);
                    if (GUI.Button(tab, pages[i].title, selected ? selectedTabStyle : tabStyle))
                        SelectPage(i);
                }

                Page page = pages[pageIndex];
                GUI.Label(new Rect(left, panel.y + 188f, innerWidth, 26f), page.subtitle, subtitleStyle);
                Rect viewport = new Rect(left, panel.y + 225f, innerWidth, panel.height - 310f);
                float rowWidth = viewport.width - 22f;
                float contentHeight = 0f;
                foreach (var tip in page.tips)
                    contentHeight += GetRowHeight(tip.text, rowWidth) + 6f;

                scrollPosition = GUI.BeginScrollView(
                    viewport,
                    scrollPosition,
                    new Rect(0f, 0f, rowWidth, contentHeight)
                );
                float top = 0f;
                foreach (var tip in page.tips)
                {
                    float rowHeight = GetRowHeight(tip.text, rowWidth);
                    DrawPanel(new Rect(0f, top, rowWidth, rowHeight), new Color(0.085f, 0.12f, 0.17f));
                    GUI.Label(new Rect(14f, top + 9f, 154f, rowHeight - 18f), tip.label, labelStyle);
                    GUI.Label(new Rect(176f, top + 9f, rowWidth - 190f, rowHeight - 18f), tip.text, textStyle);
                    top += rowHeight + 6f;
                }
                GUI.EndScrollView();

                float footer = panel.yMax - 60f;
                GUI.Label(
                    new Rect(left, footer + 10f, 410f, 24f),
                    $"{pageIndex + 1} / {pages.Length}   |   Reopen anytime: Help [H]   |   Esc to close",
                    subtitleStyle
                );
                GUI.enabled = pageIndex > 0;
                if (GUI.Button(new Rect(panel.xMax - 390f, footer, 100f, 38f), "Previous", buttonStyle))
                    SelectPage(pageIndex - 1);
                GUI.enabled = pageIndex < pages.Length - 1;
                if (GUI.Button(new Rect(panel.xMax - 280f, footer, 100f, 38f), "Next", buttonStyle))
                    SelectPage(pageIndex + 1);
                GUI.enabled = true;
                Rect close = new Rect(panel.xMax - 166f, footer, 138f, 38f);
                DrawPanel(close, Accent);
                if (GUI.Button(close, firstVisit ? "Let's play" : "Back to game", primaryButtonStyle))
                    Close();
            }
            finally
            {
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackground;
                GUI.contentColor = previousContent;
                GUI.enabled = previousEnabled;
            }
        }

        private void SelectPage(int index)
        {
            pageIndex = index;
            scrollPosition = Vector2.zero;
            GUI.FocusControl(null);
        }

        private float GetRowHeight(string text, float width)
        {
            return Mathf.Max(38f, textStyle.CalcHeight(new GUIContent(text), width - 190f) + 18f);
        }

        private static void DrawPanel(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void InitializeStyles()
        {
            if (titleStyle != null)
                return;
            titleStyle = MakeTextStyle(30, Color.white, FontStyle.Bold);
            subtitleStyle = MakeTextStyle(14, Muted);
            headingStyle = MakeTextStyle(12, Accent, FontStyle.Bold);
            labelStyle = MakeTextStyle(14, Accent, FontStyle.Bold);
            textStyle = MakeTextStyle(15, new Color(0.89f, 0.93f, 0.97f));
            textStyle.wordWrap = true;
            tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            tabStyle.normal.textColor = Color.white;
            tabStyle.hover.textColor = Accent;
            selectedTabStyle = MakeTextStyle(14, new Color(0.04f, 0.12f, 0.12f), FontStyle.Bold);
            selectedTabStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle = new GUIStyle(tabStyle);
            primaryButtonStyle = new GUIStyle(selectedTabStyle);
            primaryButtonStyle.hover.textColor = Color.white;
        }

        private static GUIStyle MakeTextStyle(int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                padding = new RectOffset(0, 0, 0, 0),
                richText = false,
                wordWrap = false,
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private static Page[] CreatePages(SimulationManager simulation)
        {
            NationSimulator economy = simulation.nationSimulator;
            return new[]
            {
                new Page(
                    "Your nation",
                    "Start with a nation, then turn land and people into a thriving empire.",
                    ("Take command", "Left-click a colored nation to control it; left-click a city to inspect it."),
                    ("Head start", "Your first nation receives 5,000 people and control locks to that nation."),
                    ("Switch nations", "Turn off 'Lock controlled nation' before selecting another nation."),
                    ("Claim land", "Every nation automatically expands into adjacent unclaimed land."),
                    ("Growth boost", "'Selected state grows 2× faster' doubles expansion frequency and mobilization speed."),
                    ("Build cities", $"Right-click your land to build for {simulation.buildCityCost:N0} gold; leave {simulation.minCitySpacing} pixels between cities."),
                    ("City value", "Cities add population capacity, launch armies and keep connected territory supplied."),
                    ("Capture cities", "A city changes owner when its own map cell is conquered."),
                    ("Your ambition", "Grow your territory and climb the Nations ranking; each pixel is one map cell.")
                ),
                new Page(
                    "People & gold",
                    "A larger army needs people; a healthy civilian population keeps your nation growing.",
                    ("Population", "Your population is civilians plus soldiers; combat losses reduce the total."),
                    ("Army slider", "'Population in army' sets the soldier share; the remaining people are civilians."),
                    ("Growth", $"Each simulation second adds {economy.populationGrowthPerPopulation * 100f:0.##}% of civilians, using at least 10 as the growth base."),
                    ("Capacity", $"Your cap is {economy.maximumPopulationPerPixel:N0} people per land pixel + {economy.maximumPopulationPerCity:N0} per city."),
                    ("Land loss", "Losing territory or cities lowers the cap and can immediately reduce population."),
                    ("Gold income", $"Per second: (civilians × {economy.incomePerPopulation:0.###} + land pixels × {economy.incomePerPixel:0.###}) × {economy.interestRate:0.###}."),
                    ("Spend wisely", "Gold pays for cities and walls; combat strength comes from committed soldiers."),
                    ("Army trade-off", "A higher army share leaves fewer civilians producing income and population growth.")
                ),
                new Page(
                    "War basics",
                    "Choose a front, send your army and keep enough soldiers committed to break through.",
                    ("Declare war", "Right-click a rival, choose the percentage of your army to commit, then press Attack."),
                    ("Mobilize", "The blue marker travels from a friendly city through your land to the enemy border."),
                    ("Awaiting route", "Combat waits for a reachable shared border; armies cannot cross sea or third-party land."),
                    ("Two percentages", "Army share × front share = fighting force: 10,000 people × 20% army × 50% front = 1,000."),
                    ("Defend", "Defenders commit automatically; use the war sliders to adjust your share on each front."),
                    ("Multiple fronts", "Changing a war's allocation redistributes the remaining army percentage across other wars."),
                    ("Break through", "Aim above 1.5× the defending force; a larger advantage increases your advance speed."),
                    ("Zero allocation", "Setting an attacking front to 0% pauses that assault; the war remains declared."),
                    ("Make peace", "Use 'Cancel war' to end a front and release its allocation to the remaining wars.")
                ),
                new Page(
                    "Battle modifiers",
                    "Numbers win the opening; position, cities and supply shape the battle.",
                    ("Force advantage", $"The force ratio increases base advance speed, up to {simulation.maximumWarAdvanceSpeed:N0} border cells per second."),
                    ("Attacker losses", "Every 0.2 simulation seconds, attackers lose 5% of their committed force, rounded up."),
                    ("Defender losses", "Defenders lose one third of attacker casualties, rounded up and limited by their army size."),
                    ("Cities near battle", "Cities near the defending border add attacker losses; cities near the attacking border add advance."),
                    ("Capital distance", "Border cells farther from the defending capital receive higher conquest priority."),
                    ("Surrounding land", "More attacking neighbors make a border cell a higher-priority target; some randomness remains."),
                    ("Cut-off pockets", $"Land disconnected from every defending city receives ×{Mathf.Max(1f, simulation.isolatedCellConquestMultiplier):0.##} conquest priority."),
                    ("Arrow influence", $"Drawn arrows bias nearby target selection in their direction, with weight {simulation.warArrowWeight:0.##}."),
                    ("Live forces", "Committed forces update as population, casualties and army allocation change.")
                ),
                new Page(
                    "Arrows & walls",
                    "Sketch a plan on the map: arrows guide an offensive, walls make an invasion costly.",
                    ("Draw an arrow", "Hold A + left-drag from your land toward a target; stay on claimed land."),
                    ("Guide an attack", "Arrows prioritize conquest during a war; declare the war separately."),
                    ("Remove an arrow", "Hold A + click an existing arrow; it also disappears once you own its entire route."),
                    ("Draft a wall", "Hold D + left-drag entirely within your territory, then release to preview the price."),
                    ("Confirm or cancel", "Left-click the map to build; right-click or press Esc to cancel the draft."),
                    ("Wall price", $"A wall costs {simulation.wallCostPerPixel:0.##} gold per unique map cell in the draft; payment happens on confirmation."),
                    ("Breach penalty", $"A city-connected wall costs the attacker {simulation.wallBreachCasualtiesPerPixel:0.##} extra soldiers per captured wall cell."),
                    ("Isolated walls", $"Walls cut off from defending cities have a decaying breach penalty (rate {simulation.isolatedWallCostDecayPerSecond:0.###}/sec).")
                ),
                new Page(
                    "Controls",
                    "Keep these shortcuts close; you can return to this guide whenever you need it.",
                    ("Move the camera", "Arrow keys or middle/right mouse-drag pan across the map."),
                    ("Zoom / reset", "Scroll to zoom toward the pointer; press F to fit the entire map."),
                    ("Inspect", "Hover to preview a nation before taking control; the right panel shows your selected nation."),
                    ("Pause / play", "Space pauses or resumes the simulation; press S while paused to advance one tick."),
                    ("Simulation speed", "Keys 1 / 2 / 3 / 4 select 1× / 2× / 5× / 10× speed for growth, income and battles."),
                    ("New world", "R regenerates the map and resets nations, wars, arrows and walls."),
                    ("Dismiss", "Esc cancels a wall draft, closes a context menu or clears the selected city."),
                    ("Need a reminder?", "Press H or click Help to reopen this guide; closing it restores your prior play/pause state.")
                ),
            };
        }
    }
}
