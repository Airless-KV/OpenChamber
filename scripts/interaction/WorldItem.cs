// =========================================================================================
// Original Author : air (Airless-KV) - Nov 13, 2026
// Responsibilities: - 3D world entity representation for physical items (Flowers, Seeds, Planks, Car Battery, Crowbar)
//                   - Visual meshes/sprites and collision detection for raycast targeting
//                   - Dynamic hover prompt dispatch ("[Space] Grab <ItemName>")
//                   - Static factory method for world item spawning and drop physics
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 13, 2026 | air : Created physical 3D WorldItem entity for tactile carry system
// - Nov 13, 2026 | air : Added ItemType.CarBattery with custom 3D model, terminal posts, and battery health
// - Nov 14, 2026 | air : Added ItemType.Crowbar with 3D rusted iron prybar model and instant barricade dismantling
// =========================================================================================
using Godot;
using System;

public enum ItemType
{
	Flower,
	Seed,
	Plank,
	CarBattery,
	Crowbar
}

public partial class WorldItem : InteractableBase
{
	[Export] public ItemType ItemType = ItemType.Seed;
	[Export] public int FlowerTier = 1;
	[Export] public int BatteryHealth = 100;

	private Node3D _visualRoot;
	private float _bobTimer;
	private Vector3 _basePos;
	private bool _initializedBasePos = false;

	public static readonly string[] FlowerNames =
	{
		"Tier 1 Flower (Daisy)",
		"Tier 2 Flower (Sunburst)",
		"Tier 3 Flower (Sunset Coral)",
		"Tier 4 Flower (Crimson Rose)",
		"Tier 5 Flower (Emerald Bloom)"
	};

	public static readonly int[] FlowerPrices = { 3, 8, 20, 45, 100 };

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("WorldItems");
		BuildVisuals();
		_basePos = GlobalPosition;
		_bobTimer = (float)GD.RandRange(0.0, Math.PI * 2);
	}

	public override void _Process(double delta)
	{
		if (!_initializedBasePos)
		{
			_basePos = GlobalPosition;
			_initializedBasePos = true;
		}

		_bobTimer += (float)delta * 2.5f;
		float bobY = Mathf.Sin(_bobTimer) * 0.08f;
		if (_visualRoot != null)
		{
			_visualRoot.Position = new Vector3(0, bobY + 0.25f, 0);
			_visualRoot.RotateY((float)delta * 1.2f);
		}
	}

	public string GetItemDisplayName()
	{
		return ItemType switch
		{
			ItemType.Plank => "Wooden Plank",
			ItemType.Seed => "Level 1 Seed",
			ItemType.Flower => FlowerNames[Mathf.Clamp(FlowerTier - 1, 0, 4)],
			ItemType.CarBattery => $"Car Battery ({BatteryHealth}% HP)",
			ItemType.Crowbar => "Rusted Crowbar",
			_ => "Item"
		};
	}

	public int GetSellValue()
	{
		if (ItemType == ItemType.Flower)
			return FlowerPrices[Mathf.Clamp(FlowerTier - 1, 0, 4)];
		if (ItemType == ItemType.Plank)
			return 2;
		if (ItemType == ItemType.Seed)
			return 1;
		if (ItemType == ItemType.CarBattery)
			return 3;
		if (ItemType == ItemType.Crowbar)
			return 2;
		return 0;
	}

	public override string GetPrompt()
	{
		return $"[Space] Grab {GetItemDisplayName()}";
	}

	public override bool CanInteract(PlayerController player) => true;

	protected override void OnInteract(PlayerController player)
	{
		player?.GetNodeOrNull<CarriedInventory>("CarriedInventory")?.TryGrab(this);
	}

	private void BuildVisuals()
	{
		// Clean existing visual children if any
		if (_visualRoot != null && IsInstanceValid(_visualRoot))
		{
			_visualRoot.QueueFree();
		}

		_visualRoot = new Node3D { Name = "VisualRoot" };
		AddChild(_visualRoot);

		// Ensure collision shape exists
		var col = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (col == null)
		{
			col = new CollisionShape3D { Name = "CollisionShape3D" };
			var shape = new SphereShape3D { Radius = 0.6f };
			col.Shape = shape;
			col.Position = new Vector3(0, 0.35f, 0);
			AddChild(col);
		}

		switch (ItemType)
		{
			case ItemType.Plank:
				BuildPlankVisual();
				break;
			case ItemType.Seed:
				BuildSeedVisual();
				break;
			case ItemType.Flower:
				BuildFlowerVisual();
				break;
			case ItemType.CarBattery:
				BuildCarBatteryVisual();
				break;
			case ItemType.Crowbar:
				BuildCrowbarVisual();
				break;
		}
	}

	private void BuildPlankVisual()
	{
		var meshInstance = new MeshInstance3D();
		var box = new BoxMesh { Size = new Vector3(1.1f, 0.12f, 0.32f) };
		meshInstance.Mesh = box;

		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.58f, 0.38f, 0.22f),
			Roughness = 0.85f
		};
		meshInstance.MaterialOverride = mat;
		_visualRoot.AddChild(meshInstance);
	}

	private void BuildSeedVisual()
	{
		// A stylized 3D seed pouch / sack
		var sackMesh = new MeshInstance3D();
		var cylinder = new CylinderMesh
		{
			TopRadius = 0.22f,
			BottomRadius = 0.26f,
			Height = 0.38f
		};
		sackMesh.Mesh = cylinder;

		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.85f, 0.72f, 0.42f),
			Roughness = 0.65f
		};
		sackMesh.MaterialOverride = mat;
		_visualRoot.AddChild(sackMesh);

		// Tie collar on top of seed pouch
		var tie = new MeshInstance3D();
		var torus = new TorusMesh
		{
			InnerRadius = 0.14f,
			OuterRadius = 0.23f
		};
		tie.Mesh = torus;
		tie.Position = new Vector3(0, 0.18f, 0);
		var tieMat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.68f, 0.32f) };
		tie.MaterialOverride = tieMat;
		_visualRoot.AddChild(tie);
	}

	private void BuildFlowerVisual()
	{
		int tier = Mathf.Clamp(FlowerTier, 1, 5);
		string texPath = $"res://assets/textures/flowers/flower_lvl{tier}.png";

		var sprite = new Sprite3D
		{
			Name = "FlowerSprite",
			Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
			Shaded = true,
			AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			PixelSize = 0.0035f
		};

		if (ResourceLoader.Exists(texPath))
		{
			sprite.Texture = GD.Load<Texture2D>(texPath);
		}

		if (tier == 5)
		{
			sprite.Modulate = new Color(1.2f, 1.2f, 1.1f, 1f);
		}

		_visualRoot.AddChild(sprite);
	}

	private void BuildCarBatteryVisual()
	{
		// 1. Battery casing body
		var body = new MeshInstance3D();
		body.Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.36f, 0.32f) };
		body.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.14f, 0.15f, 0.17f),
			Roughness = 0.6f
		};
		_visualRoot.AddChild(body);

		// 2. Positive Terminal Post (Red)
		var posTerm = new MeshInstance3D();
		posTerm.Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.05f, Height = 0.09f };
		posTerm.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.15f, 0.15f) };
		posTerm.Position = new Vector3(-0.16f, 0.21f, 0f);
		_visualRoot.AddChild(posTerm);

		// 3. Negative Terminal Post (Blue)
		var negTerm = new MeshInstance3D();
		negTerm.Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.05f, Height = 0.09f };
		negTerm.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.38f, 0.88f) };
		negTerm.Position = new Vector3(0.16f, 0.21f, 0f);
		_visualRoot.AddChild(negTerm);

		// 4. Top Carrying Handle
		var handle = new MeshInstance3D();
		handle.Mesh = new BoxMesh { Size = new Vector3(0.38f, 0.04f, 0.08f) };
		handle.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.32f) };
		handle.Position = new Vector3(0f, 0.21f, 0f);
		_visualRoot.AddChild(handle);

		// 5. Yellow Hazard Warning Label
		var label = new MeshInstance3D();
		label.Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.16f, 0.01f) };
		label.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.95f, 0.82f, 0.15f),
			Roughness = 0.4f
		};
		label.Position = new Vector3(0f, 0f, 0.165f);
		_visualRoot.AddChild(label);
	}

	private void BuildCrowbarVisual()
	{
		var rustMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.38f, 0.24f, 0.18f),
			Metallic = 0.75f,
			Roughness = 0.65f
		};

		// 1. Main shaft
		var shaft = new MeshInstance3D();
		shaft.Mesh = new CylinderMesh { TopRadius = 0.024f, BottomRadius = 0.024f, Height = 0.72f };
		shaft.MaterialOverride = rustMat;
		shaft.RotationDegrees = new Vector3(25f, 0f, 40f);
		_visualRoot.AddChild(shaft);

		// 2. Curved gooseneck hook
		var hook = new MeshInstance3D();
		hook.Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.22f };
		hook.MaterialOverride = rustMat;
		hook.Position = new Vector3(0.24f, 0.28f, -0.08f);
		hook.RotationDegrees = new Vector3(-35f, 20f, -50f);
		_visualRoot.AddChild(hook);

		// 3. Pry claw chisel head
		var claw = new MeshInstance3D();
		claw.Mesh = new BoxMesh { Size = new Vector3(0.065f, 0.015f, 0.08f) };
		claw.MaterialOverride = rustMat;
		claw.Position = new Vector3(0.32f, 0.32f, -0.12f);
		claw.RotationDegrees = new Vector3(-35f, 20f, -50f);
		_visualRoot.AddChild(claw);

		// 4. Straight end wedge / chisel
		var wedge = new MeshInstance3D();
		wedge.Mesh = new BoxMesh { Size = new Vector3(0.055f, 0.015f, 0.07f) };
		wedge.MaterialOverride = rustMat;
		wedge.Position = new Vector3(-0.25f, -0.28f, 0.09f);
		wedge.RotationDegrees = new Vector3(25f, 0f, 40f);
		_visualRoot.AddChild(wedge);
	}

	public static WorldItem Spawn(Node parent, Vector3 position, ItemType type, int tier = 1, int batteryHealth = 100)
	{
		var item = new WorldItem
		{
			ItemType = type,
			FlowerTier = tier,
			BatteryHealth = Mathf.Clamp(batteryHealth, 0, 100),
			GlobalPosition = position
		};
		parent.AddChild(item);
		item.GlobalPosition = position;
		return item;
	}
}
