using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One party member's *mutable* state: which unit they are, and how hurt they currently
/// are. Plain [Serializable] class so it survives scene loads inside GameData without any
/// save/load plumbing.
/// </summary>
[Serializable]
public class PartyMember
{
    public UnitDefinition definition;

    [Tooltip("HP carried between battles. -1 means 'not initialised yet' — it gets filled " +
             "from the definition's maxHP the first time the party is built.")]
    public int currentHP = -1;

    [Tooltip("Untick to bench someone without removing them from the roster.")]
    public bool inActiveParty = true;

    [Tooltip("Set when a unit falls and permadeath is on. Dead members are never deployed.")]
    public bool isDead;

    public string Name => definition != null ? definition.unitName : "(missing definition)";
    public int MaxHP => definition != null ? Mathf.Max(1, definition.maxHP) : 1;

    /// <summary>Can this member be put on the board right now?</summary>
    public bool IsDeployable =>
        !isDead && inActiveParty && definition != null && definition.IsSpawnable && currentHP > 0;

    /// <summary>Fill in HP the first time we see this member.</summary>
    public void EnsureInitialised()
    {
        if (currentHP < 0) currentHP = MaxHP;
        currentHP = Mathf.Clamp(currentHP, 0, MaxHP);
    }

    public void FullHeal()
    {
        if (isDead) return;
        currentHP = MaxHP;
    }
}

/// <summary>
/// The one object that outlives scene loads. Holds the roster, each member's current HP,
/// and where to put the player when a battle sends them back to the overworld.
///
/// SETUP: make a prefab with this component, fill in the starting roster, and drop a copy
/// into every scene. The first one to wake up claims Instance and marks itself
/// DontDestroyOnLoad; every later copy destroys itself immediately, so you never end up
/// with two rosters.
///
/// Deliberately runs very early (-500) so anything that reads the party in its own Awake
/// finds a built roster rather than an empty list.
/// </summary>
[DefaultExecutionOrder(-500)]
public class GameData : MonoBehaviour
{
    public static GameData Instance { get; private set; }

    [Header("Starting Roster")]
    [Tooltip("Who the player starts the game with. Built into live PartyMembers on first boot.")]
    public List<UnitDefinition> startingRoster = new List<UnitDefinition>();

    [Header("Between-Battle Rules")]
    [Tooltip("Heal everyone to full after every victory. Turn off for a more attritional game.")]
    public bool healAfterBattle = false;

    [Tooltip("A unit that falls in a won battle is gone for good. Ignored on a defeat, so a " +
             "total party wipe is a retreat rather than the end of the game.")]
    public bool permadeath = false;

    [Tooltip("HP a fallen unit comes back with when permadeath is off.")]
    [Min(1)] public int reviveHP = 1;

    // ---- Return point (set when a battle launches, consumed when we come back) ----
    [HideInInspector] public string returnSceneName;
    [HideInInspector] public Vector3 returnPosition;
    [HideInInspector] public bool hasReturnPoint;

    private readonly List<PartyMember> party = new List<PartyMember>();

    /// <summary>The live party. Order is deployment order.</summary>
    public IReadOnlyList<PartyMember> Party => party;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A second copy in a later scene. The original already holds the real roster.
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPartyFromRoster();
    }

    /// <summary>
    /// Turn the inspector roster into live PartyMembers. Runs once — later scene loads find
    /// the party already populated and leave it alone.
    /// </summary>
    private void BuildPartyFromRoster()
    {
        if (party.Count > 0) return;

        foreach (var def in startingRoster)
        {
            if (def == null) continue;

            if (!def.IsSpawnable)
                Debug.LogWarning($"[GameData] Roster entry '{def.name}' has no prefab (or the " +
                                 $"prefab has no Unit component). It can't be deployed.", this);

            var member = new PartyMember { definition = def };
            member.EnsureInitialised();
            party.Add(member);
        }

        if (party.Count == 0)
            Debug.LogWarning("[GameData] Starting roster is empty — no player units will spawn " +
                             "in combat. Assign some Unit Definitions.", this);
    }

    // ---- Roster helpers ----

    public PartyMember Find(UnitDefinition definition)
    {
        foreach (var m in party)
            if (m.definition == definition) return m;
        return null;
    }

    /// <summary>Recruit someone mid-game (an NPC joining after a conversation, say).</summary>
    public PartyMember Recruit(UnitDefinition definition)
    {
        if (definition == null) return null;

        PartyMember existing = Find(definition);
        if (existing != null) return existing;

        var member = new PartyMember { definition = definition };
        member.EnsureInitialised();
        party.Add(member);
        Debug.Log($"[GameData] {member.Name} joined the party.");
        return member;
    }

    public void HealAll()
    {
        foreach (var m in party) m.FullHeal();
    }

    /// <summary>True if nobody can be put on the board — usually a game over.</summary>
    public bool PartyIsWipedOut()
    {
        foreach (var m in party)
            if (m.IsDeployable) return false;
        return true;
    }

    // ---- Return point ----

    public void SetReturnPoint(string sceneName, Vector3 position)
    {
        returnSceneName = sceneName;
        returnPosition = position;
        hasReturnPoint = true;
    }

    public void ClearReturnPoint() => hasReturnPoint = false;
}
