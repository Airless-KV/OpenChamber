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
// - Nov 13, 2026 | air : Added rectangular farm exclusion bounding box with auto-detection
//                        and tiered buffer margins to prevent foliage encroachment on chessboard
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

	[ExportGroup("Farm Exclusion Zone")]
	[Export] public bool AutoDetectChessboard { get; set; } = true;
	[Export] public Vector2 FarmBoundsMin { get; set; } = new(-25.7f, -24.0f); // X-min, Z-min
	[Export] public Vector2 FarmBoundsMax { get; set; } = new(22.3f, 24.0f);   // X-max, Z-max
	[Export] public float TreeBufferMargin { get; set; } = 3.5f;  // Keeps wide tree trunks & crowns clear of tiles
	[Export] public float FloraBufferMargin { get; set; } = 1.5f; // Keeps bushes, rocks & wild flora clear of tiles

	[ExportGroup("Zone Dimensions")]
	[Export] public Vector3 FarmCenter { get; set; } = new(-1.66f, 0, 0);
	[Export] public float GroundY { get; set; } = 0.604f;
	[Export] public float FarmClearanceRadius { get; set; } = 24.0f;
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

		if (AutoDetectChessboard)
		{
			DetectChessboardBounds();
		}

		if (AutoGenerateOnReady && ProceduralLayerNode.GetChildCount() == 0)
		{
			GenerateAll();
		}
	}

	public void DetectChessboardBounds()
	{
		Node farmPlot = GetParent()?.GetNodeOrNull("FarmPlot") ?? GetTree()?.Root.FindChild("FarmPlot", true, false);
		if (farmPlot == null) return;

		CollisionShape3D col = farmPlot.FindChild("CollisionShape3D", true, false) as CollisionShape3D;
		if (col == null) return;

		Vector3 gpos = col.IsInsideTree() 
			? col.GlobalPosition 
			: (farmPlot is Node3D fn ? fn.Position + col.Position : col.Position);
		
		Vector3 scale = col.Scale;
		float halfExtentX = scale.X * 0.5f;
		float halfExtentZ = scale.Z * 0.5f;

		FarmBoundsMin = new Vector2(gpos.X - halfExtentX, gpos.Z - halfExtentZ);
		FarmBoundsMax = new Vector2(gpos.X + halfExtentX, gpos.Z + halfExtentZ);
		FarmCenter = new Vector3((FarmBoundsMin.X + FarmBoundsMax.X) * 0.5f, GroundY, (FarmBoundsMin.Y + FarmBoundsMax.Y) * 0.5f);
	}

	public bool IsInFarmExclusion(Vector3 pos, float buffer)
	{
		return pos.X >= (FarmBoundsMin.X - buffer) && pos.X <= (FarmBoundsMax.X + buffer) &&
			   pos.Z >= (FarmBoundsMin.Y - buffer) && pos.Z <= (FarmBoundsMax.Y + buffer);
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

		if (AutoDetectChessboard)
		{
			DetectChessboardBounds();
		}

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
				Vector3 pos = SampleFoliagePosition(rng, MidMeadowOuterRadius, OuterForestRadius, TreeBufferMargin);
				SpawnInstance(rng, TreePrefabs, pos, 0.85f, 1.25f);
			}

			// 2. Mid Meadow Trees
			for (int i = 0; i < MidTreeCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius, TreeBufferMargin);
				SpawnInstance(rng, TreePrefabs, pos, 0.8f, 1.15f);
			}
		}

		// 3. Rocks and Boulders
		if (RockPrefabs != null && RockPrefabs.Length > 0)
		{
			for (int i = 0; i < RockCount; i++)
			{
				// Heavy rocks on ridge, smaller rocks in mid meadow
				float minR = rng.Randf() < 0.65f ? MidMeadowOuterRadius : FarmClearanceRadius;
				float maxR = minR == MidMeadowOuterRadius ? OuterForestRadius : MidMeadowOuterRadius;
				Vector3 pos = SampleFoliagePosition(rng, minR, maxR, FloraBufferMargin);
				SpawnInstance(rng, RockPrefabs, pos, 0.85f, 1.2f);
			}
		}

		// 4. Bushes
		if (BushPrefabs != null && BushPrefabs.Length > 0)
		{
			for (int i = 0; i < BushCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius + 20.0f, FloraBufferMargin + 0.5f);
				SpawnInstance(rng, BushPrefabs, pos, 0.85f, 1.2f);
			}
		}

		// 5. Wild Flora (Ferns, Grass, Mushrooms, Reeds)
		if (FloraPrefabs != null && FloraPrefabs.Length > 0)
		{
			for (int i = 0; i < FloraCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius + 15.0f, FloraBufferMargin);
				SpawnInstance(rng, FloraPrefabs, pos, 0.8f, 1.15f);
			}
		}

		// 6. Props (Stumps, Logs, Hay Bales, Fence Posts, Signs, Cairns)
		if (PropPrefabs != null && PropPrefabs.Length > 0)
		{
			for (int i = 0; i < PropCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius, FloraBufferMargin);
				SpawnInstance(rng, PropPrefabs, pos, 0.9f, 1.15f);
			}
		}

		GD.Print($"[FoliageGenerator] Populated {ProceduralLayerNode.GetChildCount()} procedural 2.5D environment entities outside farm bounds X:[{FarmBoundsMin.X:F1}, {FarmBoundsMax.X:F1}], Z:[{FarmBoundsMin.Y:F1}, {FarmBoundsMax.Y:F1}] (Seed: {RandomSeed}).");
	}

	private Vector3 SampleFoliagePosition(RandomNumberGenerator rng, float minRadius, float maxRadius, float buffer)
	{
		// Rejection sampling against rectangular farm exclusion boundary
		for (int attempt = 0; attempt < 30; attempt++)
		{
			Vector3 candidate = SampleAnnulus(rng, minRadius, maxRadius);
			if (!IsInFarmExclusion(candidate, buffer))
			{
				return candidate;
			}
		}

		// Fallback: If 30 attempts fell inside (e.g. corner radii), project candidate radially outward until it clears
		Vector3 fallback = SampleAnnulus(rng, minRadius, maxRadius);
		Vector2 dir = new Vector2(fallback.X - FarmCenter.X, fallback.Z - FarmCenter.Z).Normalized();
		if (dir == Vector2.Zero) dir = Vector2.Right;

		float pushDist = Mathf.Max(
			Mathf.Abs((FarmBoundsMax.X - FarmBoundsMin.X) * 0.5f) + buffer,
			Mathf.Abs((FarmBoundsMax.Y - FarmBoundsMin.Y) * 0.5f) + buffer
		) * 1.42f; // Diagonal clearance factor ~sqrt(2)

		fallback.X = FarmCenter.X + dir.X * pushDist;
		fallback.Z = FarmCenter.Z + dir.Y * pushDist;
		return fallback;
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
			if (instance.IsInsideTree())
			{
				instance.GlobalPosition = position;
			}
			else
			{
				instance.Position = ToLocal(position);
			}

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
