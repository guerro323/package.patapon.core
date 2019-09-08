using System;
using System.Collections.Generic;
using P4TLB.MasterServer.GamePlay;
using Patapon4TLB.Default;
using Patapon4TLB.Default.Attack;
using StormiumTeam.GameBase;
using Unity.Collections;
using Unity.Entities;
using Revolution.NetCode;
using UnityEngine;

namespace Patapon4TLB.Core
{
	public static class MasterServerAbilities
	{
		private const string InternalFormat = "p4:{0}";

		private static void _c(ComponentSystemBase system, Entity entity, string typeId, IGhostSerializerCollection collection)
		{
			Entity FindCommand(Type type)
			{
				using (var query = system.EntityManager.CreateEntityQuery(type))
				{
					if (query.CalculateEntityCount() == 0)
					{
						Debug.Log("nay " + type);
						return Entity.Null;
					}

					using (var entities = query.ToEntityArray(Allocator.TempJob))
					{
						return entities[0];
					}
				}
			}

			void CreateAbility<TProvider, TActionCreate>(TActionCreate create)
				where TProvider : BaseProviderBatch<TActionCreate>
				where TActionCreate : struct
			{
				using (var entities = new NativeList<Entity>(1, Allocator.TempJob))
				{
					var provider = system.World.GetOrCreateSystem<TProvider>();
					provider.SpawnLocalEntityWithArguments(create, entities);

					// Temporary. For now, we check if the entity can be serialized or not.
					// TODO: In future, all abilities should be able to be serialized.
					bool success;
					try
					{
						collection.FindSerializer(system.EntityManager.GetChunk(entities[0]).Archetype);
						success = true;
					}
					catch
					{
						success = false;
					}

					if (success)
					{
						system.EntityManager.AddComponent(entities[0], typeof(GhostComponent));
						system.EntityManager.AddComponent(entities[0], typeof(ExcludeRelativeSynchronization));
					}
				}
			}

			switch (typeId)
			{
				case string _ when string.IsNullOrEmpty(typeId):
					throw new InvalidOperationException();
				case string _ when typeId == GetInternal("tate/basic_march"):
				case string _ when typeId == GetInternal("basic_march"):
					CreateAbility<MarchAbilityProvider, MarchAbilityProvider.Create>(new MarchAbilityProvider.Create
					{
						Owner              = entity,
						AccelerationFactor = 1,
						Command            = FindCommand(typeof(MarchCommand))
					});
					break;
				case string _ when typeId == GetInternal("basic_backward"):
					CreateAbility<BackwardAbilityProvider, BackwardAbilityProvider.Create>(new BackwardAbilityProvider.Create
					{
						Owner              = entity,
						AccelerationFactor = 1,
						Command            = FindCommand(typeof(BackwardCommand))
					});
					break;
				case string _ when typeId == GetInternal("basic_jump"):
					CreateAbility<JumpAbilityProvider, JumpAbilityProvider.Create>(new JumpAbilityProvider.Create
					{
						Owner              = entity,
						AccelerationFactor = 1,
						Command            = FindCommand(typeof(JumpCommand))
					});
					break;
				case string _ when typeId == GetInternal("basic_retreat"):
					CreateAbility<RetreatAbilityProvider, RetreatAbilityProvider.Create>(new RetreatAbilityProvider.Create
					{
						Owner              = entity,
						AccelerationFactor = 1,
						Command            = FindCommand(typeof(RetreatCommand))
					});
					break;
				case string _ when typeId == GetInternal("tate/basic_attack"):
					CreateAbility<BasicTaterazayAttackAbility.Provider, BasicTaterazayAttackAbility.Create>(new BasicTaterazayAttackAbility.Create
					{
						Owner   = entity,
						Command = FindCommand(typeof(AttackCommand))
					});
					break;
				default:
					Debug.LogError("No ability found with type: " + typeId);
					break;
			}
		}

		public static void Convert(ComponentSystemBase system, Entity entity, DynamicBuffer<UnitDefinedAbilities> abilities)
		{
			var collection = new GhostSerializerCollection();
			collection.BeginSerialize(system);

			var array = abilities.ToNativeArray(Allocator.TempJob);
			foreach (var ab in array)
			{
				_c(system, entity, ab.Type.ToString(), collection);
			}
			array.Dispose();
		}

		public static void Convert(ComponentSystemBase system, Entity entity, List<Ability> abilities)
		{
			var collection = new GhostSerializerCollection();
			collection.BeginSerialize(system);

			foreach (var ab in abilities)
			{
				_c(system, entity, ab.Type, collection);
			}
		}

		public static string GetInternal(string ability)
		{
			return string.Format(InternalFormat, ability);
		}
	}
}