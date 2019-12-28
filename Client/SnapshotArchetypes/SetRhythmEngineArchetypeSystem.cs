using package.stormiumteam.shared.ecs;
using Patapon.Mixed.GamePlay.RhythmEngine;
using Patapon.Mixed.RhythmEngine;
using Patapon.Mixed.RhythmEngine.Flow;
using Revolution;
using StormiumTeam.GameBase;
using Unity.Entities;
using Unity.Jobs;

namespace SnapshotArchetypes
{
	[UpdateInGroup(typeof(AfterSnapshotIsAppliedSystemGroup))]
	public class SetRhythmEngineArchetypeSystem : ComponentSystem
	{
		public struct IsSet : IComponentData
		{
		}

		private EntityQuery m_RhythmEngineWithoutArchetype;

		protected override void OnCreate()
		{
			m_RhythmEngineWithoutArchetype = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(RhythmEngineDescription), typeof(FlowEngineProcess)},
				None = new ComponentType[] {typeof(IsSet)}
			});
		}

		protected override void OnUpdate()
		{
			EntityManager.AddComponent(m_RhythmEngineWithoutArchetype, typeof(RhythmEngineCommandProgression));
			EntityManager.AddComponent(m_RhythmEngineWithoutArchetype, typeof(GamePredictedCommandState));
			EntityManager.AddComponent(m_RhythmEngineWithoutArchetype, typeof(GameComboPredictedClient));
			EntityManager.AddComponent(m_RhythmEngineWithoutArchetype, typeof(IsSet));
		}
	}
	
	[UpdateInGroup(typeof(AfterSnapshotIsAppliedSystemGroup))]
	public class UpdateLocalRhythmEngineSystem : JobGameBaseSystem
	{
		private EndSimulationEntityCommandBufferSystem m_EndBarrier;
		private EntityQuery m_Query;
		
		protected override void OnCreate()
		{
			base.OnCreate();

			m_EndBarrier = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
			
			m_Query = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(SetRhythmEngineArchetypeSystem.IsSet), typeof(HasAuthorityFromServer)},
				None = new ComponentType[] {typeof(FlowSimulateProcess)}
			});
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			m_EndBarrier.CreateCommandBuffer().AddComponent(m_Query, typeof(FlowSimulateProcess));
			m_EndBarrier.AddJobHandleForProducer(inputDeps);

			return inputDeps;
		}
	}
}