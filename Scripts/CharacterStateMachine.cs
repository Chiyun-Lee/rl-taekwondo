using Godot;

// Reads ICharacterInput each physics tick and drives the character state machine.
// Owns all combo timing. PlayerMovement handles body physics; this handles state+animation.
//
// State graph:
//   Idle ↔ Step / Dash / SideStep  (movement, handled by PlayerMovement)
//   Idle → RaisedFrontLeg → FrontSideKickToBody [→ SlidingFrontSideKickToBody] → Idle
//   Idle → RaisedRearLeg  → (kick window) → RearTurningKickToBody [→ Sliding] → RaisedFrontLeg→Idle
//                         → (window expires)→ LowerRear → Idle  (stance flipped)
public partial class CharacterStateMachine : Node
{
	[Export] public float KickComboWindowSec = 0.5f;   // time after RaisedRearLeg to input kick
	[Export] public float SlidingWindowSec   = 0.3f;   // time after kick starts to chain dash→slide

	[ExportGroup("Energy")]
	[Export] public float MaxEnergy          = 15f;
	[Export] public float EnergyRegenPerSec  = 1f;
	[Export] public float CostFrontKick      = 2f;
	[Export] public float CostRearKick       = 2f;
	[Export] public float CostSlidingKick    = 3f;

	public enum State
	{
		Idle,
		RaisedFrontLeg,
		FrontSideKickToBody,
		SlidingFrontSideKickToBody,
		RaisedRearLeg,
		RearTurningKickToBody,
		SlidingTurningKickToBody,
	}

	public State Current { get; private set; } = State.Idle;
	public float Energy  { get; private set; }

	ICharacterInput _input;
	PlayerMovement  _movement;
	float           _comboTimer;
	bool            _busy;

	// Signals emitted so PlayerMovement / AnimationTree can react
	[Signal] public delegate void StateChangedEventHandler(int newState);
	[Signal] public delegate void EnergyChangedEventHandler(float energy, float max);

	public override void _Ready()
	{
		_movement = GetParent<PlayerMovement>();
		_input    = _movement.Input;  // already resolved by PlayerMovement._Ready
		Energy    = MaxEnergy;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		RegenerateEnergy(dt);
		TickComboTimer(dt);
		ProcessInput();
	}

	// ── Energy ───────────────────────────────────────────────────────────────

	void RegenerateEnergy(float dt)
	{
		if (Energy >= MaxEnergy) return;
		Energy = Mathf.Min(Energy + EnergyRegenPerSec * dt, MaxEnergy);
		EmitSignal(SignalName.EnergyChanged, Energy, MaxEnergy);
	}

	bool TrySpendEnergy(float cost)
	{
		if (Energy < cost) return false;
		Energy -= cost;
		EmitSignal(SignalName.EnergyChanged, Energy, MaxEnergy);
		return true;
	}

	// ── Combo timer ──────────────────────────────────────────────────────────

	void TickComboTimer(float dt)
	{
		if (_comboTimer <= 0f) return;
		_comboTimer -= dt;
		if (_comboTimer <= 0f)
			OnComboWindowExpired();
	}

	void OnComboWindowExpired()
	{
		if (Current == State.RaisedRearLeg)
		{
			// No kick input came — lower the leg (swap stance)
			ActionLog.Log("Swap Stance...");
			_movement.TriggerSwapStance();
			Transition(State.Idle);
		}
	}

	// ── Input ────────────────────────────────────────────────────────────────

	void ProcessInput()
	{
		// Ownership: KickTriggered and SwapStanceTriggered always belong to this class.
		// LinearInput and SideStepInput normally belong to PlayerMovement, but while busy
		// we consume them here so PlayerMovement sees zeros and cannot start a move.
		bool kick = _input.KickTriggered;
		bool swap = _input.SwapStanceTriggered;

		if (_busy)
		{
			int linear = _input.LinearInput;
			_ = _input.SideStepInput;

			// Check sliding chain — only within the combo window
			if (_comboTimer > 0f
				&& (Current == State.FrontSideKickToBody || Current == State.RearTurningKickToBody)
				&& (linear == 2 || linear == -2))
				TrySlideKick();
			return;
		}

		switch (Current)
		{
			case State.Idle:
				if (kick && TrySpendEnergy(CostFrontKick))
				{
					ActionLog.Log("Front Side Kick");
					Current = State.RaisedFrontLeg;  // transitionary — skip signal, no observer
					Transition(State.FrontSideKickToBody);
					_comboTimer = SlidingWindowSec;
					_busy = true;
				}
				else if (swap)
				{
					ActionLog.Log("Raise Rear...");
					Transition(State.RaisedRearLeg);
					_comboTimer = KickComboWindowSec;
				}
				break;

			case State.RaisedRearLeg:
				if (kick && TrySpendEnergy(CostRearKick))
				{
					_comboTimer = SlidingWindowSec;
					ActionLog.Log("Rear Turning Kick");
					Transition(State.RearTurningKickToBody);
					_busy = true;
				}
				break;
		}
	}

	void TrySlideKick()
	{
		if (!TrySpendEnergy(CostSlidingKick - (Current == State.FrontSideKickToBody ? CostFrontKick : CostRearKick)))
		{
			ActionLog.Log("Not enough energy to slide");
			return;
		}
		if (Current == State.FrontSideKickToBody)
		{
			ActionLog.Log("→ Sliding Front Side Kick");
			Transition(State.SlidingFrontSideKickToBody);
		}
		else
		{
			ActionLog.Log("→ Sliding Turning Kick");
			Transition(State.SlidingTurningKickToBody);
		}
	}

	// ── Transitions ──────────────────────────────────────────────────────────

	void Transition(State next)
	{
		Current = next;
		EmitSignal(SignalName.StateChanged, (int)next);
	}

	// Called by AnimationPlayer method tracks at animation end
	public void OnKickAnimationComplete()
	{
		_busy       = false;
		_comboTimer = 0f;
		Transition(State.Idle);
		ActionLog.Log("Idle");
	}
}
