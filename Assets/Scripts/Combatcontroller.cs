using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-side combat input, turn-aware with attack resolution.
///
/// Flow:
///   1. Click a player unit (player phase only) -> movement range highlights (blue).
///   2. Click a reachable tile -> unit slides there.
///   3. If enemies are now in attack range, they highlight (red) and we wait for a target.
///        - Click a highlighted enemy -> attack resolves -> unit's turn ends.
///        - Right-click / click elsewhere -> unit waits -> turn ends.
///   4. If no enemies are in range after moving, the turn ends automatically.
///
/// Once TurnManager reports CombatOver, all input stops and the board is cleared.
/// </summary>
public class CombatController : MonoBehaviour
{
    [Header("Highlights")]
    [Tooltip("Semi-transparent square marking reachable movement tiles (e.g. blue).")]
    public GameObject moveHighlightPrefab;
    [Tooltip("Marker placed on enemies that can be attacked (e.g. red).")]
    public GameObject attackHighlightPrefab;

    [Tooltip("Optional marker for the unit's own cell (the 'stay here' tile, e.g. green). " +
             "Falls back to the move highlight if left empty.")]
    public GameObject stayHighlightPrefab;

    public Camera combatCamera;

    [Tooltip("Optional: assign the camera controller so selecting a unit attaches the camera to it. " +
             "If left empty, it's found automatically on the combat camera.")]
    public CombatCameraController cameraController;

    private enum State { Idle, UnitSelected, AwaitingTarget }
    private State state = State.Idle;

    private Unit selectedUnit;
    private HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
    private readonly List<Unit> targetsInRange = new List<Unit>();
    private readonly List<GameObject> activeHighlights = new List<GameObject>();

    private bool inputLocked;         // true while a unit is animating / resolving
    private bool combatEndHandled;    // ensures the end-of-battle cleanup runs once

    private void Awake()
    {
        if (combatCamera == null) combatCamera = Camera.main;
        if (cameraController == null && combatCamera != null)
            cameraController = combatCamera.GetComponent<CombatCameraController>();
    }

    private void Update()
    {
        if (TurnManager.Instance == null) return;

        // Battle resolved: clean up once, then ignore input permanently.
        if (TurnManager.Instance.CombatOver)
        {
            if (!combatEndHandled) HandleCombatEnd();
            return;
        }

        // TurnManager defers the first phase by a frame so units can finish placing
        // themselves. Until then CurrentPhase is only its default value, so a click in
        // that window would select a unit for a phase that hasn't started.
        if (!TurnManager.Instance.CombatStarted) return;

        if (inputLocked) return;
        if (TurnManager.Instance.CurrentPhase != Team.Player) return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 world = combatCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int cell = GridManager.Instance.WorldToCell(world);
            HandleLeftClick(cell);
        }

        if (Input.GetMouseButtonDown(1))
            HandleRightClick();
    }

    /// <summary>
    /// Runs once when TurnManager reports the battle is over: drop any selection,
    /// clear leftover highlights, and report the result recorded by TurnManager.
    /// </summary>
    private void HandleCombatEnd()
    {
        combatEndHandled = true;
        inputLocked = true;
        Deselect();

        Team? winner = TurnManager.Instance.Winner;
        if (winner == Team.Player)
            Debug.Log("Victory! All enemies defeated.");
        else if (winner == Team.Enemy)
            Debug.Log("Defeat! All player units lost.");
        else
            Debug.Log("Draw — no units remain on either side.");
    }

    private void HandleLeftClick(Vector2Int cell)
    {
        switch (state)
        {
            case State.Idle:
            case State.UnitSelected:
                HandleSelectionClick(cell);
                break;

            case State.AwaitingTarget:
                HandleTargetClick(cell);
                break;
        }
    }

    private void HandleRightClick()
    {
        if (state == State.AwaitingTarget)
        {
            // Right-click while choosing a target == "wait" (no attack).
            FinishUnitTurn();
        }
        else
        {
            Deselect();
        }
    }

    // ---- Selection / movement ----

    private void HandleSelectionClick(Vector2Int cell)
    {
        // If a unit is already selected and the player clicks its own cell,
        // treat that as "stay put" — skip movement and go to the attack/wait step.
        if (state == State.UnitSelected && selectedUnit != null && cell == selectedUnit.Cell)
        {
            StayInPlace();
            return;
        }

        // If a unit is selected and the player clicks an enemy that's already in range,
        // attack it directly from the current position (no move needed).
        if (state == State.UnitSelected && selectedUnit != null)
        {
            Unit clicked = GridManager.Instance.GetUnitAt(cell);
            if (clicked != null && targetsInRange.Contains(clicked))
            {
                StartCoroutine(ResolveAttack(selectedUnit, clicked));
                return;
            }
        }

        Unit unitAtCell = GridManager.Instance.GetUnitAt(cell);

        if (unitAtCell != null && unitAtCell.team == Team.Player && !unitAtCell.HasActed)
        {
            Select(unitAtCell);
            return;
        }

        if (state == State.UnitSelected && reachable.Contains(cell))
        {
            StartCoroutine(MoveSelectedTo(cell));
            return;
        }

        Deselect();
    }

    private void Select(Unit unit)
    {
        ClearHighlights();
        selectedUnit = unit;
        state = State.UnitSelected;
        reachable = GridManager.Instance.GetReachableCells(unit.Cell, unit.moveRange);
        ShowMoveHighlights(reachable);

        // Mark the unit's own cell as a valid "stay here" tile.
        ShowStayHighlight(unit.Cell);

        // If enemies are already in range from the current position, show them as
        // attackable right away so the player can strike without moving first.
        FindTargetsInRange(unit);
        if (targetsInRange.Count > 0)
            ShowAttackHighlights(targetsInRange);

        // Attach the camera to the selected unit so it follows this one.
        if (cameraController != null)
            cameraController.FocusOn(unit);
    }

    private void ShowStayHighlight(Vector2Int cell)
    {
        GameObject prefab = stayHighlightPrefab != null ? stayHighlightPrefab : moveHighlightPrefab;
        if (prefab == null) return;
        Vector3 pos = GridManager.Instance.CellToWorld(cell);
        activeHighlights.Add(Instantiate(prefab, pos, Quaternion.identity));
    }

    private IEnumerator MoveSelectedTo(Vector2Int dest)
    {
        // null == no route; an empty list == already standing there, which is fine.
        List<Vector2Int> path = GridManager.Instance.GetPath(
            selectedUnit.Cell, dest, selectedUnit.moveRange, selectedUnit);
        if (path == null) { Deselect(); yield break; }

        Unit acting = selectedUnit;
        ClearHighlights();
        reachable.Clear();
        inputLocked = true;

        yield return StartCoroutine(acting.MoveAlong(path));

        inputLocked = false;

        AfterArrival(acting);
    }

    /// <summary>Unit stays on its current cell (no movement), then proceeds to attack/wait.</summary>
    private void StayInPlace()
    {
        Unit acting = selectedUnit;
        ClearHighlights();
        reachable.Clear();
        AfterArrival(acting);
    }

    /// <summary>
    /// Shared step once a unit has settled on its cell (whether it moved or stayed):
    /// offer attack targets if any exist, otherwise the turn ends.
    /// </summary>
    private void AfterArrival(Unit acting)
    {
        FindTargetsInRange(acting);
        if (targetsInRange.Count > 0)
        {
            state = State.AwaitingTarget;
            ShowAttackHighlights(targetsInRange);
        }
        else
        {
            FinishUnitTurn();
        }
    }

    // ---- Attacking ----

    private void FindTargetsInRange(Unit attacker)
    {
        targetsInRange.Clear();
        foreach (var enemy in TurnManager.Instance.UnitsOnTeam(Team.Enemy))
            if (attacker.CanAttack(enemy))
                targetsInRange.Add(enemy);
    }

    private void HandleTargetClick(Vector2Int cell)
    {
        Unit clicked = GridManager.Instance.GetUnitAt(cell);
        if (clicked != null && targetsInRange.Contains(clicked))
        {
            StartCoroutine(ResolveAttack(selectedUnit, clicked));
        }
        else
        {
            // Clicked something that isn't a valid target -> treat as wait.
            FinishUnitTurn();
        }
    }

    private IEnumerator ResolveAttack(Unit attacker, Unit target)
    {
        inputLocked = true;
        ClearHighlights();

        string log = attacker.Attack(target);
        Debug.Log(log);

        // If you want battle text on-screen, this is where DialogueManager slots in:
        // if (DialogueManager.Instance != null)
        //     DialogueManager.Instance.ShowDialogue(log.Split('\n'));

        yield return new WaitForSeconds(0.4f);   // brief beat for the exchange

        // The kill may have ended the battle during that pause — if so, stop here and
        // let Update run the end-of-combat cleanup instead of starting another turn.
        if (TurnManager.Instance.CombatOver)
            yield break;

        inputLocked = false;
        FinishUnitTurn();
    }

    private void FinishUnitTurn()
    {
        Unit acting = selectedUnit;
        ClearHighlights();
        targetsInRange.Clear();
        reachable.Clear();
        selectedUnit = null;
        state = State.Idle;

        // If the attacker died to a counter, it has already unregistered and TurnManager
        // has re-evaluated the phase. NotifyUnitActed drops reports for a team that is no
        // longer the current phase, so this stays safe either way.
        if (acting != null)
            TurnManager.Instance.NotifyUnitActed(acting);
    }

    private void Deselect()
    {
        selectedUnit = null;
        state = State.Idle;
        reachable.Clear();
        targetsInRange.Clear();
        ClearHighlights();
    }

    // ---- Highlight rendering ----

    private void ShowMoveHighlights(HashSet<Vector2Int> cells)
    {
        if (moveHighlightPrefab == null) return;
        foreach (var cell in cells)
        {
            Vector3 pos = GridManager.Instance.CellToWorld(cell);
            activeHighlights.Add(Instantiate(moveHighlightPrefab, pos, Quaternion.identity));
        }
    }

    private void ShowAttackHighlights(List<Unit> targets)
    {
        if (attackHighlightPrefab == null) return;
        foreach (var t in targets)
        {
            Vector3 pos = GridManager.Instance.CellToWorld(t.Cell);
            activeHighlights.Add(Instantiate(attackHighlightPrefab, pos, Quaternion.identity));
        }
    }

    private void ClearHighlights()
    {
        foreach (var h in activeHighlights)
            if (h != null) Destroy(h);
        activeHighlights.Clear();
    }
}