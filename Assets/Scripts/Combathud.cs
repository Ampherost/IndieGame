using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The screen furniture around a battle: phase banner, round counter, end-turn button
/// and the result screen. This is the first listener TurnManager's three events have
/// ever had.
///
/// Binding is deliberately defensive about script execution order. TurnManager fires
/// OnPhaseStart from its own Start(), so a HUD that happens to run later would miss the
/// opening banner entirely; after subscribing we sync from TurnManager's current state
/// instead of trusting that we were listening in time.
/// </summary>
public class CombatHUD : MonoBehaviour
{
    [Header("Round / Phase")]
    [Tooltip("Persistent label, e.g. 'Round 3'.")]
    public TMP_Text roundText;

    [Tooltip("Panel that flashes 'Player Phase' / 'Enemy Phase' at the start of each phase.")]
    public GameObject phaseBannerPanel;
    public TMP_Text phaseBannerText;

    [Tooltip("Optional. Put a CanvasGroup on the banner and it fades out instead of popping.")]
    public CanvasGroup phaseBannerGroup;

    public float bannerHoldSeconds = 1.0f;
    public float bannerFadeSeconds = 0.35f;

    public Color playerPhaseColor = new Color(0.30f, 0.75f, 1.00f);
    public Color enemyPhaseColor = new Color(0.95f, 0.35f, 0.35f);

    [Header("End Turn")]
    [Tooltip("Ends the player phase immediately. Disabled outside the player phase.")]
    public Button endTurnButton;

    [Header("Result")]
    public GameObject resultPanel;
    public TMP_Text resultText;
    [Tooltip("Optional. Reloads the current scene.")]
    public Button retryButton;
    [Tooltip("Optional. Loads MainMenuScene.")]
    public Button mainMenuButton;
    [Tooltip("Shown on victory. Returns to the overworld.")]
    public Button continueButton;

    public string victoryMessage = "Victory";
    public string defeatMessage = "Defeat";
    public string drawMessage = "Draw";

    [Header("Health Bars")]
    [Tooltip("Adds a UnitHealthBar to every unit in the scene that doesn't already have one.")]
    public bool autoAttachHealthBars = true;

    private TurnManager turnManager;
    private Coroutine bannerRoutine;

    private void Start()
    {
        if (phaseBannerPanel != null) phaseBannerPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);

        if (endTurnButton != null)
        {
            endTurnButton.onClick.AddListener(OnEndTurnPressed);
            endTurnButton.interactable = false;
        }
        if (continueButton != null)
            continueButton.onClick.AddListener(() =>
            {
                if (BattleRunner.Instance != null) BattleRunner.Instance.ReturnNow();
                else SceneManager.LoadScene("MainMenuScene");   // standalone combat scene
            });

        if (retryButton != null)
            retryButton.onClick.AddListener(() =>
            {
                if (BattleRunner.Instance != null) BattleRunner.Instance.Retry();
                else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            });

        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(() =>
            {
                BattleLauncher.ClearPending();   // don't carry a stale encounter into the next run
                SceneManager.LoadScene("MainMenuScene");
            });

        if (autoAttachHealthBars)
            foreach (var unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
                UnitHealthBar.AttachTo(unit);

        StartCoroutine(BindToTurnManager());
    }

    private IEnumerator BindToTurnManager()
    {
        // TurnManager assigns Instance in Awake, but may not exist yet if this HUD lives
        // in a scene that loads it late. Wait rather than silently doing nothing.
        float waited = 0f;
        while (TurnManager.Instance == null && waited < 5f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        turnManager = TurnManager.Instance;
        if (turnManager == null)
        {
            Debug.LogWarning("[CombatHUD] No TurnManager in the scene — the HUD has nothing to " +
                             "listen to.", this);
            yield break;
        }

        turnManager.OnPhaseStart += HandlePhaseStart;
        turnManager.OnRoundStart += HandleRoundStart;
        turnManager.OnCombatEnd += HandleCombatEnd;

        // Catch up on whatever already happened before we were listening.
        HandleRoundStart(turnManager.RoundNumber);
        if (turnManager.CombatOver) HandleCombatEnd(turnManager.Winner);
        else HandlePhaseStart(turnManager.CurrentPhase);
    }

    private void OnDestroy()
    {
        if (turnManager == null) return;
        turnManager.OnPhaseStart -= HandlePhaseStart;
        turnManager.OnRoundStart -= HandleRoundStart;
        turnManager.OnCombatEnd -= HandleCombatEnd;
    }

    private void Update()
    {
        if (endTurnButton == null || turnManager == null) return;

        endTurnButton.interactable =
            !turnManager.CombatOver && turnManager.CurrentPhase == Team.Player;
    }

    // ---- Event handlers ----

    private void HandlePhaseStart(Team team)
    {
        ShowBanner(team == Team.Player ? "Player Phase" : "Enemy Phase",
                   team == Team.Player ? playerPhaseColor : enemyPhaseColor);
    }

    private void HandleRoundStart(int round)
    {
        if (roundText != null) roundText.text = $"Round {round}";
    }

    private void HandleCombatEnd(Team? winner)
    {
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        if (phaseBannerPanel != null) phaseBannerPanel.SetActive(false);
        if (endTurnButton != null) endTurnButton.interactable = false;

        bool victory = winner == Team.Player;

        if (victory) { resultText.text = victoryMessage; resultText.color = playerPhaseColor; }
        else if (winner == Team.Enemy) { resultText.text = defeatMessage; resultText.color = enemyPhaseColor; }
        else { resultText.text = drawMessage; resultText.color = Color.white; }

        // Victory offers one way forward; anything else offers a way to try again or bail out.
        if (continueButton != null) continueButton.gameObject.SetActive(victory);
        if (retryButton != null) retryButton.gameObject.SetActive(!victory);
        if (mainMenuButton != null) mainMenuButton.gameObject.SetActive(!victory);

        resultPanel.SetActive(true);
    }

    // ---- End turn ----

    private void OnEndTurnPressed()
    {
        if (turnManager == null || turnManager.CombatOver) return;
        if (turnManager.CurrentPhase != Team.Player) return;

        turnManager.EndPhaseEarly(Team.Player);
    }

    // ---- Banner ----

    private void ShowBanner(string text, Color color)
    {
        if (phaseBannerPanel == null || phaseBannerText == null) return;

        phaseBannerText.text = text;
        phaseBannerText.color = color;

        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        bannerRoutine = StartCoroutine(BannerRoutine());
    }

    private IEnumerator BannerRoutine()
    {
        phaseBannerPanel.SetActive(true);
        if (phaseBannerGroup != null) phaseBannerGroup.alpha = 1f;

        yield return new WaitForSeconds(bannerHoldSeconds);

        if (phaseBannerGroup != null && bannerFadeSeconds > 0f)
        {
            float t = 0f;
            while (t < bannerFadeSeconds)
            {
                t += Time.deltaTime;
                phaseBannerGroup.alpha = 1f - (t / bannerFadeSeconds);
                yield return null;
            }
            phaseBannerGroup.alpha = 0f;
        }

        phaseBannerPanel.SetActive(false);
        bannerRoutine = null;
    }
}
