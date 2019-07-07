using System;
using System.Linq;
using package.patapon.core;
using package.stormiumteam.shared;
using Runtime.Systems;
using StormiumTeam.GameBase;
using StormiumTeam.Networking.Utilities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Patapon4TLB.Default
{
	public struct JumpAbilitySnapshotData : ISnapshotData<JumpAbilitySnapshotData>
	{
		public uint Tick { get; set; }

		public bool IsActive;
		public bool IsStillChaining;
		public bool ClientPredictState;

		public int  CommandId;
		public uint OwnerGhostId;

		public void PredictDelta(uint tick, ref JumpAbilitySnapshotData baseline1, ref JumpAbilitySnapshotData baseline2)
		{
			throw new NotImplementedException();
		}

		public void Serialize(ref JumpAbilitySnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
		{
			byte mask = 0, pos = 0;
			MainBit.SetBitAt(ref mask, pos++, IsActive);
			MainBit.SetBitAt(ref mask, pos++, IsStillChaining);
			MainBit.SetBitAt(ref mask, pos++, ClientPredictState);

			writer.WritePackedUInt(mask, compressionModel);
			writer.WritePackedIntDelta(CommandId, baseline.CommandId, compressionModel);
			writer.WritePackedUIntDelta(OwnerGhostId, baseline.OwnerGhostId, compressionModel);
		}

		public void Deserialize(uint tick, ref JumpAbilitySnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
		{
			Tick = tick;

			byte pos  = 0;
			var  mask = (byte) reader.ReadPackedUInt(ref ctx, compressionModel);
			{
				IsActive           = MainBit.GetBitAt(mask, pos++) == 1;
				IsStillChaining    = MainBit.GetBitAt(mask, pos++) == 1;
				ClientPredictState = MainBit.GetBitAt(mask, pos++) == 1;
			}
			CommandId    = reader.ReadPackedIntDelta(ref ctx, baseline.CommandId, compressionModel);
			OwnerGhostId = reader.ReadPackedUIntDelta(ref ctx, baseline.OwnerGhostId, compressionModel);
		}

		public void Interpolate(ref JumpAbilitySnapshotData target, float factor)
		{
			IsActive           = target.IsActive;
			IsStillChaining    = target.IsStillChaining;
			ClientPredictState = target.ClientPredictState;

			CommandId    = target.CommandId;
			OwnerGhostId = target.OwnerGhostId;
		}
	}

	public struct JumpAbilityGhostSerializer : IGhostSerializer<JumpAbilitySnapshotData>
	{
		public int SnapshotSize => UnsafeUtility.SizeOf<JumpAbilitySnapshotData>();

		public int CalculateImportance(ArchetypeChunk chunk)
		{
			return 10;
		}

		public bool WantsPredictionDelta => false;

		[NativeDisableContainerSafetyRestriction]
		public ComponentDataFromEntity<GhostSystemStateComponent> GhostStateFromEntity;

		[NativeDisableContainerSafetyRestriction]
		public ComponentDataFromEntity<RhythmCommandId> CommandIdFromEntity;

		public void BeginSerialize(ComponentSystemBase system)
		{
			system.GetGhostComponentType(out GhostRhythmAbilityStateType);
			system.GetGhostComponentType(out GhostJumpAbilityType);
			system.GetGhostComponentType(out GhostOwnerType);

			GhostStateFromEntity = system.GetComponentDataFromEntity<GhostSystemStateComponent>();
			CommandIdFromEntity  = system.GetComponentDataFromEntity<RhythmCommandId>();
		}

		public GhostComponentType<RhythmAbilityState> GhostRhythmAbilityStateType;
		public GhostComponentType<JumpAbility>        GhostJumpAbilityType;
		public GhostComponentType<Owner>              GhostOwnerType;

		public bool CanSerialize(EntityArchetype arch)
		{
			var comps   = arch.GetComponentTypes();
			var matches = 0;
			for (var i = 0; i != comps.Length; i++)
			{
				if (comps[i] == GhostRhythmAbilityStateType) matches++;
				if (comps[i] == GhostJumpAbilityType) matches++;
				if (comps[i] == GhostOwnerType) matches++;
			}

			return matches == 3;
		}

		public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref JumpAbilitySnapshotData snapshot)
		{
			snapshot.Tick = tick;

			snapshot.ClientPredictState = true; // how should we manage that? it should be the default value, right?

			var rhythmAbilityState = chunk.GetNativeArray(GhostRhythmAbilityStateType.Archetype)[ent];
			snapshot.IsActive        = rhythmAbilityState.IsActive;
			snapshot.IsStillChaining = rhythmAbilityState.IsStillChaining;
			snapshot.CommandId       = rhythmAbilityState.Command == default ? 0 : CommandIdFromEntity[rhythmAbilityState.Command].Value;

			var owner = chunk.GetNativeArray(GhostOwnerType.Archetype)[ent];
			snapshot.OwnerGhostId = GetGhostId(owner.Target);
		}

		private uint GetGhostId(Entity target)
		{
			if (target == default || !GhostStateFromEntity.Exists(target))
				return 0;
			return (uint) GhostStateFromEntity[target].ghostId;
		}
	}

	public class JumpAbilityGhostSpawnSystem : DefaultGhostSpawnSystem<JumpAbilitySnapshotData>
	{
		protected override EntityArchetype GetGhostArchetype()
		{
			World.GetOrCreateSystem<JumpAbilityProvider>().GetComponents(out var baseArchetype);

			return EntityManager.CreateArchetype(baseArchetype.Union(new ComponentType[]
			{
				typeof(JumpAbilitySnapshotData),

				typeof(ReplicatedEntityComponent)
			}).ToArray());
		}

		protected override EntityArchetype GetPredictedGhostArchetype()
		{
			return GetGhostArchetype();
		}
	}

	[UpdateInGroup(typeof(UpdateGhostSystemGroup))]
	public class JumpAbilityGhostUpdateSystem : JobComponentSystem
	{
		private struct Job : IJobForEachWithEntity<RhythmAbilityState, JumpAbility, Owner>
		{
			[ReadOnly] public uint                                      TargetTick;
			[ReadOnly] public BufferFromEntity<JumpAbilitySnapshotData> SnapshotDataFromEntity;
			[ReadOnly] public NativeHashMap<int, Entity>                CommandIdToEntity;
			[ReadOnly] public NativeHashMap<int, Entity>                GhostEntityMap;

			public RhythmEngineDataGroup RhythmEngineDataGroup;

			[ReadOnly] public ComponentDataFromEntity<Relative<RhythmEngineDescription>> RelativeRhythmEngineFromEntity;

			public void Execute(Entity entity, int index, ref RhythmAbilityState state, ref JumpAbility jumpAbility, ref Owner owner)
			{
				SnapshotDataFromEntity[entity].GetDataAtTick(TargetTick, out var snapshot);

				var predict = snapshot.ClientPredictState && RelativeRhythmEngineFromEntity.Exists(owner.Target);
				if (predict)
				{
					var rhythmEngine = RelativeRhythmEngineFromEntity[owner.Target].Target;
					if (rhythmEngine == default)
						predict = false;
					else
					{
						var result = RhythmEngineDataGroup.GetResult(rhythmEngine);

						state.Calculate(result.CurrentCommand, result.CommandState, result.ComboState, result.EngineProcess);
					}
				}

				if (!predict)
				{
					state.IsActive        = snapshot.IsActive;
					state.IsStillChaining = snapshot.IsStillChaining;
				}

				state.IsActive = snapshot.IsActive;
				state.Command  = snapshot.CommandId == 0 ? default : CommandIdToEntity[snapshot.CommandId];
			}
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			var convertGhostMapSystem = World.GetExistingSystem<ConvertGhostEntityMap>();

			return new Job
			{
				TargetTick             = NetworkTimeSystem.interpolateTargetTick,
				SnapshotDataFromEntity = GetBufferFromEntity<JumpAbilitySnapshotData>(),

				CommandIdToEntity = World.GetExistingSystem<RhythmCommandManager>().CommandIdToEntity,
				GhostEntityMap    = convertGhostMapSystem.HashMap,

				RhythmEngineDataGroup          = new RhythmEngineDataGroup(this),
				RelativeRhythmEngineFromEntity = GetComponentDataFromEntity<Relative<RhythmEngineDescription>>(true)
			}.Schedule(this, JobHandle.CombineDependencies(inputDeps, convertGhostMapSystem.dependency));
		}
	}
}