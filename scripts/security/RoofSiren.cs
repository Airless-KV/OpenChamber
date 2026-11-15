// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Rooftop emergency alarm siren asset and visual state controller
//                   - Omnidirectional warning horn rotation during active blasts
//                   - High-intensity red emergency strobe light flashing across cabin roof
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Created physical 3D rooftop siren with beacon strobe and horn rotation
// =========================================================================================
using Godot;
using System;

public partial class RoofSiren : Node3D
{
	public static RoofSiren Instance { get; private set; }

	[ExportGroup("Visual References")]
	[Export] public OmniLight3D StrobeLight;
	[Export] public MeshInstance3D StrobeDome;
	[Export] public Node3D RotorHorns;

	[ExportGroup("Siren Settings")]
	[Export] public float StrobeEnergy = 7.0f;
	[Export] public float RotationSpeed = 12.0f;

	public bool IsActive => _isActive;

	private bool _isActive;
	private float _activeTimer;
	private float _strobeTimer;
	private StandardMaterial3D _strobeMat;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		AddToGroup("RoofSirens");

		StrobeLight ??= GetNodeOrNull<OmniLight3D>("StrobeLight");
		StrobeDome ??= GetNodeOrNull<MeshInstance3D>("Rotor/StrobeDome");
		RotorHorns ??= GetNodeOrNull<Node3D>("Rotor");

		if (StrobeDome?.MaterialOverride is StandardMaterial3D mat)
		{
			_strobeMat = mat;
		}
		else if (StrobeDome != null)
		{
			_strobeMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.95f, 0.15f, 0.15f),
				EmissionEnabled = true,
				Emission = new Color(1.0f, 0.1f, 0.1f),
				EmissionEnergyMultiplier = 0.0f
			};
			StrobeDome.MaterialOverride = _strobeMat;
		}

		SetStrobe(false);
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public override void _Process(double delta)
	{
		if (!_isActive) return;

		float dt = (float)delta;
		_activeTimer -= dt;

		if (RotorHorns != null)
		{
			RotorHorns.RotateY(RotationSpeed * dt);
		}

		_strobeTimer += dt * 14.0f; // Rapid 14Hz emergency pulse
		bool flash = (Mathf.FloorToInt(_strobeTimer) % 2) == 0;
		SetStrobe(flash);

		if (_activeTimer <= 0f)
		{
			_isActive = false;
			SetStrobe(false);
		}
	}

	public void Activate(float duration = 2.5f)
	{
		_isActive = true;
		_activeTimer = duration;
		_strobeTimer = 0f;
		SetStrobe(true);
	}

	private void SetStrobe(bool enabled)
	{
		if (StrobeLight != null)
		{
			StrobeLight.Visible = enabled;
			StrobeLight.LightEnergy = enabled ? StrobeEnergy : 0.0f;
		}

		if (_strobeMat != null)
		{
			_strobeMat.EmissionEnergyMultiplier = enabled ? 5.0f : 0.2f;
		}
	}
}
