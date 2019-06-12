using System;
using System.IO;
using package.patapon.core;
using package.patapon.def.Data;
using StormiumTeam.GameBase.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Patapon4TLB.Default
{
	[UpdateInGroup(typeof(ClientPresentationSystemGroup))]
	public class RhythmEngineClientInputSystem : JobSyncInputSystem
	{
		[BurstCompile]
		[RequireComponentTag(typeof(RhythmEngineSimulateTag))]
		private struct SendLocalEventToEngine : IJobForEachWithEntity<RhythmEngineSettings, RhythmEngineProcess, RhythmEngineState>
		{
			public NativeArray<RhythmRpcPressureFromClient> PressureEventSingleArray;

			[NativeDisableParallelForRestriction]
			public BufferFromEntity<RhythmEngineCurrentCommand> CommandSequenceFromEntity;

			[NativeDisableParallelForRestriction]
			public NativeList<FlowRhythmPressureEventProvider.Create> CreatePressureEventList;

			public unsafe void Execute(Entity entity, int _, ref RhythmEngineSettings settings, ref RhythmEngineProcess process, ref RhythmEngineState state)
			{
				var     commandSequence = CommandSequenceFromEntity[entity];
				ref var pressureEvent   = ref UnsafeUtilityEx.ArrayElementAsRef<RhythmRpcPressureFromClient>(PressureEventSingleArray.GetUnsafePtr(), 0);

				pressureEvent.Beat = process.Beat;

				var pressureData = new RhythmPressureData(pressureEvent.Key, settings.BeatInterval, process.Time, process.Beat);
				commandSequence.Add(new RhythmEngineCurrentCommand
				{
					Data = pressureData
				});

				CreatePressureEventList.Add(new FlowRhythmPressureEventProvider.Create
				{
					Ev = new PressureEvent
					{
						Engine        = entity,
						Key           = pressureEvent.Key,
						CorrectedBeat = pressureData.CorrectedBeat,
						OriginalBeat  = pressureData.OriginalBeat,
						Score         = pressureData.Score
					}
				});
			}
		}

		[BurstCompile]
		private struct SendRpcEvent : IJobForEachWithEntity<NetworkIdComponent>
		{
			[DeallocateOnJobCompletion] public NativeArray<RhythmRpcPressureFromClient> PressureEventSingleArray;
			public RpcQueue<RhythmRpcPressureFromClient> RpcQueue;

			[NativeDisableParallelForRestriction]
			public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> OutgoingDataBufferFromEntity;

			public void Execute(Entity entity, int index, ref NetworkIdComponent networkIdComponent)
			{
				RpcQueue.Schedule(OutgoingDataBufferFromEntity[entity], PressureEventSingleArray[0]);
			}
		}

		public const string AssetFileName = "input_pressurekeys.inputactions";
		public const int    ActionLength  = 4;

		public InputAction[] m_Actions;
		
		private FlowRhythmPressureEventProvider m_PressureEventProvider;

		protected override void OnCreate()
		{
			base.OnCreate();

			if (RemoveFromServerWorld())
				return;

			m_PressureEventProvider = World.GetOrCreateSystem<FlowRhythmPressureEventProvider>();

			var path = Application.streamingAssetsPath + "/" + AssetFileName;
			if (File.Exists(path))
			{
				var asset = ScriptableObject.CreateInstance<InputActionAsset>();
				asset.LoadFromJson(File.ReadAllText(path));

				Refresh(asset);
			}
			else
			{
				Debug.LogError($"The file '{path}' don't exist.");
			}
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			// this will happen if the client didn't pressed any keys (or if it's not a client at all)
			if (InputEvents.Count < 0)
				return inputDeps;
			
			// not enabled
			if (!World.GetExistingSystem<ClientPresentationSystemGroup>().Enabled)
				return inputDeps;
			
			inputDeps.Complete();
			EntityManager.CompleteAllJobs();

			var pressureEvent = new RhythmRpcPressureFromClient {Key = -1};
			foreach (var ev in InputEvents)
			{
				pressureEvent = new RhythmRpcPressureFromClient
				{
					Beat = -1,                                     // the beat will be assigned SendLocalEventToEngine job
					Key  = Array.IndexOf(m_Actions, ev.action) + 1 // match RhythmKeys
				};
			}

			InputEvents.Clear();

			if (pressureEvent.Key < 0)
				return inputDeps;

			var rpcQueue = World.GetExistingSystem<P4ExperimentRpcSystem>().GetRpcQueue<RhythmRpcPressureFromClient>();
			var pressureEventSingleArray = new NativeArray<RhythmRpcPressureFromClient>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
			{
				[0] = pressureEvent
			};

			inputDeps = new SendLocalEventToEngine
			{
				PressureEventSingleArray  = pressureEventSingleArray,
				CommandSequenceFromEntity = GetBufferFromEntity<RhythmEngineCurrentCommand>(),
				
				CreatePressureEventList = m_PressureEventProvider.GetEntityDelayedList()
			}.Schedule(this, inputDeps);
			
			m_PressureEventProvider.AddJobHandleForProducer(inputDeps);
			
			inputDeps = new SendRpcEvent
			{
				PressureEventSingleArray     = pressureEventSingleArray,
				RpcQueue                     = rpcQueue,
				OutgoingDataBufferFromEntity = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>()
			}.Schedule(this, inputDeps);
			
			inputDeps.Complete();

			return inputDeps;
		}

		protected override void OnAssetRefresh()
		{
			var actionMap = Asset.GetActionMap("Pressures");
			if (actionMap == null)
			{
				Debug.LogError("Remaking the actionmap...");

				// todo: remake the action map (and maybe save it to the file?
				return;
			}

			m_Actions = new InputAction[ActionLength];
			for (var i = 0; i != ActionLength; i++)
			{
				// we add +1 so it can match RhythmKeys constants
				var action = actionMap.GetAction("Pressure" + (i + 1));
				m_Actions[i] = action;

				action.performed += InputActionEvent;
			}
		}
	}
}