using System.Collections.Generic;
using package.patapon.core;
using StormiumTeam.GameBase;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;

namespace Patapon4TLB.Default
{
	[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
	[UpdateAfter(typeof(GhostReceiveSystemGroup))]
	[UpdateAfter(typeof(GhostUpdateSystemGroup))]
	public class RhythmEngineGroup : ComponentSystemGroup
	{
		private RhythmEngineBeginBarrier m_BeginBarrier;
		private RhythmEngineEndBarrier m_EndBarrier;

		private FlowRhythmBeatEventProvider m_BeatEventProvider;
		private FlowRhythmPressureEventProvider m_PressureEventProvider;

		private List<ComponentSystemBase> m_SystemsInGroup = new List<ComponentSystemBase>();

		public override IEnumerable<ComponentSystemBase> Systems => m_SystemsInGroup;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_BeginBarrier = World.GetOrCreateSystem<RhythmEngineBeginBarrier>();
			m_EndBarrier = World.GetOrCreateSystem<RhythmEngineEndBarrier>();

			m_BeatEventProvider = World.GetOrCreateSystem<FlowRhythmBeatEventProvider>();
			m_PressureEventProvider = World.GetOrCreateSystem<FlowRhythmPressureEventProvider>();

			if (World.GetExistingSystem<ServerSimulationSystemGroup>() != null)
			{
				World.GetOrCreateSystem<RegisterDefaultSequenceCommands>();
			}

			SortSystemUpdateList();
		}

		protected override void OnUpdate()
		{
			m_BeginBarrier.Update();
			
			m_BeatEventProvider.FlushDelayedEntities();
			m_PressureEventProvider.FlushDelayedEntities();
			
			base.OnUpdate();
			
			m_BeatEventProvider.FlushDelayedEntities();
			m_PressureEventProvider.FlushDelayedEntities();
			
			m_EndBarrier.Update();
		}

		public override void SortSystemUpdateList()
		{
			base.SortSystemUpdateList();
			m_SystemsInGroup = new List<ComponentSystemBase>(1 + m_systemsToUpdate.Count + 1);
			m_SystemsInGroup.Add(m_BeginBarrier);
			m_SystemsInGroup.AddRange(m_systemsToUpdate);
			m_SystemsInGroup.Add(m_EndBarrier);
		}
	}

	[DisableAutoCreation]
	public class RhythmEngineBeginBarrier : EntityCommandBufferSystem
	{
	}
	
	[DisableAutoCreation]
	public class RhythmEngineEndBarrier : EntityCommandBufferSystem
	{
	}

	[UpdateInGroup(typeof(RhythmEngineGroup))]
	public class RhythmEngineFlowSystem : FlowRhythmEngine
	{
	}
}