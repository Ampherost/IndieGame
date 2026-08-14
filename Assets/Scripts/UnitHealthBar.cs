using UnityEngine;

/// <summary>
/// A small HP bar floating above a unit.
///
/// Deliberately builds its own sprites at runtime from a 1x1 white texture, so there's
/// no prefab to author and no Canvas to wire — drop the component on a unit (or let
/// CombatHUD attach one to every unit) and it works. Swap in real art later by assigning
/// 'barSprite'.
///
/// Redraws on Unit.OnHPChanged rather than every frame.
/// </summary>
[RequireComponent(typeof(Unit))]
public class UnitHealthBar : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("Bar size in world units.")]
    public Vector2 size = new Vector2(0.8f, 0.1f);

    [Tooltip("Height above the unit's origin.")]
    public float yOffset = 0.55f;

    [Tooltip("Thickness of the dark border, in world units.")]
    public float border = 0.025f;

    [Header("Colours")]
    public Color playerColor = new Color(0.30f, 0.75f, 1.00f);
    public Color enemyColor = new Color(0.95f, 0.35f, 0.35f);
    public Color lowHealthColor = new Color(1.00f, 0.80f, 0.20f);
    [Tooltip("Below this fraction the bar switches to lowHealthColor. 0 disables it.")]
    [Range(0f, 1f)] public float lowHealthThreshold = 0.34f;
    public Color backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.9f);

    [Header("Behaviour")]
    [Tooltip("Hide the bar while the unit is at full HP.")]
    public bool hideWhenFull = false;

    [Tooltip("Drawn this many sorting orders above the unit's own sprite.")]
    public int sortingOrderBoost = 20;

    [Tooltip("Optional. Leave empty to generate a plain white 1x1 sprite.")]
    public Sprite barSprite;

    private Unit unit;
    private Transform root;
    private SpriteRenderer backRenderer;
    private SpriteRenderer fillRenderer;

    private static Sprite generatedSprite;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        Build();
    }

    private void OnEnable()
    {
        if (unit != null) unit.OnHPChanged += HandleHPChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (unit != null) unit.OnHPChanged -= HandleHPChanged;
    }

    private void HandleHPChanged(Unit u) => Refresh();

    /// <summary>Add a bar to a unit that doesn't have one. Safe to call twice.</summary>
    public static UnitHealthBar AttachTo(Unit unit)
    {
        if (unit == null) return null;
        UnitHealthBar existing = unit.GetComponent<UnitHealthBar>();
        return existing != null ? existing : unit.gameObject.AddComponent<UnitHealthBar>();
    }

    private void Build()
    {
        Sprite sprite = barSprite != null ? barSprite : WhiteSprite();

        var rootGO = new GameObject("HealthBar");
        root = rootGO.transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, yOffset, 0f);

        backRenderer = NewPiece("Back", sprite, backgroundColor);
        backRenderer.transform.localScale = new Vector3(size.x + border * 2f, size.y + border * 2f, 1f);

        fillRenderer = NewPiece("Fill", sprite, playerColor);

        int baseOrder = 0;
        SpriteRenderer own = GetComponent<SpriteRenderer>();
        if (own != null)
        {
            baseOrder = own.sortingOrder;
            backRenderer.sortingLayerID = own.sortingLayerID;
            fillRenderer.sortingLayerID = own.sortingLayerID;
        }
        backRenderer.sortingOrder = baseOrder + sortingOrderBoost;
        fillRenderer.sortingOrder = baseOrder + sortingOrderBoost + 1;
    }

    private SpriteRenderer NewPiece(string pieceName, Sprite sprite, Color color)
    {
        var go = new GameObject(pieceName);
        go.transform.SetParent(root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        return sr;
    }

    /// <summary>Redraw from the unit's current HP.</summary>
    public void Refresh()
    {
        if (unit == null || root == null) return;

        float fraction = unit.HPFraction;

        bool visible = unit.IsAlive && !(hideWhenFull && fraction >= 1f);
        root.gameObject.SetActive(visible);
        if (!visible) return;

        // Grow from the left: shrink the width, then shift left by half of what was lost.
        fillRenderer.transform.localScale = new Vector3(size.x * fraction, size.y, 1f);
        fillRenderer.transform.localPosition =
            new Vector3(-size.x * 0.5f * (1f - fraction), 0f, 0f);

        Color teamColor = unit.team == Team.Player ? playerColor : enemyColor;
        fillRenderer.color = (lowHealthThreshold > 0f && fraction <= lowHealthThreshold)
            ? lowHealthColor
            : teamColor;
    }

    /// <summary>One shared 1x1 white sprite for every bar in the scene.</summary>
    private static Sprite WhiteSprite()
    {
        if (generatedSprite != null) return generatedSprite;

        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "UnitHealthBar_White"
        };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();

        // pixelsPerUnit = 1 makes the sprite exactly one world unit square, so localScale
        // reads directly as the bar's size in world units.
        generatedSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        generatedSprite.name = "UnitHealthBar_White";
        return generatedSprite;
    }
}
