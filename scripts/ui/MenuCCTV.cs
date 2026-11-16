// =========================================================================================
// Original Author : Anj Ferrer (anjivv) - Nov 16, 2026
// Responsibilities: - Animated CCTV background for Main Menu with live pan, scanlines, and glitch effects
//                   - Four camera channels (North, East, South, West) with periodic auto-cycling
//                   - Fallback procedural canvas drawing of night farmyard if textures are missing
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 16, 2026 | anjivv : Created MenuCCTV background renderer and camera feed rotator
// - Nov 17, 2026 | anjivv : Adjusted feed desaturation, neutral slate tint, and glitch sound
// - Nov 17, 2026 | air    : Standardized header and relocated to scripts/ui/
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

	// [UPDATED] Glitch SFX name (uses your existing AudioManager SFX; swap to "static" or "tv_static" if you add one)
	private const string GlitchSfxName = "window_rattle";
	private const float GlitchSfxPitch = 2.2f;

	private const float ScanlineIntensity = 0.06f;
	private const float PanZoom = 1.30f;
	private const float SweepSeconds = 9f;
	private const float PauseSeconds = 1.6f;
	private const float CamSwitchMin = 16f, CamSwitchMax = 24f;

	// [UPDATED] Neutral, washed-out security camera grade (removes the heavy neon green)
	private const float FeedBrightness = 0.92f;
	private const float FeedDesaturate = 0.80f; // 0.0 = original green, 1.0 = pure black & white
	private static readonly Vector3 FeedTint = new(0.92f, 0.96f, 0.94f);

	private const string FeedShader = @"
shader_type canvas_item;

uniform float brightness = 1.0;
uniform vec3 tint = vec3(1.0, 1.0, 1.0);
uniform float desaturate = 0.0;
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

	private const string LightShader = @"
shader_type canvas_item;
render_mode blend_add;

uniform float noise_amt = 0.008;
uniform float band_amt = 0.030;
uniform float roll_speed = 0.08;
uniform float dropout = 0.0;
uniform vec3 tint = vec3(0.92, 0.95, 0.94);

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
	private bool _attached;
	private CCTVOverlay _overlay;
	private ShaderMaterial _lightMat, _feedMat;
	private TextureRect _feed;
	private readonly Texture2D[] _camTex = new Texture2D[4];

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
		menu.MoveChild(c, Mathf.Min(1, menu.GetChildCount() - 1));
		c.AddOverlay();
		return c;
	}

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		_menu ??= GetParent() as Control;
		if (_menu != null)
		{
			var bg = _menu.GetNodeOrNull<CanvasItem>("Background");
			if (bg != null) bg.Visible = false;
		}
		if (!_attached) Callable.From(AddOverlay).CallDeferred();

		LoadCameraImages();

		_feedMat = new ShaderMaterial { Shader = new Shader { Code = FeedShader } };
		// [UPDATED] Apply desaturated, subtle cool-grey grade so it isn't super green
		_feedMat.SetShaderParameter("brightness", FeedBrightness);
		_feedMat.SetShaderParameter("desaturate", FeedDesaturate);
		_feedMat.SetShaderParameter("tint", FeedTint);

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
		_dropout = 1f;
	}

	private void LoadCameraImages()
	{
		for (int i = 0; i < _camTex.Length; i++)
		{
			foreach (string ext in Extensions)
			{
				string path = $"{CamImageFolder}{CamImageNames[i]}.{ext}";
				if (!ResourceLoader.Exists(path)) continue;
				_camTex[i] = GD.Load<Texture2D>(path);
				break;
			}
		}
	}

	private void AddOverlay()
	{
		if (_menu == null || _overlay != null) return;
		var scene = GD.Load<PackedScene>(OverlayScenePath);
		_overlay = scene?.Instantiate() as CCTVOverlay;
		if (_overlay == null) return;
		_menu.AddChild(_overlay);

		Callable.From(() =>
		{
			_overlay.ShowOverlay(_camIndex);
			// [UPDATED] Disable the heavy green night-vision phosphor overlay on the menu
			_overlay.SetNightVision(false);

			if (_overlay.ScanlinesRect?.Material is ShaderMaterial sm)
			{
				var copy = (ShaderMaterial)sm.Duplicate();
				copy.SetShaderParameter("scanline_intensity", ScanlineIntensity);
				copy.SetShaderParameter("night_vision_enabled", false);
				_overlay.ScanlinesRect.Material = copy;
			}
			if (_overlay.ControlsLabel != null) _overlay.ControlsLabel.Visible = false;
			if (_overlay.ChannelLabel != null) _overlay.ChannelLabel.Visible = false;
			if (_overlay.ExitLabel != null) _overlay.ExitLabel.Visible = false;
		}).CallDeferred();
	}

	private void ShowCamera(int idx)
	{
		Texture2D tex = idx >= 0 && idx < _camTex.Length ? _camTex[idx] : null;
		_feed.Texture = tex;
		_feed.Visible = tex != null;
		_panTime = 0f;
		BuildScene(idx);
	}

	// [UPDATED] Plays a static/glitch sound effect on camera transition
	private void PlayGlitchSound()
	{
		AudioManager.Sfx(GlitchSfxName, GlitchSfxPitch);
	}

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

	private static float Ease(float x) => Mathf.Lerp(x, Mathf.SmoothStep(0f, 1f, x), 0.5f);

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

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_time += dt;
		_panTime += dt;

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

		_dropout = Mathf.MoveToward(_dropout, 0f, dt * 2.2f);
		_lightMat.SetShaderParameter("dropout", _dropout);
		_feedMat.SetShaderParameter("glitch", _dropout);

		_signalTimer -= dt;
		if (_signalTimer <= 0f && _overlay?.StatusLabel != null)
		{
			_signalTimer = 0.5f;
			int pct = _dropout > 0.2f ? GD.RandRange(38, 70) : GD.RandRange(95, 99);
			_overlay.StatusLabel.Text = $"SIGNAL: {pct}% | 30 FPS";
		}

		// Camera switch + glitch SFX
		_camTimer -= dt;
		if (_camTimer <= 0f)
		{
			_camTimer = (float)GD.RandRange(CamSwitchMin, CamSwitchMax);
			_camIndex = (_camIndex + 1) % CCTVOverlay.ChannelNames.Length;
			_overlay?.SetChannel(_camIndex);
			ShowCamera(_camIndex);
			_dropout = 1f;
			PlayGlitchSound(); // [UPDATED]
		}

		QueueRedraw();
	}

	// ------------------------------------------------------------------ fallback drawn night yard (desaturated neutral grey-slate)
	public override void _Draw()
	{
		if (_feed == null || _feed.Visible) return;
		float W = Size.X, H = Size.Y;
		if (W < 10f || H < 10f) return;
		DrawNightYard(W, H);
	}

	private void DrawNightYard(float W, float H)
	{
		float hy = H * 0.58f;
		float sway = Mathf.Sin(_time * 0.35f) * W * 0.004f;
		DrawSetTransform(new Vector2(sway, 0f), 0f, Vector2.One);

		// [UPDATED] Neutral dark slate-grey sky & ground instead of green
		Quad(-W * 0.02f, 0f, W * 1.04f, hy, new Color(0.02f, 0.022f, 0.025f), new Color(0.09f, 0.10f, 0.105f));
		Quad(-W * 0.02f, hy, W * 1.04f, H - hy, new Color(0.06f, 0.068f, 0.07f), new Color(0.02f, 0.022f, 0.025f));

		Vector2 moon = new(W * 0.78f, H * 0.17f);
		for (int i = 0; i < 8; i++)
			DrawCircle(moon, W * (0.10f - i * 0.011f), new Color(0.80f, 0.84f, 0.85f, 0.020f));
		DrawCircle(moon, W * 0.012f, new Color(0.90f, 0.94f, 0.95f, 0.65f));

		foreach (var t in _trees)
		{
			Color col = t.Far ? new Color(0.045f, 0.05f, 0.052f) : new Color(0.015f, 0.018f, 0.02f);
			float r = t.R * W;
			float cx = t.X * W;
			DrawCircle(new Vector2(cx, hy - r * 0.55f), r, col);
			DrawCircle(new Vector2(cx - r * 0.7f, hy - r * 0.30f), r * 0.7f, col);
			DrawCircle(new Vector2(cx + r * 0.7f, hy - r * 0.30f), r * 0.7f, col);
		}
		DrawRect(new Rect2(-W * 0.02f, hy - H * 0.004f, W * 1.04f, H * 0.04f), new Color(0.015f, 0.018f, 0.02f));

		float hx = W * _houseX;
		Color houseCol = new(0.010f, 0.012f, 0.014f);
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
			DrawCircle(win, W * (0.022f - i * 0.005f), new Color(0.92f, 0.88f, 0.72f, 0.035f * wf));
		DrawRect(new Rect2(win.X - W * 0.006f, win.Y - H * 0.012f, W * 0.012f, H * 0.024f), new Color(0.92f, 0.88f, 0.72f, 0.85f * wf));

		DrawPolygon(
			new[] { new Vector2(W * 0.49f, hy), new Vector2(W * 0.51f, hy), new Vector2(W * 0.82f, H), new Vector2(W * 0.18f, H) },
			new[] { new Color(0.14f, 0.15f, 0.15f, 0f), new Color(0.14f, 0.15f, 0.15f, 0f), new Color(0.14f, 0.15f, 0.15f, 0.50f), new Color(0.14f, 0.15f, 0.15f, 0.50f) });

		for (int i = 0; i < 6; i++)
		{
			float fx = W * (0.1f + 0.16f * i) + Mathf.Sin(_time * 0.12f + i * 1.7f) * W * 0.05f;
			for (int k = 0; k < 4; k++)
				DrawCircle(new Vector2(fx, hy + H * 0.03f), W * (0.17f - k * 0.035f), new Color(0.65f, 0.68f, 0.70f, 0.012f));
		}

		Color fence = new(0.018f, 0.020f, 0.022f);
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
