using System;
using package.patapon.core;
using Runtime.EcsComponents;
using StormiumTeam.GameBase;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

namespace Patapon4TLB.Default
{
	public struct RhythmRpcNewClientCommand : IRpcCommand
	{
		public bool                                           IsValid;
		public NativeArray<RhythmEngineClientRequestedCommand> ResultBuffer;

		public void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
		{
			if (!IsValid)
			{
				Debug.Log($"Invalid '{nameof(ResultBuffer)}' from client! (c={connection})");
				return;
			}

			var ent = commandBuffer.CreateEntity(jobIndex);
			commandBuffer.AddComponent(jobIndex, ent, new RhythmExecuteCommand
			{
				Connection = connection
			});
			
			var b = commandBuffer.AddBuffer<RhythmEngineClientRequestedCommand>(jobIndex, ent);
			b.CopyFrom(ResultBuffer);
			
			ResultBuffer.Dispose();
		}

		public void Serialize(DataStreamWriter writer)
		{
			if (!ResultBuffer.IsCreated)
			{
				writer.Write((byte) 0); // validity
				Debug.LogError($"We tried to send an invalid '{nameof(ResultBuffer)}'!");
			}

			writer.Write((byte) 1);            // validity
			writer.Write(ResultBuffer.Length); // count
			for (var com = 0; com != ResultBuffer.Length; com++)
			{
				writer.Write(ResultBuffer[com].Data.Score);
				writer.Write(ResultBuffer[com].Data.KeyId);
				writer.Write(ResultBuffer[com].Data.OriginalBeat);
				writer.Write(ResultBuffer[com].Data.CorrectedBeat);
			}
		}

		public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			IsValid = reader.ReadByte(ref ctx) == 1;
			if (!IsValid)
				return;

			var count = reader.ReadInt(ref ctx);
			ResultBuffer = new NativeArray<RhythmEngineClientRequestedCommand>(count, Allocator.Temp);
			for (var com = 0; com != count; com++)
			{
				var temp = default(FlowRhythmPressureData);
				temp.Score         = reader.ReadFloat(ref ctx);
				temp.KeyId         = reader.ReadInt(ref ctx);
				temp.OriginalBeat  = reader.ReadInt(ref ctx);
				temp.CorrectedBeat = reader.ReadInt(ref ctx);

				ResultBuffer[com] = new RhythmEngineClientRequestedCommand {Data = temp};
			}
		}
	}

	public struct RhythmExecuteCommand : IComponentData
	{
		public Entity Connection;
	}

	[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
	public class RhythmExecuteCommandSystem : JobComponentSystem
	{
		private struct Job : IJobForEachWithEntity<NetworkOwner, RhythmEngineState>
		{
			/// <summary>
			/// If true, players will be allowed to directly execute a command that may not be valid to the current one in the server
			/// This should only be enabled if you can trust the players.
			/// </summary>
			public bool AllowCommandChange; // default value: false

			[DeallocateOnJobCompletion]
			public NativeArray<RhythmExecuteCommand> ExecuteCommandArray;

			[DeallocateOnJobCompletion]
			public NativeArray<Entity> ExecuteCommandEntities; // we need to get the buffer from them

			[NativeDisableParallelForRestriction]
			public BufferFromEntity<RhythmEngineClientPredictedCommand> PredictedCommandFromEntity;

			[NativeDisableParallelForRestriction]
			public BufferFromEntity<RhythmEngineClientRequestedCommand> RequestedCommandFromEntity;

			[NativeDisableParallelForRestriction]
			public BufferFromEntity<RhythmEngineCurrentCommand> CurrentCommandFromEntity;

			public void Execute(Entity entity, int index, ref NetworkOwner netOwner, ref RhythmEngineState state)
			{
				var executeCommand       = default(RhythmExecuteCommand);
				var executeCommandEntity = default(Entity);
				for (var com = 0; executeCommand.Connection == default && com != ExecuteCommandArray.Length; com++)
				{
					if (ExecuteCommandArray[com].Connection == netOwner.Value)
					{
						executeCommand       = ExecuteCommandArray[com];
						executeCommandEntity = ExecuteCommandEntities[com];
					}
				}

				if (executeCommand.Connection == default)
					return;
				
				Debug.Log("Received command request!");

				var currentCommand   = CurrentCommandFromEntity[entity].Reinterpret<FlowRhythmPressureData>();
				var predictedCommand = PredictedCommandFromEntity[entity].Reinterpret<FlowRhythmPressureData>();
				var requestedCommand = RequestedCommandFromEntity[executeCommandEntity].Reinterpret<FlowRhythmPressureData>();

				if (AllowCommandChange)
				{
					currentCommand.CopyFrom(requestedCommand);
				}
				else
				{
					throw new NotImplementedException("Prediction for commands is not yet implemented");
				}

				predictedCommand.Clear();
				requestedCommand.Clear();

				state.ApplyCommandNextBeat = true;
			}
		}

		private EntityQuery m_ExecuteCommandQuery;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_ExecuteCommandQuery = GetEntityQuery(typeof(RhythmExecuteCommand));
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			m_ExecuteCommandQuery.AddDependency(inputDeps);
			
			inputDeps = new Job
			{
				AllowCommandChange = true,

				ExecuteCommandArray    = m_ExecuteCommandQuery.ToComponentDataArray<RhythmExecuteCommand>(Allocator.TempJob, out var queryHandleData),
				ExecuteCommandEntities = m_ExecuteCommandQuery.ToEntityArray(Allocator.TempJob, out var queryHandleEntities),

				PredictedCommandFromEntity = GetBufferFromEntity<RhythmEngineClientPredictedCommand>(),
				RequestedCommandFromEntity = GetBufferFromEntity<RhythmEngineClientRequestedCommand>(),
				CurrentCommandFromEntity   = GetBufferFromEntity<RhythmEngineCurrentCommand>()
			}.Schedule(this, JobHandle.CombineDependencies(inputDeps, queryHandleData, queryHandleEntities));

			if (m_ExecuteCommandQuery.CalculateLength() > 0)
			{
				inputDeps.Complete();
				EntityManager.DestroyEntity(m_ExecuteCommandQuery);
			}
			
			return inputDeps;
		}
	}
}