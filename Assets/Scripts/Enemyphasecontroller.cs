using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal enemy-phase driver so the turn loop completes.
/// For now every enemy simply "waits". Phase 6 replaces the body of
/// TakeEnemyTurn with real AI (move toward nearest player, attack if in range).
/// </summary>
public class EnemyPhaseController : MonoBehaviour
{
    [Tooltip("Seconds between each enemy's action, so the phase is visible.")]
    public float actionDelay = 0.3f;

    private void OnEnable()
    {
        StartCoroutine(WaitForTurnManager());
    }

    private IEnumerator WaitForTurnManager()
    {
        // TurnManager sets up in Start(); wait a frame so its instance exists.
        yield return null;
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnPhaseStart += HandlePhaseStart;
    }

    private void OnDisable()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnPhaseStart -= HandlePhaseStart;
    }

    private void HandlePhaseStart(Team team)
    {
        if (team == Team.Enemy)
            StartCoroutine(RunEnemyPhase());
    }

    private IEnumerator RunEnemyPhase()
    {
        // Snapshot the list so removals during iteration are safe.
        var enemies = new List<Unit>(TurnManager.Instance.UnitsOnTeam(Team.Enemy));

        foreach (var enemy in enemies)
        {
            if (enemy == null || !enemy.IsAlive) continue;

            yield return StartCoroutine(TakeEnemyTurn(enemy));
            yield return new WaitForSeconds(actionDelay);

            TurnManager.Instance.NotifyUnitActed(enemy);
        }
    }

    // ---- Replace this in Phase 6 with real behavior ----
    private IEnumerator TakeEnemyTurn(Unit enemy)
    {
        Debug.Log($"{enemy.unitName} waits.");
        yield break;
    }
}