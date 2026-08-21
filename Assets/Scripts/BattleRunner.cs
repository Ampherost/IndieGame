using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the battle, then takes it apart again. Put one of these in the combat scene.
///
/// EXECUTION ORDER is the whole trick here, so it's set explicitly to -100:
///
///   Awake (-100)  spawn units. Must beat TurnManager.Awake (0), which sweeps the scene
///                 with FindObjectsByType<Unit> to build its roster. Anything spawned
///                 after that sweep would never take a turn.
///
///   Start (-100)  set unit positions. Must beat Unit.Start (0) -> SnapToGrid(), which
///                 reads transform.position to work out which cell to claim. By Start,
///                 every Awake has run, so GridManager has finished aligning itself to the
///                 floor tilemap and CellToWorld gives the right answer.
///
/// We deliberately do NOT call Unit.PlaceAt ourselves: SnapToGrid would then see the cell
/// as occupied (by the very unit standing on it) and nudge the unit off it.
///
/// With no encounter launched — i.e. you pressed Play directly in the combat scene — this
/// leaves whatever units you hand-placed alone, so the scene still works standalone.
/// </summary>
[DefaultExecutionOrder(-100)]
public class BattleRunner : MonoBehaviour
{
    public static BattleRunner Instance { get; private set; }

    [Header("Testing")]
    [Tooltip("Used only when you press Play in this scene directly with no encounter " +
             "launched. Leave empty to keep the units you hand-placed in the scene.")]
    public EncounterData debugEncounter;

    [Header("Return Flow")]
    [Tooltip("Head back automatically once the battle resolves. Turn this off if you'd " +
             "rather CombatHUD's result panel wait for a Continue button — wire that " +
             "button to BattleRunner.ReturnNow().")]
    public bool autoReturn = true;

    [Tooltip("Beat before leaving, so the player can read the result banner.")]
    [Min(0f)] public float returnDelaySeconds = 2.5f;

    private EncounterData encounter;

    // Which party member each deployed unit represents, so HP can be written back.
    private readonly Dictionary<Unit, PartyMember> deployed = new Dictionary<Unit, PartyMember>();

    // HP before a shot was fired, so a Retry doesn't stack damage from the failed attempt.
    private readonly Dictionary<PartyMember, int> hpBeforeBattle = new Dictionary<PartyMember, int>();

    // Units spawned in Awake, positioned in Start.
    private readonly List<KeyValuePair<Unit, Vector2Int>> pendingPlacement =
        new List<KeyValuePair<Unit, Vector2Int>>();

    private bool resultsWritten;
    private bool leaving;

    // ---- Setup ----

    private void Awake()
    {
        Instance = this;

        encounter = BattleLauncher.Pending != null ? BattleLauncher.Pending : debugEncounter;

        if (encounter == null)
        {
            Debug.Log("[BattleRunner] No encounter active — running whatever units are already " +
                      "in the scene. (This is normal when you press Play in the combat scene.)");
            return;
        }

        ClearHandPlacedUnits();
        SpawnParty();
        SpawnEnemies();
        InstallObjective();
    }

    private void Start()
    {
        PlaceSpawnedUnits();

        if (TurnManager.Instance == null)
        {
            Debug.LogError("[BattleRunner] No TurnManager in this scene — the battle can never " +
                           "end, so we'd never return to the overworld.", this);
            return;
        }

        TurnManager.Instance.OnCombatEnd += HandleCombatEnd;

        // Vanishingly unlikely, but a zero-enemy encounter can resolve before we subscribe.
        if (TurnManager.Instance.CombatOver)
            HandleCombatEnd(TurnManager.Instance.Winner);
    }

    private void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnCombatEnd -= HandleCombatEnd;

        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Remove units placed by hand in the editor — this encounter brings its own cast.
    /// Deactivating first matters: Destroy is deferred to the end of the frame, so
    /// TurnManager's Awake sweep would otherwise still find them. Its sweep excludes
    /// inactive objects, so this hides them in time.
    /// </summary>
    private void ClearHandPlacedUnits()
    {
        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            if (u == null) continue;
            u.gameObject.SetActive(false);
            Destroy(u.gameObject);
        }
    }

    private void SpawnParty()
    {
        GameData data = GameData.Instance;
        if (data == null)
        {
            Debug.LogError("[BattleRunner] No GameData found, so there's no party to deploy. " +
                           "Drop the GameData prefab into your first scene.", this);
            return;
        }

        List<Vector2Int> cells = encounter.playerSpawnCells;
        int slots = Mathf.Min(cells.Count, encounter.maxDeployed);
        int slot = 0;

        foreach (var member in data.Party)
        {
            if (slot >= slots) break;
            if (!member.IsDeployable) continue;

            Unit unit = SpawnUnit(member.definition, cells[slot], Team.Player, null);
            if (unit == null) continue;

            // Carry wounds in from the last fight.
            unit.currentHP = Mathf.Clamp(member.currentHP, 1, unit.maxHP);

            deployed[unit] = member;
            hpBeforeBattle[member] = member.currentHP;
            slot++;
        }

        if (slot == 0)
            Debug.LogError("[BattleRunner] Nobody was deployed. Either the party is empty, " +
                           "everyone is at 0 HP or benched, or their definitions have no " +
                           "prefabs assigned.", this);
    }

    private void SpawnEnemies()
    {
        if (encounter.enemies == null) return;

        foreach (var spawn in encounter.enemies)
        {
            if (spawn == null || spawn.definition == null) continue;
            SpawnUnit(spawn.definition, spawn.cell, Team.Enemy, spawn.nameOverride);
        }
    }

    private Unit SpawnUnit(UnitDefinition def, Vector2Int cell, Team team, string nameOverride)
    {
        if (def == null || !def.IsSpawnable)
        {
            Debug.LogError($"[BattleRunner] '{(def != null ? def.name : "null")}' has no prefab " +
                           $"with a Unit component, so it can't be spawned.", this);
            return null;
        }

        GameObject go = Instantiate(def.prefab);
        Unit unit = go.GetComponent<Unit>();

        def.ApplyTo(unit);
        unit.team = team;
        if (!string.IsNullOrWhiteSpace(nameOverride)) unit.unitName = nameOverride;
        go.name = $"{unit.unitName} [{team}]";

        // Positioning waits for Start — see the class comment.
        pendingPlacement.Add(new KeyValuePair<Unit, Vector2Int>(unit, cell));

        // TurnManager's own Awake sweep normally catches this, but register anyway in case
        // its Awake happened to run first.
        if (TurnManager.Instance != null)
            TurnManager.Instance.RegisterUnit(unit);

        return unit;
    }

    private void PlaceSpawnedUnits()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null)
        {
            if (pendingPlacement.Count > 0)
                Debug.LogError("[BattleRunner] No GridManager in the scene — spawned units have " +
                               "nowhere to stand.", this);
            return;
        }

        foreach (var pair in pendingPlacement)
        {
            if (pair.Key == null) continue;

            if (!grid.InBounds(pair.Value))
                Debug.LogWarning($"[BattleRunner] '{pair.Key.unitName}' is set to spawn on " +
                                 $"{pair.Value}, which isn't part of the painted map. It'll be " +
                                 $"nudged to the nearest free tile.", pair.Key);

            pair.Key.transform.position = grid.CellToWorld(pair.Value);
        }

        pendingPlacement.Clear();
    }

    /// <summary>
    /// Build the win condition for this encounter, if it needs one beyond the default
    /// "kill everything" rule TurnManager already applies.
    ///
    /// Objectives are created on an inactive child object first: AddComponent runs Awake
    /// immediately on an active object, and DefeatBossObjective disables itself when it
    /// wakes up with no boss assigned. Building it inactive lets us fill the field before
    /// its Awake ever runs.
    /// </summary>
    private void InstallObjective()
    {
        switch (encounter.victory)
        {
            case VictoryCondition.DefeatBoss:
                {
                    Unit boss = FindSpawnedBoss();
                    if (boss == null)
                    {
                        Debug.LogError("[BattleRunner] Defeat Boss encounter, but the boss didn't " +
                                       "spawn. Falling back to routing every enemy.", this);
                        return;
                    }

                    var obj = CreateObjective<DefeatBossObjective>("Objective_DefeatBoss",
                        o => { o.boss = boss; o.winsWhenBossFalls = Team.Player; });
                    Register(obj);
                    break;
                }

            case VictoryCondition.SurviveRounds:
                {
                    var obj = CreateObjective<SurviveRoundsObjective>("Objective_Survive",
                        o => { o.rounds = encounter.roundsToSurvive; o.survivingTeam = Team.Player; });
                    Register(obj);
                    break;
                }

            case VictoryCondition.RoutEnemies:
            default:
                // TurnManager.autoEndWhenTeamWipedOut already covers this.
                break;
        }
    }

    private T CreateObjective<T>(string objectName, System.Action<T> configure)
        where T : CombatObjective
    {
        var go = new GameObject(objectName);
        go.SetActive(false);                 // keeps Awake from firing early
        go.transform.SetParent(transform, false);

        T objective = go.AddComponent<T>();
        configure?.Invoke(objective);

        go.SetActive(true);                  // now Awake runs, with fields filled in
        return objective;
    }

    private void Register(CombatObjective objective)
    {
        if (objective != null && TurnManager.Instance != null)
            TurnManager.Instance.RegisterObjective(objective);
    }

    private Unit FindSpawnedBoss()
    {
        EncounterData.EnemySpawn bossSpawn = encounter.FindBossSpawn();
        if (bossSpawn == null) return null;

        foreach (var pair in pendingPlacement)
        {
            Unit u = pair.Key;
            if (u == null || u.team != Team.Enemy) continue;

            string expected = string.IsNullOrWhiteSpace(bossSpawn.nameOverride)
                ? bossSpawn.definition.unitName
                : bossSpawn.nameOverride;

            if (u.unitName == expected && pair.Value == bossSpawn.cell) return u;
        }
        return null;
    }

    // ---- Results ----

    private void HandleCombatEnd(Team? winner)
    {
        if (resultsWritten) return;
        resultsWritten = true;

        bool victory = winner == Team.Player;

        BattleLauncher.RecordResult(encounter, winner);
        WriteResultsToParty(victory);

        if (autoReturn) StartCoroutine(ReturnAfterDelay());
    }

    /// <summary>
    /// Push each deployed unit's final HP back onto its party member.
    ///
    /// Permadeath only applies to a won battle: a total party wipe is treated as a retreat,
    /// otherwise a single loss would empty the roster and leave the game unwinnable.
    /// </summary>
    private void WriteResultsToParty(bool victory)
    {
        GameData data = GameData.Instance;
        if (data == null) return;

        foreach (var pair in deployed)
        {
            Unit unit = pair.Key;
            PartyMember member = pair.Value;
            if (member == null) continue;

            // A destroyed GameObject compares equal to null; treat that as a casualty.
            member.currentHP = unit == null ? 0 : Mathf.Max(0, unit.currentHP);

            if (member.currentHP <= 0)
            {
                if (victory && data.permadeath)
                {
                    member.isDead = true;
                    Debug.Log($"[BattleRunner] {member.Name} was lost for good.");
                    continue;
                }

                member.currentHP = Mathf.Clamp(data.reviveHP, 1, member.MaxHP);
                Debug.Log($"[BattleRunner] {member.Name} fell but was recovered " +
                          $"({member.currentHP} HP).");
            }
        }

        if (victory && data.healAfterBattle) data.HealAll();
    }

    /// <summary>Undo the writeback, so retrying a lost battle starts from the same HP.</summary>
    private void RestorePreBattleHP()
    {
        foreach (var pair in hpBeforeBattle)
        {
            if (pair.Key == null) continue;
            pair.Key.currentHP = pair.Value;
            pair.Key.isDead = false;
        }
    }

    // ---- Leaving ----

    private IEnumerator ReturnAfterDelay()
    {
        yield return new WaitForSeconds(returnDelaySeconds);
        ReturnNow();
    }

    /// <summary>
    /// Leave the battle. Public so a Continue button on the result panel can call it when
    /// autoReturn is off.
    /// </summary>
    public void ReturnNow()
    {
        if (leaving) return;
        if (!resultsWritten)
        {
            Debug.LogWarning("[BattleRunner] ReturnNow was called before the battle resolved.");
            return;
        }
        leaving = true;

        bool victory = BattleLauncher.LastWinner == Team.Player;
        DefeatAction defeatAction = encounter != null
            ? encounter.onDefeat
            : DefeatAction.ReturnToOverworld;

        if (!victory && defeatAction == DefeatAction.RetryBattle)
        {
            RestorePreBattleHP();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        if (!victory && defeatAction == DefeatAction.LoadGameOverScene &&
            encounter != null && !string.IsNullOrWhiteSpace(encounter.gameOverSceneName))
        {
            BattleLauncher.ClearPending();
            SceneManager.LoadScene(encounter.gameOverSceneName);
            return;
        }

        // Everything else: back to the overworld.
        BattleLauncher.ClearPending();
        SceneManager.LoadScene(ResolveReturnScene());
    }

    private string ResolveReturnScene()
    {
        if (encounter != null && !string.IsNullOrWhiteSpace(encounter.returnSceneName))
            return encounter.returnSceneName;

        GameData data = GameData.Instance;
        if (data != null && !string.IsNullOrWhiteSpace(data.returnSceneName))
            return data.returnSceneName;

        Debug.LogWarning("[BattleRunner] No return scene recorded — defaulting to OverworldScene.");
        return "OverworldScene";
    }
}
