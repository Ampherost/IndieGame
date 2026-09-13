# Gameplay tests

## Run in Unity

1. Let Unity finish importing and compiling the scripts.
2. Open **Window > General > Test Runner**.
3. Select **PlayMode**, then **Run All**.
4. Expect nine test cases under `CombatTests`. Select a failure to read its assertion and stack trace.

The tests create a grid, two units, a turn manager, and an input controller in the
test runner's scene. They need no game scene, prefabs, mouse input, or Inspector wiring.
Each test destroys its objects afterward. Do not enter Play Mode manually first.

## First coverage

- Damage subtracts defense, with a minimum of one damage.
- Forecasts do not change HP and match the resolved attack and counterattack.
- Lethal damage prevents counters, frees the occupied tile, and ends the battle.
- Friendly, distant, and off-grid targets cannot be attacked.
- End Turn is rejected during actual movement and attack coroutines; normal turn
  progression resumes after the action completes.

The last two tests also check the same `CanEndPlayerPhase` property used by the HUD.
They do not simulate a UI click or verify the button's scene wiring.

## Adding the next test

Name tests for player-visible behavior: `Retry_RestoresPreBattleHP`, for example.
Arrange a small scenario, perform a real action, and assert its observable outcome.
For a bug fix, first reproduce the bug with a failing test, then fix it and rerun.
Use `[Test]` for synchronous behavior and `[UnityTest]` with `IEnumerator` when
movement, frames, or coroutines matter. Keep waits bounded so bugs cannot hang the suite.

Useful next coverage: blocked pathfinding, retry HP restoration, and persistent
encounter completion. Add scene integration tests separately to check prefab and UI wiring.

## Assembly setup

`Verdaneth.Gameplay.asmdef` makes gameplay code referenceable by the test assembly.
Its references cover Input System, TextMesh Pro, and Unity UI. Existing script GUIDs
are unchanged. The nested Play Mode assembly is marked `TestAssemblies`, keeping
tests and NUnit out of normal player builds. No package installation is required.

The controller tests use reflection only to dispatch its private input commands.
They never set the action lock or skip the real action. If commands are renamed,
update the test helper calls; a future public command API could remove this reflection.
