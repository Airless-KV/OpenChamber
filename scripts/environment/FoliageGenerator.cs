// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Hybrid procedural generation and manual editing tool for 2.5D foliage
//                   - Distance-zone distribution (Outer Forest, Mid-Meadow, Farm Buffer clearance)
//                   - Deterministic seed scattering, scale variation, and terrain height clamping
//                   - Dedicated ManualLayer preservation during procedural regeneration
//                   - Dynamic spatial cell streaming (2D grid chunking) for active view loading
//                   - Tiered hardware distance culling (visibility_range_end) on billboard sprites
//                   - Distant collision pruning to optimize Jolt Physics broadphase performance
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 10, 2026 | air : Created hybrid FoliageGenerator tool script for procedural and manual 2.5D foliage
// - Nov 10, 2026 | air : Scaled tree density (+170%) to 320 outer trees and 100 mid-meadow trees
// - Nov 13, 2026 | air : Added rectangular farm exclusion bounding box with auto-detection
//                        and tiered buffer margins to prevent foliage encroachment on chessboard
// - Nov 15, 2026 | air : Implemented dynamic spatial cell streaming (2D grid chunking),
//                        camera-tracked cell loading/unloading with hysteresis buffer,
//                        hardware-tiered visibility ranges (35m-115m), and distant collision pruning
// - Nov 15, 2026 | air : Extended streaming radius (135m load / 160m unload) and disabled visibility
//                        range culling per user direction to restore full landscape visuals
// - Nov 15, 2026 | air : Enabled hardware visibility ranges (35m flora / 55m bush / 75m prop / 160m tree),
//                        disabled billboard shadow casting, and optimized LOD margins for Intel HD 620
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class FoliageGenerator : Node3D
{
	public struct FoliageData
	{
		public int Category; // 0=OuterTree, 1=MidTree, 2=Rock, 3=Bush, 4=Flora, 5=Prop
		public int PrefabIndex;
		public Vector3 Position;
		public float Scale;
		public bool FlipH;
		public bool HasCollision;
	}

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

	[ExportGroup("Dynamic Spatial Streaming")]
	[Export] public bool EnableDynamicStreaming { get; set; } = true;
	[Export] public float CellSize { get; set; } = 32.0f;
	[Export] public float LoadRadius { get; set; } = 135.0f;
	[Export] public float UnloadRadius { get; set; } = 160.0f;
	[Export] public float StreamCheckInterval { get; set; } = 0.15f;
	[Export] public float MinMoveDistanceThreshold { get; set; } = 3.0f;

	[ExportGroup("Hardware Visibility Ranges (LOD)")]
	[Export] public bool EnableVisibilityRanges { get; set; } = true;
	[Export] public float FloraVisibilityEnd { get; set; } = 35.0f;
	[Export] public float BushVisibilityEnd { get; set; } = 55.0f;
	[Export] public float RockVisibilityEnd { get; set; } = 75.0f;
	[Export] public float PropVisibilityEnd { get; set; } = 75.0f;
	[Export] public float TreeVisibilityEnd { get; set; } = 160.0f;

	[ExportGroup("Collision Optimization")]
	[Export] public bool OptimizeDistantCollisions { get; set; } = true;
	[Export] public float CollisionMaxDistance { get; set; } = 48.0f;

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

	private readonly Dictionary<Vector2I, List<FoliageData>> _gridData = new();
	private readonly Dictionary<Vector2I, Node3D> _activeCellNodes = new();
	private Vector3 _lastCheckedCamPos = new(9999f, 9999f, 9999f);
	private float _streamTimer = 0f;

	public override void _Ready()
	{
		EnsureLayers();

		if (AutoDetectChessboard)
		{
			DetectChessboardBounds();
		}

		if (AutoGenerateOnReady)
		{
			GenerateAll();
		}
	}

	public override void _Process(double delta)
	{
		if (!EnableDynamicStreaming || Engine.IsEditorHint()) return;

		_streamTimer += (float)delta;
		if (_streamTimer >= StreamCheckInterval)
		{
			_streamTimer = 0f;
			UpdateStreaming(force: false);
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

		// Clear existing procedural layer nodes and active cell tracking
		_activeCellNodes.Clear();
		while (ProceduralLayerNode.GetChildCount() > 0)
		{
			Node child = ProceduralLayerNode.GetChild(0);
			ProceduralLayerNode.RemoveChild(child);
			child.QueueFree();
		}

		_gridData.Clear();

		RandomNumberGenerator rng = new();
		rng.Seed = (ulong)RandomSeed;

		// 1. Outer Dense Forest
		if (TreePrefabs != null && TreePrefabs.Length > 0)
		{
			for (int i = 0; i < OuterTreeCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, MidMeadowOuterRadius, OuterForestRadius, TreeBufferMargin);
				AddInstanceData(rng, 0, TreePrefabs.Length, pos, 0.85f, 1.25f);
			}

			// 2. Mid Meadow Trees
			for (int i = 0; i < MidTreeCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius, TreeBufferMargin);
				AddInstanceData(rng, 1, TreePrefabs.Length, pos, 0.8f, 1.15f);
			}
		}

		// 3. Rocks and Boulders
		if (RockPrefabs != null && RockPrefabs.Length > 0)
		{
			for (int i = 0; i < RockCount; i++)
			{
				float minR = rng.Randf() < 0.65f ? MidMeadowOuterRadius : FarmClearanceRadius;
				float maxR = minR == MidMeadowOuterRadius ? OuterForestRadius : MidMeadowOuterRadius;
				Vector3 pos = SampleFoliagePosition(rng, minR, maxR, FloraBufferMargin);
				AddInstanceData(rng, 2, RockPrefabs.Length, pos, 0.85f, 1.2f);
			}
		}

		// 4. Bushes
		if (BushPrefabs != null && BushPrefabs.Length > 0)
		{
			for (int i = 0; i < BushCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius + 20.0f, FloraBufferMargin + 0.5f);
				AddInstanceData(rng, 3, BushPrefabs.Length, pos, 0.85f, 1.2f);
			}
		}

		// 5. Wild Flora (Ferns, Grass, Mushrooms, Reeds)
		if (FloraPrefabs != null && FloraPrefabs.Length > 0)
		{
			for (int i = 0; i < FloraCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius + 15.0f, FloraBufferMargin);
				AddInstanceData(rng, 4, FloraPrefabs.Length, pos, 0.8f, 1.15f);
			}
		}

		// 6. Props (Stumps, Logs, Hay Bales, Fence Posts, Signs, Cairns)
		if (PropPrefabs != null && PropPrefabs.Length > 0)
		{
			for (int i = 0; i < PropCount; i++)
			{
				Vector3 pos = SampleFoliagePosition(rng, FarmClearanceRadius, MidMeadowOuterRadius, FloraBufferMargin);
				AddInstanceData(rng, 5, PropPrefabs.Length, pos, 0.9f, 1.15f);
			}
		}

		int totalEntities = 0;
		foreach (var list in _gridData.Values)
		{
			totalEntities += list.Count;
		}

		if (EnableDynamicStreaming && !Engine.IsEditorHint())
		{
			_lastCheckedCamPos = new Vector3(9999f, 9999f, 9999f);
			UpdateStreaming(force: true);

			int activeEntities = 0;
			foreach (var cellNode in _activeCellNodes.Values)
			{
				activeEntities += cellNode.GetChildCount();
			}

			GD.Print($"[FoliageGenerator] Initialized dynamic streaming for {totalEntities} entities partitioned into {_gridData.Count} spatial cells ({CellSize:F0}m x {CellSize:F0}m). Active loaded: {_activeCellNodes.Count} cells ({activeEntities} entities in scene, {totalEntities - activeEntities} culled).");
		}
		else
		{
			// In editor or if dynamic streaming is disabled, load all cells
			foreach (var kvp in _gridData)
			{
				LoadCell(kvp.Key, kvp.Value);
			}
			GD.Print($"[FoliageGenerator] Populated all {_gridData.Count} cells ({ProceduralLayerNode.GetChildCount()} cell roots) with {totalEntities} total entities outside farm bounds.");
		}
	}

	private void AddInstanceData(RandomNumberGenerator rng, int category, int prefabCount, Vector3 pos, float minScale, float maxScale)
	{
		if (prefabCount <= 0) return;

		int prefabIndex = rng.RandiRange(0, prefabCount - 1);
		float scale = rng.RandfRange(minScale, maxScale);
		bool flipH = rng.Randf() > 0.5f;

		// Trees (0, 1), Rocks (2), and Props (5) have colliders. Bushes (3) and Flora (4) do not.
		bool isSolidCategory = (category == 0 || category == 1 || category == 2 || category == 5);
		bool needsCollision = isSolidCategory && (!OptimizeDistantCollisions || (pos - FarmCenter).Length() <= CollisionMaxDistance);

		var data = new FoliageData
		{
			Category = category,
			PrefabIndex = prefabIndex,
			Position = pos,
			Scale = scale,
			FlipH = flipH,
			HasCollision = needsCollision
		};

		Vector2I cellCoord = WorldToCell(pos, CellSize);
		if (!_gridData.TryGetValue(cellCoord, out var list))
		{
			list = new List<FoliageData>();
			_gridData[cellCoord] = list;
		}
		list.Add(data);
	}

	public void UpdateStreaming(bool force = false)
	{
		if (!EnableDynamicStreaming || _gridData.Count == 0) return;

		Vector3 camPos = GetActiveCameraPosition();

		if (!force && (camPos - _lastCheckedCamPos).LengthSquared() < MinMoveDistanceThreshold * MinMoveDistanceThreshold)
		{
			return;
		}

		_lastCheckedCamPos = camPos;
		Vector2 camXZ = new(camPos.X, camPos.Z);
		float loadDistSq = LoadRadius * LoadRadius;
		float unloadDistSq = UnloadRadius * UnloadRadius;

		// 1. Unload cells beyond UnloadRadius
		List<Vector2I> toUnload = null;
		foreach (var kvp in _activeCellNodes)
		{
			Vector2 cellCenter = CellToWorldCenter(kvp.Key, CellSize);
			if ((camXZ - cellCenter).LengthSquared() > unloadDistSq)
			{
				toUnload ??= new List<Vector2I>();
				toUnload.Add(kvp.Key);
			}
		}

		if (toUnload != null)
		{
			for (int i = 0; i < toUnload.Count; i++)
			{
				UnloadCell(toUnload[i]);
			}
		}

		// 2. Load cells within LoadRadius
		foreach (var kvp in _gridData)
		{
			if (_activeCellNodes.ContainsKey(kvp.Key)) continue;

			Vector2 cellCenter = CellToWorldCenter(kvp.Key, CellSize);
			if ((camXZ - cellCenter).LengthSquared() <= loadDistSq)
			{
				LoadCell(kvp.Key, kvp.Value);
			}
		}
	}

	private void LoadCell(Vector2I coord, List<FoliageData> instances)
	{
		if (_activeCellNodes.ContainsKey(coord) || instances == null || instances.Count == 0) return;

		var cellNode = new Node3D { Name = $"Cell_{coord.X}_{coord.Y}" };
		ProceduralLayerNode.AddChild(cellNode);
		_activeCellNodes[coord] = cellNode;

		for (int i = 0; i < instances.Count; i++)
		{
			FoliageData data = instances[i];
			PackedScene prefab = GetPrefab(data.Category, data.PrefabIndex);
			if (prefab == null) continue;

			if (prefab.Instantiate() is Node3D instance)
			{
				cellNode.AddChild(instance);
				instance.GlobalPosition = data.Position;
				instance.Scale = new Vector3(data.Scale, data.Scale, data.Scale);

				// Hardware visibility range configuration
				var sprite = instance.GetNodeOrNull<Sprite3D>("Sprite3D") ?? instance.FindChild("Sprite3D", true, false) as Sprite3D;
				if (sprite != null)
				{
					sprite.FlipH = data.FlipH;

					// Disable dynamic shadow casting on 2.5D billboard sprites to eliminate thousands of shadow cascade passes
					sprite.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

					if (EnableVisibilityRanges)
					{
						float endDist = data.Category switch
						{
							4 => FloraVisibilityEnd,     // Flora (35m)
							3 => BushVisibilityEnd,      // Bushes (55m)
							2 => RockVisibilityEnd,      // Rocks (75m)
							5 => PropVisibilityEnd,      // Props (75m)
							_ => TreeVisibilityEnd       // Trees (115m)
						};
						float margin = data.Category switch
						{
							4 => 5.0f,
							3 => 8.0f,
							2 => 10.0f,
							5 => 10.0f,
							_ => 15.0f
						};

						sprite.VisibilityRangeEnd = endDist;
						sprite.VisibilityRangeEndMargin = margin;
						sprite.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
					}
				}

				// Collision stripping on unreachable distant entities
				if (!data.HasCollision)
				{
					var staticBody = instance.GetNodeOrNull<StaticBody3D>("StaticBody3D") ?? instance.FindChild("StaticBody3D", true, false) as StaticBody3D;
					if (staticBody != null)
					{
						staticBody.GetParent()?.RemoveChild(staticBody);
						staticBody.QueueFree();
					}
				}

				if (Engine.IsEditorHint())
				{
					instance.Owner = GetTree()?.EditedSceneRoot ?? this;
				}
			}
		}
	}

	private void UnloadCell(Vector2I coord)
	{
		if (!_activeCellNodes.TryGetValue(coord, out Node3D cellNode)) return;

		_activeCellNodes.Remove(coord);
		if (GodotObject.IsInstanceValid(cellNode))
		{
			ProceduralLayerNode.RemoveChild(cellNode);
			cellNode.QueueFree();
		}
	}

	private PackedScene GetPrefab(int category, int index)
	{
		PackedScene[] library = category switch
		{
			0 => TreePrefabs,
			1 => TreePrefabs,
			2 => RockPrefabs,
			3 => BushPrefabs,
			4 => FloraPrefabs,
			5 => PropPrefabs,
			_ => null
		};

		if (library != null && index >= 0 && index < library.Length)
		{
			return library[index];
		}
		return null;
	}

	private Vector3 GetActiveCameraPosition()
	{
		// 1. Check NighttCamera if CCTV feed is active
		var nightCam = GetTree()?.Root.FindChild("NighttCamera", true, false) as NighttCamera;
		if (nightCam != null && nightCam.IsNightMode && nightCam.CurrentNightCamera != null && nightCam.CurrentNightCamera.Current)
		{
			return nightCam.CurrentNightCamera.GlobalPosition;
		}

		// 2. Check viewport active Camera3D
		var viewportCam = GetViewport()?.GetCamera3D();
		if (viewportCam != null)
		{
			return viewportCam.GlobalPosition;
		}

		// 3. Fallback to CameraPivot or FarmCenter
		if (CameraPivot.Instance != null)
		{
			return CameraPivot.Instance.GlobalPosition;
		}

		return FarmCenter;
	}

	public static Vector2I WorldToCell(Vector3 worldPos, float cellSize)
	{
		return new Vector2I(
			Mathf.FloorToInt(worldPos.X / cellSize),
			Mathf.FloorToInt(worldPos.Z / cellSize)
		);
	}

	public static Vector2 CellToWorldCenter(Vector2I cell, float cellSize)
	{
		return new Vector2((cell.X + 0.5f) * cellSize, (cell.Y + 0.5f) * cellSize);
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

		// Fallback: If 30 attempts fell inside, project candidate radially outward until it clears
		Vector3 fallback = SampleAnnulus(rng, minRadius, maxRadius);
		Vector2 dir = new Vector2(fallback.X - FarmCenter.X, fallback.Z - FarmCenter.Z).Normalized();
		if (dir == Vector2.Zero) dir = Vector2.Right;

		float pushDist = Mathf.Max(
			Mathf.Abs((FarmBoundsMax.X - FarmBoundsMin.X) * 0.5f) + buffer,
			Mathf.Abs((FarmBoundsMax.Y - FarmBoundsMin.Y) * 0.5f) + buffer
		) * 1.42f;

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
}
