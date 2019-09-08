using Patapon4TLB.Core;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.Components;
using StormiumTeam.GameBase.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Revolution.NetCode;
using Unity.Transforms;

namespace Patapon4TLB.Default
{
	public struct MarchAbility : IComponentData
	{
		public float AccelerationFactor;
		public float Delta;
	}

	[UpdateInGroup(typeof(ActionSystemGroup))]
	public class MarchAbilitySystem : JobGameBaseSystem
	{
		[BurstCompile]
		private struct JobProcess : IJobForEachWithEntity<Owner, RhythmAbilityState, MarchAbility, Relative<UnitTargetDescription>>
		{
			public float DeltaTime;

			[ReadOnly] public ComponentDataFromEntity<UnitTargetControlTag> UnitTargetControlFromEntity;

			[NativeDisableParallelForRestriction] public ComponentDataFromEntity<Translation>   TranslationFromEntity;
			[ReadOnly] public ComponentDataFromEntity<UnitPlayState> UnitPlayStateFromEntity;
			[ReadOnly] public ComponentDataFromEntity<GroundState>   GroundStateFromEntity;
			[ReadOnly] public ComponentDataFromEntity<UnitDirection> UnitDirectionFromEntity;

			[ReadOnly] public ComponentDataFromEntity<UnitTargetOffset> TargetOffsetFromEntity;

			[NativeDisableParallelForRestriction] public ComponentDataFromEntity<UnitControllerState> UnitControllerStateFromEntity;
			[NativeDisableParallelForRestriction] public ComponentDataFromEntity<Velocity>            VelocityFromEntity;

			public void Execute(Entity                                         entity, int              _, [ReadOnly] ref Owner owner,
			                    [ReadOnly] ref RhythmAbilityState              state,  ref MarchAbility marchAbility,
			                    [ReadOnly] ref Relative<UnitTargetDescription> relativeTarget)
			{
				if (!state.IsActive)
				{
					marchAbility.Delta = 0.0f;
					return;
				}

				var targetOffset  = TargetOffsetFromEntity[owner.Target];
				var groundState   = GroundStateFromEntity[owner.Target];
				var unitPlayState = UnitPlayStateFromEntity[owner.Target];
				if (state.Combo.IsFever && state.Combo.Score >= 50)
				{
					unitPlayState.MovementSpeed *= 1.2f;
				}

				if (!groundState.Value)
					return;

				marchAbility.Delta += DeltaTime;

				var   targetPosition = TranslationFromEntity[relativeTarget.Target].Value;
				float acceleration, walkSpeed;
				int   direction;
				
				if (UnitTargetControlFromEntity.Exists(owner.Target))
				{
					direction = UnitDirectionFromEntity[owner.Target].Value;

					// a different acceleration (not using the unit weight)
					acceleration = marchAbility.AccelerationFactor;
					acceleration = math.min(acceleration * DeltaTime, 1);

					marchAbility.Delta += DeltaTime;

					walkSpeed      =  unitPlayState.MovementSpeed;
					targetPosition += walkSpeed * direction * (marchAbility.Delta > 0.5f ? 1 : math.lerp(4, 1, marchAbility.Delta + 0.5f)) * acceleration;
					
					TranslationFromEntity[relativeTarget.Target] = new Translation {Value = targetPosition};
				}

				var velocity = VelocityFromEntity[owner.Target];

				// to not make tanks op, we need to get the weight from entity and use it as an acceleration factor
				acceleration = math.clamp(math.rcp(unitPlayState.Weight), 0, 1) * marchAbility.AccelerationFactor * 50;
				acceleration = math.min(acceleration * DeltaTime, 1);

				walkSpeed = unitPlayState.MovementSpeed;
				direction = System.Math.Sign(targetPosition.x + targetOffset.Value - TranslationFromEntity[owner.Target].Value.x);

				velocity.Value.x                 = math.lerp(velocity.Value.x, walkSpeed * direction, acceleration);
				VelocityFromEntity[owner.Target] = velocity;

				var controllerState = UnitControllerStateFromEntity[owner.Target];
				controllerState.ControlOverVelocity.x       = true;
				UnitControllerStateFromEntity[owner.Target] = controllerState;
			}
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			if (!IsServer)
				return inputDeps;

			return new JobProcess
			{
				DeltaTime                     = World.GetExistingSystem<ServerSimulationSystemGroup>().UpdateDeltaTime,
				UnitTargetControlFromEntity   = GetComponentDataFromEntity<UnitTargetControlTag>(true),
				UnitDirectionFromEntity = GetComponentDataFromEntity<UnitDirection>(true),
				UnitPlayStateFromEntity       = GetComponentDataFromEntity<UnitPlayState>(true),
				TranslationFromEntity         = GetComponentDataFromEntity<Translation>(),
				GroundStateFromEntity         = GetComponentDataFromEntity<GroundState>(true),
				TargetOffsetFromEntity        = GetComponentDataFromEntity<UnitTargetOffset>(true),
				UnitControllerStateFromEntity = GetComponentDataFromEntity<UnitControllerState>(),
				VelocityFromEntity            = GetComponentDataFromEntity<Velocity>()
			}.Schedule(this, inputDeps);
		}
	}

	public class MarchAbilityProvider : BaseProviderBatch<MarchAbilityProvider.Create>
	{
		public struct Create
		{
			public Entity Owner;
			public Entity Command;
			public float  AccelerationFactor;
		}

		public override void GetComponents(out ComponentType[] entityComponents)
		{
			entityComponents = new ComponentType[]
			{
				typeof(ActionDescription),
				typeof(RhythmAbilityState),
				typeof(MarchAbility),
				typeof(Owner),
				typeof(DestroyChainReaction),
				typeof(PlayEntityTag),
			};
		}

		public override void SetEntityData(Entity entity, Create data)
		{
			EntityManager.ReplaceOwnerData(entity, data.Owner);
			EntityManager.SetComponentData(entity, new RhythmAbilityState {Command      = data.Command});
			EntityManager.SetComponentData(entity, new MarchAbility {AccelerationFactor = data.AccelerationFactor});
			EntityManager.SetComponentData(entity, new Owner {Target                    = data.Owner});
			EntityManager.SetComponentData(entity, new DestroyChainReaction(data.Owner));
		}
	}
}