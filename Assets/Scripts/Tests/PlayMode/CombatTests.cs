using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class CombatTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private GridManager grid;
    private TurnManager turns;
    private Unit player;
    private Unit enemy;
    private CombatController controller;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        grid = Create("Test grid").AddComponent<GridManager>();
        player = CreateUnit("Player", Team.Player, new Vector2Int(1, 1));
        enemy = CreateUnit("Enemy", Team.Enemy, new Vector2Int(2, 1));
        turns = Create("Test turns").AddComponent<TurnManager>();
        controller = Create("Test controller").AddComponent<CombatController>();
        controller.enabled = false; // Drive commands explicitly, without mouse input.

        yield return null;
        yield return null;
        Assert.That(turns.CombatStarted, Is.True);
        Assert.That(turns.CombatOver, Is.False);
        Assert.That(player.IsOnGrid && enemy.IsOnGrid, Is.True);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // Destroy controllers before their dependencies, including after a failed assertion.
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.Destroy(objects[i]);
        objects.Clear();
        yield return null;
    }

    [TestCase(8, 3, 5)]
    [TestCase(5, 5, 1)]
    [TestCase(3, 8, 1)]
    public void Damage_SubtractsDefenseButAlwaysDealsAtLeastOne(int attack, int defense, int expected)
    {
        Assert.That(Unit.ComputeDamage(attack, defense), Is.EqualTo(expected));
    }

    [Test]
    public void TakeDamage_UpdatesHealth_AndNotifiesObserversOnce()
    {
        int notifications = 0;
        player.OnHPChanged += unit => notifications++;
        player.TakeDamage(7);
        Assert.That(player.currentHP, Is.EqualTo(15));
        Assert.That(notifications, Is.EqualTo(1));
    }

    [Test]
    public void Forecast_DoesNotChangeHealth_AndMatchesAttackAndCounter()
    {
        player.attack = 8;
        enemy.defense = 3;
        enemy.attack = 6;
        player.defense = 2;

        AttackForecast forecast = player.PreviewAttack(enemy);

        Assert.That(forecast.isValid, Is.True);
        Assert.That(forecast.targetCounters, Is.True);
        Assert.That(forecast.targetHPAfter, Is.EqualTo(15));
        Assert.That(forecast.attackerHPAfter, Is.EqualTo(16));
        Assert.That(player.currentHP, Is.EqualTo(20));
        Assert.That(enemy.currentHP, Is.EqualTo(20));

        player.Attack(enemy);

        Assert.That(enemy.currentHP, Is.EqualTo(forecast.targetHPAfter));
        Assert.That(player.currentHP, Is.EqualTo(forecast.attackerHPAfter));
    }

    [Test]
    public void LethalAttack_PreventsCounter_FreesCell_AndWinsBattle()
    {
        enemy.currentHP = 2;
        Vector2Int cell = enemy.Cell;
        AttackForecast forecast = player.PreviewAttack(enemy);
        Assert.That(forecast.targetDies, Is.True);
        Assert.That(forecast.targetCounters, Is.False);

        player.Attack(enemy);

        Assert.That(enemy.currentHP, Is.Zero);
        Assert.That(player.currentHP, Is.EqualTo(20));
        Assert.That(grid.GetUnitAt(cell), Is.Null);
        Assert.That(turns.CombatOver, Is.True);
        Assert.That(turns.Winner, Is.EqualTo(Team.Player));
        Assert.That(turns.CanEndPlayerPhase, Is.False);
    }

    [Test]
    public void AttackRange_RejectsFriendlyOutOfRangeAndOffGridTargets()
    {
        enemy.team = Team.Player;
        Assert.That(player.CanAttack(enemy), Is.False);
        enemy.team = Team.Enemy;
        Assert.That(enemy.TryWarpTo(new Vector2Int(5, 1)), Is.True);
        Assert.That(player.PreviewAttack(enemy).isValid, Is.False);
        Assert.That(enemy.TryWarpTo(new Vector2Int(2, 1)), Is.True);
        enemy.RemoveFromGrid();
        Assert.That(player.CanAttack(enemy), Is.False);
    }

    [UnityTest]
    public IEnumerator EndTurn_DuringMovement_IsRejected_ThenWorksAfterArrival()
    {
        Assert.That(enemy.TryWarpTo(new Vector2Int(4, 1)), Is.True);
        InvokeController("Select", player);
        controller.StartCoroutine((IEnumerator)InvokeController("MoveSelectedTo", new Vector2Int(3, 1)));

        AssertEndTurnIsBlocked();
        yield return WaitForAction();

        Assert.That(player.Cell, Is.EqualTo(new Vector2Int(3, 1)));
        Assert.That(grid.GetUnitAt(player.Cell), Is.SameAs(player));
        Assert.That(turns.CurrentPhase, Is.EqualTo(Team.Player));
        Assert.That(turns.CanEndPlayerPhase, Is.True);
        turns.EndPhaseEarly(Team.Player);
        Assert.That(turns.CurrentPhase, Is.EqualTo(Team.Enemy));
    }

    [UnityTest]
    public IEnumerator EndTurn_DuringAttackPause_IsRejected_ThenNormalPhaseFlowResumes()
    {
        InvokeController("Select", player);
        controller.StartCoroutine((IEnumerator)InvokeController("ResolveAttack", player, enemy));

        AssertEndTurnIsBlocked();
        yield return WaitForAction();

        Assert.That(player.HasActed, Is.True);
        Assert.That(turns.CurrentPhase, Is.EqualTo(Team.Enemy));
        turns.EndPhaseEarly(Team.Enemy);
        Assert.That(turns.CurrentPhase, Is.EqualTo(Team.Player));
        Assert.That(turns.CanEndPlayerPhase, Is.True);
    }

    private void AssertEndTurnIsBlocked()
    {
        Assert.That(turns.IsPlayerActionInProgress, Is.True);
        Assert.That(turns.CanEndPlayerPhase, Is.False);
        turns.EndPhaseEarly(Team.Player);
        Assert.That(turns.CurrentPhase, Is.EqualTo(Team.Player));
        Assert.That(player.HasActed, Is.False);
    }

    private IEnumerator WaitForAction()
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (turns.IsPlayerActionInProgress && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(turns.IsPlayerActionInProgress, Is.False, "Action did not finish within five seconds.");
    }

    // Only the input adapter is private. These tests run the real movement/attack
    // coroutines instead of setting the lock manually and testing a boolean in isolation.
    private object InvokeController(string method, params object[] args)
    {
        MethodInfo command = typeof(CombatController).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(command, Is.Not.Null, "Controller command was renamed: " + method);
        return command.Invoke(controller, args);
    }

    private GameObject Create(string name)
    {
        var obj = new GameObject(name);
        objects.Add(obj);
        return obj;
    }

    private Unit CreateUnit(string name, Team team, Vector2Int cell)
    {
        GameObject obj = Create(name);
        obj.transform.position = grid.CellToWorld(cell);
        Unit unit = obj.AddComponent<Unit>();
        unit.unitName = name;
        unit.team = team;
        unit.maxHP = unit.currentHP = 20;
        return unit;
    }
}
