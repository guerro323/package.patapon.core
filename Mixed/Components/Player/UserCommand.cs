using package.stormiumteam.shared;
using StormiumTeam.GameBase;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Patapon4TLB.Default.Player
{
	public enum AbilitySelection
	{
		Horizontal = 0,
		Top        = 1,
		Bottom     = 2
	}

	public unsafe struct UserCommand : ICommandData<UserCommand>
	{
		public uint Tick { get; set; }

		public struct RhythmAction
		{
			public byte flags;

			public bool IsActive
			{
				get => MainBit.GetBitAt(flags, 0) == 1;
				set => MainBit.SetBitAt(ref flags, 0, value);
			}

			public bool FrameUpdate
			{
				get => MainBit.GetBitAt(flags, 1) == 1;
				set => MainBit.SetBitAt(ref flags, 1, value);
			}

			public bool WasPressed  => IsActive && FrameUpdate;
			public bool WasReleased => !IsActive && FrameUpdate;
		}

		public const int MaxActionCount = 4;

		private fixed byte m_RhythmActions[sizeof(byte) * 4];

		public float Panning;

		public bool             IsSelectingAbility;
		public AbilitySelection Ability;

		public int  LastActionIndex;
		public uint LastActionFrame;

		public UnsafeAllocationLength<RhythmAction> GetRhythmActions()
		{
			fixed (byte* fx = m_RhythmActions)
			{
				return new UnsafeAllocationLength<RhythmAction>(fx, 4);
			}
		}

		public void ReadFrom(DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
		{
			ReadFrom(reader, ref ctx, default, compressionModel);
		}

		public void WriteTo(DataStreamWriter writer, NetworkCompressionModel compressionModel)
		{
			WriteTo(writer, default, compressionModel);
		}

		public void ReadFrom(DataStreamReader reader, ref DataStreamReader.Context ctx, UserCommand baseline, NetworkCompressionModel compressionModel)
		{
			var baselineActions = baseline.GetRhythmActions();

			for (var i = 0; i < 4; i++)
				m_RhythmActions[i] = (byte) reader.ReadPackedUIntDelta(ref ctx, baseline.m_RhythmActions[i++], compressionModel);

			Panning            = reader.ReadPackedFloat(ref ctx, compressionModel);
			IsSelectingAbility = reader.ReadBitBool(ref ctx);
			Ability            = (AbilitySelection) reader.ReadPackedUIntDelta(ref ctx, (uint) baseline.Ability, compressionModel);

			LastActionIndex = reader.ReadPackedIntDelta(ref ctx, baseline.LastActionIndex, compressionModel);
			LastActionFrame = reader.ReadPackedUIntDelta(ref ctx, baseline.LastActionFrame, compressionModel);
		}

		public void WriteTo(DataStreamWriter writer, UserCommand baseline, NetworkCompressionModel compressionModel)
		{
			var baselineActions = baseline.GetRhythmActions();
			for (var i = 0; i < 4; i++)
				writer.WritePackedUIntDelta(m_RhythmActions[i], baseline.m_RhythmActions[i++], compressionModel);

			writer.WritePackedFloat(Panning, compressionModel);
			writer.WriteBitBool(IsSelectingAbility);
			writer.WritePackedUIntDelta((uint) Ability, (uint) baseline.Ability, compressionModel);

			writer.WritePackedIntDelta(LastActionIndex, baseline.LastActionIndex, compressionModel);
			writer.WritePackedUIntDelta(LastActionFrame, baseline.LastActionFrame, compressionModel);
		}
	}

	[UpdateInGroup(typeof(ClientAndServerInitializationSystemGroup))]
	public class SpawnUserCommand : ComponentSystem
	{
		protected override void OnUpdate()
		{
			EntityManager.AddComponent(Entities.WithNone<UserCommand>().WithAll<GamePlayer>().ToEntityQuery(), typeof(UserCommand));
		}
	}

	[UpdateInGroup(typeof(ServerInitializationSystemGroup))]
	public class SpawnGamePlayerUserCommand : ComponentSystem
	{
		protected override void OnUpdate()
		{
			EntityManager.AddComponent(Entities.WithNone<GamePlayerCommand>().WithAll<GamePlayer>().ToEntityQuery(), typeof(GamePlayerCommand));
		}
	}
}