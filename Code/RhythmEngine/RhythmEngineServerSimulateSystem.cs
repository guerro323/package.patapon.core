using package.patapon.core;
using package.patapon.def.Data;
using StormiumTeam.GameBase;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Patapon4TLB.Default
{
	[UpdateInGroup(typeof(RhythmEngineGroup))]
	public class RhythmEngineServerSimulateSystem : JobGameBaseSystem
	{
		private struct SimulateJob : IJobForEachWithEntity<RhythmEngineProcess, RhythmEngineState, RhythmEngineSettings>
		{
			public uint CurrentTime;

			[ReadOnly]
			public int FrameCount;

			[NativeDisableParallelForRestriction]
			public NativeList<FlowRhythmBeatEventProvider.Create> CreateBeatEventList;

			public EntityCommandBuffer.Concurrent EntityCommandBuffer;

			private void NonBurst_ThrowWarning(Entity entity)
			{
				Debug.LogWarning($"Engine '{entity}' had a FlowRhythmEngineSettingsData.BeatInterval of 0 (or less), this is not accepted.");
			}

			public void Execute(Entity entity, int index, ref RhythmEngineProcess process, ref RhythmEngineState state, [ReadOnly] ref RhythmEngineSettings settings)
			{
				process.TimeTick = (int)(CurrentTime - process.StartTime);
				if (settings.BeatInterval <= 0.0001f)
				{
					NonBurst_ThrowWarning(entity);
					return;
				}

				var previousBeat = process.Beat;

				if (process.TimeTick != 0)
				{
					process.Beat = process.TimeTick / settings.BeatInterval;
				}
				else
				{
					process.Beat = 0;
				}

				state.IsNewBeat = false;

				var beatDiff = math.abs(previousBeat - process.Beat);
				if (beatDiff == 0)
					return;

				state.IsNewBeat = true;

				if (beatDiff > 1)
				{
					// what to do?
				}

				CreateBeatEventList.Add(new FlowRhythmBeatEventProvider.Create
				{
					Target     = entity,
					FrameCount = FrameCount,
					Beat       = process.Beat
				});
			}
		}

		private RhythmEngineEndBarrier      m_EndBarrier;
		private FlowRhythmBeatEventProvider m_BeatEventProvider;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_EndBarrier        = World.GetOrCreateSystem<RhythmEngineEndBarrier>();
			m_BeatEventProvider = World.GetOrCreateSystem<FlowRhythmBeatEventProvider>();

		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			var topGroup = World.GetExistingSystem<ServerSimulationSystemGroup>();
			if (topGroup == null)
				return inputDeps; // not a server

			inputDeps = new SimulateJob
			{
				CurrentTime         = World.GetExistingSystem<SynchronizedSimulationTimeSystem>().Value.Predicted,
				FrameCount          = (int) topGroup.ServerTick,
				CreateBeatEventList = m_BeatEventProvider.GetEntityDelayedList(),
				EntityCommandBuffer = m_EndBarrier.CreateCommandBuffer().ToConcurrent()
			}.Schedule(this, inputDeps);

			m_BeatEventProvider.AddJobHandleForProducer(inputDeps);
			m_EndBarrier.AddJobHandleForProducer(inputDeps);

			return inputDeps;
		}
	}
}