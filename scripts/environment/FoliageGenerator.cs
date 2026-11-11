// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Hybrid procedural generation and manual editing tool for 2.5D foliage
//                   - Distance-zone distribution (Outer Forest, Mid-Meadow, Farm Buffer clearance)
//                   - Deterministic seed scattering, scale variation, and terrain height clamping
//                   - Dedicated ManualLayer preservation during procedural regeneration
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 10, 2026 | air : Created hybrid FoliageGenerator tool script for procedural and manual 2.5D foliage
// - Nov 10, 2026 | air : Scaled tree density (+170%) to 320 outer trees and 100 mid-meadow trees
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class FoliageGenerator : Node3D
{
	[ExportGroup("Prefab Libraries")]
	[Export] public PackedScene[] TreePrefabs { get; set; } = [];
	[Export] public PackedScene[] BushPrefabs { get; set; } = [];
	[Export] public PackedScene[] FloraPrefabs { get; set; } = [];
	[Export] public PackedScene[] RockPrefabs { get; set; } = [];
	[Export] public PackedScene[] PropPrefabs { get; set; } = [];

	[ExportGroup("Zone Dimensions")]
	[Export] public Vector3 FarmCenter { get; set; } = new(-1.66f, 0, 0);
	[Export] public float GroundY { get; set; } = 0.604f;
	[Export] public float FarmClearanceRadius { get; set; } = 25.0f;
	[Export] public float MidMeadowOuterRadius { get; set; } = 65.0f;
	[Export] public float OuterForestRadius { get; set; } = 175.0f;

	[ExportGroup("Spawn Density")]
	[Export] public int OuterTreeCount { get; set; } = 320;
	[Export] public int MidTreeCount { get; set; } = 100;
	[Export] public int BushCount { get; set; } = 70;
	[Export] public int FloraCount { get; set; } = 100;
	[Export] public int RockCount { get; set; } = 45;
	[Export] public int PropCount { get; set; } = 35;

	[ExportGroup("Generation Control")]
	[Export] public int RandomSeed { get; set; } = 1337;
	[Export] public bool AutoGenerateOnReady { get; set; } = true;

	private bool _triggerRegenerate = false;
	[Export]
	public bool RegenerateFoliage
	{
		get => _triggerRegenerate;
		set
		{
			_triggerRegenerate = value;
			if (value)
			{
				_triggerRegenerate = false;
				GenerateAll();
			}
		}
	}

	public Node3D ProceduralLayerNode { get; private set; }
	public Node3D ManualLayerNode { get; private set; }

	public override void _Ready()
	{
		EnsureLayers();

		if (AutoGenerateOnReady && ProceduralLayerNode.GetChildCount() == 0)
		{
			GenerateAll();
		}
	}

	private void EnsureLayers()
	{
		ProceduralLayerNode = GetNodeOrNull<Node3D>("ProceduralLayer");
		if (ProceduralLayerNode == null)
		{
			ProceduralLayerNode = new Node3D { Name = "ProceduralLayer" };
			AddChild(ProceduralLayerNode);
			if (Engine.IsEditorHint())
			{
				ProceduralLayerNode.Owner = GetTree()?.EditedSceneRoot ?? this;
			}
		}

		ManualLayerNode = GetNodeOrNull<Node3D>("ManualLayer");
		if (ManualLayerNode == null)
		{
			ManualLayerNode = new Node3D { Name = "ManualLayer" };
			AddChild(ManualLayerNode);
			if (Engine.IsEditorHint())
			{
				ManualLayerNode.Owner = GetTree()?.EditedSceneRoot ?? this;
			}
		}
	}

	public void GenerateAll()
	{
		EnsureLayers();

		// Clear procedural layer only — ManualLayer is strictly preserved
		while (ProceduralLayerNode.GetChildCount() > 0)
		{
			Node child = ProceduralLayerNode.GetChild(0);
			ProceduralLayerNode.RemoveChild(child);
			child.QueueFree();
		}

		RandomNumberGenerator rng = new();
		rng.Seed = (ulong)RandomSeed;

		// 1. Outer Dense Forest
		if (TreePrefabs != null && TreePrefabs.Length > 0)
		{
			for (int i = 0; i < OuterTreeCount; i++)
			{
				Vector3 pos = SampleAnnulus(rng, MidMeadowOuterRadius, OuterForestRadius);
				SpawnInstance(rng, TreePrefabs, pos, 0.85f, 1.25f);
			}

			// 2. Mid Meadow Trees
			for (int i = 0; i < MidTreeCount; i++)
			{
				Vector3 pos = SampleAnnulus(rng, FarmClearanceRadius + 2.0f, MidMeadowOuterRadius);
				SpawnInstance(rng, TreePrefabs, pos, 0.8f, 1.15f);
			}
		}

		// 3. Rocks and Boulders
		if (RockPrefabs != null && RockPrefabs.Length > 0)
		{
			for (int i = 0; i < RockCount; i++)
			{
				// Heavy rocks on ridge, smaller rocks in mid meadow
				float minR = rng.Randf() < 0.65f ? MidMeadowOuterRadius : FarmClearanceRadius + 1.0f;
				float maxR = minR == MidMeadowOuterRadius ? OuterForestRadius : MidMeadowOuterRadius;
				Vector3 pos = SampleAnnulus(rng, minR, maxR);
				SpawnInstance(rng, RockPrefabs, pos, 0.85f, 1.2f);
			}
		}

		// 4. Bushes
		if (BushPrefabs != null && BushPrefabs.Length > 0)
		{
			for (int i = 0; i < BushCount; i++)
			{
				Vector3 pos = SampleAnnulus(rng, FarmClearanceRadius, MidMeadowOuterRadius + 20.0f);
				SpawnInstance(rng, BushPrefabs, pos, 0.85f, 1.2f);
			}
		}

		// 5. Wild Flora (Ferns, Grass, Mushrooms, Reeds)
		if (FloraPrefabs != null && FloraPrefabs.Length > 0)
		{
			for (int i = 0; i < FloraCount; i++)
			{
				Vector3 pos = SampleAnnulus(rng, FarmClearanceRadius, MidMeadowOuterRadius + 15.0f);
				SpawnInstance(rng, FloraPrefabs, pos, 0.8f, 1.15f);
			}
		}

		// 6. Props (Stumps, Logs, Hay Bales, Fence Posts, Signs, Cairns)
		if (PropPrefabs != null && PropPrefabs.Length > 0)
		{
			for (int i = 0; i < PropCount; i++)
			{
				Vector3 pos = SampleAnnulus(rng, FarmClearanceRadius, MidMeadowOuterRadius);
				SpawnInstance(rng, PropPrefabs, pos, 0.9f, 1.15f);
			}
		}

		GD.Print($"[FoliageGenerator] Populated {ProceduralLayerNode.GetChildCount()} procedural 2.5D environment entities (Seed: {RandomSeed}).");
	}

	private Vector3 SampleAnnulus(RandomNumberGenerator rng, float minRadius, float maxRadius)
	{
		float angle = rng.RandfRange(0.0f, Mathf.Tau);
		// Uniform area distribution across circular annulus
		float r = Mathf.Sqrt(rng.RandfRange(minRadius * minRadius, maxRadius * maxRadius));
		float x = FarmCenter.X + r * Mathf.Cos(angle);
		float z = FarmCenter.Z + r * Mathf.Sin(angle);

		// Clamp within boundary limits
		x = Mathf.Clamp(x, -190.0f, 190.0f);
		z = Mathf.Clamp(z, -190.0f, 190.0f);

		return new Vector3(x, GroundY, z);
	}

	private void SpawnInstance(RandomNumberGenerator rng, PackedScene[] library, Vector3 position, float minScale, float maxScale)
	{
		if (library == null || library.Length == 0) return;

		PackedScene prefab = library[rng.RandiRange(0, library.Length - 1)];
		if (prefab == null) return;

		if (prefab.Instantiate() is Node3D instance)
		{
			ProceduralLayerNode.AddChild(instance);
			instance.GlobalPosition = position;

			float scaleFactor = rng.RandfRange(minScale, maxScale);
			instance.Scale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

			// Flip sprite horizontally randomly for natural asymmetric variation
			if (instance.GetNodeOrNull<Sprite3D>("Sprite3D") is Sprite3D sprite)
			{
				sprite.FlipH = rng.Randf() > 0.5f;
			}

			if (Engine.IsEditorHint())
			{
				instance.Owner = GetTree()?.EditedSceneRoot ?? this;
			}
		}
	}
}
