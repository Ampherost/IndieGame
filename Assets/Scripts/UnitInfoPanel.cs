using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stat readout for whichever unit the player is hovering or has selected.
/// CombatController drives it: Show(unit) / Hide().
///
/// Every field is optional. Assign the ones you've built and leave the rest empty —
/// a half-built panel shows what it can instead of throwing.
/// </summary>
public class UnitInfoPanel : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("The panel GameObject toggled on and off. Defaults to this GameObject.")]
    public GameObject panel;

    [Header("Text")]
    public TMP_Text nameText;
    public TMP_Text teamText;
    public TMP_Text hpText;
    public TMP_Text attackText;
    public TMP_Text defenseText;
    public TMP_Text moveText;
    public TMP_Text rangeText;
    [Tooltip("Optional. Shows 'Done' once the unit has acted this turn.")]
    public TMP_Text statusText;

    [Header("HP Bar (optional)")]
    [Tooltip("An Image with Image Type = Filled, Fill Method = Horizontal.")]
    public Image hpFill;

    [Header("Colours")]
    public Color playerColor = new Color(0.30f, 0.75f, 1.00f);
    public Color enemyColor = new Color(0.95f, 0.35f, 0.35f);

    private Unit shown;

    private void Awake()
    {
        if (panel == null) panel = gameObject;
        panel.SetActive(false);
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>Display a unit's stats. Passing null hides the panel.</summary>
    public void Show(Unit unit)
    {
        if (unit == null) { Hide(); return; }

        if (shown != unit)
        {
            Unsubscribe();
            shown = unit;
            shown.OnHPChanged += HandleHPChanged;
        }

        if (panel != null) panel.SetActive(true);
        Redraw();
    }

    public void Hide()
    {
        Unsubscribe();
        shown = null;
        if (panel != null) panel.SetActive(false);
    }

    /// <summary>Re-read the currently shown unit (call after anything that changes stats).</summary>
    public void Redraw()
    {
        if (shown == null) return;

        Color teamColor = shown.team == Team.Player ? playerColor : enemyColor;

        if (nameText != null)
        {
            nameText.text = shown.unitName;
            nameText.color = teamColor;
        }
        if (teamText != null) teamText.text = shown.team == Team.Player ? "Ally" : "Enemy";
        if (hpText != null) hpText.text = $"HP  {shown.currentHP}/{shown.maxHP}";
        if (attackText != null) attackText.text = $"ATK  {shown.attack}";
        if (defenseText != null) defenseText.text = $"DEF  {shown.defense}";
        if (moveText != null) moveText.text = $"MOV  {shown.moveRange}";
        if (rangeText != null) rangeText.text = $"RNG  {shown.attackRange}";
        if (statusText != null) statusText.text = shown.HasActed ? "Done" : string.Empty;

        if (hpFill != null)
        {
            hpFill.fillAmount = shown.HPFraction;
            hpFill.color = teamColor;
        }
    }

    private void HandleHPChanged(Unit u) => Redraw();

    private void Unsubscribe()
    {
        if (shown != null) shown.OnHPChanged -= HandleHPChanged;
    }
}
