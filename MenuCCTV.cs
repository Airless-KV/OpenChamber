// =========================================================================================
// MenuCCTV (v4) - the main menu plays on the in-game CCTV monitor.
// =========================================================================================
using System.Collections.Generic;
using Godot;

public partial class MenuCCTV : Control
{
	// ---- Tweak these ----
	private const string OverlayScenePath = "res://scenes/security/cctv_overlay.tscn";
	private const string CamImageFolder = "res://assets/ui/cctv/";
	private static readonly string[] CamImageNames = { "cam1", "cam2", "cam3", "cam4" };
	private static readonly string[] Extensions = { "png", "jpg", "jpeg", "webp" };

	private const float ScanlineIntensity = 0.07f;   // strength of the scanline pattern over the screen (your in-game overlay uses 0.16)
	private const float PanZoom = 1.30f;         // 1.30 = image is 30% wider than the screen; that extra width is what the camera sweeps across
	private const float SweepSeconds = 9f;        // time to sweep from one end to the other
	private const float PauseSeconds = 1.6f;      // pause at each end, like a real CCTV motor
	private const float CamSwitchMin = 16f, CamSwitchMax = 24f;   // seconds between camera switches

	// (To tone the green down: brightness 0.9, tint vec3(0.85, 1.0, 0.90), desaturate 0.90.)
	private const string FeedShader = @"
shader_type canvas_item;

uniform float brightness = 1.0;
uniform vec3 tint = vec3(0.85, 1.0, 0.90);
uniform float desaturate = 0.90;
uniform float glitch = 0.0;

float hash(float n) { return fract(sin(n) * 43758.5453); }

void fragment() {
	vec2 uv = UV;
	uv.x += (hash(floor(uv.y * 36.0) + floor(TIME * 20.0)) - 0.5) * 0.07 * glitch;
	vec3 c = texture(TEXTURE, uv).rgb;
	float l = dot(c, vec3(0.299, 0.587, 0.114));
	c = mix(c, vec3(l), desaturate) * tint * brightness;
	COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
}
";

	// Additive light: faint static, a slow rolling band, and signal dropout bursts
	private const string LightShader = @"
shader_type canvas_item;
render_mode blend_add;

uniform float noise_amt = 0.02;     // screen-wide static grain (was 0.05)
uniform float band_amt = 0.035;     // brightness of the rolling band
uniform float roll_speed = 0.08;    // band speed; make it negative to roll the other way
uniform float dropout = 0.0;
uniform vec3 tint = vec3(0.35, 0.90, 0.50);

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
	vec2 uv = UV;
	float t = TIME;
	float n = hash(floor(uv * vec2(260.0, 150.0)) + floor(t * 30.0));
	float roll = fract(uv.y - t * roll_speed);
	float band = smoothstep(0.0, 0.02, roll) * (1.0 - smoothstep(0.02, 0.10, roll));
	float row = hash(vec2(floor(uv.y * 40.0), floor(t * 20.0)));
	float burst = dropout * (0.35 + 0.65 * step(0.55, row)) * n;
	float v = n * noise_amt + band * band_amt + burst * 0.55;
	COLOR = vec4(tint * v, 1.0);
}
";

	private struct Tree { public float X, R; public bool Far; }

	private Control _menu;
	private bool _attached;          // true when created by MenuCCTV.Attach(); false when placed in the scene
	private CCTVOverlay _overlay;
	private ShaderMaterial _lightMat, _feedMat;
	private TextureRect _feed;
	private readonly Texture2D[] _camTex = new Texture2D[4];

	// fallback drawn yard
	private readonly List<Tree> _trees = new();
	private readonly List<float> _posts = new();
	private readonly RandomNumberGenerator _rng = new();
	private float _houseX;

	private int _camIndex;
	private float _time;
	private float _panTime;
	private float _camTimer;
	private float _dropout;
	private float _signalTimer;

	public static MenuCCTV Attach(Control menu)
	{
		var c = new MenuCCTV { _menu = menu, _attached = true };
		menu.AddChild(c);
		// Above the flat Background panel, below the Vignette, title and buttons.
		menu.MoveChild(c, Mathf.Min(1, menu.GetChildCount() - 1));
		c.AddOverlay();
		return c;
	}

	public override void _Ready()
	{
		GD.Print("MenuCCTV: ready");
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;   // never block menu clicks

		// Works both when added by code (Attach) and when this script sits on a node in the scene.
		_menu ??= GetParent() as Control;
		if (_menu != null)
		{
			// The old flat "Background" panel is opaque and would hide the feed, so turn it off.
			var bg = _menu.GetNodeOrNull<CanvasItem>("Background");
			if (bg != null) bg.Visible = false;
		}
		if (!_attached) Callable.From(AddOverlay).CallDeferred();   // scene-placed: add the overlay ourselves

		LoadCameraImages();

		// Layer order (back to front): feed picture -> static/light
		_feedMat = new ShaderMaterial { Shader = new Shader { Code = FeedShader } };
		_feed = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
			Material = _feedMat,
			TextureFilter = TextureFilterEnum.Linear,
		};
		AddChild(_feed);

		_lightMat = new ShaderMaterial { Shader = new Shader { Code = LightShader } };
		var light = new ColorRect { Color = Colors.White, MouseFilter = MouseFilterEnum.Ignore, Material = _lightMat };
		AddChild(light);
		light.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		ShowCamera(_camIndex);
		_camTimer = (float)GD.RandRange(CamSwitchMin, CamSwitchMax);
		_dropout = 1f;   // start with a burst of static, like the signal just connected
	}

	private void LoadCameraImages()
	{
		int found = 0;
		for (int i = 0; i < _camTex.Length; i++)
		{
			foreach (string ext in Extensions)
			{
				string path = $"{CamImageFolder}{CamImageNames[i]}.{ext}";
				if (!ResourceLoader.Exists(path)) continue;
				_camTex[i] = GD.Load<Texture2D>(path);
				found++;
				break;
			}
		}
		GD.Print($"MenuCCTV: loaded {found}/{_camTex.Length} camera images from {CamImageFolder}");
	}

	// Adds your existing CCTV overlay on top of the menu (scanlines + on-screen display).
	private void AddOverlay()
	{
		if (_menu == null || _overlay != null) return;
		var scene = GD.Load<PackedScene>(OverlayScenePath);
		_overlay = scene?.Instantiate() as CCTVOverlay;
		if (_overlay == null)
		{
			GD.PushWarning($"MenuCCTV: could not load {OverlayScenePath} as a CCTVOverlay.");
			return;
		}
		_menu.AddChild(_overlay);

		// The overlay hides itself in _Ready, so show it afterwards.
		Callable.From(() =>
		{
			_overlay.ShowOverlay(_camIndex);
			// Lighten the scanline pattern on the menu only. The material is copied first so the
			// in-game CCTV overlay (which shares the same resource) keeps its normal strength.
			if (_overlay.ScanlinesRect?.Material is ShaderMaterial sm)
			{
				var copy = (ShaderMaterial)sm.Duplicate();
				copy.SetShaderParameter("scanline_intensity", ScanlineIntensity);
				_overlay.ScanlinesRect.Material = copy;
			}
			// Those two hints are for the in-game desk ("switch camera / exit desk"), not the menu.
			if (_overlay.ControlsLabel != null) _overlay.ControlsLabel.Visible = false;
			// Top-left camera name ("CAM 01 - NORTH PERIMETER") hidden on the menu.
			if (_overlay.ChannelLabel != null) _overlay.ChannelLabel.Visible = false;
			if (_overlay.ExitLabel != null) _overlay.ExitLabel.Visible = false;
		}).CallDeferred();
	}

	// Switches to a camera: its picture (or the drawn yard if it has none), and restarts its sweep from the left.
	private void ShowCamera(int idx)
	{
		Texture2D tex = idx >= 0 && idx < _camTex.Length ? _camTex[idx] : null;
		_feed.Texture = tex;
		_feed.Visible = tex != null;
		_panTime = 0f;
		BuildScene(idx);
	}

	// ------------------------------------------------------------------ camera sweep
	// 0 = far left, 1 = far right. Left->right, pause, right->left, pause. Gentle ease at the ends.
	private static float PanAmount(float t)
	{
		float cycle = 2f * (SweepSeconds + PauseSeconds);
		float u = t % cycle;
		if (u < SweepSeconds) return Ease(u / SweepSeconds);
		u -= SweepSeconds;
		if (u < PauseSeconds) return 1f;
		u -= PauseSeconds;
		if (u < SweepSeconds) return 1f - Ease(u / SweepSeconds);
		return 0f;
	}

	// Mostly constant speed (like a motorised camera) with a soft start and stop.
	private static float Ease(float x) => Mathf.Lerp(x, Mathf.SmoothStep(0f, 1f, x), 0.5f);

	// ------------------------------------------------------------------ fallback scene data
	private void BuildScene(int seed)
	{
		_rng.Seed = (ulong)(9001 + seed * 131);
		_trees.Clear();
		for (int layer = 0; layer < 2; layer++)
			for (int i = 0; i < 16; i++)
				_trees.Add(new Tree
				{
					X = (i + _rng.Randf() * 0.9f) / 16f * 1.08f - 0.04f,
					R = (layer == 0 ? 0.028f : 0.045f) + _rng.Randf() * 0.035f,
					Far = layer == 0,
				});

		_houseX = _rng.Randf() < 0.5f ? 0.10f + _rng.Randf() * 0.18f : 0.72f + _rng.Randf() * 0.18f;

		_posts.Clear();
		for (float x = -0.02f; x < 1.05f; x += 0.075f) _posts.Add(x);
	}

	// ------------------------------------------------------------------ per-frame
	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_time += dt;
		_panTime += dt;

		// Picture position: sweep across the extra width, with a tiny motor vibration
		if (_feed.Visible)
		{
			Vector2 s = Size;
			float extraX = s.X * (PanZoom - 1f);
			float extraY = s.Y * (PanZoom - 1f);
			_feed.Size = new Vector2(s.X * PanZoom, s.Y * PanZoom);
			_feed.Position = new Vector2(
				-extraX * PanAmount(_panTime) + Mathf.Sin(_time * 53f) * 0.6f,
				-extraY * 0.5f + Mathf.Sin(_time * 0.7f) * extraY * 0.06f);
		}

		// Signal dropout decays; static and picture tearing follow it
		_dropout = Mathf.MoveToward(_dropout, 0f, dt * 2.2f);
		_lightMat.SetShaderParameter("dropout", _dropout);
		_feedMat.SetShaderParameter("glitch", _dropout);

		// Signal readout drops during glitches
		_signalTimer -= dt;
		if (_signalTimer <= 0f && _overlay?.StatusLabel != null)
		{
			_signalTimer = 0.5f;
			int pct = _dropout > 0.2f ? GD.RandRange(38, 70) : GD.RandRange(95, 99);
			_overlay.StatusLabel.Text = $"SIGNAL: {pct}% | 30 FPS";
		}

		// Camera switch
		_camTimer -= dt;
		if (_camTimer <= 0f)
		{
			_camTimer = (float)GD.RandRange(CamSwitchMin, CamSwitchMax);
			_camIndex = (_camIndex + 1) % CCTVOverlay.ChannelNames.Length;
			_overlay?.SetChannel(_camIndex);
			ShowCamera(_camIndex);
			_dropout = 1f;
		}

		QueueRedraw();
	}

	// ------------------------------------------------------------------ fallback drawn night yard (only when a camera has no image)
	public override void _Draw()
	{
		if (_feed == null || _feed.Visible) return;
		float W = Size.X, H = Size.Y;
		if (W < 10f || H < 10f) return;
		DrawNightYard(W, H);
	}

	private void DrawNightYard(float W, float H)
	{
		float hy = H * 0.58f;                                   // horizon
		float sway = Mathf.Sin(_time * 0.35f) * W * 0.004f;     // slow camera sway
		DrawSetTransform(new Vector2(sway, 0f), 0f, Vector2.One);

		// Sky and ground
		Quad(-W * 0.02f, 0f, W * 1.04f, hy, new Color(0.015f, 0.03f, 0.025f), new Color(0.08f, 0.13f, 0.10f));
		Quad(-W * 0.02f, hy, W * 1.04f, H - hy, new Color(0.05f, 0.09f, 0.07f), new Color(0.015f, 0.03f, 0.022f));

		// Moon glow
		Vector2 moon = new(W * 0.78f, H * 0.17f);
		for (int i = 0; i < 8; i++)
			DrawCircle(moon, W * (0.10f - i * 0.011f), new Color(0.70f, 0.90f, 0.78f, 0.022f));
		DrawCircle(moon, W * 0.012f, new Color(0.85f, 1f, 0.92f, 0.65f));

		// Treeline (far layer lighter, near layer darker)
		foreach (var t in _trees)
		{
			Color col = t.Far ? new Color(0.035f, 0.07f, 0.05f) : new Color(0.010f, 0.025f, 0.018f);
			float r = t.R * W;
			float cx = t.X * W;
			DrawCircle(new Vector2(cx, hy - r * 0.55f), r, col);
			DrawCircle(new Vector2(cx - r * 0.7f, hy - r * 0.30f), r * 0.7f, col);
			DrawCircle(new Vector2(cx + r * 0.7f, hy - r * 0.30f), r * 0.7f, col);
		}
		DrawRect(new Rect2(-W * 0.02f, hy - H * 0.004f, W * 1.04f, H * 0.04f), new Color(0.010f, 0.025f, 0.018f));

		// Far-off house with a flickering window
		float hx = W * _houseX;
		Color houseCol = new(0.006f, 0.015f, 0.012f);
		DrawRect(new Rect2(hx - W * 0.035f, hy - H * 0.07f, W * 0.07f, H * 0.075f), houseCol);
		DrawColoredPolygon(new[]
		{
			new Vector2(hx - W * 0.045f, hy - H * 0.07f),
			new Vector2(hx + W * 0.045f, hy - H * 0.07f),
			new Vector2(hx, hy - H * 0.125f),
		}, houseCol);
		float wf = Mathf.Clamp(0.75f + 0.25f * Mathf.Sin(_time * 23f) * Mathf.Sin(_time * 5.3f), 0.3f, 1f);
		Vector2 win = new(hx + W * 0.016f, hy - H * 0.036f);
		for (int i = 0; i < 4; i++)
			DrawCircle(win, W * (0.022f - i * 0.005f), new Color(0.85f, 1f, 0.55f, 0.035f * wf));
		DrawRect(new Rect2(win.X - W * 0.006f, win.Y - H * 0.012f, W * 0.012f, H * 0.024f), new Color(0.85f, 1f, 0.55f, 0.85f * wf));

		// Pale dirt path leading toward the camera
		DrawPolygon(
			new[] { new Vector2(W * 0.49f, hy), new Vector2(W * 0.51f, hy), new Vector2(W * 0.82f, H), new Vector2(W * 0.18f, H) },
			new[] { new Color(0.12f, 0.18f, 0.14f, 0f), new Color(0.12f, 0.18f, 0.14f, 0f), new Color(0.12f, 0.18f, 0.14f, 0.55f), new Color(0.12f, 0.18f, 0.14f, 0.55f) });

		// Low drifting ground fog
		for (int i = 0; i < 6; i++)
		{
			float fx = W * (0.1f + 0.16f * i) + Mathf.Sin(_time * 0.12f + i * 1.7f) * W * 0.05f;
			for (int k = 0; k < 4; k++)
				DrawCircle(new Vector2(fx, hy + H * 0.03f), W * (0.17f - k * 0.035f), new Color(0.55f, 0.75f, 0.62f, 0.014f));
		}

		// Fence in the foreground
		Color fence = new(0.012f, 0.026f, 0.020f);
		float fy = hy + H * 0.07f;
		DrawRect(new Rect2(-W * 0.02f, fy + H * 0.03f, W * 1.04f, H * 0.006f), fence);
		DrawRect(new Rect2(-W * 0.02f, fy + H * 0.08f, W * 1.04f, H * 0.006f), fence);
		foreach (float px in _posts)
			DrawRect(new Rect2(px * W, fy, W * 0.007f, H * 0.12f), fence);

		DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}

	private void Quad(float x, float y, float w, float h, Color top, Color bottom)
	{
		DrawPolygon(
			new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) },
			new[] { top, top, bottom, bottom });
	}
}
