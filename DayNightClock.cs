using Godot;

// Day/Night clock dial. The ring is split into a day arc (yellow) and a night arc (purple),
// sized by how long today's day and night last. The hand sweeps around as time passes.
// HUDManager creates it automatically; no scene setup needed.
// Self-contained: it only needs the GameManager's TimeUpdated / PhaseChanged signals.
public partial class DayNightClock : Control
{
	[Export] public Color DayColor = new Color(1f, 0.85f, 0.25f);
	[Export] public Color NightColor = new Color(0.45f, 0.35f, 0.8f);
	[Export] public Color FaceColor = new Color(0.07f, 0.07f, 0.10f, 0.85f);
	[Export] public Color RimColor = new Color(0.9f, 0.9f, 0.9f);

	// Used until the clock sees the real length of a phase from the GameManager.
	private static readonly float[] DefaultDay = { 300, 300, 240, 240, 180, 180, 120 };
	private static readonly float[] DefaultNight = { 180, 180, 240, 240, 300, 300, 600 };

	private GameManager _gm;
	private int _day = 1;
	private float _dayDuration = 300f;
	private float _nightDuration = 180f;
	private bool _isDay = true;
	private bool _totalKnown = false;
	private float _phaseProgress = 0f; // 0..1 within the current phase

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public void Bind(GameManager gm)
	{
		_gm = gm;
		_gm.TimeUpdated += OnTimeUpdated;
		_gm.PhaseChanged += OnPhaseChanged;

		_isDay = gm.CurrentPhase == GameManager.GamePhase.Day;
		_day = gm.CurrentDay;
		ApplyDefaults();
		_totalKnown = true; // we may have joined mid-phase, so trust the defaults
		OnTimeUpdated(gm.TimeRemaining);
	}

	private void ApplyDefaults()
	{
		int i = Mathf.Clamp(_day - 1, 0, DefaultDay.Length - 1);
		_dayDuration = DefaultDay[i];
		_nightDuration = DefaultNight[i];
	}

	private void OnPhaseChanged(bool isDay, int currentDay)
	{
		_isDay = isDay;
		_day = currentDay;
		ApplyDefaults();
		_totalKnown = false; // the first TimeUpdated of the phase tells us its real length
		_phaseProgress = 0f;
		QueueRedraw();
	}

	private void OnTimeUpdated(float timeRemaining)
	{
		if (!_totalKnown)
		{
			if (_isDay)
				_dayDuration = Mathf.Max(timeRemaining, 1f);
			else
				_nightDuration = Mathf.Max(timeRemaining, 1f);
			_totalKnown = true;
		}

		float total = _isDay ? _dayDuration : _nightDuration;
		_phaseProgress = total > 0f ? Mathf.Clamp(1f - timeRemaining / total, 0f, 1f) : 0f;
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 c = Size * 0.5f;
		float r = Mathf.Min(Size.X, Size.Y) * 0.5f - 4f;

		DrawCircle(c, r, FaceColor);

		float cycle = _dayDuration + _nightDuration;
		float start = -Mathf.Pi / 2f; // 12 o'clock
		float dayAngle = Mathf.Tau * (_dayDuration / cycle);

		DrawArc(c, r - 5f, start, start + dayAngle, 48, DayColor, 6f, true);
		DrawArc(c, r - 5f, start + dayAngle, start + Mathf.Tau, 48, NightColor, 6f, true);
		DrawArc(c, r, 0f, Mathf.Tau, 64, RimColor, 2f, true);

		// Clock hand
		float elapsed = _isDay
			? _phaseProgress * _dayDuration
			: _dayDuration + _phaseProgress * _nightDuration;
		float ang = start + Mathf.Tau * (elapsed / cycle);
		Vector2 tip = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (r - 10f);
		DrawLine(c, tip, Colors.White, 3f, true);
		DrawCircle(c, 4f, Colors.White);

		// Sun / moon icon
		Vector2 ic = c + new Vector2(0f, r * 0.38f);
		if (_isDay)
		{
			DrawCircle(ic, 5f, DayColor);
			for (int i = 0; i < 8; i++)
			{
				float a = i * Mathf.Tau / 8f;
				var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
				DrawLine(ic + dir * 8f, ic + dir * 12f, DayColor, 2f);
			}
		}
		else
		{
			DrawCircle(ic, 8f, new Color(0.88f, 0.88f, 1f));
			DrawCircle(ic + new Vector2(4f, -3f), 7f, FaceColor);
		}
	}
}
