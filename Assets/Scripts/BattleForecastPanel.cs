using TMPro;
using UnityEngine;

/// <summary>
/// The pre-attack forecast: damage dealt, HP before and after on both sides, and whether
/// the defender counters.
///
/// It renders an AttackForecast and nothing else — it does no math of its own, so it can
/// never disagree with the attack that follows.
/// </summary>
public class BattleForecastPanel : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("The panel GameObject toggled on and off. Defaults to this GameObject.")]
    public GameObject panel;

    [Header("Attacker side")]
    public TMP_Text attackerNameText;
    public TMP_Text attackerHPText;
    [Tooltip("Damage the attacker deals.")]
    public TMP_Text attackerDamageText;

    [Header("Target side")]
    public TMP_Text targetNameText;
    public TMP_Text targetHPText;
    [Tooltip("Damage the counter deals, or a dash when there isn't one.")]
    public TMP_Text targetDamageText;

    [Header("Summary")]
    [Tooltip("Optional. 'Counters for 4' / 'No counter' / 'Defeats target'.")]
    public TMP_Text counterText;

    [Header("Colours")]
    public Color normalColor = Color.white;
    [Tooltip("Used on an HP line that reaches zero.")]
    public Color lethalColor = new Color(1f, 0.35f, 0.35f);

    private void Awake()
    {
        if (panel == null) panel = gameObject;
        panel.SetActive(false);
    }

    /// <summary>Render a forecast. An invalid forecast hides the panel.</summary>
    public void Show(AttackForecast f)
    {
        if (!f.isValid || f.attacker == null || f.target == null) { Hide(); return; }

        if (panel != null) panel.SetActive(true);

        SetText(attackerNameText, f.attacker.unitName, normalColor);
        SetText(attackerHPText, f.AttackerHPText, f.attackerDies ? lethalColor : normalColor);
        SetText(attackerDamageText, f.damage.ToString(), normalColor);

        SetText(targetNameText, f.target.unitName, normalColor);
        SetText(targetHPText, f.TargetHPText, f.targetDies ? lethalColor : normalColor);
        SetText(targetDamageText, f.targetCounters ? f.counterDamage.ToString() : "—", normalColor);

        if (counterText != null)
        {
            counterText.text = f.targetDies ? $"Defeats {f.target.unitName}" : f.CounterText;
            counterText.color = f.attackerDies ? lethalColor : normalColor;
        }
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void SetText(TMP_Text field, string value, Color color)
    {
        if (field == null) return;
        field.text = value;
        field.color = color;
    }
}
