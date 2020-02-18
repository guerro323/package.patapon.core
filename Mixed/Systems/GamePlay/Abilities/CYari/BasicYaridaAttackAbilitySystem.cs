using System;
using package.stormiumteam.shared.ecs;
using Patapon.Mixed.GamePlay;
using Patapon.Mixed.GamePlay.Abilities;
using Patapon.Mixed.GamePlay.Abilities.CYari;
using Patapon.Mixed.GamePlay.Physics;
using Patapon.Mixed.Units;
using StormiumTeam.GameBase;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Systems.GamePlay.CYari
{
	public class BasicYaridaAttackAbilitySystem : BaseAbilitySystem
	{
		private SpearProjectile.Provider m_ProjectileProvider;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_ProjectileProvider = World.GetOrCreateSystem<SpearProjectile.Provider>();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			var tick                   = ServerTick;
			var impl                   = new BasicUnitAbilityImplementation(this);
			var seekingStateFromEntity = GetComponentDataFromEntity<UnitEnemySeekingState>(true);


			var queueWriter = m_ProjectileProvider.GetEntityDelayedStream()
			                                      .AsParallelWriter();

			var rand = new Random((uint) Environment.TickCount);
			Entities
				.ForEach((Entity          entity, int                 nativeThreadIndex, ref BasicYaridaAttackAbility ability, ref AbilityControlVelocity controlTarget,
				          in AbilityState state,  in AbilityEngineSet engineSet,         in  Owner                    owner) =>
				{
					if (!impl.CanExecuteAbility(owner.Target))
						return;

					var seekingState = seekingStateFromEntity[owner.Target];
					var playState    = impl.UnitPlayStateFromEntity[owner.Target];
					var unitPosition = impl.TranslationFromEntity[owner.Target].Value;
					var direction    = impl.UnitDirectionFromEntity[owner.Target].Value;

					var velocityUpdater   = impl.VelocityFromEntity.GetUpdater(owner.Target).Out(out var velocity);
					var controllerUpdater = impl.ControllerFromEntity.GetUpdater(owner.Target).Out(out var controller);

					var attackStartTick = UTick.CopyDelta(tick, ability.AttackStartTick);

					ability.NextAttackDelay -= tick.Delta;

					var throwOffset = new float3 {x = direction, y = 1.75f};
					var gravity     = new float3 {y = -22};
					if (ability.AttackStartTick > 0)
					{
						controller.ControlOverVelocity.x = true;

						if (tick >= UTick.AddMs(attackStartTick, BasicYaridaAttackAbility.DelayThrowMs) && !ability.HasThrown)
						{
							var accuracy = AbilityUtility.CompileStat(engineSet.Combo, 0.2f, 1, 2.5, 1.5);
							queueWriter.Enqueue(new SpearProjectile.Create
							{
								Owner       = owner.Target,
								Position    = unitPosition + throwOffset,
								Velocity    = {x = ability.ThrowSpeed * direction, y = ability.ThrowHeight + accuracy * rand.NextFloat()},
								StartDamage = playState.Attack,
								Gravity     = gravity
							});

							ability.HasThrown = true;
						}

						// stop moving
						if (ability.HasThrown)
							velocity.Value.x = math.lerp(velocity.Value.x, 0, playState.GetAcceleration() * 25 * tick.Delta);

						// stop attacking once the animation is done
						if (tick >= UTick.AddMs(attackStartTick, 500))
							ability.AttackStartTick = 0;
					}
					else if ((state.Phase & EAbilityPhase.Chaining) != 0)
					{
						controller.ControlOverVelocity.x = true;
						velocity.Value.x                 = math.lerp(velocity.Value.x, 0, playState.GetAcceleration() * 50 * tick.Delta);
					}

					if ((state.Phase & EAbilityPhase.Active) != 0 && seekingState.Enemy != default)
					{
						var targetPosition     = impl.LocalToWorldFromEntity[seekingState.Enemy].Position;
						var throwDeltaPosition = PredictTrajectory.Simple(throwOffset, new float3 {x = ability.ThrowSpeed * direction, y = ability.ThrowHeight}, gravity);
						targetPosition.x -= throwDeltaPosition.x;

						controlTarget.IsActive       = true;
						controlTarget.TargetPosition = targetPosition;

						var distanceMercy = 4f;
						if (math.abs(targetPosition.x - unitPosition.x) < distanceMercy && ability.NextAttackDelay <= 0 && ability.AttackStartTick <= 0)
						{
							ability.NextAttackDelay = playState.AttackSpeed;
							ability.AttackStartTick = tick.AsUInt;
							ability.HasThrown       = false;

							controlTarget.Acceleration = 5;
						}
						else if (tick >= UTick.AddMs(attackStartTick, BasicYaridaAttackAbility.DelayThrowMs))
							controlTarget.Acceleration = 50;
					}

					velocityUpdater.CompareAndUpdate(velocity);
					controllerUpdater.CompareAndUpdate(controller);
				})
				.WithReadOnly(seekingStateFromEntity)
				.Run();

			return default;
		}
	}
}