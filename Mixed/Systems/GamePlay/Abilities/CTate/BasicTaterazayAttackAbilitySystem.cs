using package.stormiumteam.shared.ecs;
using Patapon.Mixed.GamePlay;
using Patapon.Mixed.GamePlay.Abilities.CTate;
using Patapon.Mixed.GamePlay.RhythmEngine;
using Patapon.Mixed.Units;
using Patapon.Mixed.Utilities;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using SphereCollider = Unity.Physics.SphereCollider;

namespace Systems.GamePlay.CTate
{
	[UpdateInGroup(typeof(RhythmAbilitySystemGroup))]
	[UpdateInWorld(UpdateInWorld.TargetWorld.Server)]
	public unsafe class BasicTaterazayAttackAbilitySystem : JobGameBaseSystem
	{
		private JobPhysicsQuery            m_HitQuery;
		private TargetDamageEvent.Provider m_DamageEventProvider;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_HitQuery = new JobPhysicsQuery(() => SphereCollider.Create(new SphereGeometry
			{
				Center = float3.zero,
				Radius = 2f
			}));
			m_DamageEventProvider = World.GetOrCreateSystem<TargetDamageEvent.Provider>();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			m_HitQuery.Dispose();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			var tick                      = GetTick(true);
			var enemiesFromTeam           = GetBufferFromEntity<TeamEnemies>(true);
			var physicsColliderFromEntity = GetComponentDataFromEntity<PhysicsCollider>(true);
			var impl                      = new BasicUnitAbilityImplementation(this);
			var seekEnemies               = new SeekEnemies(this);

			var colliderQuery            = m_HitQuery;
			var relativeTeamFromEntity   = GetComponentDataFromEntity<Relative<TeamDescription>>(true);
			var relativeTargetFromEntity = GetComponentDataFromEntity<Relative<UnitTargetDescription>>(true);

			var damageEvArchetype = m_DamageEventProvider.EntityArchetype;
			var ecb = m_DamageEventProvider.CreateEntityCommandBuffer()
			                               .ToConcurrent();

			inputDeps =
				Entities
					.ForEach((Entity entity, int nativeThreadIndex, ref RhythmAbilityState state, ref BasicTaterazayAttackAbility ability, in UnitEnemySeekingState seekingState, in Owner owner) =>
					{
						const float attackRange = 3f;

						Relative<TeamDescription> relativeTeam;
						if (!relativeTeamFromEntity.TryGet(entity, out relativeTeam))
							if (!relativeTeamFromEntity.TryGet(owner.Target, out relativeTeam))
								return;

						Relative<UnitTargetDescription> relativeTarget;
						if (!relativeTargetFromEntity.TryGet(entity, out relativeTarget))
							if (!relativeTargetFromEntity.TryGet(owner.Target, out relativeTarget))
								return;

						var teamEnemies  = enemiesFromTeam[relativeTeam.Target];
						var statistics   = impl.UnitSettingsFromEntity[owner.Target];
						var playState    = impl.UnitPlayStateFromEntity[owner.Target];
						var unitPosition = impl.TranslationFromEntity[owner.Target].Value;

						var velocityUpdater   = impl.VelocityFromEntity.GetUpdater(owner.Target).Out(out var velocity);
						var controllerUpdater = impl.ControllerFromEntity.GetUpdater(owner.Target).Out(out var controller);
						if (state.IsStillChaining && !state.IsActive && seekingState.Enemy != default)
						{
							var acceleration = math.clamp(math.rcp(playState.Weight), 0, 1) * 50;
							acceleration = math.min(acceleration * tick.Delta, 1);

							velocity.Value.x = math.lerp(velocity.Value.x, 0, acceleration);
						}

						if (state.IsStillChaining && seekingState.Enemy != default)
							controller.ControlOverVelocity.x = true;

						var attackStartTick = UTick.CopyDelta(tick, ability.AttackStartTick);

						ability.NextAttackDelay -= tick.Delta;
						if (ability.AttackStartTick > 0)
						{
							// slash!
							if (tick >= UTick.AddMs(attackStartTick, BasicTaterazayAttackAbility.DelaySlashMs) && !ability.HasSlashed)
							{
								ability.HasSlashed = true;

								var   damage      = statistics.Attack;
								float damageFever = damage;
								if (state.Combo.IsFever)
								{
									damageFever *= 1.2f;
									if (state.Combo.Score >= 50)
										damageFever *= 1.2f;

									damage += (int) damageFever - damage;
								}

								var unitDirection = impl.UnitDirectionFromEntity[owner.Target];
								var distanceInput = CreateDistanceFlatInput.ColliderWithOffset(colliderQuery.Ptr, unitPosition.xy, new float2(unitDirection.Value, 1));
								var allEnemies    = new NativeList<Entity>(Allocator.Temp);
								seekEnemies.GetAllEnemies(ref allEnemies, teamEnemies);

								var rigidBodies = new NativeList<RigidBody>(allEnemies.Length, Allocator.Temp);
								for (var ent = 0; ent != allEnemies.Length; ent++)
								{
									var enemy = allEnemies[ent];
									if (seekEnemies.LivableHealthFromEntity.TryGet(enemy, out var enemyHealth) && enemyHealth.IsDead)
										continue;
									if (!seekEnemies.HitShapeContainerFromEntity.Exists(enemy))
										continue;

									rigidBodies.Clear();

									var hitShapes = seekEnemies.HitShapeContainerFromEntity[enemy];
									CreateRigidBody.Execute(ref rigidBodies, hitShapes.AsNativeArray(),
										enemy,
										impl.LocalToWorldFromEntity, impl.TranslationFromEntity, physicsColliderFromEntity);
									for (var i = 0; i != rigidBodies.Length; i++)
									{
										var rb = rigidBodies[i];
										var cc = new CustomCollide(rb);
										cc.WorldFromMotion.pos.z = 0;

										var collection = new CustomCollideCollection(ref cc);
										Debug.DrawRay(distanceInput.Transform.pos, Vector3.up, Color.red, 2f);
										if (!collection.CalculateDistance(distanceInput, out var closestHit))
											continue;

										// dat pun 
										var evEnt = ecb.CreateEntity(nativeThreadIndex, damageEvArchetype);
										ecb.SetComponent(nativeThreadIndex, evEnt, new TargetDamageEvent
										{
											Origin      = owner.Target,
											Destination = enemy,
											Damage      = -damage,
										});
										ecb.AddComponent(nativeThreadIndex, evEnt, new Translation {Value = closestHit.Position});
									}
								}
							}

							// stop moving
							if (ability.HasSlashed)
							{
								var acceleration = math.clamp(math.rcp(playState.Weight), 0, 1) * 150;
								acceleration = math.min(acceleration * tick.Delta, 1);

								velocity.Value.x = math.lerp(velocity.Value.x, 0, acceleration);
							}

							// stop attacking once the animation is done
							if (tick >= UTick.AddMs(attackStartTick, 500))
								ability.AttackStartTick = 0;
						}

						velocityUpdater.CompareAndUpdate(velocity);
						controllerUpdater.CompareAndUpdate(controller);

						// if inactive or no enemy are present, continue...
						if (!state.IsActive || seekingState.Enemy == default)
							return;

						if (state.Combo.IsFever)
						{
							playState.MovementAttackSpeed *= 1.8f;
							if (state.Combo.Score >= 50)
								playState.MovementAttackSpeed *= 1.2f;
						}

						// if all conditions are ok, start attacking.
						var targetPosition = impl.LocalToWorldFromEntity[seekingState.Enemy].Position;
						var enemyDistance  = math.distance(unitPosition.x, targetPosition.x);
						if (enemyDistance <= attackRange && ability.NextAttackDelay <= 0.0f && ability.AttackStartTick <= 0)
						{
							var atkSpeed = playState.AttackSpeed;
							if (state.Combo.IsFever && state.Combo.Score >= 50)
							{
								atkSpeed *= 0.75f;
							}

							ability.NextAttackDelay = atkSpeed;
							ability.AttackStartTick = (uint) tick.Value;
							ability.HasSlashed      = false;

							Debug.Log("Start..." + tick.Value);
						}
						else if (tick >= UTick.AddMs(attackStartTick, BasicTaterazayAttackAbility.DelaySlashMs))
						{
							controller.ControlOverVelocity.x = true;

							var acceleration = math.clamp(math.rcp(playState.Weight), 0, 1) * 50;
							acceleration = math.min(acceleration * tick.Delta, 1);

							var direction = math.sign(targetPosition.x - unitPosition.x);
							velocity.Value.x = math.lerp(velocity.Value.x, playState.MovementAttackSpeed * direction, acceleration);
						}

						velocityUpdater.CompareAndUpdate(velocity);
						controllerUpdater.CompareAndUpdate(controller);
					})
					.WithReadOnly(enemiesFromTeam)
					.WithReadOnly(physicsColliderFromEntity)
					.WithReadOnly(relativeTargetFromEntity)
					.WithReadOnly(relativeTeamFromEntity)
					.Schedule(inputDeps);

			m_DamageEventProvider.AddJobHandleForProducer(inputDeps);

			return inputDeps;
		}
	}
}