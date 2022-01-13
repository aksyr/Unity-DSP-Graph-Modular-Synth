using UnityEngine;
using System.Collections;
using Unity.Burst;
using System.Runtime.InteropServices;

[BurstCompile(CompileSynchronously = true)]
[StructLayout(LayoutKind.Sequential)]
public struct SchmittTrigger
{
	public bool _State;

	public void Reset()
	{
		_State = true;
	}

	/** Updates the state of the Schmitt Trigger given a value.
	Returns true if triggered, i.e. the value increases from 0 to 1.
	If different trigger thresholds are needed, use
		process(rescale(in, low, high, 0.f, 1.f))
	for example.
	*/
	public bool Process(float val)
	{
		if (_State)
		{
			// HIGH to LOW
			if (val <= 0.0f) {
				_State = false;
			}
		}
		else
		{
			// LOW to HIGH
			if (val >= 1.0f) {
				_State = true;
				return true;
			}
		}
		return false;
	}

	public bool IsHigh()
	{
		return _State;
	}
};