// =========================================================================================
// Original Author : air (Airless-KV) - Nov 15, 2026
// Responsibilities: - Lightweight non-intrusive on-screen FPS and frame time counter
//                   - Real-time performance metric display for Intel HD 620 optimization
//                   - Isolated CanvasLayer overlay (zero HUDManager dependency)
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Created standalone FpsOverlay CanvasLayer for performance monitoring
// - Nov 15, 2026 | air : Fixed overlay viewport anchoring to guarantee top-right screen visibility
// =========================================================================================
using Godot;

public partial class FpsOverlay : CanvasLayer
{
	public static FpsOverlay Instance { get; private set; }

	private Label _label;
	private PanelContainer _panel;
	private double _timer;
	private const double UpdateInterval = 0.25;

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}
		Instance = this;
		Layer = 125;
		ProcessMode = ProcessModeEnum.Always;

		BuildUI();
		Visible = SaveManager.CurrentData?.Audio?.ShowFpsCounter ?? false;
	}

	public static void EnsureInstance(SceneTree tree)
	{
		if (Instance != null || tree?.Root == null) return;
		var overlay = new FpsOverlay();
		overlay.Name = "FpsOverlay";
		tree.Root.CallDeferred(Node.MethodName.AddChild, overlay);
	}

	private void BuildUI()
	{
		_panel = new PanelContainer();
		_panel.MouseFilter = Control.MouseFilterEnum.Ignore;

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0, 0, 0, 0.65f);
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		_panel.AddThemeStyleboxOverride("panel", style);

		_label = new Label();
		_label.MouseFilter = Control.MouseFilterEnum.Ignore;
		_label.Text = "FPS: --";
		_label.AddThemeFontSizeOverride("font_size", 13);
		_label.AddThemeColorOverride("font_color", new Color(0.35f, 1.0f, 0.45f));

		_panel.AddChild(_label);
		AddChild(_panel);
		UpdatePosition();
	}

	public void SetOverlayVisible(bool show)
	{
		Visible = show;
		if (show) UpdatePosition();
	}

	public override void _Process(double delta)
	{
		if (!Visible || _label == null) return;

		UpdatePosition();

		_timer += delta;
		if (_timer >= UpdateInterval)
		{
			_timer = 0.0;
			double fps = Engine.GetFramesPerSecond();
			double ms = fps > 0 ? (1000.0 / fps) : 0.0;
			_label.Text = $"FPS: {Mathf.RoundToInt(fps)} ({ms:F1} ms)";

			if (fps >= 50.0)
				_label.AddThemeColorOverride("font_color", new Color(0.35f, 1.0f, 0.45f));
			else if (fps >= 28.0)
				_label.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.25f));
			else
				_label.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
		}
	}

	private void UpdatePosition()
	{
		if (_panel == null) return;
		Vector2 vpSize = GetViewport()?.GetVisibleRect().Size ?? DisplayServer.WindowGetSize();
		if (vpSize.X > 0)
		{
			float panelW = _panel.Size.X > 0 ? _panel.Size.X : 140.0f;
			_panel.Position = new Vector2(vpSize.X - panelW - 16, 16);
		}
	}
}
