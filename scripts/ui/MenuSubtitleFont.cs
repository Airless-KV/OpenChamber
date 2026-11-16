// =========================================================================================
// Original Author : Anj Ferrer (anjivv) - Nov 16, 2026
// Responsibilities: - Applies eerie horror font styling to Main Menu SubtitleLabel
//                   - Font variation letter-spacing tracking, pale ash tint, and outlines
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 16, 2026 | anjivv : Created MenuSubtitleFont styling helper
// - Nov 17, 2026 | anjivv : Polished letter-spacing and font loading
// - Nov 17, 2026 | air    : Standardized header and relocated to scripts/ui/
// =========================================================================================
using Godot;

public static class MenuSubtitleFont
{
    private const string FontPath = "res://assets/fonts/subtitle-font.ttf";
    private const int SubtitleSize = 18;
    private static readonly Color SubtitleColor = new(0.82f, 0.85f, 0.82f, 0.92f);
    private static readonly Color OutlineColor = new(0.02f, 0.02f, 0.02f, 0.95f);
    private const int OutlineSize = 4;

    public static void Apply(Node menu)
    {
        var subtitle = menu.FindChild("SubtitleLabel", true, false) as Label;
        if (subtitle == null)
        {
            GD.PushWarning("MenuSubtitleFont: no node named SubtitleLabel found.");
            return;
        }

        if (ResourceLoader.Exists(FontPath))
        {
            var baseFont = GD.Load<Font>(FontPath);
            var variation = new FontVariation
            {
                BaseFont = baseFont,
                SpacingGlyph = 3
            };
            subtitle.AddThemeFontOverride("font", variation);
        }
        else
        {
            GD.PushWarning($"MenuSubtitleFont: font not found at {FontPath}, applying color/outline styling only.");
        }

        subtitle.AddThemeFontSizeOverride("font_size", SubtitleSize);
        subtitle.AddThemeColorOverride("font_color", SubtitleColor);
        subtitle.AddThemeColorOverride("font_outline_color", OutlineColor);
        subtitle.AddThemeConstantOverride("outline_size", OutlineSize);
        subtitle.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.85f));
        subtitle.AddThemeConstantOverride("shadow_offset_x", 2);
        subtitle.AddThemeConstantOverride("shadow_offset_y", 2);
        GD.Print("MenuSubtitleFont: applied");
    }
}
