// =========================================================================================
// MenuSubtitleFont - applies a crisp, eerie horror subtitle style to "SubtitleLabel".
// =========================================================================================
using Godot;

public static class MenuSubtitleFont
{
	// Point this to your subtitle font (or fall back to title-font.ttf / default monospace)
	private const string FontPath = "res://assets/fonts/subtitle-font.ttf";
	private const int SubtitleSize = 18;
	private static readonly Color SubtitleColor = new(0.82f, 0.85f, 0.82f, 0.92f); // pale bone/ash white
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
			// Use FontVariation so we can add subtle tracking/letter-spacing without altering the asset
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
