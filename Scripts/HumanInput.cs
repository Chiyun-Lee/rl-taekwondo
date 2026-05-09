using System.Collections.Generic;
using Godot;

public partial class HumanInput : Node, ICharacterInput
{
	// How many same-direction notches within DashWindowMs counts as a whip → Dash
	[Export] public int   DashEventCount  = 5;
	[Export] public float DashWindowSec   = 0.1f;
	// How long after the last scroll event before a non-Dash commits as Step
	[Export] public float StepSettleSec   = 0.08f;
	[Export] public float DashCooldownSec = 0.25f;

	readonly Queue<ulong> _scrollTimes = new();
	float _firstScroll;
	bool  _stepPending;
	ulong _stepCommitMs;
	ulong _cooldownEndMs;

	int  _pendingLinear;
	int  _pendingSideStep;
	bool _pendingSwap;
	bool _pendingKick;

	public int  LinearInput         { get { var v = _pendingLinear;   _pendingLinear   = 0; return v; } }
	public int  SideStepInput       { get { var v = _pendingSideStep; _pendingSideStep = 0; return v; } }
	public bool SwapStanceTriggered { get { var v = _pendingSwap;     _pendingSwap     = false; return v; } }
	public bool KickTriggered       { get { var v = _pendingKick;     _pendingKick     = false; return v; } }

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
		SetProcess(false);  // only active while a scroll gesture is pending
	}

	public override void _Process(double delta)
	{
		if (NowMs() >= _stepCommitMs)
		{
			_pendingLinear = _firstScroll > 0f ? 1 : -1;
			_stepPending   = false;
			_scrollTimes.Clear();
			SetProcess(false);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.PhysicalKeycode == Key.Escape)
				Input.MouseMode = Input.MouseModeEnum.Visible;
			else if (key.PhysicalKeycode == Key.Space && !_pendingSwap)
				_pendingSwap = true;
			else if (key.PhysicalKeycode == Key.S && !_pendingKick)
				_pendingKick = true;
		}

		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			switch (mb.ButtonIndex)
			{
				case MouseButton.WheelUp:   HandleScroll(mb.Factor);  break;
				case MouseButton.WheelDown: HandleScroll(-mb.Factor); break;
				case MouseButton.Left:
					if (Input.MouseMode == Input.MouseModeEnum.Captured)
					{ if (_pendingSideStep == 0) _pendingSideStep = -1; }
					else Input.MouseMode = Input.MouseModeEnum.Captured;
					break;
				case MouseButton.Right:
					if (Input.MouseMode == Input.MouseModeEnum.Captured)
					{ if (_pendingSideStep == 0) _pendingSideStep = 1; }
					else Input.MouseMode = Input.MouseModeEnum.Captured;
					break;
			}
		}
	}

	static ulong NowMs() => Time.GetTicksMsec();

	void HandleScroll(float value)
	{
		ulong now = NowMs();
		if (now < _cooldownEndMs) { ClearScroll(); return; }

		// Direction change resets the gesture
		if (_scrollTimes.Count > 0 && Mathf.Sign(value) != Mathf.Sign(_firstScroll))
			ClearScroll();

		_firstScroll = value;
		_scrollTimes.Enqueue(now);

		// Prune events outside the rolling window
		ulong windowMs = (ulong)(DashWindowSec * 1000f);
		while (_scrollTimes.Count > 0 && now - _scrollTimes.Peek() > windowMs)
			_scrollTimes.Dequeue();

		if (_scrollTimes.Count >= DashEventCount)
		{
			_pendingLinear = value > 0f ? 2 : -2;
			_cooldownEndMs = now + (ulong)(DashCooldownSec * 1000f);
			_stepPending   = false;
			_scrollTimes.Clear();
			SetProcess(false);
		}
		else
		{
			// Extend the step commit deadline on each new event
			_stepPending  = true;
			_stepCommitMs = now + (ulong)(StepSettleSec * 1000f);
			SetProcess(true);
		}
	}

	void ClearScroll()
	{
		_scrollTimes.Clear();
		_stepPending   = false;
		_firstScroll   = 0f;
		_pendingLinear = 0;  // prevent a committed step from leaking if direction resets before it's read
		SetProcess(false);
	}
}
