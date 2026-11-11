// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Interactive vertical ladder for treehouse entry and descent
//                   - Two-way climb transitions between ground level and elevated platform
//                   - Context-sensitive prompt display based on player position
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 11, 2026 | air : Implemented two-way ladder climbing between ground and treehouse
// =========================================================================================
using Godot;

public partial class InteractableLadder : InteractableBase
{
	[Export] public Vector3 GroundPosition = new(-1.66f, 1.0f, 4.2f);
	[Export] public Vector3 TreehousePosition = new(-1.66f, 4.35f, 1.8f);
	[Export] public Node3D GroundPoint;
	[Export] public Node3D TreehousePoint;
	[Export] public float TransitionDuration = 0.35f;

	private bool _isClimbing = false;

	public override string GetPrompt()
	{
		var player = GetTree().GetFirstNodeInGroup("Player") as Node3D;
		if (player != null)
		{
			Vector3 ground = GroundPoint?.GlobalPosition ?? GroundPosition;
			Vector3 top = TreehousePoint?.GlobalPosition ?? TreehousePosition;
			float distGround = player.GlobalPosition.DistanceSquaredTo(ground);
			float distHouse = player.GlobalPosition.DistanceSquaredTo(top);
			if (distGround < distHouse)
			{
				return "Press [E] to Climb Up into Treehouse";
			}
			return "Press [E] to Climb Down to Ground";
		}
		return "Press [E] to Use Ladder";
	}

	protected override void OnInteract(PlayerController player)
	{
		if (_isClimbing || player == null) return;

		Vector3 ground = GroundPoint?.GlobalPosition ?? GroundPosition;
		Vector3 top = TreehousePoint?.GlobalPosition ?? TreehousePosition;
		float distGround = player.GlobalPosition.DistanceSquaredTo(ground);
		float distHouse = player.GlobalPosition.DistanceSquaredTo(top);

		Vector3 targetPos = (distGround < distHouse) ? top : ground;

		_isClimbing = true;
		player.Velocity = Vector3.Zero;
		AudioManager.Sfx("foliage_rustle", -4f, 0.2f);

		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(player, "global_position", targetPos, TransitionDuration);
		tween.Finished += () =>
		{
			_isClimbing = false;
			player.Velocity = Vector3.Zero;
		};
	}
}
