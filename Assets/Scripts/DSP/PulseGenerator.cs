using UnityEngine;
using System.Collections;
using Unity.Burst;
using System.Runtime.InteropServices;

[BurstCompile(CompileSynchronously = true)]
[StructLayout(LayoutKind.Sequential)]
public struct PulseGenerator
{
	public float _Remaining;

	/** Immediately disables the pulse */
	public void Reset()
	{
		_Remaining = 0f;
	}

	/** Advances the state by `deltaTime`. Returns whether the pulse is in the HIGH state. */
	public bool Process(float deltaTime)
	{
		if (_Remaining > 0f)
		{
			_Remaining -= deltaTime;
			return true;
		}
		return false;
	}

	/** Begins a trigger with the given `duration`. */
	public void Trigger(float duration = 1e-3f)
	{
		// Keep the previous pulse if the existing pulse will be held longer than the currently requested one.
		if (duration > _Remaining)
		{
			_Remaining = duration;
		}
	}
};
