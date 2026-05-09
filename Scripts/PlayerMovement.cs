using Godot;

public partial class PlayerMovement : CharacterBody3D
{
	[Export] public Node3D CameraTarget;
	[Export] public Node3D CharacterVisual;

	[ExportGroup("Distances (metres)")]
	[Export] public float StepDistance  = 0.4f;
	[Export] public float DashDistance  = 1.2f;
	[Export] public float SideStepChord = 0.8f;

	[ExportGroup("Smoothing")]
	[Export] public float MoveDuration   = 0.15f;
	[Export] public float PauseAfterMove = 0.08f;

	[ExportGroup("Facing")]
	[Export] public float TrackingSpeed = 720f;

	// Exposed so CharacterStateMachine can read the same input source without its own lookup.
	public ICharacterInput Input { get; private set; }

	AnimationTree _animTree;
	bool          _moving;
	bool          _mirrored;

	enum MovePhase { Idle, Moving, Pausing }
	MovePhase _phase      = MovePhase.Idle;
	Vector3   _moveDelta;
	float     _moveElapsed;
	float     _movePrevEase;
	float     _pauseClock;

	static readonly float Gravity = ProjectSettings
		.GetSetting("physics/3d/default_gravity").As<float>();

	static readonly StringName SwapStanceParam =
		$"parameters/conditions/{AnimationSetup.SwapStanceCondition}";

	public override void _Ready()
	{
		// Discover whichever input implementation (HumanInput or AgentInput) is in the scene.
		foreach (Node child in GetChildren())
			if (child is ICharacterInput input) { Input = input; break; }

		_animTree = GetNodeOrNull<AnimationTree>("CharacterVisual/AnimationTree");

		CameraTarget    ??= GetParent()?.GetNodeOrNull<Node3D>("CameraTarget");
		CharacterVisual ??= GetNodeOrNull<Node3D>("CharacterVisual");
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (!IsOnFloor())
			Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);
		else if (Velocity.Y < 0f)
			Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);

		Vector3 hVel = TickMove(dt);
		Velocity = new Vector3(hVel.X, Velocity.Y, hVel.Z);
		MoveAndSlide();

		if (!_moving && Input != null)
		{
			int linear = Input.LinearInput;
			int side   = Input.SideStepInput;
			// KickTriggered and SwapStanceTriggered are consumed by CharacterStateMachine

			if (linear != 0)
			{
				ActionLog.Log(linear ==  2 ? "Dash Forward" :
							  linear == -2 ? "Dash Back"    :
							  linear ==  1 ? "Step Forward"  : "Step Back");
				BeginMove(LinearDelta(linear));
			}
			else if (side != 0)
			{
				ActionLog.Log(side == -1 ? "Sidestep Left" : "Sidestep Right");
				BeginMove(SideStepDelta(side));
			}
		}

		FaceTarget(dt);
	}

	// ── Smooth move state machine ─────────────────────────────────────────────

	void BeginMove(Vector3 delta)
	{
		if (delta == Vector3.Zero) return;
		_moveDelta    = delta;
		_moveElapsed  = 0f;
		_movePrevEase = 0f;
		_phase        = MovePhase.Moving;
		_moving       = true;
	}

	Vector3 TickMove(float dt)
	{
		switch (_phase)
		{
			case MovePhase.Moving:
				_moveElapsed += dt;
				float t    = Mathf.Clamp(_moveElapsed / MoveDuration, 0f, 1f);
				float ease = Mathf.SmoothStep(0f, 1f, t);
				Vector3 step = _moveDelta * (ease - _movePrevEase);
				_movePrevEase = ease;

				if (_moveElapsed >= MoveDuration)
				{
					// SmoothStep reaches 1.0 only asymptotically; add the leftover so we
					// always land exactly at the target distance regardless of frame timing.
					Vector3 remainder = _moveDelta * (1f - ease);
					step += remainder;
					_phase = PauseAfterMove > 0f ? MovePhase.Pausing : MovePhase.Idle;
					if (_phase == MovePhase.Pausing) _pauseClock = 0f;
					else _moving = false;
				}

				return dt > 0f ? step / dt : Vector3.Zero;

			case MovePhase.Pausing:
				_pauseClock += dt;
				if (_pauseClock >= PauseAfterMove)
				{
					_phase  = MovePhase.Idle;
					_moving = false;
				}
				return Vector3.Zero;

			default:
				return Vector3.Zero;
		}
	}

	// ── Stance swap — called by CharacterStateMachine ─────────────────────────

	public void TriggerSwapStance()
	{
		if (_animTree != null)
		{
			// Block movement for the duration of the animation; OnStanceSwapComplete clears it.
			_moving = true;
			_animTree.Set(SwapStanceParam, true);
		}
		else
		{
			// No AnimationTree yet — flip instantly so the stance is never stuck.
			OnStanceMidpoint();
		}
	}

	// Called by AnimationPlayer method track at start of LowerRear
	public void OnStanceMidpoint()
	{
		_mirrored = !_mirrored;
		if (CharacterVisual != null)
			CharacterVisual.Scale = new Vector3(_mirrored ? -1f : 1f, 1f, 1f);
		ActionLog.Log(_mirrored ? "→ Southpaw" : "→ Orthodox");
	}

	// Called by AnimationPlayer method track at end of LowerRear
	public void OnStanceSwapComplete() => _moving = false;

	// ── Facing ────────────────────────────────────────────────────────────────

	void FaceTarget(float dt)
	{
		if (CameraTarget == null) return;
		Vector3 dir = CameraTarget.GlobalPosition - GlobalPosition;
		dir.Y = 0f;
		if (dir.LengthSquared() < 0.001f) return;

		Quaternion currentQ = GlobalTransform.Basis.GetRotationQuaternion();
		Quaternion targetQ  = Basis.LookingAt(dir.Normalized(), Vector3.Up)
								   .GetRotationQuaternion();

		float angle = currentQ.AngleTo(targetQ);
		if (angle < 0.00873f) return;

		float maxAngle = Mathf.DegToRad(TrackingSpeed) * dt;
		float t = Mathf.Min(maxAngle / angle, 1f);
		GlobalTransform = new Transform3D(
			new Basis(currentQ.Slerp(targetQ, t)),
			GlobalPosition);
	}

	// ── Delta calculations ────────────────────────────────────────────────────

	Vector3 ForwardDir()
	{
		if (CameraTarget != null)
		{
			Vector3 d = CameraTarget.GlobalPosition - GlobalPosition;
			d.Y = 0f;
			if (d.LengthSquared() > 0.001f) return d.Normalized();
		}
		return -GlobalTransform.Basis.Z;
	}

	Vector3 LinearDelta(int input)
	{
		float dist = Mathf.Abs(input) == 2 ? DashDistance : StepDistance;
		int   sign = input > 0 ? 1 : -1;
		return ForwardDir() * dist * sign;
	}

	Vector3 SideStepDelta(int direction)
	{
		if (CameraTarget == null)
		{
			Vector3 right = GlobalTransform.Basis.X;
			right.Y = 0f;
			return right.Normalized() * SideStepChord * direction;
		}
		// Move along the arc of a circle centred on the opponent so the facing distance
		// stays constant. SideStepChord is the straight-line distance; convert to arc angle
		// via the chord-length formula: chord = 2r·sin(θ/2) → θ = 2·arcsin(chord/2r).
		Vector3 offset = GlobalPosition - CameraTarget.GlobalPosition;
		offset.Y = 0f;
		float radius = offset.Length();
		if (radius < 0.01f) return Vector3.Zero;

		float sinHalf  = Mathf.Clamp(SideStepChord / (2f * radius), -1f, 1f);
		float angle    = 2f * Mathf.Asin(sinHalf) * direction;
		var   rotation = new Quaternion(Vector3.Up, angle);
		Vector3 rotated = rotation * offset;
		Vector3 dest    = CameraTarget.GlobalPosition + rotated.Normalized() * radius;
		dest.Y          = GlobalPosition.Y;
		return dest - GlobalPosition;
	}
}
