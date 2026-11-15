// =========================================================================================
// MenuTitleFont - applies a horror font to the main menu title (the "OPEN CHAMBER" label).
// =========================================================================================
using Godot;

public static class MenuTitleFont
{
	private const string FontPath = "res://assets/fonts/title-font.ttf";
	private const int TitleSize = 68;
	private static readonly Color TitleColor = new(0.86f, 0.12f, 0.12f);   // blood red
	private static readonly Color OutlineColor = new(0.04f, 0.0f, 0.0f);
	private const int OutlineSize = 8;                                      // dark outline keeps it readable over the feed

	public static void Apply(Node menu)
	{
		var title = menu.FindChild("TitleLabel", true, false) as Label;
		if (title == null)
		{
			GD.PushWarning("MenuTitleFont: no node named TitleLabel found.");
			return;
		}
		if (!ResourceLoader.Exists(FontPath))
		{
			GD.PushWarning($"MenuTitleFont: font not found at {FontPath}");
			return;
		}

		var font = GD.Load<Font>(FontPath);
		title.AddThemeFontOverride("font", font);
		title.AddThemeFontSizeOverride("font_size", TitleSize);
		title.AddThemeColorOverride("font_color", TitleColor);
		title.AddThemeColorOverride("font_outline_color", OutlineColor);
		title.AddThemeConstantOverride("outline_size", OutlineSize);
		title.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
		title.AddThemeConstantOverride("shadow_offset_x", 4);
		title.AddThemeConstantOverride("shadow_offset_y", 4);
		GD.Print("MenuTitleFont: applied");
	}
}
