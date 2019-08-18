using package.patapon.core;
using Patapon4TLB.Core;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.Components;
using StormiumTeam.GameBase.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using SphereCollider = Unity.Physics.SphereCollider;

namespace Patapon4TLB.Default.Attack
{ 
	public struct BasicYaridaAttackAbility : IComponentData
	{
		public const int DelaySlashMs = 300;

		public bool HasThrown;

		public uint  AttackStartTick;
		public float NextAttackDelay;

		[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
		public class Process : JobGameBaseSystem
		{
			private struct Job : IJobForEach<RhythmAbilityState, BasicYaridaAttackAbility, Relative<TeamDescription>, Relative<UnitTargetDescription>, Owner>
			{
				public UTick Tick;
				
				[ReadOnly] public ComponentDataFromEntity<UnitStatistics> UnitSettingsFromEntity;
				[ReadOnly] public ComponentDataFromEntity<UnitPlayState>  UnitPlayStateFromEntity;

				public ComponentDataFromEntity<Translation>         TranslationFromEntity;
				public ComponentDataFromEntity<UnitControllerState> ControllerFromEntity;
				public ComponentDataFromEntity<Velocity>            VelocityFromEntity;
				
				[ReadOnly] public BufferFromEntity<TeamEnemies>            EnemiesFromTeam;

				public SeekEnemies     SeekEnemies;
				
				public void Execute(ref            RhythmAbilityState              state, ref BasicYaridaAttackAbility ability,
				                    [ReadOnly] ref Relative<TeamDescription>       teamRelative,
				                    [ReadOnly] ref Relative<UnitTargetDescription> targetRelative,
				                    [ReadOnly] ref Owner                           owner)
				{
					const float attackRange = 3f;

					var teamEnemies = EnemiesFromTeam[teamRelative.Target];
					var statistics  = UnitSettingsFromEntity[owner.Target];
					var playState   = UnitPlayStateFromEntity[owner.Target];

					SeekEnemies.Execute
					(
						TranslationFromEntity[targetRelative.Target].Value, statistics.AttackSeekRange, teamEnemies,
						out var nearestEnemy, out var targetPosition, out var enemyDistance
					);

					if (state.IsStillChaining && !state.IsActive && nearestEnemy != default)
					{
						var velocity     = VelocityFromEntity[owner.Target];
						var acceleration = math.clamp(math.rcp(playState.Weight), 0, 1) * 50;
						acceleration = math.min(acceleration * Tick.Delta, 1);

						velocity.Value.x = math.lerp(velocity.Value.x, 0, acceleration);

						VelocityFromEntity[owner.Target] = velocity;
					}

					if (state.IsStillChaining && nearestEnemy != default)
					{
						var controller = ControllerFromEntity[owner.Target];
						controller.ControlOverVelocity.x   = true;
						ControllerFromEntity[owner.Target] = controller;
					}

					var attackStartTick = UTick.CopyDelta(Tick, ability.AttackStartTick);

					ability.NextAttackDelay -= Tick.Delta;
					if (ability.AttackStartTick > 0)
					{
						if (Tick >= UTick.AddMs(attackStartTick, DelaySlashMs) && !ability.HasThrown)
						{
							ability.HasThrown = true;
						}

						// stop moving
						if (ability.HasThrown)
						{
							var velocity     = VelocityFromEntity[owner.Target];
							var acceleration = math.clamp(math.rcp(playState.Weight), 0, 1) * 200;
							acceleration = math.min(acceleration * Tick.Delta, 1);

							velocity.Value.x = math.lerp(velocity.Value.x, 0, acceleration);

							VelocityFromEntity[owner.Target] = velocity;
						}

						// stop attacking once the animation is done
						if (Tick >= UTick.AddMs(attackStartTick, 800))
							ability.AttackStartTick = 0;
					}

					// if inactive or no enemy are present, continue...
					if (!state.IsActive || nearestEnemy == default)
						return;

					if (state.Combo.IsFever)
					{
						playState.MovementAttackSpeed *= 1.2f;
						if (state.Combo.Score >= 50)
							playState.MovementAttackSpeed *= 1.8f;
					}

					// if all conditions are ok, start attacking.
					enemyDistance = math.distance(TranslationFromEntity[owner.Target].Value.x, targetPosition.x);
					if (enemyDistance <= attackRange && ability.NextAttackDelay <= 0.0f && ability.AttackStartTick <= 0)
					{
						ability.NextAttackDelay = playState.AttackSpeed;
						ability.AttackStartTick = (uint) Tick.Value;
						ability.HasThrown       = false;
					}
					else if (Tick >= UTick.AddMs(attackStartTick, DelaySlashMs))
					{
						var controller = ControllerFromEntity[owner.Target];
						controller.ControlOverVelocity.x = true;

						ControllerFromEntity[owner.Target] = controller;

						var velocity     = VelocityFromEntity[owner.Target];
						var acceleration = math.clamp(math.rcp(playState.Weight), 0, 1) * 50;
						acceleration = math.min(acceleration * Tick.Delta, 1);

						var direction = math.sign(targetPosition.x - TranslationFromEntity[owner.Target].Value.x);
						velocity.Value.x = math.lerp(velocity.Value.x, playState.MovementAttackSpeed * direction, acceleration);

						VelocityFromEntity[owner.Target] = velocity;
					}
				}
			}

			private TargetDamageEvent.Provider m_DamageEventProvider;

			protected override void OnCreate()
			{
				base.OnCreate();

				m_DamageEventProvider = World.GetOrCreateSystem<TargetDamageEvent.Provider>();
			}

			protected override JobHandle OnUpdate(JobHandle inputDeps)
			{
				inputDeps = new Job
				{
					Tick            = World.GetExistingSystem<ServerSimulationSystemGroup>().GetTick(),
					
					EnemiesFromTeam           = GetBufferFromEntity<TeamEnemies>(true),
					UnitSettingsFromEntity  = GetComponentDataFromEntity<UnitStatistics>(true),
					UnitPlayStateFromEntity = GetComponentDataFromEntity<UnitPlayState>(true),
					ControllerFromEntity    = GetComponentDataFromEntity<UnitControllerState>(),
					TranslationFromEntity   = GetComponentDataFromEntity<Translation>(),
					VelocityFromEntity      = GetComponentDataFromEntity<Velocity>(),

					SeekEnemies = new SeekEnemies(this)
				}.ScheduleSingle(this, inputDeps);

				m_DamageEventProvider.AddJobHandleForProducer(inputDeps);

				return inputDeps;
			}
		}

		public struct Create
		{
			public Entity Owner;
			public Entity Command;
		}

		public class Provider : BaseProviderBatch<Create>
		{
			public override void GetComponents(out ComponentType[] entityComponents)
			{
				entityComponents = new ComponentType[]
				{
					typeof(ActionDescription),
					typeof(RhythmAbilityState),
					typeof(BasicYaridaAttackAbility),
					typeof(Owner),
					typeof(DestroyChainReaction),
					typeof(PlayEntityTag),
				};
			}

			public override void SetEntityData(Entity entity, Create data)
			{
				EntityManager.ReplaceOwnerData(entity, data.Owner);
				EntityManager.SetComponentData(entity, new RhythmAbilityState {Command = data.Command});
				EntityManager.SetComponentData(entity, new BasicYaridaAttackAbility { });
				EntityManager.SetComponentData(entity, new Owner {Target = data.Owner});
				EntityManager.SetComponentData(entity, new DestroyChainReaction(data.Owner));
			}
		}
	}
}