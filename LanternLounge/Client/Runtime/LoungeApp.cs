using LanternLounge.Client.Online;
using LanternLounge.Client.Screens;
using LanternLounge.Client.Ui;
using LanternLounge.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
#if LL_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace LanternLounge.Client
{
    /// <summary>
    /// The app shell. Builds the UI Toolkit panel in code, owns the game registry, and swaps
    /// screens. It starts by itself when you press Play (see <see cref="LoungeBootstrap"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoungeApp : MonoBehaviour
    {
        private const float PhoneWidth = 390f;
        private const float PhoneHeight = 844f;
        private const float MaxContentWidth = 480f;

        private UIDocument document;
        private PanelSettings panelSettings;
        private VisualElement frame;
        private bool? wasLandscape;
        private bool usingFallbackTheme;

        public GameRegistry Registry { get; private set; }

        /// <summary>The connection to the online game server (idle until someone plays online).</summary>
        internal OnlineSession Online { get; private set; }

        private void Awake()
        {
            Registry = GameCatalog.Create();
            Online = new OnlineSession();
            EnsureEventSystem();
            Sfx.Init(gameObject);

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Lantern Lounge Panel";
            // Unity's default runtime theme gives scroll views, fonts and text settings their base styles.
            var theme = Resources.Load<ThemeStyleSheet>("LanternLoungeTheme");
            usingFallbackTheme = theme == null;
            panelSettings.themeStyleSheet = theme != null ? theme : ScriptableObject.CreateInstance<ThemeStyleSheet>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int((int)PhoneWidth, (int)PhoneHeight);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            UpdateScaleMatch();

            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
        }

        private void Start()
        {
            BuildRoot();
            ShowLounge();
        }

        private void Update()
        {
            UpdateScaleMatch();
            Online?.Pump();
        }

        private void OnApplicationQuit()
        {
            Online?.Shutdown();
        }

        /// <summary>Create or join an online room for a game.</summary>
        public void ShowOnline(string gameId)
        {
            string name = Registry.TryGetInfo(gameId, out var info) ? info.DisplayName : gameId;
            Show(new OnlineScreen(this, Online, gameId, name).Build());
        }

        /// <summary>The online table for the room's game, once the host has started it.</summary>
        public void ShowOnlineGame(string gameId)
        {
            switch (gameId)
            {
                case global::LanternLounge.Games.PebbleDrop.PebbleDropModule.GameId:
                    Show(new PebbleDropScreen(this, Online).Build());
                    break;
                default:
                    Debug.LogWarning($"Lantern Lounge: online play for {gameId} isn't in the app yet.");
                    Online.Leave();
                    ShowLounge();
                    break;
            }
        }

        public void ShowLounge()
        {
            Show(new LoungeScreen(this).Build());
        }

        /// <summary>Four-player Pebble Drop: seats 0..humans-1 are people, the rest are bots.</summary>
        public void ShowPebbleDrop(int humans)
        {
            Show(new PebbleDropScreen(this, humans).Build());
        }

        public void ShowTidebound()
        {
            Show(new TideboundScreen(this).Build());
        }

        public void ShowTreehouseHoldem(int humans)
        {
            Show(new TreehouseHoldemScreen(this, humans).Build());
        }

        public void ShowPrismRush()
        {
            Show(new PrismRushScreen(this).Build());
        }

        private void Show(VisualElement screen)
        {
            frame.Clear();
            screen.style.flexGrow = 1;
            frame.Add(screen);
        }

        private void BuildRoot()
        {
            var root = document.rootVisualElement;
            root.Clear();
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.backgroundColor = Theme.Ground;
            root.style.alignItems = Align.Center;

            if (usingFallbackTheme)
            {
                // Without the default theme there is no font, so text would not draw.
                Debug.LogWarning("Lantern Lounge: LanternLoungeTheme.tss was not found; using a plain fallback theme.");
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            }

            frame = new VisualElement { name = "frame" };
            frame.style.flexGrow = 1;
            frame.style.width = Length.Percent(100);
            frame.style.maxWidth = MaxContentWidth;
            root.Add(frame);
        }

        // Phones in portrait scale to their width; PCs and landscape screens scale to height,
        // so the phone-shaped layout stays readable and centred.
        private void UpdateScaleMatch()
        {
            if (panelSettings == null) return;
            bool landscape = Screen.width > Screen.height;
            if (wasLandscape == landscape) return;
            wasLandscape = landscape;
            panelSettings.match = landscape ? 1f : 0f;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if LL_INPUT_SYSTEM
            // Added from code, the module can start without any input actions, which leaves
            // clicks and taps unhandled. Load the standard point/click/navigate actions.
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            DontDestroyOnLoad(go);
        }
    }

    /// <summary>Creates the app the moment Play starts, so no scene setup is needed.</summary>
    internal static class LoungeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindAnyObjectByType<LoungeApp>() != null) return;
            var go = new GameObject("Lantern Lounge App");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<LoungeApp>();
        }
    }

    /// <summary>Player settings kept on this device.</summary>
    internal static class Prefs
    {
        private const string ReduceMotionKey = "lanternlounge.reduceMotion";
        private const string SoundKey = "lanternlounge.sound";
        private const string HoldemHintsKey = "lanternlounge.holdemHints";

        /// <summary>Treehouse Hold'em: show "how strong is my hand?" hints.</summary>
        public static bool HoldemHints
        {
            get => PlayerPrefs.GetInt(HoldemHintsKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(HoldemHintsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(SoundKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static bool ReduceMotion
        {
            get => PlayerPrefs.GetInt(ReduceMotionKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(ReduceMotionKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
